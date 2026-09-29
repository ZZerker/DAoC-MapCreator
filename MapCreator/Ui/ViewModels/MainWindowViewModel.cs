using CommunityToolkit.Mvvm.ComponentModel;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    public sealed partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string outputFolder;

        [ObservableProperty]
        private string gamePath;

        public MainWindowViewModel()
        {
            // Warnings outside a render (settings, zone list) go to the activity log as well
            var reporter = new UiRenderReporter();
            AppLog.Reporter = reporter;

            var settings = AppSettings.Current;
            var renderSettings = RenderSettings.FromSettings(settings);
            this.ShowGamePath(settings.GamePath);
            this.OutputFolder = renderSettings.TargetPath;
            this.ZoneBrowser = new ZoneBrowserViewModel(settings, renderSettings);
            this.Options = new OptionsViewModel(settings);
            this.Options.OutputChanged += this.OnOutputChanged;
            this.Render = new RenderViewModel(reporter, this.ZoneBrowser, this.Options);
            this.MapViewer = new MapViewerViewModel(this.ZoneBrowser, this.Render);
            this.Settings = new SettingsViewModel(settings, this.Options, this.ZoneBrowser, this.ShowGamePath);
        }

        public ZoneBrowserViewModel ZoneBrowser { get; }

        public OptionsViewModel Options { get; }

        public RenderViewModel Render { get; }

        public MapViewerViewModel MapViewer { get; }

        public SettingsViewModel Settings { get; }

        private void ShowGamePath(string path)
        {
            this.GamePath = string.IsNullOrEmpty(path) ? "not set" : path;
        }

        private void OnOutputChanged()
        {
            var renderSettings = RenderSettings.FromSettings(AppSettings.Current);
            this.OutputFolder = renderSettings.TargetPath;
            this.ZoneBrowser.RefreshStatuses(renderSettings);
        }
    }
}
