using System;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using MapCreator.Classes;

namespace MapCreator.Ui.ViewModels
{
    public enum ZoneMapState
    {
        Checking,
        NotRendered,
        Rendered,
        ArchiveNewer,
        Queued,
        Rendering,
        Failed,
        Unknown
    }

    /// <summary>
    /// A zone's map in the viewed folder, or its place in the running render; Time is the map's write time
    /// </summary>
    public readonly record struct ZoneStatus(ZoneMapState State, DateTime Time = default)
    {
        // Set by a render; a folder scan does not replace them while it runs
        public bool IsRenderState => this.State is ZoneMapState.Queued or ZoneMapState.Rendering or ZoneMapState.Failed;
    }

    public sealed partial class ZoneRowViewModel : ViewModelBase
    {
        private static readonly IBrush RenderedBrush = new ImmutableSolidColorBrush(Color.Parse("#5CC46E"));
        private static readonly IBrush RenderingBrush = new ImmutableSolidColorBrush(Color.Parse("#4FA3E0"));
        private static readonly IBrush ArchiveNewerBrush = new ImmutableSolidColorBrush(Color.Parse("#E8A247"));
        private static readonly IBrush FailedBrush = new ImmutableSolidColorBrush(Color.Parse("#F06262"));
        private static readonly IBrush QueuedBrush = new ImmutableSolidColorBrush(Color.Parse("#A0A0A0"));
        private static readonly IBrush MissingBrush = new ImmutableSolidColorBrush(Color.Parse("#6A6A6A"));

        [ObservableProperty]
        private bool isTicked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBrush))]
        private ZoneStatus status = new(ZoneMapState.Checking);

        internal ZoneRowViewModel(ZoneSelection zone, bool isTicked)
        {
            this.Zone = zone;
            this.IsTicked = isTicked;
        }

        public ZoneSelection Zone { get; }

        public string Id => this.Zone.Id;

        public string Name => this.Zone.Name;

        public string Details => string.Format("{0}, {1}", this.Zone.Realm, this.Zone.Type);

        // The dot keeps the state readable without the color
        public string StatusText => this.Status.State switch
        {
            ZoneMapState.Rendered => "● rendered " + this.Status.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ZoneMapState.ArchiveNewer => "● archive newer than map",
            ZoneMapState.Queued => "○ queued",
            ZoneMapState.Rendering => "◐ rendering",
            ZoneMapState.Failed => "✕ failed",
            ZoneMapState.NotRendered => "○ not rendered",
            ZoneMapState.Unknown => "? unknown",
            _ => "checking"
        };

        public IBrush StatusBrush => this.Status.State switch
        {
            ZoneMapState.Rendered => RenderedBrush,
            ZoneMapState.ArchiveNewer => ArchiveNewerBrush,
            ZoneMapState.Queued => QueuedBrush,
            ZoneMapState.Rendering => RenderingBrush,
            ZoneMapState.Failed => FailedBrush,
            _ => MissingBrush
        };
    }
}
