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

        public TerrainHeights(ZoneConfiguration zoneConfiguration)
        {
            var heightmap = zoneConfiguration.Heightmap.HeightmapScaled;
            this.size = (int)heightmap.Width;
            this.heights = new float[this.size * this.size];
            var tolerance = zoneConfiguration.ZoneCoordinateToMapCoordinate(TOLERANCE);
            using var pixels = heightmap.GetPixelsUnsafe();
            for (var y = 0; y < this.size; y++)
            {
                for (var x = 0; x < this.size; x++)
                {
                    this.heights[y * this.size + x] = (float)(zoneConfiguration.ZoneCoordinateToMapCoordinate(pixels.GetPixel(x, y).GetChannel(0)) - tolerance);
                }
            }
        }

        /// <summary>
        /// Raises the ground to a model surface (map units, NaN where none), so what lies under it is hidden too
        /// </summary>
        public void Raise(float[] surface, ZoneConfiguration zoneConfiguration)
        {
            var tolerance = (float)zoneConfiguration.ZoneCoordinateToMapCoordinate(TOLERANCE);
            for (var i = 0; i < this.heights.Length; i++)
            {
                if (surface[i] - tolerance > this.heights[i])
                {
                    this.heights[i] = surface[i] - tolerance;
                }
            }
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
