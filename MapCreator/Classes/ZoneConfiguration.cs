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
using System.Collections.Generic;
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

        /// <summary>
        /// Side of the square the map shows, in world units. 65536 for outdoor zones, the city frame for cities.
        /// </summary>
        public double ZoneSize { get; private set; } = ZONE_MAX_COORDINATE;

        public double LocScale { get; private set; } = 1;

        public double MapScale { get; } = 1;

        public double LocsPerLixel { get; private set; } = 1;

        /// <summary>
        /// Null for zones without terrain
        /// </summary>
        public MapHeightmap Heightmap { get; }

        public bool HasTerrain { get; }

        /// <summary>
        /// Capital cities are built from the models in city.csv instead of terrain and fixtures
        /// </summary>
        public bool IsCity { get; }

        /// <summary>
        /// Dungeons place room models from dungeon.chunk by dungeon.place
        /// </summary>
        public bool IsDungeon { get; }

        /// <summary>
        /// Name of the chunk and place files: dungeon, or skycity for instanced zones
        /// </summary>
        public string DungeonFiles { get; }

        /// <summary>
        /// Keeps and towers from data\Keeps.csv
        /// </summary>
        public bool DrawKeeps { get; set; } = true;

        /// <summary>
        /// Levels of a multi level dungeon from areas.dat, empty for all other zones
        /// </summary>
        public IReadOnlyList<MapLevel> Levels { get; } = new List<MapLevel>();

        #region MPK files
        public string DatMpk { get; }

        public string CvsMpk { get; }

        public string LodMpk { get; }

        public string TerMpk { get; }

        public string TexMpk { get; }
        #endregion

        public IRenderReporter Reporter { get; }

        public ZoneConfiguration(string zoneId, int mapSize, IRenderReporter reporter)
        {
            this.ZoneId = zoneId;
            this.Reporter = reporter;

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

            this.HasTerrain = MpkWrapper.ContainsFile(this.DatMpk, "terrain.pcx") && MpkWrapper.ContainsFile(this.DatMpk, "offset.pcx");
            this.IsCity = !this.HasTerrain && MpkWrapper.ContainsFile(this.DatMpk, "city.csv");
            this.DungeonFiles = MpkWrapper.ContainsFile(this.DatMpk, "skycity.place") ? "skycity" : "dungeon";
            this.IsDungeon = !this.HasTerrain && !this.IsCity && MpkWrapper.ContainsFile(this.DatMpk, this.DungeonFiles + ".place");
            if (!this.HasTerrain && !this.IsCity && !this.IsDungeon)
            {
                throw new NotSupportedException(string.Format("Zone {0} has no terrain, city or dungeon data, not supported yet.", zoneId));
            }

            if (this.IsDungeon)
            {
                this.Levels = MapLevel.Load(Properties.Settings.Default.game_path, zoneId);
            }

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
            this.SetZoneSize(ZONE_MAX_COORDINATE);

            if (this.HasTerrain)
            {
                this.Heightmap = new MapHeightmap(this);
            }
        }

        public void SetZoneSize(double zoneSize)
        {
            this.ZoneSize = zoneSize;
            this.LocsPerLixel = zoneSize / this.TargetMapSize;
            this.LocScale = this.TargetMapSize / zoneSize;
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

            if (!File.Exists(Path.Combine(zoneDataDirectory, "dat" + zoneId + ".mpk")))
            {
                zoneDataDirectory = ZoneCatalog.FindZoneDirectory(Properties.Settings.Default.game_path, zoneId) ?? zoneDataDirectory;
            }

            return zoneDataDirectory;
        }

        public double ZoneCoordinateToMapCoordinate(double zoneCoordinate)
        {
            return (this.TargetMapSize * zoneCoordinate) / this.ZoneSize;
        }

        public double MapCoordinateToZoneCoordinate(double mapCoordinate)
        {
            return (this.ZoneSize * mapCoordinate) / this.TargetMapSize;
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
            this.Heightmap?.Dispose();
        }
    }
}
