using System;
using System.Collections.Concurrent;
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

        // One .poly file per model and source archive, with blended texture layers, vertex colors, dark maps (also "_dm_" detail maps), water and additive flags
        private const string POLYS_CACHE_DIRECTORY = "polys8";

        private static readonly Lazy<(List<TreeRow> Trees, List<TreeClusterRow> Clusters)> TreeData = new(LoadTreeData);

        // Models other zones can reuse; each loads at most once however many zones ask
        private static readonly ConcurrentDictionary<string, Lazy<Polygon[]>> Polygons = new(StringComparer.OrdinalIgnoreCase);

        // Guards only the one-time housekeeping (old cache cleanup) and Clear(), never a model load
        private static readonly object PolysLock = new();

        private static volatile bool housekeepingDone;

        private static string PolysCacheDirectory => Path.Combine(AppContext.BaseDirectory, "data", POLYS_CACHE_DIRECTORY);

        public static IReadOnlyList<TreeRow> Trees => TreeData.Value.Trees;

        public static int CachedModels => Polygons.Count(entry => entry.Value.IsValueCreated);

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
            var fromMemory = 0;
            var missing = new List<(NifRow Row, string ArchivePath, string CacheName)>();
            foreach (var (row, archivePath) in models)
            {
                var cacheName = GetCacheName(archivePath, row.Variant);
                if (Polygons.TryGetValue(cacheName, out var lazyPolygons) && lazyPolygons.IsValueCreated)
                {
                    row.Polygons = lazyPolygons.Value;
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

            LoadMissingPolygons(missing, zoneDirectory, reporter);
        }

        /// <summary>
        /// Deletes the cache file and forgets the models in memory
        /// </summary>
        public static void Clear()
        {
            lock (PolysLock)
            {
                Polygons.Clear();
                housekeepingDone = false;
                if (Directory.Exists(PolysCacheDirectory))
                {
                    Directory.Delete(PolysCacheDirectory, true);
                }
            }
        }

        // Once per process before the first model file is read
        private static void EnsureHousekeeping()
        {
            if (housekeepingDone)
            {
                return;
            }

            lock (PolysLock)
            {
                if (housekeepingDone)
                {
                    return;
                }

                DeleteOldCache("polys.mpk");
                DeleteOldCache("polys2.mpk");
                DeleteOldCache("polys3.mpk");
                DeleteOldCache("polys4.mpk");
                DeleteOldCache("polys5.mpk");
                DeleteOldCache("polys6.mpk");
                DeleteOldCache("polys7.mpk");
                DeleteOldCache("polys8.mpk");
                Directory.CreateDirectory(PolysCacheDirectory);
                housekeepingDone = true;
            }
        }

        private static void LoadMissingPolygons(List<(NifRow Row, string ArchivePath, string CacheName)> missing, string zoneDirectory, IRenderReporter reporter)
        {
            reporter.Log("Loading polygons ...", LogLevel.Notice);
            reporter.ProgressStart("Loading polygons ...");

            EnsureHousekeeping();

            // Models from the zone's own archives are not kept for other zones
            var loadedLocally = new Dictionary<string, Polygon[]>(StringComparer.OrdinalIgnoreCase);
            var zoneArchives = zoneDirectory.TrimEnd('\\') + "\\";

            var progressCounter = 0;
            foreach (var (nifRow, nifArchivePath, modelPolyFileName) in missing)
            {
                reporter.ProgressUpdate(100 * progressCounter++ / missing.Count);

                Polygon[] polygons;
                if (nifArchivePath.StartsWith(zoneArchives, StringComparison.OrdinalIgnoreCase))
                {
                    if (!loadedLocally.TryGetValue(modelPolyFileName, out polygons))
                    {
                        polygons = LoadModel(nifRow, nifArchivePath, modelPolyFileName, reporter);
                        if (polygons == null)
                        {
                            continue;
                        }

                        loadedLocally[modelPolyFileName] = polygons;
                    }
                }
                else
                {
                    polygons = LoadSharedModel(nifRow, nifArchivePath, modelPolyFileName, reporter);
                    if (polygons == null)
                    {
                        continue;
                    }
                }

                nifRow.Polygons = polygons;
            }

            reporter.Log("Polygons loaded!", LogLevel.Success);
            reporter.ProgressReset();
        }

        // A failed or missing model is removed again so a later zone can retry
        private static Polygon[] LoadSharedModel(NifRow nifRow, string nifArchivePath, string modelPolyFileName, IRenderReporter reporter)
        {
            var lazyPolygons = Polygons.GetOrAdd(modelPolyFileName, _ => new Lazy<Polygon[]>(() => LoadModel(nifRow, nifArchivePath, modelPolyFileName, reporter), LazyThreadSafetyMode.ExecutionAndPublication));
            Polygon[] polygons;
            try
            {
                polygons = lazyPolygons.Value;
            }
            catch
            {
                Polygons.TryRemove(new KeyValuePair<string, Lazy<Polygon[]>>(modelPolyFileName, lazyPolygons));
                throw;
            }

            if (polygons == null)
            {
                Polygons.TryRemove(new KeyValuePair<string, Lazy<Polygon[]>>(modelPolyFileName, lazyPolygons));
            }

            return polygons;
        }

        /// <summary>
        /// Polygons from the cache file or converted from the NIF, null if the NIF is missing in its archive
        /// </summary>
        private static Polygon[] LoadModel(NifRow nifRow, string nifArchivePath, string modelPolyFileName, IRenderReporter reporter)
        {
            var cacheFile = Path.Combine(PolysCacheDirectory, modelPolyFileName);
            if (File.Exists(cacheFile))
            {
                return NifParser.ReadPoly(new StreamReader(new MemoryStream(File.ReadAllBytes(cacheFile))));
            }

            using var nifFileFromNpk = MpkWrapper.GetFileFromMpk(nifArchivePath, nifRow.Filename);
            if (nifFileFromNpk == null)
            {
                return null;
            }

            reporter.Log(string.Format("Processing {0}...", nifRow.TextualName), LogLevel.Notice);

            using var nifParser = new NifParser();
            nifParser.IsNodeDrawable += node => IsNodeDrawable(nifRow, node);
            nifParser.ResolveTexture = nifRow.ResolveTexture;

            try
            {
                nifParser.Load(nifFileFromNpk);
                WriteCacheFile(cacheFile, temporaryFile => nifParser.Convert(ConvertType.Poly, temporaryFile));
            }
            catch (Exception ex)
            {
                reporter.Log(string.Format("Skipping {0} ({1}): {2}", nifRow.TextualName, nifArchivePath, ex.Message), LogLevel.Warning);
                return Array.Empty<Polygon>();
            }

            return nifParser.GetPolys();
        }

        // Moved into place so other processes never read half a file; if another process wrote it first, ours is dropped
        private static void WriteCacheFile(string cacheFile, Action<string> write)
        {
            var temporaryFile = string.Format("{0}.{1}.tmp", cacheFile, Environment.ProcessId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                write(temporaryFile);
                if (File.Exists(cacheFile))
                {
                    return;
                }

                try
                {
                    File.Move(temporaryFile, cacheFile);
                }
                catch (IOException) when (File.Exists(cacheFile))
                {
                    // Another process moved its own copy into place first, target now exists: not an error
                }
            }
            finally
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
        }

        private static void DeleteOldCache(string fileName)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "data", fileName);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        // Zones ship their own variants of shared models, so the cache name contains the archive folder
        private static string GetCacheName(string archivePath, string variant)
        {
            var name = Path.GetRelativePath(AppSettings.Current.GamePath, Path.ChangeExtension(archivePath, null)).Replace('\\', '_').ToLowerInvariant();
            return name + (variant == null ? "" : "_" + variant) + ".poly";
        }

        private static bool IsNodeDrawable(NifRow nifRow, Niflib.NiAVObject node)
        {
            return nifRow.IsNodeDrawable == null || nifRow.IsNodeDrawable(node.Name.Value);
        }

        private static (List<TreeRow>, List<TreeClusterRow>) LoadTreeData()
        {
            var provider = new System.Globalization.NumberFormatInfo
                           {
                               NumberDecimalSeparator = ".",
                               NumberGroupSeparator = "",
                               NumberGroupSizes = new int[] { 2 }
                           };

            var treeMpk = string.Format("{0}\\zones\\trees\\treemap.mpk", AppSettings.Current.GamePath);
            var treeClusterMpk = string.Format("{0}\\zones\\trees\\tree_clusters.mpk", AppSettings.Current.GamePath);

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

            var treeTextureFile = Path.Combine(AppSettings.Current.GamePath, "zones", "trees", textureName);
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
