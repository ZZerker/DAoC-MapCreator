using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            this.InitializeComponent();
            this.AttachedToVisualTree += this.OnAttached;
        }

        // The popup attaches its content each time the flyout opens
        private void OnAttached(object sender, VisualTreeAttachmentEventArgs e)
        {
            (this.DataContext as SettingsViewModel)?.ResetCachePrompts();
        }

        private async void OnBrowseGameClick(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is not SettingsViewModel settings)
            {
                return;
            }

            var path = await this.PickFolder("Game folder", settings.GamePathText);
            if (path != null)
            {
                settings.GamePathText = path;
            }
        }

        private async void OnBrowseOutputClick(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is not SettingsViewModel settings)
            {
                return;
            }

            var path = await this.PickFolder("Output folder", settings.TargetPath);
            if (path != null)
            {
                settings.TargetPath = path;
            }
        }

        private async System.Threading.Tasks.Task<string> PickFolder(string title, string current)
        {
            // The flyout is a popup, which may have no storage provider; ask the main window
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var storage = mainWindow?.StorageProvider ?? TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null)
            {
                return null;
            }

            var start = Directory.Exists(current) ? await storage.TryGetFolderFromPathAsync(current) : null;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }
    }
}
