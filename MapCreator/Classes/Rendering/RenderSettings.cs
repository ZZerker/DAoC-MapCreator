using System;
using System.Drawing;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Everything a zone render needs, captured from the main window on the UI thread or read from the saved settings (batch mode)
    /// </summary>
    internal sealed record RenderSettings
    {
        /// <summary>
        /// The saved options
        /// </summary>
        public static RenderSettings FromSettings(AppSettings settings)
        {
            return new RenderSettings
            {
                MapSize = settings.MapSize,
                Parallel = settings.RenderParallel,
                TargetPath = !string.IsNullOrEmpty(settings.TargetPath) ? settings.TargetPath : AppContext.BaseDirectory,
                DirectoryPattern = settings.TargetDirectoryPattern,
                FilePattern = settings.MapFilePattern,
                FileType = settings.MapType,
                Quality = (uint)settings.MapQuality,
                SkipIfFileExists = settings.SkipIfFileExists,
                DrawBackground = settings.DrawBackground,
                Lightmap = settings.Lightmap,
                LightmapZScale = settings.LightmapZScale,
                LightmapLightMin = settings.LightmapMinLight,
                LightmapLightMax = settings.LightmapMaxLight,
                LightmapZVector = (double[])settings.LightmapZVector.Clone(),
                Rivers = settings.Rivers,
                RiversUseDefaultColor = settings.RiversUseDefaultColor,
                RiversColor = settings.RiversColor,
                RiverOpacity = settings.RiverOpacity,
                DepthShadedWater = settings.DepthShadedWater,
                Bounds = settings.Bounds,
                BoundsColor = settings.BoundsColor,
                BoundsOpacity = settings.BoundsOpacity,
                ExcludeBoundsFromMap = settings.ExcludeBoundsFromMap,
                DrawFixtures = settings.DrawFixtures,
                DrawFixturesBelowWater = settings.DrawFixturesBelowWater,
                DrawKeeps = settings.DrawKeeps,
                ObliqueKeeps = settings.ObliqueKeeps,
                ObliqueBuildings = settings.ObliqueBuildings,
                ObliqueTrees = settings.ObliqueTrees,
                AmbientOcclusion = settings.AmbientOcclusion,
                DrawTrees = settings.DrawTrees,
                TreeTransparency = settings.TreeTransparency,
                LabelDirectory = string.IsNullOrEmpty(settings.LabelDirectory) ? null : settings.LabelDirectory,
                LabelSize = settings.LabelSize
            };
        }

        public int MapSize { get; init; }

        // Zones rendered at the same time
        public int Parallel { get; init; } = 1;

        // Output
        public string TargetPath { get; init; }
        public string DirectoryPattern { get; init; }
        public string FilePattern { get; init; }
        public string FileType { get; init; }
        public uint Quality { get; init; }
        public bool SkipIfFileExists { get; init; }

        // Only write the zNNN.labels.json files, no images (labeled copies of existing maps still get drawn)
        public bool LabelsOnly { get; init; }

        // Labeled copies of the maps go into this directory under the target path, scaled to LabelSize (0 keeps the map size); null draws none
        public string LabelDirectory { get; init; }
        public int LabelSize { get; init; }

        public bool DrawBackground { get; init; }

        // Lightmap
        public bool Lightmap { get; init; }
        public double LightmapZScale { get; init; }
        public double LightmapLightMin { get; init; }
        public double LightmapLightMax { get; init; }
        public double[] LightmapZVector { get; init; }

        // Water
        public bool Rivers { get; init; }
        public bool RiversUseDefaultColor { get; init; }
        public Color RiversColor { get; init; }
        public int RiverOpacity { get; init; }
        public bool DepthShadedWater { get; init; } = true;

        // Bounds
        public bool Bounds { get; init; }
        public Color BoundsColor { get; init; }
        public int BoundsOpacity { get; init; }
        public bool ExcludeBoundsFromMap { get; init; }

        // Fixtures and trees
        public bool DrawFixtures { get; init; }
        public bool DrawFixturesBelowWater { get; init; }
        public bool DrawKeeps { get; init; } = true;

        public bool ObliqueKeeps { get; init; }
        public bool AmbientOcclusion { get; init; }
        public bool ObliqueBuildings { get; init; }
        public bool ObliqueTrees { get; init; }
        public bool DrawTrees { get; init; }
        public int TreeTransparency { get; init; }
    }
}
