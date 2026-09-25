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
using System.Drawing;
using System.IO;
using System.Linq;
using ImageMagick;
using ImageMagick.Drawing;

namespace MapCreator.Classes.MapCreation
{
	internal class MapWater
    {
        private readonly ZoneConfiguration zoneConfiguration;

        internal List<WaterConfiguration> WaterAreas { get; } = new List<WaterConfiguration>();

        public Color WaterColor { get; set; }

        public int WaterTransparency { get; set; }

        public bool UseClientColors { get; set; } = true;

        private readonly bool debug = false;

        public MapWater(ZoneConfiguration zoneConfiguration)
        {
            MainForm.ProgressStartMarquee("Loading water configurations ...");
            this.zoneConfiguration = zoneConfiguration;

            var riversFound = true;
            var riverIndex = 0;

            while (riversFound)
            {
                var riverIndexString = "river" + ((riverIndex < 10) ? "0" + riverIndex : riverIndex.ToString());

                // Check if there is a section
                var riverCheck = zoneConfiguration.SectorDat.Get(riverIndexString, "name");
                if (string.IsNullOrEmpty(riverCheck))
                {
                    riversFound = false;
                    continue;
                }

                var waterConf = new WaterConfiguration(riverCheck)
                                {
		                                Texture = zoneConfiguration.SectorDat.Get(riverIndexString, "texture"),
		                                Multitexture = zoneConfiguration.SectorDat.Get(riverIndexString, "multitexture"),
		                                Flow = zoneConfiguration.SectorDat.Get(riverIndexString, "flow"),
		                                Height = Convert.ToInt32(zoneConfiguration.SectorDat.Get(riverIndexString, "height")),
		                                Bankpoints = Convert.ToInt32(zoneConfiguration.SectorDat.Get(riverIndexString, "bankpoints")),
		                                ExtendPosX = zoneConfiguration.SectorDat.Get(riverIndexString, "Extend_PosX"),
		                                ExtendPosY = zoneConfiguration.SectorDat.Get(riverIndexString, "Extend_PosY"),
		                                ExtendNegX = zoneConfiguration.SectorDat.Get(riverIndexString, "Extend_NegX"),
		                                ExtendNegY = zoneConfiguration.SectorDat.Get(riverIndexString, "Extend_NegY"),
		                                Tesselation = zoneConfiguration.SectorDat.Get(riverIndexString, "Tesselation"),
		                                Type = zoneConfiguration.SectorDat.Get(riverIndexString, "type")
                                };

                // Adjust some river heights
                if (zoneConfiguration.ZoneId == "168" || zoneConfiguration.ZoneId == "171" || zoneConfiguration.ZoneId == "178")
                {
                    waterConf.Height += 30;
                }

                // Ignore some definitions
                if(zoneConfiguration.ZoneId == "163" && riverIndexString == "river14")
                {
                    riverIndex++;
                    continue;
                }

                var color = zoneConfiguration.SectorDat.Get(riverIndexString, "color");
                var baseColor = zoneConfiguration.SectorDat.Get(riverIndexString, "base_color");
                if (color.Length >= 6)
                {
                    waterConf.Color = ColorTranslator.FromWin32(Convert.ToInt32((string.IsNullOrEmpty(baseColor)) ? color : baseColor));
                }

                for (var i = 0; i < waterConf.Bankpoints; i++)
                {
                    var coordinatesIndexString = (i < 10) ? "0" + i : i.ToString();
                    var left = zoneConfiguration.SectorDat.Get(riverIndexString, "left" + coordinatesIndexString);
                    var right = zoneConfiguration.SectorDat.Get(riverIndexString, "right" + coordinatesIndexString);

                    if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                    {
                        continue;
                    }

                    var leftArr = left.Split(',');
                    var rightArr = right.Split(',');

                    var leftPoint = new PointD((Convert.ToInt32(leftArr[0]) >= 0) ? Convert.ToInt32(leftArr[0]) : 0, (Convert.ToInt32(leftArr[1]) >= 0) ? Convert.ToInt32(leftArr[1]) : 0);
                    waterConf.LeftCoordinates.Add(leftPoint);

                    var rightPoint = new PointD((Convert.ToInt32(rightArr[0]) >= 0) ? Convert.ToInt32(rightArr[0]) : 0, (Convert.ToInt32(rightArr[1]) >= 0) ? Convert.ToInt32(rightArr[1]) : 0);
                    waterConf.RightCoordinates.Add(rightPoint);
                }

                this.WaterAreas.Add(waterConf);

                riverIndex++;
            }

            MainForm.ProgressReset();
        }

        private MagickImage waterTexture = null;
        private MagickImage lavaTexture = null;

        private MagickImage GetWateryTexture()
        {
            if (this.waterTexture != null) return this.waterTexture;

            var textureFile = string.Format("{0}\\data\\textures\\watery.dds", System.Windows.Forms.Application.StartupPath);
            
            var tex = new MagickImage(textureFile);
            tex.ColorSpace = ColorSpace.Gray;
            tex.Normalize();
            tex.Evaluate(Channels.RGB, EvaluateOperator.Multiply, 0.3);
            tex.Evaluate(Channels.RGB, EvaluateOperator.Add, Quantum.Max * 0.7);
            // Back to RGB, otherwise the tint is lost
            tex.ColorSpace = ColorSpace.sRGB;

            this.waterTexture = tex;
            return this.waterTexture;
        }

        private MagickImage GetLavaTexture()
        {
            if (this.lavaTexture != null) return this.lavaTexture;

            var textureFile = string.Format("{0}\\data\\textures\\lava.dds", System.Windows.Forms.Application.StartupPath);

            var tex = new MagickImage(textureFile);
            //tex.ColorSpace = ColorSpace.GRAY;

            this.lavaTexture = tex;
            return this.lavaTexture;
        }

        public void Draw(MagickImage map)
        {
            MainForm.ProgressStart("Rendering water ...");


            using (IPixelCollection<ushort> heightmapPixels = this.zoneConfiguration.Heightmap.HeightmapScaled.GetPixelsUnsafe())
            {
                using (var water = MagickWrapper.NewImage(MagickColors.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
                {
                    var progressCounter = 0;

                    foreach (var river in this.WaterAreas)
                    {
                        MainForm.Log(river.Name + "...", MainForm.LogLevel.Notice);

                        MagickColor fillColor;
                        if (this.UseClientColors) fillColor = river.Color.ToMagickColor();
                        else fillColor = this.WaterColor.ToMagickColor();
                        //water.FillColor = fillColor;

                        // Get the river coordinates and scale them to the targets size
                        var riverCoordinates = river.GetCoordinates().Select(c => new PointD(c.X * this.zoneConfiguration.MapScale, c.Y * this.zoneConfiguration.MapScale)).ToList();

                        // Texture
                        var isLava = river.Type.ToLower() == "lava";
                        using (var texture = new MagickImage(isLava ? this.GetLavaTexture() : this.GetWateryTexture()))
                        {
                            using (var pattern = new MagickImage(fillColor, texture.Width, texture.Height))
                            {
                                texture.Composite(pattern, 0, 0, CompositeOperator.DstIn);
                                texture.Composite(pattern, 0, 0, isLava ? CompositeOperator.ColorDodge : CompositeOperator.Multiply);

                                water.Settings.FillPattern = texture;
                                var poly = new DrawablePolygon(riverCoordinates);
                                water.Draw(poly);
                            }
                        }

                        // get the min/max and just process them
                        var minX = Convert.ToInt32(riverCoordinates.Min(m => m.X)) - 10;
                        var maxX = Convert.ToInt32(riverCoordinates.Max(m => m.X)) + 10;
                        var minY = Convert.ToInt32(riverCoordinates.Min(m => m.Y)) - 10;
                        var maxY = Convert.ToInt32(riverCoordinates.Max(m => m.Y)) + 10;

                        using (IPixelCollection<ushort> riverPixelCollection = water.GetPixelsUnsafe())
                        {
                            for (var x = minX; x < maxX; x++)
                            {
                                if (x < 0) continue;
                                if (x >= this.zoneConfiguration.TargetMapSize) continue;

                                for (var y = minY; y < maxY; y++)
                                {
                                    if (y < 0) continue;
                                    if (y >= this.zoneConfiguration.TargetMapSize) continue;

                                    var pixelHeight = heightmapPixels.GetPixel(x, y).GetChannel(0);
                                    if (pixelHeight > river.Height)
                                    {
                                        riverPixelCollection.SetPixel(new Pixel(x, y, new ushort[] { 0, 0, 0, ushort.MinValue }));
                                    }
                                }
                            }
                        }
                        

                        if (this.debug)
                        {
                            this.DebugRiver(progressCounter, river, riverCoordinates);
                        }

                        var percent = 100 * progressCounter / this.WaterAreas.Count();
                        MainForm.ProgressUpdate(percent);
                        progressCounter++;
                    }

                    MainForm.ProgressStartMarquee("Merging...");

                    if (this.WaterTransparency != 0)
                    {
                        water.Alpha(AlphaOption.Set);
                        var divideValue = 100.0 / (100.0 - this.WaterTransparency);
                        water.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                    }

                    water.Blur();
                    map.Composite(water, 0, 0, CompositeOperator.SrcOver);
                }
            }
            

            MainForm.ProgressReset();
        }

        private void DebugRiver(int index, WaterConfiguration river, List<PointD> riverCoordinates)
        {
            var debugFilename = string.Format("{0}\\debug\\rivers\\{1}_{2}_{3}.jpg", System.Windows.Forms.Application.StartupPath, this.zoneConfiguration.ZoneId, index, river.Name);

            if (index == 0)
            {
                var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(debugFilename));
                if (directoryInfo.Exists) directoryInfo.EnumerateFiles().ToList().ForEach(f => f.Delete());
                else directoryInfo.Create();
            }

            using (var debugRiver = MagickWrapper.NewImage(MagickColors.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
            {
                debugRiver.BackgroundColor = MagickColors.White;
                debugRiver.Settings.FillColor = new MagickColor(0, 0, ushort.MaxValue, 256 * 128);

                double resizeFactor = this.zoneConfiguration.TargetMapSize / this.zoneConfiguration.Heightmap.Heightmap.Width;

                var poly = new DrawablePolygon(riverCoordinates);
                debugRiver.Draw(poly);
                
                var originalCoordinates = river.GetCoordinates();
                for (var i = 0; i < riverCoordinates.Count(); i++)
                {
                    double x, y;

                    if (riverCoordinates[i].X > this.zoneConfiguration.TargetMapSize / 2) x = riverCoordinates[i].X - 15;
                    else x = riverCoordinates[i].X + 1;

                    if (riverCoordinates[i].Y < this.zoneConfiguration.TargetMapSize / 2) y = riverCoordinates[i].Y + 15;
                    else y = riverCoordinates[i].Y - 1;

                    debugRiver.Settings.FontPointsize = 14.0;
                    debugRiver.Settings.FillColor = MagickColors.Black;
                    var text = new DrawableText(x, y, string.Format("{0} ({1}/{2})", i, originalCoordinates[i].X, originalCoordinates[i].Y));
                    debugRiver.Draw(text);
                }

                debugRiver.Quality = 100;
                debugRiver.Write(debugFilename);
            }
        }
        
    }
}
