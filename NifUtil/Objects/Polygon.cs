//
// MapCreator NifUtil Library
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

using System.Numerics;

namespace NifUtil.Objects
{
    public struct Polygon
    {
        public Vector3 P1 => this.Vectors[0];

        public Vector3 P2 => this.Vectors[1];

        public Vector3 P3 => this.Vectors[2];

        public Vector3[] Vectors { get; set; }

        /// <summary>
        /// File name of the base texture as stored in the NIF, null if the mesh has none
        /// </summary>
        public string Texture { get; set; }

        public string Texture2 { get; set; }

        /// <summary>
        /// Texture coordinates of the three corners, null if the mesh has none
        /// </summary>
        public Vector2[] Uvs { get; set; }

        public Vector2[] Uvs2 { get; set; }

        public float[] TextureBlend { get; set; }

        /// <summary>
        /// Dark map (baked lighting texture) multiplied into the color, with its own texture coordinates
        /// </summary>
        public string DarkTexture { get; set; }

        public Vector2[] DarkUvs { get; set; }

        /// <summary>
        /// Vertex colors (baked lighting) of the three corners as r, g, b each, null if the mesh has none
        /// </summary>
        public float[] VertexColors { get; set; }

        /// <summary>
        /// Diffuse material color as 0xRRGGBB, -1 if the mesh has none
        /// </summary>
        public int MaterialColor { get; set; }

        public Polygon(Vector3 p1, Vector3 p2, Vector3 p3, string texture = null, Vector2[] uvs = null)
        {
            this.Vectors = new Vector3[3];
            this.Vectors.SetValue(p1, 0);
            this.Vectors.SetValue(p2, 1);
            this.Vectors.SetValue(p3, 2);
            this.Texture = texture;
            this.Uvs = uvs;
            this.Texture2 = null;
            this.Uvs2 = null;
            this.TextureBlend = null;
            this.DarkTexture = null;
            this.DarkUvs = null;
            this.MaterialColor = -1;
        }
    }
}
