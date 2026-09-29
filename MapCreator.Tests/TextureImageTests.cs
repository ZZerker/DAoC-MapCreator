using System;
using ImageMagick;
using MapCreator.Classes.MapCreation.Fixtures;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class TextureImageTests
    {
        private const int SIZE = 64;

        private static MagickImage CreateImage()
        {
            var data = new byte[SIZE * SIZE * 4];
            for (var y = 0; y < SIZE; y++)
            {
                for (var x = 0; x < SIZE; x++)
                {
                    var i = (y * SIZE + x) * 4;
                    data[i] = (byte)(x * 4);
                    data[i + 1] = (byte)(y * 4);
                    data[i + 2] = (byte)(((x / 8 + y / 8) % 2) * 200);
                    data[i + 3] = 255;
                }
            }

            return new MagickImage(data, new PixelReadSettings((uint)SIZE, (uint)SIZE, StorageType.Char, PixelMapping.RGBA));
        }

        private static double[] TextureMean()
        {
            var sum = new double[3];
            for (var y = 0; y < SIZE; y++)
            {
                for (var x = 0; x < SIZE; x++)
                {
                    sum[0] += x * 4;
                    sum[1] += y * 4;
                    sum[2] += ((x / 8 + y / 8) % 2) * 200;
                }
            }

            return new[] { sum[0] / (SIZE * SIZE), sum[1] / (SIZE * SIZE), sum[2] / (SIZE * SIZE) };
        }

        [Fact]
        public void PlainTextureWrapsAndKeepsTexelValues()
        {
            using var image = CreateImage();
            var texture = new TextureImage(image);

            Assert.False(texture.AntiTile);
            texture.Sample(10.5 / SIZE, 20.5 / SIZE, 0, out var r, out var g, out var b, out var a);
            Assert.Equal(40, r, 6);
            Assert.Equal(80, g, 6);
            Assert.Equal(200, b, 6);
            Assert.Equal(255, a, 6);

            for (var i = 0; i < 50; i++)
            {
                var u = 0.013 + i * 0.0197;
                var v = 0.071 + i * 0.0311;
                texture.Sample(u, v, 0, out var r1, out var g1, out var b1, out _);
                texture.Sample(u + 1, v, 0, out var r2, out var g2, out var b2, out _);
                Assert.Equal(r1, r2, 6);
                Assert.Equal(g1, g2, 6);
                Assert.Equal(b1, b2, 6);
            }
        }

        [Fact]
        public void AntiTileIsNotPeriodic()
        {
            using var image = CreateImage();
            var texture = new TextureImage(image, true);

            Assert.True(texture.AntiTile);
            var different = 0;
            const int PROBES = 200;
            for (var i = 0; i < PROBES; i++)
            {
                var u = 0.013 + i * 0.0197;
                var v = 0.071 + i * 0.0311;
                texture.Sample(u, v, 0, out var r1, out var g1, out var b1, out _);
                texture.Sample(u + 1, v, 0, out var r2, out var g2, out var b2, out _);
                if (Math.Abs(r1 - r2) > 0.5 || Math.Abs(g1 - g2) > 0.5 || Math.Abs(b1 - b2) > 0.5)
                {
                    different++;
                }
            }

            Assert.True(different > PROBES * 0.8, "Anti-tiled samples one repeat apart were equal at " + (PROBES - different) + " of " + PROBES + " probes");
        }

        [Fact]
        public void AntiTileIsDeterministicAndInRange()
        {
            using var image = CreateImage();
            var texture = new TextureImage(image, true);

            for (var i = 0; i < 500; i++)
            {
                var u = -3 + i * 0.0731;
                var v = -2 + i * 0.0533;
                texture.Sample(u, v, 1.5, out var r1, out var g1, out var b1, out var a1);
                texture.Sample(u, v, 1.5, out var r2, out var g2, out var b2, out var a2);
                Assert.Equal(r1, r2);
                Assert.Equal(g1, g2);
                Assert.Equal(b1, b2);
                Assert.Equal(a1, a2);
                Assert.InRange(r1, 0, 255);
                Assert.InRange(g1, 0, 255);
                Assert.InRange(b1, 0, 255);
            }
        }

        [Fact]
        public void AntiTileKeepsTheTextureMean()
        {
            using var image = CreateImage();
            var texture = new TextureImage(image, true);
            var expected = TextureMean();

            var sum = new double[3];
            const int GRID = 120;
            for (var y = 0; y < GRID; y++)
            {
                for (var x = 0; x < GRID; x++)
                {
                    texture.Sample(x * 0.0837, y * 0.0913, 0, out var r, out var g, out var b, out _);
                    sum[0] += r;
                    sum[1] += g;
                    sum[2] += b;
                }
            }

            for (var c = 0; c < 3; c++)
            {
                var actual = sum[c] / (GRID * GRID);
                Assert.True(Math.Abs(actual - expected[c]) <= expected[c] * 0.1, "Channel " + c + " mean " + actual + " is not within 10 percent of " + expected[c]);
            }
        }
    }
}
