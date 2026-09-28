namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Reporter for one of several zones rendered at the same time: prefixes log lines with the zone id.
    /// The zone's own progress goes to an IZoneProgress receiver, other receivers only get the overall progress.
    /// </summary>
    internal sealed class ZoneReporter(IRenderReporter inner, string zoneId) : IRenderReporter
    {
        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            inner.Log(string.Format("[{0}] {1}", zoneId, text), logLevel);
        }

        public void ProgressStart(string label)
        {
            (inner as IZoneProgress)?.ZoneProgress(zoneId, label, 0);
        }

        public void ProgressStartMarquee(string label)
        {
            (inner as IZoneProgress)?.ZoneProgress(zoneId, label, null);
        }

        public void ProgressUpdate(int percent)
        {
            (inner as IZoneProgress)?.ZoneProgress(zoneId, null, percent);
        }

        public void ProgressReset()
        {
            (inner as IZoneProgress)?.ZoneProgress(zoneId, null, null);
        }
    }

    /// <summary>
    /// Receives the progress of each zone of a batch
    /// </summary>
    internal interface IZoneProgress
    {
        /// <summary>
        /// Step label (null keeps the current one) and percent (null for no known progress)
        /// </summary>
        void ZoneProgress(string zoneId, string label, int? percent);
    }
}
