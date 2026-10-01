using System.Numerics;
using MapCreator.Classes.MapCreation.Fixtures;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class ObliqueProjectionTests
    {
        private const double F = DrawableFixture.OBLIQUE_FACTOR;

        [Fact]
        public void FacesViewerKeepsSouthAndTopFaces()
        {
            Assert.True(DrawableFixture.FacesViewer(new Vector3(0, -1, 0)));
            Assert.False(DrawableFixture.FacesViewer(new Vector3(0, 1, 0)));
            Assert.True(DrawableFixture.FacesViewer(new Vector3(0, 0, 1)));
            Assert.False(DrawableFixture.FacesViewer(new Vector3(0, 0, -1)));
        }

        [Fact]
        public void FootprintStaysWhereItIs()
        {
            var projected = DrawableFixture.ProjectOblique(new Vector3(3, -7, 0));

            Assert.Equal(3, projected.X);
            Assert.Equal(-7, projected.Y);
        }

        [Fact]
        public void SouthWallReachesUpTheMapAndMeetsTheTopFace()
        {
            var wallBottom = DrawableFixture.ProjectOblique(new Vector3(0, -5, 0));
            var wallTop = DrawableFixture.ProjectOblique(new Vector3(0, -5, 20));
            var topSouthEdge = DrawableFixture.ProjectOblique(new Vector3(0, -5, 20));

            Assert.Equal(topSouthEdge, wallTop);
            Assert.Equal(F * 20, wallTop.Y - wallBottom.Y, 4);
        }

        [Fact]
        public void HigherPointOnTheSameViewRayIsNearer()
        {
            var back = new Vector3(0, 5, 10);
            var steps = 4f;
            var front = back + steps * new Vector3(0, (float)-F, 1);

            Assert.Equal(DrawableFixture.ProjectOblique(back).Y, DrawableFixture.ProjectOblique(front).Y, 4);
            Assert.True(DrawableFixture.ObliqueDepth(front) > DrawableFixture.ObliqueDepth(back));
        }

        [Fact]
        public void TopFaceIsBrightestAndWallsDiffer()
        {
            var top = DrawableFixture.ObliqueLight(new Vector3(0, 0, 1));
            var south = DrawableFixture.ObliqueLight(new Vector3(0, -1, 0));
            var west = DrawableFixture.ObliqueLight(new Vector3(-1, 0, 0));
            var east = DrawableFixture.ObliqueLight(new Vector3(1, 0, 0));
            var down = DrawableFixture.ObliqueLight(new Vector3(0, 0, -1));

            Assert.Equal(1.0, top, 6);
            Assert.True(south < top && west < south && east < west);
            Assert.True(down <= east);
        }

        [Fact]
        public void TopFaceCoversTheHiddenNorthWall()
        {
            // The ray from the north wall point up to the top face at height 20
            var wall = new Vector3(0, 5, 5);
            var top = wall + 15f * new Vector3(0, (float)-F, 1);

            Assert.Equal(20, top.Z, 4);
            Assert.True(DrawableFixture.ObliqueDepth(top) > DrawableFixture.ObliqueDepth(wall));
        }
    }
}
