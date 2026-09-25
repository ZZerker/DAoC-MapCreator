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
using System.IO;
using MapCreator.Classes.MapCreation;

namespace MapCreator.Classes
{
    public class ZoneConfiguration : IDisposable
    {
        const double ZONE_MAX_COORDINATE = 65536.0;

        public string ZoneId { get; }

        public GameExpansion Expansion { get; }

        public string ZoneDirectory { get; }

        public DatFile SectorDat { get; }

        public int TargetMapSize { get; } = 1024;

        public double LocScale { get; } = 1;

        public double MapScale { get; } = 1;

        public double LocsPerLixel { get; } = 1;

        public MapHeightmap Heightmap { get; }

        #region MPK files
        public string DatMpk { get; }

        public string CvsMpk { get; }

        public string LodMpk { get; }

        public string TerMpk { get; }

        public string TexMpk { get; }
        #endregion

        public ZoneConfiguration(string zoneId, int mapSize)
        {
            this.ZoneId = zoneId;

            // Get expansion from zones.xml
            this.Expansion = DataWrapper.GetExpansionByZone(zoneId);

            // Zone Directory
            this.ZoneDirectory = this.GetZoneDirectory();

            // Some mpk files
            this.DatMpk = string.Format("{0}\\dat{1}.mpk", this.ZoneDirectory, this.ZoneId);
            this.CvsMpk = string.Format("{0}\\csv{1}.mpk", this.ZoneDirectory, this.ZoneId);
            this.LodMpk = string.Format("{0}\\lod{1}.mpk", this.ZoneDirectory, this.ZoneId);
            this.TerMpk = string.Format("{0}\\ter{1}.mpk", this.ZoneDirectory, this.ZoneId);
            this.TexMpk = string.Format("{0}\\tex{1}.mpk", this.ZoneDirectory, this.ZoneId);

            // Check if the zone gets its data from an other zone

            // Check if file exists, else map to datXXX.mpk
            if (!File.Exists(this.CvsMpk)) this.CvsMpk = this.DatMpk;
            if (!File.Exists(this.LodMpk)) this.LodMpk = this.DatMpk;
            if (!File.Exists(this.TerMpk)) this.TerMpk = this.DatMpk;
            if (!File.Exists(this.TexMpk)) this.TexMpk = this.DatMpk;

            // Useful for everything, so open it
            this.SectorDat = DatFile.FromMpk(this.DatMpk, "sector.dat");

            // for math
            this.TargetMapSize = mapSize;
            this.MapScale = this.TargetMapSize / 256.0;
            this.LocsPerLixel = ZONE_MAX_COORDINATE / mapSize;
            this.LocScale = this.TargetMapSize / ZONE_MAX_COORDINATE;

            // Heightmap
            this.Heightmap = new MapHeightmap(this);
        }

        public string GetZoneDirectory(string zoneId = null)
        {
            if (string.IsNullOrEmpty(zoneId)) zoneId = this.ZoneId;
            var expansion = DataWrapper.GetExpansionByZone(zoneId);
            var zoneDataDirectory = "";

            switch (expansion)
            {
                case GameExpansion.Foundations:
                    zoneDataDirectory = string.Format("{0}\\phousing\\zones\\zone{1}", Properties.Settings.Default.game_path, zoneId);
                    break;
                case GameExpansion.NewFrontiers:
                    zoneDataDirectory = string.Format("{0}\\frontiers\\zones\\zone{1}", Properties.Settings.Default.game_path, zoneId);
                    break;
                case GameExpansion.Tutorial:
                    zoneDataDirectory = string.Format("{0}\\tutorial\\zones\\zone{1}", Properties.Settings.Default.game_path, zoneId);
                    break;
                default:
                    zoneDataDirectory = string.Format("{0}\\zones\\zone{1}", Properties.Settings.Default.game_path, zoneId);
                    break;
            }

            // Special path for tutorial zones
            if (zoneId == "027")
            {
                zoneDataDirectory = string.Format("{0}\\tutorial\\zones\\zone{1}", Properties.Settings.Default.game_path, zoneId);
            }

            return zoneDataDirectory;
        }

        public double ZoneCoordinateToMapCoordinate(double zoneCoordinate)
        {
            return (this.TargetMapSize * zoneCoordinate) / ZONE_MAX_COORDINATE;
        }

        public double MapCoordinateToZoneCoordinate(double mapCoordinate)
        {
            return (ZONE_MAX_COORDINATE * mapCoordinate) / this.TargetMapSize;
        }

        public ImageMagick.MagickImage GetWaterMap()
        {
            var watermap = new ImageMagick.MagickImage(MpkWrapper.GetFileBytesFromMpk(this.DatMpk, "water.pcx"));
            watermap.Resize((uint)(this.TargetMapSize), (uint)(this.TargetMapSize));

            return watermap;
        }

        public ImageMagick.MagickImage GetTerrainMap()
        {
            var terrainmap = new ImageMagick.MagickImage(MpkWrapper.GetFileBytesFromMpk(this.DatMpk, "terrain.pcx"));
            return terrainmap;
        }

        public ImageMagick.MagickImage GetOffsetMap()
        {
            var offsetmap = new ImageMagick.MagickImage(MpkWrapper.GetFileBytesFromMpk(this.DatMpk, "offset.pcx"));
            return offsetmap;
        }

        public void Dispose()
        {
            this.Heightmap.Dispose();
        }
    }
}
