using System;
using System.Collections.Generic;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui
{
    /// <summary>
    /// Collects log lines and zone progress from the render threads; the window drains them on a timer.
    /// During a render every line also goes to the render log file.
    /// </summary>
    internal sealed class UiRenderReporter : IRenderReporter, IZoneProgress
    {
        private readonly object queueLock = new();
        private readonly object fileLock = new();
        private Pending pending = new();
        private FileLog file;

        internal sealed class ZoneStep
        {
            public string Label { get; set; }
            public int? Percent { get; set; }
        }

        internal sealed class Pending
        {
            public List<LogEntry> Lines { get; } = new();
            public List<ZoneSelection> Started { get; } = new();
            public Dictionary<string, ZoneStep> Steps { get; } = new();
            public List<(ZoneSelection Zone, ZoneOutcome Outcome)> Finished { get; } = new();
        }

        public void AttachFile(FileLog log)
        {
            lock (this.fileLock)
            {
                this.file = log;
            }
        }

        public void DetachFile()
        {
            lock (this.fileLock)
            {
                this.file = null;
            }
        }

        /// <summary>
        /// Everything collected since the last call
        /// </summary>
        public Pending Drain()
        {
            lock (this.queueLock)
            {
                var drained = this.pending;
                this.pending = new Pending();
                return drained;
            }
        }

        public void ZoneStarted(ZoneSelection zone)
        {
            lock (this.queueLock)
            {
                this.pending.Started.Add(zone);
            }
        }

        public void ZoneFinished(ZoneSelection zone, ZoneOutcome outcome)
        {
            lock (this.queueLock)
            {
                this.pending.Finished.Add((zone, outcome));
            }
        }

        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            lock (this.fileLock)
            {
                this.file?.Log(text, logLevel);
            }

            var time = DateTime.Now;
            var zoneId = LogEntry.ParseZoneId(text);
            lock (this.queueLock)
            {
                foreach (var line in (text ?? "").Split('\n'))
                {
                    this.pending.Lines.Add(new LogEntry(time, logLevel, line.TrimEnd('\r'), zoneId));
                }
            }
        }

        public void ZoneProgress(string zoneId, string label, int? percent)
        {
            lock (this.queueLock)
            {
                if (!this.pending.Steps.TryGetValue(zoneId, out var step))
                {
                    step = new ZoneStep();
                    this.pending.Steps[zoneId] = step;
                }

                if (label != null)
                {
                    step.Label = label.TrimEnd('.', ' ');
                }

                step.Percent = percent;
            }
        }

        public void ProgressStart(string label)
        {
        }

        public void ProgressStartMarquee(string label)
        {
        }

        public void ProgressUpdate(int percent)
        {
        }

        public void ProgressReset()
        {
        }
    }
}
