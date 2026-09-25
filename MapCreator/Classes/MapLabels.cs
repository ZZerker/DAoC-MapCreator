using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapCreator.Classes
{
    /// <summary>
    /// Names and landmarks of a zone, written next to the map as zNNN.labels.json. tools\png_to_dds.py draws them at the final map size.
    /// Neighbor zones come from zones.dat, keeps from data\Keeps.csv, everything else from data\Landmarks.csv.
    /// </summary>
    internal static class MapLabels
    {
        private const double BLOCK = 8192;

        private static readonly string[] TowerWords = { "tower", "outpost", "spire" };

        public sealed record Label(string Kind, string Text, double X, double Y, int Priority, int Realm, string Edge);

        private sealed record ZoneArea(string Id, string Name, int Region, int Type, double Left, double Top, double Width, double Height);

        public static void Write(string zoneId, FileInfo mapFile)
        {
            var labels = Get(zoneId);
            var file = Path.Combine(mapFile.DirectoryName, Path.GetFileNameWithoutExtension(mapFile.Name) + ".labels.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new { zone = zoneId, labels }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        public static List<Label> Get(string zoneId)
        {
            var labels = new List<Label>();
            var zones = LoadZones();
            var zone = zones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null)
            {
                return labels;
            }

            labels.AddRange(GetNeighbors(zone, zones));
            labels.AddRange(GetKeeps(zone));
            labels.AddRange(GetLandmarks(zone));
            return labels;
        }

        /// <summary>
        /// Outdoor zones of the same region that share an edge, labeled in the middle of the shared part
        /// </summary>
        private static IEnumerable<Label> GetNeighbors(ZoneArea zone, List<ZoneArea> zones)
        {
            foreach (var other in zones.Where(z => z.Id != zone.Id && z.Region == zone.Region && z.Type == 0))
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

        private static IEnumerable<Label> GetKeeps(ZoneArea zone)
        {
            var file = Path.Combine(System.Windows.Forms.Application.StartupPath, "data", "Keeps.csv");
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
                var isTower = TowerWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
                yield return new Label(isTower ? "tower" : "keep", name, x / zone.Width, y / zone.Height, isTower ? 3 : 1, int.Parse(fields[3]), null);
            }
        }

        private static IEnumerable<Label> GetLandmarks(ZoneArea zone)
        {
            var file = Path.Combine(System.Windows.Forms.Application.StartupPath, "data", "Landmarks.csv");
            if (!File.Exists(file))
            {
                yield break;
            }

            foreach (var fields in File.ReadLines(file).Skip(1).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(';')))
            {
                if (fields.Length < 6 || fields[0] != zone.Id.TrimStart('0'))
                {
                    continue;
                }

                var x = double.Parse(fields[1], CultureInfo.InvariantCulture);
                var y = double.Parse(fields[2], CultureInfo.InvariantCulture);
                yield return new Label(fields[3], fields[4], x / zone.Width, y / zone.Height, int.Parse(fields[5]), 0, null);
            }
        }

        private static List<ZoneArea> LoadZones()
        {
            var zones = new List<ZoneArea>();
            var zonesDat = DatFile.FromMpk(Path.Combine(Properties.Settings.Default.game_path, "zones", "zones.mpk"), "zones.dat");
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
