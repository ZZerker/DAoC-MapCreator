using System;
using System.ComponentModel;
using Avalonia.Controls;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui.Views
{
    public partial class MainWindow : Window
    {
        private const int ACTIVITY_ROW = 3;
        private const double DEFAULT_ACTIVITY_HEIGHT = 220;
        private const double MIN_ACTIVITY_HEIGHT = 80;

        private MainWindowViewModel viewModel;
        private double activityHeight = DEFAULT_ACTIVITY_HEIGHT;
        private bool closeRequested;

        public MainWindow()
        {
            this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
            this.Closing += this.OnWindowClosing;
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (this.viewModel != null)
            {
                this.viewModel.Render.PropertyChanged -= this.OnRenderChanged;
            }

            this.viewModel = this.DataContext as MainWindowViewModel;
            if (this.viewModel != null)
            {
                this.viewModel.Render.PropertyChanged += this.OnRenderChanged;
                this.UpdateActivityRow();
            }
        }

        private void OnRenderChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RenderViewModel.IsActivityExpanded))
            {
                this.UpdateActivityRow();
            }
        }

        // The activity row is sized here so the splitter above it can resize it; the height is kept for this session only
        private void UpdateActivityRow()
        {
            var row = this.RootGrid.RowDefinitions[ACTIVITY_ROW];
            if (this.viewModel.Render.IsActivityExpanded)
            {
                row.MinHeight = MIN_ACTIVITY_HEIGHT;
                row.Height = new GridLength(this.activityHeight);
                return;
            }

            if (row.ActualHeight > 0)
            {
                this.activityHeight = row.ActualHeight;
            }

            row.MinHeight = 0;
            row.Height = new GridLength(0);
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
