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
using System.IO;
using ImageMagick;

namespace MapCreator.Classes.MapCreation
{
    public class MapHeightmap: IDisposable
    {
        private readonly ZoneConfiguration zoneConfiguration;
        private readonly int terrainfactor = 1;
        private readonly int offsetfactor = 1;
        private readonly FileInfo heightmapFile;
        bool heightmapGenerated = false;

        private MagickImage heightmap = null;

        internal MagickImage Heightmap => this.heightmap;

        private MagickImage heightmapScaled = null;

        internal MagickImage HeightmapScaled => this.heightmapScaled;

        public MapHeightmap(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.zoneConfiguration.Reporter.Log("Preloading zone heightmap ...", LogLevel.Notice);
            this.terrainfactor = Convert.ToInt32(zoneConfiguration.SectorDat.Get("terrain", "scalefactor"));
            this.offsetfactor = Convert.ToInt32(zoneConfiguration.SectorDat.Get("terrain", "offsetfactor"));
            this.heightmapFile = new FileInfo(string.Format("{0}\\data\\heightmaps\\zone{1}_heightmap.png", System.Windows.Forms.Application.StartupPath, zoneConfiguration.ZoneId));

            if (!System.IO.Directory.Exists(this.heightmapFile.DirectoryName))
            {
                Directory.CreateDirectory(this.heightmapFile.DirectoryName);
            }

            // Generate it, needed for lightmap, river and fixtures
            this.GenerateHeightmap();
        }

        private void GenerateHeightmap()
        {
            if(this.heightmapGenerated) return;
            this.zoneConfiguration.Reporter.ProgressStart("Processing heightmap ...");

            using (var offsetmap = this.zoneConfiguration.GetOffsetMap())
            {
                using (var terrainmap = this.zoneConfiguration.GetTerrainMap())
                {
                    this.heightmap = MagickWrapper.NewImage(Color.Black, (int)offsetmap.Width, (int)offsetmap.Height);

                    using (var heightmapPixels = this.heightmap.GetPixels())
                    {
                        var terrainPixels = terrainmap.GetPixels();
                        var offsetPixels = offsetmap.GetPixels();

                        for (var x = 0; x < offsetmap.Width; x++)
                        {
                            for (var y = 0; y < offsetmap.Height; y++)
                            {
                                var terrainPixelValue = (ushort)(terrainPixels[x, y].GetChannel(0) / 256);
                                var offsetPixelValue = (ushort)(offsetPixels[x, y].GetChannel(0) / 256);
                                var heightmapPixelValue = (ushort)(terrainPixelValue * this.terrainfactor + offsetPixelValue * this.offsetfactor);

                                heightmapPixels.SetPixel(x, y, new ushort[] { heightmapPixelValue, heightmapPixelValue, heightmapPixelValue });
                            }

                            var percent = 100 * x / (int)offsetmap.Width;
                            this.zoneConfiguration.Reporter.ProgressUpdate(percent);
                        }

                        //heightmapPixels.Write();    
                    }

                    this.zoneConfiguration.Reporter.ProgressStartMarquee("Merging ...");

                    this.heightmap.Quality = 100;
                    this.heightmap.Write(this.heightmapFile.FullName);

                    // Scale to target size
                    this.heightmapScaled = new MagickImage(this.heightmap);
                    this.heightmapScaled.Resize((uint)(this.zoneConfiguration.TargetMapSize), (uint)(this.zoneConfiguration.TargetMapSize));
                }
            }

            this.heightmapGenerated = true;
            this.zoneConfiguration.Reporter.ProgressReset();
        }

        /// <summary>
        /// Get the Height of a specified location
        /// </summary>
        /// <param name="locX"></param>
        /// <param name="locY"></param>
        /// <returns></returns>
        public double GetHeight(double locX, double locY)
        {
            var x = Convert.ToInt32(this.zoneConfiguration.ZoneCoordinateToMapCoordinate(locX));
            var y = Convert.ToInt32(this.zoneConfiguration.ZoneCoordinateToMapCoordinate(locY));
            
            if (x == this.zoneConfiguration.TargetMapSize) x -= 1;
            else if (x < 0) x = 0;
            
            if (y == this.zoneConfiguration.TargetMapSize) y -= 1;
            else if (y < 0) y = 0;

            return this.heightmapScaled.GetPixels().GetPixel(x, y).GetChannel(0);
        }

        public void Dispose()
        {
            if (this.heightmap != null) this.heightmap.Dispose();
            if (this.heightmapScaled != null) this.heightmapScaled.Dispose();
        }

    }
}
