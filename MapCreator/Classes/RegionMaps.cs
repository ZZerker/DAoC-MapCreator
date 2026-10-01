using System;
using System.Collections.Generic;
using System.Linq;
using ImageMagick;

namespace MapCreator.Classes
{
    /// <summary>
    /// Stitches rendered zone maps into a region map like the client's ui\maps\rNNN.dds: the zones placed by zones.dat,
    /// framed by the square around them, centered; labeled with the zone names
    /// </summary>
    internal static class RegionMaps
    {
        private static readonly MagickColor Background = MagickColor.FromRgb(30, 30, 30);

        /// <summary>
        /// One zone of a region: its place in zones.dat, its name and its rendered map
        /// </summary>
        internal sealed record Tile(MapLabels.ZoneArea Area, string Name, string MapFile);

        /// <summary>
        /// Where a tile landed on the region map, in pixels
        /// </summary>
        internal sealed record Placement(int Left, int Top, int Width, int Height);

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
        /// The region map at the given size and where each tile went, in the order of the tiles
        /// </summary>
        public static MagickImage Stitch(IReadOnlyList<Tile> tiles, int size, out List<Placement> placements)
        {
            var (left, top, side) = GetFrame(tiles.Select(t => t.Area).ToList());
            var scale = size / side;
            var map = MagickWrapper.NewImage(Background, size, size);
            var labels = new List<MapLabels.Label>();
            placements = new List<Placement>();
            foreach (var tile in tiles)
            {
                var area = tile.Area;
                var x = (int)Math.Round((area.Left - left) * scale);
                var y = (int)Math.Round((area.Top - top) * scale);
                var width = (int)Math.Round((area.Left + area.Width - left) * scale) - x;
                var height = (int)Math.Round((area.Top + area.Height - top) * scale) - y;
                placements.Add(new Placement(x, y, width, height));
                using var zoneMap = new MagickImage(tile.MapFile);
                zoneMap.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                map.Composite(zoneMap, x, y, CompositeOperator.SrcOver);
                labels.Add(new MapLabels.Label("zone", tile.Name, (area.Left + area.Width / 2 - left) / side, (area.Top + area.Height / 2 - top) / side, 1, 0, null));
            }

            MapLabelPainter.Draw(map, labels);
            map.Depth = 8;
            return map;
        }
    }
}
