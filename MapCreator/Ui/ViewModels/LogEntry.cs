using System;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using MapCreator.Classes;

namespace MapCreator.Ui.ViewModels
{
    public enum LogLevelFilter
    {
        All,
        Notices,
        Warnings,
        Errors
    }

    /// <summary>
    /// One log line; created on the render threads, so it holds no UI objects of its own
    /// </summary>
    public sealed class LogEntry
    {
        internal LogEntry(DateTime time, LogLevel level, string text, string zoneId)
        {
            this.Time = time;
            this.Level = level;
            this.Text = text;
            this.ZoneId = zoneId;
        }

        public DateTime Time { get; }

        public LogLevel Level { get; }

        public string Text { get; }

        // From the "[id] " prefix ZoneReporter adds (also for the following lines of a multi line text), null for other lines
        public string ZoneId { get; }

        public string TimeText => this.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        public IBrush Foreground => LevelBrush(this.Level);

        internal static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.Parse("#F06262"));
        internal static readonly IBrush WarningBrush = new ImmutableSolidColorBrush(Color.Parse("#E8C547"));
        internal static readonly IBrush SuccessBrush = new ImmutableSolidColorBrush(Color.Parse("#5CC46E"));
        internal static readonly IBrush NoticeBrush = new ImmutableSolidColorBrush(Color.Parse("#FFFFFF"));
        internal static readonly IBrush NormalBrush = new ImmutableSolidColorBrush(Color.Parse("#9A9A9A"));

        internal static IBrush LevelBrush(LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => ErrorBrush,
                LogLevel.Warning => WarningBrush,
                LogLevel.Success => SuccessBrush,
                LogLevel.Notice => NoticeBrush,
                _ => NormalBrush
            };
        }

        internal static string ParseZoneId(string text)
        {
            if (text == null || !text.StartsWith('['))
            {
                return null;
            }

            var end = text.IndexOf("] ", StringComparison.Ordinal);
            return end > 1 ? text.Substring(1, end - 1) : null;
        }

        /// <summary>
        /// Notices shows everything but plain lines, zoneId null shows all zones
        /// </summary>
        internal bool Matches(LogLevelFilter filter, string zoneId)
        {
            if (zoneId != null && this.ZoneId != zoneId)
            {
                return false;
            }

            return filter switch
            {
                LogLevelFilter.Notices => this.Level != LogLevel.Normal,
                LogLevelFilter.Warnings => this.Level == LogLevel.Warning || this.Level == LogLevel.Error,
                LogLevelFilter.Errors => this.Level == LogLevel.Error,
                _ => true
            };
        }
    }
}
