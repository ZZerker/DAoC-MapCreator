using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    /// <summary>
    /// The render bar and the activity panel: starts and cancels a render of the ticked zones and shows its progress and log
    /// </summary>
    public sealed partial class RenderViewModel : ViewModelBase
    {
        private const int MAX_LOG_LINES = 5000;

        // The shown lines are trimmed in steps, not line by line
        private const int TRIM_SLACK = 500;

        private const int TICK_MILLISECONDS = 150;

        private const string ALL_ZONES = "All zones";

        private readonly UiRenderReporter reporter;
        private readonly ZoneBrowserViewModel zoneBrowser;
        private readonly OptionsViewModel options;
        private readonly List<LogEntry> lines = new();
        private readonly Dictionary<string, ActiveZoneViewModel> activeById = new();
        private readonly Stopwatch renderTimer = new();
        private readonly DispatcherTimer timer;
        private CancellationTokenSource cancellation;
        private Action closeAfterRender;
        private int totalZones;
        private int done;
        private int withErrors;
        private int skipped;
        private int failed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ButtonText))]
        [NotifyCanExecuteChangedFor(nameof(RenderOrCancelCommand))]
        private bool isRendering;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ButtonText))]
        private bool isCancelling;

        [ObservableProperty]
        private bool hasRun;

        [ObservableProperty]
        private bool isClosePending;

        // Center tab: 0 map, 1 activity
        [ObservableProperty]
        private int centerTab;

        [ObservableProperty]
        private string activityHeader = "Activity";

        [ObservableProperty]
        private double progress;

        [ObservableProperty]
        private string progressText = "";

        [ObservableProperty]
        private string countsText = "";

        [ObservableProperty]
        private string elapsedText = "";

        [ObservableProperty]
        private ObservableCollection<LogEntry> shownLines = new();

        [ObservableProperty]
        private int levelFilterIndex;

        [ObservableProperty]
        private string zoneFilter = ALL_ZONES;

        internal RenderViewModel(UiRenderReporter reporter, ZoneBrowserViewModel zoneBrowser, OptionsViewModel options)
        {
            this.reporter = reporter;
            this.zoneBrowser = zoneBrowser;
            this.options = options;
            this.zoneBrowser.PropertyChanged += this.OnZoneBrowserChanged;

            this.timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TICK_MILLISECONDS) };
            this.timer.Tick += this.OnTick;
            this.timer.Start();
        }

        /// <summary>
        /// Raised after new log lines were shown, so the view can scroll to the end
        /// </summary>
        public event Action LinesAdded;

        public string ButtonText
        {
            get
            {
                if (!this.IsRendering)
                {
                    return this.zoneBrowser.RenderLabel;
                }

                return this.IsCancelling ? "Cancelling" : "Cancel";
            }
        }

        public ObservableCollection<string> ZoneFilterChoices { get; } = new() { ALL_ZONES };

        public ObservableCollection<ActiveZoneViewModel> ActiveZones { get; } = new();

        public ObservableCollection<FinishedZoneViewModel> FinishedZones { get; } = new();

        private LogLevelFilter LevelFilter => (LogLevelFilter)Math.Max(0, this.LevelFilterIndex);

        private string ZoneFilterId => this.ZoneFilter == null || this.ZoneFilter == ALL_ZONES ? null : this.ZoneFilter;

        partial void OnLevelFilterIndexChanged(int value)
        {
            this.RebuildShownLines();
        }

        partial void OnZoneFilterChanged(string value)
        {
            this.RebuildShownLines();
        }

        private bool CanRenderOrCancel()
        {
            return this.IsRendering || this.zoneBrowser.TickedCount > 0;
        }

        [RelayCommand(CanExecute = nameof(CanRenderOrCancel))]
        private void RenderOrCancel()
        {
            if (!this.IsRendering)
            {
                this.StartRender(AppSettings.Current.TickedZones);
                return;
            }

            this.RequestCancel();
        }

        /// <summary>
        /// Renders just this zone, leaving the ticks alone
        /// </summary>
        internal void RenderZone(string zoneId)
        {
            if (this.IsRendering)
            {
                return;
            }

            this.StartRender(new[] { zoneId });
        }

        /// <summary>
        /// Cancels the render and calls close once the running zones have finished
        /// </summary>
        internal void CloseAfterRender(Action close)
        {
            this.closeAfterRender = close;
            this.IsClosePending = true;
            this.RequestCancel();
        }

        private void RequestCancel()
        {
            if (this.IsCancelling)
            {
                return;
            }

            this.IsCancelling = true;
            this.cancellation?.Cancel();
            AppLog.Log("Cancel requested: the running zones finish, no new zone starts.", LogLevel.Warning);
        }

        /// <summary>
        /// Renders the zones of a --ui command line with its settings, leaving the ticks and saved settings alone
        /// </summary>
        internal void RenderFromCommandLine(IReadOnlyList<string> zoneIds, RenderSettings commandLineSettings)
        {
            this.StartRender(zoneIds, commandLineSettings);
        }

        private void StartRender(IReadOnlyList<string> zoneIds, RenderSettings commandLineSettings = null)
        {
            if (!GameFolderLocator.CheckGamePath())
            {
                return;
            }

            // Everything that can fail comes before the window is locked
            RenderSettings renderSettings;
            var zones = new List<ZoneSelection>();
            try
            {
                renderSettings = commandLineSettings ?? RenderSettings.FromSettings(AppSettings.Current);
                foreach (var zoneId in zoneIds.Distinct().OrderBy(id => id, StringComparer.Ordinal))
                {
                    if (DataWrapper.IsKnownZone(zoneId))
                    {
                        zones.Add(DataWrapper.GetZoneSelectionByZoneId(zoneId));
                    }
                    else
                    {
                        AppLog.Log(string.Format("Skipped: zone {0} is not in the zone list.", zoneId), LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Log("Render not started: " + ex.Message, LogLevel.Error);
                return;
            }

            if (zones.Count == 0)
            {
                AppLog.Log("No zone to render.", LogLevel.Warning);
                return;
            }

            this.activeById.Clear();
            this.ActiveZones.Clear();
            this.FinishedZones.Clear();
            this.totalZones = zones.Count;
            this.done = 0;
            this.withErrors = 0;
            this.skipped = 0;
            this.failed = 0;
            this.ZoneFilterChoices.Clear();
            this.ZoneFilterChoices.Add(ALL_ZONES);
            foreach (var zone in zones)
            {
                this.ZoneFilterChoices.Add(zone.Id);
            }
            this.ZoneFilter = ALL_ZONES;

            this.IsRendering = true;
            this.IsCancelling = false;
            this.HasRun = true;
            this.zoneBrowser.IsRendering = true;
            this.options.IsRendering = true;
            this.renderTimer.Restart();
            this.UpdateCounts();

            // The zone list and the map show the folder this render writes into
            this.zoneBrowser.ShowRenderFolder(renderSettings);
            foreach (var zone in zones)
            {
                this.zoneBrowser.SetStatus(zone.Id, new ZoneStatus(ZoneMapState.Queued));
            }

            this.cancellation = new CancellationTokenSource();
            var token = this.cancellation.Token;
            Task.Run(() => this.RunRender(renderSettings, zones, token));
        }

        // Runs on a pool thread
        private void RunRender(RenderSettings renderSettings, List<ZoneSelection> zones, CancellationToken token)
        {
            var started = 0;
            var watch = Stopwatch.StartNew();
            try
            {
                // One log per render folder, so renders into different folders do not share a file
                var logFolder = RenderFolders.TargetFolder(renderSettings) ?? renderSettings.TargetPath;
                Directory.CreateDirectory(logFolder);
                using (var log = new FileLog(Path.Combine(logFolder, "render.log")))
                {
                    this.reporter.AttachFile(log);
                    try
                    {
                        var threads = Math.Clamp(renderSettings.Parallel, 1, Math.Max(1, zones.Count));
                        var target = Path.Combine(renderSettings.TargetPath, renderSettings.DirectoryPattern ?? "");
                        this.reporter.Log(string.Format("Rendering {0} zones at {1} px, {2} at a time, into {3}", zones.Count, renderSettings.MapSize, threads, target), LogLevel.Notice);

                        var batch = new ZoneBatch(renderSettings, this.reporter)
                                    {
                                        ZoneIdsInLog = true,
                                        ZoneStarted = (zone, number) =>
                                        {
                                            Interlocked.Increment(ref started);
                                            this.reporter.ZoneStarted(zone);
                                        },
                                        ZoneFinished = (zone, outcome) => this.reporter.ZoneFinished(zone, outcome)
                                    };
                        batch.Run(zones, token);
                        if (token.IsCancellationRequested && started < zones.Count)
                        {
                            this.reporter.Log(string.Format("Cancelled after {0} of {1} zones.", started, zones.Count), LogLevel.Warning);
                        }

                        batch.LogSummary(token.IsCancellationRequested ? started : zones.Count, watch.Elapsed);
                    }
                    finally
                    {
                        this.reporter.DetachFile();
                    }
                }
            }
            catch (Exception ex)
            {
                // Zones catch their own errors; this is the log file or the batch itself, and the window must leave the render state
                this.reporter.Log("Render stopped: " + ex.Message, LogLevel.Error);
            }
            finally
            {
                Dispatcher.UIThread.Post(this.OnRenderEnded);
            }
        }

        private void OnRenderEnded()
        {
            this.OnTick(this, EventArgs.Empty);
            this.renderTimer.Stop();
            this.UpdateCounts();
            this.cancellation.Dispose();
            this.cancellation = null;
            this.IsRendering = false;
            this.IsCancelling = false;
            this.zoneBrowser.IsRendering = false;
            this.options.IsRendering = false;
            this.zoneBrowser.EndRenderStates(this.zoneBrowser.FileSettings);

            var close = this.closeAfterRender;
            this.closeAfterRender = null;
            close?.Invoke();
        }

        private void OnZoneBrowserChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ZoneBrowserViewModel.RenderLabel))
            {
                this.OnPropertyChanged(nameof(this.ButtonText));
                this.RenderOrCancelCommand.NotifyCanExecuteChanged();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            var pending = this.reporter.Drain();
            foreach (var zone in pending.Started)
            {
                var active = new ActiveZoneViewModel(zone);
                this.activeById[zone.Id] = active;
                this.ActiveZones.Add(active);
                this.zoneBrowser.SetStatus(zone.Id, new ZoneStatus(ZoneMapState.Rendering));
            }

            foreach (var (zoneId, step) in pending.Steps)
            {
                if (this.activeById.TryGetValue(zoneId, out var active))
                {
                    active.Update(step.Label, step.Percent);
                }
            }

            foreach (var (zone, outcome) in pending.Finished)
            {
                var elapsed = TimeSpan.Zero;
                if (this.activeById.Remove(zone.Id, out var active))
                {
                    this.ActiveZones.Remove(active);
                    elapsed = active.Elapsed;
                }

                this.FinishedZones.Add(new FinishedZoneViewModel(zone, outcome, elapsed));
                this.Count(outcome);
                if (outcome == ZoneOutcome.Failed)
                {
                    this.zoneBrowser.SetStatus(zone.Id, new ZoneStatus(ZoneMapState.Failed));
                }
                else
                {
                    this.zoneBrowser.ScanZone(zone.Id);
                }
            }

            this.AddLines(pending.Lines);

            if (this.IsRendering)
            {
                foreach (var active in this.ActiveZones)
                {
                    active.Tick();
                }

                this.UpdateCounts();
            }
        }

        private void Count(ZoneOutcome outcome)
        {
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

        private void UpdateCounts()
        {
            var finished = this.done + this.withErrors + this.skipped + this.failed;
            this.Progress = this.totalZones == 0 ? 0 : 100.0 * finished / this.totalZones;
            this.ProgressText = string.Format("{0} of {1} zones", finished, this.totalZones);
            this.ActivityHeader = this.ActiveZones.Count == 0 ? "Activity" : string.Format("Activity ({0} running)", this.ActiveZones.Count);
            this.CountsText = string.Format("done {0}, with errors {1}, skipped {2}, failed {3}", this.done, this.withErrors, this.skipped, this.failed);
            this.ElapsedText = ActiveZoneViewModel.FormatElapsed(this.renderTimer.Elapsed);
        }

        private void AddLines(List<LogEntry> newLines)
        {
            if (newLines.Count == 0)
            {
                return;
            }

            this.lines.AddRange(newLines);
            if (this.lines.Count > MAX_LOG_LINES)
            {
                this.lines.RemoveRange(0, this.lines.Count - MAX_LOG_LINES);
            }

            var filter = this.LevelFilter;
            var zoneId = this.ZoneFilterId;
            foreach (var line in newLines.Where(l => l.Matches(filter, zoneId)))
            {
                this.ShownLines.Add(line);
            }

            if (this.ShownLines.Count > MAX_LOG_LINES + TRIM_SLACK)
            {
                this.RebuildShownLines();
                return;
            }

            this.LinesAdded?.Invoke();
        }

        private void RebuildShownLines()
        {
            var filter = this.LevelFilter;
            var zoneId = this.ZoneFilterId;
            this.ShownLines = new ObservableCollection<LogEntry>(this.lines.Where(l => l.Matches(filter, zoneId)));
            this.LinesAdded?.Invoke();
        }
    }
}
