using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;
using System.Numerics;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Draws textured triangles of one model into a pixel buffer. ImageMagick cannot map textures onto triangles.
    /// </summary>
    internal sealed class FixtureCanvas
    {
        // Rendered at a higher resolution and scaled down, for smooth edges
        private const int SUPER_SAMPLING = 2;

        private const int ALPHA_THRESHOLD = 128;

        // Dark maps are authored far below white (medians 40 to 140); 1.5 keeps roofs from going black without bleaching cities
        private const double DARK_MAP_SCALE = 1.5;

        private readonly int width;
        private readonly int height;
        private readonly int bufferWidth;
        private readonly int bufferHeight;
        private readonly byte[] pixels;

        // Highest surface drawn so far per pixel; overlapping parts (ramps, bridges) keep the top one
        private readonly float[] depth;

        // Terrain height per map pixel in map units; parts of models below it are hidden like in the game
        private readonly TerrainHeights terrain;
        private readonly double terrainOriginX;
        private readonly double terrainOriginY;

        public FixtureCanvas(int width, int height, TerrainHeights terrain = null, double terrainOriginX = 0, double terrainOriginY = 0)
        {
            this.width = width;
            this.height = height;
            this.bufferWidth = width * SUPER_SAMPLING;
            this.bufferHeight = height * SUPER_SAMPLING;
            this.pixels = new byte[this.bufferWidth * this.bufferHeight * 4];
            this.depth = new float[this.bufferWidth * this.bufferHeight];
            Array.Fill(this.depth, float.MinValue);
            this.terrain = terrain;
            this.terrainOriginX = terrainOriginX;
            this.terrainOriginY = terrainOriginY;
        }

        /// <summary>
        /// Fills a triangle with its texture, or with the color if there is no texture. Light scales the color.
        /// Depths are corner heights; the offsets place a model's canvas coordinates on a shared canvas.
        /// </summary>
        public void FillTriangle(IEnumerable<PointD> coordinates, Vector2[] uvs, TextureImage texture, MagickColor color, double light, Vector2[] uvs2 = null, TextureImage texture2 = null, float[] textureBlend = null,
                                 double[] depths = null, double offsetX = 0, double offsetY = 0, double depthOffset = 0, float[] vertexColors = null, TextureImage dark = null, Vector2[] darkUvs = null, bool isWater = false, int additiveColor = -1, Vector3[] positions = null, double darkScale = DARK_MAP_SCALE, OcclusionMap occlusion = null, double floorZ = double.MinValue)
        {
            var points = coordinates.Select(p => new PointD((p.X + offsetX) * SUPER_SAMPLING, (p.Y + offsetY) * SUPER_SAMPLING)).ToArray();
            if (points.Length != 3)
            {
                return;
            }

            var area = Edge(points[0], points[1], points[2]);
            if (Math.Abs(area) < 1e-9)
            {
                return;
            }

            var textured = texture != null && uvs != null;
            var texelsPerPixel = 0d;
            if (textured)
            {
                var uvArea = Math.Abs((uvs[1].X - uvs[0].X) * (uvs[2].Y - uvs[0].Y) - (uvs[2].X - uvs[0].X) * (uvs[1].Y - uvs[0].Y)) * texture.Width * texture.Height;
                texelsPerPixel = Math.Sqrt(uvArea / Math.Abs(area));
            }

            var darkened = textured && dark != null && darkUvs != null;
            var darkTexelsPerPixel = 0d;
            if (darkened)
            {
                var darkUvArea = Math.Abs((darkUvs[1].X - darkUvs[0].X) * (darkUvs[2].Y - darkUvs[0].Y) - (darkUvs[2].X - darkUvs[0].X) * (darkUvs[1].Y - darkUvs[0].Y)) * dark.Width * dark.Height;
                darkTexelsPerPixel = Math.Sqrt(darkUvArea / Math.Abs(area));
            }

            var solidR = color.R / 257d * light;
            var solidG = color.G / 257d * light;
            var solidB = color.B / 257d * light;

            var minX = Math.Max(0, (int)Math.Floor(points.Min(p => p.X)));
            var maxX = Math.Min(this.bufferWidth - 1, (int)Math.Ceiling(points.Max(p => p.X)));
            var minY = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
            var maxY = Math.Min(this.bufferHeight - 1, (int)Math.Ceiling(points.Max(p => p.Y)));

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new PointD(x + 0.5, y + 0.5);
                    var w0 = Edge(points[1], points[2], p) / area;
                    var w1 = Edge(points[2], points[0], p) / area;
                    var w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                    {
                        continue;
                    }

                    var index = (y * this.bufferWidth + x) * 4;
                    var z = depths == null ? 0 : (float)(w0 * depths[0] + w1 * depths[1] + w2 * depths[2] + depthOffset);
                    if (depths != null && z < this.depth[index / 4])
                    {
                        continue;
                    }

                    // Water proxies are bigger than their basin, outside of it they would lie over nothing
                    if (isWater && this.pixels[index + 3] == 0)
                    {
                        continue;
                    }

                    if (depths != null && this.terrain != null)
                    {
                        // Real absolute positions (map column, map row, height) when depth and pixel are not the ground position
                        var hidden = positions != null
                            ? this.terrain.IsBelow(w0 * positions[0].X + w1 * positions[1].X + w2 * positions[2].X, w0 * positions[0].Y + w1 * positions[1].Y + w2 * positions[2].Y, w0 * positions[0].Z + w1 * positions[1].Z + w2 * positions[2].Z)
                            : this.terrain.IsBelow((x + 0.5) / SUPER_SAMPLING + this.terrainOriginX, (y + 0.5) / SUPER_SAMPLING + this.terrainOriginY, z);
                        if (hidden)
                        {
                            continue;
                        }
                    }

                    // Below the water surface (bridge pillars in a river)
                    if (positions != null && w0 * positions[0].Z + w1 * positions[1].Z + w2 * positions[2].Z < floorZ)
                    {
                        continue;
                    }

                    if (additiveColor >= 0)
                    {
                        double gr = 255, gg = 255, gb = 255, ga = 255;
                        if (textured)
                        {
                            texture.Sample(w0 * uvs[0].X + w1 * uvs[1].X + w2 * uvs[2].X, w0 * uvs[0].Y + w1 * uvs[1].Y + w2 * uvs[2].Y, texelsPerPixel, out gr, out gg, out gb, out ga);
                        }
                        this.AddLight(index, gr * ga / 255d * ((additiveColor >> 16) & 0xFF) / 255d, gg * ga / 255d * ((additiveColor >> 8) & 0xFF) / 255d, gb * ga / 255d * (additiveColor & 0xFF) / 255d);
                        continue;
                    }

                    double r = solidR, g = solidG, b = solidB;
                    if (textured)
                    {
                        var u = w0 * uvs[0].X + w1 * uvs[1].X + w2 * uvs[2].X;
                        var v = w0 * uvs[0].Y + w1 * uvs[1].Y + w2 * uvs[2].Y;
                        texture.Sample(u, v, texelsPerPixel, out r, out g, out b, out var a);
                        if (texture2 != null && uvs2 != null && textureBlend != null)
                        {
                            var u2 = w0 * uvs2[0].X + w1 * uvs2[1].X + w2 * uvs2[2].X;
                            var v2 = w0 * uvs2[0].Y + w1 * uvs2[1].Y + w2 * uvs2[2].Y;
                            texture2.Sample(u2, v2, texelsPerPixel, out var r2, out var g2, out var b2, out var a2);
                            // Vertex alpha is the weight of the first layer, unpainted vertices keep the default 1
                            var blend = 1 - Math.Clamp(w0 * textureBlend[0] + w1 * textureBlend[1] + w2 * textureBlend[2], 0, 1);
                            r += (r2 - r) * blend;
                            g += (g2 - g) * blend;
                            b += (b2 - b) * blend;
                            a += (a2 - a) * blend;
                        }

                        // Leaves and fences are cut out by the alpha channel
                        if (a < ALPHA_THRESHOLD)
                        {
                            continue;
                        }

                        if (darkened)
                        {
                            var du = w0 * darkUvs[0].X + w1 * darkUvs[1].X + w2 * darkUvs[2].X;
                            var dv = w0 * darkUvs[0].Y + w1 * darkUvs[1].Y + w2 * darkUvs[2].Y;
                            dark.Sample(du, dv, darkTexelsPerPixel, out var dr, out var dg, out var db, out _);
                            r *= dr * darkScale / 255d;
                            g *= dg * darkScale / 255d;
                            b *= db * darkScale / 255d;
                        }

                        r *= light;
                        g *= light;
                        b *= light;
                    }

                    if (vertexColors != null)
                    {
                        r *= w0 * vertexColors[0] + w1 * vertexColors[3] + w2 * vertexColors[6];
                        g *= w0 * vertexColors[1] + w1 * vertexColors[4] + w2 * vertexColors[7];
                        b *= w0 * vertexColors[2] + w1 * vertexColors[5] + w2 * vertexColors[8];
                    }

                    if (occlusion != null && positions != null)
                    {
                        var factor = occlusion.At((int)(w0 * positions[0].X + w1 * positions[1].X + w2 * positions[2].X), (int)(w0 * positions[0].Y + w1 * positions[1].Y + w2 * positions[2].Y),
                                                  w0 * positions[0].Z + w1 * positions[1].Z + w2 * positions[2].Z);
                        r *= factor;
                        g *= factor;
                        b *= factor;
                    }

                    if (depths != null)
                    {
                        this.depth[index / 4] = z;
                    }
                    this.pixels[index] = ToByte(r);
                    this.pixels[index + 1] = ToByte(g);
                    this.pixels[index + 2] = ToByte(b);
                    this.pixels[index + 3] = 255;
                }
            }
        }

        // Glows add to what is drawn; over nothing they become a glow whose opacity is its brightness
        private void AddLight(int index, double r, double g, double b)
        {
            if (this.pixels[index + 3] == 0)
            {
                this.pixels[index] = ToByte(r);
                this.pixels[index + 1] = ToByte(g);
                this.pixels[index + 2] = ToByte(b);
                this.pixels[index + 3] = ToByte(Math.Max(r, Math.Max(g, b)));
                return;
            }
            this.pixels[index] = ToByte(this.pixels[index] + r);
            this.pixels[index + 1] = ToByte(this.pixels[index + 1] + g);
            this.pixels[index + 2] = ToByte(this.pixels[index + 2] + b);
        }

        public MagickImage ToImage()
        {
            var image = new MagickImage(this.pixels, new PixelReadSettings((uint)this.bufferWidth, (uint)this.bufferHeight, StorageType.Char, PixelMapping.RGBA));
            image.FilterType = FilterType.Box;
            image.Resize(new MagickGeometry((uint)this.width, (uint)this.height) { IgnoreAspectRatio = true });
            return image;
        }

        public int CoveredPixels()
        {
            var count = 0;
            for (var i = 3; i < this.pixels.Length; i += 4)
            {
                if (this.pixels[i] != 0)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Ambient occlusion per sample: each drawn sample is compared by its own depth with the blurred heights of its map pixel (NaN = unchanged).
        /// Per sample, because walls seen edge-on cover single sample columns and would lend their height to the floor beside them.
        /// </summary>
        public void Darken(float[] blurred, double fullHeight, double strength)
        {
            Parallel.For(0, this.bufferHeight, y =>
            {
                for (var x = 0; x < this.bufferWidth; x++)
                {
                    var sample = y * this.bufferWidth + x;
                    var around = blurred[y / SUPER_SAMPLING * this.width + x / SUPER_SAMPLING];
                    if (this.pixels[sample * 4 + 3] == 0 || this.depth[sample] == float.MinValue || float.IsNaN(around))
                    {
                        continue;
                    }

                    var factor = HeightOcclusion.Factor(around, this.depth[sample], fullHeight, strength);
                    for (var c = 0; c < 3; c++)
                    {
                        this.pixels[sample * 4 + c] = (byte)(this.pixels[sample * 4 + c] * factor + 0.5);
                    }
                }
            });
        }

        /// <summary>
        /// Highest surface per map pixel in map units, NaN where nothing was drawn
        /// </summary>
        public float[] ToHeights()
        {
            var heights = new float[this.width * this.height];
            for (var y = 0; y < this.height; y++)
            {
                for (var x = 0; x < this.width; x++)
                {
                    var top = float.NaN;
                    for (var sy = 0; sy < SUPER_SAMPLING; sy++)
                    {
                        for (var sx = 0; sx < SUPER_SAMPLING; sx++)
                        {
                            var sample = (y * SUPER_SAMPLING + sy) * this.bufferWidth + x * SUPER_SAMPLING + sx;
                            // Glows over nothing have no depth
                            if (this.pixels[sample * 4 + 3] != 0 && this.depth[sample] != float.MinValue && !(top >= this.depth[sample]))
                            {
                                top = this.depth[sample];
                            }
                        }
                    }
                    heights[y * this.width + x] = top;
                }
            }
            return heights;
        }

        private static double Edge(PointD a, PointD b, PointD p)
        {
            return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Clamp(Math.Round(value), 0, 255);
        }
    }
}
