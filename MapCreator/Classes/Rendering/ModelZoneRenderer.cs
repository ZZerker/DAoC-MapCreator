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

        // Dungeon textures are often very dark; the median of the drawn pixels is lifted to this level
        private const double TARGET_MEDIAN = 0.38;

        private const double MAX_GAIN = 4.0;

        // A high gain multiplies the color too: dark olive moss turned lime in Fomor. Saturation keeps 1 / (1 + this * (gain - 1))
        private const double GAIN_DESATURATION = 0.25;

        // Tiled floor textures repeat visibly (Jordheim grass); a soft brightness variation over several tiles breaks the pattern
        private const double VARIATION = 0.15;

        // Dungeons with lights.csv: unlit parts keep this share of their brightness, a light adds up to LIGHT_STRENGTH in its color
        private const double AMBIENT = 0.8;

        // lightdef.csv radii (256 to 1536) light far less than the client shows; the reach is this multiple
        private const double LIGHT_REACH = 4;

        private const double LIGHT_STRENGTH = 0.8;

        // Share of a light's own color, the rest is white: the full colors made the map look like a fairground
        private const double LIGHT_SATURATION = 0.35;

        private const double MAX_LIGHT = 1.6;

        public void Render(ZoneConfiguration conf, FileInfo mapFile)
        {
            reporter.Log("Loading models ...", LogLevel.Notice);
            var loader = new FixturesLoader(conf);

            using (var map = MagickWrapper.NewImage(BackgroundColor, settings.MapSize, settings.MapSize))
            {
                var gain = 1.0;
                var lit = false;
                using (var models = this.DrawModels(conf, loader))
                {
                    if (conf.IsDungeon)
                    {
                        gain = GetGain(models);
                        reporter.Log(string.Format("Dungeon brightness gain {0:F2}", gain), LogLevel.Notice);
                        Brighten(models, gain);

                        // Lit after the gain: dark textured dungeons (Darkness Falls, Darkspire) otherwise stay near black
                        var lights = loader.GetMapLights();
                        lit = lights.Count > 0;
                        if (lit)
                        {
                            reporter.Log(string.Format("Dungeon lights: {0}", lights.Count), LogLevel.Notice);
                            Light(models, lights);
                        }
                    }
                    map.Composite(models, 0, 0, CompositeOperator.SrcOver);
                }
                this.Write(map, mapFile);
                conf.Frame?.Write(conf.ZoneId, mapFile);
                mapFile.Refresh();
                MapLabelPainter.WriteLabeled(settings, mapFile, MapLabels.Write(conf.ZoneId, mapFile, false, conf.Frame));

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
                            if (lit)
                            {
                                Light(models, loader.GetMapLights());
                            }
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
            try
            {
                var models = new MapFixtures(conf, new List<WaterConfiguration>(), loader);
                models.Start();

                reporter.Log("Rendering models ...", LogLevel.Notice);
                models.DrawShared(layer);
                BreakUpTiling(layer, conf.ZoneId);
                return layer;
            }
            catch
            {
                layer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Multiplies the drawn pixels by smooth noise of two scales, the same for every map of the zone
        /// </summary>
        private static void BreakUpTiling(MagickImage layer, string zoneId)
        {
            var size = (int)layer.Width;
            var random = new Random(int.Parse(zoneId));
            var noise = new double[size * size];
            foreach (var (cells, weight) in new[] { (12, 0.5), (40, 0.3), (120, 0.2) })
            {
                var grid = new double[(cells + 1) * (cells + 1)];
                for (var i = 0; i < grid.Length; i++)
                {
                    grid[i] = random.NextDouble() * 2 - 1;
                }

                for (var y = 0; y < size; y++)
                {
                    var gy = (double)y * cells / size;
                    var y0 = (int)gy;
                    var fy = SmoothStep(gy - y0);
                    for (var x = 0; x < size; x++)
                    {
                        var gx = (double)x * cells / size;
                        var x0 = (int)gx;
                        var fx = SmoothStep(gx - x0);
                        var top = grid[y0 * (cells + 1) + x0] * (1 - fx) + grid[y0 * (cells + 1) + x0 + 1] * fx;
                        var bottom = grid[(y0 + 1) * (cells + 1) + x0] * (1 - fx) + grid[(y0 + 1) * (cells + 1) + x0 + 1] * fx;
                        noise[y * size + x] += weight * (top * (1 - fy) + bottom * fy);
                    }
                }
            }

            var channels = (int)layer.ChannelCount;
            using var pixels = layer.GetPixels();
            var values = pixels.ToArray();
            if (values == null || channels < 4)
            {
                return;
            }

            for (var i = 0; i < noise.Length; i++)
            {
                var factor = 1 + VARIATION * noise[i];
                for (var c = 0; c < 3; c++)
                {
                    values[i * channels + c] = (ushort)Math.Clamp(values[i * channels + c] * factor, 0, ushort.MaxValue);
                }
            }
            pixels.SetPixels(values);
        }

        private static double SmoothStep(double t)
        {
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// Gain whose curve (see Brighten) lifts the median brightness of the drawn pixels to TARGET_MEDIAN.
        /// The 95th percentile overexposed evenly lit dungeons (ROCK halls of 244) and unlit rooms (233).
        /// </summary>
        private static double GetGain(MagickImage layer)
        {
            using var pixels = layer.GetPixels();
            var values = pixels.ToArray();
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
            var median = brightness[brightness.Count / 2] / (double)ushort.MaxValue;
            if (median <= 0)
            {
                return MAX_GAIN;
            }
            return Math.Clamp(TARGET_MEDIAN * (1 - median) / (median * (1 - TARGET_MEDIAN)), 1, MAX_GAIN);
        }

        /// <summary>
        /// Lights the drawn pixels like the client: AMBIENT everywhere plus each light's color, falling off smoothly to its radius
        /// </summary>
        private static void Light(MagickImage layer, IReadOnlyList<DungeonLight> lights)
        {
            var size = (int)layer.Width;
            var channels = (int)layer.ChannelCount;
            var light = new float[size * size * 3];
            Array.Fill(light, (float)AMBIENT);
            foreach (var source in lights)
            {
                var radius = source.Radius * LIGHT_REACH;
                var left = Math.Max(0, (int)(source.X - radius));
                var right = Math.Min(size - 1, (int)(source.X + radius));
                var top = Math.Max(0, (int)(source.Y - radius));
                var bottom = Math.Min(size - 1, (int)(source.Y + radius));
                for (var y = top; y <= bottom; y++)
                {
                    for (var x = left; x <= right; x++)
                    {
                        var dx = x - source.X;
                        var dy = y - source.Y;
                        var falloff = 1 - (dx * dx + dy * dy) / (radius * radius);
                        if (falloff <= 0)
                        {
                            continue;
                        }

                        var strength = (float)(LIGHT_STRENGTH * falloff * falloff);
                        var i = (y * size + x) * 3;
                        light[i] += strength * Tint(source.Red);
                        light[i + 1] += strength * Tint(source.Green);
                        light[i + 2] += strength * Tint(source.Blue);
                    }
                }
            }

            using var pixels = layer.GetPixels();
            var values = pixels.ToArray();
            if (values == null || channels < 3)
            {
                return;
            }

            for (var p = 0; p < size * size; p++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var factor = Math.Min(light[p * 3 + c], MAX_LIGHT);
                    values[p * channels + c] = (ushort)Math.Clamp(values[p * channels + c] * factor, 0, ushort.MaxValue);
                }
            }
            pixels.SetPixels(values);
        }

        private static float Tint(double channel)
        {
            return (float)(1 - LIGHT_SATURATION + LIGHT_SATURATION * channel);
        }

        /// <summary>
        /// Lifts dark pixels by the gain and bends bright ones towards white instead of clipping them: gain * x / (1 + (gain - 1) * x)
        /// on the brightest channel, the others keep their ratio
        /// </summary>
        private static void Brighten(MagickImage layer, double gain)
        {
            var channels = (int)layer.ChannelCount;
            if (gain <= 1 || channels < 3)
            {
                return;
            }

            using var pixels = layer.GetPixels();
            var values = pixels.ToArray();
            if (values == null)
            {
                return;
            }

            var saturation = 1 / (1 + GAIN_DESATURATION * (gain - 1));
            for (var i = 0; i + 2 < values.Length; i += channels)
            {
                var brightest = Math.Max(values[i], Math.Max(values[i + 1], values[i + 2])) / (double)ushort.MaxValue;
                if (brightest <= 0)
                {
                    continue;
                }

                var factor = gain / (1 + (gain - 1) * brightest);
                var luma = (0.299 * values[i] + 0.587 * values[i + 1] + 0.114 * values[i + 2]) * factor;
                for (var c = 0; c < 3; c++)
                {
                    values[i + c] = (ushort)Math.Clamp(luma + (values[i + c] * factor - luma) * saturation, 0, ushort.MaxValue);
                }
            }
            pixels.SetPixels(values);
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
