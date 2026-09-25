using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MapCreator.Classes
{
    /// <summary>
    /// Square a city or dungeon map shows, in zone coordinates: the top left corner and the side.
    /// From data\MapFrames.csv; zones without an entry are framed by their models.
    /// </summary>
    public sealed record MapFrame(double OffsetX, double OffsetY, double Width)
    {
        // City and dungeon models are placed around 0; zone x = ORIGIN - model x, zone y = model y + ORIGIN
        // (checked on the zone jumps of Darkness Falls, which fit the frame table exactly)
        public const double ORIGIN = 23807;

        private static readonly Lazy<Dictionary<string, MapFrame>> Frames = new Lazy<Dictionary<string, MapFrame>>(Load);

        public static MapFrame Get(string zoneId)
        {
            return Frames.Value.TryGetValue(zoneId, out var frame) ? frame : null;
        }

        private static Dictionary<string, MapFrame> Load()
        {
            var frames = new Dictionary<string, MapFrame>();
            var file = Path.Combine(System.Windows.Forms.Application.StartupPath, "data", "MapFrames.csv");
            if (!File.Exists(file))
            {
                return frames;
            }

            foreach (var line in File.ReadLines(file).Where(l => l.Length > 0 && !l.StartsWith('#')))
            {
                var fields = line.Split(';');
                if (fields.Length >= 4
                    && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetX)
                    && double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetY)
                    && double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                {
                    frames[fields[0].Trim()] = new MapFrame(offsetX, offsetY, width);
                }
            }
            return frames;
        }
    }
}
