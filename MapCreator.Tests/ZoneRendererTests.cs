using MapCreator.Classes;
using MapCreator.Classes.Rendering;
using Xunit;

namespace MapCreator.Tests
{
    public class ZoneRendererTests
    {
        private static readonly ZoneSelection Zone163 = new("163", "Forest/Glade", "New Frontiers", "All Realms", "Outdoor");

        [Fact]
        public void DefaultPatternsGiveZoneIdAndSize()
        {
            var settings = new RenderSettings { TargetPath = "C:\\out", DirectoryPattern = "maps", FilePattern = "zone{id}_{size}", MapSize = 2048, FileType = "PNG" };

            var file = ZoneRenderer.GetTargetFile(settings, Zone163);

            Assert.Equal("C:\\out\\maps\\zone163_2048.png", file.FullName);
        }

        [Fact]
        public void EmptyPatternsFallBackToTheDefaults()
        {
            var settings = new RenderSettings { TargetPath = "C:\\out", MapSize = 1024, FileType = "JPEG" };

            var file = ZoneRenderer.GetTargetFile(settings, Zone163);

            Assert.Equal("C:\\out\\maps\\zone163_1024.jpg", file.FullName);
        }

        [Fact]
        public void PlaceholdersAreReplacedAndMadeValid()
        {
            var settings = new RenderSettings { TargetPath = "C:\\out", DirectoryPattern = "{realm}", FilePattern = "z{id} {name}", MapSize = 2048, FileType = "PNG" };

            var file = ZoneRenderer.GetTargetFile(settings, Zone163);

            Assert.Equal("C:\\out\\All Realms\\z163 Forest_Glade.png", file.FullName);
        }
    }
}
