using System;
using Avalonia.Controls;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui.Views
{
    public partial class RenderView : UserControl
    {
        private RenderViewModel viewModel;

        public RenderView()
        {
            this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (this.viewModel != null)
            {
                this.viewModel.LinesAdded -= this.OnLinesAdded;
            }

            this.viewModel = this.DataContext as RenderViewModel;
            if (this.viewModel != null)
            {
                this.viewModel.LinesAdded += this.OnLinesAdded;
            }
        }

        // Keeps the newest log line in view
        private void OnLinesAdded()
        {
            var count = this.viewModel.ShownLines.Count;
            if (count > 0 && this.LogList.IsEffectivelyVisible)
            {
                this.LogList.ScrollIntoView(count - 1);
            }
        }
    }
}
