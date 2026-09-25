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
    /// Log for code that is shared by all zones (caches, configuration files)
    /// </summary>
    public static class AppLog
    {
        private static IRenderReporter reporter = NullRenderReporter.Instance;

        public static IRenderReporter Reporter
        {
            get => reporter;
            set => reporter = value ?? NullRenderReporter.Instance;
        }

        public static void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            reporter.Log(text, logLevel);
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
