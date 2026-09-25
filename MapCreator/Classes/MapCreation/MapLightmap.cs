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

        // Blur of the 256 px heightmap before scaling, in heightmap pixels
        private const double HEIGHT_SMOOTHING = 1.0;

        private const double RELIEF_DIVISOR = 10.0;

        public double LightMin { get; set; } = 0.5;

        public double LightMax { get; set; } = 1.5;

        public double[] ZVector { get; set; } = new double[] { -1.0, 1, -1.0 };

        public MapLightmap(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.RecalculateLights();
        }

        public void RecalculateLights()
        {
            this.ZVector = Tools.NormalizeVector(this.ZVector);
        }

        /// <summary>
        /// Hillshade at full map size: slopes facing the light get brighter, slopes facing away darker,
        /// flat ground keeps its color. The factor is limited to LightMin and LightMax.
        /// </summary>
        public void Draw(MagickImage map)
        {
            this.zoneConfiguration.Reporter.ProgressStart("Drawing lightmap ...");

            var size = (int)map.Width;
            ushort[] heights;
            int heightChannels;

            // The heightmap has 8 bit steps; smoothed before scaling so they do not show as terraces
            using (var heightmap = (MagickImage)this.zoneConfiguration.Heightmap.Heightmap.Clone())
            {
                heightmap.Blur(0, HEIGHT_SMOOTHING);
                heightmap.FilterType = FilterType.Catrom;
                heightmap.Resize(new MagickGeometry((uint)size, (uint)size) { IgnoreAspectRatio = true });
                heightmap.Blur(0, size / 256d);
                heightChannels = (int)heightmap.ChannelCount;
                heights = heightmap.GetPixels().ToArray();
            }

            // The light travels along ZVector, so the sun lies the other way
            var sunX = -this.ZVector[0];
            var sunY = -this.ZVector[1];
            var sunZ = -this.ZVector[2];

            // Heightmap pixels are 256 units apart, differences span two of them. ZScale exaggerates the relief;
            // it was tuned for the old lightmap, a tenth of it gives a similar look (35: about 3.5 times)
            var nz = 512d / (this.ZScale / RELIEF_DIVISOR);
            var heightmapPixelsPerMapPixel = 256d / size;

            using (var pixels = map.GetPixels())
            {
                var channels = (int)map.ChannelCount;
                var values = pixels.ToArray();

                System.Threading.Tasks.Parallel.For(0, size, y =>
                {
                    var up = Math.Max(y - 1, 0);
                    var down = Math.Min(y + 1, size - 1);
                    for (var x = 0; x < size; x++)
                    {
                        var left = Math.Max(x - 1, 0);
                        var right = Math.Min(x + 1, size - 1);

                        var nx = (Height(heights, heightChannels, size, left, y) - Height(heights, heightChannels, size, right, y)) / (heightmapPixelsPerMapPixel * (right - left));
                        var ny = (Height(heights, heightChannels, size, x, up) - Height(heights, heightChannels, size, x, down)) / (heightmapPixelsPerMapPixel * (down - up));
                        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        var light = (nx * sunX + ny * sunY + nz * sunZ) / length / sunZ;
                        var factor = Math.Clamp(light, this.LightMin, this.LightMax);

                        var index = (y * size + x) * channels;
                        for (var c = 0; c < Math.Min(channels, 3); c++)
                        {
                            values[index + c] = (ushort)Math.Min(values[index + c] * factor, ushort.MaxValue);
                        }
                    }
                });

                pixels.SetPixels(values);
            }

            this.zoneConfiguration.Reporter.ProgressReset();
        }

        private static double Height(ushort[] heights, int channels, int size, int x, int y)
        {
            return heights[(y * size + x) * channels];
        }
    }
}
