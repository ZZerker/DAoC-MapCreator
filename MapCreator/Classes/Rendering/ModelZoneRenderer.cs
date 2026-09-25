using System.Collections.Generic;
using System.IO;
using ImageMagick;
using MapCreator.Classes.MapCreation;
using MapCreator.Classes.MapCreation.Fixtures;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders zones built only from placed models: capital cities (city.csv) and dungeons (dungeon.place).
    /// They have no terrain, water or bounds.
    /// </summary>
    internal sealed class ModelZoneRenderer(RenderSettings settings, IRenderReporter reporter)
    {
        private static readonly MagickColor BackgroundColor = MagickColor.FromRgb(30, 30, 30);

        public void Render(ZoneConfiguration conf, FileInfo mapFile)
        {
            reporter.Log("Loading models ...", LogLevel.Notice);
            var loader = new FixturesLoader(conf);

            using (var map = MagickWrapper.NewImage(BackgroundColor, settings.MapSize, settings.MapSize))
            using (var models = new MapFixtures(conf, new List<WaterConfiguration>(), loader))
            {
                models.Start();

                reporter.Log("Rendering models ...", LogLevel.Notice);
                models.Draw(map, false);

                reporter.Log(string.Format("Writing map image {0} ...", mapFile.Name));
                reporter.ProgressStartMarquee("Writing map image ...");
                map.Quality = settings.Quality;
                map.Depth = 8;
                map.Write(mapFile.FullName);
            }
        }
    }
}
