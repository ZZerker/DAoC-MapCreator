using MapCreator.Classes;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            var settings = AppSettings.Current;
            var renderSettings = RenderSettings.FromSettings(settings);
            this.GamePath = string.IsNullOrEmpty(settings.GamePath) ? "not set" : settings.GamePath;
            this.OutputFolder = renderSettings.TargetPath;
            this.ZoneBrowser = new ZoneBrowserViewModel(settings, renderSettings);
        }

        public string GamePath { get; }

        public string OutputFolder { get; }

        public ZoneBrowserViewModel ZoneBrowser { get; }
    }
}
