using System.Drawing;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Everything a zone render needs, captured from the main window on the UI thread or read from the saved settings (batch mode)
    /// </summary>
    internal sealed record RenderSettings
    {
        /// <summary>
        /// The options the window last saved
        /// </summary>
        public static RenderSettings FromSaved(Properties.Settings saved)
        {
            return new RenderSettings
            {
                MapSize = (int)saved.mapWidth,
                Parallel = (int)saved.renderParallel,
                TargetPath = !string.IsNullOrEmpty(saved.targetMapPath) ? saved.targetMapPath : System.Windows.Forms.Application.StartupPath,
                DirectoryPattern = saved.targetDirectoryPattern,
                FilePattern = saved.mapFilePattern,
                FileType = saved.mapType,
                Quality = (uint)saved.mapQuality,
                SkipIfFileExists = saved.skipIfFileExists,
                DrawBackground = saved.mapGenerateBackground,
                Lightmap = saved.mapGenerateHeightmap,
                LightmapZScale = (double)saved.mapHeightmapZScale,
                LightmapLightMin = (double)saved.mapHeightmapMinLight,
                LightmapLightMax = (double)saved.mapHeightmapMaxLight,
                LightmapZVector = new[] { (double)saved.mapHeightmapZVector1, (double)saved.mapHeightmapZVector2, (double)saved.mapHeightmapZVector3 },
                Rivers = saved.mapGenerateRivers,
                RiversUseDefaultColor = saved.mapRiverColorUseDefault,
                RiversColor = saved.mapRiverColor,
                RiverOpacity = (int)saved.mapRiverOpacity,
                DepthShadedWater = saved.mapDepthShadedWater,
                Bounds = saved.mapGenerateBounds,
                BoundsColor = saved.mapBoundsColor,
                BoundsOpacity = (int)saved.mapBoundsOpacity,
                ExcludeBoundsFromMap = saved.removeBoundsFromMap,
                DrawFixtures = saved.mapDrawBuildings,
                DrawFixturesBelowWater = saved.mapDrawBuildingsBelowWater,
                DrawKeeps = saved.mapDrawKeeps,
                DrawTrees = saved.mapDrawTrees,
                TreeTransparency = (int)saved.mapTreeTransparency
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

        // Only write the zNNN.labels.json files, no images
        public bool LabelsOnly { get; init; }

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
        public bool DrawTrees { get; init; }
        public int TreeTransparency { get; init; }
    }
}
