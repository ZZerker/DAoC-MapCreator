using CommunityToolkit.Mvvm.ComponentModel;
using MapCreator.Classes;

namespace MapCreator.Ui.ViewModels
{
    public sealed partial class ZoneRowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool isTicked;

        [ObservableProperty]
        private string status = "checking";

        internal ZoneRowViewModel(ZoneSelection zone, bool isTicked)
        {
            this.Zone = zone;
            this.IsTicked = isTicked;
        }

        public ZoneSelection Zone { get; }

        public string Id => this.Zone.Id;

        public string Name => this.Zone.Name;

        public string Details => string.Format("{0}, {1}", this.Zone.Realm, this.Zone.Type);
    }
}
