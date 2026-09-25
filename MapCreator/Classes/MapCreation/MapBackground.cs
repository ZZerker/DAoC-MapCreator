//
// MapCreator
// Copyright(C) 2015 Stefan Schäfer <merec@merec.org>
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
using System.Linq;
using ImageMagick;
using MPKLib;

namespace MapCreator.Classes.MapCreation
{
	internal class MapBackground
    {
        private readonly ZoneConfiguration zoneConfiguration;

        private readonly string textureZoneDataDirectory;

        private readonly string textureZoneId;

        private readonly bool flipX = false;

        private readonly bool flipY = false;

        public MapBackground(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.textureZoneId = zoneConfiguration.ZoneId;
            this.textureZoneDataDirectory = zoneConfiguration.ZoneDirectory;

            var flipX = zoneConfiguration.SectorDat.Get("terrain", "flip_x");
            var flipY = zoneConfiguration.SectorDat.Get("terrain", "flip_y");
            var useTexture = zoneConfiguration.SectorDat.Get("terrain", "use_texture");

            if (!string.IsNullOrEmpty(flipX)) this.flipX = (Convert.ToInt32(flipX) != 0) ? true : false;
            if (!string.IsNullOrEmpty(flipY)) this.flipY = (Convert.ToInt32(flipY) != 0) ? true : false;

            if (!string.IsNullOrEmpty(useTexture))
            {
                var useTextureInt = Convert.ToInt32(useTexture);
                useTexture = (useTextureInt < 10) ? "00" + useTextureInt : (useTextureInt < 100) ? "0" + useTextureInt : useTextureInt.ToString();
                this.textureZoneId = useTexture;
                this.textureZoneDataDirectory = zoneConfiguration.GetZoneDirectory(useTexture);
            }
        }

        public bool DrawBackground { get; set; } = true;

        public MagickImage Draw()
        {
            this.zoneConfiguration.Reporter.ProgressStart("Rendering background ...");

            if(!this.DrawBackground)
            {
                return MagickWrapper.NewImage(Color.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize);
            }

            // Check which terrain file is used
            var texMpk = string.Format("{0}\\tex{1}.mpk", this.textureZoneDataDirectory, this.textureZoneId);
            var lodMpk = string.Format("{0}\\lod{1}.mpk", this.textureZoneDataDirectory, this.textureZoneId);

            // Get the tile dimension
            var tileWidth = 512.0;
            var tileTemplate = "";

            MPAK mpak = null;
            if (File.Exists(texMpk))
            {
                mpak = MpkWrapper.Open(texMpk);

                if (mpak.Files.Any(f => f.Name.ToLower() == "tex00-00.dds"))
                {
                    tileTemplate += "tex0{0}-0{1}.dds";
                    tileWidth = 512.0;
                }
            }

            if (string.IsNullOrEmpty(tileTemplate) && File.Exists(lodMpk))
            {
                mpak = MpkWrapper.Open(lodMpk);

                if (mpak.Files.Any(f => f.Name.ToLower() == "lod00-00.dds"))
                {
                    tileTemplate += "lod0{0}-0{1}.dds";
                    tileWidth = 256.0;
                }
            }

            if (string.IsNullOrEmpty(tileTemplate))
            {
                this.zoneConfiguration.Reporter.Log(string.Format("Zone {0}: No background textures found!", this.zoneConfiguration.ZoneId), LogLevel.Error);
                return null;
            }

            // original size
            var orginalWidth = tileWidth * 8;
            var resizeFactor = (double)this.zoneConfiguration.TargetMapSize / (double)orginalWidth; // 0 - 1

            var map = MagickWrapper.NewImage(Color.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize);

            for (var col = 0; col <= 7; col++)
            {
                var x = TileEdge(col, tileWidth, resizeFactor);
                var width = TileEdge(col + 1, tileWidth, resizeFactor) - x;

                for (var row = 0; row <= 7; row++)
                {
                    var y = TileEdge(row, tileWidth, resizeFactor);
                    var height = TileEdge(row + 1, tileWidth, resizeFactor) - y;
                    var filename = string.Format(tileTemplate, col, row);

                    using (var mapTile = new MagickImage(mpak.GetFile(filename).Data))
                    {
                        mapTile.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                        map.Composite(mapTile, x, y, CompositeOperator.SrcOver);
                    }
                }

                var percent = 100 * col / 8;
                this.zoneConfiguration.Reporter.ProgressUpdate(percent);
            }

            this.zoneConfiguration.Reporter.ProgressStartMarquee("Merging ...");

            // Flip if set
            if (this.flipX) map.Flop();
            if (this.flipY) map.Flip();

            // Light sharpening after the downscale; a strong one turns the ground textures into noise
            map.Sharpen(0, 1.0);

            this.zoneConfiguration.Reporter.ProgressReset();

            return map;
        }

        // Tiles are placed on exact edges so non power of two sizes leave no gaps
        private static int TileEdge(int index, double tileWidth, double resizeFactor)
        {
            return (int)Math.Round(index * tileWidth * resizeFactor);
        }
    }
}
