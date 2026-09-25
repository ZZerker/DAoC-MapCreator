//
// MapCreator
// Copyright(C) 2017 Stefan Schäfer <merec@merec.org>
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapCreator.Classes.MapCreation.Fixtures.Objects;
using NifUtil.Objects;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Loads the fixtures of one zone
    /// </summary>
	internal sealed class FixturesLoader
    {
        private readonly ZoneConfiguration zoneConf;

        private readonly List<FixtureRow> fixtureRows = new List<FixtureRow>();

        // Location where to search for npk with nifs
        private readonly List<string> nifSearchPaths;

        internal List<NifRow> NifRows { get; } = new List<NifRow>();

        public FixturesLoader(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConf = zoneConfiguration;

            var gamePath = Properties.Settings.Default.game_path;
            this.nifSearchPaths = new List<string>
                                  {
                                      Path.Combine(gamePath, "Newtowns\\zones\\Nifs"),
                                      Path.Combine(this.zoneConf.ZoneDirectory, "nifs"),
                                      Path.Combine(gamePath, "zones\\Nifs"),
                                      Path.Combine(gamePath, "frontiers\\NIFS"),
                                      Path.Combine(gamePath, "phousing\\nifs"),
                                      Path.Combine(gamePath, "Tutorial\\zones\\nifs"),
                                      Path.Combine(gamePath, "pregame"),
                                      Path.Combine(gamePath, "zones\\Dnifs")
                                  };

            this.LoadCsvData();
            this.LoadPolygons();
        }

        /// <summary>
        /// Load all required CSV data
        /// </summary>
        private void LoadCsvData() {
            this.zoneConf.Reporter.ProgressStartMarquee("Loading fixture data ...");

            var nifsCsvRows = DataWrapper.GetFileContent(this.zoneConf.CvsMpk, "nifs.csv");
            var fixturesRows = DataWrapper.GetFileContent(this.zoneConf.CvsMpk, "fixtures.csv");

            // Create a NumberFormatInfo object for floats and set some of its properties.
            var provider = new System.Globalization.NumberFormatInfo
                           {
		                           NumberDecimalSeparator = ".",
		                           NumberGroupSeparator = "",
		                           NumberGroupSizes = new int[] { 2 }
                           };

            foreach (var row in nifsCsvRows)
            {
                if (string.IsNullOrWhiteSpace(row) || row.StartsWith("Grid") || row.StartsWith("NIF")) continue;

                var fields = row.Split(',');

                var nifRow = new NifRow
                             {
		                             NifId = Convert.ToInt32(fields[0]),
		                             TextualName = fields[1],
		                             Filename = fields[2],
		                             Color = Convert.ToInt32(fields[5])
                             };
                this.NifRows.Add(nifRow);
            }

            // Read fixtures.csv
            foreach (var row in fixturesRows)
            {
                if (string.IsNullOrWhiteSpace(row) || row.StartsWith("Fixtures") || row.StartsWith("ID")) continue;

                var fields = row.Split(',');
                var fixtureRow = new FixtureRow
                                 {
		                                 Id = Convert.ToInt32(fields[0]),
		                                 NifId = Convert.ToInt32(fields[1]),
		                                 TextualName = fields[2],
		                                 X = Convert.ToDouble(fields[3], provider),
		                                 Y = Convert.ToDouble(fields[4], provider),
		                                 Z = Convert.ToDouble(fields[5], provider),
		                                 A = Convert.ToInt32(fields[6]),
		                                 Scale = Convert.ToInt32(fields[7]),
		                                 OnGround = (Convert.ToInt32(fields[11]) == 1) ? true : false,
		                                 Flip = (Convert.ToInt32(fields[12]) == 1) ? true : false
                                 };

                if (fields.Length > 15)
                {
                    fixtureRow.Angle3D = Convert.ToDouble(fields[15], provider);
                    fixtureRow.AxisX3D = Convert.ToDouble(fields[16], provider);
                    fixtureRow.AxisY3D = Convert.ToDouble(fields[17], provider);
                    fixtureRow.AxisZ3D = Convert.ToDouble(fields[18], provider);
                }

                this.fixtureRows.Add(fixtureRow);
            }

            // Add the trees of the clusters to the cache
            for (var i = 0; i < this.NifRows.Count; i++ )
            {
                var treeCluster = FixtureCache.TreeClusters.FirstOrDefault(tc => tc.Name.ToLower() == this.NifRows[i].Filename.ToLower());
                if (treeCluster == null || this.NifRows.Any(n => n.Filename == treeCluster.Tree)) continue;

                var tree = new NifRow
                           {
		                           NifId = 10000 + i,
		                           TextualName = treeCluster.Tree + " (cluster tree)",
		                           Filename = treeCluster.Tree
                           };
                this.NifRows.Add(tree);
            }

            this.zoneConf.Reporter.ProgressReset();
        }

        /// <summary>
        /// Load the Polygons if the nifRows
        /// </summary>
        private void LoadPolygons()
        {
            if (!this.NifRows.Any() || !this.fixtureRows.Any()) return;

            var models = new List<(NifRow, string)>();
            foreach (var nifRow in this.NifRows)
            {
                if (FixtureCache.IsTreeCluster(nifRow.Filename)) continue;

                var nifArchivePath = this.FindNifArchive(nifRow);
                if (string.IsNullOrEmpty(nifArchivePath)) continue;

                nifRow.ArchiveDirectory = Path.GetDirectoryName(nifArchivePath);
                models.Add((nifRow, nifArchivePath));
            }

            FixtureCache.LoadPolygons(models, this.zoneConf.Reporter);
        }

        public List<DrawableFixture> GetDrawableFixtures()
        {
            var drawables = new List<DrawableFixture>();

            this.zoneConf.Reporter.Log("Preparing fixtures ...", LogLevel.Notice);
            this.zoneConf.Reporter.ProgressStart("Preparing fixtures ...");

            var trees = FixtureCache.Trees;
            var treeClusters = FixtureCache.TreeClusters;

            var progressCounter = 0;
            foreach (var fixtureRow in this.fixtureRows)
            {
                try
                {
                    var nifRow = this.NifRows.FirstOrDefault(n => n.NifId == fixtureRow.NifId);
                    if (nifRow == null) continue;

                    var fixture = new DrawableFixture
                                  {
		                                  // Set default values
		                                  Name = fixtureRow.TextualName,
		                                  NifName = nifRow.Filename,
		                                  TextureDirectory = nifRow.ArchiveDirectory,
		                                  FixtureRow = fixtureRow,
		                                  ZoneConf = this.zoneConf
                                  };

                    // Get renderer configuration
                    var rConf = FixtureRendererConfigurations.GetFixtureRendererConfiguration(nifRow.Filename);

                    fixture.IsTree = trees.Any(t => t.Name.ToLower() == nifRow.Filename.ToLower());
                    fixture.IsTreeCluster = treeClusters.Any(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());

                    if (rConf != null && (rConf.Value.Name == "TreeShaded" || rConf.Value.Name == "TreeImage"))
                    {
                        fixture.IsTree = true;
                    }

                    if (fixture.IsTree)
                    {
                        fixture.Tree = trees.FirstOrDefault(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());
                        fixture.RawPolygons = nifRow.Polygons;

                        fixture.RendererConf = rConf ?? FixtureRendererConfigurations.GetRendererById("TreeImage");
                    }
                    else if (fixture.IsTreeCluster)
                    {
                        fixture.TreeCluster = treeClusters.FirstOrDefault(tc => tc.Name.ToLower() == nifRow.Filename.ToLower());

                        // Get the polygons of the base nif
                        var treeNif = this.NifRows.FirstOrDefault(n => n.Filename.ToLower() == fixture.TreeCluster.Tree.ToLower());
                        if (treeNif == null) continue;
                        var baseTreePolygons = treeNif.Polygons;

                        // Loop the instances and transform the polygons
                        var treeClusterPolygons = new List<Polygon>();
                        foreach (var tree in fixture.TreeCluster.TreeInstances)
                        {
                            foreach (var treePolygon in baseTreePolygons)
                            {
                                var newPolygon = new Polygon(treePolygon.P1, treePolygon.P2, treePolygon.P3, treePolygon.Texture);
                                for (var i = 0; i < newPolygon.Vectors.Length; i++)
                                {
                                    newPolygon.Vectors[i].X -= tree.X;
                                    newPolygon.Vectors[i].Y += tree.Y;
                                    newPolygon.Vectors[i].Z += tree.Z;
                                }
                                treeClusterPolygons.Add(newPolygon);
                            }
                        }
                        fixture.RawPolygons = treeClusterPolygons;

                        if (rConf == null) fixture.RendererConf = FixtureRendererConfigurations.GetRendererById("TreeImage");
                        else fixture.RendererConf = rConf.GetValueOrDefault();
                    }
                    else
                    {
                        fixture.RawPolygons = nifRow.Polygons;

                        if (rConf == null)
                        {
                            fixture.RendererConf = FixtureCache.HasPrerenderedImage(fixture.NifName)
                                ? FixtureRendererConfigurations.GetRendererById("Prerendered")
                                : FixtureRendererConfigurations.DefaultConfiguration;
                        }
                        else fixture.RendererConf = rConf.GetValueOrDefault();
                    }

                    // Calculate the final look of the model
                    var result = fixture.Calc();
                    if (result)
                    {
                        drawables.Add(fixture);
                    }
                    else
                    {
                        this.zoneConf.Reporter.Log(string.Format("Fixture {0} (x: {1}, y: {2}, z: {3}) is too small to get drawn.", fixtureRow.TextualName, fixtureRow.X, fixtureRow.Y, fixtureRow.Z));
                    }

                    progressCounter++;
                    var percent = 100 * progressCounter / this.fixtureRows.Count;
                    this.zoneConf.Reporter.ProgressUpdate(percent);
                }
                catch
                {
                    this.zoneConf.Reporter.Log(string.Format("Error in fixture row of {0} (x: {1}, y: {2}, z: {3})", fixtureRow.TextualName, fixtureRow.X, fixtureRow.Y, fixtureRow.Z));
                    continue;
                }
            }

            this.zoneConf.Reporter.Log("Fixtures prepared!", LogLevel.Success);
            this.zoneConf.Reporter.ProgressReset();

            return drawables;
        }

        private string FindNifArchive(NifRow nifRow)
        {
            var archiveName = Path.GetFileNameWithoutExtension(nifRow.Filename) + ".npk";
            foreach (var dir in this.nifSearchPaths)
            {
                if (File.Exists(Path.Combine(dir, archiveName)))
                {
                    return string.Format("{0}\\{1}", dir, archiveName);
                }
            }

            this.zoneConf.Reporter.Log(string.Format("Unable to find nif \"{0}\"!", nifRow.Filename), LogLevel.Warning);
            return null;
        }
    }
}
