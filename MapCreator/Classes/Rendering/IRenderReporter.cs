namespace MapCreator.Classes
{
    public enum LogLevel
    {
        Normal = 1,
        Success = 2,
        Notice = 3,
        Warning = 4,
        Error = 5
    }

    /// <summary>
    /// Receives log lines and progress of a render. Implementations must be thread safe.
    /// </summary>
    public interface IRenderReporter
    {
        void Log(string text, LogLevel logLevel = LogLevel.Normal);

        void ProgressStart(string label);

        void ProgressStartMarquee(string label);

        void ProgressUpdate(int percent);

        void ProgressReset();
    }

    /// <summary>
    /// Log for code that is shared by all zones (caches, configuration files). While a zone renders, its lines go to
    /// that zone's reporter, so they carry the zone id.
    /// </summary>
    public static class AppLog
    {
        private static IRenderReporter reporter = NullRenderReporter.Instance;

        private static readonly System.Threading.AsyncLocal<IRenderReporter> CurrentZone = new();

        public static IRenderReporter Reporter
        {
            get => reporter;
            set => reporter = value ?? NullRenderReporter.Instance;
        }

        public static void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            (CurrentZone.Value ?? reporter).Log(text, logLevel);
        }

        /// <summary>
        /// Sends the lines of the current thread and its tasks to the zone's reporter until disposed
        /// </summary>
        public static System.IDisposable ForZone(IRenderReporter zoneReporter)
        {
            var previous = CurrentZone.Value;
            CurrentZone.Value = zoneReporter;
            return new Scope(() => CurrentZone.Value = previous);
        }

        private sealed class Scope(System.Action end) : System.IDisposable
        {
            public void Dispose()
            {
                end();
            }
        }
    }

    public sealed class NullRenderReporter : IRenderReporter
    {
        public static readonly NullRenderReporter Instance = new();

        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
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
