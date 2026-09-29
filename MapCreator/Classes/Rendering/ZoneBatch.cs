using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders a list of zones, one after another or several at a time. Shared by the window and the batch mode.
    /// </summary>
    internal enum ZoneOutcome
    {
        Done,
        WithErrors,
        Skipped,
        Failed
    }

    internal sealed class ZoneBatch(RenderSettings settings, IRenderReporter reporter)
    {
        private int failed;
        private int withErrors;

        /// <summary>
        /// Called when a zone starts, with its number in the queue
        /// </summary>
        public Action<ZoneSelection, int> ZoneStarted { get; init; }

        /// <summary>
        /// Called when a zone ends
        /// </summary>
        public Action<ZoneSelection, ZoneOutcome> ZoneFinished { get; init; }

        /// <summary>
        /// Called with every written map
        /// </summary>
        public Action<FileInfo> MapWritten { get; init; }

        /// <summary>
        /// Prefixes the lines with the zone id also when zones render one after another
        /// </summary>
        public bool ZoneIdsInLog { get; init; }

        /// <summary>
        /// Zones that ended with an error
        /// </summary>
        public int Failed => this.failed;

        /// <summary>
        /// Zones whose map was written but that logged errors on the way
        /// </summary>
        public int WithErrors => this.withErrors;

        /// <summary>
        /// Renders the zones; after a cancel the running zones finish and no new zone starts
        /// </summary>
        public void Run(IReadOnlyList<ZoneSelection> zones, CancellationToken cancellationToken = default)
        {
            var parallel = Math.Clamp(settings.Parallel, 1, Math.Max(1, zones.Count));
            var started = 0;
            if (parallel == 1)
            {
                foreach (var zone in zones)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    this.Render(zone, ++started, this.ZoneIdsInLog ? new ZoneReporter(reporter, zone.Id) : reporter);
                }
                return;
            }

            var finished = 0;
            reporter.ProgressStart(string.Format("Rendering {0} zones, {1} at a time ...", zones.Count, parallel));
            try
            {
                Parallel.ForEach(zones, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, zone =>
                {
                    this.Render(zone, Interlocked.Increment(ref started), new ZoneReporter(reporter, zone.Id));
                    reporter.ProgressUpdate(100 * Interlocked.Increment(ref finished) / zones.Count);
                });
            }
            catch (OperationCanceledException)
            {
                // Thrown after the running zones have finished
            }
            reporter.ProgressReset();
        }

        /// <summary>
        /// The closing lines of a batch: what the caches keep and the zone counts
        /// </summary>
        public void LogSummary(int zoneCount, TimeSpan elapsed)
        {
            reporter.Log(string.Format("Kept for reuse: {0} models, {1} textures ({2} MB), process memory {3} MB", MapCreation.Fixtures.FixtureCache.CachedModels, MapCreation.Fixtures.TextureCache.Count,
                                       MapCreation.Fixtures.TextureCache.Bytes / (1024 * 1024), Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024)), LogLevel.Notice);
            reporter.Log(string.Format("Done: {0} zones in {1:hh\\:mm\\:ss}, {2} failed, {3} with errors", zoneCount, elapsed, this.Failed, this.WithErrors), this.Failed + this.WithErrors == 0 ? LogLevel.Success : LogLevel.Error);
        }

        private void Render(ZoneSelection zone, int number, IRenderReporter zoneReporter)
        {
            this.ZoneStarted?.Invoke(zone, number);
            var outcome = this.RenderZone(zone, zoneReporter);
            if (outcome == ZoneOutcome.Failed)
            {
                Interlocked.Increment(ref this.failed);
            }
            else if (outcome == ZoneOutcome.WithErrors)
            {
                Interlocked.Increment(ref this.withErrors);
            }
            this.ZoneFinished?.Invoke(zone, outcome);
        }

        private ZoneOutcome RenderZone(ZoneSelection zone, IRenderReporter zoneReporter)
        {
            var counter = new ErrorCounter(zoneReporter);
            using (AppLog.ForZone(counter))
            {
                counter.Log(string.Format("Rendering {0} ({1})...", zone.Name, zone.Id), LogLevel.Notice);
                try
                {
                    var mapFile = new ZoneRenderer(settings, counter).Render(zone);
                    if (mapFile != null && !mapFile.Exists)
                    {
                        counter.Log("No map was written.", LogLevel.Error);
                        return ZoneOutcome.Failed;
                    }

                    if (mapFile != null)
                    {
                        this.MapWritten?.Invoke(mapFile);
                        counter.ProgressReset();
                    }

                    if (counter.Errors > 0)
                    {
                        counter.Log(string.Format("Finished with {0} errors.", counter.Errors), LogLevel.Warning);
                        return ZoneOutcome.WithErrors;
                    }

                    counter.Log("Finished without errors!", LogLevel.Success);
                    return mapFile == null ? ZoneOutcome.Skipped : ZoneOutcome.Done;
                }
                catch (NotSupportedException ex)
                {
                    counter.Log("Skipped: " + ex.Message, LogLevel.Warning);
                    return ZoneOutcome.Skipped;
                }
                catch (Exception ex)
                {
                    counter.Log("Unhandled Exception thrown!", LogLevel.Error);
                    counter.Log(ex.Message, LogLevel.Error);
                    counter.Log(ex.StackTrace, LogLevel.Error);
                    return ZoneOutcome.Failed;
                }
            }
        }

        private sealed class ErrorCounter(IRenderReporter inner) : IRenderReporter
        {
            private int errors;

            public int Errors => this.errors;

            public void Log(string text, LogLevel logLevel = LogLevel.Normal)
            {
                if (logLevel == LogLevel.Error)
                {
                    Interlocked.Increment(ref this.errors);
                }
                inner.Log(text, logLevel);
            }

            public void ProgressStart(string label)
            {
                inner.ProgressStart(label);
            }

            public void ProgressStartMarquee(string label)
            {
                inner.ProgressStartMarquee(label);
            }

            public void ProgressUpdate(int percent)
            {
                inner.ProgressUpdate(percent);
            }

            public void ProgressReset()
            {
                inner.ProgressReset();
            }
        }
    }
}
