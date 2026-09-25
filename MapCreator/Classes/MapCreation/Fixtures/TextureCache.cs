using System;
using System.Collections.Concurrent;
using System.IO;
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

                return new TextureImage(image);
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

        public int Width => this.widths[0];

        public int Height => this.heights[0];

        public TextureImage(MagickImage image)
        {
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
                this.levels[i] = level.GetPixels().ToByteArray(PixelMapping.RGBA);
            }
        }

        /// <summary>
        /// Color at the texture coordinate; texelsPerPixel selects the mip level
        /// </summary>
        public void Sample(double u, double v, double texelsPerPixel, out double r, out double g, out double b, out double a)
        {
            var level = texelsPerPixel <= 1 ? 0 : Math.Min(this.levels.Length - 1, (int)Math.Log2(texelsPerPixel));
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
