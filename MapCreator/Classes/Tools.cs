//
// MapCreator
// Copyright(C) 2015 Stefan Schäfer <merec@merec.org>
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
using System.Linq;

namespace MapCreator.Classes
{
	internal static class Tools
    {

        /// <summary>
        /// Normalize a 3x1 vector
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        public static double[] NormalizeVector(double[] vector)
        {
            var modulus = Math.Sqrt(vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2]);
            if (modulus == 0) modulus = 1e-50;

            return new double[] { vector[0] / modulus, vector[1] / modulus, vector[2] / modulus };
        }

        /// <summary>
        /// Parses a color. Ban be [r,g,b], [r,g,b,a] or int32 (engine color)
        /// </summary>
        /// <returns></returns>
        public static Color ParseColor(string color)
        {
            int[] colorParts;
            if (color.Contains(','))
            {
                colorParts = (int[])color.Split(',').Select(int.Parse);
            }
            else
            {
                colorParts = new int[] { Convert.ToInt32(color) };
            }

            if (colorParts.Length == 1)
            {
                return Color.FromArgb(255, (int)(colorParts[0] % 256), (int)(colorParts[0] / 256) % 256, (int)(colorParts[0] / 256 / 65536));
            }
            else if (colorParts.Length == 3)
            {
                return Color.FromArgb(255, colorParts[0], colorParts[1], colorParts[2]);
            }
            else if (colorParts.Length == 4)
            {
                return Color.FromArgb(colorParts[3], colorParts[0], colorParts[1], colorParts[2]);
            }
            else
            {
                throw new Exception("Bad color given: " + color);
            }

        }

        public static string MakeValidFileName(string name)
        {
            var invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
            var invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }

        public static string MakeValidDirectoryName(string name)
        {
            var invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidPathChars()));
            var invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }

        public static PointF GetCentroid(List<PointF> polygon)
        {
            return polygon.Aggregate(
                    new { xSum = 0.0, ySum = 0.0, n = 0 },
                    (acc, p) => new
                    {
                        xSum = acc.xSum + p.X,
                        ySum = acc.ySum + p.Y,
                        n = acc.n + 1
                    },
                    acc => new PointF((float)(acc.xSum / acc.n), (float)(acc.ySum / acc.n))
                );
        }

        public static IEnumerable<PointF> SortCounterClockwise(List<PointF> points)
        {
            var centroid = Tools.GetCentroid(points);
            points.Sort((a, b) => SortCornersClockwise(a, b, centroid));
            return points;
        }

        private static int SortCornersClockwise(PointF a, PointF b, PointF centroid)
        {
            //  Variables to Store the atans
            double aTanA, aTanB;

            //  Fetch the atans
            aTanA = Math.Atan2(a.Y - centroid.Y, a.X - centroid.X);
            aTanB = Math.Atan2(b.Y - centroid.Y, b.X - centroid.X);

            //  Determine next point in Clockwise rotation
            if (aTanA < aTanB) return -1;
            else if (aTanA > aTanB) return 1;
            return 0;
        }

        public static double GetPointDistance(PointF a, PointF b)
        {
            return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }

        public static bool PolygonHasClockwiseOrder(IEnumerable<PointF> points)
        {
            // Add the first point to the end.
            var numPoints = points.Count();

            var pts = new PointF[numPoints + 1];
            points.ToArray().CopyTo(pts, 0);
            pts[numPoints] = points.First();

            // Get the areas.
            float area = 0;
            for (var i = 0; i < numPoints; i++)
            {
                area +=
                    (pts[i + 1].X - pts[i].X) *
                    (pts[i + 1].Y + pts[i].Y) / 2;
            }

            // Return the result.
            return (area < 0);
        }


    }
}
