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
        private readonly List<DrawableFixture> obliqueKeepPieces = new List<DrawableFixture>();

        private TerrainHeights terrain;

        private const string TERRAIN_CATEGORY = "Terrain";

        private const string GROUND_CATEGORY = "Ground";

        // Tree models whose cut out texture covers less than this share of their outline from above
        private const double CARD_TREE_COVERAGE = 0.3;

        // Keep occlusion: blur radius and the height above the surroundings that gives full occlusion (zone units), and its strength
        private const double KEEP_AO_RADIUS = 300;
        private const double KEEP_AO_FULL_HEIGHT = 400;
        private const double KEEP_AO_STRENGTH = 0.45;
        // Ramps, wall bases and courtyards inside the keep are lit by their own textures and need more to read as shaded
        private const double KEEP_AO_SURFACE_FULL_HEIGHT = 200;
        private const double KEEP_AO_SURFACE_STRENGTH = 0.45;
        // Cities and dungeons (option AmbientOcclusion): empty pixels do not count, so floors next to the void stay as they are
        private const double MODEL_AO_RADIUS = 300;
        private const double MODEL_AO_FULL_HEIGHT = 200;
        private const double MODEL_AO_STRENGTH = 0.45;
        // Outdoor zones (option AmbientOcclusion): the ground around buildings, like around the 3D keeps
        private const double BUILDING_AO_RADIUS = 300;
        private const double BUILDING_AO_FULL_HEIGHT = 400;
        private const double BUILDING_AO_STRENGTH = 0.45;

        // Soft edge of models with their own terrain, in zone units
        private const double TERRAIN_FEATHER = 256;

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

                    if (model.IsOblique)
                    {
                        if (riverHeight != 0)
                        {
                            model.WaterLevel = this.zoneConfiguration.ZoneCoordinateToMapCoordinate(riverHeight);
                        }
                        this.obliqueKeepPieces.Add(model);
                        continue;
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
        /// Draws the ground of models that bring their own terrain (category Terrain: meshes with blended texture
        /// layers) straight onto the map, before the relief shading, with a soft edge. Returns its surface height per
        /// map pixel in zone units (NaN elsewhere), so the relief shading covers it like the terrain around it.
        /// The rest of these models (walls, towers) is drawn later as usual.
        /// </summary>
        public float[] DrawTerrain(MagickImage map)
        {
            var terrainFixtures = this.fixturesAboveWater.Where(f => f.RendererConf.Name == TERRAIN_CATEGORY && f.RendererConf.Texture == TextureMode.Map).ToList();
            if (terrainFixtures.Count == 0)
            {
                return null;
            }

            var size = this.zoneConfiguration.TargetMapSize;
            var canvas = new FixtureCanvas(size, size, this.GetTerrain());
            foreach (var fixture in terrainFixtures)
            {
                var ground = fixture.DrawableElements.Where(e => e.Texture2 != null && !e.IsWater && !e.IsAdditive).ToList();
                fixture.DrawableElements = fixture.DrawableElements.Except(ground).ToList();
                foreach (var drawableElement in ground)
                {
                    canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, drawableElement.Texture, GetFillColor(fixture, drawableElement), 1, drawableElement.Uvs2, drawableElement.Texture2, drawableElement.TextureBlend,
                                        drawableElement.Depths, fixture.ExactCanvasX, fixture.ExactCanvasY, fixture.BaseCanvasZ, drawableElement.VertexColors);
                }
            }

            using (var layer = canvas.ToImage())
            {
                FeatherEdge(layer, this.zoneConfiguration.ZoneCoordinateToMapCoordinate(TERRAIN_FEATHER));
                map.Composite(layer, 0, 0, CompositeOperator.SrcOver);
            }

            var heights = canvas.ToHeights();
            this.GetTerrain()?.Raise(heights, this.zoneConfiguration);
            var zoneUnitsPerMapUnit = this.zoneConfiguration.ZoneSize / size;
            for (var i = 0; i < heights.Length; i++)
            {
                heights[i] = (float)(heights[i] * zoneUnitsPerMapUnit);
            }
            return heights;
        }

        // Ground tiles share one canvas at their exact positions; drawn one by one at rounded positions, gaps open between them
        private void DrawGround(MagickImage overlay, List<DrawableFixture> ground)
        {
            var size = this.zoneConfiguration.TargetMapSize;
            var canvas = new FixtureCanvas(size, size, this.GetTerrain());
            foreach (var fixture in ground)
            {
                var lit = fixture.RendererConf.Renderer == FixtureRendererType.Shaded && fixture.RendererConf.HasLight;
                foreach (var drawableElement in fixture.DrawableElements.OrderBy(GetDrawPass))
                {
                    canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, drawableElement.Texture, GetFillColor(fixture, drawableElement), lit ? drawableElement.Lightning : 1, drawableElement.Uvs2, drawableElement.Texture2,
                                        drawableElement.TextureBlend, drawableElement.Depths, fixture.ExactCanvasX, fixture.ExactCanvasY, fixture.BaseCanvasZ, drawableElement.VertexColors, drawableElement.Dark, drawableElement.DarkUvs,
                                        drawableElement.IsWater, drawableElement.IsAdditive ? drawableElement.AdditiveColor : -1);
                }
            }

            using var layer = canvas.ToImage();
            overlay.Composite(layer, 0, 0, CompositeOperator.SrcOver);
        }

        // Fades the layer out towards its outline; the alpha only shrinks, so no dark fringe grows outside
        private static void FeatherEdge(MagickImage layer, double radius)
        {
            if (radius < 1)
            {
                return;
            }

            using var alpha = (MagickImage)layer.Separate(Channels.Alpha).First();
            alpha.Morphology(new MorphologySettings { Method = MorphologyMethod.Erode, Kernel = Kernel.Disk, KernelArguments = radius.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            alpha.Blur(0, radius / 2);

            using var alphaPixels = alpha.GetPixels();
            var soft = alphaPixels.ToArray();
            var alphaChannels = (int)alpha.ChannelCount;
            using var layerPixels = layer.GetPixels();
            var values = layerPixels.ToArray();
            var channels = (int)layer.ChannelCount;
            for (var i = 0; i < values.Length / channels; i++)
            {
                values[i * channels + 3] = Math.Min(values[i * channels + 3], soft[i * alphaChannels]);
            }
            layerPixels.SetPixels(values);
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

            if (this.zoneConfiguration.AmbientOcclusion)
            {
                var size = this.zoneConfiguration.TargetMapSize;
                var blurred = HeightOcclusion.BlurDrawn(canvas.ToHeights(), size, size, this.zoneConfiguration.ZoneCoordinateToMapCoordinate(MODEL_AO_RADIUS));
                canvas.Darken(blurred, this.zoneConfiguration.ZoneCoordinateToMapCoordinate(MODEL_AO_FULL_HEIGHT), MODEL_AO_STRENGTH);
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
                this.Draw(map, this.fixturesAboveWater, true);
            }
        }

        // All keep pieces share one canvas and depth buffer; depths are the map row of the ground point plus the height shifted up
        private void DrawObliqueKeeps(MagickImage target)
        {
            if (this.obliqueKeepPieces.Count == 0)
            {
                return;
            }

            var size = this.zoneConfiguration.TargetMapSize;
            var factors = this.DarkenAroundKeeps(target, size);
            var canvas = new FixtureCanvas(size, size, this.GetTerrain());
            foreach (var pass in new[] { 0, 1, 2 })
            {
                foreach (var fixture in this.obliqueKeepPieces)
                {
                    // Integer halves like the canvas coordinates of GenerateCanvas
                    var centerX = fixture.ExactCanvasX + fixture.CanvasWidth / 2;
                    var centerY = fixture.ExactCanvasY + fixture.CanvasHeight / 2;
                    foreach (var element in fixture.DrawableElements.Where(e => GetDrawPass(e) == pass))
                    {
                        var positions = element.Positions.Select(p => new System.Numerics.Vector3((float)(centerX + p.X), (float)(centerY - p.Y), (float)(fixture.BaseCanvasZ + p.Z))).ToArray();
                        canvas.FillTriangle(element.Coordinates, element.Uvs, element.Texture, GetFillColor(fixture, element), element.Lightning,
                                            element.Uvs2, element.Texture2, element.TextureBlend, element.Depths, fixture.ExactCanvasX, fixture.ExactCanvasY, centerY + fixture.ObliqueFactor * fixture.BaseCanvasZ,
                                            element.VertexColors, element.Dark, element.DarkUvs, element.IsWater, element.IsAdditive ? element.AdditiveColor : -1, positions, DrawableFixture.KEEP_DARK_MAP_SCALE, factors,
                                            double.IsNaN(fixture.WaterLevel) ? double.MinValue : fixture.WaterLevel);
                    }
                }
            }

            using var layer = canvas.ToImage();
            var shadowConf = this.obliqueKeepPieces.Select(f => f.RendererConf).FirstOrDefault(c => c.HasShadow);
            if (shadowConf.HasShadow)
            {
                this.CastShadow(layer, shadowConf.ShadowOffsetX, shadowConf.ShadowOffsetY, shadowConf.ShadowSize, new Percentage(100 - shadowConf.ShadowTransparency), shadowConf.ShadowColor, false);
            }
            target.Composite(layer, 0, 0, CompositeOperator.SrcOver);
        }

        // Darkens the map where the keeps stand higher than their surroundings; returns the factors for the keep floors, null without a terrain
        private OcclusionMap DarkenAroundKeeps(MagickImage target, int size)
        {
            var ground = this.GetTerrain();
            if (ground == null)
            {
                return null;
            }

            var radius = this.zoneConfiguration.ZoneCoordinateToMapCoordinate(KEEP_AO_RADIUS);

            // Everything runs on the bounding box of the keeps plus the blur reach
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var fixture in this.obliqueKeepPieces)
            {
                var centerX = fixture.ExactCanvasX + fixture.CanvasWidth / 2;
                var centerY = fixture.ExactCanvasY + fixture.CanvasHeight / 2;
                foreach (var p in fixture.DrawableElements.SelectMany(e => e.Positions))
                {
                    minX = Math.Min(minX, centerX + p.X);
                    maxX = Math.Max(maxX, centerX + p.X);
                    minY = Math.Min(minY, centerY - p.Y);
                    maxY = Math.Max(maxY, centerY - p.Y);
                }
            }

            if (minX > maxX)
            {
                return null;
            }

            var margin = 3 * HeightOcclusion.BoxRadius(radius);
            var x0 = Math.Max(0, (int)Math.Floor(minX) - margin);
            var y0 = Math.Max(0, (int)Math.Floor(minY) - margin);
            var width = Math.Min(size, (int)Math.Ceiling(maxX) + margin + 1) - x0;
            var height = Math.Min(size, (int)Math.Ceiling(maxY) + margin + 1) - y0;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            // Top-down surface of the keep pieces at their real positions, heights absolute
            var heightCanvas = new FixtureCanvas(width, height);
            foreach (var fixture in this.obliqueKeepPieces)
            {
                var centerX = fixture.ExactCanvasX + fixture.CanvasWidth / 2;
                var centerY = fixture.ExactCanvasY + fixture.CanvasHeight / 2;
                foreach (var element in fixture.DrawableElements.Where(e => GetDrawPass(e) == 0))
                {
                    var coordinates = element.Positions.Select(p => new PointD(centerX + p.X, centerY - p.Y)).ToArray();
                    var depths = element.Positions.Select(p => fixture.BaseCanvasZ + p.Z).ToArray();
                    heightCanvas.FillTriangle(coordinates, null, null, MagickColors.White, 1, depths: depths, offsetX: -x0, offsetY: -y0);
                }
            }

            var heights = heightCanvas.ToHeights();
            var groundHeights = new float[heights.Length];
            for (var i = 0; i < heights.Length; i++)
            {
                groundHeights[i] = ground.GroundAt(x0 + i % width, y0 + i / width);
                heights[i] = float.IsNaN(heights[i]) ? 0 : Math.Max(0, heights[i] - groundHeights[i]);
            }

            // Every point is compared by its own height, so wall bases and ramps darken, and the ground darkens as ground where overhangs uncover it
            var occlusion = new OcclusionMap(HeightOcclusion.Blur(heights, width, height, radius), groundHeights, x0, y0, width, height,
                                             this.zoneConfiguration.ZoneCoordinateToMapCoordinate(KEEP_AO_FULL_HEIGHT), KEEP_AO_STRENGTH,
                                             this.zoneConfiguration.ZoneCoordinateToMapCoordinate(KEEP_AO_SURFACE_FULL_HEIGHT), KEEP_AO_SURFACE_STRENGTH);
            HeightOcclusion.Apply(target, occlusion);
            return occlusion;
        }

        // Darkens the ground around outdoor models by their height above the terrain; runs before the models are drawn, their canvas positions are still unshifted
        private void DarkenAroundBuildings(MagickImage map, List<DrawableFixture> buildings)
        {
            var ground = this.GetTerrain();
            if (ground == null || buildings.Count == 0)
            {
                return;
            }

            var size = this.zoneConfiguration.TargetMapSize;
            var heightCanvas = new FixtureCanvas(size, size);
            foreach (var fixture in buildings)
            {
                foreach (var element in fixture.DrawableElements.Where(e => GetDrawPass(e) == 0 && e.Depths != null))
                {
                    heightCanvas.FillTriangle(element.Coordinates, null, null, MagickColors.White, 1, depths: element.Depths, offsetX: fixture.CanvasX, offsetY: fixture.CanvasY, depthOffset: fixture.BaseCanvasZ);
                }
            }

            var heights = heightCanvas.ToHeights();
            var groundHeights = new float[heights.Length];
            for (var i = 0; i < heights.Length; i++)
            {
                groundHeights[i] = ground.GroundAt(i % size, i / size);
                heights[i] = float.IsNaN(heights[i]) ? 0 : Math.Max(0, heights[i] - groundHeights[i]);
            }

            var fullHeight = this.zoneConfiguration.ZoneCoordinateToMapCoordinate(BUILDING_AO_FULL_HEIGHT);
            var occlusion = new OcclusionMap(HeightOcclusion.Blur(heights, size, size, this.zoneConfiguration.ZoneCoordinateToMapCoordinate(BUILDING_AO_RADIUS)), groundHeights, 0, 0, size, size,
                                             fullHeight, BUILDING_AO_STRENGTH, fullHeight, BUILDING_AO_STRENGTH);
            HeightOcclusion.Apply(map, occlusion);
        }

        private void Draw(MagickImage map, List<DrawableFixture> fixtures, bool withKeeps = false)
        {
            this.zoneConfiguration.Reporter.ProgressStart(string.Format("Drawing fixtures ({0}) ...", fixtures.Count));
            var timer = Stopwatch.StartNew();

            using (var modelsOverlay = MagickWrapper.NewImage(MagickColors.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
            {
                var ground = fixtures.Where(f => f.RendererConf.Name == GROUND_CATEGORY && f.RendererConf.Texture == TextureMode.Map).ToList();
                if (ground.Count > 0)
                {
                    this.DrawGround(modelsOverlay, ground);
                    fixtures = fixtures.Except(ground).ToList();
                }

                if (withKeeps && this.zoneConfiguration.AmbientOcclusion)
                {
                    this.DarkenAroundBuildings(map, fixtures.Where(f => !f.IsTree && !f.IsTreeCluster).ToList());
                }

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
                    if (withKeeps)
                    {
                        this.DrawObliqueKeeps(map);
                    }
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

        private FixtureCanvas FillCanvas(DrawableFixture fixture, bool lit, bool textured)
        {
            var canvas = new FixtureCanvas(fixture.CanvasWidth, fixture.CanvasHeight, this.GetTerrain(), fixture.CanvasX, fixture.CanvasY);
            foreach (var drawableElement in fixture.DrawableElements.OrderBy(GetDrawPass))
            {
                canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, textured ? drawableElement.Texture : null, GetFillColor(fixture, drawableElement), lit ? drawableElement.Lightning : 1, drawableElement.Uvs2, drawableElement.Texture2, drawableElement.TextureBlend, drawableElement.Depths,
                                    depthOffset: fixture.BaseCanvasZ, vertexColors: drawableElement.VertexColors, dark: drawableElement.Dark, darkUvs: drawableElement.DarkUvs, isWater: drawableElement.IsWater,
                                    additiveColor: drawableElement.IsAdditive ? drawableElement.AdditiveColor : -1);
            }
            return canvas;
        }

        /// <summary>
        /// Draws the model's triangles in z order into a new canvas, textured or filled with their color
        /// </summary>
        private MagickImage DrawTriangles(DrawableFixture fixture, bool lit)
        {
            if (fixture.RendererConf.Texture == TextureMode.Map)
            {
                if (!fixture.IsTree && !fixture.IsTreeCluster)
                {
                    return this.FillCanvas(fixture, lit, true).ToImage();
                }

                // Billboard trees (side views on crossed cards) lose their leaves when cut out from above; decided per placed tree, not per model, so drawing order cannot matter
                var textured = this.FillCanvas(fixture, lit, true);
                var filled = this.FillCanvas(fixture, lit, false);
                return (textured.CoveredPixels() < filled.CoveredPixels() * CARD_TREE_COVERAGE ? filled : textured).ToImage();
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
