using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace MapCreator.Classes
{
    /// <summary>
    /// User settings, stored as JSON in %LOCALAPPDATA%\MapCreator\settings.json
    /// </summary>
    internal sealed class AppSettings
    {
        public static string DefaultPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapCreator", "settings.json");

        private static readonly Lazy<AppSettings> Loaded = new(() => Load(DefaultPath));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new HexColorConverter() }
        };

        public static AppSettings Current => Loaded.Value;

        public string GamePath { get; set; } = "";

        // Output
        public string TargetPath { get; set; } = "";
        public string TargetDirectoryPattern { get; set; } = "maps";
        public string MapFilePattern { get; set; } = "zone{id}_{size}";
        public string MapType { get; set; } = "JPEG";
        public int MapQuality { get; set; } = 90;
        public int MapSize { get; set; } = 2048;
        public int RenderParallel { get; set; } = 4;
        public bool SkipIfFileExists { get; set; }

        public bool DrawBackground { get; set; } = true;

        // Lightmap
        public bool Lightmap { get; set; } = true;
        public double LightmapZScale { get; set; } = 35;
        public double LightmapMinLight { get; set; } = 0.5;
        public double LightmapMaxLight { get; set; } = 1.5;
        public double[] LightmapZVector { get; set; } = new double[] { 1, 1, -1 };

        // Water
        public bool Rivers { get; set; } = true;
        public bool RiversUseDefaultColor { get; set; }
        public Color RiversColor { get; set; } = Color.FromArgb(0, 64, 128);
        public int RiverOpacity { get; set; } = 50;
        public bool DepthShadedWater { get; set; } = true;

        // Bounds
        public bool Bounds { get; set; } = true;
        public Color BoundsColor { get; set; } = Color.FromArgb(0, 0, 0);
        public int BoundsOpacity { get; set; } = 65;
        public bool ExcludeBoundsFromMap { get; set; }

        // Fixtures and trees
        public bool DrawFixtures { get; set; } = true;
        public bool DrawFixturesBelowWater { get; set; } = true;
        public bool DrawKeeps { get; set; } = true;
        public bool DrawTrees { get; set; } = true;
        public int TreeTransparency { get; set; } = 25;

        // Labeled copies of the maps go into this directory under the target path ("" draws none), scaled to LabelSize (0 keeps the map size)
        public string LabelDirectory { get; set; } = "";
        public int LabelSize { get; set; }

        public List<string> TickedZones { get; set; } = new() { "000" };

        public static AppSettings Load(string path)
        {
            return Load(path, FindOldUserConfig());
        }

        /// <summary>
        /// Loads the settings file; a missing or broken file is replaced by the imported old user.config (null: defaults)
        /// </summary>
        public static AppSettings Load(string path, string userConfigPath)
        {
            if (File.Exists(path))
            {
                string error;
                try
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                    if (loaded != null)
                    {
                        loaded.FillInvalid();
                        return loaded;
                    }

                    error = "empty";
                }
                catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    error = ex.Message;
                }

                AppLog.Log(string.Format("Settings file {0} is unreadable, using defaults ({1})", path, error), LogLevel.Warning);
                KeepBadCopy(path);
            }

            var settings = new AppSettings();
            if (userConfigPath != null)
            {
                try
                {
                    settings = ImportUserConfig(userConfigPath);
                }
                catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppLog.Log(string.Format("Old settings {0} not imported ({1})", userConfigPath, ex.Message), LogLevel.Warning);
                }
            }

            try
            {
                settings.Save(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLog.Log(string.Format("Settings file {0} not written ({1})", path, ex.Message), LogLevel.Warning);
            }

            return settings;
        }

        public void Save()
        {
            this.Save(DefaultPath);
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tempPath, path, true);
        }

        /// <summary>
        /// Reads the settings of the old WinForms user.config
        /// </summary>
        public static AppSettings ImportUserConfig(string userConfigPath)
        {
            var values = new Dictionary<string, string>();
            foreach (var setting in XDocument.Load(userConfigPath).Descendants("MapCreator.Properties.Settings").Elements("setting"))
            {
                var name = (string)setting.Attribute("name");
                var value = setting.Element("value");
                if (name != null && value != null)
                {
                    values[name] = value.Value;
                }
            }

            var settings = new AppSettings();
            settings.GamePath = ReadString(values, "game_path", settings.GamePath);
            settings.TargetPath = ReadString(values, "targetMapPath", settings.TargetPath);
            settings.TargetDirectoryPattern = ReadString(values, "targetDirectoryPattern", settings.TargetDirectoryPattern);
            settings.MapFilePattern = ReadString(values, "mapFilePattern", settings.MapFilePattern);
            settings.MapType = ReadString(values, "mapType", settings.MapType);
            settings.MapQuality = ReadInt(values, "mapQuality", settings.MapQuality);
            settings.MapSize = ReadInt(values, "mapWidth", settings.MapSize);
            settings.RenderParallel = ReadInt(values, "renderParallel", settings.RenderParallel);
            settings.SkipIfFileExists = ReadBool(values, "skipIfFileExists", settings.SkipIfFileExists);
            settings.DrawBackground = ReadBool(values, "mapGenerateBackground", settings.DrawBackground);
            settings.Lightmap = ReadBool(values, "mapGenerateHeightmap", settings.Lightmap);
            settings.LightmapZScale = ReadDouble(values, "mapHeightmapZScale", settings.LightmapZScale);
            settings.LightmapMinLight = ReadDouble(values, "mapHeightmapMinLight", settings.LightmapMinLight);
            settings.LightmapMaxLight = ReadDouble(values, "mapHeightmapMaxLight", settings.LightmapMaxLight);
            settings.LightmapZVector = new[]
            {
                ReadDouble(values, "mapHeightmapZVector1", settings.LightmapZVector[0]),
                ReadDouble(values, "mapHeightmapZVector2", settings.LightmapZVector[1]),
                ReadDouble(values, "mapHeightmapZVector3", settings.LightmapZVector[2])
            };
            settings.Rivers = ReadBool(values, "mapGenerateRivers", settings.Rivers);
            settings.RiversUseDefaultColor = ReadBool(values, "mapRiverColorUseDefault", settings.RiversUseDefaultColor);
            settings.RiversColor = ReadColor(values, "mapRiverColor", settings.RiversColor);
            settings.RiverOpacity = ReadInt(values, "mapRiverOpacity", settings.RiverOpacity);
            settings.DepthShadedWater = ReadBool(values, "mapDepthShadedWater", settings.DepthShadedWater);
            settings.Bounds = ReadBool(values, "mapGenerateBounds", settings.Bounds);
            settings.BoundsColor = ReadColor(values, "mapBoundsColor", settings.BoundsColor);
            settings.BoundsOpacity = ReadInt(values, "mapBoundsOpacity", settings.BoundsOpacity);
            settings.ExcludeBoundsFromMap = ReadBool(values, "removeBoundsFromMap", settings.ExcludeBoundsFromMap);
            settings.DrawFixtures = ReadBool(values, "mapDrawBuildings", settings.DrawFixtures);
            settings.DrawFixturesBelowWater = ReadBool(values, "mapDrawBuildingsBelowWater", settings.DrawFixturesBelowWater);
            settings.DrawKeeps = ReadBool(values, "mapDrawKeeps", settings.DrawKeeps);
            settings.DrawTrees = ReadBool(values, "mapDrawTrees", settings.DrawTrees);
            settings.TreeTransparency = ReadInt(values, "mapTreeTransparency", settings.TreeTransparency);
            if (values.TryGetValue("lastCreatedMaps", out var ticked))
            {
                settings.TickedZones = ticked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            return settings;
        }

        /// <summary>
        /// The newest %LOCALAPPDATA%\MapCreator\*\*\user.config, or null
        /// </summary>
        public static string FindOldUserConfig()
        {
            var root = Path.GetDirectoryName(DefaultPath);
            try
            {
                if (!Directory.Exists(root))
                {
                    return null;
                }

                return Directory.EnumerateDirectories(root)
                    .SelectMany(d => Directory.EnumerateDirectories(d))
                    .Select(d => Path.Combine(d, "user.config"))
                    .Where(f => File.Exists(f))
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void KeepBadCopy(string path)
        {
            try
            {
                File.Copy(path, path + ".bad", true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLog.Log(string.Format("Settings file {0} not backed up ({1})", path, ex.Message), LogLevel.Warning);
            }
        }

        // JSON null, invalid colors and a wrong vector length fall back to the defaults
        private void FillInvalid()
        {
            var defaults = new AppSettings();
            this.GamePath ??= defaults.GamePath;
            this.TargetPath ??= defaults.TargetPath;
            this.TargetDirectoryPattern ??= defaults.TargetDirectoryPattern;
            this.MapFilePattern ??= defaults.MapFilePattern;
            this.MapType ??= defaults.MapType;
            this.LabelDirectory ??= defaults.LabelDirectory;
            this.TickedZones ??= defaults.TickedZones;
            if (this.LightmapZVector == null || this.LightmapZVector.Length != 3)
            {
                this.LightmapZVector = defaults.LightmapZVector;
            }

            if (this.RiversColor.IsEmpty)
            {
                this.RiversColor = defaults.RiversColor;
            }

            if (this.BoundsColor.IsEmpty)
            {
                this.BoundsColor = defaults.BoundsColor;
            }
        }

        private static string ReadString(Dictionary<string, string> values, string name, string fallback)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback;
        }

        private static bool ReadBool(Dictionary<string, string> values, string name, bool fallback)
        {
            if (values.TryGetValue(name, out var value) && bool.TryParse(value, out var result))
            {
                return result;
            }

            return fallback;
        }

        private static int ReadInt(Dictionary<string, string> values, string name, int fallback)
        {
            if (values.TryGetValue(name, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
                && result >= int.MinValue && result <= int.MaxValue)
            {
                return (int)result;
            }

            return fallback;
        }

        private static double ReadDouble(Dictionary<string, string> values, string name, double fallback)
        {
            if (values.TryGetValue(name, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return fallback;
        }

        private static Color ReadColor(Dictionary<string, string> values, string name, Color fallback)
        {
            if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            try
            {
                if (new ColorConverter().ConvertFromInvariantString(value) is Color color && !color.IsEmpty)
                {
                    return Color.FromArgb(color.R, color.G, color.B);
                }

                return fallback;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is NotSupportedException)
            {
                return fallback;
            }
        }

        /// <summary>
        /// Writes colors as "#RRGGBB"; unreadable values become Color.Empty and are replaced by the default after loading
        /// </summary>
        internal static bool TryParseHexColor(string text, out Color color)
        {
            if (text != null && text.Length == 7 && text[0] == '#'
                && int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            {
                color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                return true;
            }

            color = Color.Empty;
            return false;
        }

        internal static string ToHexColor(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private sealed class HexColorConverter : JsonConverter<Color>
        {
            public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    reader.Skip();
                    return Color.Empty;
                }

                return TryParseHexColor(reader.GetString(), out var color) ? color : Color.Empty;
            }

            public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(ToHexColor(value));
            }
        }
    }
}
