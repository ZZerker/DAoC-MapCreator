using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Live console view of a batch: totals, one row per zone being rendered with its step and progress, and the latest log lines.
    /// Every line also goes to the log file.
    /// </summary>
    internal sealed class BatchDashboard : IRenderReporter, IZoneProgress, IDisposable
    {
        private const int REFRESH_MILLISECONDS = 250;

        private const int BAR_WIDTH = 20;

        private const int MAX_LOG_LINES = 500;

        private readonly FileLog file;
        private readonly IAnsiConsole console;
        private readonly object stateLock = new();
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private readonly Dictionary<string, ActiveZone> activeZones = new();
        private readonly Queue<(LogLevel Level, string Text)> logLines = new();
        private readonly Thread renderThread;

        private int zoneCount;
        private int threads;
        private int done;
        private int withErrors;
        private int skipped;
        private int failed;
        private string title = "MapCreator";
        private volatile bool stopping;

        private sealed class ActiveZone
        {
            public string Name;
            public string Step = "Starting";
            public int? Percent;
            public readonly Stopwatch Timer = Stopwatch.StartNew();
        }

        public BatchDashboard(FileLog file)
        {
            this.file = file;
            OpenConsoleWindow();
            this.console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out), Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.TrueColor });
            this.renderThread = new Thread(this.RenderLoop) { IsBackground = true, Name = "Dashboard" };
            this.renderThread.Start();
        }

        public void Start(string batchTitle, int zones, int threadCount)
        {
            lock (this.stateLock)
            {
                this.title = batchTitle;
                this.zoneCount = zones;
                this.threads = threadCount;
                this.timer.Restart();
            }
        }

        public void ZoneStarted(ZoneSelection zone)
        {
            lock (this.stateLock)
            {
                this.activeZones[zone.Id] = new ActiveZone { Name = zone.Name };
            }
        }

        public void ZoneFinished(ZoneSelection zone, ZoneOutcome outcome)
        {
            lock (this.stateLock)
            {
                this.activeZones.Remove(zone.Id);
                switch (outcome)
                {
                    case ZoneOutcome.Done:
                        this.done++;
                        break;
                    case ZoneOutcome.WithErrors:
                        this.withErrors++;
                        break;
                    case ZoneOutcome.Skipped:
                        this.skipped++;
                        break;
                    case ZoneOutcome.Failed:
                        this.failed++;
                        break;
                }
            }
        }

        public void Log(string text, LogLevel logLevel = LogLevel.Normal)
        {
            this.file.Log(text, logLevel);
            lock (this.stateLock)
            {
                foreach (var line in (text ?? "").Split('\n'))
                {
                    this.logLines.Enqueue((logLevel, line.TrimEnd('\r')));
                }
                while (this.logLines.Count > MAX_LOG_LINES)
                {
                    this.logLines.Dequeue();
                }
            }
        }

        public void ZoneProgress(string zoneId, string label, int? percent)
        {
            lock (this.stateLock)
            {
                if (!this.activeZones.TryGetValue(zoneId, out var zone))
                {
                    return;
                }
                if (label != null)
                {
                    zone.Step = label.TrimEnd('.', ' ');
                }
                zone.Percent = percent;
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

        /// <summary>
        /// Draws the final state and keeps the window open until a key is pressed or the wait is over
        /// </summary>
        public void Finish(TimeSpan wait)
        {
            this.Dispose();
            this.console.MarkupLine(string.Format("[grey]Press a key to close, closes by itself in {0:0} s.[/]", wait.TotalSeconds));
            var end = DateTime.Now + wait;
            while (DateTime.Now < end && !Console.KeyAvailable)
            {
                Thread.Sleep(200);
            }
        }

        public void Dispose()
        {
            this.stopping = true;
            if (this.renderThread.IsAlive)
            {
                this.renderThread.Join();
            }
        }

        private void RenderLoop()
        {
            Console.CursorVisible = false;
            this.console.Clear();
            this.console.Live(this.BuildView()).Overflow(VerticalOverflow.Crop).Start(context =>
            {
                do
                {
                    Thread.Sleep(REFRESH_MILLISECONDS);
                    context.UpdateTarget(this.BuildView());
                }
                while (!this.stopping);
            });
        }

        private IRenderable BuildView()
        {
            // One line spare, else the console scrolls
            var height = Math.Max(20, Console.WindowHeight - 1);
            lock (this.stateLock)
            {
                var width = Math.Max(40, Console.WindowWidth - 1);
                var finished = this.done + this.withErrors + this.skipped + this.failed;
                var elapsed = this.timer.Elapsed;
                var remaining = finished == 0 || finished == this.zoneCount ? "" : string.Format("   left ~{0:hh\\:mm\\:ss}", TimeSpan.FromTicks(elapsed.Ticks / finished * (this.zoneCount - finished)));
                var memory = Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024);

                var header = new Rows(
                    new Markup(string.Format("[bold]{0}[/]", Markup.Escape(Shorten(this.title, width - 4)))),
                    new Markup(string.Format("zones [bold]{0}/{1}[/]   done [green]{2}[/]   with errors [yellow]{3}[/]   skipped [grey]{4}[/]   failed [red]{5}[/]",
                                             finished, this.zoneCount, this.done, this.withErrors, this.skipped, this.failed)),
                    new Markup(string.Format("threads [bold]{0}[/] ({1} busy)   elapsed [bold]{2:hh\\:mm\\:ss}[/]{3}   memory [bold]{4:N0} MB[/]", this.threads, this.activeZones.Count, elapsed, remaining, memory)),
                    new Markup(Bar(this.zoneCount == 0 ? 0 : 100 * finished / this.zoneCount, width - 12)));

                var zones = new Table().Border(TableBorder.Simple).Expand();
                zones.AddColumn("Zone");
                zones.AddColumn("Name");
                zones.AddColumn("Step");
                zones.AddColumn("Progress");
                zones.AddColumn(new TableColumn("Time").RightAligned());
                foreach (var (id, zone) in this.activeZones.OrderBy(z => z.Key))
                {
                    zones.AddRow(new Markup(Markup.Escape(id)), new Markup(Markup.Escape(Shorten(zone.Name, 28))), new Markup(Markup.Escape(Shorten(zone.Step, 40))),
                                 new Markup(zone.Percent == null ? "[grey]working[/]" : Bar(zone.Percent.Value, BAR_WIDTH)), new Markup(string.Format("{0:mm\\:ss}", zone.Timer.Elapsed)));
                }

                // Header 4 lines plus the panel borders, the zone table has a header, a rule and its rows
                var logHeight = Math.Max(3, height - 4 - 2 - (this.activeZones.Count + 3) - 2);
                var log = new StringBuilder();
                foreach (var (level, text) in this.logLines.Skip(Math.Max(0, this.logLines.Count - logHeight)))
                {
                    log.AppendLine(string.Format("[{0}]{1}[/]", Color(level), Markup.Escape(Shorten(text, width - 4))));
                }
                for (var i = this.logLines.Count; i < logHeight; i++)
                {
                    log.AppendLine();
                }

                return new Rows(
                    new Panel(header).Header("Batch").Expand(),
                    zones,
                    new Panel(new Markup(log.ToString().TrimEnd('\r', '\n'))).Header("Log").Expand());
            }
        }

        private static string Bar(int percent, int width)
        {
            percent = Math.Clamp(percent, 0, 100);
            var filled = width * percent / 100;
            return string.Format("[green]{0}[/][grey]{1}[/] {2,3}%", new string('█', filled), new string('░', width - filled), percent);
        }

        private static string Color(LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => "red",
                LogLevel.Warning => "yellow",
                LogLevel.Success => "green",
                LogLevel.Notice => "white",
                _ => "grey"
            };
        }

        private static string Shorten(string text, int length)
        {
            text ??= "";
            return text.Length <= length ? text : text.Substring(0, Math.Max(0, length - 1)) + "…";
        }

        // The exe is a window application, so it has no console of its own
        private static void OpenConsoleWindow()
        {
            AllocConsole();

            // A new console does not interpret escape sequences until asked to
            var output = GetStdHandle(STD_OUTPUT_HANDLE);
            if (GetConsoleMode(output, out var mode))
            {
                SetConsoleMode(output, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN);
            }

            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "MapCreator batch";
        }

        private const int STD_OUTPUT_HANDLE = -11;

        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
