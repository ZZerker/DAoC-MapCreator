using System;
using System.Collections.Generic;
using System.Linq;
using ImageMagick;
using SharpDX;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Draws textured triangles of one model into a pixel buffer. ImageMagick cannot map textures onto triangles.
    /// </summary>
    internal sealed class FixtureCanvas
    {
        // Rendered at a higher resolution and scaled down, for smooth edges
        private const int SUPER_SAMPLING = 2;

        private const int ALPHA_THRESHOLD = 128;

        private readonly int width;
        private readonly int height;
        private readonly int bufferWidth;
        private readonly int bufferHeight;
        private readonly byte[] pixels;

        public FixtureCanvas(int width, int height)
        {
            this.width = width;
            this.height = height;
            this.bufferWidth = width * SUPER_SAMPLING;
            this.bufferHeight = height * SUPER_SAMPLING;
            this.pixels = new byte[this.bufferWidth * this.bufferHeight * 4];
        }

        /// <summary>
        /// Fills a triangle with its texture, or with the color if there is no texture. Light scales the color.
        /// </summary>
        public void FillTriangle(IEnumerable<PointD> coordinates, Vector2[] uvs, TextureImage texture, MagickColor color, double light)
        {
            var points = coordinates.Select(p => new PointD(p.X * SUPER_SAMPLING, p.Y * SUPER_SAMPLING)).ToArray();
            if (points.Length != 3)
            {
                return;
            }

            var area = Edge(points[0], points[1], points[2]);
            if (Math.Abs(area) < 1e-9)
            {
                return;
            }

            var textured = texture != null && uvs != null;
            var texelsPerPixel = 0d;
            if (textured)
            {
                var uvArea = Math.Abs((uvs[1].X - uvs[0].X) * (uvs[2].Y - uvs[0].Y) - (uvs[2].X - uvs[0].X) * (uvs[1].Y - uvs[0].Y)) * texture.Width * texture.Height;
                texelsPerPixel = Math.Sqrt(uvArea / Math.Abs(area));
            }

            var solidR = color.R / 257d * light;
            var solidG = color.G / 257d * light;
            var solidB = color.B / 257d * light;

            var minX = Math.Max(0, (int)Math.Floor(points.Min(p => p.X)));
            var maxX = Math.Min(this.bufferWidth - 1, (int)Math.Ceiling(points.Max(p => p.X)));
            var minY = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
            var maxY = Math.Min(this.bufferHeight - 1, (int)Math.Ceiling(points.Max(p => p.Y)));

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new PointD(x + 0.5, y + 0.5);
                    var w0 = Edge(points[1], points[2], p) / area;
                    var w1 = Edge(points[2], points[0], p) / area;
                    var w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                    {
                        continue;
                    }

                    double r = solidR, g = solidG, b = solidB;
                    if (textured)
                    {
                        var u = w0 * uvs[0].X + w1 * uvs[1].X + w2 * uvs[2].X;
                        var v = w0 * uvs[0].Y + w1 * uvs[1].Y + w2 * uvs[2].Y;
                        texture.Sample(u, v, texelsPerPixel, out r, out g, out b, out var a);

                        // Leaves and fences are cut out by the alpha channel
                        if (a < ALPHA_THRESHOLD)
                        {
                            continue;
                        }

                        r *= light;
                        g *= light;
                        b *= light;
                    }

                    var index = (y * this.bufferWidth + x) * 4;
                    this.pixels[index] = ToByte(r);
                    this.pixels[index + 1] = ToByte(g);
                    this.pixels[index + 2] = ToByte(b);
                    this.pixels[index + 3] = 255;
                }
            }
        }

        public MagickImage ToImage()
        {
            var image = new MagickImage(this.pixels, new PixelReadSettings((uint)this.bufferWidth, (uint)this.bufferHeight, StorageType.Char, PixelMapping.RGBA));
            image.FilterType = FilterType.Box;
            image.Resize(new MagickGeometry((uint)this.width, (uint)this.height) { IgnoreAspectRatio = true });
            return image;
        }

        private static double Edge(PointD a, PointD b, PointD p)
        {
            return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Clamp(Math.Round(value), 0, 255);
        }
    }
}
