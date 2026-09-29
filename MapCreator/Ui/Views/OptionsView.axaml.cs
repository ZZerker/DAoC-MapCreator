using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui.Views
{
    public partial class OptionsView : UserControl
    {
        public OptionsView()
        {
            this.InitializeComponent();
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null || this.DataContext is not OptionsViewModel options)
            {
                return;
            }

            var start = Directory.Exists(options.TargetPath) ? await storage.TryGetFolderFromPathAsync(options.TargetPath) : null;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Output folder", AllowMultiple = false, SuggestedStartLocation = start });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path != null)
            {
                options.TargetPath = path;
            }
        }
    }
}
