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
using System.IO;
using System.Linq;
using MapCreator.Classes.MapCreation.Fixtures.Objects;
using NifUtil.Objects;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Loads the fixtures of one zone
    /// </summary>
	internal sealed class FixturesLoader
    {
        private readonly ZoneConfiguration zoneConf;

        private readonly List<FixtureRow> fixtureRows = new List<FixtureRow>();

        // Location where to search for npk with nifs
        private readonly List<string> nifSearchPaths;

        internal List<NifRow> NifRows { get; } = new List<NifRow>();

        public FixturesLoader(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConf = zoneConfiguration;

            var gamePath = Properties.Settings.Default.game_path;
            this.nifSearchPaths = new List<string>
                                  {
                                      Path.Combine(gamePath, "Newtowns\\zones\\Nifs"),
                                      Path.Combine(this.zoneConf.ZoneDirectory, "nifs"),
                                      Path.Combine(gamePath, "zones\\Nifs"),
                                      Path.Combine(gamePath, "frontiers\\NIFS"),
                                      Path.Combine(gamePath, "phousing\\nifs"),
                                      Path.Combine(gamePath, "Tutorial\\zones\\nifs"),
                                      Path.Combine(gamePath, "pregame"),
                                      Path.Combine(gamePath, "zones\\Dnifs")
                                  };

            this.textureProxies = LoadProxies(this.zoneConf.DatMpk, "TEXPROXY.csv");

            if (this.zoneConf.IsCity)
            {
                this.LoadCityData();
                this.ApplyModelProxies();
                this.LoadPolygons();
                this.PlaceCityPieces();
            }
            else if (this.zoneConf.IsDungeon)
            {
                this.LoadDungeonData();
                this.ApplyModelProxies();
                this.LoadPolygons();
                this.PlaceDungeonPieces();
            }
            else
            {
                this.LoadCsvData();
                this.ApplyModelProxies();
                if (this.zoneConf.DrawKeeps)
                {
                    this.LoadKeepPieces();
                }
                this.LoadPolygons();
            }
        }

        private void LoadKeepPieces()
        {
            foreach (var piece in KeepPieces.Load(this.zoneConf.ZoneId, this.zoneConf.Reporter))
            {
                if (!this.NifRows.Contains(piece.Nif))
                {
                    this.NifRows.Add(piece.Nif);
                }

                // Clockwise on the map; model y points north
                this.placementRotations[piece.Fixture.Id] = System.Numerics.Matrix4x4.CreateRotationZ(-(float)(piece.Heading * Math.PI / 180d));
                this.placementHeights[piece.Fixture.Id] = piece.Fixture.Z;
                this.fixtureRows.Add(piece.Fixture);
            }
        }

        /// <summary>
        /// Trees without a texture in Treemap.csv (BigHibTree, mightyoak) take the color of the texture most of their model uses
        /// </summary>
        private static void SetModelTreeColor(TreeRow tree, NifRow nifRow)
        {
            var texture = nifRow.Polygons?.Where(p => p.Texture != null).GroupBy(p => p.Texture, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            var color = texture == null ? null : TextureColors.Get(texture, nifRow.ArchiveDirectory);
            if (color != null)
            {
                tree.AverageColor = color.Value;
                tree.HasTextureColor = true;
            }
        }

        // Textures the client loads in place of others in this zone, by file name without extension
        private readonly Dictionary<string, string> textureProxies;

        /// <summary>
        /// NIFPROXY.csv and TEXPROXY.csv in the dat archive name replacements the client loads instead:
        /// original, replacement, flag (Caer Sidi only ships the ALTSTONEHall models of its STONEHall rooms)
        /// </summary>
        private static Dictionary<string, string> LoadProxies(string mpk, string filename)
        {
            var proxies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!MpkWrapper.ContainsFile(mpk, filename))
            {
                return proxies;
            }

            foreach (var row in DataWrapper.GetFileContent(mpk, filename))
            {
                var fields = row.Split(',').Select(f => f.Trim()).ToArray();
                if (fields.Length >= 2 && fields[0].Length > 0 && fields[1].Length > 0)
                {
                    proxies[Path.GetFileNameWithoutExtension(fields[0])] = fields[1];
                }
            }
            return proxies;
        }

        private void ApplyModelProxies()
        {
            var modelProxies = LoadProxies(this.zoneConf.DatMpk, "NIFPROXY.csv");
            foreach (var nifRow in this.NifRows)
            {
                if (nifRow.Filename != null && modelProxies.TryGetValue(Path.GetFileNameWithoutExtension(nifRow.Filename), out var replacement))
                {
                    nifRow.Filename = replacement;
                }
            }
        }

        // Empty border around the city, as a share of its size
        private const double CITY_MARGIN = 0.02;

        // Rotation of each dungeon placement by fixture row id
        private readonly Dictionary<int, System.Numerics.Matrix4x4> placementRotations = new Dictionary<int, System.Numerics.Matrix4x4>();

        // Placement height by fixture row id; drawing overwrites FixtureRow.Z, so every level starts from here
        private readonly Dictionary<int, double> placementHeights = new Dictionary<int, double>();

        // Zone heights are dungeon.place heights plus this (every zone jump of Darkness Falls sits 16000 above its hall)
        private const double DUNGEON_Z_OFFSET = 16000;

        /// <summary>
        /// Level to draw, null for the whole zone
        /// </summary>
        public MapLevel Level { get; set; }

        /// <summary>
        /// Replaces the model bounds frame by the zone's known map frame, in model coordinates
        /// </summary>
        private void ApplyMapFrame(ref double side, ref double left, ref double bottom)
        {
            var frame = MapFrame.Get(this.zoneConf.ZoneId);
            if (frame == null)
            {
                return;
            }

            side = frame.Width;
            left = MapFrame.ORIGIN - frame.OffsetX - side;
            bottom = frame.OffsetY - MapFrame.ORIGIN;
        }

        /// <summary>
        /// Placement heights a level shows. The lowest level also takes everything below it and the highest
        /// everything above, as on the client maps (Trollheim's top halls, the high halls of Darkness Falls).
        /// </summary>
        private (double Bottom, double Top) GetHeightBand(MapLevel level)
        {
            var bottom = level.Z == this.zoneConf.Levels.Min(l => l.Z) ? double.MinValue : level.Z - DUNGEON_Z_OFFSET;
            var top = level.Z + level.Depth == this.zoneConf.Levels.Max(l => l.Z + l.Depth) ? double.MaxValue : level.Z + level.Depth - DUNGEON_Z_OFFSET;
            return (bottom, top);
        }

        // On the client maps city and dungeon x grows to the left and y downwards
        private static readonly System.Numerics.Matrix4x4 TurnAroundMatrix = System.Numerics.Matrix4x4.CreateRotationZ((float)Math.PI);

        /// <summary>
        /// dungeon.chunk lists the room models by index, dungeon.place places them:
        /// chunk, x, y, z, angle in radians, rotation axis x, y, z, flags
        /// </summary>
        private void LoadDungeonData()
        {
            var chunks = DataWrapper.GetFileContent(this.zoneConf.DatMpk, this.zoneConf.DungeonFiles + ".chunk").Select(c => c.Trim()).ToList();
            for (var i = 0; i < chunks.Count; i++)
            {
                if (!string.IsNullOrEmpty(chunks[i]))
                {
                    this.NifRows.Add(new NifRow { NifId = i, TextualName = Path.GetFileNameWithoutExtension(chunks[i]), Filename = chunks[i] });
                }
            }

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var id = 0;
            foreach (var row in DataWrapper.GetFileContent(this.zoneConf.DatMpk, this.zoneConf.DungeonFiles + ".place"))
            {
                var fields = row.Split(',').Select(f => f.Trim()).ToArray();
                if (fields.Length < 8 || !int.TryParse(fields[0], out var chunk))
                {
                    continue;
                }

                var values = fields.Skip(1).Take(7).Select(f => double.Parse(f, culture)).ToArray();
                var axis = new System.Numerics.Vector3((float)values[4], (float)values[5], (float)values[6]);
                // The source angle uses the opposite sign (checked on the curved halls of Keltoi Fogou)
                var rotation = values[3] == 0 || axis.LengthSquared() == 0 ? System.Numerics.Matrix4x4.Identity : System.Numerics.Matrix4x4.CreateFromAxisAngle(Normalize(axis), -(float)values[3]);

                this.placementRotations[id] = rotation;
                this.placementHeights[id] = values[2];
                this.fixtureRows.Add(new FixtureRow { Id = id, NifId = chunk, TextualName = chunks.ElementAtOrDefault(chunk), X = values[0], Y = values[1], Z = values[2], Scale = 100 });
                id++;
            }
        }

        /// <summary>
        /// Frames the dungeon by the bounds of all placed rooms and converts the placements into map positions
        /// </summary>
        private void PlaceDungeonPieces()
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var fixtureRow in this.fixtureRows)
            {
                var nifRow = this.NifRows.FirstOrDefault(n => n.NifId == fixtureRow.NifId);
                if (nifRow?.Polygons == null || nifRow.Polygons.Length == 0)
                {
                    continue;
                }

                foreach (var vector in nifRow.Polygons.SelectMany(p => p.Vectors))
                {
                    var placed = Niflib.NumericsTransform.TransformCoordinate(vector, this.placementRotations[fixtureRow.Id]);
                    minX = Math.Min(minX, placed.X + fixtureRow.X);
                    maxX = Math.Max(maxX, placed.X + fixtureRow.X);
                    minY = Math.Min(minY, placed.Y + fixtureRow.Y);
                    maxY = Math.Max(maxY, placed.Y + fixtureRow.Y);
                }
            }

            if (minX > maxX)
            {
                return;
            }

            // Without a known frame: areas.dat gives the size of the client maps (Darkness Falls: the extent of all rooms)
            var extent = Math.Max(maxX - minX, maxY - minY);
            var side = this.zoneConf.Levels.Count > 0 ? Math.Max(extent, this.zoneConf.Levels.Max(l => l.Width)) : extent * (1 + 2 * CITY_MARGIN);
            var left = (minX + maxX) / 2d - side / 2d;
            var bottom = (minY + maxY) / 2d - side / 2d;
            this.ApplyMapFrame(ref side, ref left, ref bottom);
            this.zoneConf.SetZoneSize(side);
            this.zoneConf.Reporter.Log(string.Format("Dungeon frame: x {0:F0} to {1:F0}, y {2:F0} to {3:F0}", left, left + side, bottom, bottom + side), LogLevel.Notice);

            foreach (var fixtureRow in this.fixtureRows)
            {
                this.placementRotations[fixtureRow.Id] = Niflib.NumericsTransform.Multiply(this.placementRotations[fixtureRow.Id], TurnAroundMatrix);
                fixtureRow.X = left + side - fixtureRow.X;
                fixtureRow.Y = fixtureRow.Y - bottom;
            }
        }

        /// <summary>
        /// city.csv lists the city models; each one is placed once
        /// </summary>
        private void LoadCityData()
        {
            foreach (var row in DataWrapper.GetFileContent(this.zoneConf.DatMpk, "city.csv"))
            {
                var fields = row.Split(',');
                if (fields.Length < 2 || !int.TryParse(fields[0], out var id) || string.IsNullOrWhiteSpace(fields[1]))
                {
                    continue;
                }

                var filename = fields[1].Trim();
                this.NifRows.Add(new NifRow { NifId = id, TextualName = Path.GetFileNameWithoutExtension(filename), Filename = filename });
                this.fixtureRows.Add(new FixtureRow { Id = id, NifId = id, TextualName = filename, Scale = 100 });
            }
        }

        /// <summary>
        /// City models share one coordinate system with the north up. The map frame comes from MapFrames.csv, else it is the square around all of them;
        /// each model is centered on its own bounds so its canvas stays small.
        /// </summary>
        private void PlaceCityPieces()
        {
            var vectors = this.NifRows.Where(n => n.Polygons != null).SelectMany(n => n.Polygons).SelectMany(p => p.Vectors).ToList();
            if (vectors.Count == 0)
            {
                return;
            }

            var minX = vectors.Min(v => v.X);
            var maxX = vectors.Max(v => v.X);
            var minY = vectors.Min(v => v.Y);
            var maxY = vectors.Max(v => v.Y);
            var side = Math.Max(maxX - minX, maxY - minY) * (1 + 2 * CITY_MARGIN);
            var left = (minX + maxX) / 2d - side / 2d;
            var bottom = (minY + maxY) / 2d - side / 2d;
            this.ApplyMapFrame(ref side, ref left, ref bottom);
            var top = bottom + side;
            this.zoneConf.SetZoneSize(side);
            this.zoneConf.Reporter.Log(string.Format("City frame: x {0:F0} to {1:F0}, y {2:F0} to {3:F0}", left, left + side, top - side, top), LogLevel.Notice);

            foreach (var fixtureRow in this.fixtureRows)
            {
                var nifRow = this.NifRows.First(n => n.NifId == fixtureRow.NifId);
                if (nifRow.Polygons == null || nifRow.Polygons.Length == 0)
                {
                    continue;
                }

                var pieceVectors = nifRow.Polygons.SelectMany(p => p.Vectors).ToList();
                var center = new System.Numerics.Vector3((pieceVectors.Min(v => v.X) + pieceVectors.Max(v => v.X)) / 2f, (pieceVectors.Min(v => v.Y) + pieceVectors.Max(v => v.Y)) / 2f, 0);

                // A copy, the cached polygons are shared with other zones. Turned by 180 degrees: on the client maps
                // city x grows to the left and y downwards (checked against the Camelot, Jordheim and Tir na Nog maps).
                nifRow.Polygons = nifRow.Polygons.Select(p => new Polygon(TurnAround(p.P1, center), TurnAround(p.P2, center), TurnAround(p.P3, center), p.Texture, p.Uvs) { Texture2 = p.Texture2, Uvs2 = p.Uvs2, TextureBlend = p.TextureBlend, VertexColors = p.VertexColors, MaterialColor = p.MaterialColor }).ToArray();
                fixtureRow.X = left + side - center.X;
                fixtureRow.Y = center.Y - (top - side);
            }
        }

        /// <summary>
        /// Load all required CSV data
        /// </summary>
        private void LoadCsvData() {
            this.zoneConf.Reporter.ProgressStartMarquee("Loading fixture data ...");

            var nifsCsvRows = DataWrapper.GetFileContent(this.zoneConf.CvsMpk, "nifs.csv");
            var fixturesRows = DataWrapper.GetFileContent(this.zoneConf.CvsMpk, "fixtures.csv");

            // Create a NumberFormatInfo object for floats and set some of its properties.
            var provider = new System.Globalization.NumberFormatInfo
                           {
		                           NumberDecimalSeparator = ".",
		                           NumberGroupSeparator = "",
		                           NumberGroupSizes = new int[] { 2 }
                           };

            foreach (var row in nifsCsvRows)
            {
                if (string.IsNullOrWhiteSpace(row) || row.StartsWith("Grid") || row.StartsWith("NIF")) continue;

                var fields = row.Split(',');

                var nifRow = new NifRow
                             {
		                             NifId = Convert.ToInt32(fields[0]),
		                             TextualName = fields[1],
		                             Filename = fields[2],
		                             Color = Convert.ToInt32(fields[5])
                             };
                this.NifRows.Add(nifRow);
            }

            // Read fixtures.csv
            foreach (var row in fixturesRows)
            {
                if (string.IsNullOrWhiteSpace(row) || row.StartsWith("Fixtures") || row.StartsWith("ID")) continue;

                var fields = row.Split(',');
                var fixtureRow = new FixtureRow
                                 {
		                                 Id = Convert.ToInt32(fields[0]),
		                                 NifId = Convert.ToInt32(fields[1]),
		                                 TextualName = fields[2],
		                                 X = Convert.ToDouble(fields[3], provider),
		                                 Y = Convert.ToDouble(fields[4], provider),
		                                 Z = Convert.ToDouble(fields[5], provider),
		                                 A = Convert.ToInt32(fields[6]),
		                                 Scale = Convert.ToInt32(fields[7]),
		                                 OnGround = (Convert.ToInt32(fields[11]) == 1) ? true : false,
		                                 Flip = (Convert.ToInt32(fields[12]) == 1) ? true : false
                                 };

                if (fields.Length > 15)
                {
                    fixtureRow.Angle3D = Convert.ToDouble(fields[15], provider);
                    fixtureRow.AxisX3D = Convert.ToDouble(fields[16], provider);
                    fixtureRow.AxisY3D = Convert.ToDouble(fields[17], provider);
                    fixtureRow.AxisZ3D = Convert.ToDouble(fields[18], provider);
                }

                this.fixtureRows.Add(fixtureRow);
            }

            // Add the trees of the clusters to the cache
            for (var i = 0; i < this.NifRows.Count; i++ )
            {
                var treeCluster = FixtureCache.TreeClusters.FirstOrDefault(tc => tc.Name.ToLower() == this.NifRows[i].Filename.ToLower());
                if (treeCluster == null || this.NifRows.Any(n => n.Filename == treeCluster.Tree)) continue;

                var tree = new NifRow
                           {
		                           NifId = 10000 + i,
		                           TextualName = treeCluster.Tree + " (cluster tree)",
		                           Filename = treeCluster.Tree
                           };
                this.NifRows.Add(tree);
            }

            this.zoneConf.Reporter.ProgressReset();
        }

        /// <summary>
        /// Load the Polygons if the nifRows
        /// </summary>
        private void LoadPolygons()
        {
            if (!this.NifRows.Any() || !this.fixtureRows.Any()) return;

            var models = new List<(NifRow, string)>();
            foreach (var nifRow in this.NifRows)
            {
                if (FixtureCache.IsTreeCluster(nifRow.Filename)) continue;

                var nifArchivePath = this.FindNifArchive(nifRow);
                if (string.IsNullOrEmpty(nifArchivePath)) continue;

                nifRow.ArchiveDirectory = Path.GetDirectoryName(nifArchivePath);
                models.Add((nifRow, nifArchivePath));
            }

            FixtureCache.LoadPolygons(models, this.zoneConf.Reporter);
        }

        private static System.Numerics.Vector3 TurnAround(System.Numerics.Vector3 vector, System.Numerics.Vector3 center)
        {
            return new System.Numerics.Vector3(center.X - vector.X, center.Y - vector.Y, vector.Z);
        }

        /// <summary>
        /// Longer side of the placed model in world units, 0 if it has no polygons
        /// </summary>
        private static double GetFootprint(NifRow nifRow, FixtureRow fixtureRow)
        {
            if (nifRow.Polygons == null || nifRow.Polygons.Length == 0)
            {
                return 0;
            }

            var size = nifRow.GetSize(0, 0);
            return Math.Max(size.Width, size.Height) * fixtureRow.Scale / 100d;
        }

        public List<DrawableFixture> GetDrawableFixtures()
        {
            var drawables = new List<DrawableFixture>();

            this.zoneConf.Reporter.Log("Preparing fixtures ...", LogLevel.Notice);
            this.zoneConf.Reporter.ProgressStart("Preparing fixtures ...");

            var trees = FixtureCache.Trees;
            var treeClusters = FixtureCache.TreeClusters;

            var progressCounter = 0;
            foreach (var fixtureRow in this.fixtureRows)
            {
                try
                {
                    var nifRow = this.NifRows.FirstOrDefault(n => n.NifId == fixtureRow.NifId);
                    if (nifRow == null) continue;

                    if (this.placementHeights.TryGetValue(fixtureRow.Id, out var placementHeight))
                    {
                        fixtureRow.Z = placementHeight;
                    }

                    var fixture = new DrawableFixture
                                  {
                                      PlacementRotation = this.placementRotations.TryGetValue(fixtureRow.Id, out var placementRotation) ? placementRotation : null,
		                                  // Set default values
		                                  Name = fixtureRow.TextualName,
		                                  NifName = nifRow.Filename,
		                                  TextureDirectory = nifRow.ArchiveDirectory,
		                                  FixtureRow = fixtureRow,
		                                  ZoneConf = this.zoneConf,
		                                  TextureProxies = this.textureProxies
                                  };

                    if (this.Level != null)
                    {
                        fixture.HeightBand = this.GetHeightBand(this.Level);
                    }

                    // Get renderer configuration
                    var rConf = FixtureRendererConfigurations.GetFixtureRendererConfiguration(nifRow.Filename);

                    fixture.IsTree = trees.Any(t => t.Name.ToLower() == nifRow.Filename.ToLower());
                    fixture.IsTreeCluster = treeClusters.Any(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());

                    if (rConf != null && rConf.Value.Name == "TreeShaded")
                    {
                        fixture.IsTree = true;
                    }

                    if (fixture.IsTree)
                    {
                        fixture.Tree = trees.FirstOrDefault(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());
                        fixture.RawPolygons = nifRow.Polygons;
                        if (fixture.Tree != null && !fixture.Tree.HasTextureColor)
                        {
                            SetModelTreeColor(fixture.Tree, nifRow);
                        }

                        fixture.RendererConf = rConf ?? FixtureRendererConfigurations.GetRendererById("TreeShaded");
                    }
                    else if (fixture.IsTreeCluster)
                    {
                        fixture.TreeCluster = treeClusters.FirstOrDefault(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());

                        // Get the polygons of the base nif
                        var treeNif = this.NifRows.FirstOrDefault(n => n.Filename.ToLower() == fixture.TreeCluster.Tree.ToLower());
                        if (treeNif == null) continue;
                        var baseTreePolygons = treeNif.Polygons;

                        // Loop the instances and transform the polygons
                        var treeClusterPolygons = new List<Polygon>();
                        foreach (var tree in fixture.TreeCluster.TreeInstances)
                        {
                            foreach (var treePolygon in baseTreePolygons)
                            {
                                var newPolygon = new Polygon(treePolygon.P1, treePolygon.P2, treePolygon.P3, treePolygon.Texture, treePolygon.Uvs) { Texture2 = treePolygon.Texture2, Uvs2 = treePolygon.Uvs2, TextureBlend = treePolygon.TextureBlend, VertexColors = treePolygon.VertexColors, MaterialColor = treePolygon.MaterialColor };
                                for (var i = 0; i < newPolygon.Vectors.Length; i++)
                                {
                                    newPolygon.Vectors[i].X -= tree.X;
                                    newPolygon.Vectors[i].Y += tree.Y;
                                    newPolygon.Vectors[i].Z += tree.Z;
                                }
                                treeClusterPolygons.Add(newPolygon);
                            }
                        }
                        fixture.RawPolygons = treeClusterPolygons;

                        if (rConf == null) fixture.RendererConf = FixtureRendererConfigurations.GetRendererById("TreeShaded");
                        else fixture.RendererConf = rConf.GetValueOrDefault();
                    }
                    else
                    {
                        fixture.RawPolygons = nifRow.Polygons;

                        if (rConf == null)
                        {
                            fixture.RendererConf = FixtureCache.HasPrerenderedImage(fixture.NifName)
                                ? FixtureRendererConfigurations.GetRendererById("Prerendered")
                                : FixtureRendererConfigurations.GetRendererBySize(GetFootprint(nifRow, fixtureRow));
                        }
                        else fixture.RendererConf = rConf.GetValueOrDefault();
                    }

                    // Calculate the final look of the model
                    var result = fixture.Calc();
                    if (result)
                    {
                        drawables.Add(fixture);
                    }
                    else if (this.Level == null)
                    {
                        this.zoneConf.Reporter.Log(string.Format("Fixture {0} (x: {1}, y: {2}, z: {3}) is too small to get drawn.", fixtureRow.TextualName, fixtureRow.X, fixtureRow.Y, fixtureRow.Z));
                    }

                    progressCounter++;
                    var percent = 100 * progressCounter / this.fixtureRows.Count;
                    this.zoneConf.Reporter.ProgressUpdate(percent);
                }
                catch
                {
                    this.zoneConf.Reporter.Log(string.Format("Error in fixture row of {0} (x: {1}, y: {2}, z: {3})", fixtureRow.TextualName, fixtureRow.X, fixtureRow.Y, fixtureRow.Z));
                    continue;
                }
            }

            this.zoneConf.Reporter.Log("Fixtures prepared!", LogLevel.Success);
            this.zoneConf.Reporter.ProgressReset();

            return drawables;
        }

        private string FindNifArchive(NifRow nifRow)
        {
            var archiveName = Path.GetFileNameWithoutExtension(nifRow.Filename) + ".npk";
            foreach (var dir in this.nifSearchPaths)
            {
                if (File.Exists(Path.Combine(dir, archiveName)))
                {
                    return string.Format("{0}\\{1}", dir, archiveName);
                }
            }

            this.zoneConf.Reporter.Log(string.Format("Unable to find nif \"{0}\"!", nifRow.Filename), LogLevel.Warning);
            return null;
        }

        private static System.Numerics.Vector3 Normalize(System.Numerics.Vector3 value)
        {
            var length = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
            if (Math.Abs(length) < 1e-6f)
            {
                return value;
            }

            var inverse = 1f / length;
            value.X *= inverse;
            value.Y *= inverse;
            value.Z *= inverse;
            return value;
        }
    }
}
