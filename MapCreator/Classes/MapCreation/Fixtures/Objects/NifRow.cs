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

using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using NifUtil.Objects;

namespace MapCreator.Classes.MapCreation.Fixtures.Objects
{
	internal class NifRow
    {
	    #region Getter/setter

        public int Color { get; set; }

        public string Filename { get; set; }

        public string TextualName { get; set; }

        public int NifId { get; set; }

        public Polygon[] Polygons { get; set; }

        /// <summary>
        /// Folder of the .npk the model was loaded from, searched first for its textures
        /// </summary>
        public string ArchiveDirectory { get; set; }

        /// <summary>
        /// Model variant (keep realm and tier), part of the cache name; null for plain models
        /// </summary>
        public string Variant { get; set; }

        /// <summary>
        /// Additional node filter by node name
        /// </summary>
        public System.Func<string, bool> IsNodeDrawable { get; set; }

        /// <summary>
        /// Replaces base textures by material name and texture name
        /// </summary>
        public System.Func<string, string, string> ResolveTexture { get; set; }
        #endregion

        public NifRow()
        {
        }

        public SizeF GetSize(double scale, double angle)
        {
            // Create a copy of the polygons
            var polygons = new List<Polygon>(this.Polygons);

            var vectors = this.Polygons.SelectMany(p => p.Vectors);
            double minX = vectors.Min(p => p.X);
            double maxX = vectors.Max(p => p.X);
            double minY = vectors.Min(p => p.Y);
            double maxY = vectors.Max(p => p.Y);

            // Get the nif dimensions
            var minXProduct = (minX < 0) ? minX * -1 : minX;
            var maxXProduct = (maxX < 0) ? maxX * -1 : maxX;
            var minYProduct = (minY < 0) ? minY * -1 : minY;
            var maxYProduct = (maxY < 0) ? maxY * -1 : maxY;

            var size = new SizeF
                       {
		                       Width = (float)((minXProduct < maxXProduct) ? maxXProduct * 2d : minXProduct * 2d),
		                       Height = (float)((minYProduct < maxYProduct) ? maxYProduct * 2d : minYProduct * 2d)
                       };
            return size;
        }

        public override string ToString()
        {
            return this.NifId.ToString() + ": " + this.TextualName + "(" + this.Filename + ")";
        }
    }
}
