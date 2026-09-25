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
using MPKLib;
using NifUtil;
using NifUtil.Objects;

namespace MapCreator.Classes.MapCreation.Fixtures
{
	internal static class FixturesLoader
    {
        private static ZoneConfiguration zoneConf;

        private static List<NifRow> nifRows = new List<NifRow>();
        private static readonly List<FixtureRow> FixtureRows = new List<FixtureRow>();
        private static readonly List<TreeRow> TreeRows = new List<TreeRow>();
        private static readonly List<TreeClusterRow> TreeClusterRows = new List<TreeClusterRow>();

        // Location where to search for npk with nifs
        private static readonly List<string> NifSearchPaths = new List<string>();

        private static List<string> nifObjectImages = new List<string>();

        internal static List<NifRow> NifRows
        {
            get => FixturesLoader.nifRows;
            set => FixturesLoader.nifRows = value;
        }

        public static void Initialize(ZoneConfiguration zoneConfiguration)
        {
            FixturesLoader.zoneConf = zoneConfiguration;

            // Clear on call
            nifRows.Clear();
            FixtureRows.Clear();
            NifSearchPaths.Clear();
            // Trees and TreeCluster are always the same, do not load on each progress
            //treeRows.Clear();
            //treeClusterRows.Clear();

            LoadCsvData();
            LoadPolygons();

            // Load the filenames in data/prerendered/objects
            var objectImageFileDirectory = string.Format("{0}\\data\\prerendered\\objects", System.Windows.Forms.Application.StartupPath);
            if (!Directory.Exists(objectImageFileDirectory)) Directory.CreateDirectory(objectImageFileDirectory);
            nifObjectImages = Directory.GetFiles(objectImageFileDirectory).Select(f => Path.GetFileNameWithoutExtension(f).ToLower()).ToList();
        }

        /// <summary>
        /// Load all required CSV data
        /// </summary>
        private static void LoadCsvData() {
            MainForm.ProgressStartMarquee("Loading fixture data ...");

            var nifsCsvRows = DataWrapper.GetFileContent(zoneConf.CvsMpk, "nifs.csv");
            var fixturesRows = DataWrapper.GetFileContent(zoneConf.CvsMpk, "fixtures.csv");

            // Create a NumberFormatInfo object for floats and set some of its properties.
            var provider = new System.Globalization.NumberFormatInfo
                           {
		                           NumberDecimalSeparator = ".",
		                           NumberGroupSeparator = "",
		                           NumberGroupSizes = new int[] { 2 }
                           };

            foreach (var row in nifsCsvRows)
            {
                if (row.StartsWith("Grid") || row.StartsWith("NIF")) continue;

                var fields = row.Split(',');

                var nifRow = new NifRow
                             {
		                             NifId = Convert.ToInt32(fields[0]),
		                             TextualName = fields[1],
		                             Filename = fields[2],
		                             Color = Convert.ToInt32(fields[5])
                             };
                nifRows.Add(nifRow);
            }

            // Read fixtures.csv
            foreach (var row in fixturesRows)
            {
                if (row.StartsWith("Fixtures") || row.StartsWith("ID")) continue;

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

                FixtureRows.Add(fixtureRow);
            }

            // Only load on first init
            if (TreeRows.Count == 0)
            {
                var treeMpk = string.Format("{0}\\zones\\trees\\treemap.mpk", Properties.Settings.Default.game_path);
                var treeClusterMpk = string.Format("{0}\\zones\\trees\\tree_clusters.mpk", Properties.Settings.Default.game_path);

                var treesCsvRows = DataWrapper.GetFileContent(treeMpk, "Treemap.csv");
                var treeClusterCsvRows = DataWrapper.GetFileContent(treeClusterMpk, "tree_clusters.csv");

                foreach (var row in treesCsvRows)
                {
                    if (row.StartsWith("NIF Name")) continue;

                    var fields = row.Split(',');
                    //if (fields[4] == "") continue;

                    var treeRow = new TreeRow
                                  {
		                                  Name = fields[0],
		                                  ZOffset = (string.IsNullOrEmpty(fields[4])) ? 0 : Convert.ToInt32(fields[4]),
		                                  BarkTexture = fields[2],
		                                  LeafTexture = fields[3]
                                  };
                    treeRow.AverageColor = GetTreeColor(treeRow);
                    TreeRows.Add(treeRow);
                }

                foreach (var row in treeClusterCsvRows)
                {
                    if (row.StartsWith("name")) continue;
                    if (row == "") continue;

                    var fields = row.Split(',');
                    var treeClusterRow = new TreeClusterRow
                                         {
		                                         Name = fields[0],
		                                         Tree = fields[1],
		                                         TreeInstances = new List<SharpDX.Vector3>()
                                         };
                    for (var i = 2; i < fields.Length; i = i + 3)
                    {
                        if(fields[i] == "" || fields[i+1] == "" || fields[i+2] == "") break;

                        var x = Convert.ToSingle(fields[i], provider);
                        var y = Convert.ToSingle(fields[i + 1], provider);
                        var z = Convert.ToSingle(fields[i + 2], provider);
                        if (x == 0 && y == 0 && z == 0) break;

                        treeClusterRow.TreeInstances.Add(new SharpDX.Vector3(x, y, z));
                    }

                    TreeClusterRows.Add(treeClusterRow);
                }
            }
            
            // Add the trees of the clusters to the cache
            for (var i = 0; i < nifRows.Count; i++ )
            {
                var isTreeCluster = TreeClusterRows.Any(tc => tc.Name.ToLower() == nifRows[i].Filename.ToLower());
                if (isTreeCluster)
                {
                    var treeCluster = TreeClusterRows.FirstOrDefault(tc => tc.Name.ToLower() == nifRows[i].Filename.ToLower());
                    if(treeCluster == null || nifRows.Any(n => n.Filename == treeCluster.Tree)) continue;

                    var tree = new NifRow
                               {
		                               NifId = 10000 + i,
		                               TextualName = treeCluster.Tree + " (cluster tree)",
		                               Filename = treeCluster.Tree
                               };
                    nifRows.Add(tree);
                }
            }

            MainForm.ProgressReset();
        }

        private static readonly Dictionary<string, System.Drawing.Color> TreeColors = new Dictionary<string,System.Drawing.Color>();
        private static readonly System.Drawing.Color DefaultTreeColor = System.Drawing.ColorTranslator.FromHtml("#5e683a");

        private static System.Drawing.Color GetTreeColor(TreeRow treeRow)
        {
            if (TreeColors.TryGetValue(treeRow.Name, out var treeColor)) return treeColor;

            // Leafless trees (burnt trees, reeds) only have a bark texture
            var textureName = string.IsNullOrEmpty(treeRow.LeafTexture) ? treeRow.BarkTexture : treeRow.LeafTexture;
            if (string.IsNullOrEmpty(textureName))
            {
                MainForm.Log(string.Format("Tree {0} has no texture in Treemap.csv. Using default color.", treeRow.Name));
                return DefaultTreeColor;
            }

            var treeTextureFile = Path.Combine(Properties.Settings.Default.game_path, "zones", "trees", textureName);
            if (!File.Exists(treeTextureFile))
            {
                MainForm.Log(string.Format("Texture {0} for tree {1} not found. Using default color.", textureName, treeRow.Name), MainForm.LogLevel.Warning);
                return DefaultTreeColor;
            }

            using (var texture = new ImageMagick.MagickImage(treeTextureFile))
            {
                texture.Resize(1, 1);
                var color = texture.GetPixels().First().ToColor().ToSystemColor();
                TreeColors.Add(treeRow.Name, color);
                return color;
            }
        }

        /// <summary>
        /// Load the Polygons if the nifRows
        /// </summary>
        private static void LoadPolygons()
        {
            if (!nifRows.Any() || !FixtureRows.Any()) return;

            // Shared with parallel MapCreator processes
            using (var polysMutex = new System.Threading.Mutex(false, "MapCreatorPolysCache"))
            {
                try
                {
                    polysMutex.WaitOne();
                }
                catch (System.Threading.AbandonedMutexException)
                {
                }

                try
                {
                    LoadPolygonsExclusive();
                }
                finally
                {
                    polysMutex.ReleaseMutex();
                }
            }
        }

        private static void LoadPolygonsExclusive()
        {

            // MainForm progress
            MainForm.Log("Loading polygons ...", MainForm.LogLevel.Notice);
            MainForm.ProgressStart("Loading polygons ...");

            var polysDirectory = new DirectoryInfo(string.Format("{0}\\data\\polys", System.Windows.Forms.Application.StartupPath));
            if (!polysDirectory.Exists) polysDirectory.Create();

            // polys2.mpk: .poly files with texture names, one entry per source archive
            var polysMpkFile = string.Format("{0}\\data\\polys2.mpk", System.Windows.Forms.Application.StartupPath);

            var polyMpk = new MPAK();
            var polyMpkModified = false;

            if (!File.Exists(polysMpkFile)) polyMpkModified = true; // Create a new poyls.mpk
            else polyMpk = MpkWrapper.Open(polysMpkFile);

            var cachedPolys = new HashSet<string>(polyMpk.Files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

            // Loop all nifs from nifs.csv
            var progressCounter = 0;
            foreach (var nifRow in nifRows)
            {
                // Check if this nif is a TreeCluster
                var isTreeCluster = TreeClusterRows.Any(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());
                if(isTreeCluster) continue;

                var nifArchivePath = FindNifArchive(nifRow);
                if(string.IsNullOrEmpty(nifArchivePath)) continue;
                nifRow.ArchiveDirectory = Path.GetDirectoryName(nifArchivePath);

                // Zones ship their own variants of shared models, so the cache name contains the archive folder
                var modelPolyFileName = Path.GetRelativePath(Properties.Settings.Default.game_path, Path.ChangeExtension(nifArchivePath, ".poly")).Replace('\\', '_').ToLowerInvariant();
                var modelPolySavePath = string.Format("{0}\\{1}", polysDirectory, modelPolyFileName);

                // MPK handling, cache .poly file for models
                if (!cachedPolys.Contains(modelPolyFileName))
                {

                    // open the archive
                    using (var nifFileFromNpk = MpkWrapper.GetFileFromMpk(nifArchivePath, nifRow.Filename))
                    {
                        if (nifFileFromNpk != null)
                        {
                            MainForm.Log(string.Format("Processing {0}...", nifRow.TextualName), MainForm.LogLevel.Notice);

                            // Create a new poly and add to mpk
                            polyMpkModified = true;

                            var nifParser = new NifParser();
                            nifParser.IsNodeDrawable += delegate(Niflib.NiAVObject node)
                            {
                                return NifParser_IsNodeDrawable(nifRow, node);
                            };

                            try
                            {
                                nifParser.Load(nifFileFromNpk);
                                nifParser.Convert(ConvertType.Poly, modelPolySavePath);
                            }
                            catch (Exception ex)
                            {
                                MainForm.Log(string.Format("Skipping {0} ({1}): {2}", nifRow.TextualName, nifArchivePath, ex.Message), MainForm.LogLevel.Warning);
                                nifRow.Polygons = Array.Empty<Polygon>();
                                continue;
                            }

                            // Add file to polys.mpk
                            polyMpk.AddFile(modelPolySavePath);
                            cachedPolys.Add(modelPolyFileName);

                            // Assign polys
                            nifRow.Polygons = nifParser.GetPolys();
                        }
                    }
                }
                else
                {
                    // .poly file for model is found, read it
                    nifRow.Polygons = NifParser.ReadPoly(new StreamReader(new MemoryStream(polyMpk.GetFile(modelPolyFileName).Data)));
                }

                var percent = 100 * progressCounter / nifRows.Count;
                MainForm.ProgressUpdate(percent);
                progressCounter++;
            }

            MainForm.ProgressStartMarquee("Saving polygons ...");

            // Save the mpk
            if (polyMpkModified)
            {
                polyMpk.Save(polysMpkFile);
            }

            // Delete polys directory
            Directory.Delete(polysDirectory.FullName, true);

            MainForm.Log("Polygons loaded!", MainForm.LogLevel.Success);
            MainForm.ProgressReset();
        }

        private static bool NifParser_IsNodeDrawable(NifRow nifRow, Niflib.NiAVObject node)
        {
            // Only draw the elements, sticking out of the ground. NifIds differ per zone, so match the file.
            if (string.Equals(nifRow.Filename, "agramonKeep01.nif", StringComparison.OrdinalIgnoreCase))
            {
                var validNodes = new List<string>
                                 {
		                                 "agramonKeep01",
		                                 "collisionswitch",
		                                 "visible",
		                                 "wall -outdoors",
		                                 "wall -outdoors01",
		                                 "tower",
		                                 "tower01",
		                                 "tower02",
		                                 "entrance",
		                                 "disc"
                                 };

                var result = validNodes.Find(v => node.Name.Value.StartsWith(v));
                if (result != null)
                {
                    return true;
                }
                return false;
            }
            
            return true;
        }

        public static List<DrawableFixture> GetDrawableFixtures()
        {
            var drawables = new List<DrawableFixture>();

            // MainForm progress
            MainForm.Log("Preparing fixtures ...", MainForm.LogLevel.Notice);
            MainForm.ProgressStart("Preparing fixtures ...");

            var progressCounter = 0;
            foreach (var fixtureRow in FixtureRows)
            {
                try
                {
                    var nifRow = nifRows.FirstOrDefault(n => n.NifId == fixtureRow.NifId);
                    if (nifRow == null) continue;

                    var fixture = new DrawableFixture
                                  {
		                                  // Set default values
		                                  Name = fixtureRow.TextualName,
		                                  NifName = nifRow.Filename,
		                                  FixtureRow = fixtureRow,
		                                  ZoneConf = zoneConf
                                  };

                    // Get renderer configuration
                    var rConf = FixtureRendererConfigurations.GetFixtureRendererConfiguration(nifRow.Filename);

                    fixture.IsTree = TreeRows.Any(t => t.Name.ToLower() == nifRow.Filename.ToLower());
                    fixture.IsTreeCluster = TreeClusterRows.Any(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());

                    if (rConf != null && (rConf.Value.Name == "TreeShaded" || rConf.Value.Name == "TreeImage"))
                    {
                        fixture.IsTree = true;
                    }

                    if (fixture.IsTree)
                    {
                        fixture.Tree = TreeRows.FirstOrDefault(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());
                        fixture.RawPolygons = nifRow.Polygons;

                        fixture.RendererConf = rConf ?? FixtureRendererConfigurations.GetRendererById("TreeImage");
                    }
                    else if (fixture.IsTreeCluster)
                    {
                        fixture.TreeCluster = TreeClusterRows.FirstOrDefault(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());

                        // Get the polygons of the base nif
                        var treeNif = nifRows.FirstOrDefault(n => n.Filename.ToLower() == fixture.TreeCluster.Tree.ToLower());
                        if (treeNif == null) continue;
                        var baseTreePolygons = treeNif.Polygons;

                        // Loop the instances and transform the polygons
                        var treeClusterPolygons = new List<Polygon>();
                        foreach (var tree in fixture.TreeCluster.TreeInstances)
                        {
                            foreach (var treePolygon in baseTreePolygons)
                            {
                                var newPolygon = new Polygon(treePolygon.P1, treePolygon.P2, treePolygon.P3, treePolygon.Texture);
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

                        if (rConf == null) fixture.RendererConf = FixtureRendererConfigurations.GetRendererById("TreeImage");
                        else fixture.RendererConf = rConf.GetValueOrDefault();
                    }
                    else
                    {
                        fixture.RawPolygons = nifRow.Polygons;

                        if (rConf == null)
                        {
                            var nifFilenamWithoutExtension = Path.GetFileNameWithoutExtension(fixture.NifName);
                            if (nifObjectImages.Contains(nifFilenamWithoutExtension.ToLower()))
                            {
                                fixture.RendererConf = FixtureRendererConfigurations.GetRendererById("Prerendered");
                            }
                            else
                            {
                                fixture.RendererConf = FixtureRendererConfigurations.DefaultConfiguration;
                            }
                        }
                        else fixture.RendererConf = rConf.GetValueOrDefault();
                    }

                    // Calculate the final look of the model
                    var result = fixture.Calc();
                    if (result)
                    {
                        drawables.Add(fixture);
                    }
                    else
                    {
                        MainForm.Log(string.Format("Fixture {0} (x: {1}, y: {2}, z: {3}) is too small to get drawn.", fixtureRow.TextualName, fixtureRow.X, fixtureRow.Y, fixtureRow.Z));
                    }

                    progressCounter++;
                    var percent = 100 * progressCounter / FixtureRows.Count;
                    MainForm.ProgressUpdate(percent);
                }
                catch
                {
                    // TODO: Send meesage to client
                    MainForm.Log(string.Format("Error in fixture row of {0} (x: {1}, y: {2}, z: {3})", fixtureRow.TextualName, fixtureRow.X, fixtureRow.Y, fixtureRow.Z));
                    continue;
                }
            }

            MainForm.Log("Fixtures prepared!", MainForm.LogLevel.Success);
            MainForm.ProgressReset();

            return drawables;
        }

        private static string FindNifArchive(NifRow nifRow)
        {
            if (NifSearchPaths.Count == 0)
            {
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "Newtowns\\zones\\Nifs")); // Newtows
                NifSearchPaths.Add(string.Format("{0}\\{1}", zoneConf.ZoneDirectory, "nifs")); // Current Zone Directory
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "zones\\Nifs")); // Globals zones nif dir
                //nifPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "zones\\trees")); // Global trees nif dir: removed, theses nifs are CAD files
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "frontiers\\NIFS")); // Frontiers
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "phousing\\nifs")); // Housing
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "Tutorial\\zones\\nifs")); // Tutorial
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "pregame")); // Pregame?
                NifSearchPaths.Add(string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "zones\\Dnifs")); // Guess Dungeon nifs
            }

            // Search NPKs
            var archiveName = Path.GetFileNameWithoutExtension(nifRow.Filename) + ".npk";
            //MainForm.Log(string.Format("Searching for {0}", archiveName), MainForm.LogLevel.notice);
            foreach (var dir in NifSearchPaths)
            {
                if (File.Exists(Path.Combine(dir, archiveName)))
                {
                    //MainForm.Log(string.Format("Found {0} in {1}!", archiveName, dir), MainForm.LogLevel.success);
                    return string.Format("{0}\\{1}", dir, archiveName);
                }
            }

            MainForm.Log(string.Format("Unable to find nif \"{0}\"!", nifRow.Filename), MainForm.LogLevel.Warning);
            return null;
        }

    }
}
