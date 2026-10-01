using MapCreator.Classes;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class RegionMapsTests
    {
        private static MapLabels.ZoneArea Zone(double left, double top)
        {
            return new MapLabels.ZoneArea("000", "Zone", 163, MapLabels.OUTDOOR, left, top, 8, 8);
        }

        [Fact]
        public void FrameIsTheCenteredSquareAroundTheZones()
        {
            // New Frontiers in zones.dat: 44 wide (44 to 88), 48 high (36 to 84)
            var (left, top, side) = RegionMaps.GetFrame(new[] { Zone(44, 52), Zone(80, 44), Zone(60, 36), Zone(68, 76) });

            Assert.Equal(48, side);
            Assert.Equal(42, left);
            Assert.Equal(36, top);
        }

        [Fact]
        public void SingleZoneFillsTheFrame()
        {
            var (left, top, side) = RegionMaps.GetFrame(new[] { Zone(64, 64) });

            Assert.Equal((64d, 64d, 8d), (left, top, side));
        }
    }
}
