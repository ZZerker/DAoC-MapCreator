using Avalonia.Controls;
using Avalonia.Threading;

namespace MapCreator.Ui.Views
{
    public partial class ZoneBrowserView : UserControl
    {
        public ZoneBrowserView()
        {
            this.InitializeComponent();

            // The group list opened scrolled down; start at the top once the first layout is done
            this.Loaded += (sender, e) => Dispatcher.UIThread.Post(() => this.GroupScroller.ScrollToHome(), DispatcherPriority.Background);
        }
    }
}
