using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;
using MapCreator.Ui.ViewModels;
using MapCreator.Ui.Views;

namespace MapCreator.Ui
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var viewModel = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow { DataContext = viewModel };

                var args = desktop.Args ?? System.Array.Empty<string>();
                if (BatchMode.IsUiRun(args))
                {
                    var settings = BatchMode.ParseArgs(args, out var zoneTerms, out _);
                    if (zoneTerms.Count > 0)
                    {
                        var zoneIds = ZoneGroups.Resolve(string.Join(",", zoneTerms), message => AppLog.Log(message, LogLevel.Warning)).ToList();
                        Dispatcher.UIThread.Post(() => viewModel.Render.RenderFromCommandLine(zoneIds, settings));
                    }
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
