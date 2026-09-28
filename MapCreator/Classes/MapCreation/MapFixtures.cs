//
// MapCreator
// Copyright(C) 2017 Stefan Schäfer <merec@merec.org>
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ImageMagick;
using ImageMagick.Drawing;
using MapCreator.Classes.MapCreation.Fixtures;

namespace MapCreator.Classes.MapCreation
{
	internal class MapFixtures
    {
        private readonly ZoneConfiguration zoneConfiguration;
        private readonly List<WaterConfiguration> rivers;

        private readonly List<DrawableFixture> fixtures;

        private List<DrawableFixture> fixturesUnderWater = new List<DrawableFixture>();
        private List<DrawableFixture> fixturesAboveWater = new List<DrawableFixture>();

        private TerrainHeights terrain;

        #region Settings
        public bool DrawFixtures { get; set; } = true;

        public bool DrawTrees { get; set; } = true;

        public int TreeTransparency { get; set; } = 20;
        #endregion

        public MapFixtures(ZoneConfiguration zoneConfiguration, List<WaterConfiguration> rivers, FixturesLoader loader = null)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.rivers = rivers;

            // Loads CSV files and polygons, then prepares the models
            this.fixtures = (loader ?? new FixturesLoader(zoneConfiguration)).GetDrawableFixtures();
        }

        public void Start()
        {
            if (this.fixtures.Count == 0) return;
            this.zoneConfiguration.Reporter.ProgressStartMarquee("Sorting fixtures ....");

            // Create paths out of the rivers
            var riverPaths = new Dictionary<WaterConfiguration, System.Drawing.Drawing2D.GraphicsPath>();
            foreach (var rConf in this.rivers)
            {
                var riverPath = new System.Drawing.Drawing2D.GraphicsPath();
                var points = rConf.GetCoordinates().Select(c => new System.Drawing.PointF(Convert.ToSingle(c.X * this.zoneConfiguration.MapScale), Convert.ToSingle(c.Y * this.zoneConfiguration.MapScale))).ToArray();
                riverPath.AddPolygon(points);
                riverPaths.Add(rConf, riverPath);
            }

            try
            {
                foreach (var model in this.fixtures)
                {
                    // ignote the model if there are no polygons
                    if(!model.ProcessedPolygons.Any()) continue;

                    // UI options
                    if (!this.DrawTrees && (model.IsTree || model.IsTreeCluster))
                    {
                        continue;
                    }

                    if (!this.DrawFixtures && !(model.IsTree || model.IsTreeCluster))
                    {
                        continue;
                    }

                    if (model.IsTree || model.IsTreeCluster)
                    {
                        model.RendererConf = FixtureRendererConfigurations.GetRendererById("TreeShaded");
                    }

                    var modelCenterX = this.zoneConfiguration.ZoneCoordinateToMapCoordinate(model.FixtureRow.X);
                    var modelCenterY = this.zoneConfiguration.ZoneCoordinateToMapCoordinate(model.FixtureRow.Y);

                    // Check if on river or not
                    var riverHeight = 0;
                    foreach (var river in riverPaths)
                    {
                        if (river.Value.IsVisible(Convert.ToSingle(modelCenterX), Convert.ToSingle(modelCenterY)))
                        {
                            riverHeight = river.Key.Height;
                            break;
                        }
                    }

                    if (riverHeight == 0 || (riverHeight != 0 && model.TopZ > riverHeight))
                    {
                        this.fixturesAboveWater.Add(model);
                    }
                    else
                    {
                        this.fixturesUnderWater.Add(model);
                    }
                }

                this.fixturesAboveWater = this.fixturesAboveWater.OrderBy(f => f.CanvasZ).ToList();
                this.fixturesUnderWater = this.fixturesUnderWater.OrderBy(f => f.CanvasZ).ToList();
            }
            finally
            {
                foreach (var riverPath in riverPaths.Values)
                {
                    riverPath.Dispose();
                }
            }

            this.zoneConfiguration.Reporter.ProgressReset();
        }

        /// <summary>
        /// Draws all textured models into one map sized canvas with a shared depth buffer, so overlapping models
        /// (ramps, bridges, stacked halls) show their top surface. Used for cities and dungeons, which have no
        /// trees or water. Models of other renderers are drawn the usual way on top.
        /// </summary>
        public void DrawShared(MagickImage map)
        {
            var shared = this.fixturesAboveWater.Where(f => f.RendererConf.Texture == TextureMode.Map
                                                            && (f.RendererConf.Renderer == FixtureRendererType.Shaded || f.RendererConf.Renderer == FixtureRendererType.Flat)
                                                            && f.RendererConf.Transparency == 0).ToList();
            this.zoneConfiguration.Reporter.Log(string.Format("There are {0} fixtures to draw.", this.fixturesAboveWater.Count), LogLevel.Notice);

            var canvas = new FixtureCanvas(this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize);
            // Solid surfaces first, then water over them, glows last
            foreach (var pass in new[] { 0, 1, 2 })
            {
                foreach (var fixture in shared)
                {
                    var lit = fixture.RendererConf.Renderer == FixtureRendererType.Shaded && fixture.RendererConf.HasLight;
                    foreach (var drawableElement in fixture.DrawableElements.Where(e => GetDrawPass(e) == pass))
                    {
                        canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, drawableElement.Texture, GetFillColor(fixture, drawableElement), lit ? drawableElement.Lightning : 1,
                                            drawableElement.Uvs2, drawableElement.Texture2, drawableElement.TextureBlend, drawableElement.Depths, fixture.CanvasX, fixture.CanvasY, fixture.BaseCanvasZ, drawableElement.VertexColors, drawableElement.Dark, drawableElement.DarkUvs,
                                            drawableElement.IsWater, drawableElement.IsAdditive ? drawableElement.AdditiveColor : -1);
                    }
                }
            }

            using (var layer = canvas.ToImage())
            {
                var shadowConf = shared.Select(f => f.RendererConf).FirstOrDefault(c => c.HasShadow);
                if (shadowConf.HasShadow)
                {
                    this.CastShadow(layer, shadowConf.ShadowOffsetX, shadowConf.ShadowOffsetY, shadowConf.ShadowSize, new Percentage(100 - shadowConf.ShadowTransparency), shadowConf.ShadowColor, false);
                }
                map.Composite(layer, 0, 0, CompositeOperator.SrcOver);
            }

            this.Draw(map, this.fixturesAboveWater.Except(shared).ToList());
        }

        public void Draw(MagickImage map, bool underwater)
        {
            if (underwater)
            {
                this.zoneConfiguration.Reporter.Log(string.Format("There are {0} fixtures to draw.", this.fixturesUnderWater.Count), LogLevel.Notice);
                this.Draw(map, this.fixturesUnderWater);
            }
            else
            {
                this.zoneConfiguration.Reporter.Log(string.Format("There are {0} fixtures to draw.", this.fixturesAboveWater.Count), LogLevel.Notice);
                this.Draw(map, this.fixturesAboveWater);
            }
        }

        private void Draw(MagickImage map, List<DrawableFixture> fixtures)
        {
            this.zoneConfiguration.Reporter.ProgressStart(string.Format("Drawing fixtures ({0}) ...", fixtures.Count));
            var timer = Stopwatch.StartNew();

            using (var modelsOverlay = MagickWrapper.NewImage(MagickColors.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
            {
                using (var treeOverlay = MagickWrapper.NewImage(MagickColors.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
                {
                    var processCounter = 0;
                    foreach (var fixture in fixtures)
                    {
                        // Debug single models
                        //if (fixture.FixtureRow.NifId != 408)
                        //{
                        //    continue;
                        //}                                             

                        switch (fixture.RendererConf.Renderer)
                        {
                            case FixtureRendererType.Shaded:
                                this.DrawShaded((fixture.IsTree || fixture.IsTreeCluster) ? treeOverlay : modelsOverlay, fixture);
                                break;
                            case FixtureRendererType.Flat:
                                this.DrawFlat((fixture.IsTree || fixture.IsTreeCluster) ? treeOverlay : modelsOverlay, fixture);
                                break;
                        }

                        var percent = 100 * processCounter / fixtures.Count();
                        this.zoneConfiguration.Reporter.ProgressUpdate(percent);
                        processCounter++;
                    }

                    this.zoneConfiguration.Reporter.ProgressStartMarquee("Merging ...");

                    // Trees cast one shadow as a layer, so overlapping crowns don't darken each other
                    var treeRConf = FixtureRendererConfigurations.GetRendererById("TreeShaded");
                    if (treeRConf.HasShadow)
                    {
                        this.CastShadow(
                                        treeOverlay,
                                        treeRConf.ShadowOffsetX,
                                        treeRConf.ShadowOffsetY,
                                        treeRConf.ShadowSize,
                                        new Percentage(100 - treeRConf.ShadowTransparency),
                                        treeRConf.ShadowColor,
                                        false
                                       );
                    }

                    if (treeRConf.Transparency != 0)
                    {
                        treeOverlay.Alpha(AlphaOption.Set);
                        var divideValue = 100.0 / (100.0 - this.TreeTransparency);
                        treeOverlay.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                    }

                    map.Composite(modelsOverlay, 0, 0, CompositeOperator.SrcOver);
                    map.Composite(treeOverlay, 0, 0, CompositeOperator.SrcOver);
                }
            }

            timer.Stop();
            this.zoneConfiguration.Reporter.Log(string.Format("Finished in {0} seconds.", timer.Elapsed.TotalSeconds), LogLevel.Success);
            this.zoneConfiguration.Reporter.ProgressReset();
        }

        private void DrawShaded(MagickImage overlay, DrawableFixture fixture)
        {
            //this.zoneConfiguration.Reporter.Log(string.Format("Shaded: {0} ({1}) ...", fixture.Name, fixture.NifName), LogLevel.notice);

            // A Shaded model without lightning is not shaded... but just we add this just be flexible
            using (var modelCanvas = this.DrawTriangles(fixture, fixture.RendererConf.HasLight))
            {
                // Trees get their shadow on the tree layer
                if (fixture.RendererConf.HasShadow && !fixture.IsTree && !fixture.IsTreeCluster)
                {
                    this.CastShadow(
                                    modelCanvas,
                                    fixture.RendererConf.ShadowOffsetX,
                                    fixture.RendererConf.ShadowOffsetY,
                                    fixture.RendererConf.ShadowSize,
                                    new Percentage(100 - fixture.RendererConf.ShadowTransparency),
                                    fixture.RendererConf.ShadowColor
                                   );

                    // Update the canvas position to match the new border
                    fixture.CanvasX -= fixture.RendererConf.ShadowSize;
                    fixture.CanvasY -= fixture.RendererConf.ShadowSize;
                }

                if (fixture.RendererConf.Transparency != 0)
                {
                    modelCanvas.Alpha(AlphaOption.Set);

                    var divideValue = 100.0 / (100.0 - fixture.RendererConf.Transparency);
                    modelCanvas.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                }
                
                overlay.Composite(modelCanvas, Convert.ToInt32(fixture.CanvasX), Convert.ToInt32(fixture.CanvasY), CompositeOperator.SrcOver);
            }
        }

        private void DrawFlat(MagickImage overlay, DrawableFixture fixture)
        {
            //this.zoneConfiguration.Reporter.Log(string.Format("Flat: {0} ({1}) ...", fixture.Name, fixture.NifName), LogLevel.notice);

            using (var modelCanvas = this.DrawTriangles(fixture, false))
            {

                if (fixture.RendererConf.HasShadow)
                {
                    this.CastShadow(
                                    modelCanvas,
                                    fixture.RendererConf.ShadowOffsetX,
                                    fixture.RendererConf.ShadowOffsetY,
                                    fixture.RendererConf.ShadowSize,
                                    new Percentage(100 - fixture.RendererConf.ShadowTransparency),
                                    fixture.RendererConf.ShadowColor
                                   );

                    // Update the canvas position to match the new border
                    fixture.CanvasX -= fixture.RendererConf.ShadowSize;
                    fixture.CanvasY -= fixture.RendererConf.ShadowSize;
                }

                if (fixture.RendererConf.Transparency != 0)
                {
                    modelCanvas.Alpha(AlphaOption.Set);

                    var divideValue = 100.0 / (100.0 - fixture.RendererConf.Transparency);
                    modelCanvas.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                }

                overlay.Composite(modelCanvas, Convert.ToInt32(fixture.CanvasX), Convert.ToInt32(fixture.CanvasY), CompositeOperator.SrcOver);
            }
        }

        // Solid surfaces first, then water over them, glows last
        private static int GetDrawPass(DrawableElement element)
        {
            return element.IsAdditive ? 2 : element.IsWater ? 1 : 0;
        }

        private TerrainHeights GetTerrain()
        {
            if (this.terrain == null && this.zoneConfiguration.HasTerrain && this.zoneConfiguration.Heightmap != null)
            {
                this.terrain = new TerrainHeights(this.zoneConfiguration);
            }
            return this.terrain;
        }

        /// <summary>
        /// Draws the model's triangles in z order into a new canvas, textured or filled with their color
        /// </summary>
        private MagickImage DrawTriangles(DrawableFixture fixture, bool lit)
        {
            if (fixture.RendererConf.Texture == TextureMode.Map)
            {
                var canvas = new FixtureCanvas(fixture.CanvasWidth, fixture.CanvasHeight, this.GetTerrain(), fixture.CanvasX, fixture.CanvasY);
                foreach (var drawableElement in fixture.DrawableElements.OrderBy(GetDrawPass))
                {
                    canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, drawableElement.Texture, GetFillColor(fixture, drawableElement), lit ? drawableElement.Lightning : 1, drawableElement.Uvs2, drawableElement.Texture2, drawableElement.TextureBlend, drawableElement.Depths,
                                        depthOffset: fixture.BaseCanvasZ, vertexColors: drawableElement.VertexColors, dark: drawableElement.Dark, darkUvs: drawableElement.DarkUvs, isWater: drawableElement.IsWater,
                                        additiveColor: drawableElement.IsAdditive ? drawableElement.AdditiveColor : -1);
                }
                return canvas.ToImage();
            }

            var image = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight);
            var drawables = new Drawables();
            foreach (var drawableElement in fixture.DrawableElements)
            {
                var color = GetFillColor(fixture, drawableElement);
                if (lit)
                {
                    drawables.FillColor(new MagickColor(
                        Convert.ToUInt16(drawableElement.Lightning * color.R),
                        Convert.ToUInt16(drawableElement.Lightning * color.G),
                        Convert.ToUInt16(drawableElement.Lightning * color.B)
                    ));
                }
                else
                {
                    drawables.FillColor(color);
                }

                drawables.Polygon(drawableElement.Coordinates);
            }

            DrawBatch(image, drawables);
            return image;
        }

        private static MagickColor GetFillColor(DrawableFixture fixture, DrawableElement drawableElement)
        {
            // Trees switch to TreeShaded after the elements were prepared, so check the mode here
            var textureColor = fixture.RendererConf.Texture != TextureMode.None ? drawableElement.TextureColor : null;
            return textureColor?.ToMagickColor() ?? fixture.Tree?.AverageColor.ToMagickColor() ?? fixture.RendererConf.Color;
        }

        // One draw call per model: ImageMagick sets up a full draw pass for every call
        private static void DrawBatch(MagickImage canvas, Drawables drawables)
        {
            if (drawables.Any())
            {
                drawables.Draw(canvas);
            }
        }

        private void CastShadow(IMagickImage<ushort> caster, int offsetX, int offsetY, double size, Percentage alpha, MagickColor color, bool extendCasterWithBorder = true)
        {
            using(var shadow = caster.Clone())
            {
                shadow.Shadow(offsetX, offsetY, size, alpha, color);

                if (extendCasterWithBorder)
                {
                    caster.BorderColor = MagickColors.Transparent;
                    caster.Border((uint)size);
                    caster.Composite(shadow, 0, 0, CompositeOperator.DstOver);
                }
                else
                {
                    // Shadow() grows the image by the blur and keeps offset and growth in the page
                    caster.Composite(shadow, shadow.Page.X, shadow.Page.Y, CompositeOperator.DstOver);
                }
            }
        }
    }
}
