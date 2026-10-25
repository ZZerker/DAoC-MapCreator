using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MapCreator.Classes.MapCreation
{
    /// <summary>
    /// Point light of a dungeon in zone coordinates; color 0..1 per channel, radius in world units
    /// </summary>
    internal sealed record DungeonLight(double X, double Y, double Z, double Red, double Green, double Blue, double Radius);

    /// <summary>
    /// Dungeon lights the client adds at runtime: lights.csv in the zone's dat archive (x, y, z, type) and the types in
    /// zones\textures\lightdef.csv (id, name, red, green, blue, unknown, radius, flag)
    /// </summary>
    internal static class DungeonLights
    {
        private static readonly Lazy<Dictionary<int, (double Red, double Green, double Blue, double Radius)>> Types = new(LoadTypes);

        public static List<DungeonLight> Read(string datMpk)
        {
            var lights = new List<DungeonLight>();
            using var csv = MpkWrapper.GetFileFromMpk(datMpk, "lights.csv");
            if (csv == null)
            {
                return lights;
            }

            string row;
            while ((row = csv.ReadLine()) != null)
            {
                var fields = row.Split(',');
                if (fields.Length < 4
                    || !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                    || !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var type)
                    || !Types.Value.TryGetValue((int)type, out var light))
                {
                    continue;
                }

                lights.Add(new DungeonLight(x, y, z, light.Red, light.Green, light.Blue, light.Radius));
            }
            return lights;
        }

        private static Dictionary<int, (double, double, double, double)> LoadTypes()
        {
            var types = new Dictionary<int, (double, double, double, double)>();
            var file = Path.Combine(AppSettings.Current.GamePath, "zones", "textures", "lightdef.csv");
            if (!File.Exists(file))
            {
                return types;
            }

            foreach (var fields in File.ReadLines(file).Select(l => l.Split(',')))
            {
                if (fields.Length >= 7 && int.TryParse(fields[0], out var id)
                    && int.TryParse(fields[2], out var red) && int.TryParse(fields[3], out var green) && int.TryParse(fields[4], out var blue)
                    && double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
                {
                    types[id] = (red / 255d, green / 255d, blue / 255d, radius);
                }
            }
            return types;
        }
    }
}
