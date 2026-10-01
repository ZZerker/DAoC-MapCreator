using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ImageMagick;

namespace MapCreator.Classes
{
    /// <summary>
    /// Builds the TokaZerk UI map set from a render folder, one folder per size: labeled zone maps (proxy zones get their source's map),
    /// dungeon levels, zoomed rooms (data\ZoomAreas.csv), region maps for every client rNNN.dds, and areas.dat and regions.dat
    /// written from the client's files with our frames and layout, so the marker fits our maps
    /// </summary>
    internal static class UiMaps
    {
        // An empty render (z490 Eden Garden) compresses to a few KB, the smallest real map has 300 KB
        private const long EMPTY_RENDER_BYTES = 65536;

        // Maps and Maps_large take 512, Maps_small 256
        internal static readonly int[] Sizes = { 512, 256 };

        private static readonly Regex LevelFile = new(@"^z(\d{3})_(\d{2})\.png$", RegexOptions.IgnoreCase);

        // Descriptions in the client files are not UTF-8
        private static readonly Encoding DatEncoding = Encoding.Latin1;

        /// <summary>
        /// One level or zoomed room: cut from the source map when Crop is set, the whole level map otherwise
        /// </summary>
        private sealed record Level(string ZoneId, int Index, string MapFile, MagickGeometry Crop);

        public static int Write(string renderDirectory, string targetDirectory, string gamePath, IRenderReporter reporter)
        {
            var clientMaps = Path.Combine(gamePath, "ui", "maps");
            var zonesDat = DatFile.FromMpk(Path.Combine(gamePath, "zones", "zones.mpk"), "zones.dat");
            var areas = MapLabels.LoadZones(gamePath).ToDictionary(z => z.Id);
            var sources = GetSources(zonesDat, renderDirectory);
            reporter.Log(string.Format("{0} zone maps, {1} of them proxy zones", sources.Count, sources.Count(s => s.Key != s.Value)), LogLevel.Notice);

            var clientAreas = ReadDat(Path.Combine(clientMaps, "areas.dat"));
            var levels = new List<Level>();
            var areaSections = new SortedDictionary<string, List<string>>();
            foreach (var (id, source) in sources)
            {
                AddLevels(id, source, renderDirectory, clientAreas, levels, areaSections);
            }
            reporter.Log(string.Format("{0} level maps in {1} zones", levels.Count, areaSections.Count), LogLevel.Notice);

            var regionsText = File.ReadAllText(Path.Combine(clientMaps, "regions.dat"), DatEncoding);
            var regions = new DatFile(new StringReader(regionsText));
            var written = 0;
            foreach (var size in Sizes)
            {
                var directory = Path.Combine(targetDirectory, size.ToString());
                Directory.CreateDirectory(directory);
                Parallel.ForEach(sources, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                                 s => WriteZoneMap(s.Key, s.Value, renderDirectory, directory, size));
                foreach (var level in levels)
                {
                    WriteLevel(level, directory, size);
                }

                var rewritten = WriteRegionMaps(regions, sources, areas, zonesDat, renderDirectory, clientMaps, directory, size, reporter);
                File.WriteAllText(Path.Combine(directory, "regions.dat"), RewriteSections(regionsText, rewritten), DatEncoding);
                File.WriteAllText(Path.Combine(directory, "areas.dat"), AreasText(areaSections), DatEncoding);
                written += sources.Count;
                reporter.Log(string.Format("{0} px: {1} zone maps, {2} level maps, {3} regions rewritten", size, sources.Count, levels.Count, rewritten.Count), LogLevel.Success);
            }
            return written;
        }

        /// <summary>
        /// Zone id to the id whose render it shows: the zone itself, or the zone a proxy zone copies. Placeholders and empty renders are left out.
        /// </summary>
        private static SortedDictionary<string, string> GetSources(DatFile zonesDat, string renderDirectory)
        {
            var sources = new SortedDictionary<string, string>();
            foreach (var section in zonesDat.Sections.Where(s => s.StartsWith("zone") && int.TryParse(s[4..], out _)))
            {
                var id = int.Parse(section[4..]).ToString("000");
                if (MapLabels.IsPlaceholder(zonesDat.Get(section, "name")))
                {
                    continue;
                }

                var source = File.Exists(MainMap(renderDirectory, id)) ? id
                                 : int.TryParse(zonesDat.Get(section, "proxy_zone"), out var proxy) ? proxy.ToString("000") : null;
                var mapFile = source == null ? null : new FileInfo(MainMap(renderDirectory, source));
                if (mapFile is { Exists: true, Length: > EMPTY_RENDER_BYTES })
                {
                    sources[id] = source;
                }
            }
            return sources;
        }

        private static string MainMap(string renderDirectory, string zoneId)
        {
            return Path.Combine(renderDirectory, "z" + zoneId + ".png");
        }

        private static MapFrame GetFrame(string renderDirectory, string sourceId)
        {
            return MapFrame.Read(sourceId, new FileInfo(MainMap(renderDirectory, sourceId))) ?? MapFrame.Get(sourceId);
        }

        /// <summary>
        /// Rendered levels of the source (areas.dat of the client gives their heights and names), else the zoomed rooms of data\ZoomAreas.csv
        /// </summary>
        private static void AddLevels(string id, string source, string renderDirectory, DatFile clientAreas, List<Level> levels, SortedDictionary<string, List<string>> areaSections)
        {
            var frame = GetFrame(renderDirectory, source);
            if (frame == null)
            {
                return;
            }

            var side = Math.Round(frame.Width).ToString(CultureInfo.InvariantCulture);
            var lines = new List<string>();
            var files = Directory.GetFiles(renderDirectory, "z" + source + "_??.png").Where(f => LevelFile.IsMatch(Path.GetFileName(f))).OrderBy(f => f).ToList();
            var section = "zone" + source;
            for (var i = 0; i < files.Count; i++)
            {
                var index = int.Parse(LevelFile.Match(Path.GetFileName(files[i])).Groups[2].Value);
                levels.Add(new Level(id, index, files[i], null));
                lines.AddRange(new[]
                {
                    Key(index, "left", "0"), Key(index, "top", "0"), Key(index, "z", clientAreas.Get(section, string.Format("area{0}_z", index))),
                    Key(index, "width", side), Key(index, "height", side), Key(index, "depth", clientAreas.Get(section, string.Format("area{0}_depth", index))),
                    Key(index, "name", clientAreas.Get(section, string.Format("area{0}_name", index))), Key(index, "desc", "")
                });
            }

            if (files.Count == 0)
            {
                var rooms = ReadZoomAreas().Where(r => r.Zone == source).ToList();
                var mapSize = rooms.Count == 0 ? 0 : new MagickImageInfo(MainMap(renderDirectory, source)).Width;
                for (var i = 0; i < rooms.Count; i++)
                {
                    var (_, x, y, roomSide, name) = rooms[i];
                    var scale = mapSize / frame.Width;
                    var crop = new MagickGeometry((int)Math.Round((x - frame.OffsetX) * scale), (int)Math.Round((y - frame.OffsetY) * scale), (uint)Math.Round(roomSide * scale), (uint)Math.Round(roomSide * scale));
                    levels.Add(new Level(id, i, MainMap(renderDirectory, source), crop));
                    lines.AddRange(new[]
                    {
                        Key(i, "left", Math.Round(x - frame.OffsetX).ToString(CultureInfo.InvariantCulture)), Key(i, "top", Math.Round(y - frame.OffsetY).ToString(CultureInfo.InvariantCulture)),
                        Key(i, "width", roomSide.ToString(CultureInfo.InvariantCulture)), Key(i, "height", roomSide.ToString(CultureInfo.InvariantCulture)), Key(i, "name", name)
                    });
                }
                if (rooms.Count == 0)
                {
                    return;
                }
                lines.Insert(0, "area_count=" + rooms.Count);
            }
            else
            {
                lines.Insert(0, "area_count=" + files.Count);
            }
            areaSections["zone" + id] = lines;
        }

        private static string Key(int area, string key, string value)
        {
            return string.Format("area{0}_{1}={2}", area, key, value);
        }

        private static List<(string Zone, double X, double Y, double Side, string Name)> ReadZoomAreas()
        {
            var file = Path.Combine(AppContext.BaseDirectory, "data", "ZoomAreas.csv");
            if (!File.Exists(file))
            {
                return new List<(string, double, double, double, string)>();
            }

            return File.ReadLines(file).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(';')).Where(f => f.Length >= 5)
                       .Select(f => (f[0].Trim(), double.Parse(f[1], CultureInfo.InvariantCulture), double.Parse(f[2], CultureInfo.InvariantCulture),
                                     double.Parse(f[3], CultureInfo.InvariantCulture), f[4].Trim()))
                       .ToList();
        }

        private static void WriteZoneMap(string id, string source, string renderDirectory, string directory, int size)
        {
            var mapFile = new FileInfo(MainMap(renderDirectory, source));
            var labels = MapLabels.Read(mapFile) ?? MapLabels.Write(source, mapFile);
            using var map = new MagickImage(mapFile.FullName);
            map.Resize((uint)size, (uint)size);
            MapLabelPainter.Draw(map, labels);
            map.Depth = 8;
            map.Write(Path.Combine(directory, "z" + id + ".png"));
        }

        private static void WriteLevel(Level level, string directory, int size)
        {
            using var map = new MagickImage(level.MapFile);
            if (level.Crop != null)
            {
                map.Crop(level.Crop);
                map.ResetPage();
            }
            map.Resize((uint)size, (uint)size);
            map.Depth = 8;
            map.Write(Path.Combine(directory, string.Format("z{0}_{1:00}.png", level.ZoneId, level.Index)));
        }

        /// <summary>
        /// Writes rNNN.png for every region the client has a map for and returns the regions.dat sections to replace: those region maps,
        /// and every single-zone region of a city or dungeon we have a frame for (the marker on its zone map)
        /// </summary>
        private static Dictionary<string, List<string>> WriteRegionMaps(DatFile regions, SortedDictionary<string, string> sources, Dictionary<string, MapLabels.ZoneArea> areas,
                                                                     DatFile zonesDat, string renderDirectory, string clientMaps, string directory, int size, IRenderReporter reporter)
        {
            var rewritten = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in regions.Sections.Where(s => s.StartsWith("region", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[6..], out _)))
            {
                var regionId = int.Parse(section[6..]).ToString("000");
                if (!int.TryParse(regions.Get(section, "zone_count"), out var count) || count < 1)
                {
                    continue;
                }

                // Values can end in an inline comment (Malmohus: "116;")
                var zoneIds = Enumerable.Range(0, count).Select(i => int.TryParse(regions.Get(section, string.Format("zone{0}_number", i)).Split(';')[0], out var n) ? n.ToString("000") : null).ToList();
                var hasMap = File.Exists(Path.Combine(clientMaps, "r" + regionId + ".dds"));
                if (count == 1)
                {
                    var id = zoneIds[0];
                    var frame = id != null && sources.TryGetValue(id, out var source) ? GetFrame(renderDirectory, source) : null;
                    if (frame == null)
                    {
                        continue;
                    }
                    if (hasMap)
                    {
                        File.Copy(Path.Combine(directory, "z" + id + ".png"), Path.Combine(directory, "r" + regionId + ".png"), true);
                    }
                    rewritten[section] = new List<string>
                    {
                        "zone_count=1", "zone_width=" + size, "zone_height=" + size, "zone0_number=" + id, "zone0_left=0", "zone0_top=0",
                        "zone0_offsetx=" + Round(frame.OffsetX), "zone0_offsety=" + Round(frame.OffsetY), "zone0_width=" + Round(frame.Width), "zone0_height=" + Round(frame.Width),
                        "zone0_desc=" + regions.Get(section, "zone0_desc")
                    };
                    continue;
                }

                if (!hasMap)
                {
                    continue;
                }

                var tiles = new List<RegionMaps.Tile>();
                var indexes = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    var id = zoneIds[i];
                    if (id == null || !sources.TryGetValue(id, out var source))
                    {
                        continue;
                    }
                    var area = areas.GetValueOrDefault(id) ?? areas.GetValueOrDefault(source);
                    if (area == null)
                    {
                        continue;
                    }
                    var name = zonesDat.Get("zone" + id, "name");
                    tiles.Add(new RegionMaps.Tile(area, string.IsNullOrEmpty(name) ? area.Name : name, MainMap(renderDirectory, source)));
                    indexes.Add(i);
                }
                if (tiles.Count == 0)
                {
                    reporter.Log(string.Format("Region {0}: none of its zones is rendered", regionId), LogLevel.Warning);
                    continue;
                }

                using var map = RegionMaps.Stitch(tiles, size, out var placements);
                map.Write(Path.Combine(directory, "r" + regionId + ".png"));
                var lines = new List<string> { "zone_count=" + tiles.Count, "zone_width=" + placements[0].Width, "zone_height=" + placements[0].Height };
                for (var t = 0; t < tiles.Count; t++)
                {
                    var i = indexes[t];
                    lines.Add(string.Format("zone{0}_number={1}", t, zoneIds[i]));
                    lines.Add(string.Format("zone{0}_left={1}", t, placements[t].Left));
                    lines.Add(string.Format("zone{0}_top={1}", t, placements[t].Top));
                    lines.Add(string.Format("zone{0}_desc={1}", t, regions.Get(section, string.Format("zone{0}_desc", i))));
                }
                rewritten[section] = lines;
                if (tiles.Count < count)
                {
                    reporter.Log(string.Format("Region {0}: {1} of {2} zones rendered", regionId, tiles.Count, count), LogLevel.Warning);
                }
            }
            return rewritten;
        }

        private static string Round(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The dat file with the given sections replaced; comments and every other section stay as they are
        /// </summary>
        internal static string RewriteSections(string text, Dictionary<string, List<string>> sections)
        {
            var result = new StringBuilder();
            var skipping = false;
            foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                var row = line.Trim();
                if (row.StartsWith('['))
                {
                    var name = row.TrimStart('[').Split(']')[0];
                    skipping = sections.TryGetValue(name, out var lines);
                    result.Append(line).Append("\r\n");
                    if (skipping)
                    {
                        foreach (var replaced in lines)
                        {
                            result.Append(replaced).Append("\r\n");
                        }
                    }
                    continue;
                }

                if (skipping && row.Length > 0 && !row.StartsWith(';'))
                {
                    continue;
                }
                result.Append(line).Append("\r\n");
            }
            return result.ToString().TrimEnd() + "\r\n";
        }

        private static string AreasText(SortedDictionary<string, List<string>> sections)
        {
            var text = new StringBuilder("; areas.dat written by MapCreator: levels of dungeons and zoomed rooms, positions in the frame of the zone map\r\n");
            foreach (var (section, lines) in sections)
            {
                text.Append("\r\n[").Append(section).Append("]\r\n");
                foreach (var line in lines)
                {
                    text.Append(line).Append("\r\n");
                }
            }
            return text.ToString();
        }

        private static DatFile ReadDat(string file)
        {
            return new DatFile(new StringReader(File.Exists(file) ? File.ReadAllText(file, DatEncoding) : ""));
        }
    }
}
