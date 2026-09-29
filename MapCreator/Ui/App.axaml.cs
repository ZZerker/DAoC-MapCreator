using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
                desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
