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
	internal class MapFixtures : IDisposable
    {
        private readonly ZoneConfiguration zoneConfiguration;
        private readonly List<WaterConfiguration> rivers;
        private readonly FixturesLoader loader;

        private readonly List<DrawableFixture> fixtures = new List<DrawableFixture>();

        private List<DrawableFixture> fixturesUnderWater = new List<DrawableFixture>();
        private List<DrawableFixture> fixturesAboveWater = new List<DrawableFixture>();

        private readonly Dictionary<string, MagickImage> modelImages = new Dictionary<string, MagickImage>();

        #region Settings
        public bool DrawFixtures { get; set; } = true;

        public bool DrawTrees { get; set; } = true;

        public bool DrawTreesAsImages { get; set; } = true;

        public int TreeTransparency { get; set; } = 20;
        #endregion

        public MapFixtures(ZoneConfiguration zoneConfiguration, List<WaterConfiguration> rivers, FixturesLoader loader = null)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.rivers = rivers;

            // Loads CSV files and polygons
            this.loader = loader ?? new FixturesLoader(zoneConfiguration);

            // Prepare models
            this.fixtures = this.loader.GetDrawableFixtures();
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

                if (!this.DrawTreesAsImages && (model.IsTree || model.IsTreeCluster))
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

                if (riverHeight == 0 || (riverHeight != 0 && model.FixtureRow.Z > riverHeight))
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

            // Dispose all paths
            riverPaths.Select(d => d.Value).ToList().ForEach(r => r.Dispose());

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
            foreach (var fixture in shared)
            {
                var lit = fixture.RendererConf.Renderer == FixtureRendererType.Shaded && fixture.RendererConf.HasLight;
                foreach (var drawableElement in fixture.DrawableElements)
                {
                    canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, drawableElement.Texture, GetFillColor(fixture, drawableElement), lit ? drawableElement.Lightning : 1,
                                        drawableElement.Uvs2, drawableElement.Texture2, drawableElement.TextureBlend, drawableElement.Depths, fixture.CanvasX, fixture.CanvasY, fixture.BaseCanvasZ, drawableElement.VertexColors);
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
                            case FixtureRendererType.Image:
                                //DrawShaded((fixture.IsTree || fixture.IsTreeCluster) ? treeOverlay : modelsOverlay, fixture);
                                this.DrawImage((fixture.IsTree || fixture.IsTreeCluster) ? treeOverlay : modelsOverlay, fixture);
                                break;
                        }

                        var percent = 100 * processCounter / fixtures.Count();
                        this.zoneConfiguration.Reporter.ProgressUpdate(percent);
                        processCounter++;
                    }

                    this.zoneConfiguration.Reporter.ProgressStartMarquee("Merging ...");

                    var treeImagesRConf = FixtureRendererConfigurations.GetRendererById("TreeImage");
                    if (treeImagesRConf.HasShadow)
                    {
                        this.CastShadow(
                                        treeOverlay,
                                        treeImagesRConf.ShadowOffsetX,
                                        treeImagesRConf.ShadowOffsetY,
                                        treeImagesRConf.ShadowSize,
                                        new Percentage(100 - treeImagesRConf.ShadowTransparency),
                                        treeImagesRConf.ShadowColor,
                                        false
                                       );
                    }
                    
                    if (treeImagesRConf.Transparency != 0)
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
            using (var modelCanvas = DrawTriangles(fixture, fixture.RendererConf.HasLight))
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

        private void DrawFlat(MagickImage overlay, DrawableFixture fixture)
        {
            //this.zoneConfiguration.Reporter.Log(string.Format("Flat: {0} ({1}) ...", fixture.Name, fixture.NifName), LogLevel.notice);

            using (var modelCanvas = DrawTriangles(fixture, false))
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

        private void DrawImage(MagickImage overlay, DrawableFixture fixture)
        {
            //this.zoneConfiguration.Reporter.Log(string.Format("Image: {0} ({1}) ...", fixture.Name, fixture.NifName), LogLevel.notice);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(fixture.NifName);
            var defaultTree = "elm1";

            // Load default tree
            if (!this.modelImages.ContainsKey(defaultTree))
            {
                var defaultTreeImage = string.Format("{0}\\data\\prerendered\\trees\\{1}.png", System.Windows.Forms.Application.StartupPath, defaultTree);                     
                if (System.IO.File.Exists(defaultTreeImage))
                {
                    var treeImage = new MagickImage(defaultTreeImage);
                    treeImage.Blur();
                    this.modelImages.Add(defaultTree, treeImage);
                }
                else
                {
                    this.modelImages.Add(fileName, null);
                }
            }

            // TreeClusters are sets of trees in a specified arrangement
            // They need to be drawe separately
            if (fixture.IsTreeCluster)
            {
                this.DrawTreeCluster(overlay, fixture);
                return;
            }

            // Load model image
            if (!this.modelImages.ContainsKey(fileName))
            {
                var objectImageFile = string.Format("{0}\\data\\prerendered\\objects\\{1}.png", System.Windows.Forms.Application.StartupPath, fileName);
                if (fixture.IsTree) objectImageFile = string.Format("{0}\\data\\prerendered\\trees\\{1}.png", System.Windows.Forms.Application.StartupPath, fileName);

                if (System.IO.File.Exists(objectImageFile))
                {
                    var objectImage = new MagickImage(objectImageFile);
                    if(fixture.IsTree) objectImage.Blur();
                    this.modelImages.Add(fileName, objectImage);
                }
                else
                {
                    if (fixture.IsTree)
                    {
                        this.zoneConfiguration.Reporter.Log(string.Format("Can not find image for tree {0} ({1}), using default tree", fixture.Name, fixture.NifName), LogLevel.Warning);
                        this.modelImages.Add(fileName, this.modelImages[defaultTree]);
                    }
                    else this.modelImages.Add(fileName, null);
                }
            }

            // Draw the image
            if (this.modelImages.ContainsKey(fileName) && this.modelImages[fileName] != null)
            {
                var orginalNif = this.loader.NifRows.FirstOrDefault(n => n.NifId == fixture.FixtureRow.NifId);
                if (orginalNif == null)
                {
                    this.zoneConfiguration.Reporter.Log(string.Format("Error with imaged nif ({0})!", fixture.FixtureRow.TextualName), LogLevel.Warning);
                }

                var objectSize = orginalNif.GetSize(0, 0);

                // The final image
                using (var modelImage = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight))
                {
                    // Place the replacing image
                    using (var newModelImage = this.modelImages[fileName].Clone())
                    {
                        newModelImage.BackgroundColor = MagickColors.Transparent;

                        double scaleWidthToTreeImage = objectSize.Width / newModelImage.Width;
                        double scaleHeightToTreeImage = objectSize.Height / newModelImage.Height;
                        var width = Convert.ToInt32(newModelImage.Width * scaleWidthToTreeImage * fixture.Scale);
                        var height = Convert.ToInt32(newModelImage.Height * scaleHeightToTreeImage * fixture.Scale);

                        // Resize to new size
                        newModelImage.FilterType = FilterType.Gaussian;
                        newModelImage.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                        newModelImage.Resize((uint)(width), (uint)(height));

                        // Rotate the image
                        //newModelImage.Rotate(fixture.FixtureRow.A * -1 * fixture.FixtureRow.AxisZ3D);
                        newModelImage.Rotate((360d * fixture.FixtureRow.AxisZ3D - fixture.FixtureRow.A) * -1);

                        // Place in center of modelImage
                        modelImage.Composite(newModelImage, Gravity.Center, CompositeOperator.SrcOver);
                    }

                    // Draw the shaped model if wanted
                    if (fixture.RendererConf.HasLight)
                    {
                        using (var modelShaped = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight))
                        {
                            var drawables = new Drawables();
                            foreach (var drawableElement in fixture.DrawableElements)
                            {
                                var light = 1 - drawableElement.Lightning;
                                drawables.FillColor(new MagickColor(
                                    Convert.ToUInt16(ushort.MaxValue * light),
                                    Convert.ToUInt16(ushort.MaxValue * light),
                                    Convert.ToUInt16(ushort.MaxValue * light)
                                ));
                                drawables.Polygon(drawableElement.Coordinates);
                            }

                            DrawBatch(modelShaped, drawables);

                            using(var modelMask = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight))
                            {
                                modelShaped.Blur();
                                modelMask.Composite(modelShaped, 0, 0, CompositeOperator.DstAtop);
                                modelMask.Composite(modelImage, 0, 0, CompositeOperator.DstIn);
                                modelMask.Level(new Percentage(20), new Percentage(100), Channels.All);
                                modelImage.Composite(modelMask, 0, 0, CompositeOperator.ColorDodge);
                            }
                        }
                    }

                    // Add the shadow if not a tree (tree shadow are substituted by a treeoverlay)
                    if (fixture.RendererConf.HasShadow && !fixture.IsTree)
                    {
                        this.CastShadow(
                                        modelImage,
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

                    // Set transprency if not a tree (see shadow)
                    if (fixture.RendererConf.Transparency != 0 && !fixture.IsTree)
                    {
                        var divideValue = 100.0 / (100.0 - fixture.RendererConf.Transparency);
                        modelImage.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                    }

                    // Place the image on the right position
                    overlay.Composite(modelImage, Convert.ToInt32(fixture.CanvasX), Convert.ToInt32(fixture.CanvasY), CompositeOperator.SrcOver);
                }
            }
        }

        private void DrawTree(MagickImage overlay, DrawableFixture fixture)
        {
            var testColor = System.Drawing.ColorTranslator.FromHtml("#5e683a");

            using (var pattern = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight))
            {
                using (var patternTexture = new MagickImage(string.Format("{0}\\data\\textures\\{1}.png", System.Windows.Forms.Application.StartupPath, "leaves_mask")))
                {
                    patternTexture.Resize((uint)(fixture.CanvasWidth / 2), (uint)(fixture.CanvasHeight / 2));
                    pattern.Texture(patternTexture);

                    var rnd = new Random();
                    pattern.Rotate(rnd.Next(0, 360));
                    
                    using (var modelCanvas = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight))
                    {
                        foreach (var drawableElement in fixture.DrawableElements)
                        {
                            var polyDraw = new DrawablePolygon(drawableElement.Coordinates);
                            
                            // A Shaded model without lightning is not shaded... but just we add this just be flexible
                            if (fixture.RendererConf.HasLight)
                            {
                                float r, g, b, light;

                                light = (float)drawableElement.Lightning * 2f;
                                r = fixture.Tree.AverageColor.R * light;
                                g = fixture.Tree.AverageColor.G * light;
                                b = fixture.Tree.AverageColor.B * light;


                                modelCanvas.Settings.FillColor = new MagickColor(
                                    Convert.ToUInt16(r * 255),
                                    Convert.ToUInt16(g * 255),
                                    Convert.ToUInt16(b * 255)
                                );
                            }
                            else
                            {
                                modelCanvas.Settings.FillColor = fixture.RendererConf.Color;
                            }

                            modelCanvas.Draw(polyDraw);
                        }

                        // Add leaves pattern
                        pattern.Composite(modelCanvas, Gravity.Center, CompositeOperator.DstIn);
                        modelCanvas.Composite(pattern, Gravity.Center, CompositeOperator.CopyAlpha);


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
            }
        }

        private void DrawTreeCluster(MagickImage overlay, DrawableFixture fixture)
        {
            //this.zoneConfiguration.Reporter.Log(string.Format("Image: {0} ({1}) ...", fixture.Name, fixture.TreeCluster.Tree), LogLevel.notice);
            var fileName = System.IO.Path.GetFileNameWithoutExtension(fixture.TreeCluster.Tree);
            var defaultTree = "elm1";

            // Load model image
            if (!this.modelImages.ContainsKey(fileName))
            {
                var treeImageFile = string.Format("{0}\\data\\prerendered\\trees\\{1}.png", System.Windows.Forms.Application.StartupPath, fileName);
                if (System.IO.File.Exists(treeImageFile))
                {
                    var modelImage = new MagickImage(treeImageFile);
                    modelImage.Blur();
                    this.modelImages.Add(fileName, modelImage);
                }
                else
                {
                    this.zoneConfiguration.Reporter.Log(string.Format("Can not find image for tree {0} ({1}), using default tree", fixture.TreeCluster.Tree, fixture.NifName), LogLevel.Warning);
                    this.modelImages.Add(fileName, this.modelImages[defaultTree]);
                }
            }

            if (this.modelImages.ContainsKey(fileName) && this.modelImages[fileName] != null)
            {
                // Get the width of the orginal tree shape
                var tree = this.loader.NifRows.FirstOrDefault(n => n.Filename.ToLower() == fixture.TreeCluster.Tree.ToLower());
                if (tree == null) return;

                var treeSize = tree.GetSize(0, 0);

                var dimensions = ((fixture.CanvasWidth > fixture.CanvasHeight) ? fixture.CanvasWidth : fixture.CanvasHeight) + 10;
                var extendedWidth = dimensions - fixture.CanvasWidth;
                var extendedHeight = dimensions - fixture.CanvasHeight;

                using (var treeCluster = MagickWrapper.NewImage(MagickColors.Transparent, dimensions, dimensions))
                {
                    var centerX = treeCluster.Width / 2d;
                    var centerY = treeCluster.Height / 2d;

                    foreach (var treeInstance in fixture.TreeCluster.TreeInstances)
                    {
                        using (var treeImage = this.modelImages[fileName].Clone())
                        {
                            double scaleWidthToTreeImage = treeSize.Width / treeImage.Width;
                            double scaleHeightToTreeImage = treeSize.Height / treeImage.Height;
                            var width = Convert.ToInt32(treeImage.Width * scaleWidthToTreeImage * fixture.Scale);
                            var height = Convert.ToInt32(treeImage.Height * scaleHeightToTreeImage * fixture.Scale);
                            treeImage.Resize((uint)(width), (uint)(height));

                            var x = Convert.ToInt32(centerX - width / 2d - this.zoneConfiguration.ZoneCoordinateToMapCoordinate(treeInstance.X) * (fixture.FixtureRow.Scale / 100));
                            var y = Convert.ToInt32(centerY - height / 2d - this.zoneConfiguration.ZoneCoordinateToMapCoordinate(treeInstance.Y) * (fixture.FixtureRow.Scale / 100));
                            treeCluster.Composite(treeImage, x, y, CompositeOperator.SrcOver);
                        }
                    }

                    treeCluster.Rotate((360d * fixture.FixtureRow.AxisZ3D - fixture.FixtureRow.A) * -1);

                    using (var modelCanvas = MagickWrapper.NewImage(MagickColors.Transparent, fixture.CanvasWidth, fixture.CanvasHeight))
                    {
                        var drawables = new Drawables();
                        foreach (var drawableElement in fixture.DrawableElements)
                        {
                            drawables.FillColor(new MagickColor(
                                Convert.ToUInt16(128 * 256 * drawableElement.Lightning),
                                Convert.ToUInt16(128 * 256 * drawableElement.Lightning),
                                Convert.ToUInt16(128 * 256 * drawableElement.Lightning)
                            ));
                            drawables.Polygon(drawableElement.Coordinates);
                        }

                        DrawBatch(modelCanvas, drawables);

                        modelCanvas.Composite(treeCluster, Gravity.Center, CompositeOperator.DstIn);
                        treeCluster.Composite(modelCanvas, Gravity.Center, CompositeOperator.Overlay);
                        //treeCluster.Composite(modelCanvas, Gravity.Center, CompositeOperator.SrcOver);
                    }

                    if (fixture.RendererConf.HasShadow)
                    {
                        this.CastShadow(
                                        treeCluster,
                                        fixture.RendererConf.ShadowOffsetX,
                                        fixture.RendererConf.ShadowOffsetY,
                                        fixture.RendererConf.ShadowSize,
                                        new Percentage(100 - fixture.RendererConf.ShadowTransparency),
                                        fixture.RendererConf.ShadowColor,
                                        false
                                       );
                    }

                    if (fixture.RendererConf.Transparency != 0)
                    {
                        treeCluster.Alpha(AlphaOption.Set);

                        var divideValue = 100.0 / (100.0 - fixture.RendererConf.Transparency);
                        treeCluster.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                    }

                    overlay.Composite(treeCluster, Convert.ToInt32(fixture.CanvasX - extendedWidth/2), Convert.ToInt32(fixture.CanvasY - extendedHeight/2), CompositeOperator.SrcOver);
                }
            }
        }

        /// <summary>
        /// Draws the model's triangles in z order into a new canvas, textured or filled with their color
        /// </summary>
        private static MagickImage DrawTriangles(DrawableFixture fixture, bool lit)
        {
            if (fixture.RendererConf.Texture == TextureMode.Map)
            {
                var canvas = new FixtureCanvas(fixture.CanvasWidth, fixture.CanvasHeight);
                foreach (var drawableElement in fixture.DrawableElements)
                {
                    canvas.FillTriangle(drawableElement.Coordinates, drawableElement.Uvs, drawableElement.Texture, GetFillColor(fixture, drawableElement), lit ? drawableElement.Lightning : 1, drawableElement.Uvs2, drawableElement.Texture2, drawableElement.TextureBlend, drawableElement.Depths, vertexColors: drawableElement.VertexColors);
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

        public void Dispose()
        {
            this.modelImages.Select(i => i.Value).Where(i => i != null).ToList().ForEach(i => i.Dispose());
        }
    }
}
