//
// MapCreator NifUtil Library
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

using System.Collections.Generic;
using System.IO;
using Niflib;
using NifUtil.Objects;
using SharpDX;

namespace NifUtil.Classes
{
	internal class ConvertPoly : Convert
    {
	    internal List<Polygon> Polys { get; set; } = new List<Polygon>();

        public ConvertPoly(NiFile niFile)
            :base(niFile)
        {
          
        }

        public void Start()
        {
            this.WalkNodes(this.File.FindRoot());
        }

        private void WalkNodes(NiAVObject node)
        {
            if (!this.IsValidNode(node)) return;

            // Render Children
            if (node is NiTriShape shape)
            {
                this.ParseShape(shape);
            }
            else if (node is NiTriStrips strips)
            {
                this.ParseStrips(strips);
            }

            var currentNode = node as NiNode;
            if (currentNode != null)
            {
                if (currentNode.Children.Length > 0)
                {

                    foreach (var child in currentNode.Children)
                    {
                        if (child.IsValid())
                        {
                            this.WalkNodes(child.Object);
                        }
                    }
                }
            }
        }

        private void ParseShape(NiTriShape shape)
        {
            var export = new List<string>();

            var geometry = (NiTriShapeData)shape.Data.Object;

            // Verticles (v)
            if (geometry.HasVertices && geometry.NumVertices >= 3)
            {
                var transformationMatrix = this.ComputeWorldMatrix(shape);
                this.ComputePolys(geometry.Triangles, geometry.Vertices, transformationMatrix);
            }
        }

        private void ParseStrips(NiTriStrips strips)
        {
            var export = new List<string>();

            var geometry = (NiTriStripsData)strips.Data.Object;

            var triangles = new List<Triangle>();
            foreach (var points in geometry.Points)
            {
                var t = false;
                var j = 1;

                var p1 = points[0];
                var p2 = points[1];

                while (j < points.Length - 1)
                {
                    var p3 = points[j + 1];

                    if (p1 != p2 && p1 != p3 && p2 != p3)
                    {
                        if (t)
                        {
                            triangles.Add(new Triangle(p1, p3, p2));
                        }
                        else
                        {
                            triangles.Add(new Triangle(p1, p2, p3));
                        }
                    }

                    j = j + 1;
                    p1 = p2;
                    p2 = p3;
                    t = !t;
                }
            }

            // Verticles (v)
            if (geometry.HasVertices && geometry.NumVertices >= 3)
            {
                var transformationMatrix = this.ComputeWorldMatrix(strips);
                this.ComputePolys(triangles.ToArray(), geometry.Vertices, transformationMatrix);
            }
        }

        private void ComputePolys(Triangle[] trianlges, Vector3[] vertices, Matrix transformation)
        {
            // Transaform all vertices
            var verticesTransformed = new List<Vector3>();
            foreach (var vector in vertices) verticesTransformed.Add(Vector3.TransformCoordinate(vector, transformation));

            foreach (var triangle in trianlges)
            {
                var poly = new Polygon(
                                       new Vector3(verticesTransformed[triangle.X].X, verticesTransformed[triangle.X].Y, verticesTransformed[triangle.X].Z),
                                       new Vector3(verticesTransformed[triangle.Y].X, verticesTransformed[triangle.Y].Y, verticesTransformed[triangle.Y].Z),
                                       new Vector3(verticesTransformed[triangle.Z].X, verticesTransformed[triangle.Z].Y, verticesTransformed[triangle.Z].Z)
                                      );
                this.Polys.Add(poly);
            }
        }

        /// <summary>
        /// Writes a plain version of the poly
        /// </summary>
        /// <param name="targetFile"></param>
        public void WritePlain(string targetFile)
        {
            using (var writer = new StreamWriter(targetFile))
            {
                foreach (var poly in this.Polys)
                {
                    writer.WriteLine(string.Format("{0} {1} {2}", poly.P1.X, poly.P1.Y, poly.P1.Z));
                    writer.WriteLine(string.Format("{0} {1} {2}", poly.P2.X, poly.P2.Y, poly.P2.Z));
                    writer.WriteLine(string.Format("{0} {1} {2}", poly.P3.X, poly.P3.Y, poly.P3.Z));
                    writer.WriteLine();
                }
            }
        }

        public override void Write(string targetFile)
        {
            using (var fs = new FileStream(targetFile, FileMode.Create))
            {
                using (var writer = new BinaryWriter(fs))
                {
                    foreach (var poly in this.Polys)
                    {
                        writer.Write(poly.P1.X);
                        writer.Write(poly.P1.Y);
                        writer.Write(poly.P1.Z);

                        writer.Write(poly.P2.X);
                        writer.Write(poly.P2.Y);
                        writer.Write(poly.P2.Z);

                        writer.Write(poly.P3.X);
                        writer.Write(poly.P3.Y);
                        writer.Write(poly.P3.Z);
                    }
                }
            }
        }

    }
}
