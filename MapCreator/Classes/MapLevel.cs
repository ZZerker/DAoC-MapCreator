using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MapCreator.Classes
{
    /// <summary>
    /// One level of a multi level zone, from the client's ui\maps\areas.dat.
    /// Z is the level's floor, it shows what lies between Z and Z + Depth (zone heights). In Darkness Falls
    /// every Z + Depth is the Z of the level above.
    /// </summary>
    public sealed record MapLevel(int Index, string Name, double Z, double Depth, double Width)
    {
        public static List<MapLevel> Load(string gamePath, string zoneId)
        {
            var levels = new List<MapLevel>();
            var areasFile = Path.Combine(gamePath, "ui", "maps", "areas.dat");
            if (!File.Exists(areasFile))
            {
                return levels;
            }

            DatFile areas;
            using (var reader = new StreamReader(areasFile))
            {
                areas = new DatFile(reader);
            }

            var section = "zone" + zoneId;
            if (!int.TryParse(areas.Get(section, "area_count"), out var count))
            {
                return levels;
            }

            for (var i = 0; i < count; i++)
            {
                if (TryGet(areas, section, i, "z", out var z) && TryGet(areas, section, i, "depth", out var depth) && TryGet(areas, section, i, "width", out var width))
                {
                    levels.Add(new MapLevel(i, areas.Get(section, string.Format("area{0}_name", i)), z, depth, width));
                }
            }
            return levels;
        }

        private static bool TryGet(DatFile areas, string section, int area, string key, out double value)
        {
            return double.TryParse(areas.Get(section, string.Format("area{0}_{1}", area, key)), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
