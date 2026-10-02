using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MapCreator.Classes;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class PresetsTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "MapCreatorTests", Guid.NewGuid().ToString("N"));

        private string PresetsPath => Path.Combine(this.directory, "presets.json");

        public void Dispose()
        {
            if (Directory.Exists(this.directory))
            {
                Directory.Delete(this.directory, true);
            }
        }

        [Fact]
        public void SaveThenLoadKeepsTwoPresetsWithEveryOption()
        {
            var first = Custom();
            var second = new AppSettings { MapType = "PNG", MapSize = 512, LightmapZVector = new double[] { 9, 8, 7 }, BoundsColor = Color.FromArgb(9, 8, 7) };
            var store = PresetStore.Load(this.PresetsPath);
            Assert.Null(store.Add("Alpha", first));
            Assert.Null(store.Add("Beta", second));
            store.Save();

            var loaded = PresetStore.Load(this.PresetsPath);

            Assert.Equal(new[] { "Alpha", "Beta" }, loaded.Presets.Select(p => p.Name).ToArray());
            var target = new AppSettings();
            loaded.Find("Alpha").ApplyTo(target);
            AssertSameOptions(first, target);
            Assert.True(loaded.Find("alpha").HasSameOptions(first));
            Assert.False(loaded.Find("Beta").HasSameOptions(first));
            var other = new AppSettings();
            loaded.Find("Beta").ApplyTo(other);
            AssertSameOptions(second, other);
            Assert.False(File.Exists(this.PresetsPath + ".tmp"));
        }

        [Fact]
        public void ApplyLeavesMachineStateUntouched()
        {
            var preset = new Preset { Name = "p" };
            preset.CopyFrom(Custom());
            var target = new AppSettings { GamePath = "C:\\Game", TargetPath = "D:\\Maps", RenderParallel = 7, TickedZones = new List<string> { "163" } };

            preset.ApplyTo(target);

            Assert.Equal("C:\\Game", target.GamePath);
            Assert.Equal("D:\\Maps", target.TargetPath);
            Assert.Equal(7, target.RenderParallel);
            Assert.Equal(new[] { "163" }, target.TickedZones);
            Assert.Equal(4096, target.MapSize);
        }

        [Fact]
        public void MissingFileGivesNoPresetsAndCreatesNothing()
        {
            var store = PresetStore.Load(this.PresetsPath);

            Assert.Empty(store.Presets);
            Assert.False(File.Exists(this.PresetsPath));
        }

        [Fact]
        public void BrokenFileGivesNoPresetsAndKeepsABadCopy()
        {
            Directory.CreateDirectory(this.directory);
            File.WriteAllText(this.PresetsPath, "[ { \"name\": \"x\", broken");

            var store = PresetStore.Load(this.PresetsPath);

            Assert.Empty(store.Presets);
            Assert.Equal("[ { \"name\": \"x\", broken", File.ReadAllText(this.PresetsPath + ".bad"));
        }

        [Fact]
        public void InvalidValuesAreSanitized()
        {
            Directory.CreateDirectory(this.directory);
            File.WriteAllText(this.PresetsPath, """
                [
                  { "name": " Odd ", "mapType": null, "mapFilePattern": null, "labelDirectory": null, "targetDirectoryPattern": null,
                    "riversColor": "blue-ish", "boundsColor": "#GG0000", "lightmapZVector": [1, 2], "mapSize": 1024 },
                  { "name": null },
                  { "name": "  " },
                  { "name": "ODD" },
                  null
                ]
                """);

            var store = PresetStore.Load(this.PresetsPath);
            var target = new AppSettings { MapType = "PNG" };
            store.Find("odd").ApplyTo(target);

            Assert.Equal(new[] { "Odd" }, store.Presets.Select(p => p.Name).ToArray());
            var defaults = new AppSettings();
            Assert.Equal(defaults.MapType, target.MapType);
            Assert.Equal(defaults.MapFilePattern, target.MapFilePattern);
            Assert.Equal(defaults.TargetDirectoryPattern, target.TargetDirectoryPattern);
            Assert.Equal("", target.LabelDirectory);
            Assert.Equal(defaults.RiversColor.ToArgb(), target.RiversColor.ToArgb());
            Assert.Equal(defaults.BoundsColor.ToArgb(), target.BoundsColor.ToArgb());
            Assert.Equal(defaults.LightmapZVector, target.LightmapZVector);
            Assert.Equal(1024, target.MapSize);
        }

        [Fact]
        public void NamesAreUniqueIgnoringCaseAndNotEmpty()
        {
            var store = PresetStore.Load(this.PresetsPath);
            var settings = new AppSettings();
            Assert.Null(store.Add("  Alpha ", settings));
            Assert.Null(store.Add("Beta", settings));

            Assert.NotNull(store.Add("ALPHA", settings));
            Assert.NotNull(store.Add("   ", settings));
            Assert.NotNull(store.Rename("Beta", "alpha"));
            Assert.NotNull(store.Rename("Beta", ""));
            Assert.Null(store.Rename("Beta", "ALPHA2"));
            Assert.Null(store.Rename("Alpha", "aLPHA"));

            Assert.Equal(new[] { "aLPHA", "ALPHA2" }, store.Presets.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void DeleteRemovesThePreset()
        {
            var store = PresetStore.Load(this.PresetsPath);
            store.Add("Alpha", new AppSettings());
            store.Add("Beta", new AppSettings());

            store.Delete("alpha");

            Assert.Equal(new[] { "Beta" }, store.Presets.Select(p => p.Name).ToArray());
            Assert.Null(store.Find("Alpha"));
        }

        [Fact]
        public void OverwriteReplacesTheOptions()
        {
            var store = PresetStore.Load(this.PresetsPath);
            store.Add("Alpha", new AppSettings());
            var changed = Custom();

            store.Overwrite("Alpha", changed);

            Assert.True(store.Find("Alpha").HasSameOptions(changed));
        }

        [Fact]
        public void ActivePresetSurvivesSaveAndLoad()
        {
            var settingsPath = Path.Combine(this.directory, "settings.json");
            new AppSettings { ActivePreset = "Alpha" }.Save(settingsPath);
            Assert.Equal("Alpha", AppSettings.Load(settingsPath, null).ActivePreset);

            File.WriteAllText(settingsPath, "{ \"activePreset\": null }");
            Assert.Equal("", AppSettings.Load(settingsPath, null).ActivePreset);
        }

        [Fact]
        public void LockedFileIsNotOverwrittenAndNoBadCopyIsKept()
        {
            var seed = PresetStore.Load(this.PresetsPath);
            seed.Add("Alpha", new AppSettings());
            seed.Save();
            var bytes = File.ReadAllBytes(this.PresetsPath);

            PresetStore store;
            using (new FileStream(this.PresetsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                store = PresetStore.Load(this.PresetsPath);
            }

            Assert.Empty(store.Presets);
            store.Add("Beta", new AppSettings());
            Assert.Throws<InvalidOperationException>(() => store.Save());
            Assert.Equal(bytes, File.ReadAllBytes(this.PresetsPath));
            Assert.False(File.Exists(this.PresetsPath + ".bad"));
        }

        [Fact]
        public void PresetHasEveryOptionOfTheSettings()
        {
            var machineState = new[] { "GamePath", "TargetPath", "RenderParallel", "TickedZones", "ActivePreset", "ViewedFolder" };
            var settingsNames = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).Where(n => !machineState.Contains(n)).OrderBy(n => n).ToArray();
            var presetNames = typeof(Preset).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).Where(n => n != "Name").OrderBy(n => n).ToArray();

            Assert.Equal(settingsNames, presetNames);
        }

        [Fact]
        public void EveryPresetOptionReachesTheSettingsAndBack()
        {
            var preset = new Preset { Name = "p" };
            foreach (var property in typeof(Preset).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "Name"))
            {
                var current = property.GetValue(preset);
                object changed = current switch
                {
                    bool b => !b,
                    int i => i + 7,
                    double d => d + 1.5,
                    string t => t + "x",
                    double[] v => new[] { v[0] + 1, v[1] + 2, v[2] + 3 },
                    Color c => Color.FromArgb(255 - c.R, 255 - c.G, 255 - c.B),
                    _ => throw new InvalidOperationException("Unhandled type of " + property.Name)
                };
                property.SetValue(preset, changed);
            }

            var settings = new AppSettings();
            preset.ApplyTo(settings);
            var copy = new Preset { Name = "p" };
            copy.CopyFrom(settings);

            Assert.Equal(JsonSerializer.Serialize(preset, AppSettings.JsonOptions), JsonSerializer.Serialize(copy, AppSettings.JsonOptions));
            Assert.NotEqual(JsonSerializer.Serialize(new Preset { Name = "p" }, AppSettings.JsonOptions), JsonSerializer.Serialize(copy, AppSettings.JsonOptions));
        }

        private static AppSettings Custom()
        {
            return new AppSettings
            {
                TargetDirectoryPattern = "out",
                MapFilePattern = "z{id}",
                MapType = "PNG",
                MapQuality = 77,
                MapSize = 4096,
                SkipIfFileExists = true,
                DrawBackground = false,
                Lightmap = false,
                LightmapZScale = 12.5,
                LightmapMinLight = 0.25,
                LightmapMaxLight = 2.75,
                LightmapZVector = new double[] { 0.5, -2, 3.25 },
                Rivers = false,
                RiversUseDefaultColor = true,
                RiversColor = Color.FromArgb(20, 80, 170),
                RiverOpacity = 33,
                DepthShadedWater = false,
                Bounds = false,
                BoundsColor = Color.FromArgb(1, 2, 3),
                BoundsOpacity = 44,
                ExcludeBoundsFromMap = true,
                DrawFixtures = false,
                DrawFixturesBelowWater = false,
                DrawKeeps = false,
                DrawTrees = false,
                TreeTransparency = 55,
                LabelDirectory = "labeled",
                LabelSize = 1024
            };
        }

        private static void AssertSameOptions(AppSettings expected, AppSettings actual)
        {
            Assert.Equal(expected.TargetDirectoryPattern, actual.TargetDirectoryPattern);
            Assert.Equal(expected.MapFilePattern, actual.MapFilePattern);
            Assert.Equal(expected.MapType, actual.MapType);
            Assert.Equal(expected.MapQuality, actual.MapQuality);
            Assert.Equal(expected.MapSize, actual.MapSize);
            Assert.Equal(expected.SkipIfFileExists, actual.SkipIfFileExists);
            Assert.Equal(expected.DrawBackground, actual.DrawBackground);
            Assert.Equal(expected.Lightmap, actual.Lightmap);
            Assert.Equal(expected.LightmapZScale, actual.LightmapZScale);
            Assert.Equal(expected.LightmapMinLight, actual.LightmapMinLight);
            Assert.Equal(expected.LightmapMaxLight, actual.LightmapMaxLight);
            Assert.Equal(expected.LightmapZVector, actual.LightmapZVector);
            Assert.Equal(expected.Rivers, actual.Rivers);
            Assert.Equal(expected.RiversUseDefaultColor, actual.RiversUseDefaultColor);
            Assert.Equal(expected.RiversColor.ToArgb(), actual.RiversColor.ToArgb());
            Assert.Equal(expected.RiverOpacity, actual.RiverOpacity);
            Assert.Equal(expected.DepthShadedWater, actual.DepthShadedWater);
            Assert.Equal(expected.Bounds, actual.Bounds);
            Assert.Equal(expected.BoundsColor.ToArgb(), actual.BoundsColor.ToArgb());
            Assert.Equal(expected.BoundsOpacity, actual.BoundsOpacity);
            Assert.Equal(expected.ExcludeBoundsFromMap, actual.ExcludeBoundsFromMap);
            Assert.Equal(expected.DrawFixtures, actual.DrawFixtures);
            Assert.Equal(expected.DrawFixturesBelowWater, actual.DrawFixturesBelowWater);
            Assert.Equal(expected.DrawKeeps, actual.DrawKeeps);
            Assert.Equal(expected.DrawTrees, actual.DrawTrees);
            Assert.Equal(expected.TreeTransparency, actual.TreeTransparency);
            Assert.Equal(expected.LabelDirectory, actual.LabelDirectory);
            Assert.Equal(expected.LabelSize, actual.LabelSize);
        }
    }
}
