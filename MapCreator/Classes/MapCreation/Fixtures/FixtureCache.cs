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
    /// Fixture data shared by all zones: trees and model polygons
    /// </summary>
    internal static class FixtureCache
    {
        private static readonly System.Drawing.Color DefaultTreeColor = System.Drawing.ColorTranslator.FromHtml("#5e683a");

        // .poly files with blended texture layers, vertex colors, dark maps (also "_dm_" detail maps), water and additive flags, one entry per source archive
        private const string POLYS_CACHE_FILE = "polys8.mpk";

        private const string POLYS_MUTEX = "MapCreatorPolysCache";

        private static readonly Lazy<(List<TreeRow> Trees, List<TreeClusterRow> Clusters)> TreeData = new(LoadTreeData);

        // Polygons of models other zones can use too, by cache name, guarded by PolysLock. The cache file is additionally shared with other processes.
        private static readonly Dictionary<string, Polygon[]> Polygons = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object PolysLock = new();

        private static string PolysCacheFile => Path.Combine(System.Windows.Forms.Application.StartupPath, "data", POLYS_CACHE_FILE);

        public static IReadOnlyList<TreeRow> Trees => TreeData.Value.Trees;

        public static int CachedModels
        {
            get
            {
                lock (PolysLock)
                {
                    return Polygons.Count;
                }
            }
        }

        public static IReadOnlyList<TreeClusterRow> TreeClusters => TreeData.Value.Clusters;

        public static bool IsTreeCluster(string nifName)
        {
            return TreeClusters.Any(tc => string.Equals(tc.Name, nifName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Assigns the polygons of each model, converting and caching models that are not cached yet. Models from the zone's own
        /// archives are not kept in memory: no other zone can use them.
        /// </summary>
        public static void LoadPolygons(IReadOnlyList<(NifRow Row, string ArchivePath)> models, string zoneDirectory, IRenderReporter reporter)
        {
            lock (PolysLock)
            {
                var fromMemory = 0;
                var missing = new List<(NifRow Row, string ArchivePath, string CacheName)>();
                foreach (var (row, archivePath) in models)
                {
                    var cacheName = GetCacheName(archivePath, row.Variant);
                    if (Polygons.TryGetValue(cacheName, out var polygons))
                    {
                        row.Polygons = polygons;
                        fromMemory++;
                    }
                    else
                    {
                        missing.Add((row, archivePath, cacheName));
                    }
                }

                reporter.Log(string.Format("Models: {0} reused from memory, {1} to load", fromMemory, missing.Select(m => m.CacheName).Distinct(StringComparer.OrdinalIgnoreCase).Count()), LogLevel.Notice);
                if (missing.Count == 0)
                {
                    return;
                }

                using var polysMutex = LockCacheFile();
                try
                {
                    LoadMissingPolygons(missing, zoneDirectory, reporter);
                }
                finally
                {
                    polysMutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Deletes the cache file and forgets the models in memory
        /// </summary>
        public static void Clear()
        {
            lock (PolysLock)
            {
                using var polysMutex = LockCacheFile();
                try
                {
                    Polygons.Clear();
                    File.Delete(PolysCacheFile);
                }
                finally
                {
                    polysMutex.ReleaseMutex();
                }
            }
        }

        private static Mutex LockCacheFile()
        {
            var polysMutex = new Mutex(false, POLYS_MUTEX);
            try
            {
                polysMutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
            }
            return polysMutex;
        }

        private static void LoadMissingPolygons(List<(NifRow Row, string ArchivePath, string CacheName)> missing, string zoneDirectory, IRenderReporter reporter)
        {
            reporter.Log("Loading polygons ...", LogLevel.Notice);
            reporter.ProgressStart("Loading polygons ...");

            var polysDirectory = new DirectoryInfo(string.Format("{0}\\data\\polys", System.Windows.Forms.Application.StartupPath));
            if (!polysDirectory.Exists) polysDirectory.Create();

            var polysMpkFile = PolysCacheFile;
            DeleteOldCache("polys.mpk");
            DeleteOldCache("polys2.mpk");
            DeleteOldCache("polys3.mpk");
            DeleteOldCache("polys4.mpk");
            DeleteOldCache("polys5.mpk");
            DeleteOldCache("polys6.mpk");
            DeleteOldCache("polys7.mpk");

            var polyMpk = File.Exists(polysMpkFile) ? MpkWrapper.Open(polysMpkFile) : new MPKLib.MPAK();
            var polyMpkModified = !File.Exists(polysMpkFile);
            var cachedPolys = new HashSet<string>(polyMpk.Files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

            // Several rows of one zone can point to the same model
            var loaded = new Dictionary<string, Polygon[]>(StringComparer.OrdinalIgnoreCase);
            var zoneArchives = zoneDirectory.TrimEnd('\\') + "\\";

            var progressCounter = 0;
            foreach (var (nifRow, nifArchivePath, modelPolyFileName) in missing)
            {
                reporter.ProgressUpdate(100 * progressCounter++ / missing.Count);

                if (!loaded.TryGetValue(modelPolyFileName, out var polygons))
                {
                    polygons = LoadModel(nifRow, nifArchivePath, modelPolyFileName, polyMpk, cachedPolys, polysDirectory, reporter, ref polyMpkModified);
                    if (polygons == null)
                    {
                        continue;
                    }

                    loaded[modelPolyFileName] = polygons;
                    if (!nifArchivePath.StartsWith(zoneArchives, StringComparison.OrdinalIgnoreCase))
                    {
                        Polygons[modelPolyFileName] = polygons;
                    }
                }

                nifRow.Polygons = polygons;
            }

            reporter.ProgressStartMarquee("Saving polygons ...");
            if (polyMpkModified)
            {
                // Other processes wait for the mutex, but an interrupted save must not leave a broken cache behind
                var temporaryFile = polysMpkFile + ".tmp";
                polyMpk.Save(temporaryFile);
                File.Move(temporaryFile, polysMpkFile, true);
            }

            Directory.Delete(polysDirectory.FullName, true);

            reporter.Log("Polygons loaded!", LogLevel.Success);
            reporter.ProgressReset();
        }

        /// <summary>
        /// Polygons from the cache file or converted from the NIF, null if the NIF is missing in its archive
        /// </summary>
        private static Polygon[] LoadModel(NifRow nifRow, string nifArchivePath, string modelPolyFileName, MPKLib.MPAK polyMpk, HashSet<string> cachedPolys, DirectoryInfo polysDirectory,
                                           IRenderReporter reporter, ref bool polyMpkModified)
        {
            if (cachedPolys.Contains(modelPolyFileName))
            {
                return NifParser.ReadPoly(new StreamReader(new MemoryStream(polyMpk.GetFile(modelPolyFileName).Data)));
            }

            using var nifFileFromNpk = MpkWrapper.GetFileFromMpk(nifArchivePath, nifRow.Filename);
            if (nifFileFromNpk == null)
            {
                return null;
            }

            reporter.Log(string.Format("Processing {0}...", nifRow.TextualName), LogLevel.Notice);

            var modelPolySavePath = string.Format("{0}\\{1}", polysDirectory, modelPolyFileName);
            using var nifParser = new NifParser();
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
                return Array.Empty<Polygon>();
            }

            polyMpk.AddFile(modelPolySavePath);
            cachedPolys.Add(modelPolyFileName);
            polyMpkModified = true;
            return nifParser.GetPolys();
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
                                         TreeInstances = new List<System.Numerics.Vector3>()
                                     };
                for (var i = 2; i < fields.Length; i = i + 3)
                {
                    if (fields[i] == "" || fields[i + 1] == "" || fields[i + 2] == "") break;

                    var x = Convert.ToSingle(fields[i], provider);
                    var y = Convert.ToSingle(fields[i + 1], provider);
                    var z = Convert.ToSingle(fields[i + 2], provider);
                    if (x == 0 && y == 0 && z == 0) break;

                    treeClusterRow.TreeInstances.Add(new System.Numerics.Vector3(x, y, z));
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
            using var pixels = texture.GetPixels();
            return pixels.First().ToColor().ToSystemColor();
        }
    }
}
