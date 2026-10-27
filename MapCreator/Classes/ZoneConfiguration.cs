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
        private const int PCX_HEADER_SIZE = 128;
        private const byte PCX_PALETTE_MARKER = 0x0C;

        public string ZoneId { get; }

        public GameExpansion Expansion { get; }

        public string ZoneDirectory { get; }

        public DatFile SectorDat { get; }

        public int TargetMapSize { get; } = 1024;

        /// <summary>
        /// Side of the square the map shows, in world units. 65536 for outdoor zones, the city frame for cities.
        /// </summary>
        public double ZoneSize { get; private set; } = ZONE_MAX_COORDINATE;

        /// <summary>
        /// Zone coordinates a city or dungeon map shows, set when its models are placed
        /// </summary>
        public MapFrame Frame { get; set; }

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
        public bool ObliqueKeeps { get; set; }
        public bool AmbientOcclusion { get; set; }

        /// <summary>
        /// Outdoor buildings and trees drawn in 3D like the keeps
        /// </summary>
        public bool ObliqueBuildings { get; set; }
        public bool ObliqueTrees { get; set; }

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

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> loggedWarnings = new();

        /// <summary>
        /// Logs a warning once per zone; models are placed many times
        /// </summary>
        public void WarnOnce(string message)
        {
            if (this.loggedWarnings.TryAdd(message, 0))
            {
                this.Reporter.Log(message, LogLevel.Warning);
            }
        }

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
                this.Levels = MapLevel.Load(AppSettings.Current.GamePath, zoneId);
            }

            // Missing or empty archives map to datXXX.mpk (Blackthorn ships an empty csv009.mpk and keeps the CSVs in dat009.mpk)
            this.CvsMpk = OrDat(this.CvsMpk);
            this.LodMpk = OrDat(this.LodMpk);
            this.TerMpk = OrDat(this.TerMpk);
            this.TexMpk = OrDat(this.TexMpk);

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
                    zoneDataDirectory = string.Format("{0}\\phousing\\zones\\zone{1}", AppSettings.Current.GamePath, zoneId);
                    break;
                case GameExpansion.NewFrontiers:
                    zoneDataDirectory = string.Format("{0}\\frontiers\\zones\\zone{1}", AppSettings.Current.GamePath, zoneId);
                    break;
                case GameExpansion.Tutorial:
                    zoneDataDirectory = string.Format("{0}\\tutorial\\zones\\zone{1}", AppSettings.Current.GamePath, zoneId);
                    break;
                default:
                    zoneDataDirectory = string.Format("{0}\\zones\\zone{1}", AppSettings.Current.GamePath, zoneId);
                    break;
            }

            // Special path for tutorial zones
            if (zoneId == "027")
            {
                zoneDataDirectory = string.Format("{0}\\tutorial\\zones\\zone{1}", AppSettings.Current.GamePath, zoneId);
            }

            if (!File.Exists(Path.Combine(zoneDataDirectory, "dat" + zoneId + ".mpk")))
            {
                zoneDataDirectory = ZoneCatalog.FindZoneDirectory(AppSettings.Current.GamePath, zoneId) ?? zoneDataDirectory;
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
            var watermap = new ImageMagick.MagickImage(this.ReadPcx("water.pcx"));
            watermap.Resize((uint)(this.TargetMapSize), (uint)(this.TargetMapSize));

            return watermap;
        }

        public ImageMagick.MagickImage GetTerrainMap()
        {
            var terrainmap = new ImageMagick.MagickImage(this.ReadPcx("terrain.pcx"));
            return terrainmap;
        }

        public ImageMagick.MagickImage GetOffsetMap()
        {
            var offsetmap = new ImageMagick.MagickImage(this.ReadPcx("offset.pcx"));
            return offsetmap;
        }

        private byte[] ReadPcx(string name)
        {
            return CompletePcxPalette(MpkWrapper.GetFileBytesFromMpk(this.DatMpk, name));
        }

        // A palette marker without palette after the pixels (Blackthorn zone 113) gets the gray ramp the other maps carry
        internal static byte[] CompletePcxPalette(byte[] pcx)
        {
            if (pcx.Length <= PCX_HEADER_SIZE || pcx[3] != 8 || pcx[65] != 1)
            {
                return pcx;
            }

            var pixels = (pcx[67] << 8 | pcx[66]) * ((pcx[11] << 8 | pcx[10]) - (pcx[7] << 8 | pcx[6]) + 1);
            var position = PCX_HEADER_SIZE;
            for (var decoded = 0; decoded < pixels && position < pcx.Length; position++)
            {
                if (pcx[position] >= 0xC0)
                {
                    decoded += pcx[position] & 0x3F;
                    position++;
                }
                else
                {
                    decoded++;
                }
            }

            if (position != pcx.Length - 1 || pcx[position] != PCX_PALETTE_MARKER)
            {
                return pcx;
            }

            var completed = new byte[pcx.Length + 768];
            pcx.CopyTo(completed, 0);
            for (var i = 0; i < 256; i++)
            {
                completed.AsSpan(pcx.Length + 3 * i, 3).Fill((byte)i);
            }
            return completed;
        }

        private string OrDat(string archive)
        {
            return File.Exists(archive) && new FileInfo(archive).Length > 0 ? archive : this.DatMpk;
        }

        public void Dispose()
        {
            this.Heightmap?.Dispose();
            MapCreation.Fixtures.TextureCache.Release(this.ZoneDirectory);
            MapCreation.Fixtures.TextureColors.Release(this.ZoneDirectory);
        }
    }
}
