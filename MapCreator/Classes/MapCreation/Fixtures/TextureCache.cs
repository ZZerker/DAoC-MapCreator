using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using ImageMagick;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Decoded model textures with mip levels, shared by all zones
    /// </summary>
    internal static class TextureCache
    {
        // A building is rarely more than 100 px wide on a map, larger textures only cost memory
        private const int MAX_TEXTURE_SIZE = 256;

        private static readonly ConcurrentDictionary<string, Lazy<TextureImage>> Textures = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Texture by the name stored in the NIF, null if it cannot be found or read
        /// </summary>
        public static TextureImage Get(string texture, string modelDirectory)
        {
            var file = TextureColors.FindFile(texture, modelDirectory);
            return file == null ? null : Textures.GetOrAdd(file, f => new Lazy<TextureImage>(() => Load(f))).Value;
        }

        public static int Count => Textures.Count;

        public static long Bytes => Textures.Values.Where(t => t.IsValueCreated && t.Value != null).Sum(t => t.Value.Bytes);

        /// <summary>
        /// Forgets the textures of a zone's own folders, no other zone can use them
        /// </summary>
        public static void Release(string zoneDirectory)
        {
            var prefix = zoneDirectory.TrimEnd('\\') + "\\";
            foreach (var file in Textures.Keys.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                Textures.TryRemove(file, out _);
            }
        }

        private static TextureImage Load(string file)
        {
            try
            {
                using var image = new MagickImage(file);
                image.Alpha(AlphaOption.Set);
                if (image.Width > MAX_TEXTURE_SIZE || image.Height > MAX_TEXTURE_SIZE)
                {
                    image.Resize(new MagickGeometry(MAX_TEXTURE_SIZE, MAX_TEXTURE_SIZE) { IgnoreAspectRatio = false });
                }

                return new TextureImage(image, Path.GetFileNameWithoutExtension(file).Contains("grass", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is MagickException or IOException)
            {
                AppLog.Log(string.Format("Unable to read texture {0}: {1}", file, ex.Message), LogLevel.Warning);
                return null;
            }
        }
    }

    /// <summary>
    /// RGBA texture with a mip chain, sampled with wrapping and bilinear filtering
    /// </summary>
    internal sealed class TextureImage
    {
        private readonly int[] widths;
        private readonly int[] heights;
        private readonly byte[][] levels;

        private const double MIP_BIAS = 1.0;

        // Second lattice of the anti-tiling sample: rotated, scaled and shifted so it never lines up with the first
        private const double ROTATION_COS = 0.8253356149096783;
        private const double ROTATION_SIN = 0.5646424733950354;
        private const double SECOND_SCALE = 0.77;
        private const double SECOND_OFFSET_U = 0.31;
        private const double SECOND_OFFSET_V = 0.57;

        // Blend weight noise: one lattice cell is about 2.7 texture repeats
        private const double NOISE_CELL = 2.7;

        private readonly double[] mean;

        public bool AntiTile { get; }

        public int Width => this.widths[0];

        public int Height => this.heights[0];

        public long Bytes => this.levels.Sum(l => (long)l.Length);

        public TextureImage(MagickImage image, bool antiTile = false)
        {
            this.AntiTile = antiTile;
            var count = 1 + (int)Math.Floor(Math.Log2(Math.Max(image.Width, image.Height)));
            this.widths = new int[count];
            this.heights = new int[count];
            this.levels = new byte[count][];

            using var level = (MagickImage)image.Clone();
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    level.Resize(new MagickGeometry(Math.Max(1u, level.Width / 2), Math.Max(1u, level.Height / 2)) { IgnoreAspectRatio = true });
                }

                this.widths[i] = (int)level.Width;
                this.heights[i] = (int)level.Height;
                using var pixels = level.GetPixels();
                this.levels[i] = pixels.ToByteArray(PixelMapping.RGBA);
            }

            if (antiTile)
            {
                this.mean = new double[3];
                var top = this.levels[0];
                for (var i = 0; i < top.Length; i += 4)
                {
                    this.mean[0] += top[i];
                    this.mean[1] += top[i + 1];
                    this.mean[2] += top[i + 2];
                }

                for (var c = 0; c < 3; c++)
                {
                    this.mean[c] /= top.Length / 4.0;
                }
            }
        }

        /// <summary>
        /// Color at the texture coordinate, blended between the two mip levels around texelsPerPixel
        /// </summary>
        public void Sample(double u, double v, double texelsPerPixel, out double r, out double g, out double b, out double a)
        {
            if (!this.AntiTile)
            {
                this.SampleTiled(u, v, texelsPerPixel, out r, out g, out b, out a);
                return;
            }

            // Two samples with a noise weight break the lattice of tiled grass; the division keeps its contrast
            this.SampleTiled(u, v, texelsPerPixel, out r, out g, out b, out a);
            var u2 = (u * ROTATION_COS + v * ROTATION_SIN) * SECOND_SCALE + SECOND_OFFSET_U;
            var v2 = (-u * ROTATION_SIN + v * ROTATION_COS) * SECOND_SCALE + SECOND_OFFSET_V;
            this.SampleTiled(u2, v2, texelsPerPixel, out var r2, out var g2, out var b2, out _);

            var w = Noise(u / NOISE_CELL, v / NOISE_CELL);
            var scale = 1 / Math.Sqrt(w * w + (1 - w) * (1 - w));
            r = Math.Clamp(this.mean[0] + (r * w + r2 * (1 - w) - this.mean[0]) * scale, 0, 255);
            g = Math.Clamp(this.mean[1] + (g * w + g2 * (1 - w) - this.mean[1]) * scale, 0, 255);
            b = Math.Clamp(this.mean[2] + (b * w + b2 * (1 - w) - this.mean[2]) * scale, 0, 255);
        }

        // Smooth value noise in 0..1 from a hashed integer lattice
        private static double Noise(double x, double y)
        {
            var x0 = Math.Floor(x);
            var y0 = Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var ix = (int)x0;
            var iy = (int)y0;

            var top = Hash(ix, iy) + (Hash(ix + 1, iy) - Hash(ix, iy)) * fx;
            var bottom = Hash(ix, iy + 1) + (Hash(ix + 1, iy + 1) - Hash(ix, iy + 1)) * fx;
            return top + (bottom - top) * fy;
        }

        private static double Hash(int x, int y)
        {
            unchecked
            {
                var h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }

        private void SampleTiled(double u, double v, double texelsPerPixel, out double r, out double g, out double b, out double a)
        {
            // The canvas is supersampled, the final map pixel covers one level more
            var level = texelsPerPixel <= 0 ? 0 : Math.Clamp(Math.Log2(texelsPerPixel) + MIP_BIAS, 0, this.levels.Length - 1);
            var lower = (int)level;
            this.SampleLevel(lower, u, v, out r, out g, out b, out a);
            var t = level - lower;
            if (t > 0 && lower + 1 < this.levels.Length)
            {
                this.SampleLevel(lower + 1, u, v, out var r2, out var g2, out var b2, out var a2);
                r += (r2 - r) * t;
                g += (g2 - g) * t;
                b += (b2 - b) * t;
                a += (a2 - a) * t;
            }
        }

        private void SampleLevel(int level, double u, double v, out double r, out double g, out double b, out double a)
        {
            var width = this.widths[level];
            var height = this.heights[level];
            var pixels = this.levels[level];

            var x = (u - Math.Floor(u)) * width - 0.5;
            var y = (v - Math.Floor(v)) * height - 0.5;
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;

            var i00 = Index(x0, y0, width, height);
            var i10 = Index(x0 + 1, y0, width, height);
            var i01 = Index(x0, y0 + 1, width, height);
            var i11 = Index(x0 + 1, y0 + 1, width, height);

            r = Lerp(pixels, i00, i10, i01, i11, 0, fx, fy);
            g = Lerp(pixels, i00, i10, i01, i11, 1, fx, fy);
            b = Lerp(pixels, i00, i10, i01, i11, 2, fx, fy);
            a = Lerp(pixels, i00, i10, i01, i11, 3, fx, fy);
        }

        private static int Index(int x, int y, int width, int height)
        {
            x %= width;
            y %= height;
            if (x < 0)
            {
                x += width;
            }
            if (y < 0)
            {
                y += height;
            }
            return (y * width + x) * 4;
        }

        private static double Lerp(byte[] pixels, int i00, int i10, int i01, int i11, int channel, double fx, double fy)
        {
            var top = pixels[i00 + channel] + (pixels[i10 + channel] - pixels[i00 + channel]) * fx;
            var bottom = pixels[i01 + channel] + (pixels[i11 + channel] - pixels[i01 + channel]) * fx;
            return top + (bottom - top) * fy;
        }
    }
}
