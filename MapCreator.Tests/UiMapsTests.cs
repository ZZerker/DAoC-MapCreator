using System.Collections.Generic;
using MapCreator.Classes;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class UiMapsTests
    {
        [Fact]
        public void RewriteReplacesOnlyTheGivenSectionsAndKeepsComments()
        {
            var text = "; header\r\n[region010]\r\nzone_count=1\r\n; Camelot\r\nzone0_offsetx=21900\r\n[region020]\r\nzone_count=1\r\nzone0_offsetx=5\r\n";
            var sections = new Dictionary<string, List<string>> { { "region010", new List<string> { "zone_count=1", "zone0_offsetx=100" } } };

            var result = UiMaps.RewriteSections(text, sections);

            Assert.Equal("; header\r\n[region010]\r\nzone_count=1\r\nzone0_offsetx=100\r\n; Camelot\r\n[region020]\r\nzone_count=1\r\nzone0_offsetx=5\r\n", result);
        }
    }
}
