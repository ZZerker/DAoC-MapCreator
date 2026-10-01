using System;
using System.Linq;
using MapCreator.Classes.MapCreation;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class HeightOcclusionTests
    {
        private const int SIZE = 64;
        private const double STRENGTH = 0.35;

        // Wall ring 4 px wide (height 100) around a 24 x 24 courtyard, ground 0 outside
        private static float[] Factors()
        {
            var heights = new float[SIZE * SIZE];
            for (var y = 16; y < 48; y++)
            {
                for (var x = 16; x < 48; x++)
                {
                    var courtyard = x >= 20 && x < 44 && y >= 20 && y < 44;
                    heights[y * SIZE + x] = courtyard ? 0 : 100;
                }
            }
            return HeightOcclusion.Compute(heights, SIZE, SIZE, 4, 50, STRENGTH);
        }

        [Fact]
        public void NonSquareInputKeepsFarGroundAtOne()
        {
            var heights = new float[40 * 16];
            for (var y = 6; y < 10; y++)
            {
                for (var x = 18; x < 22; x++)
                {
                    heights[y * 40 + x] = 100;
                }
            }
            var factors = HeightOcclusion.Compute(heights, 40, 16, 4, 50, STRENGTH);

            Assert.Equal(1f, factors[0]);
            Assert.Equal(1f, factors[15 * 40 + 39]);
            Assert.True(factors[8 * 40 + 16] < 1f);
        }

        [Fact]
        public void WallTopStaysAndCourtyardNextToTheWallDarkens()
        {
            var factors = Factors();

            Assert.Equal(1f, factors[32 * SIZE + 17]);
            Assert.True(factors[32 * SIZE + 20] < 1f);
        }

        [Fact]
        public void GroundUnderTheWallEdgeDarkens()
        {
            var heights = new float[SIZE * SIZE];
            for (var y = 16; y < 48; y++)
            {
                for (var x = 16; x < 48; x++)
                {
                    heights[y * SIZE + x] = 100;
                }
            }
            var map = new OcclusionMap(HeightOcclusion.Blur(heights, SIZE, SIZE, 4), new float[heights.Length], 0, 0, SIZE, SIZE, 50, STRENGTH, 50, STRENGTH);

            Assert.Equal(1f, map.At(16, 32, 100));
            Assert.True(map.At(16, 32, 0) < 1f);
            Assert.True(map.At(16, 32, 0) < map.At(12, 32, 0));
            Assert.Equal(1f, map.At(0, 0, 0));
        }

        [Fact]
        public void WallBaseIsDarkerThanItsTopAndGroundOffsetsCount()
        {
            var heights = new float[SIZE * SIZE];
            for (var y = 16; y < 48; y++)
            {
                for (var x = 30; x < 34; x++)
                {
                    heights[y * SIZE + x] = 100;
                }
            }
            var ground = Enumerable.Repeat(1000f, heights.Length).ToArray();
            var map = new OcclusionMap(HeightOcclusion.Blur(heights, SIZE, SIZE, 4), ground, 0, 0, SIZE, SIZE, 50, STRENGTH, 50, STRENGTH);

            Assert.True(map.At(30, 32, 1000) < map.At(30, 32, 1050));
            Assert.Equal(1f, map.At(30, 32, 1100));
        }

        [Fact]
        public void FarGroundStaysAndNothingGetsDarkerThanTheStrength()
        {
            var factors = Factors();

            Assert.Equal(1f, factors[0]);
            Assert.True(factors.All(f => f >= 1 - STRENGTH - 1e-6));
        }
    }
}
