using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders a list of zones, one after another or several at a time. Shared by the window and the batch mode.
    /// </summary>
    internal sealed class ZoneBatch(RenderSettings settings, IRenderReporter reporter)
    {
        private int failed;

        /// <summary>
        /// Called when a zone starts, with its number in the queue
        /// </summary>
        public Action<ZoneSelection, int> ZoneStarted { get; init; }

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

        public void Run(IReadOnlyList<ZoneSelection> zones)
        {
            var parallel = Math.Clamp(settings.Parallel, 1, Math.Max(1, zones.Count));
            var started = 0;
            if (parallel == 1)
            {
                foreach (var zone in zones)
                {
                    this.Render(zone, ++started, this.ZoneIdsInLog ? new ZoneReporter(reporter, zone.Id) : reporter);
                }
                return;
            }

            var finished = 0;
            reporter.ProgressStart(string.Format("Rendering {0} zones, {1} at a time ...", zones.Count, parallel));
            Parallel.ForEach(zones, new ParallelOptions { MaxDegreeOfParallelism = parallel }, zone =>
            {
                this.Render(zone, Interlocked.Increment(ref started), new ZoneReporter(reporter, zone.Id));
                reporter.ProgressUpdate(100 * Interlocked.Increment(ref finished) / zones.Count);
            });
            reporter.ProgressReset();
        }

        private void Render(ZoneSelection zone, int number, IRenderReporter zoneReporter)
        {
            this.ZoneStarted?.Invoke(zone, number);
            using (AppLog.ForZone(zoneReporter))
            {
                zoneReporter.Log(string.Format("Rendering {0} ({1})...", zone.Name, zone.Id), LogLevel.Notice);
                try
                {
                    var mapFile = new ZoneRenderer(settings, zoneReporter).Render(zone);
                    if (mapFile != null && !mapFile.Exists)
                    {
                        zoneReporter.Log("No map was written.", LogLevel.Error);
                        Interlocked.Increment(ref this.failed);
                        return;
                    }

                    if (mapFile != null)
                    {
                        this.MapWritten?.Invoke(mapFile);
                        zoneReporter.ProgressReset();
                    }
                    zoneReporter.Log("Finished without errors!", LogLevel.Success);
                }
                catch (NotSupportedException ex)
                {
                    zoneReporter.Log("Skipped: " + ex.Message, LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    zoneReporter.Log("Unhandled Exception thrown!", LogLevel.Error);
                    zoneReporter.Log(ex.Message, LogLevel.Error);
                    zoneReporter.Log(ex.StackTrace, LogLevel.Error);
                    Interlocked.Increment(ref this.failed);
                }
            }
        }
    }
}
