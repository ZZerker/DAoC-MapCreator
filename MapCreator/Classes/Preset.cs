using System.Drawing;
using System.Text.Json;

namespace MapCreator.Classes
{
    /// <summary>
    /// A named set of the look and output format options (no paths, thread count or zone selection)
    /// </summary>
    internal sealed class Preset
    {
        public string Name { get; set; } = "";

        public string TargetDirectoryPattern { get; set; }
        public string MapFilePattern { get; set; }
        public string MapType { get; set; }
        public int MapQuality { get; set; }
        public int MapSize { get; set; }
        public bool SkipIfFileExists { get; set; }
        public bool DrawBackground { get; set; }

        public bool Lightmap { get; set; }
        public double LightmapZScale { get; set; }
        public double LightmapMinLight { get; set; }
        public double LightmapMaxLight { get; set; }
        public double[] LightmapZVector { get; set; }

        public bool Rivers { get; set; }
        public bool RiversUseDefaultColor { get; set; }
        public Color RiversColor { get; set; }
        public int RiverOpacity { get; set; }
        public bool DepthShadedWater { get; set; }

        public bool Bounds { get; set; }
        public Color BoundsColor { get; set; }
        public int BoundsOpacity { get; set; }
        public bool ExcludeBoundsFromMap { get; set; }

        public bool DrawFixtures { get; set; }
        public bool DrawFixturesBelowWater { get; set; }
        public bool DrawKeeps { get; set; }

        public bool ObliqueKeeps { get; set; }
        public bool ObliqueBuildings { get; set; }
        public bool ObliqueTrees { get; set; }
        public bool AmbientOcclusion { get; set; }
        public bool DrawTrees { get; set; }
        public int TreeTransparency { get; set; }

        public string LabelDirectory { get; set; }
        public int LabelSize { get; set; }

        public Preset()
        {
            this.CopyFrom(new AppSettings());
        }

        public void CopyFrom(AppSettings settings)
        {
            this.TargetDirectoryPattern = settings.TargetDirectoryPattern;
            this.MapFilePattern = settings.MapFilePattern;
            this.MapType = settings.MapType;
            this.MapQuality = settings.MapQuality;
            this.MapSize = settings.MapSize;
            this.SkipIfFileExists = settings.SkipIfFileExists;
            this.DrawBackground = settings.DrawBackground;
            this.Lightmap = settings.Lightmap;
            this.LightmapZScale = settings.LightmapZScale;
            this.LightmapMinLight = settings.LightmapMinLight;
            this.LightmapMaxLight = settings.LightmapMaxLight;
            this.LightmapZVector = (double[])settings.LightmapZVector.Clone();
            this.Rivers = settings.Rivers;
            this.RiversUseDefaultColor = settings.RiversUseDefaultColor;
            this.RiversColor = settings.RiversColor;
            this.RiverOpacity = settings.RiverOpacity;
            this.DepthShadedWater = settings.DepthShadedWater;
            this.Bounds = settings.Bounds;
            this.BoundsColor = settings.BoundsColor;
            this.BoundsOpacity = settings.BoundsOpacity;
            this.ExcludeBoundsFromMap = settings.ExcludeBoundsFromMap;
            this.DrawFixtures = settings.DrawFixtures;
            this.DrawFixturesBelowWater = settings.DrawFixturesBelowWater;
            this.DrawKeeps = settings.DrawKeeps;
            this.ObliqueKeeps = settings.ObliqueKeeps;
            this.ObliqueBuildings = settings.ObliqueBuildings;
            this.ObliqueTrees = settings.ObliqueTrees;
            this.AmbientOcclusion = settings.AmbientOcclusion;
            this.DrawTrees = settings.DrawTrees;
            this.TreeTransparency = settings.TreeTransparency;
            this.LabelDirectory = settings.LabelDirectory;
            this.LabelSize = settings.LabelSize;
        }

        public void ApplyTo(AppSettings settings)
        {
            settings.TargetDirectoryPattern = this.TargetDirectoryPattern;
            settings.MapFilePattern = this.MapFilePattern;
            settings.MapType = this.MapType;
            settings.MapQuality = this.MapQuality;
            settings.MapSize = this.MapSize;
            settings.SkipIfFileExists = this.SkipIfFileExists;
            settings.DrawBackground = this.DrawBackground;
            settings.Lightmap = this.Lightmap;
            settings.LightmapZScale = this.LightmapZScale;
            settings.LightmapMinLight = this.LightmapMinLight;
            settings.LightmapMaxLight = this.LightmapMaxLight;
            settings.LightmapZVector = (double[])this.LightmapZVector.Clone();
            settings.Rivers = this.Rivers;
            settings.RiversUseDefaultColor = this.RiversUseDefaultColor;
            settings.RiversColor = this.RiversColor;
            settings.RiverOpacity = this.RiverOpacity;
            settings.DepthShadedWater = this.DepthShadedWater;
            settings.Bounds = this.Bounds;
            settings.BoundsColor = this.BoundsColor;
            settings.BoundsOpacity = this.BoundsOpacity;
            settings.ExcludeBoundsFromMap = this.ExcludeBoundsFromMap;
            settings.DrawFixtures = this.DrawFixtures;
            settings.DrawFixturesBelowWater = this.DrawFixturesBelowWater;
            settings.DrawKeeps = this.DrawKeeps;
            settings.ObliqueKeeps = this.ObliqueKeeps;
            settings.ObliqueBuildings = this.ObliqueBuildings;
            settings.ObliqueTrees = this.ObliqueTrees;
            settings.AmbientOcclusion = this.AmbientOcclusion;
            settings.DrawTrees = this.DrawTrees;
            settings.TreeTransparency = this.TreeTransparency;
            settings.LabelDirectory = this.LabelDirectory;
            settings.LabelSize = this.LabelSize;
        }

        public bool HasSameOptions(AppSettings settings)
        {
            var current = new Preset { Name = this.Name };
            current.CopyFrom(settings);
            return JsonSerializer.Serialize(current, AppSettings.JsonOptions) == JsonSerializer.Serialize(this, AppSettings.JsonOptions);
        }

        // JSON null, invalid colors and a wrong vector length fall back to the defaults
        public void FillInvalid()
        {
            var defaults = new Preset();
            this.TargetDirectoryPattern ??= defaults.TargetDirectoryPattern;
            this.MapFilePattern ??= defaults.MapFilePattern;
            this.MapType ??= defaults.MapType;
            this.LabelDirectory ??= defaults.LabelDirectory;
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
    }
}
