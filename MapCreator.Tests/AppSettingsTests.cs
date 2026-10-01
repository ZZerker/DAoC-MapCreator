using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using MapCreator.Classes;
using Xunit;

namespace MapCreator.Tests
{
    public sealed class AppSettingsTests : IDisposable
    {
        private const string SAMPLE_USER_CONFIG = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
                <userSettings>
                    <MapCreator.Properties.Settings>
                        <setting name="mapRiverColor" serializeAs="String">
                            <value>20, 80, 170</value>
                        </setting>
                        <setting name="mapBoundsColor" serializeAs="String">
                            <value>0, 0, 0</value>
                        </setting>
                        <setting name="mapWidth" serializeAs="String">
                            <value>2048</value>
                        </setting>
                        <setting name="mapHeightmapZScale" serializeAs="String">
                            <value>1.5</value>
                        </setting>
                        <setting name="lastCreatedMaps" serializeAs="String">
                            <value>163,171</value>
                        </setting>
                        <setting name="mapQuality" serializeAs="String">
                            <value>not a number</value>
                        </setting>
                        <setting name="mapDrawTrees" serializeAs="String">
                            <value>False</value>
                        </setting>
                    </MapCreator.Properties.Settings>
                </userSettings>
            </configuration>
            """;

        private readonly string directory = Path.Combine(Path.GetTempPath(), "MapCreatorTests", Guid.NewGuid().ToString("N"));

        private string SettingsPath => Path.Combine(this.directory, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(this.directory))
            {
                Directory.Delete(this.directory, true);
            }
        }

        [Fact]
        public void SaveThenLoadKeepsEveryProperty()
        {
            var saved = new AppSettings
            {
                GamePath = "C:\\Game",
                TargetPath = "D:\\Maps",
                TargetDirectoryPattern = "out",
                MapFilePattern = "z{id}",
                MapType = "PNG",
                MapQuality = 77,
                MapSize = 4096,
                RenderParallel = 8,
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
                LabelSize = 1024,
                TickedZones = new List<string> { "163", "171" }
            };

            saved.Save(this.SettingsPath);
            var loaded = AppSettings.Load(this.SettingsPath, null);

            Assert.Equal("C:\\Game", loaded.GamePath);
            Assert.Equal("D:\\Maps", loaded.TargetPath);
            Assert.Equal("out", loaded.TargetDirectoryPattern);
            Assert.Equal("z{id}", loaded.MapFilePattern);
            Assert.Equal("PNG", loaded.MapType);
            Assert.Equal(77, loaded.MapQuality);
            Assert.Equal(4096, loaded.MapSize);
            Assert.Equal(8, loaded.RenderParallel);
            Assert.True(loaded.SkipIfFileExists);
            Assert.False(loaded.DrawBackground);
            Assert.False(loaded.Lightmap);
            Assert.Equal(12.5, loaded.LightmapZScale);
            Assert.Equal(0.25, loaded.LightmapMinLight);
            Assert.Equal(2.75, loaded.LightmapMaxLight);
            Assert.Equal(new double[] { 0.5, -2, 3.25 }, loaded.LightmapZVector);
            Assert.False(loaded.Rivers);
            Assert.True(loaded.RiversUseDefaultColor);
            Assert.Equal(Color.FromArgb(20, 80, 170).ToArgb(), loaded.RiversColor.ToArgb());
            Assert.Equal(33, loaded.RiverOpacity);
            Assert.False(loaded.DepthShadedWater);
            Assert.False(loaded.Bounds);
            Assert.Equal(Color.FromArgb(1, 2, 3).ToArgb(), loaded.BoundsColor.ToArgb());
            Assert.Equal(44, loaded.BoundsOpacity);
            Assert.True(loaded.ExcludeBoundsFromMap);
            Assert.False(loaded.DrawFixtures);
            Assert.False(loaded.DrawFixturesBelowWater);
            Assert.False(loaded.DrawKeeps);
            Assert.False(loaded.DrawTrees);
            Assert.Equal(55, loaded.TreeTransparency);
            Assert.Equal("labeled", loaded.LabelDirectory);
            Assert.Equal(1024, loaded.LabelSize);
            Assert.Equal(new[] { "163", "171" }, loaded.TickedZones);
            Assert.False(File.Exists(this.SettingsPath + ".tmp"));
        }

        [Fact]
        public void ColorsAreWrittenAsHexStrings()
        {
            var settings = new AppSettings { RiversColor = Color.FromArgb(20, 80, 170) };

            settings.Save(this.SettingsPath);
            var json = File.ReadAllText(this.SettingsPath);

            Assert.Contains("\"riversColor\": \"#1450AA\"", json);
            Assert.Contains("\"boundsColor\": \"#000000\"", json);
        }

        [Fact]
        public void MissingFileGivesDefaultsAndCreatesTheFile()
        {
            var loaded = AppSettings.Load(this.SettingsPath, null);

            AssertDefaults(loaded);
            Assert.True(File.Exists(this.SettingsPath));
        }

        [Fact]
        public void CorruptJsonGivesDefaultsAndKeepsABadCopy()
        {
            Directory.CreateDirectory(this.directory);
            File.WriteAllText(this.SettingsPath, "{ \"mapSize\": 4096, broken");

            var loaded = AppSettings.Load(this.SettingsPath, null);

            AssertDefaults(loaded);
            Assert.Equal("{ \"mapSize\": 4096, broken", File.ReadAllText(this.SettingsPath + ".bad"));
            Assert.Equal(2048, AppSettings.Load(this.SettingsPath, null).MapSize);
        }

        [Fact]
        public void MissingJsonPropertiesKeepTheirDefaults()
        {
            Directory.CreateDirectory(this.directory);
            File.WriteAllText(this.SettingsPath, "{ \"mapSize\": 4096, \"gamePath\": \"C:\\\\Game\" }");

            var loaded = AppSettings.Load(this.SettingsPath, null);

            Assert.Equal(4096, loaded.MapSize);
            Assert.Equal("C:\\Game", loaded.GamePath);
            Assert.Equal("JPEG", loaded.MapType);
            Assert.Equal(8, loaded.RenderParallel);
            Assert.Equal(new double[] { 1, 1, -1 }, loaded.LightmapZVector);
            Assert.Equal(Color.FromArgb(0, 64, 128).ToArgb(), loaded.RiversColor.ToArgb());
            Assert.Equal(new[] { "000" }, loaded.TickedZones);
            Assert.Equal("", loaded.LabelDirectory);
            Assert.Equal(0, loaded.LabelSize);
        }

        [Fact]
        public void InvalidJsonColorFallsBackToTheDefault()
        {
            Directory.CreateDirectory(this.directory);
            File.WriteAllText(this.SettingsPath, "{ \"riversColor\": \"blue-ish\", \"boundsColor\": \"#GG0000\", \"mapSize\": 1024 }");

            var loaded = AppSettings.Load(this.SettingsPath, null);

            Assert.Equal(Color.FromArgb(0, 64, 128).ToArgb(), loaded.RiversColor.ToArgb());
            Assert.Equal(Color.FromArgb(0, 0, 0).ToArgb(), loaded.BoundsColor.ToArgb());
            Assert.Equal(1024, loaded.MapSize);
        }

        [Fact]
        public void ImportUserConfigReadsTheOldSettings()
        {
            Directory.CreateDirectory(this.directory);
            var userConfig = Path.Combine(this.directory, "user.config");
            File.WriteAllText(userConfig, SAMPLE_USER_CONFIG);

            var imported = AppSettings.ImportUserConfig(userConfig);

            Assert.Equal(Color.FromArgb(20, 80, 170).ToArgb(), imported.RiversColor.ToArgb());
            Assert.Equal(Color.FromArgb(0, 0, 0).ToArgb(), imported.BoundsColor.ToArgb());
            Assert.Equal(2048, imported.MapSize);
            Assert.Equal(1.5, imported.LightmapZScale);
            Assert.Equal(new[] { "163", "171" }, imported.TickedZones);
            Assert.False(imported.DrawTrees);
            Assert.Equal(90, imported.MapQuality);
            Assert.Equal("JPEG", imported.MapType);
        }

        [Fact]
        public void MissingFileImportsTheOldUserConfig()
        {
            Directory.CreateDirectory(this.directory);
            var userConfig = Path.Combine(this.directory, "user.config");
            File.WriteAllText(userConfig, SAMPLE_USER_CONFIG);

            var loaded = AppSettings.Load(this.SettingsPath, userConfig);

            Assert.Equal(Color.FromArgb(20, 80, 170).ToArgb(), loaded.RiversColor.ToArgb());
            Assert.Equal(new[] { "163", "171" }, AppSettings.Load(this.SettingsPath, null).TickedZones);
        }

        private static void AssertDefaults(AppSettings settings)
        {
            var defaults = new AppSettings();
            Assert.Equal(defaults.GamePath, settings.GamePath);
            Assert.Equal(defaults.MapType, settings.MapType);
            Assert.Equal(defaults.MapSize, settings.MapSize);
            Assert.Equal(defaults.LightmapZScale, settings.LightmapZScale);
            Assert.Equal(defaults.LightmapZVector, settings.LightmapZVector);
            Assert.Equal(defaults.RiversColor.ToArgb(), settings.RiversColor.ToArgb());
            Assert.Equal(defaults.BoundsColor.ToArgb(), settings.BoundsColor.ToArgb());
            Assert.Equal(defaults.TickedZones, settings.TickedZones);
        }
    }
}
