using System;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Terrain height of every map pixel in map units, to hide the parts of models that lie below the ground
    /// </summary>
    internal sealed class TerrainHeights
    {
        // The height map has one sample per 256 units; flat models on the ground must not flicker through it
        private const double TOLERANCE = 64;

        private readonly int size;
        private readonly float[] heights;
        private readonly float tolerance;

        public TerrainHeights(ZoneConfiguration zoneConfiguration)
        {
            var heightmap = zoneConfiguration.Heightmap.HeightmapScaled;
            this.size = (int)heightmap.Width;
            this.heights = new float[this.size * this.size];
            var toleranceMap = zoneConfiguration.ZoneCoordinateToMapCoordinate(TOLERANCE);
            this.tolerance = (float)toleranceMap;
            using var pixels = heightmap.GetPixelsUnsafe();
            for (var y = 0; y < this.size; y++)
            {
                for (var x = 0; x < this.size; x++)
                {
                    this.heights[y * this.size + x] = (float)(zoneConfiguration.ZoneCoordinateToMapCoordinate(pixels.GetPixel(x, y).GetChannel(0)) - toleranceMap);
                }
            }
        }

        /// <summary>
        /// Raises the ground to a model surface (map units, NaN where none), so what lies under it is hidden too
        /// </summary>
        public void Raise(float[] surface, ZoneConfiguration zoneConfiguration)
        {
            var raise = (float)zoneConfiguration.ZoneCoordinateToMapCoordinate(TOLERANCE);
            for (var i = 0; i < this.heights.Length; i++)
            {
                if (surface[i] - raise > this.heights[i])
                {
                    this.heights[i] = surface[i] - raise;
                }
            }
        }

        /// <summary>
        /// Ground height in map units at a map pixel, without the tolerance
        /// </summary>
        public float GroundAt(int x, int y)
        {
            x = Math.Clamp(x, 0, this.size - 1);
            y = Math.Clamp(y, 0, this.size - 1);
            return this.heights[y * this.size + x] + this.tolerance;
        }

        public bool IsBelow(double mapX, double mapY, double z)
        {
            var x = (int)mapX;
            var y = (int)mapY;
            if (x < 0 || y < 0 || x >= this.size || y >= this.size)
            {
                return false;
            }
            return z < this.heights[y * this.size + x];
        }
    }
}
