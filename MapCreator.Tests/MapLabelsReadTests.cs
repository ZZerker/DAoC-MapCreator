using System;
using System.IO;
using MapCreator.Classes;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class MapLabelsReadTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "MapLabelsReadTests_" + Guid.NewGuid().ToString("N"));

        public MapLabelsReadTests()
        {
            Directory.CreateDirectory(this.directory);
        }

        public void Dispose()
        {
            Directory.Delete(this.directory, true);
        }

        [Fact]
        public void ReadsLabelsWithAndWithoutEdge()
        {
            File.WriteAllText(Path.Combine(this.directory, "z011.labels.json"), """
                {
                  "zone": "011",
                  "labels": [
                    { "kind": "keep", "text": "Dun Crauchon", "x": 0.25, "y": 0.5, "priority": 1, "realm": 2, "edge": null },
                    { "kind": "neighbor", "text": "Dartmoor", "x": 1, "y": 0.4, "priority": 1, "realm": 0, "edge": "east" }
                  ]
                }
                """);

            var labels = MapLabels.Read(new FileInfo(Path.Combine(this.directory, "z011.png")));

            Assert.NotNull(labels);
            Assert.Equal(2, labels.Count);
            Assert.Equal(new MapLabels.Label("keep", "Dun Crauchon", 0.25, 0.5, 1, 2, null), labels[0]);
            Assert.Equal(new MapLabels.Label("neighbor", "Dartmoor", 1, 0.4, 1, 0, "east"), labels[1]);
        }

        [Fact]
        public void MissingFileGivesNull()
        {
            Assert.Null(MapLabels.Read(new FileInfo(Path.Combine(this.directory, "z001.png"))));
        }

        [Fact]
        public void MalformedFileGivesNull()
        {
            File.WriteAllText(Path.Combine(this.directory, "z002.labels.json"), "{ \"zone\": \"002\", \"labels\": [ {");

            Assert.Null(MapLabels.Read(new FileInfo(Path.Combine(this.directory, "z002.png"))));
        }

        [Fact]
        public void EmptyLabelsGiveEmptyList()
        {
            File.WriteAllText(Path.Combine(this.directory, "z001.labels.json"), "{\"zone\":\"001\",\"labels\":[]}");

            var labels = MapLabels.Read(new FileInfo(Path.Combine(this.directory, "z001.png")));

            Assert.NotNull(labels);
            Assert.Empty(labels);
        }
    }
}
