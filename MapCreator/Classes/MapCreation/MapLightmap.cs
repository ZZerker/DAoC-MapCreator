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
using System.Drawing;
using ImageMagick;

namespace MapCreator.Classes.MapCreation
{

    /// <summary>
    /// Direct conversion of MapperGuis BumpmapRender.py
    /// </summary>
    internal class MapLightmap
    {
        private readonly ZoneConfiguration zoneConfiguration;

        public double ZScale { get; set; } = 20.0;

        public double LightMin { get; set; } = 0.5;

        public double LightMax { get; set; } = 1.5;

        public double[] ZVector { get; set; } = new double[] { -1.0, 1, -1.0 };

        private double lightBase;
        private double lightScale;

        public MapLightmap(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.RecalculateLights();
        }

        public void RecalculateLights()
        {
            // Set vector and lights
            this.ZVector = Tools.NormalizeVector(this.ZVector);

            var lminScaled = this.LightMin / this.LightMax;
            var baseLight = 255 * lminScaled;
            this.lightBase = baseLight + (255 - baseLight) / 2;
            this.lightScale = 255 - this.lightBase;
        }

        public void Draw(MagickImage map)
        {
            this.zoneConfiguration.Reporter.ProgressStart("Drawing lightmap ...");

            // Get the heightmap
            var heightmap = this.zoneConfiguration.Heightmap.Heightmap;

            using (var lightmap = MagickWrapper.NewImage(Color.Transparent, 256, 256))
            {
                using (var heightmapPixels = heightmap.GetPixels())
                {
                    using (var lightmapPixels = lightmap.GetPixels())
                    {
                        // z-component of surface normals
                        var nz = 512d / this.ZScale;
                        var nz2 = nz * nz;
                        var nzlz = nz * this.ZVector[2];

                        for (var y = 0; y < lightmap.Height; y++)
                        {
	                        var y1 = 0;
	                        if (y == 0) y1 = 0;
                            else y1 = y - 1;
                            var y2 = 0;
                            if (y == 255) y2 = 255;
                            else y2 = y + 1;

                            for (var x = 0; x < lightmap.Width; x++)
                            {
	                            var x1 = 0;
	                            if (x == 0) x1 = 0;
                                else x1 = x - 1;
	                            var x2 = 0;
	                            if (x == 255) x2 = 255;
                                else x2 = x + 1;

                                double l = heightmapPixels.GetPixel(x1, y).GetChannel(0);
                                double r = heightmapPixels.GetPixel(x2, y).GetChannel(0);
                                double u = heightmapPixels.GetPixel(x, y1).GetChannel(0);
                                double d = heightmapPixels.GetPixel(x, y2).GetChannel(0);

                                var nx = l - r;
                                var ny = u - d;

                                var normal = Math.Sqrt(nx * nx + ny * ny + nz2);
                                var ndotl = (nx * this.ZVector[0] + ny * this.ZVector[1] + nzlz) / normal;

                                var pixelValue = this.lightBase - ndotl * this.lightScale * 256d;

                                ushort pixelValueDiff = 0;
                                var alphaValue = ushort.MaxValue;
                                if(pixelValue < 0)
                                {
                                    pixelValueDiff = 0;
                                    alphaValue = (ushort)pixelValue;
                                }
                                else
                                {
                                    pixelValueDiff = (ushort)pixelValue;
                                }

                                // ColorDodge map
                                // white lightens areas where black does nothing
                                // alpha darkens areas
                                lightmapPixels.SetPixel(x, y, new ushort[] { pixelValueDiff, pixelValueDiff, pixelValueDiff, alphaValue });
                            }

                            var percent = 100 * y / (int)lightmap.Height;
                            this.zoneConfiguration.Reporter.ProgressUpdate(percent);
                        }
                    }
                }

                this.zoneConfiguration.Reporter.ProgressStartMarquee("Merging...");
                lightmap.Blur(0.0, 0.5);

                lightmap.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                lightmap.FilterType = FilterType.Gaussian;
                lightmap.Resize((uint)(this.zoneConfiguration.TargetMapSize), (uint)(this.zoneConfiguration.TargetMapSize));

                // Apply the bumpmap using ColorDodge
                map.Composite(lightmap, 0, 0, CompositeOperator.ColorDodge);

                this.zoneConfiguration.Reporter.ProgressReset();
            }
        }
    }
}
