using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;

namespace MapCreator.Classes
{
    /// <summary>
    /// Stitches rendered zone maps (zNNN.png) into region maps rNNN.png like the client's ui\maps\rNNN.dds: the outdoor zones of a
    /// region placed by zones.dat, framed by the square around them, centered; labeled with the zone names
    /// </summary>
    internal static class RegionMaps
    {
        private static readonly MagickColor Background = MagickColor.FromRgb(30, 30, 30);

        /// <summary>
        /// Left, top and side of the square the client frames a region with, in the units of the zone areas
        /// </summary>
        internal static (double Left, double Top, double Side) GetFrame(IReadOnlyCollection<MapLabels.ZoneArea> zones)
        {
            var left = zones.Min(z => z.Left);
            var top = zones.Min(z => z.Top);
            var width = zones.Max(z => z.Left + z.Width) - left;
            var height = zones.Max(z => z.Top + z.Height) - top;
            var side = Math.Max(width, height);
            return (left - (side - width) / 2, top - (side - height) / 2, side);
        }

        /// <summary>
        /// Writes one map per region with at least one rendered zone and returns how many were written
        /// </summary>
        public static int Write(string renderDirectory, string targetDirectory, int size, string gamePath, IRenderReporter reporter)
        {
            Directory.CreateDirectory(targetDirectory);
            var regions = MapLabels.LoadZones(gamePath)
                                   .Where(z => z.Type == MapLabels.OUTDOOR && !MapLabels.IsPlaceholder(z.Name ?? ""))
                                   .GroupBy(z => z.Region)
                                   .OrderBy(g => g.Key);
            var written = 0;
            foreach (var region in regions)
            {
                // Single-zone regions (battlegrounds, instances) have no region map in the client
                var zones = region.ToList();
                if (zones.Count < 2)
                {
                    continue;
                }

                var rendered = zones.Where(z => File.Exists(Path.Combine(renderDirectory, "z" + z.Id + ".png"))).ToList();
                if (rendered.Count == 0)
                {
                    continue;
                }

                var (left, top, side) = GetFrame(zones);
                var scale = size / side;
                using var map = MagickWrapper.NewImage(Background, size, size);
                var labels = new List<MapLabels.Label>();
                foreach (var zone in rendered)
                {
                    using var zoneMap = new MagickImage(Path.Combine(renderDirectory, "z" + zone.Id + ".png"));
                    var x = (int)Math.Round((zone.Left - left) * scale);
                    var y = (int)Math.Round((zone.Top - top) * scale);
                    var width = (int)Math.Round((zone.Left + zone.Width - left) * scale) - x;
                    var height = (int)Math.Round((zone.Top + zone.Height - top) * scale) - y;
                    zoneMap.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                    map.Composite(zoneMap, x, y, CompositeOperator.SrcOver);
                    labels.Add(new MapLabels.Label("zone", zone.Name, (zone.Left + zone.Width / 2 - left) / side, (zone.Top + zone.Height / 2 - top) / side, 1, 0, null));
                }

                MapLabelPainter.Draw(map, labels);
                map.Depth = 8;
                map.Write(Path.Combine(targetDirectory, "r" + region.Key.ToString("000") + ".png"));
                reporter.Log(string.Format("Region {0:000}: {1} of {2} zones", region.Key, rendered.Count, zones.Count), LogLevel.Notice);
                written++;
            }
            return written;
        }
    }
}
