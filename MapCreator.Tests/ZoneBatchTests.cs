using System.Linq;
using System.Threading;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;
using Xunit;

namespace MapCreator.Tests
{
    public class ZoneBatchTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void CancelledTokenStartsNoZone(int parallel)
        {
            var zones = Enumerable.Range(1, 5).Select(i => new ZoneSelection(i.ToString("000"), "Zone " + i, "Classic", "Albion", "Outdoor")).ToList();
            var started = 0;
            var batch = new ZoneBatch(new RenderSettings { Parallel = parallel }, NullRenderReporter.Instance)
                        {
                            ZoneStarted = (zone, number) => Interlocked.Increment(ref started)
                        };
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            batch.Run(zones, cancellation.Token);

            Assert.Equal(0, started);
            Assert.Equal(0, batch.Failed);
        }

        [Fact]
        public void EmptyLabelDirectoryDrawsNoLabeledCopies()
        {
            Assert.Null(RenderSettings.FromSettings(new AppSettings()).LabelDirectory);

            var labeled = RenderSettings.FromSettings(new AppSettings { LabelDirectory = "labeled", LabelSize = 1024 });
            Assert.Equal("labeled", labeled.LabelDirectory);
            Assert.Equal(1024, labeled.LabelSize);
        }
    }
}
