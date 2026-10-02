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
using System.Numerics;

namespace MapCreator.Classes.MapCreation.Fixtures
{
	internal class DrawableFixture
    {
        // Wall tops: faces within about 6 degrees of vertical and at least this tall (zone units)
        private const float WALL_MAX_NORMAL_Z = 0.1f;
        private const double WALL_MIN_HEIGHT = 256;

        // Strip width and how far it sits below the edge, in map pixels
        private const float WALL_TOP_WIDTH = 1.5f;
        private const float WALL_TOP_DEPTH = 0.5f;

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

        /// <summary>
        /// Height of the model's highest point in zone units
        /// </summary>
        public double TopZ;
        public int CanvasWidth;
        public int CanvasHeight;
        public ImageMagick.MagickColor ModelColor;
        public int ModelTransparency;

        public double Scale;

        /// <summary>
        /// Full rotation of a placed model (dungeon pieces), replaces the fixture angle
        /// </summary>
        public Matrix4x4? PlacementRotation;

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

        // Kept out of the canvas size, which would move tiled models by a pixel
        private readonly List<Polygon> wallTops = new List<Polygon>();
        public IEnumerable<DrawableElement> DrawableElements = new List<DrawableElement>();

        public FixtureRendererConfiguration2 RendererConf;

        public bool IsKeepPiece;

        // Other New Frontiers buildings drawn in 3D with the keeps (relic keeps and temples, mile gates, bridges)
        public bool IsStructure;

        // Map units the oblique view shifts a point up the map per map unit of height
        internal const double OBLIQUE_FACTOR = 1.0;

        // Structures stand lower than the keeps in the oblique view; tall bridges and gates looked stretched at the keep factor
        internal const double STRUCTURE_OBLIQUE_FACTOR = 0.75;

        internal bool IsOblique => ((this.IsKeepPiece || this.IsStructure) && this.ZoneConf.ObliqueKeeps) || this.IsObliqueModel;

        internal double ObliqueFactor => this.IsKeepPiece ? OBLIQUE_FACTOR : STRUCTURE_OBLIQUE_FACTOR;

        // Ordinary buildings and trees in 3D (options ObliqueBuildings, ObliqueTrees); their dark maps are authored like city ones
        public bool IsObliqueModel;

        internal double DarkMapScale => this.IsObliqueModel ? FixtureCanvas.DARK_MAP_SCALE : KEEP_DARK_MAP_SCALE;

        /// <summary>
        /// Water surface under the model in map units (NaN on land); parts below it are hidden in the oblique view
        /// </summary>
        public double WaterLevel = double.NaN;

        /// <summary>
        /// Position on the map in the oblique view from the south, same axes as the input (X east, Y north)
        /// </summary>
        internal static Vector2 ProjectOblique(Vector3 v, double factor = OBLIQUE_FACTOR)
        {
            return new Vector2(v.X, (float)(v.Y + factor * v.Z));
        }

        // The viewer is above and south: the direction to the viewer is (0, -factor, 1)
        internal static bool FacesViewer(Vector3 normal, double factor = OBLIQUE_FACTOR)
        {
            return normal.Z - factor * normal.Y > 0;
        }

        // Direction towards the sun: mostly from above, slightly from the south west
        private static readonly Vector3 KeepSun = Vector3.Normalize(new Vector3(-0.3f, -0.45f, 1f));

        internal const double KEEP_AMBIENT = 0.45;

        // Keep lightmaps are authored near white, so they are not boosted like city dark maps
        internal const double KEEP_DARK_MAP_SCALE = 1.0;

        // Brightness relative to a flat top face, which is 1
        internal static double ObliqueLight(Vector3 normal)
        {
            var top = KEEP_AMBIENT + (1 - KEEP_AMBIENT) * Math.Max(0, Vector3.Dot(Vector3.UnitZ, KeepSun));
            var own = KEEP_AMBIENT + (1 - KEEP_AMBIENT) * Math.Max(0, Vector3.Dot(normal, KeepSun));
            return Math.Clamp(own / top, 0, 1);
        }

        // Larger is nearer the viewer; a step towards the viewer along a view ray raises it
        internal static double ObliqueDepth(Vector3 v, double factor = OBLIQUE_FACTOR)
        {
            return -v.Y + factor * v.Z;
        }

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

            var baseZ = this.FixtureRow.OnGround ? this.ZoneConf.Heightmap.GetHeight(this.FixtureRow.X, this.FixtureRow.Y) : this.FixtureRow.Z;
            this.BaseCanvasZ = this.ZoneConf.ZoneCoordinateToMapCoordinate(baseZ);
            this.TopZ = this.RawPolygons.SelectMany(p => p.Vectors).Max(p => p.Z) + baseZ;
            this.CanvasZ = this.ZoneConf.ZoneCoordinateToMapCoordinate(this.TopZ);

            // Transform Polygons
            this.TransformPolygons(baseZ);

            // Within a level the pieces are ordered by the top of what is left of them
            if (this.HeightBand != null && this.ProcessedPolygons.Any())
            {
                this.CanvasZ = this.ZoneConf.ZoneCoordinateToMapCoordinate(baseZ) + this.ProcessedPolygons.SelectMany(p => p.Vectors).Max(p => p.Z);
            }
            return this.GenerateCanvas();
        }

        /// <summary>
        /// Draws a 3D model top-down again
        /// </summary>
        public bool Flatten()
        {
            this.IsObliqueModel = false;
            this.ProcessedPolygons.Clear();
            this.wallTops.Clear();
            return this.Calc();
        }

        private bool GenerateCanvas()
        {
            if (!this.ProcessedPolygons.Any()) return false;

            var oblique = this.IsOblique;
            var vectors = this.ProcessedPolygons.SelectMany(p => p.Vectors);
            if (oblique)
            {
                vectors = vectors.Select(v => new Vector3(v.X, ProjectOblique(v, this.ObliqueFactor).Y, v.Z));
            }
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

            // Wall tops reaching past the rest grow the canvas on both sides by an even pixel count: the rounded
            // position (Convert.ToInt32 rounds half to even) then moves by exactly that much and the model does not shift
            if (this.wallTops.Count > 0)
            {
                var wallVectors = this.wallTops.SelectMany(p => p.Vectors).ToList();
                this.CanvasWidth += 2 * EvenCeiling(wallVectors.Max(p => Math.Abs(p.X)) - this.CanvasWidth / 2d);
                this.CanvasHeight += 2 * EvenCeiling(wallVectors.Max(p => Math.Abs(p.Y)) - this.CanvasHeight / 2d);
            }

            // Contains all polygons
            var drawlist = new List<DrawableElement>();

            foreach (var poly in this.ProcessedPolygons.Concat(this.wallTops))
            {
                var n = Normalize(this.GetNormal(poly.P1, poly.P2, poly.P3));

                // Leaves and some walls (Avalon Isle's ring) are single sheets; seen from behind in 3D a culled sheet leaves a hole
                var tree = this.IsTree || this.IsTreeCluster;
                if (oblique && (tree || this.IsObliqueModel))
                {
                    if (!FacesViewer(n, this.ObliqueFactor))
                    {
                        n = -n;
                    }
                }
                else if (oblique ? !FacesViewer(n, this.ObliqueFactor) : n.Z < 0) continue;

                double lighting;
                if (oblique && !tree)
                {
                    lighting = ObliqueLight(n);
                }
                else
                {
                    // shade
                    double ndotl = this.RendererConf.LightVector.X * n.X + this.RendererConf.LightVector.Y * n.Y + this.RendererConf.LightVector.Z * n.Z;
                    if (ndotl > 0) ndotl = 0;

                    // Lightning must be between 0 and 1, its multiplied with RGB and that must return a ushort
                    lighting = this.RendererConf.LightMin - (this.RendererConf.LightMax - this.RendererConf.LightMin) * ndotl;
                    if (lighting < 0) lighting = 0;
                    else if (lighting > 1) lighting = 1;
                }

                var coordinates = new List<ImageMagick.PointD>();
                var depths = oblique ? poly.Vectors.Select(v => ObliqueDepth(v, this.ObliqueFactor)).ToArray() : poly.Vectors.Select(v => (double)v.Z).ToArray();
                foreach (var vector in poly.Vectors)
                {
                    var shownY = oblique ? ProjectOblique(vector, this.ObliqueFactor).Y : vector.Y;
                    coordinates.Add(new ImageMagick.PointD(this.CanvasWidth / 2 + vector.X, this.CanvasHeight / 2 - shownY));
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
                if (textureColor == null && textureMode != TextureMode.None && !poly.IsAdditive)
                {
                    this.ZoneConf.WarnOnce(string.Format("{0}: no texture or material color, drawn in the {1} color", this.NifName, this.RendererConf.Name));
                }
                var texture2Name = poly.Texture2 != null && this.TextureProxies != null && this.TextureProxies.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(poly.Texture2), out var proxy2) ? proxy2 : poly.Texture2;
                var texture = textureMode == TextureMode.Map && poly.Uvs != null ? TextureCache.Get(textureName, this.TextureDirectory) : null;
                var texture2 = textureMode == TextureMode.Map && poly.Uvs2 != null ? TextureCache.Get(texture2Name, this.TextureDirectory) : null;
                var darkName = poly.DarkTexture != null && this.TextureProxies != null && this.TextureProxies.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(poly.DarkTexture), out var darkProxy) ? darkProxy : poly.DarkTexture;
                var dark = textureMode == TextureMode.Map && poly.DarkUvs != null ? TextureCache.Get(darkName, this.TextureDirectory) : null;
                drawlist.Add(new DrawableElement(maxZ, lighting, coordinates, textureColor, texture, poly.Uvs, texture2, poly.Uvs2, poly.TextureBlend) { Depths = depths, VertexColors = poly.VertexColors, Dark = dark, DarkUvs = poly.DarkUvs, IsWater = poly.IsWater, IsAdditive = poly.IsAdditive, AdditiveColor = poly.MaterialColor, Positions = oblique ? poly.Vectors : null });
            }

            this.DrawableElements = drawlist.OrderBy(o => o.Order);
            this.CanvasX = Convert.ToInt32(this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.X) - this.CanvasWidth / 2d);
            this.CanvasY = Convert.ToInt32(this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.Y) - this.CanvasHeight / 2d);
            this.ModelColor = this.RendererConf.Color;
            this.ModelTransparency = this.RendererConf.Transparency;
            return true;
        }

        // CanvasX/Y are rounded to whole pixels; on a shared canvas that opens gaps between tiled models
        public double ExactCanvasX => this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.X) - this.CanvasWidth / 2;

        public double ExactCanvasY => this.ZoneConf.ZoneCoordinateToMapCoordinate(this.FixtureRow.Y) - this.CanvasHeight / 2;

        private void TransformPolygons(double baseZ)
        {
            this.Scale = ((this.FixtureRow.Scale / 100f) * this.ZoneConf.LocScale);

            var angle = 360d * this.FixtureRow.AxisZ3D - this.FixtureRow.A;

            if (this.ZoneConf.ZoneId == "330" || this.ZoneConf.ZoneId == "334" || this.ZoneConf.ZoneId == "335")
            {
                angle = (360 - this.FixtureRow.A) * this.FixtureRow.AxisZ3D;
            }

            var rotation = Matrix4x4.Identity;
            if (this.PlacementRotation != null)
            {
                rotation = this.PlacementRotation.Value;
            }
            else if (angle != 0)
            {
                rotation = Niflib.NumericsTransform.Multiply(rotation, Matrix4x4.CreateRotationZ(Convert.ToSingle(angle * Math.PI / 180.0)));
            }

            foreach (var poly in this.RawPolygons)
            {
                var p1 = Niflib.NumericsTransform.TransformCoordinate(poly.P1, rotation);
                var p2 = Niflib.NumericsTransform.TransformCoordinate(poly.P2, rotation);
                var p3 = Niflib.NumericsTransform.TransformCoordinate(poly.P3, rotation);

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
                var newPolygon = poly with { Vectors = new[] { p1, p2, p3 } };
                var seen = this.IsOblique
                    ? this.PolygonArea(newPolygon.Vectors.Select(v => new Vector3(ProjectOblique(v, this.ObliqueFactor), 0)).ToArray())
                    : this.PolygonArea(newPolygon.Vectors);
                if (seen > 0.01)
                {
                    this.ProcessedPolygons.Add(newPolygon);
                }
                if (!this.IsOblique && !this.IsTree && !this.IsTreeCluster && !poly.IsWater && !poly.IsAdditive)
                {
                    this.wallTops.AddRange(this.GetWallTop(newPolygon));
                }
            }
        }

        /// <summary>
        /// Walls built as bare vertical sheets (Avalon Isle's city wall) have no top face and vanish from above.
        /// Their top edge becomes a thin strip behind the face, just below the edge, so a real top face or the
        /// ground behind a cliff at the same height still covers it.
        /// </summary>
        private IEnumerable<Polygon> GetWallTop(Polygon polygon)
        {
            var v = polygon.Vectors;
            var normal = Normalize(this.GetNormal(v[0], v[1], v[2]));
            var top = v.Max(p => p.Z);
            var height = top - v.Min(p => p.Z);
            if (Math.Abs(normal.Z) > WALL_MAX_NORMAL_Z || height < this.ZoneConf.ZoneCoordinateToMapCoordinate(WALL_MIN_HEIGHT))
            {
                yield break;
            }
            var upper = Enumerable.Range(0, 3).Where(i => top - v[i].Z <= height * 0.05f).ToArray();
            var side = new Vector2(normal.X, normal.Y);
            if (upper.Length != 2 || side.LengthSquared() < 1e-6f)
            {
                yield break;
            }
            side = Vector2.Normalize(side) * -WALL_TOP_WIDTH;
            var a = v[upper[0]] with { Z = v[upper[0]].Z - WALL_TOP_DEPTH };
            var b = v[upper[1]] with { Z = v[upper[1]].Z - WALL_TOP_DEPTH };
            var offset = new Vector3(side.X, side.Y, 0);
            yield return WallTopTriangle(polygon, new[] { a, b, b + offset }, new[] { upper[0], upper[1], upper[1] });
            yield return WallTopTriangle(polygon, new[] { a, b + offset, a + offset }, new[] { upper[0], upper[1], upper[0] });
        }

        private static int EvenCeiling(double value)
        {
            var ceiling = (int)Math.Ceiling(Math.Max(0, value));
            return ceiling + ceiling % 2;
        }

        // Takes the corner attributes of the wall triangle's top edge; the winding faces up so backface culling keeps it
        private static Polygon WallTopTriangle(Polygon wall, Vector3[] corners, int[] source)
        {
            if (Vector3.Cross(corners[1] - corners[0], corners[2] - corners[1]).Z < 0)
            {
                (corners[1], corners[2]) = (corners[2], corners[1]);
                (source[1], source[2]) = (source[2], source[1]);
            }
            return wall with
            {
                Vectors = corners,
                Uvs = wall.Uvs == null ? null : source.Select(i => wall.Uvs[i]).ToArray(),
                Uvs2 = wall.Uvs2 == null ? null : source.Select(i => wall.Uvs2[i]).ToArray(),
                DarkUvs = wall.DarkUvs == null ? null : source.Select(i => wall.DarkUvs[i]).ToArray(),
                TextureBlend = wall.TextureBlend == null ? null : source.Select(i => wall.TextureBlend[i]).ToArray(),
                VertexColors = wall.VertexColors == null ? null : source.SelectMany(i => wall.VertexColors.Skip(i * 3).Take(3)).ToArray()
            };
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
            var v1 = new Vector3(p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z);
            var v2 = new Vector3(p3.X - p2.X, p3.Y - p2.Y, p3.Z - p2.Z);
            return Vector3.Cross(v1, v2);
        }

        private static Vector3 Normalize(Vector3 value)
        {
            var length = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
            if (Math.Abs(length) < 1e-6f)
            {
                return value;
            }

            var inverse = 1f / length;
            value.X *= inverse;
            value.Y *= inverse;
            value.Z *= inverse;
            return value;
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
        /// Corners relative to the placement (X east, Y north, Z up, map units) for the terrain test of the oblique view, null otherwise
        /// </summary>
        public Vector3[] Positions;

        /// <summary>
        /// Baked lighting of the corners as r, g, b each, multiplied into the color
        /// </summary>
        public float[] VertexColors;

        /// <summary>
        /// Dark map (baked lighting) multiplied into the color, null if the mesh has none
        /// </summary>
        public TextureImage Dark;

        public Vector2[] DarkUvs;

        /// <summary>
        /// Water surface, drawn after the rest and only over it
        /// </summary>
        public bool IsWater;

        /// <summary>
        /// Glow that adds AdditiveColor (0xRRGGBB) times its texture to what lies below
        /// </summary>
        public bool IsAdditive;

        public int AdditiveColor;

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
