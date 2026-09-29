using System;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    /// <summary>
    /// A zone that is rendering right now
    /// </summary>
    public sealed partial class ActiveZoneViewModel : ViewModelBase
    {
        private readonly Stopwatch timer = Stopwatch.StartNew();

        [ObservableProperty]
        private string step = "Starting";

        [ObservableProperty]
        private double percent;

        [ObservableProperty]
        private bool isIndeterminate = true;

        [ObservableProperty]
        private string elapsedText = "00:00";

        internal ActiveZoneViewModel(ZoneSelection zone)
        {
            this.Id = zone.Id;
            this.Name = zone.Name;
        }

        public string Id { get; }

        public string Name { get; }

        internal TimeSpan Elapsed => this.timer.Elapsed;

        internal void Update(string label, int? progress)
        {
            if (label != null)
            {
                this.Step = label;
            }

            this.IsIndeterminate = progress == null;
            this.Percent = progress ?? 0;
        }

        internal void Tick()
        {
            this.ElapsedText = FormatElapsed(this.timer.Elapsed);
        }

        internal static string FormatElapsed(TimeSpan elapsed)
        {
            return elapsed.TotalHours >= 1 ? elapsed.ToString("h\\:mm\\:ss") : elapsed.ToString("mm\\:ss");
        }
    }

    /// <summary>
    /// A zone of the current or last render that has ended
    /// </summary>
    public sealed class FinishedZoneViewModel : ViewModelBase
    {
        internal FinishedZoneViewModel(ZoneSelection zone, ZoneOutcome outcome, TimeSpan elapsed)
        {
            this.Id = zone.Id;
            this.Name = zone.Name;
            this.ElapsedText = ActiveZoneViewModel.FormatElapsed(elapsed);
            this.Outcome = outcome switch
            {
                ZoneOutcome.WithErrors => "with errors",
                ZoneOutcome.Skipped => "skipped",
                ZoneOutcome.Failed => "failed",
                _ => "done"
            };
            this.Foreground = outcome switch
            {
                ZoneOutcome.WithErrors => LogEntry.WarningBrush,
                ZoneOutcome.Skipped => LogEntry.NormalBrush,
                ZoneOutcome.Failed => LogEntry.ErrorBrush,
                _ => LogEntry.SuccessBrush
            };
        }

        public string Id { get; }

        public string Name { get; }

        public string Outcome { get; }

        public string ElapsedText { get; }

        public IBrush Foreground { get; }
    }
}
