using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace MapCreator.Classes
{
    /// <summary>
    /// Writes the data of the static map website from a render folder: WebP viewer images, thumbnails, label files and zones.json
    /// </summary>
    internal static class SiteExport
    {
        // An empty render (z490 Eden Garden) compresses to a few KB, the smallest real map has 300 KB
        private const long EMPTY_RENDER_BYTES = 65536;
        private const uint WEBP_QUALITY = 85;
        private const uint THUMB_SIZE = 256;

        private static readonly Regex ZoneFile = new(@"^z(\d{3})\.png$", RegexOptions.IgnoreCase);
        private static readonly Regex LevelFile = new(@"^z(\d{3})_(\d{2})\.png$", RegexOptions.IgnoreCase);
        private static readonly Regex ClientName = new(@"^[a-z0-9-]+\z");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        private sealed record SiteFrame(int OffsetX, int OffsetY, int Width);

        private sealed record SiteLevel(int Index, string Name, double? Z, double? Depth);

        private sealed record SiteZone(string Id, string Name, string Realm, string Expansion, string Type, int Size, SiteFrame Frame, bool Labels, List<SiteLevel> Levels);

        private sealed record SiteFile(string Client, string Source, string Generated, List<SiteZone> Zones);

        /// <summary>
        /// True when the client name is usable in URLs
        /// </summary>
        public static bool IsValidClient(string client)
        {
            return ClientName.IsMatch(client);
        }

        /// <summary>
        /// Writes viewer images, thumbnails, labels and zones.json for every rendered zone and returns the zone count
        /// </summary>
        public static int Write(string renderDirectory, string targetDirectory, string gamePath, string client, IRenderReporter reporter)
        {
            if (!Directory.Exists(renderDirectory))
            {
                throw new DirectoryNotFoundException("Render folder not found: " + renderDirectory);
            }

            var viewerDirectory = Path.Combine(targetDirectory, "viewer");
            var thumbDirectory = Path.Combine(targetDirectory, "thumbs");
            var labelDirectory = Path.Combine(targetDirectory, "labels");
            Directory.CreateDirectory(viewerDirectory);
            Directory.CreateDirectory(thumbDirectory);
            Directory.CreateDirectory(labelDirectory);

            var clientZones = ZoneCatalog.Load(gamePath).ToDictionary(z => z.Id);
            if (clientZones.Count == 0)
            {
                reporter.Log("No zones.dat in " + gamePath + ", names and levels come from zones.xml only", LogLevel.Warning);
            }
            var listedZones = DataWrapper.GetAllZones().GroupBy(z => z.Id).ToDictionary(g => g.Key, g => g.First());
            var ids = Directory.GetFiles(renderDirectory, "z???.png")
                               .Where(f => ZoneFile.IsMatch(Path.GetFileName(f)) && new FileInfo(f).Length > EMPTY_RENDER_BYTES)
                               .Select(f => ZoneFile.Match(Path.GetFileName(f)).Groups[1].Value)
                               .Where(id => !MapLabels.IsPlaceholder(clientZones.TryGetValue(id, out var c) ? c.Name : listedZones.TryGetValue(id, out var l) ? l.Name : "Zone " + id))
                               .OrderBy(id => id, StringComparer.Ordinal)
                               .ToList();
            reporter.Log(string.Format("{0} zones to export", ids.Count), LogLevel.Notice);

            var zones = new ConcurrentBag<SiteZone>();
            long bytes = 0;
            var levelImages = 0;
            Parallel.ForEach(ids, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, id =>
            {
                var mapFile = Path.Combine(renderDirectory, "z" + id + ".png");
                int size;
                using (var map = new MagickImage(mapFile))
                {
                    size = (int)map.Width;
                    map.Depth = 8;
                    map.Quality = WEBP_QUALITY;
                    Interlocked.Add(ref bytes, WriteWebP(map, Path.Combine(viewerDirectory, "z" + id + ".webp")));
                    map.Resize(THUMB_SIZE, THUMB_SIZE);
                    Interlocked.Add(ref bytes, WriteWebP(map, Path.Combine(thumbDirectory, "z" + id + ".webp")));
                }

                var levelFiles = Directory.GetFiles(renderDirectory, "z" + id + "_??.png").Where(f => LevelFile.IsMatch(Path.GetFileName(f))).OrderBy(f => f, StringComparer.Ordinal).ToList();
                var areas = levelFiles.Count == 0 ? new List<MapLevel>() : MapLevel.Load(gamePath, id);
                var levels = new List<SiteLevel>();
                foreach (var levelFile in levelFiles)
                {
                    var index = int.Parse(LevelFile.Match(Path.GetFileName(levelFile)).Groups[2].Value);
                    using (var level = new MagickImage(levelFile))
                    {
                        level.Depth = 8;
                        level.Quality = WEBP_QUALITY;
                        Interlocked.Add(ref bytes, WriteWebP(level, Path.Combine(viewerDirectory, string.Format("z{0}_{1:00}.webp", id, index))));
                    }
                    Interlocked.Increment(ref levelImages);
                    var area = areas.FirstOrDefault(a => a.Index == index);
                    levels.Add(new SiteLevel(index, area?.Name, area?.Z, area?.Depth));
                }

                var labelFile = Path.Combine(renderDirectory, "z" + id + ".labels.json");
                var hasLabels = File.Exists(labelFile);
                if (hasLabels)
                {
                    File.Copy(labelFile, Path.Combine(labelDirectory, "z" + id + ".json"), true);
                    Interlocked.Add(ref bytes, new FileInfo(labelFile).Length);
                }

                var frame = MapFrame.Read(id, new FileInfo(mapFile)) ?? MapFrame.Get(id);
                var siteFrame = frame == null ? null : new SiteFrame((int)Math.Round(frame.OffsetX), (int)Math.Round(frame.OffsetY), (int)Math.Round(frame.Width));
                clientZones.TryGetValue(id, out var clientZone);
                var isListed = listedZones.TryGetValue(id, out var listed);
                var realm = isListed && listed.Realm != ZoneCatalog.REALM_NAME ? listed.Realm : null;
                var expansion = isListed && listed.Expansion != ZoneCatalog.EXPANSION_NAME ? listed.Expansion : null;
                zones.Add(new SiteZone(id, clientZone?.Name ?? (isListed ? listed.Name : "Zone " + id), realm, expansion, isListed ? listed.Type : clientZone?.Type, size, siteFrame, hasLabels,
                                       levels.Count == 0 ? null : levels));
            });

            var site = new SiteFile(client, Path.GetFileName(renderDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                    DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), zones.OrderBy(z => z.Id, StringComparer.Ordinal).ToList());
            var json = JsonSerializer.Serialize(site, JsonOptions);
            File.WriteAllText(Path.Combine(targetDirectory, "zones.json"), json);
            bytes += Encoding.UTF8.GetByteCount(json);

            reporter.Log(string.Format("{0} zones, {1} level images, {2:N0} bytes written", site.Zones.Count, levelImages, bytes), LogLevel.Success);
            return site.Zones.Count;
        }

        private static long WriteWebP(MagickImage image, string file)
        {
            image.Write(file, MagickFormat.WebP);
            return new FileInfo(file).Length;
        }
    }
}
