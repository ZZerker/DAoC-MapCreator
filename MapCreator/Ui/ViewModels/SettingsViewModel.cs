using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapCreator.Classes;
using MapCreator.Classes.MapCreation;
using MapCreator.Classes.MapCreation.Fixtures;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    internal enum DeleteOutcome
    {
        Deleted,
        NothingToDelete,
        Failed
    }

    /// <summary>
    /// The settings flyout: game folder, output folder, cache cleanup and the about text
    /// </summary>
    public sealed partial class SettingsViewModel : ViewModelBase
    {
        private readonly AppSettings settings;
        private readonly OptionsViewModel options;
        private readonly ZoneBrowserViewModel zoneBrowser;
        private readonly Action<string> gamePathChanged;

        [ObservableProperty]
        private string gamePathText;

        [ObservableProperty]
        private string gameStatus = "";

        [ObservableProperty]
        private bool isGameFound;

        [ObservableProperty]
        private string gameNote = "";

        [ObservableProperty]
        private bool isConfirmingModelCache;

        [ObservableProperty]
        private bool isConfirmingHeightmaps;

        [ObservableProperty]
        private string cacheResult = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool isBusy;

        internal SettingsViewModel(AppSettings settings, OptionsViewModel options, ZoneBrowserViewModel zoneBrowser, Action<string> gamePathChanged)
        {
            this.settings = settings;
            this.options = options;
            this.zoneBrowser = zoneBrowser;
            this.gamePathChanged = gamePathChanged;
            this.gamePathText = settings.GamePath ?? "";
            this.UpdateGameStatus(this.gamePathText);
            this.options.PropertyChanged += this.OnOptionsChanged;
        }

        public bool IsEditable => this.options.IsEditable;

        public bool CanEdit => this.IsEditable && !this.IsBusy;

        public string TargetPath
        {
            get => this.options.TargetPath;
            set => this.options.TargetPath = value;
        }

        public string Version { get; } = "MapCreator " + GetVersion(Assembly.GetExecutingAssembly());

        public string Copyright { get; } = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

        /// <summary>
        /// The informational version without a "+commit" suffix, else the assembly version
        /// </summary>
        internal static string GetVersion(Assembly assembly)
        {
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(version))
            {
                version = assembly.GetName().Version?.ToString() ?? "";
            }

            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }

        /// <summary>
        /// Applies the path to the settings only when it holds camelot.exe
        /// </summary>
        internal static bool TryApplyGamePath(AppSettings settings, string path)
        {
            if (!GameFolderLocator.IsGameFolder(path))
            {
                return false;
            }

            settings.GamePath = path;
            return true;
        }

        internal static (DeleteOutcome Outcome, string Error) DeleteFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return (DeleteOutcome.NothingToDelete, null);
                }

                Directory.Delete(path, true);
                return (DeleteOutcome.Deleted, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return (DeleteOutcome.Failed, ex.Message);
            }
        }

        partial void OnGamePathTextChanged(string value)
        {
            var path = (value ?? "").Trim();
            this.UpdateGameStatus(path);
            if (!this.IsGameFound || path == this.settings.GamePath || !TryApplyGamePath(this.settings, path))
            {
                return;
            }

            SettingsSaver.Request();
            this.gamePathChanged(path);
            this.zoneBrowser.RefreshStatuses(RenderSettings.FromSettings(this.settings));
            this.GameNote = "The client zone list is read at start, restart to reload it.";
        }

        [RelayCommand]
        private void AutoDetect()
        {
            var found = GameFolderLocator.Find();
            if (found == null)
            {
                this.GameNote = "No client found";
                return;
            }

            this.GameNote = "";
            this.GamePathText = found;
        }

        [RelayCommand]
        private void AskClearModelCache()
        {
            this.ResetCachePrompts();
            this.IsConfirmingModelCache = true;
        }

        /// <summary>
        /// Closes both prompts and clears the last result
        /// </summary>
        internal void ResetCachePrompts()
        {
            this.IsConfirmingModelCache = false;
            this.IsConfirmingHeightmaps = false;
            if (!this.IsBusy)
            {
                this.CacheResult = "";
            }
        }

        [RelayCommand]
        private void CancelClearModelCache()
        {
            this.IsConfirmingModelCache = false;
        }

        [RelayCommand]
        private async Task ConfirmClearModelCache()
        {
            this.IsConfirmingModelCache = false;
            if (!this.CanEdit)
            {
                return;
            }

            this.IsBusy = true;
            this.CacheResult = "Deleting...";
            try
            {
                await Task.Run(FixtureCache.Clear);
                this.CacheResult = "Model cache deleted";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                this.ReportFailure(ex.Message);
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        [RelayCommand]
        private void AskClearHeightmaps()
        {
            this.ResetCachePrompts();
            this.IsConfirmingHeightmaps = true;
        }

        [RelayCommand]
        private void CancelClearHeightmaps()
        {
            this.IsConfirmingHeightmaps = false;
        }

        [RelayCommand]
        private async Task ConfirmClearHeightmaps()
        {
            this.IsConfirmingHeightmaps = false;
            if (!this.CanEdit)
            {
                return;
            }

            this.IsBusy = true;
            this.CacheResult = "Deleting...";
            (DeleteOutcome outcome, string error) result;
            try
            {
                result = await Task.Run(() => DeleteFolder(MapHeightmap.CacheDirectory));
            }
            finally
            {
                this.IsBusy = false;
            }

            var (outcome, error) = result;
            switch (outcome)
            {
                case DeleteOutcome.Deleted:
                    this.CacheResult = "Heightmaps deleted";
                    break;
                case DeleteOutcome.NothingToDelete:
                    this.CacheResult = "Nothing to delete";
                    break;
                default:
                    this.ReportFailure(error);
                    break;
            }
        }

        private void ReportFailure(string reason)
        {
            this.CacheResult = "Could not delete: " + reason;
            AppLog.Log(this.CacheResult, LogLevel.Warning);
        }

        private void UpdateGameStatus(string path)
        {
            this.IsGameFound = GameFolderLocator.IsGameFolder(path);
            this.GameStatus = this.IsGameFound ? "camelot.exe found" : "camelot.exe not found in this folder";
        }

        private void OnOptionsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OptionsViewModel.TargetPath) || string.IsNullOrEmpty(e.PropertyName))
            {
                this.OnPropertyChanged(nameof(this.TargetPath));
            }

            if (e.PropertyName == nameof(OptionsViewModel.IsEditable) || string.IsNullOrEmpty(e.PropertyName))
            {
                this.OnPropertyChanged(nameof(this.IsEditable));
                this.OnPropertyChanged(nameof(this.CanEdit));
            }
        }
    }
}
