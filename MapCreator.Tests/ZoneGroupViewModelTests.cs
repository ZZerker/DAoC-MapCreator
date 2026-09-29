using System.Collections.Generic;
using System.Linq;
using MapCreator.Classes;
using MapCreator.Ui.ViewModels;
using Xunit;

namespace MapCreator.Tests
{
    public class ZoneGroupViewModelTests
    {
        private static List<ZoneRowViewModel> Rows(params bool[] ticked)
        {
            return ticked
                .Select((isTicked, i) => new ZoneRowViewModel(new ZoneSelection((100 + i).ToString(), "Zone " + i, "Classic", "Albion", "Outdoor"), isTicked))
                .ToList();
        }

        [Fact]
        public void StateFollowsTheTickedZones()
        {
            var rows = Rows(false, false, false);
            var group = new ZoneGroupViewModel("Albion", rows);
            Assert.False(group.State);

            rows[0].IsTicked = true;
            Assert.Null(group.State);

            rows[1].IsTicked = true;
            rows[2].IsTicked = true;
            Assert.True(group.State);

            rows[1].IsTicked = false;
            Assert.Null(group.State);
        }

        [Fact]
        public void InitialTicksAreCounted()
        {
            Assert.True(new ZoneGroupViewModel("a", Rows(true, true)).State);
            Assert.Null(new ZoneGroupViewModel("b", Rows(true, false)).State);
        }

        [Fact]
        public void ToggleTicksAllUnlessAllAreTicked()
        {
            var rows = Rows(true, false, false);
            var group = new ZoneGroupViewModel("Albion", rows);

            group.ToggleCommand.Execute(null);
            Assert.True(rows.All(r => r.IsTicked));
            Assert.True(group.State);

            group.ToggleCommand.Execute(null);
            Assert.True(rows.All(r => !r.IsTicked));
            Assert.False(group.State);
        }

        [Fact]
        public void OverlappingGroupsStayInSync()
        {
            var rows = Rows(false, false, false);
            var all = new ZoneGroupViewModel("All", rows);
            var part = new ZoneGroupViewModel("Part", rows.Take(2).ToList());

            part.ToggleCommand.Execute(null);

            Assert.True(part.State);
            Assert.Null(all.State);
        }
    }
}
