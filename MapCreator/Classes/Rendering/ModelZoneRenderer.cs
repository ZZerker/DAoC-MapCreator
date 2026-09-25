using System;
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

        // Dungeon textures are often very dark; the brightest 5% of the drawn pixels are lifted to this level
        private const double TARGET_BRIGHTNESS = 0.85;

        private const double MAX_GAIN = 3.0;

        public void Render(ZoneConfiguration conf, FileInfo mapFile)
        {
            reporter.Log("Loading models ...", LogLevel.Notice);
            var loader = new FixturesLoader(conf);

            using (var map = MagickWrapper.NewImage(BackgroundColor, settings.MapSize, settings.MapSize))
            {
                var gain = 1.0;
                using (var models = this.DrawModels(conf, loader))
                {
                    if (conf.IsDungeon)
                    {
                        gain = GetGain(models);
                        reporter.Log(string.Format("Dungeon brightness gain {0:F2}", gain), LogLevel.Notice);
                        Brighten(models, gain);
                    }
                    map.Composite(models, 0, 0, CompositeOperator.SrcOver);
                }
                this.Write(map, mapFile);

                foreach (var level in conf.Levels)
                {
                    reporter.Log(string.Format("Rendering level {0} ({1}) ...", level.Index, level.Name), LogLevel.Notice);
                    using (var levelMap = (MagickImage)map.Clone())
                    {
                        levelMap.Evaluate(Channels.RGB, EvaluateOperator.Multiply, OTHER_LEVELS_OPACITY);
                        levelMap.Evaluate(Channels.RGB, EvaluateOperator.Add, (1 - OTHER_LEVELS_OPACITY) * BackgroundColor.R);

                        loader.Level = level;
                        using (var models = this.DrawModels(conf, loader))
                        {
                            Brighten(models, gain);
                            levelMap.Composite(models, 0, 0, CompositeOperator.SrcOver);
                        }
                        var levelName = string.Format("{0}_{1:00}{2}", Path.GetFileNameWithoutExtension(mapFile.Name), level.Index, mapFile.Extension);
                        this.Write(levelMap, new FileInfo(Path.Combine(mapFile.DirectoryName, levelName)));
                    }
                }
                loader.Level = null;
            }
        }

        private MagickImage DrawModels(ZoneConfiguration conf, FixturesLoader loader)
        {
            var layer = MagickWrapper.NewImage(MagickColors.Transparent, settings.MapSize, settings.MapSize);
            using (var models = new MapFixtures(conf, new List<WaterConfiguration>(), loader))
            {
                models.Start();

                reporter.Log("Rendering models ...", LogLevel.Notice);
                models.DrawShared(layer);
            }
            return layer;
        }

        /// <summary>
        /// Gain that lifts the 95th brightness percentile of the drawn pixels to TARGET_BRIGHTNESS
        /// </summary>
        private static double GetGain(MagickImage layer)
        {
            var values = layer.GetPixels().ToArray();
            var channels = (int)layer.ChannelCount;
            if (values == null || channels < 4)
            {
                return 1;
            }

            var brightness = new List<int>();
            for (var i = 0; i + 3 < values.Length; i += channels)
            {
                if (values[i + 3] > ushort.MaxValue / 2)
                {
                    brightness.Add(Math.Max(values[i], Math.Max(values[i + 1], values[i + 2])));
                }
            }
            if (brightness.Count == 0)
            {
                return 1;
            }

            brightness.Sort();
            var percentile = brightness[(int)(brightness.Count * 0.95)];
            return percentile == 0 ? MAX_GAIN : Math.Clamp(TARGET_BRIGHTNESS * ushort.MaxValue / percentile, 1, MAX_GAIN);
        }

        private static void Brighten(MagickImage layer, double gain)
        {
            if (gain > 1)
            {
                layer.Evaluate(Channels.RGB, EvaluateOperator.Multiply, gain);
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
