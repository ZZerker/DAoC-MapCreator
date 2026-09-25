namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Reporter for one of several zones rendered at the same time: prefixes log lines with the zone id
    /// and drops the zone's own progress, the caller shows the overall progress instead
    /// </summary>
    internal sealed class ZoneReporter(IRenderReporter inner, string zoneId) : IRenderReporter
    {
        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            inner.Log(string.Format("[{0}] {1}", zoneId, text), logLevel);
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
