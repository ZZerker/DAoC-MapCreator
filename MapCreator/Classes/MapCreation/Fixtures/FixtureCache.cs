using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MapCreator.Classes.MapCreation.Fixtures.Objects;
using NifUtil;
using NifUtil.Objects;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Fixture data shared by all zones: trees, prerendered images and model polygons
    /// </summary>
    internal static class FixtureCache
    {
        private static readonly System.Drawing.Color DefaultTreeColor = System.Drawing.ColorTranslator.FromHtml("#5e683a");

        private static readonly Lazy<(List<TreeRow> Trees, List<TreeClusterRow> Clusters)> TreeData = new(LoadTreeData);

        private static readonly Lazy<HashSet<string>> PrerenderedObjectNames = new(LoadPrerenderedObjectNames);

        // Polygons by cache name, guarded by PolysLock. polys4.mpk is additionally shared with other processes.
        private static readonly Dictionary<string, Polygon[]> Polygons = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object PolysLock = new();

        public static IReadOnlyList<TreeRow> Trees => TreeData.Value.Trees;

        public static IReadOnlyList<TreeClusterRow> TreeClusters => TreeData.Value.Clusters;

        public static bool HasPrerenderedImage(string nifName)
        {
            return PrerenderedObjectNames.Value.Contains(Path.GetFileNameWithoutExtension(nifName));
        }

        public static bool IsTreeCluster(string nifName)
        {
            return TreeClusters.Any(tc => string.Equals(tc.Name, nifName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Assigns the polygons of each model, converting and caching models that are not cached yet
        /// </summary>
        public static void LoadPolygons(IReadOnlyList<(NifRow Row, string ArchivePath)> models, IRenderReporter reporter)
        {
            lock (PolysLock)
            {
                var missing = new List<(NifRow Row, string ArchivePath, string CacheName)>();
                foreach (var (row, archivePath) in models)
                {
                    var cacheName = GetCacheName(archivePath, row.Variant);
                    if (Polygons.TryGetValue(cacheName, out var polygons))
                    {
                        row.Polygons = polygons;
                    }
                    else
                    {
                        missing.Add((row, archivePath, cacheName));
                    }
                }

                if (missing.Count == 0)
                {
                    return;
                }

                using var polysMutex = new Mutex(false, "MapCreatorPolysCache");
                try
                {
                    polysMutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                }

                try
                {
                    LoadMissingPolygons(missing, reporter);
                }
                finally
                {
                    polysMutex.ReleaseMutex();
                }
            }
        }

        private static void LoadMissingPolygons(List<(NifRow Row, string ArchivePath, string CacheName)> missing, IRenderReporter reporter)
        {
            reporter.Log("Loading polygons ...", LogLevel.Notice);
            reporter.ProgressStart("Loading polygons ...");

            var polysDirectory = new DirectoryInfo(string.Format("{0}\\data\\polys", System.Windows.Forms.Application.StartupPath));
            if (!polysDirectory.Exists) polysDirectory.Create();

            // polys4.mpk: .poly files with blended texture layers, one entry per source archive
            var polysMpkFile = string.Format("{0}\\data\\polys4.mpk", System.Windows.Forms.Application.StartupPath);
            DeleteOldCache("polys.mpk");
            DeleteOldCache("polys2.mpk");
            DeleteOldCache("polys3.mpk");

            var polyMpk = File.Exists(polysMpkFile) ? MpkWrapper.Open(polysMpkFile) : new MPKLib.MPAK();
            var polyMpkModified = !File.Exists(polysMpkFile);
            var cachedPolys = new HashSet<string>(polyMpk.Files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

            var progressCounter = 0;
            foreach (var (nifRow, nifArchivePath, modelPolyFileName) in missing)
            {
                reporter.ProgressUpdate(100 * progressCounter++ / missing.Count);

                // Several rows of one zone can point to the same model
                if (Polygons.TryGetValue(modelPolyFileName, out var loaded))
                {
                    nifRow.Polygons = loaded;
                    continue;
                }

                if (cachedPolys.Contains(modelPolyFileName))
                {
                    nifRow.Polygons = NifParser.ReadPoly(new StreamReader(new MemoryStream(polyMpk.GetFile(modelPolyFileName).Data)));
                    Polygons[modelPolyFileName] = nifRow.Polygons;
                    continue;
                }

                using (var nifFileFromNpk = MpkWrapper.GetFileFromMpk(nifArchivePath, nifRow.Filename))
                {
                    if (nifFileFromNpk == null)
                    {
                        continue;
                    }

                    reporter.Log(string.Format("Processing {0}...", nifRow.TextualName), LogLevel.Notice);

                    var modelPolySavePath = string.Format("{0}\\{1}", polysDirectory, modelPolyFileName);
                    var nifParser = new NifParser();
                    nifParser.IsNodeDrawable += node => IsNodeDrawable(nifRow, node);
                    nifParser.ResolveTexture = nifRow.ResolveTexture;

                    try
                    {
                        nifParser.Load(nifFileFromNpk);
                        nifParser.Convert(ConvertType.Poly, modelPolySavePath);
                    }
                    catch (Exception ex)
                    {
                        reporter.Log(string.Format("Skipping {0} ({1}): {2}", nifRow.TextualName, nifArchivePath, ex.Message), LogLevel.Warning);
                        nifRow.Polygons = Array.Empty<Polygon>();
                        Polygons[modelPolyFileName] = nifRow.Polygons;
                        continue;
                    }

                    polyMpk.AddFile(modelPolySavePath);
                    cachedPolys.Add(modelPolyFileName);
                    polyMpkModified = true;

                    nifRow.Polygons = nifParser.GetPolys();
                    Polygons[modelPolyFileName] = nifRow.Polygons;
                }
            }

            reporter.ProgressStartMarquee("Saving polygons ...");
            if (polyMpkModified)
            {
                polyMpk.Save(polysMpkFile);
            }

            Directory.Delete(polysDirectory.FullName, true);

            reporter.Log("Polygons loaded!", LogLevel.Success);
            reporter.ProgressReset();
        }

        private static void DeleteOldCache(string fileName)
        {
            var file = Path.Combine(System.Windows.Forms.Application.StartupPath, "data", fileName);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        // Zones ship their own variants of shared models, so the cache name contains the archive folder
        private static string GetCacheName(string archivePath, string variant)
        {
            var name = Path.GetRelativePath(Properties.Settings.Default.game_path, Path.ChangeExtension(archivePath, null)).Replace('\\', '_').ToLowerInvariant();
            return name + (variant == null ? "" : "_" + variant) + ".poly";
        }

        private static bool IsNodeDrawable(NifRow nifRow, Niflib.NiAVObject node)
        {
            if (nifRow.IsNodeDrawable != null)
            {
                return nifRow.IsNodeDrawable(node.Name.Value);
            }

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

                return validNodes.Find(v => node.Name.Value.StartsWith(v)) != null;
            }

            return true;
        }

        private static HashSet<string> LoadPrerenderedObjectNames()
        {
            var objectImageFileDirectory = string.Format("{0}\\data\\prerendered\\objects", System.Windows.Forms.Application.StartupPath);
            if (!Directory.Exists(objectImageFileDirectory)) Directory.CreateDirectory(objectImageFileDirectory);
            return new HashSet<string>(Directory.GetFiles(objectImageFileDirectory).Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
        }

        private static (List<TreeRow>, List<TreeClusterRow>) LoadTreeData()
        {
            var provider = new System.Globalization.NumberFormatInfo
                           {
                               NumberDecimalSeparator = ".",
                               NumberGroupSeparator = "",
                               NumberGroupSizes = new int[] { 2 }
                           };

            var treeMpk = string.Format("{0}\\zones\\trees\\treemap.mpk", Properties.Settings.Default.game_path);
            var treeClusterMpk = string.Format("{0}\\zones\\trees\\tree_clusters.mpk", Properties.Settings.Default.game_path);

            var treeRows = new List<TreeRow>();
            foreach (var row in DataWrapper.GetFileContent(treeMpk, "Treemap.csv"))
            {
                if (row.StartsWith("NIF Name")) continue;

                var fields = row.Split(',');
                var treeRow = new TreeRow
                              {
                                  Name = fields[0],
                                  ZOffset = (string.IsNullOrEmpty(fields[4])) ? 0 : Convert.ToInt32(fields[4]),
                                  BarkTexture = fields[2],
                                  LeafTexture = fields[3]
                              };
                treeRow.AverageColor = GetTreeColor(treeRow);
                treeRows.Add(treeRow);
            }

            var treeClusterRows = new List<TreeClusterRow>();
            foreach (var row in DataWrapper.GetFileContent(treeClusterMpk, "tree_clusters.csv"))
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
                    if (fields[i] == "" || fields[i + 1] == "" || fields[i + 2] == "") break;

                    var x = Convert.ToSingle(fields[i], provider);
                    var y = Convert.ToSingle(fields[i + 1], provider);
                    var z = Convert.ToSingle(fields[i + 2], provider);
                    if (x == 0 && y == 0 && z == 0) break;

                    treeClusterRow.TreeInstances.Add(new SharpDX.Vector3(x, y, z));
                }

                treeClusterRows.Add(treeClusterRow);
            }

            return (treeRows, treeClusterRows);
        }

        private static System.Drawing.Color GetTreeColor(TreeRow treeRow)
        {
            // Leafless trees (burnt trees, reeds) only have a bark texture
            var textureName = string.IsNullOrEmpty(treeRow.LeafTexture) ? treeRow.BarkTexture : treeRow.LeafTexture;
            if (string.IsNullOrEmpty(textureName))
            {
                AppLog.Log(string.Format("Tree {0} has no texture in Treemap.csv, its model textures are used.", treeRow.Name));
                treeRow.HasTextureColor = false;
                return DefaultTreeColor;
            }

            var treeTextureFile = Path.Combine(Properties.Settings.Default.game_path, "zones", "trees", textureName);
            if (!File.Exists(treeTextureFile))
            {
                AppLog.Log(string.Format("Texture {0} for tree {1} not found. Using default color.", textureName, treeRow.Name), LogLevel.Warning);
                return DefaultTreeColor;
            }

            using var texture = new ImageMagick.MagickImage(treeTextureFile);
            texture.Resize(1, 1);
            return texture.GetPixels().First().ToColor().ToSystemColor();
        }
    }
}
