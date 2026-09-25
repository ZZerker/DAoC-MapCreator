using System.Collections.Generic;
using System.IO;
using ImageMagick;
using MapCreator.Classes.MapCreation;
using MapCreator.Classes.MapCreation.Fixtures;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders zones built only from placed models: capital cities (city.csv) and dungeons (dungeon.place).
    /// They have no terrain, water or bounds. Dungeons with levels in areas.dat get one more map per level.
    /// </summary>
    internal sealed class ModelZoneRenderer(RenderSettings settings, IRenderReporter reporter)
    {
        private static readonly MagickColor BackgroundColor = MagickColor.FromRgb(30, 30, 30);

        // Share of the whole dungeon left visible under a level, like the client level maps
        private const double OTHER_LEVELS_OPACITY = 0.25;

        public void Render(ZoneConfiguration conf, FileInfo mapFile)
        {
            reporter.Log("Loading models ...", LogLevel.Notice);
            var loader = new FixturesLoader(conf);

            using (var map = MagickWrapper.NewImage(BackgroundColor, settings.MapSize, settings.MapSize))
            {
                this.DrawModels(conf, loader, map);
                this.Write(map, mapFile);

                foreach (var level in conf.Levels)
                {
                    reporter.Log(string.Format("Rendering level {0} ({1}) ...", level.Index, level.Name), LogLevel.Notice);
                    using (var levelMap = (MagickImage)map.Clone())
                    {
                        levelMap.Evaluate(Channels.RGB, EvaluateOperator.Multiply, OTHER_LEVELS_OPACITY);
                        levelMap.Evaluate(Channels.RGB, EvaluateOperator.Add, (1 - OTHER_LEVELS_OPACITY) * BackgroundColor.R);

                        loader.Level = level;
                        this.DrawModels(conf, loader, levelMap);
                        var levelName = string.Format("{0}_{1:00}{2}", Path.GetFileNameWithoutExtension(mapFile.Name), level.Index, mapFile.Extension);
                        this.Write(levelMap, new FileInfo(Path.Combine(mapFile.DirectoryName, levelName)));
                    }
                }
                loader.Level = null;
            }
        }

        private void DrawModels(ZoneConfiguration conf, FixturesLoader loader, MagickImage map)
        {
            using (var models = new MapFixtures(conf, new List<WaterConfiguration>(), loader))
            {
                models.Start();

                reporter.Log("Rendering models ...", LogLevel.Notice);
                models.Draw(map, false);
            }
        }

        private void Write(MagickImage map, FileInfo mapFile)
        {
            reporter.Log(string.Format("Writing map image {0} ...", mapFile.Name));
            reporter.ProgressStartMarquee("Writing map image ...");
            map.Quality = settings.Quality;
            map.Depth = 8;
            map.Write(mapFile.FullName);
        }
    }
}
