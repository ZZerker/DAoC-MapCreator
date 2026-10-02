using System;
using System.IO;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class RenderFoldersTests : IDisposable
    {
        private static readonly ZoneSelection Zone163 = new("163", "Forest/Glade", "New Frontiers", "All Realms", "Outdoor");

        private readonly string directory = Path.Combine(Path.GetTempPath(), "RenderFoldersTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(this.directory))
            {
                Directory.Delete(this.directory, true);
            }
        }

        private string Touch(string relativePath, DateTime time)
        {
            var path = Path.Combine(this.directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "");
            File.SetLastWriteTime(path, time);
            return path;
        }

        private RenderSettings Settings => new() { TargetPath = this.directory, DirectoryPattern = "maps", FilePattern = "zone{id}_{size}", MapSize = 2048, FileType = "PNG" };

        [Fact]
        public void ListsFoldersWithMapsNewestFirstAndCountsZones()
        {
            this.Touch("old\\zone163_2048.png", new DateTime(2026, 9, 29, 10, 0, 0));
            this.Touch("new\\z163.png", new DateTime(2026, 10, 2, 16, 45, 0));
            this.Touch("new\\z164.png", new DateTime(2026, 10, 2, 16, 40, 0));
            this.Touch("new\\z163.labels.json", new DateTime(2026, 10, 2, 17, 0, 0));
            this.Touch("logs\\render.log", new DateTime(2026, 10, 2, 18, 0, 0));

            var folders = RenderFolders.List(this.directory);

            Assert.Equal(2, folders.Count);
            Assert.Equal("new", folders[0].Name);
            Assert.Equal(2, folders[0].ZoneCount);
            Assert.Equal(new DateTime(2026, 10, 2, 16, 45, 0), folders[0].LastWrite);
            Assert.Equal("old", folders[1].Name);
        }

        [Fact]
        public void FindsTheBatchNameWhenThePatternNameIsMissing()
        {
            var batchMap = this.Touch("all\\z163.png", DateTime.Now);

            var file = RenderFolders.FindMap(Path.Combine(this.directory, "all"), this.Settings, Zone163);

            Assert.Equal(batchMap, file.FullName);
        }

        [Fact]
        public void PrefersThePatternNameAndReturnsItWhenNothingExists()
        {
            var folder = Path.Combine(this.directory, "maps");
            var patternMap = this.Touch("maps\\zone163_2048.png", DateTime.Now);
            this.Touch("maps\\z163.png", DateTime.Now);

            Assert.Equal(patternMap, RenderFolders.FindMap(folder, this.Settings, Zone163).FullName);

            var missing = RenderFolders.FindMap(Path.Combine(this.directory, "empty"), this.Settings, Zone163);
            Assert.False(missing.Exists);
            Assert.Equal("zone163_2048.png", missing.Name);
        }

        [Fact]
        public void TargetFolderIsNullForZoneDependentPatterns()
        {
            Assert.Equal(Path.Combine(this.directory, "maps"), RenderFolders.TargetFolder(this.Settings));
            Assert.Null(RenderFolders.TargetFolder(this.Settings with { DirectoryPattern = "{realm}" }));
        }
    }
}
