using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MapCreator.Ui.ViewModels
{
    /// <summary>
    /// A group checkbox: checked when all its zones are ticked, indeterminate (null) when some are
    /// </summary>
    public sealed partial class ZoneGroupViewModel : ViewModelBase
    {
        private int tickedCount;

        internal ZoneGroupViewModel(string name, IReadOnlyList<ZoneRowViewModel> zones)
        {
            this.Name = name;
            this.Zones = zones;
            foreach (var zone in zones)
            {
                if (zone.IsTicked)
                {
                    this.tickedCount++;
                }

                zone.PropertyChanged += this.OnZoneChanged;
            }
        }

        public string Name { get; }

        public IReadOnlyList<ZoneRowViewModel> Zones { get; }

        public string Label => string.Format("{0} ({1})", this.Name, this.Zones.Count);

        public bool? State
        {
            get
            {
                if (this.tickedCount == 0)
                {
                    return false;
                }

                if (this.tickedCount == this.Zones.Count)
                {
                    return true;
                }

                return null;
            }
        }

        // Unticks a fully ticked group, otherwise ticks all its zones
        [RelayCommand]
        private void Toggle()
        {
            var tick = this.State != true;
            foreach (var zone in this.Zones)
            {
                zone.IsTicked = tick;
            }

            // The checkbox toggled itself before the command ran; push the real state back
            this.OnPropertyChanged(nameof(this.State));
        }

        private void OnZoneChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ZoneRowViewModel.IsTicked))
            {
                return;
            }

            var before = this.State;
            this.tickedCount += ((ZoneRowViewModel)sender).IsTicked ? 1 : -1;
            if (before != this.State)
            {
                this.OnPropertyChanged(nameof(this.State));
            }
        }
    }
}
