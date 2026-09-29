using System;
using MapCreator.Classes;
using MapCreator.Ui.ViewModels;
using Xunit;

namespace MapCreator.Tests
{
    public class LogEntryTests
    {
        private static LogEntry Line(LogLevel level, string text)
        {
            return new LogEntry(DateTime.Now, level, text, LogEntry.ParseZoneId(text));
        }

        [Theory]
        [InlineData("[163] Rendering background ...", "163")]
        [InlineData("[z] x", "z")]
        [InlineData("Rendering 3 zones", null)]
        [InlineData("[] empty", null)]
        [InlineData("[163]no space", null)]
        public void ZoneIdComesFromThePrefix(string text, string expected)
        {
            Assert.Equal(expected, LogEntry.ParseZoneId(text));
        }

        [Fact]
        public void LevelFilterKeepsTheLevelAndAbove()
        {
            var normal = Line(LogLevel.Normal, "a");
            var success = Line(LogLevel.Success, "b");
            var warning = Line(LogLevel.Warning, "c");
            var error = Line(LogLevel.Error, "d");

            Assert.True(normal.Matches(LogLevelFilter.All, null));
            Assert.False(normal.Matches(LogLevelFilter.Notices, null));
            Assert.True(success.Matches(LogLevelFilter.Notices, null));
            Assert.False(success.Matches(LogLevelFilter.Warnings, null));
            Assert.True(warning.Matches(LogLevelFilter.Warnings, null));
            Assert.False(warning.Matches(LogLevelFilter.Errors, null));
            Assert.True(error.Matches(LogLevelFilter.Errors, null));
        }

        [Fact]
        public void ZoneFilterKeepsOnlyThatZone()
        {
            var zoneLine = Line(LogLevel.Normal, "[163] step");
            var otherLine = Line(LogLevel.Normal, "[171] step");
            var generalLine = Line(LogLevel.Normal, "Rendering 3 zones");

            Assert.True(zoneLine.Matches(LogLevelFilter.All, "163"));
            Assert.False(otherLine.Matches(LogLevelFilter.All, "163"));
            Assert.False(generalLine.Matches(LogLevelFilter.All, "163"));
            Assert.True(generalLine.Matches(LogLevelFilter.All, null));
        }

        [Theory]
        [InlineData("#1450AA", true)]
        [InlineData("1450aa", true)]
        [InlineData(" #000000 ", true)]
        [InlineData("#12345", false)]
        [InlineData("blue", false)]
        [InlineData("", false)]
        public void ColorTextNeedsSixHexDigits(string text, bool valid)
        {
            Assert.Equal(valid, OptionsViewModel.TryParseColor(text, out var color));
        }
    }
}
