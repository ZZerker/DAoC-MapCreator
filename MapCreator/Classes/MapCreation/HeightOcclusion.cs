using System;
using System.Threading.Tasks;
using ImageMagick;

namespace MapCreator.Classes.MapCreation
{
    /// <summary>
    /// Blurred heights above the ground and the ground itself for a rectangle of the map (X, Y, Width, Height in map pixels).
    /// FullHeight and Strength apply to the ground, SurfaceFullHeight and SurfaceStrength to points of the models.
    /// </summary>
    internal sealed record OcclusionMap(float[] Blurred, float[] Ground, int X, int Y, int Width, int Height, double FullHeight, double Strength, double SurfaceFullHeight, double SurfaceStrength)
    {
        /// <summary>
        /// Factor of a model point at an absolute height, 1 = unchanged, 1 outside of the rectangle
        /// </summary>
        public float At(int x, int y, double z)
        {
            var column = x - this.X;
            var row = y - this.Y;
            if (column < 0 || row < 0 || column >= this.Width || row >= this.Height)
            {
                return 1;
            }
            var i = row * this.Width + column;
            return HeightOcclusion.Factor(this.Blurred[i], z - this.Ground[i], this.SurfaceFullHeight, this.SurfaceStrength);
        }
    }

    /// <summary>
    /// Ambient occlusion from a height map: points lower than their blurred surroundings get darker
    /// </summary>
    internal static class HeightOcclusion
    {
        private const int BLUR_PASSES = 3;

        /// <summary>
        /// Box radius of one blur pass: three box passes approximate a Gaussian of sigma radius / 2 (box width = sqrt(12 sigma^2 / passes + 1))
        /// </summary>
        public static int BoxRadius(double radius)
        {
            var sigma = radius / 2;
            return Math.Max(1, (int)Math.Round((Math.Sqrt(12 * sigma * sigma / BLUR_PASSES + 1) - 1) / 2));
        }

        public static float Factor(float blurred, double height, double fullHeight, double strength)
        {
            return (float)(1 - strength * Math.Clamp((blurred - height) / fullHeight, 0, 1));
        }

        /// <summary>
        /// Heights are per pixel (width x height, row-major), above the ground in map units, no NaN
        /// </summary>
        public static float[] Blur(float[] heights, int width, int height, double radius)
        {
            var blurred = (float[])heights.Clone();
            var scratch = new float[heights.Length];

            var boxRadius = BoxRadius(radius);
            for (var pass = 0; pass < BLUR_PASSES; pass++)
            {
                BoxBlur(blurred, scratch, width, height, boxRadius, true);
                BoxBlur(scratch, blurred, width, height, boxRadius, false);
            }
            return blurred;
        }

        /// <summary>
        /// Factor per pixel for the surface of the height map itself, 1 = unchanged
        /// </summary>
        public static float[] Compute(float[] heights, int width, int height, double radius, double fullHeight, double strength)
        {
            var blurred = Blur(heights, width, height, radius);
            var factors = new float[heights.Length];
            Parallel.For(0, factors.Length, i => factors[i] = Factor(blurred[i], heights[i], fullHeight, strength));
            return factors;
        }

        /// <summary>
        /// Darkens the ground (height 0) inside the map's rectangle, alpha stays
        /// </summary>
        public static void Apply(MagickImage image, OcclusionMap map)
        {
            using var pixels = image.GetPixels();
            var channels = (int)image.ChannelCount;
            var values = pixels.GetArea(map.X, map.Y, (uint)map.Width, (uint)map.Height);
            if (values == null)
            {
                return;
            }

            Parallel.For(0, map.Blurred.Length, i =>
            {
                var factor = Factor(map.Blurred[i], 0, map.FullHeight, map.Strength);
                for (var c = 0; c < Math.Min(channels, 3); c++)
                {
                    values[i * channels + c] = (ushort)Math.Min(values[i * channels + c] * factor, ushort.MaxValue);
                }
            });
            pixels.SetArea(map.X, map.Y, (uint)map.Width, (uint)map.Height, values);
        }

        // One box blur pass along rows or columns with the border pixel repeated
        private static void BoxBlur(float[] source, float[] target, int width, int height, int radius, bool horizontal)
        {
            var boxWidth = 2 * radius + 1;
            var length = horizontal ? width : height;
            var step = horizontal ? 1 : width;
            Parallel.For(0, horizontal ? height : width, line =>
            {
                var start = horizontal ? line * width : line;
                double sum = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    sum += source[start + Math.Clamp(k, 0, length - 1) * step];
                }

                for (var k = 0; k < length; k++)
                {
                    target[start + k * step] = (float)(sum / boxWidth);
                    sum += source[start + Math.Min(k + radius + 1, length - 1) * step] - source[start + Math.Max(k - radius, 0) * step];
                }
            });
        }
    }
}
