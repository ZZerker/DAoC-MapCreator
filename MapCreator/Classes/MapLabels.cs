using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapCreator.Classes
{
    /// <summary>
    /// Names and landmarks of a zone, written next to the map as zNNN.labels.json; MapLabelPainter draws them.
    /// Neighbor zones come from zones.dat, keeps from data\Keeps.csv, everything else from the point files (Landmarks.csv by hand,
    /// the others built by tools\build_poi_data.ps1). Positions are fractions of the map: of the zone outdoors, of the frame in cities and dungeons.
    /// </summary>
    internal static class MapLabels
    {
        private const double BLOCK = 8192;

        private const int OUTDOOR = 0;

        private static readonly string[] TowerWords = { "tower", "outpost", "spire" };

        private static readonly (string Word, string Short)[] TowerShortNames = { ("Guardtower", "GT"), ("Guard Tower", "GT"), ("Watchtower", "WT"), ("Watch Tower", "WT"), ("Outpost", "OP"), ("Spire", "SP") };

        // Old frontier zones only get their neighbors: their client points (keep teleports named DFEntrance) are unreliable
        private static readonly HashSet<string> OldFrontiers = new() { "011", "012", "014", "015", "111", "112", "113", "115", "210", "211", "212", "214" };

        private static readonly string[] PointFiles = { "Landmarks.csv", "Bosses.csv", "CityNpcs.csv", "Entrances.csv", "Towns.csv", "Artifacts.csv" };

        public sealed record Label(string Kind, string Text, double X, double Y, int Priority, int Realm, string Edge);

        private sealed record LabelFile(string Zone, List<Label> Labels);

        private sealed record ZoneArea(string Id, string Name, int Region, int Type, double Left, double Top, double Width, double Height);

        /// <summary>
        /// Writes zNNN.labels.json next to the map and returns the labels. Cities and dungeons need their frame: the one given,
        /// else the zNNN.frame.json next to the map, else data\MapFrames.csv; without one they get no labels.
        /// </summary>
        public static List<Label> Write(string zoneId, FileInfo mapFile, bool keeps = true, MapFrame frame = null)
        {
            var labels = Get(zoneId, keeps, frame ?? MapFrame.Read(zoneId, mapFile) ?? MapFrame.Get(zoneId));
            var file = Path.Combine(mapFile.DirectoryName, Path.GetFileNameWithoutExtension(mapFile.Name) + ".labels.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new { zone = zoneId, labels }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return labels;
        }

        /// <summary>
        /// Reads the zNNN.labels.json next to the map; null when it is missing or unreadable, an empty list when it holds no labels
        /// </summary>
        public static List<Label> Read(FileInfo mapFile)
        {
            var file = Path.Combine(mapFile.DirectoryName, Path.GetFileNameWithoutExtension(mapFile.Name) + ".labels.json");
            try
            {
                if (!File.Exists(file))
                {
                    return null;
                }

                var content = JsonSerializer.Deserialize<LabelFile>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                return content?.Labels?.Where(l => l != null).ToList();
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static List<Label> Get(string zoneId, bool keeps = true, MapFrame frame = null)
        {
            var labels = new List<Label>();
            var zones = LoadZones();
            var zone = zones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null)
            {
                return labels;
            }

            if (zone.Type != OUTDOOR)
            {
                return frame == null ? labels : GetPoints(zone, frame).ToList();
            }

            labels.AddRange(GetNeighbors(zone, zones));
            if (OldFrontiers.Contains(zone.Id))
            {
                return labels;
            }
            if (keeps)
            {
                labels.AddRange(GetKeeps(zone));
            }
            labels.AddRange(GetPoints(zone, null));
            return labels;
        }

        /// <summary>
        /// Outdoor zones of the same region that share an edge, labeled in the middle of the shared part
        /// </summary>
        private static IEnumerable<Label> GetNeighbors(ZoneArea zone, List<ZoneArea> zones)
        {
            foreach (var other in zones.Where(z => z.Id != zone.Id && z.Region == zone.Region && z.Type == OUTDOOR && !IsPlaceholder(z.Name)))
            {
                var top = Math.Max(zone.Top, other.Top);
                var bottom = Math.Min(zone.Top + zone.Height, other.Top + other.Height);
                var left = Math.Max(zone.Left, other.Left);
                var right = Math.Min(zone.Left + zone.Width, other.Left + other.Width);

                if (bottom > top && other.Left == zone.Left + zone.Width)
                {
                    yield return new Label("neighbor", other.Name, 1, ((top + bottom) / 2 - zone.Top) / zone.Height, 1, 0, "east");
                }
                else if (bottom > top && other.Left + other.Width == zone.Left)
                {
                    yield return new Label("neighbor", other.Name, 0, ((top + bottom) / 2 - zone.Top) / zone.Height, 1, 0, "west");
                }
                else if (right > left && other.Top == zone.Top + zone.Height)
                {
                    yield return new Label("neighbor", other.Name, ((left + right) / 2 - zone.Left) / zone.Width, 1, 1, 0, "south");
                }
                else if (right > left && other.Top + other.Height == zone.Top)
                {
                    yield return new Label("neighbor", other.Name, ((left + right) / 2 - zone.Left) / zone.Width, 0, 1, 0, "north");
                }
            }
        }

        // zones.dat keeps enabled placeholders such as Dummy Zone (157) and TestBG (242); no player gets there
        private static bool IsPlaceholder(string name)
        {
            return name.Contains("dummy", StringComparison.OrdinalIgnoreCase) || name.Contains("test", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<Label> GetKeeps(ZoneArea zone)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "data", "Keeps.csv");
            if (!File.Exists(file))
            {
                yield break;
            }

            var seen = new HashSet<string>();
            foreach (var fields in File.ReadLines(file).Skip(1).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(',')))
            {
                if (fields.Length < 8 || fields[0] != zone.Region.ToString() || !seen.Add(fields[1]))
                {
                    continue;
                }

                var x = double.Parse(fields[4], CultureInfo.InvariantCulture) - zone.Left;
                var y = double.Parse(fields[5], CultureInfo.InvariantCulture) - zone.Top;
                if (x < 0 || y < 0 || x >= zone.Width || y >= zone.Height)
                {
                    continue;
                }

                var name = fields[2];
                // Every battleground has the three realm portal keeps; their names say nothing a player needs
                if (name.Contains("Portal Keep", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var isTower = TowerWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
                yield return new Label(isTower ? "tower" : "keep", isTower ? ShortTowerName(name) : name, x / zone.Width, y / zone.Height, isTower ? 2 : 1, int.Parse(fields[3]), null);
            }
        }

        private static string ShortTowerName(string name)
        {
            foreach (var (word, abbreviation) in TowerShortNames)
            {
                if (name.EndsWith(" " + word, StringComparison.OrdinalIgnoreCase))
                {
                    return name[..^word.Length] + abbreviation;
                }
            }
            return name;
        }

        private static IEnumerable<Label> GetPoints(ZoneArea zone, MapFrame frame)
        {
            var zoneNumber = int.Parse(zone.Id).ToString();
            foreach (var name in PointFiles)
            {
                var file = Path.Combine(AppContext.BaseDirectory, "data", name);
                if (!File.Exists(file))
                {
                    continue;
                }

                foreach (var fields in File.ReadLines(file).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(';')))
                {
                    if (fields.Length < 6 || fields[0] != zoneNumber)
                    {
                        continue;
                    }

                    var x = double.Parse(fields[1], CultureInfo.InvariantCulture);
                    var y = double.Parse(fields[2], CultureInfo.InvariantCulture);
                    x = frame == null ? x / zone.Width : (x - frame.OffsetX) / frame.Width;
                    y = frame == null ? y / zone.Height : (y - frame.OffsetY) / frame.Width;
                    if (x < 0 || y < 0 || x > 1 || y > 1)
                    {
                        continue;
                    }
                    yield return new Label(fields[3], fields[4], x, y, int.Parse(fields[5]), 0, null);
                }
            }
        }

        private static List<ZoneArea> LoadZones()
        {
            var zones = new List<ZoneArea>();
            var zonesDat = DatFile.FromMpk(Path.Combine(AppSettings.Current.GamePath, "zones", "zones.mpk"), "zones.dat");
            if (zonesDat == null)
            {
                return zones;
            }

            foreach (var section in zonesDat.Sections.Where(s => s.StartsWith("zone") && int.TryParse(s[4..], out _)))
            {
                if (zonesDat.Get(section, "enabled") == "0"
                    || !int.TryParse(zonesDat.Get(section, "region"), out var region)
                    || !int.TryParse(zonesDat.Get(section, "region_offset_x"), out var offsetX)
                    || !int.TryParse(zonesDat.Get(section, "region_offset_y"), out var offsetY)
                    || !int.TryParse(zonesDat.Get(section, "width"), out var width)
                    || !int.TryParse(zonesDat.Get(section, "height"), out var height))
                {
                    continue;
                }

                int.TryParse(zonesDat.Get(section, "type"), out var type);
                var id = int.Parse(section[4..]).ToString("000");
                zones.Add(new ZoneArea(id, zonesDat.Get(section, "name"), region, type, offsetX * BLOCK, offsetY * BLOCK, width * BLOCK, height * BLOCK));
            }
            return zones;
        }
    }
}
