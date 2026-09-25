using System.IO;
using MapCreator.Classes.MapCreation;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// Renders one outdoor zone into an image file
    /// </summary>
    internal sealed class ZoneRenderer(RenderSettings settings, IRenderReporter reporter)
    {
        /// <summary>
        /// Renders the zone and returns the written file, or null if it was skipped
        /// </summary>
        public FileInfo Render(ZoneSelection zone)
        {
            reporter.Log(string.Format("Start creating map for zone {0} ...", zone.Id), LogLevel.Notice);

            var mapFile = this.GetTargetFile(zone);
            if (!Directory.Exists(mapFile.DirectoryName))
            {
                Directory.CreateDirectory(mapFile.DirectoryName);
            }

            if (settings.SkipIfFileExists && mapFile.Exists)
            {
                reporter.Log(string.Format("The target file \"{0}\" already exists. Skipping.", mapFile.FullName));
                return null;
            }

            var drawFixtures = settings.DrawFixtures;
            var drawFixturesBelowWater = settings.DrawFixturesBelowWater;
            var drawTrees = settings.DrawTrees;

            using (var conf = new ZoneConfiguration(zone.Id, settings.MapSize, reporter))
            {
                if (conf.IsCity || conf.IsDungeon)
                {
                    new ModelZoneRenderer(settings, reporter).Render(conf, mapFile);
                    mapFile.Refresh();
                    return mapFile;
                }

                var background = new MapBackground(conf)
                                 {
                                     DrawBackground = settings.DrawBackground
                                 };

                reporter.Log("Rendering background ...", LogLevel.Notice);
                using (var map = background.Draw())
                {
                    if (map == null)
                    {
                        return mapFile;
                    }

                    reporter.Log("Finished background rendering!", LogLevel.Success);

                    if (settings.Lightmap)
                    {
                        reporter.Log("Rendering lightmap ...", LogLevel.Notice);
                        var lightmapGenerator = new MapLightmap(conf)
                                                {
                                                    ZScale = settings.LightmapZScale,
                                                    LightMin = settings.LightmapLightMin,
                                                    LightMax = settings.LightmapLightMax,
                                                    ZVector = (double[])settings.LightmapZVector.Clone()
                                                };
                        lightmapGenerator.RecalculateLights();
                        lightmapGenerator.Draw(map);
                        reporter.Log("Finished lightmap rendering!", LogLevel.Success);
                    }

                    // Water areas are also needed to sort the fixtures
                    reporter.Log("Loading water configurations ...", LogLevel.Notice);
                    var river = new MapWater(conf);
                    reporter.Log("Finished loading water configurations!", LogLevel.Success);

                    MapFixtures fixturesGenerator = null;
                    if (drawFixtures || drawFixturesBelowWater || drawTrees)
                    {
                        reporter.Log("Loading fixtures ...", LogLevel.Notice);
                        fixturesGenerator = new MapFixtures(conf, river.WaterAreas)
                                            {
                                                DrawFixtures = drawFixtures || drawFixturesBelowWater,
                                                DrawTrees = drawTrees,
                                                DrawTreesAsImages = settings.TreesAsImages,
                                                TreeTransparency = settings.TreeTransparency
                                            };
                        fixturesGenerator.Start();
                        reporter.Log("Finished loading fixtures!", LogLevel.Success);
                    }

                    if (drawFixturesBelowWater)
                    {
                        reporter.Log("Rendering fixtures below water level ...", LogLevel.Notice);
                        fixturesGenerator.Draw(map, true);
                        reporter.Log("Finished rendering fixtures below water level!", LogLevel.Success);
                    }

                    if (settings.Rivers)
                    {
                        reporter.Log("Rendering water ...", LogLevel.Notice);
                        river.WaterColor = settings.RiversColor;
                        river.WaterTransparency = settings.RiverOpacity;
                        river.UseClientColors = settings.RiversUseDefaultColor;
                        river.Draw(map);
                        reporter.Log("Finished water rendering!", LogLevel.Success);
                    }

                    if (drawFixtures || drawTrees)
                    {
                        reporter.Log("Rendering fixtures above water level ...", LogLevel.Notice);
                        fixturesGenerator.Draw(map, false);
                        reporter.Log("Finished rendering fixtures above water level!", LogLevel.Success);
                    }

                    fixturesGenerator?.Dispose();

                    if (settings.Bounds)
                    {
                        reporter.Log("Adding zone bounds ...", LogLevel.Notice);
                        var mapBounds = new MapBounds(conf)
                                        {
                                            BoundsColor = settings.BoundsColor,
                                            Transparency = settings.BoundsOpacity,
                                            ExcludeFromMap = settings.ExcludeBoundsFromMap
                                        };
                        mapBounds.Draw(map);
                        reporter.Log("Finished zone bunds!", LogLevel.Success);
                    }

                    reporter.Log(string.Format("Writing map image {0} ...", mapFile.Name));
                    reporter.ProgressStartMarquee("Writing map image ...");
                    map.Quality = settings.Quality;
                    map.Depth = 8;
                    map.Write(mapFile.FullName);
                }

                MapLabels.Write(zone.Id, mapFile);
            }

            mapFile.Refresh();
            return mapFile;
        }

        private FileInfo GetTargetFile(ZoneSelection zone)
        {
            var directory = string.IsNullOrEmpty(settings.DirectoryPattern) ? "maps" : settings.DirectoryPattern;
            directory = Tools.MakeValidDirectoryName(this.ReplacePlaceholders(directory, zone));

            var fileName = string.IsNullOrEmpty(settings.FilePattern) ? "zone{id}_{size}" : settings.FilePattern;
            fileName = Tools.MakeValidFileName(this.ReplacePlaceholders(fileName, zone));

            var extension = settings.FileType == "PNG" ? "png" : "jpg";
            return new FileInfo(string.Format("{0}\\{1}\\{2}.{3}", settings.TargetPath, directory, fileName, extension));
        }

        private string ReplacePlaceholders(string pattern, ZoneSelection zone)
        {
            return pattern
                .Replace("{id}", zone.Id)
                .Replace("{name}", zone.Name)
                .Replace("{realm}", zone.Realm)
                .Replace("{expansion}", zone.Expansion)
                .Replace("{type}", zone.Type)
                .Replace("{size}", settings.MapSize.ToString());
        }
    }
}
