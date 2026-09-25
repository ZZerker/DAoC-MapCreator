using System.Collections.Generic;
using System.IO;
using ImageMagick;
using MapCreator.Classes.MapCreation;
using MapCreator.Classes.MapCreation.Fixtures;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders a capital city from its city.csv models. Cities have no terrain, water or bounds.
    /// </summary>
    internal sealed class CityRenderer(RenderSettings settings, IRenderReporter reporter)
    {
        private static readonly MagickColor BackgroundColor = MagickColor.FromRgb(30, 30, 30);

        public void Render(ZoneConfiguration conf, FileInfo mapFile)
        {
            reporter.Log("Loading city models ...", LogLevel.Notice);
            var loader = new FixturesLoader(conf);

            using (var map = MagickWrapper.NewImage(BackgroundColor, settings.MapSize, settings.MapSize))
            using (var cityModels = new MapFixtures(conf, new List<WaterConfiguration>(), loader))
            {
                cityModels.Start();

                reporter.Log("Rendering city ...", LogLevel.Notice);
                cityModels.Draw(map, false);

                reporter.Log(string.Format("Writing map image {0} ...", mapFile.Name));
                reporter.ProgressStartMarquee("Writing map image ...");
                map.Quality = settings.Quality;
                map.Depth = 8;
                map.Write(mapFile.FullName);
            }
        }
    }
}
