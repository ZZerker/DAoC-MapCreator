using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
    /// Zone list with search, group checkboxes and the render status of every zone
    /// </summary>
    public sealed partial class ZoneBrowserViewModel : ViewModelBase
    {
        private const int STATUS_CHUNK = 50;

        private readonly AppSettings settings;
        private readonly List<ZoneRowViewModel> zones;
        private readonly Dictionary<string, ZoneRowViewModel> rowsById;
        private int statusGeneration;

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShownSummary))]
        private IReadOnlyList<ZoneRowViewModel> shownZones;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RenderLabel))]
        private int tickedCount;

        [ObservableProperty]
        private bool isGroupSearch;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEditable))]
        private bool isRendering;

        internal ZoneBrowserViewModel(AppSettings settings, RenderSettings renderSettings)
        {
            this.settings = settings;

            var entries = DataWrapper.GetAllZones();
            var ticked = settings.TickedZones.ToHashSet();

            // zones.xml can list a zone under several realms; one row per id
            this.zones = entries
                .GroupBy(z => z.Id)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ZoneRowViewModel(g.First(), ticked.Contains(g.Key)))
                .ToList();
            this.rowsById = this.zones.ToDictionary(z => z.Id);

            this.AllGroup = new ZoneGroupViewModel("All", this.zones);
            this.RealmGroups = BuildGroups(entries, z => z.Realm, this.rowsById);
            this.ExpansionGroups = BuildGroups(entries, z => z.Expansion, this.rowsById);
            this.TypeGroups = BuildGroups(entries, z => z.Type, this.rowsById);

            foreach (var zone in this.zones)
            {
                zone.PropertyChanged += this.OnZoneChanged;
            }

            this.TickedCount = this.zones.Count(z => z.IsTicked);
            this.ShownZones = this.zones;
            this.RefreshStatuses(renderSettings);
        }

        public ZoneGroupViewModel AllGroup { get; }

        public IReadOnlyList<ZoneGroupViewModel> RealmGroups { get; }

        public IReadOnlyList<ZoneGroupViewModel> ExpansionGroups { get; }

        public IReadOnlyList<ZoneGroupViewModel> TypeGroups { get; }

        public string ShownSummary => string.Format("{0} of {1} shown", this.ShownZones.Count, this.zones.Count);

        public string RenderLabel => this.TickedCount == 1 ? "Render 1 zone" : string.Format("Render {0} zones", this.TickedCount);

        // Ticks are read-only while a render runs
        public bool IsEditable => !this.IsRendering;

        /// <summary>
        /// "rendered" with the file date, "archive newer" when a zone archive changed after the render, or "not rendered"
        /// </summary>
        internal static string GetStatus(RenderSettings renderSettings, string gamePath, ZoneSelection zone)
        {
            var file = ZoneRenderer.GetTargetFile(renderSettings, zone);
            if (!file.Exists)
            {
                return "not rendered";
            }

            var zoneDirectory = GameFolderLocator.IsGameFolder(gamePath) ? ZoneCatalog.FindZoneDirectory(gamePath, zone.Id) : null;
            if (zoneDirectory != null && Directory.EnumerateFiles(zoneDirectory, "*.mpk").Any(f => File.GetLastWriteTimeUtc(f) > file.LastWriteTimeUtc))
            {
                return "archive newer";
            }

            return "rendered " + file.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        partial void OnSearchTextChanged(string value)
        {
            this.ApplyFilter();
        }

        /// <summary>
        /// Computes the status of every zone in the background; a newer call makes the running one stop
        /// </summary>
        internal void RefreshStatuses(RenderSettings renderSettings)
        {
            var generation = Interlocked.Increment(ref this.statusGeneration);
            var rows = this.zones.ToList();
            var gamePath = this.settings.GamePath;
            Task.Run(() =>
            {
                for (var start = 0; start < rows.Count; start += STATUS_CHUNK)
                {
                    if (Volatile.Read(ref this.statusGeneration) != generation)
                    {
                        return;
                    }

                    var chunk = rows.Skip(start).Take(STATUS_CHUNK).ToList();
                    var statuses = chunk.Select(row => ScanStatus(renderSettings, gamePath, row.Zone)).ToList();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (this.statusGeneration != generation)
                        {
                            return;
                        }

                        for (var i = 0; i < chunk.Count; i++)
                        {
                            chunk[i].Status = statuses[i];
                        }
                    });
                }
            });
        }

        internal void SetStatus(string zoneId, string status)
        {
            if (this.rowsById.TryGetValue(zoneId, out var row))
            {
                row.Status = status;
            }
        }

        internal static string ScanStatus(RenderSettings renderSettings, string gamePath, ZoneSelection zone)
        {
            try
            {
                return GetStatus(renderSettings, gamePath, zone);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return "unknown";
            }
        }

        [RelayCommand]
        private void TickShown()
        {
            this.TickAll(this.ShownZones, true);
        }

        [RelayCommand]
        private void UntickShown()
        {
            this.TickAll(this.ShownZones, false);
        }

        private void TickAll(IEnumerable<ZoneRowViewModel> rows, bool tick)
        {
            if (this.IsRendering)
            {
                return;
            }

            foreach (var zone in rows)
            {
                zone.IsTicked = tick;
            }
        }

        private static List<ZoneGroupViewModel> BuildGroups(List<ZoneSelection> entries, Func<ZoneSelection, string> key, Dictionary<string, ZoneRowViewModel> rowsById)
        {
            return entries
                .GroupBy(key)
                .Select(g => new ZoneGroupViewModel(g.Key, g.Select(z => z.Id).Distinct().Select(id => rowsById[id]).ToList()))
                .ToList();
        }

        // Terms like nf+outdoor, 163,171 or a group name select by id; other text searches names and ids
        private static HashSet<string> ResolveTerms(string text)
        {
            var isTermList = text.Contains('+') || text.Contains(',');
            if (!isTermList && text.All(char.IsDigit))
            {
                return null;
            }

            var warned = false;
            var ids = ZoneGroups.Resolve(text, warning => { warned = true; }).ToHashSet();
            if (warned && !isTermList)
            {
                return null;
            }

            return ids;
        }

        private void ApplyFilter()
        {
            var text = (this.SearchText ?? "").Trim();
            if (text.Length == 0)
            {
                this.IsGroupSearch = false;
                this.ShownZones = this.zones;
                return;
            }

            var ids = ResolveTerms(text);
            this.IsGroupSearch = ids != null;
            if (ids != null)
            {
                this.ShownZones = this.zones.Where(z => ids.Contains(z.Id)).ToList();
                return;
            }

            this.ShownZones = this.zones
                .Where(z => z.Id.Contains(text, StringComparison.OrdinalIgnoreCase) || z.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void OnZoneChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ZoneRowViewModel.IsTicked))
            {
                return;
            }

            this.TickedCount += ((ZoneRowViewModel)sender).IsTicked ? 1 : -1;
            this.settings.TickedZones = this.zones.Where(z => z.IsTicked).Select(z => z.Id).ToList();
            SettingsSaver.Request();
        }
    }
}
