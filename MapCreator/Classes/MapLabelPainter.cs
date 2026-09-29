using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;
using ImageMagick.Drawing;
using MapCreator.Classes.Rendering;

namespace MapCreator.Classes
{
    /// <summary>
    /// Draws the labels of a zone onto a copy of its map, in the fonts of the TokaZerk UI (data\fonts).
    /// Sizes are given for a 512 px map and scale with the map; text is drawn at the final size so it stays sharp.
    /// </summary>
    internal static class MapLabelPainter
    {
        // Maps below this size only keep the labels of priority 1
        private const int SMALL_MAP = 512;

        private static readonly string FontDirectory = Path.Combine(AppContext.BaseDirectory, "data", "fonts");
        private static readonly string BoldFont = Path.Combine(FontDirectory, "TokaFontBold.ttf");
        private static readonly string RegularFont = Path.Combine(FontDirectory, "TokaFontRegular.ttf");

        // Muted colors that sit in the map instead of on top of it
        private static readonly MagickColor Text = MagickColor.FromRgb(238, 230, 207);
        private static readonly MagickColor Halo = MagickColor.FromRgb(16, 12, 8);
        private static readonly MagickColor NeighborText = MagickColor.FromRgb(228, 204, 140);
        private static readonly MagickColor BossText = MagickColor.FromRgb(236, 168, 112);
        private static readonly MagickColor ArtifactText = MagickColor.FromRgb(206, 170, 240);
        private static readonly MagickColor TrainerText = MagickColor.FromRgb(214, 214, 190);
        private static readonly MagickColor LegendBack = MagickColor.FromRgba(16, 12, 8, 170);

        private static readonly Dictionary<int, MagickColor> RealmText = new()
        {
            { 1, MagickColor.FromRgb(232, 168, 152) },
            { 2, MagickColor.FromRgb(170, 198, 232) },
            { 3, MagickColor.FromRgb(170, 214, 150) }
        };

        private static readonly Dictionary<string, MagickColor> IconFill = new()
        {
            { "entrance", MagickColor.FromRgb(70, 58, 44) },
            { "portal", MagickColor.FromRgb(60, 70, 96) },
            { "porter", MagickColor.FromRgb(60, 70, 96) },
            { "boss", MagickColor.FromRgb(150, 60, 40) },
            { "artifact", MagickColor.FromRgb(110, 60, 150) },
            { "dock", MagickColor.FromRgb(60, 80, 100) }
        };

        // Service NPCs are colored dots explained in a legend, like on the old UI city maps
        private static readonly (string Kind, string Name, MagickColor Color)[] Services =
        {
            ("smith", "Smith", MagickColor.FromRgb(220, 60, 50)),
            ("enchanter", "Enchanter", MagickColor.FromRgb(80, 200, 90)),
            ("healer", "Healer", MagickColor.FromRgb(70, 200, 220)),
            ("vault", "Vault", MagickColor.FromRgb(240, 150, 50))
        };

        private static readonly Dictionary<string, (bool Bold, double Points)> Styles = new()
        {
            { "keep", (true, 11) },
            { "tower", (true, 9) },
            { "place", (true, 10) },
            { "town", (true, 10) },
            { "neighbor", (true, 11) },
            { "entrance", (true, 10) },
            { "portal", (true, 10) },
            { "porter", (true, 10) },
            { "boss", (true, 10) },
            { "artifact", (true, 10) },
            { "dock", (true, 9) },
            { "stable", (true, 9) },
            { "trainer", (false, 9) }
        };

        // Drawing order: what matters most claims its place first
        private static readonly string[] Order = { "neighbor", "keep", "town", "portal", "porter", "entrance", "tower", "boss", "artifact", "place", "stable", "trainer", "dock" };

        private sealed record Box(double Left, double Top, double Right, double Bottom)
        {
            public bool Overlaps(Box other)
            {
                return this.Left < other.Right && this.Right > other.Left && this.Top < other.Bottom && this.Bottom > other.Top;
            }
        }

        /// <summary>
        /// Writes the map with its labels into the label directory, scaled to the label size first, when the settings ask for it
        /// </summary>
        public static void WriteLabeled(RenderSettings settings, FileInfo mapFile, IReadOnlyList<MapLabels.Label> labels)
        {
            if (string.IsNullOrEmpty(settings.LabelDirectory) || !mapFile.Exists)
            {
                return;
            }

            var directory = Path.Combine(settings.TargetPath, settings.LabelDirectory);
            Directory.CreateDirectory(directory);
            using var map = new MagickImage(mapFile.FullName);
            var size = settings.LabelSize > 0 ? (uint)settings.LabelSize : map.Width;
            if (map.Width != size)
            {
                map.Resize(size, size);
            }
            Draw(map, labels);
            map.Depth = 8;
            map.Write(Path.Combine(directory, mapFile.Name));
        }

        public static void Draw(MagickImage map, IReadOnlyList<MapLabels.Label> labels)
        {
            var size = (double)map.Width;
            var scale = size / SMALL_MAP;
            var maxPriority = size < SMALL_MAP ? 1 : 2;
            var margin = Math.Max(4, size / 96);
            var legend = PlaceLegend(map, labels, scale, margin);
            var boxes = legend.Box == null ? new List<Box>() : new List<Box> { legend.Box };
            var shown = labels.Where(l => l.Priority <= maxPriority)
                              .OrderBy(l => l.Priority).ThenBy(l => Array.IndexOf(Order, l.Kind) is var i && i >= 0 ? i : Order.Length).ToList();

            // Marks first, so no text covers a dot or icon; a mark stays even when its text finds no room
            var iconRadius = Math.Max(2, Math.Round(3 * scale));
            foreach (var label in shown)
            {
                var x = label.X * size;
                var y = label.Y * size;
                var service = Services.FirstOrDefault(s => s.Kind == label.Kind);
                if (service.Kind != null)
                {
                    var r = Math.Max(2, 3 * scale);
                    new Drawables().FillColor(service.Color).StrokeColor(Halo).StrokeWidth(Math.Max(1, scale / 2)).Circle(x, y, x + r, y).Draw(map);
                    boxes.Add(new Box(x - r, y - r, x + r, y + r));
                }
                else if (IconFill.TryGetValue(label.Kind, out var fill))
                {
                    DrawIcon(map, label.Kind, x, y, iconRadius, fill);
                    boxes.Add(new Box(x - iconRadius - 1, y - iconRadius - 1, x + iconRadius + 1, y + iconRadius + 1));
                }
            }

            foreach (var label in shown.Where(l => Services.All(s => s.Kind != l.Kind)))
            {
                var x = label.X * size;
                var y = label.Y * size;
                var (bold, points) = Styles.TryGetValue(label.Kind, out var style) ? style : (true, 10);
                var font = bold ? BoldFont : RegularFont;
                var pointSize = Math.Max(8, Math.Round(points * scale));
                var metrics = new Drawables().Font(font).FontPointSize(pointSize).FontTypeMetrics(label.Text);
                if (metrics == null)
                {
                    continue;
                }
                var width = metrics.TextWidth;
                var height = metrics.Ascent - metrics.Descent;
                var color = label.Kind switch
                {
                    "keep" or "tower" => RealmText.TryGetValue(label.Realm, out var realm) ? realm : Text,
                    "neighbor" => NeighborText,
                    "boss" => BossText,
                    "artifact" => ArtifactText,
                    "trainer" => TrainerText,
                    _ => Text
                };

                if (label.Kind == "neighbor")
                {
                    DrawNeighbor(map, label, font, pointSize, width, height, metrics.Ascent, color, margin, boxes);
                    continue;
                }

                // Keeps are labeled clear of their walls, other points right next to their mark
                var gap = scale * (label.Kind == "keep" ? 14 : label.Kind == "tower" ? 8 : label.Kind == "trainer" ? 0 : 6);
                var candidates = label.Kind == "trainer"
                                     ? new[] { (x - width / 2, y - height / 2) }
                                     : new[] { (x - width / 2, y + gap), (x - width / 2, y - gap - height), (x + gap, y - height / 2), (x - gap - width, y - height / 2) };
                foreach (var (left, top) in candidates)
                {
                    var box = new Box(left, top, left + width, top + height);
                    if (box.Left < 0 || box.Top < 0 || box.Right > size || box.Bottom > size || boxes.Any(b => b.Overlaps(box)))
                    {
                        continue;
                    }

                    DrawText(map, label.Text, font, pointSize, left, top + metrics.Ascent, color, scale);
                    boxes.Add(box);
                    break;
                }
            }

            if (legend.Box != null)
            {
                DrawLegend(map, legend.Services, legend.Box, scale);
            }
        }

        private static void DrawText(MagickImage map, string text, string font, double pointSize, double left, double baseline, MagickColor color, double scale)
        {
            var halo = Math.Max(1.5, scale * 0.9);
            new Drawables().Font(font).FontPointSize(pointSize).StrokeColor(Halo).StrokeWidth(halo * 2).FillColor(Halo).Text(left, baseline, text).Draw(map);
            new Drawables().Font(font).FontPointSize(pointSize).FillColor(color).Text(left, baseline, text).Draw(map);
        }

        private static void DrawIcon(MagickImage map, string kind, double x, double y, double r, MagickColor fill)
        {
            var drawables = new Drawables().FillColor(fill).StrokeColor(Text).StrokeWidth(Math.Max(1, r / 3));
            if (kind is "entrance" or "dock")
            {
                drawables.Circle(x, y, x + r, y);
            }
            else
            {
                drawables.Polygon(new PointD(x, y - r - 1), new PointD(x + r + 1, y), new PointD(x, y + r + 1), new PointD(x - r - 1, y));
            }
            drawables.Draw(map);
        }

        /// <summary>
        /// Neighbor zones along the border with an arrow pointing out; on the side borders the text runs along the edge
        /// </summary>
        private static void DrawNeighbor(MagickImage map, MapLabels.Label label, string font, double pointSize, double width, double height, double ascent, MagickColor color,
                                         double margin, List<Box> boxes)
        {
            var size = (double)map.Width;
            var arrow = Math.Round(height * 0.7);
            var stripWidth = (uint)Math.Ceiling(width + arrow + 4);
            var stripHeight = (uint)Math.Ceiling(height + 4);
            using var strip = new MagickImage(MagickColors.Transparent, stripWidth, stripHeight);
            var sideways = label.Edge is "west" or "east";
            var pointsOut = label.Edge is "north" or "west";
            var arrowX = pointsOut ? arrow / 2 + 1 : width + arrow / 2 + 3;
            var textX = pointsOut ? arrow + 2 : 1;
            var tip = sideways ? "north" : label.Edge;
            DrawArrow(strip, tip, arrowX, stripHeight / 2d, arrow / 2 - 1, color);
            DrawText(strip, label.Text, font, pointSize, textX, 2 + ascent, color, size / SMALL_MAP);
            if (sideways)
            {
                strip.Rotate(label.Edge == "west" ? -90 : 90);
            }

            var x = label.X * size;
            var y = label.Y * size;
            double left, top;
            if (sideways)
            {
                left = label.Edge == "west" ? margin : size - strip.Width - margin;
                top = Math.Clamp(y - strip.Height / 2d, margin, size - strip.Height - margin);
            }
            else
            {
                left = Math.Clamp(x - strip.Width / 2d, margin, size - strip.Width - margin);
                top = label.Edge == "north" ? margin : size - strip.Height - margin;
            }

            var box = new Box(left, top, left + strip.Width, top + strip.Height);
            if (boxes.Any(b => b.Overlaps(box)))
            {
                return;
            }
            map.Composite(strip, (int)Math.Round(left), (int)Math.Round(top), CompositeOperator.Over);
            boxes.Add(box);
        }

        private static void DrawArrow(MagickImage image, string edge, double x, double y, double r, MagickColor color)
        {
            var points = edge switch
            {
                "north" => new[] { new PointD(x, y - r), new PointD(x + r, y + r * 0.6), new PointD(x - r, y + r * 0.6) },
                "south" => new[] { new PointD(x, y + r), new PointD(x + r, y - r * 0.6), new PointD(x - r, y - r * 0.6) },
                "west" => new[] { new PointD(x - r, y), new PointD(x + r * 0.6, y - r), new PointD(x + r * 0.6, y + r) },
                _ => new[] { new PointD(x + r, y), new PointD(x - r * 0.6, y - r), new PointD(x - r * 0.6, y + r) }
            };
            new Drawables().FillColor(color).StrokeColor(Halo).StrokeWidth(1).Polygon(points).Draw(image);
        }

        /// <summary>
        /// Legend of the service dots on the map, in the corner with the fewest labels; no box when there are none
        /// </summary>
        private static (Box Box, List<(string Kind, string Name, MagickColor Color)> Services) PlaceLegend(MagickImage map, IReadOnlyList<MapLabels.Label> labels, double scale, double margin)
        {
            var shown = Services.Where(s => labels.Any(l => l.Kind == s.Kind)).ToList();
            if (shown.Count == 0 || map.Width < SMALL_MAP)
            {
                return (null, shown);
            }

            var size = (double)map.Width;
            var line = LegendPointSize(scale) * 1.3;
            var width = shown.Max(s => new Drawables().Font(RegularFont).FontPointSize(LegendPointSize(scale)).FontTypeMetrics(s.Name)?.TextWidth ?? 0) + line * 1.6;
            var height = line * shown.Count + line * 0.4;
            var corners = new[]
            {
                new Box(margin, size - margin - height, margin + width, size - margin),
                new Box(size - margin - width, size - margin - height, size - margin, size - margin),
                new Box(margin, margin, margin + width, margin + height),
                new Box(size - margin - width, margin, size - margin, margin + height)
            };
            var best = corners.OrderBy(c => labels.Count(l => l.X * size >= c.Left - line && l.X * size <= c.Right + line && l.Y * size >= c.Top - line && l.Y * size <= c.Bottom + line)).First();
            return (best, shown);
        }

        private static double LegendPointSize(double scale)
        {
            return Math.Max(8, Math.Round(9 * scale));
        }

        private static void DrawLegend(MagickImage map, List<(string Kind, string Name, MagickColor Color)> shown, Box box, double scale)
        {
            var pointSize = LegendPointSize(scale);
            var line = pointSize * 1.3;
            new Drawables().FillColor(LegendBack).StrokeColor(LegendBack).Rectangle(box.Left, box.Top, box.Right, box.Bottom).Draw(map);
            for (var i = 0; i < shown.Count; i++)
            {
                var y = box.Top + line * (i + 0.7);
                var r = pointSize / 3;
                new Drawables().FillColor(shown[i].Color).StrokeColor(Halo).StrokeWidth(1).Circle(box.Left + line * 0.6, y, box.Left + line * 0.6 + r, y).Draw(map);
                DrawText(map, shown[i].Name, RegularFont, pointSize, box.Left + line * 1.1, y + pointSize / 3, Text, scale);
            }
        }
    }
}
