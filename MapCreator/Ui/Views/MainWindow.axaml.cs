using System;
using Avalonia.Controls;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel viewModel;
        private bool closeRequested;

        public MainWindow()
        {
            this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
            this.Closing += this.OnWindowClosing;
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            this.viewModel = this.DataContext as MainWindowViewModel;

            // The flyout content lives in a popup and may not inherit the window's data context
            if (this.viewModel != null && this.SettingsButton.Flyout is Flyout { Content: Control content })
            {
                content.DataContext = this.viewModel.Settings;
            }
        }

        private void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            // Closing during a render cancels it and waits for the running zones; a second close does not wait
            if (this.viewModel != null && this.viewModel.Render.IsRendering && !this.closeRequested)
            {
                e.Cancel = true;
                this.closeRequested = true;
                this.viewModel.Render.CloseAfterRender(this.Close);
                return;
            }

            SettingsSaver.Flush();
        }
    }
}
