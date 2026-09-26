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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MapCreator.Classes.MapCreation.Fixtures.Objects;
using NifUtil.Objects;
using SharpDX;

namespace MapCreator.Classes.MapCreation.Fixtures
{
	internal class DrawableFixture
    {
        public string Name;
        public string NifName;
        public string TextureDirectory;

        public FixtureRow FixtureRow;

        public double CanvasX;
        public double CanvasY;
        public double CanvasZ;

        /// <summary>
        /// Placement height in map units; element depths are relative to it
        /// </summary>
        public double BaseCanvasZ;
        public int CanvasWidth;
        public int CanvasHeight;
        public ImageMagick.MagickColor ModelColor;
        public int ModelTransparency;

        public double Scale;

        /// <summary>
        /// Full rotation of a placed model (dungeon pieces), replaces the fixture angle
        /// </summary>
        public Matrix? PlacementRotation;

        /// <summary>
        /// Keeps only triangles whose mean height lies in (Bottom, Top], in placement heights. Null keeps all.
        /// </summary>
        public (double Bottom, double Top)? HeightBand;

        /// <summary>
        /// Textures the zone replaces, by file name without extension
        /// </summary>
        public IReadOnlyDictionary<string, string> TextureProxies;

        public IEnumerable<Polygon> RawPolygons;
        public readonly List<Polygon> ProcessedPolygons = new List<Polygon>();
        public IEnumerable<DrawableElement> DrawableElements = new List<DrawableElement>();

        public FixtureRendererConfiguration2 RendererConf;

        public bool IsTree = false; // Trees need some extra love
        public TreeRow Tree;
        public bool IsTreeCluster = false; // TreeCluster at all
        public TreeClusterRow TreeCluster;

        #region Getter/Setter

        public ZoneConfiguration ZoneConf { get; set; }
        #endregion

        public bool Calc()
        {
            // Do nothig if we don't want to draw the nif
            if (this.RendererConf.Renderer == FixtureRendererType.None)
            {
                return false;
            }

            // Do nothing is there are no polgons
            if (!this.RawPolygons.Any())
            {
                return false;
            }

            // Calculate X, Y, Z on the map canvas
            //CanvasX = ZoneConf.LocToPixel(FixtureRow.X);
            //CanvasY = ZoneConf.LocToPixel(FixtureRow.Y);

            // Calculate correct Z
            if (this.FixtureRow.OnGround)
            {
                this.FixtureRow.Z = this.ZoneConf.Heightmap.GetHeight(this.FixtureRow.X, this.FixtureRow.Y);
            }
            var baseZ = this.FixtureRow.Z;
            this.BaseCanvasZ = this.ZoneConf.ZoneCoordinateToMapCoordinate(baseZ);
            this.FixtureRow.Z = this.RawPolygons.SelectMany(p => p.Vectors).Max(p => p.Z) + this.FixtureRow.Z;
            this.CanvasZ = this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.Z);

            // Transform Polygons
            this.TransformPolygons(baseZ);

            // Within a level the pieces are ordered by the top of what is left of them
            if (this.HeightBand != null && this.ProcessedPolygons.Any())
            {
                this.CanvasZ = this.ZoneConf.ZoneCoordinateToMapCoordinate(baseZ) + this.ProcessedPolygons.SelectMany(p => p.Vectors).Max(p => p.Z);
            }
            return this.GenerateCanvas();
        }

        private bool GenerateCanvas()
        {
            if (!this.ProcessedPolygons.Any()) return false;

            var vectors = this.ProcessedPolygons.SelectMany(p => p.Vectors);
            double minX = vectors.Min(p => p.X);
            double maxX = vectors.Max(p => p.X);
            double minY = vectors.Min(p => p.Y);
            double maxY = vectors.Max(p => p.Y);

            // Get the canvas size
            var minXProduct = (minX < 0) ? minX * -1 : minX;
            var maxXProduct = (maxX < 0) ? maxX * -1 : maxX;
            var minYProduct = (minY < 0) ? minY * -1 : minY;
            var maxYProduct = (maxY < 0) ? maxY * -1 : maxY;
            this.CanvasWidth = Convert.ToInt32((minXProduct < maxXProduct) ? maxXProduct * 2f : minXProduct * 2f);
            this.CanvasHeight = Convert.ToInt32((minYProduct < maxYProduct) ? maxYProduct * 2f : minYProduct * 2f);

            if(this.CanvasWidth <= 0 || this.CanvasHeight <= 0)
            {
                return false;
            }


            // Contains all polygons
            var drawlist = new List<DrawableElement>();

            foreach (var poly in this.ProcessedPolygons)
            {
                var n = Vector3.Normalize(this.GetNormal(poly.P1, poly.P2, poly.P3));

                // backface cull
                if (n[2] < 0) continue;

                // shade
                double ndotl = this.RendererConf.LightVector[0] * n[0] + this.RendererConf.LightVector[1] * n[1] + this.RendererConf.LightVector[2] * n[2];
                if (ndotl > 0) ndotl = 0;

                // Lightning must be between 0 and 1, its multiplied with RGB and that must return a ushort
                var lighting = this.RendererConf.LightMin - (this.RendererConf.LightMax - this.RendererConf.LightMin) * ndotl;
                if (lighting < 0) lighting = 0;
                else if (lighting > 1) lighting = 1;

                var coordinates = new List<ImageMagick.PointD>();
                var depths = poly.Vectors.Select(v => (double)v.Z).ToArray();
                foreach (var vector in poly.Vectors)
                {
                    coordinates.Add(new ImageMagick.PointD(this.CanvasWidth / 2 + vector.X, this.CanvasHeight / 2 - vector.Y));
                }

                // We want to draw the vectors in z-order
                double maxZ = poly.Vectors.Max(p => p.Z);
                var textureMode = this.RendererConf.Texture;
                var textureName = poly.Texture != null && this.TextureProxies != null && this.TextureProxies.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(poly.Texture), out var proxy) ? proxy : poly.Texture;
                var textureColor = textureMode != TextureMode.None ? TextureColors.Get(textureName, this.TextureDirectory) : null;
                if (textureColor == null && poly.MaterialColor >= 0)
                {
                    textureColor = System.Drawing.Color.FromArgb(255, (poly.MaterialColor >> 16) & 0xFF, (poly.MaterialColor >> 8) & 0xFF, poly.MaterialColor & 0xFF);
                }
                var texture2Name = poly.Texture2 != null && this.TextureProxies != null && this.TextureProxies.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(poly.Texture2), out var proxy2) ? proxy2 : poly.Texture2;
                var texture = textureMode == TextureMode.Map && poly.Uvs != null ? TextureCache.Get(textureName, this.TextureDirectory) : null;
                var texture2 = textureMode == TextureMode.Map && poly.Uvs2 != null ? TextureCache.Get(texture2Name, this.TextureDirectory) : null;
                drawlist.Add(new DrawableElement(maxZ, lighting, coordinates, textureColor, texture, poly.Uvs, texture2, poly.Uvs2, poly.TextureBlend) { Depths = depths, VertexColors = poly.VertexColors });
            }

            this.DrawableElements = drawlist.OrderBy(o => o.Order);
            this.CanvasX = Convert.ToInt32(this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.X) - this.CanvasWidth / 2d);
            this.CanvasY = Convert.ToInt32(this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.Y) - this.CanvasHeight / 2d);
            this.ModelColor = this.RendererConf.Color;
            this.ModelTransparency = this.RendererConf.Transparency;
            return true;
        }

        private void TransformPolygons(double baseZ)
        {
            this.Scale = ((this.FixtureRow.Scale / 100f) * this.ZoneConf.LocScale);

            var angle = 360d * this.FixtureRow.AxisZ3D - this.FixtureRow.A;

            if (this.ZoneConf.ZoneId == "330" || this.ZoneConf.ZoneId == "334" || this.ZoneConf.ZoneId == "335")
            {
                angle = (360 - this.FixtureRow.A) * this.FixtureRow.AxisZ3D;
            }

            var rotation = Matrix.Identity;
            if (this.PlacementRotation != null)
            {
                rotation = this.PlacementRotation.Value;
            }
            else if (angle != 0)
            {
                rotation *= Matrix.RotationZ(Convert.ToSingle(angle * Math.PI / 180.0));
            }

            foreach (var poly in this.RawPolygons)
            {
                var p1 = Vector3.TransformCoordinate(poly.P1, rotation);
                var p2 = Vector3.TransformCoordinate(poly.P2, rotation);
                var p3 = Vector3.TransformCoordinate(poly.P3, rotation);

                if (this.HeightBand is var (bottom, top))
                {
                    var height = baseZ + (p1.Z + p2.Z + p3.Z) / 3d;
                    if (height <= bottom || height > top)
                    {
                        continue;
                    }
                }

                if (this.Scale != 1)
                {
                    p1.X *= (float)this.Scale;
                    p1.Y *= (float)this.Scale;
                    p1.Z *= (float)this.Scale;

                    p2.X *= (float)this.Scale;
                    p2.Y *= (float)this.Scale;
                    p2.Z *= (float)this.Scale;

                    p3.X *= (float)this.Scale;
                    p3.Y *= (float)this.Scale;
                    p3.Z *= (float)this.Scale;
                }

                // Check visibility of polygons
                var newPolygon = new Polygon(p1, p2, p3, poly.Texture, poly.Uvs) { Texture2 = poly.Texture2, Uvs2 = poly.Uvs2, TextureBlend = poly.TextureBlend, VertexColors = poly.VertexColors, MaterialColor = poly.MaterialColor };
                if (this.PolygonArea(newPolygon.Vectors) > 0.01)
                {
                    this.ProcessedPolygons.Add(newPolygon);
                }
            }
        }

        /// <summary>
        /// Gets the area of a polygon
        /// </summary>
        /// <param name="polygon"></param>
        /// <returns></returns>
        private double PolygonArea(Vector3[] polygon)
        {
            int i, j;
            double area = 0;

            for (i = 0; i < polygon.Length; i++)
            {
                j = (i + 1) % polygon.Length;
                area += polygon[i].X * polygon[j].Y;
                area -= polygon[i].Y * polygon[j].X;
            }

            area /= 2.0;
            return (area < 0 ? -area : area);
        }

        /// <summary>
        /// Get the normals of a set of Vectors3 for lighning
        /// </summary>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <param name="p3"></param>
        /// <returns></returns>
        private Vector3 GetNormal(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var v1 = new Vector3(p2[0] - p1[0], p2[1] - p1[1], p2[2] - p1[2]);
            var v2 = new Vector3(p3[0] - p2[0], p3[1] - p2[1], p3[2] - p2[2]);
            return Vector3.Cross(v1, v2);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1})", this.Name, this.NifName);
        }
    }

	internal struct DrawableElement : IEnumerable
    {
        public readonly double Order;
        public readonly double Lightning;
        public readonly IEnumerable<ImageMagick.PointD> Coordinates;

        /// <summary>
        /// Average color of the triangle's texture or its material color, null to use the renderer color
        /// </summary>
        public readonly System.Drawing.Color? TextureColor;

        /// <summary>
        /// Texture to map with Uvs, null to fill with a color
        /// </summary>
        public readonly TextureImage Texture;

        public readonly Vector2[] Uvs;
        public readonly TextureImage Texture2;
        public readonly Vector2[] Uvs2;
        public readonly float[] TextureBlend;

        /// <summary>
        /// Height of each corner in map units, for the depth test
        /// </summary>
        public double[] Depths;

        /// <summary>
        /// Baked lighting of the corners as r, g, b each, multiplied into the color
        /// </summary>
        public float[] VertexColors;

        public DrawableElement(double order, double lightning, IEnumerable<ImageMagick.PointD> coordinates, System.Drawing.Color? textureColor = null, TextureImage texture = null, Vector2[] uvs = null, TextureImage texture2 = null, Vector2[] uvs2 = null, float[] textureBlend = null)
        {
            this.Order = order;
            this.Lightning = lightning;
            this.Coordinates = coordinates;
            this.TextureColor = textureColor;
            this.Texture = texture;
            this.Uvs = uvs;
            this.Texture2 = texture2;
            this.Uvs2 = uvs2;
            this.TextureBlend = textureBlend;
        }

        public IEnumerator GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
