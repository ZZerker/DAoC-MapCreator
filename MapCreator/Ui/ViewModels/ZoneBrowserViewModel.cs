using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private ZoneRowViewModel selectedRow;

        [ObservableProperty]
        private bool isGroupSearch;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEditable))]
        private bool isRendering;

        [ObservableProperty]
        private RenderFolder selectedFolder;

        // Full path of the folder the zone status and the map viewer read; null when the subfolder pattern has placeholders
        [ObservableProperty]
        private string viewedFolder;

        // Output folder and file pattern for the file names
        private RenderSettings fileSettings;

        internal RenderSettings FileSettings => this.fileSettings;

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
            this.ReloadFolders(renderSettings);
            this.RefreshStatuses(renderSettings);
        }

        // Render folders under the output folder, newest first
        public ObservableCollection<RenderFolder> Folders { get; } = new();

        public ZoneGroupViewModel AllGroup { get; }

        public IReadOnlyList<ZoneGroupViewModel> RealmGroups { get; }

        public IReadOnlyList<ZoneGroupViewModel> ExpansionGroups { get; }

        public IReadOnlyList<ZoneGroupViewModel> TypeGroups { get; }

        public string ShownSummary => string.Format("{0} of {1} shown", this.ShownZones.Count, this.zones.Count);

        public string RenderLabel => this.TickedCount == 1 ? "Render 1 zone" : string.Format("Render {0} zones", this.TickedCount);

        // Ticks are read-only while a render runs
        public bool IsEditable => !this.IsRendering;

        /// <summary>
        /// The zone's map in the folder; without a folder (subfolder pattern with placeholders) where a window render writes it
        /// </summary>
        internal static FileInfo FindMap(string folder, RenderSettings renderSettings, ZoneSelection zone)
        {
            return folder == null ? ZoneRenderer.GetTargetFile(renderSettings, zone) : RenderFolders.FindMap(folder, renderSettings, zone);
        }

        /// <summary>
        /// Rendered with the file date, archive newer when a zone archive changed after the render, or not rendered
        /// </summary>
        internal static ZoneStatus GetStatus(string folder, RenderSettings renderSettings, string gamePath, ZoneSelection zone)
        {
            var file = FindMap(folder, renderSettings, zone);
            if (!file.Exists)
            {
                return new ZoneStatus(ZoneMapState.NotRendered);
            }

            var zoneDirectory = GameFolderLocator.IsGameFolder(gamePath) ? ZoneCatalog.FindZoneDirectory(gamePath, zone.Id) : null;
            if (zoneDirectory != null && Directory.EnumerateFiles(zoneDirectory, "*.mpk").Any(f => File.GetLastWriteTimeUtc(f) > file.LastWriteTimeUtc))
            {
                return new ZoneStatus(ZoneMapState.ArchiveNewer, file.LastWriteTime);
            }

            return new ZoneStatus(ZoneMapState.Rendered, file.LastWriteTime);
        }

        internal static ZoneStatus ScanStatus(string folder, RenderSettings renderSettings, string gamePath, ZoneSelection zone)
        {
            try
            {
                return GetStatus(folder, renderSettings, gamePath, zone);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return new ZoneStatus(ZoneMapState.Unknown);
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            this.ApplyFilter();
        }

        partial void OnSelectedFolderChanged(RenderFolder value)
        {
            if (value == null || string.Equals(value.Path, this.ViewedFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.ViewedFolder = value.Path;
            this.settings.ViewedFolder = value.Path;
            SettingsSaver.Request();
            this.RefreshStatuses(this.fileSettings);
        }

        /// <summary>
        /// Reads the render folders of the output folder again; keeps the viewed folder, else the saved one, else where window renders write
        /// </summary>
        internal void ReloadFolders(RenderSettings renderSettings)
        {
            this.fileSettings = renderSettings;
            var wanted = this.ViewedFolder;
            if (wanted == null || !wanted.StartsWith(renderSettings.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                wanted = !string.IsNullOrEmpty(this.settings.ViewedFolder) && Directory.Exists(this.settings.ViewedFolder)
                    ? this.settings.ViewedFolder
                    : RenderFolders.TargetFolder(renderSettings);
            }

            if (!string.Equals(wanted, this.ViewedFolder, StringComparison.OrdinalIgnoreCase))
            {
                this.ViewedFolder = wanted;
                this.RefreshStatuses(renderSettings);
            }

            var outputPath = renderSettings.TargetPath;
            Task.Run(() => RenderFolders.List(outputPath)).ContinueWith(task =>
            {
                var folders = task.IsCompletedSuccessfully ? task.Result : new List<RenderFolder>();
                Dispatcher.UIThread.Post(() => this.ShowFolders(folders));
            });
        }

        [RelayCommand]
        private void RefreshFolders()
        {
            this.ReloadFolders(this.fileSettings);
        }

        private void ShowFolders(List<RenderFolder> folders)
        {
            this.Folders.Clear();
            foreach (var folder in folders)
            {
                this.Folders.Add(folder);
            }

            this.SelectFolder(this.ViewedFolder);
        }

        // The folder may hold no map yet (a render just started); it is listed anyway
        private void SelectFolder(string path)
        {
            if (path == null)
            {
                this.SelectedFolder = null;
                return;
            }

            var folder = this.Folders.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (folder == null)
            {
                folder = new RenderFolder(path, Path.GetFileName(path.TrimEnd('\\')), 0, DateTime.Now);
                this.Folders.Insert(0, folder);
            }

            this.SelectedFolder = folder;
        }

        /// <summary>
        /// Shows the folder a render writes into, with the file names of that render
        /// </summary>
        internal void ShowRenderFolder(RenderSettings renderSettings)
        {
            var folder = RenderFolders.TargetFolder(renderSettings);
            if (folder == null)
            {
                return;
            }

            // Selecting it rescans the zones the render does not touch
            this.fileSettings = renderSettings;
            this.SelectFolder(folder);
        }

        /// <summary>
        /// Computes the status of every zone in the viewed folder in the background; a newer call makes the running one stop.
        /// While a render runs, and with keepRenderStates, rows the render has marked keep their state.
        /// </summary>
        internal void RefreshStatuses(RenderSettings renderSettings, bool keepRenderStates = false)
        {
            this.fileSettings = renderSettings;
            var generation = Interlocked.Increment(ref this.statusGeneration);
            var rows = this.zones.ToList();
            var gamePath = this.settings.GamePath;
            var folder = this.ViewedFolder;
            var keep = keepRenderStates || this.IsRendering;
            Task.Run(() =>
            {
                for (var start = 0; start < rows.Count; start += STATUS_CHUNK)
                {
                    if (Volatile.Read(ref this.statusGeneration) != generation)
                    {
                        return;
                    }

                    var chunk = rows.Skip(start).Take(STATUS_CHUNK).ToList();
                    var statuses = chunk.Select(row => ScanStatus(folder, renderSettings, gamePath, row.Zone)).ToList();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (this.statusGeneration != generation)
                        {
                            return;
                        }

                        for (var i = 0; i < chunk.Count; i++)
                        {
                            if (!(keep && chunk[i].Status.IsRenderState))
                            {
                                chunk[i].Status = statuses[i];
                            }
                        }
                    });
                }
            });
        }

        internal void SetStatus(string zoneId, ZoneStatus status)
        {
            if (this.rowsById.TryGetValue(zoneId, out var row))
            {
                row.Status = status;
            }
        }

        /// <summary>
        /// Reads the zone's map in the viewed folder again (after the render wrote it)
        /// </summary>
        internal void ScanZone(string zoneId)
        {
            if (!this.rowsById.TryGetValue(zoneId, out var row))
            {
                return;
            }

            row.Status = ScanStatus(this.ViewedFolder, this.fileSettings, this.settings.GamePath, row.Zone);
        }

        /// <summary>
        /// Rows still queued or rendering when a render ends (cancelled) are read from the folder again; failed ones stay
        /// </summary>
        internal void EndRenderStates(RenderSettings renderSettings)
        {
            foreach (var row in this.zones.Where(z => z.Status.State is ZoneMapState.Queued or ZoneMapState.Rendering))
            {
                row.Status = new ZoneStatus(ZoneMapState.Checking);
            }

            this.RefreshStatuses(renderSettings, true);
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
