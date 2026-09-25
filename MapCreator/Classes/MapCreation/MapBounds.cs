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
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using ImageMagick;
using ImageMagick.Drawing;

namespace MapCreator.Classes.MapCreation
{
	internal class MapBounds
    {
        /// <summary>
        /// The current zoneConfiguration
        /// </summary>
        private readonly ZoneConfiguration zoneConfiguration;

        /// <summary>
        /// If true, each shape will be drawn with its coordinates in data/debug/zoneXXX
        /// </summary>
        private readonly bool debug = false;

        /// <summary>
        /// The final shapes
        /// </summary>
        private readonly List<List<PointF>> bounds = new List<List<PointF>>();

        /// <summary>
        /// Substraction bounds
        /// </summary>
        private List<List<int[]>> boundsSubstraction = new List<List<int[]>>();

        /// <summary>
        /// Background Color
        /// </summary>
        private Color boundsColor = Color.Black;

        /// <summary>
        /// Opacity
        /// </summary>
        private int transparency = 30;

        /// <summary>
        /// Removes the area of the bounds from the final image
        /// </summary>
        private bool excludeFromMap = false;

        #region Settings

        /// <summary>
        /// Exlude bound from final image
        /// </summary>
        public bool ExcludeFromMap
        {
            get => this.excludeFromMap;
            set => this.excludeFromMap = value;
        }

        /// <summary>
        /// Opacity
        /// </summary>
        public int Transparency
        {
            set => this.transparency = value;
        }

        /// <summary>
        /// Background color
        /// </summary>
        public Color BoundsColor
        {
            set => this.boundsColor = value;
        }

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="zoneConfiguration"></param>
        public MapBounds(ZoneConfiguration zoneConfiguration)
        {
            this.zoneConfiguration = zoneConfiguration;
            this.ParseBounds();
        }

        /// <summary>
        /// Gets the raw lines from bound.csv
        /// </summary>
        /// <returns></returns>
        private List<string> GetCoordinateLines()
        {
            var lines = new List<string>();

            using (var csv = MpkWrapper.GetFileFromMpk(this.zoneConfiguration.DatMpk, "bound.csv"))
            {
                string row;
                while ((row = csv.ReadLine()) != null)
                {
                    lines.Add(row);
                }
            }

            return lines;
        }

        private void ParseBounds()
        {
            var lines = this.GetCoordinateLines();
            var polygons = new List<List<PointF>>();

            foreach (var line in lines)
            {
                var polygon = new List<PointF>();

                var coordsRaw = line.Split(',');

                // The first is 0?; the second the number of points
                var unknown1 = Convert.ToInt32(coordsRaw[0]);
                var count = Convert.ToInt32(coordsRaw[1]);

                for (var i = 1; i <= count; i++)
                {
                    var x = Convert.ToInt32(coordsRaw[i * 2]);
                    if (x == 65536) x = 65535;

                    var y = Convert.ToInt32(coordsRaw[i * 2 + 1]);
                    if (y == 65536) y = 65535;

                    polygon.Add(new PointF(x, y));
                }

                polygons.Add(polygon);
            }

            // Correct some zones here
            
            // 028
            if (this.zoneConfiguration.ZoneId == "028")
            {
                // The last point is too close to the first
                polygons.First().RemoveAt(polygons.First().Count - 1);
                polygons.First().Add(new PointF(1284, 2100));
            }

            // Housing, some shapes have clockwise order but must be counter clockwise
            if (this.zoneConfiguration.ZoneId == "064") polygons[0].Reverse();
            if (this.zoneConfiguration.ZoneId == "117") polygons[2].Reverse();
            if (this.zoneConfiguration.ZoneId == "122") polygons[1].Reverse();
            if (this.zoneConfiguration.ZoneId == "218") polygons[1].Reverse();
            if (this.zoneConfiguration.ZoneId == "262") polygons[0].Reverse();

            //polygons = CombinePolygons(polygons);

            // 015 old hadrians wall
            if (this.zoneConfiguration.ZoneId == "015")
            {
                // The last point is too close to the first
                polygons.RemoveRange(1, 3);
            }
            // Oceanus Notots, the first must be counter clockwise, it must be negated
            if (this.zoneConfiguration.ZoneId == "076")
            {
                polygons[0].Reverse();
            }
            // DR, the outland zone have one shape in wrong direction which breaks the parser
            if (this.zoneConfiguration.ZoneId == "330") polygons[1].Reverse();
            if (this.zoneConfiguration.ZoneId == "334") polygons[1].Reverse();
            if (this.zoneConfiguration.ZoneId == "335") polygons[1].Reverse();

            foreach (var polygon in polygons)
            {
                if (polygon.Count < 4) continue;
                //if (zoneConfiguration.Expansion == GameExpansion.NewFrontiers && polygon.Count <= 6) continue;

                this.FillPolygon(polygon);
                this.bounds.Add(polygon);
            }

        }

        private List<List<PointF>> CombinePolygons(List<List<PointF>> polygons)
        {
            for (var i = 0; i < polygons.Count; i++)
            {
                var last = polygons[i].Last();

                for (var n = 0; n < polygons.Count; n++)
                {
                    if (i == n) continue;
                    var first = polygons[n].First();

                    var connect = false;
                    if (last.X == first.X && last.Y == first.Y) connect = true;
                    else if (this.IsNextTo(last, first, 20)) connect = true;
                    else if (this.IsNextTo(first, last, 20)) connect = true;

                    if (connect)
                    {
                        // found a connection
                        polygons[i].AddRange(polygons[n]);
                        polygons.RemoveAt(n);
                        return this.CombinePolygons(polygons);
                    }
                }
            }

            return polygons;
        }

        private bool IsNextTo(PointF p1, PointF p2, int distance = 50)
        {
            var diffX = Math.Abs(p1.X - p2.X);
            var diffY = Math.Abs(p1.Y - p2.Y);
            return diffX <= distance && diffY <= distance;
        }

        private void FillPolygon(List<PointF> points)
        {
            // We want to know to which side of the maps the first points need to be drawn to
            // This can also be done with pure math, but I don't want to... (http://www.blackpawn.com/texts/pointinpoly/)
            //
            // These graphic paths defines the draw direction
            // -----------
            // | \  n  / |
            // |  \   /  |
            // |   \ /   |
            // | w  /  e |
            // |   / \   |
            // |  /   \  |
            // | /  s  \ |
            // -----------
            var first = new PointF((float)points.First().X, (float)points.First().Y); // there are some shapes wich use 65536 as max X or Y
            var last = new PointF((float)points.Last().X, (float)points.Last().Y); // there are some shapes wich use 65536 as max X or Y

            // If its a complete polygon where the last equals the first point, don't do anything
            if (first.X == last.X && first.Y == last.Y) return;

            // Avoid flood fills
            // This happens if the start AND end of the shape are on the same side (see dartmoor, llyn bafog).
            var avoidFloodFill = false;
            if (first.Y == 0 && last.Y == 0 && first.X > last.X) avoidFloodFill = true; // north
            else if (first.X == 65535 && last.X == 65535 && first.Y > last.Y) avoidFloodFill = true; // east
            else if (first.Y == 65535 && last.Y == 65535 && first.X < last.X) avoidFloodFill = true; // south
            else if (first.X == 0 && last.X == 0 && first.Y < last.Y) avoidFloodFill = true; // west

            // Fill the shape in a clockwise order, maximum 6 required steps
            // But first check if the distance last-point <--> first-point is lower
            var firstLastDistance = Tools.GetPointDistance(first, last);

            // Go the next map border at n, e, s, w
            var pointToPrepend = GetNearestBorderPoint(first);
            if (pointToPrepend.HasValue && firstLastDistance < Tools.GetPointDistance(pointToPrepend.Value, first))
            {
                return;
            }

            var pointToAppend = GetNearestBorderPoint(last);
            if (pointToAppend.HasValue && firstLastDistance < Tools.GetPointDistance(last, pointToAppend.Value))
            {
                return;
            }

            // Okay, we need to fill
            if (pointToPrepend.HasValue)
            {
                points.Insert(0, pointToPrepend.Value);
            }
            if (pointToAppend.HasValue)
            {
                points.Add(pointToAppend.Value);
            }

            // No we have first and last at least with one of 0/65535 on x and y

            // Go around
            while (true)
            {
                first = new PointF((float)points.First().X, (float)points.First().Y); // there are some shapes wich use 65536 as max X or Y
                last = new PointF((float)points.Last().X, (float)points.Last().Y); // there are some shapes wich use 65536 as max X or Y

                // Chek if we are finished
                if ((first.X == last.X && first.Y == last.Y) && !avoidFloodFill) break;

                if (first.Y == 0 && first.X != 65535 && (last.Y != 0 || first.X > last.X)) points.Insert(0, new PointF(65535, 0)); // to north-east corner
                else if (first.X == 65535 && first.Y != 65535 && (last.X != 65535 || first.Y > last.Y)) points.Insert(0, new PointF(65535, 65535)); // to south-east corner
                else if (first.Y == 65535 && first.X != 0 && (last.Y != 65535 || first.X < last.X)) points.Insert(0, new PointF(0, 65535)); // to south-west corner
                else if (first.X == 0 && first.Y != 0 && (last.X != 0 || first.Y < last.Y)) points.Insert(0, new PointF(0, 0)); // to north-west corner
                else break;
            }
        }

        // 65536 instead of 65535, else points on the border are not visible
        private static readonly GraphicsPath NorthTriangle = CreateTriangle(new PointF(0, 0), new PointF(65536, 0));
        private static readonly GraphicsPath EastTriangle = CreateTriangle(new PointF(65536, 0), new PointF(65536, 65536));
        private static readonly GraphicsPath SouthTriangle = CreateTriangle(new PointF(65536, 65536), new PointF(0, 65536));
        private static readonly GraphicsPath WestTriangle = CreateTriangle(new PointF(0, 65536), new PointF(0, 0));

        private static GraphicsPath CreateTriangle(PointF corner1, PointF corner2)
        {
            var path = new GraphicsPath();
            path.AddLines(new[] { corner1, corner2, new PointF(32768, 32768) });
            return path;
        }

        private static PointF? GetNearestBorderPoint(PointF point)
        {
            if (NorthTriangle.IsVisible(point))
            {
                return new PointF(point.X, 0);
            }
            if (EastTriangle.IsVisible(point))
            {
                return new PointF(65535, point.Y);
            }
            if (SouthTriangle.IsVisible(point))
            {
                return new PointF(point.X, 65535);
            }
            if (WestTriangle.IsVisible(point))
            {
                return new PointF(0, point.Y);
            }
            return null;
        }

        /// <summary>
        /// Draw the bounds onto map
        /// </summary>
        /// <param name="map"></param>
        public void Draw(MagickImage map)
        {
            if (this.bounds.Count == 0) return;
            this.zoneConfiguration.Reporter.ProgressStart("Drawing zone bounds ...");

            // Sort the polygons
            var polygons = new List<List<PointD>>();
            var negatedPolygons = new List<List<PointD>>();

            foreach (var polygon in this.bounds)
            {
                var isClockwise = Tools.PolygonHasClockwiseOrder(polygon);
                var polygonConverted = polygon.Select(c => new PointD(this.zoneConfiguration.ZoneCoordinateToMapCoordinate(c.X), this.zoneConfiguration.ZoneCoordinateToMapCoordinate(c.Y))).ToList();

                // polygons in clockwise order needs to be negated
                if (isClockwise) negatedPolygons.Add(polygonConverted);
                else polygons.Add(polygonConverted);
            }

            var backgroundColor = MagickColors.Transparent;
            if (polygons.Count == 0) {
                // There are no normal polygons, we need to fill the hole zone and substract negatedPolygons
                backgroundColor = this.boundsColor.ToMagickColor();
            }

            using (var boundMap = MagickWrapper.NewImage(backgroundColor, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
            {
                var progressCounter = 0;

                boundMap.Alpha(AlphaOption.Set);
                boundMap.Settings.FillColor = this.boundsColor.ToMagickColor();
                foreach (var coords in polygons)
                {
                    var poly = new DrawablePolygon(coords);
                    boundMap.Draw(poly);

                    progressCounter++;
                    var percent = 100 * progressCounter / this.bounds.Count();
                    this.zoneConfiguration.Reporter.ProgressUpdate(percent);
                }

                if (negatedPolygons.Count > 0)
                {
                    using (var negatedBoundMap = MagickWrapper.NewImage(Color.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
                    {
                        negatedBoundMap.Settings.FillColor = this.boundsColor.ToMagickColor();

                        foreach (var coords in negatedPolygons)
                        {
                            var poly = new DrawablePolygon(coords);
                            negatedBoundMap.Draw(poly);

                            progressCounter++;
                            var percent = 100 * progressCounter / this.bounds.Count();
                            this.zoneConfiguration.Reporter.ProgressUpdate(percent);
                        }
                        boundMap.Composite(negatedBoundMap, 0, 0, CompositeOperator.DstOut);
                    }
                }

                this.zoneConfiguration.Reporter.ProgressStartMarquee("Merging ...");
                if (this.ExcludeFromMap)
                {
                    map.Composite(boundMap, 0, 0, CompositeOperator.DstOut);
                }
                else
                {
                    if (this.transparency != 0)
                    {
                        boundMap.Alpha(AlphaOption.Set);
                        var divideValue = 100.0 / (100.0 - this.transparency);
                        boundMap.Evaluate(Channels.Alpha, EvaluateOperator.Divide, divideValue);
                    }

                    map.Composite(boundMap, 0, 0, CompositeOperator.SrcOver);
                }
            }

            if (this.debug)
            {
                this.DebugMaps();
            }

            this.zoneConfiguration.Reporter.ProgressReset();
        }

        private void DebugMaps()
        {
            this.zoneConfiguration.Reporter.Log("Drawing debug bound images ...", LogLevel.Warning);
            this.zoneConfiguration.Reporter.ProgressStartMarquee("Debug bound images ...");

            var debugDir = new DirectoryInfo(string.Format("{0}\\debug\\bound\\{1}", System.Windows.Forms.Application.StartupPath, this.zoneConfiguration.ZoneId));
            if (!debugDir.Exists) debugDir.Create();
            debugDir.GetFiles().ToList().ForEach(f => f.Delete());

            var boundIndex = 0;
            foreach (var allCoords in this.bounds)
            {
                using (var bound = MagickWrapper.NewImage(MagickColors.Transparent, this.zoneConfiguration.TargetMapSize, this.zoneConfiguration.TargetMapSize))
                {
                    var coords = allCoords.Select(c => new PointD(this.zoneConfiguration.ZoneCoordinateToMapCoordinate(c.X), this.zoneConfiguration.ZoneCoordinateToMapCoordinate(c.Y))).ToList();

                    var poly = new DrawablePolygon(coords);
                    bound.Settings.FillColor = new MagickColor(0, 0, 0, 256 * 128);
                    bound.Draw(poly);
                    
                    // Print Text
                    for (var i = 0; i < coords.Count; i++)
                    {
                        double x, y;

                        if (coords[i].X > this.zoneConfiguration.TargetMapSize / 2) x = coords[i].X - 15;
                        else x = coords[i].X + 1;

                        if (coords[i].Y < this.zoneConfiguration.TargetMapSize / 2) y = coords[i].Y + 15;
                        else y = coords[i].Y - 1;

                        bound.Settings.FontPointsize = 10.0;
                        bound.Settings.FillColor = MagickColors.Black;
                        var text = new DrawableText(x, y, string.Format("{0} ({1}/{2})", i, this.zoneConfiguration.MapCoordinateToZoneCoordinate(coords[i].X), this.zoneConfiguration.MapCoordinateToZoneCoordinate(coords[i].Y)));
                        bound.Draw(text);
                        
                        using (var pixels = bound.GetPixels())
                        {
                            int x2, y2;
                            if (coords[i].X == this.zoneConfiguration.TargetMapSize) x2 = this.zoneConfiguration.TargetMapSize - 1;
                            else x2 = (int)coords[i].X;
                            if (coords[i].Y == this.zoneConfiguration.TargetMapSize) y2 = this.zoneConfiguration.TargetMapSize - 1;
                            else y2 = (int)coords[i].Y;

                            pixels.SetPixel(x2, y2, new ushort[] { 0, 0, 65535, 0 });
                        }
                    }

                    //bound.Quality = 100;
                    bound.Write(string.Format("{0}\\bound_{1}.png", debugDir.FullName, boundIndex));

                    boundIndex++;
                }
            }

            this.zoneConfiguration.Reporter.ProgressReset();
        }
    }
}
