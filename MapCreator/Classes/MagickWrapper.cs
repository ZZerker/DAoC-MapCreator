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

using System.Drawing;
using ImageMagick;

namespace MapCreator
{
    static class MagickWrapper
    {
        public static MagickColor ToMagickColor(this Color color)
        {
            // The MagickColor constructor expects 16 bit channels
            return MagickColor.FromRgba(color.R, color.G, color.B, color.A);
        }

        public static Color ToSystemColor(this IMagickColor<ushort> color)
        {
            return Color.FromArgb(color.A / 257, color.R / 257, color.G / 257, color.B / 257);
        }

        public static MagickImage NewImage(IMagickColor<ushort> color, int width, int height)
        {
            return new MagickImage(color, (uint)width, (uint)height);
        }

        public static MagickImage NewImage(Color color, int width, int height)
        {
            return NewImage(color.ToMagickColor(), width, height);
        }
    }
}
