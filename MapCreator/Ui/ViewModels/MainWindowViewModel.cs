using CommunityToolkit.Mvvm.ComponentModel;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    public sealed partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string outputFolder;

        public MainWindowViewModel()
        {
            // Warnings outside a render (settings, zone list) go to the activity log as well
            var reporter = new UiRenderReporter();
            AppLog.Reporter = reporter;

            var settings = AppSettings.Current;
            var renderSettings = RenderSettings.FromSettings(settings);
            this.GamePath = string.IsNullOrEmpty(settings.GamePath) ? "not set" : settings.GamePath;
            this.OutputFolder = renderSettings.TargetPath;
            this.ZoneBrowser = new ZoneBrowserViewModel(settings, renderSettings);
            this.Options = new OptionsViewModel(settings);
            this.Options.OutputChanged += this.OnOutputChanged;
            this.Render = new RenderViewModel(reporter, this.ZoneBrowser, this.Options);
        }

        public string GamePath { get; }

        public ZoneBrowserViewModel ZoneBrowser { get; }

        public OptionsViewModel Options { get; }

        public RenderViewModel Render { get; }

        private void OnOutputChanged()
        {
            var renderSettings = RenderSettings.FromSettings(AppSettings.Current);
            this.OutputFolder = renderSettings.TargetPath;
            this.ZoneBrowser.RefreshStatuses(renderSettings);
        }
    }
}
