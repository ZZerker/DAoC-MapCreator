using System.Drawing;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Everything a zone render needs from the main window, captured on the UI thread
    /// </summary>
    internal sealed record RenderSettings
    {
        public int MapSize { get; init; }

        // Output
        public string TargetPath { get; init; }
        public string DirectoryPattern { get; init; }
        public string FilePattern { get; init; }
        public string FileType { get; init; }
        public uint Quality { get; init; }
        public bool SkipIfFileExists { get; init; }

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

        // Bounds
        public bool Bounds { get; init; }
        public Color BoundsColor { get; init; }
        public int BoundsOpacity { get; init; }
        public bool ExcludeBoundsFromMap { get; init; }

        // Fixtures and trees
        public bool DrawFixtures { get; init; }
        public bool DrawFixturesBelowWater { get; init; }
        public bool DrawTrees { get; init; }
        public bool TreesAsImages { get; init; }
        public int TreeTransparency { get; init; }
    }
}
