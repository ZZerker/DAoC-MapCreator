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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Niflib;
using NifUtil.Objects;
using SharpDX;

namespace NifUtil.Classes
{
	internal class ConvertPoly : Convert
    {
	    private readonly IReadOnlyDictionary<uint, ShaderTexture[]> shaderTextures;

	    internal List<Polygon> Polys { get; set; } = new List<Polygon>();

	    internal Func<string, string, string> ResolveTexture { get; set; }

        public ConvertPoly(NiFile niFile, byte[] nifData)
            :base(niFile)
        {
            this.shaderTextures = ReadShaderTextures(niFile, nifData);
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
                var textures = this.ApplyTextureResolver(shape, this.GetTextures(shape));
                this.ComputePolys(geometry.Triangles, geometry.Vertices, transformationMatrix, textures.Texture1?.Name, GetUvSet(geometry, textures.Texture1), textures.Texture2?.Name, GetUvSet(geometry, textures.Texture2), GetBlend(geometry, textures.Texture2), this.GetMaterialColor(shape));
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
                var textures = this.ApplyTextureResolver(strips, this.GetTextures(strips));
                this.ComputePolys(triangles.ToArray(), geometry.Vertices, transformationMatrix, textures.Texture1?.Name, GetUvSet(geometry, textures.Texture1), textures.Texture2?.Name, GetUvSet(geometry, textures.Texture2), GetBlend(geometry, textures.Texture2), this.GetMaterialColor(strips));
            }
        }

        private sealed record BaseTexture(string Name, int UvSetIndex);

        private sealed record TextureLayers(BaseTexture Texture1, BaseTexture Texture2);

        private sealed record ShaderTexture(uint SourceRef, int UvSetIndex, uint MapId);

        private TextureLayers GetTextures(NiAVObject node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                foreach (var property in current.Properties)
                {
                    if (!property.IsValid() || property.Object is not NiTexturingProperty texturing)
                    {
                        continue;
                    }

                    if (texturing.BaseTexture?.Source != null
                        && this.File.ObjectsByRef.TryGetValue(texturing.BaseTexture.Source.RefId, out var source)
                        && source is NiSourceTexture sourceTexture && sourceTexture.FileName != null)
                    {
                        return new TextureLayers(new BaseTexture(sourceTexture.FileName.ToString(), (int)texturing.BaseTexture.UVSetIndex), null);
                    }

                    if (this.shaderTextures.TryGetValue(property.RefId, out var maps)
                        && TryGetIndex(node, "Texture1Index", out var first)
                        && TryGetIndex(node, "Texture2Index", out var second))
                    {
                        return new TextureLayers(this.Resolve(maps, first), this.Resolve(maps, second));
                    }

                    if (texturing.NumShaderTextures > 0
                        && this.File.ObjectsByRef.TryGetValue(property.RefId + 1, out var next)
                        && next is NiSourceTexture shaderTexture && shaderTexture.FileName != null)
                    {
                        return new TextureLayers(new BaseTexture(shaderTexture.FileName.ToString(), 0), null);
                    }
                }
            }
            return new TextureLayers(null, null);
        }

        private TextureLayers ApplyTextureResolver(NiAVObject node, TextureLayers textures)
        {
            if (this.ResolveTexture == null || textures.Texture1 == null)
            {
                return textures;
            }
            var name = this.ResolveTexture(GetMaterialName(node), textures.Texture1.Name);
            return textures with { Texture1 = textures.Texture1 with { Name = name } };
        }

        private static string GetMaterialName(NiAVObject node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                foreach (var property in current.Properties)
                {
                    if (property.IsValid() && property.Object is NiMaterialProperty material)
                    {
                        return material.Name?.Value;
                    }
                }
            }
            return null;
        }

        private BaseTexture Resolve(IEnumerable<ShaderTexture> maps, uint mapId)
        {
            var map = maps.FirstOrDefault(m => m?.MapId == mapId);
            if (map != null && this.File.ObjectsByRef.TryGetValue(map.SourceRef, out var source)
                && source is NiSourceTexture texture && texture.FileName != null)
            {
                return new BaseTexture(texture.FileName.ToString(), map.UvSetIndex);
            }
            return null;
        }

        private static bool TryGetIndex(NiAVObject node, string name, out uint value)
        {
            foreach (var reference in node.ExtraData)
            {
                if (reference.IsValid() && reference.Object is NiIntegerExtraData data
                    && string.Equals(data.Name.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = data.Data;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        // Niflib skips shader texture lists, so scan the raw NIF for their descriptors.
        private static IReadOnlyDictionary<uint, ShaderTexture[]> ReadShaderTextures(NiFile file, byte[] bytes)
        {
            var properties = file.ObjectsByRef.Where(item => item.Value is NiTexturingProperty property && property.NumShaderTextures > 0).OrderBy(item => item.Key).ToArray();
            var candidates = new List<ShaderTexture[]>();
            for (var offset = 0; offset <= bytes.Length - 4; offset++)
            {
                var count = BitConverter.ToUInt32(bytes, offset);
                if (count > 0 && count <= 16 && properties.Any(item => ((NiTexturingProperty)item.Value).NumShaderTextures == count)
                    && TryReadShaderTextures(file, bytes, offset + 4, (int)count, out var maps))
                {
                    candidates.Add(maps);
                    offset += 3;
                }
            }

            var result = new Dictionary<uint, ShaderTexture[]>();
            var next = 0;
            foreach (var property in properties)
            {
                var count = ((NiTexturingProperty)property.Value).NumShaderTextures;
                while (next < candidates.Count && candidates[next].Length != count)
                {
                    next++;
                }
                if (next < candidates.Count)
                {
                    result[property.Key] = candidates[next++];
                }
            }
            return result;
        }

        private static bool TryReadShaderTextures(NiFile file, byte[] bytes, int offset, int count, out ShaderTexture[] maps)
        {
            maps = new ShaderTexture[count];
            for (var i = 0; i < count; i++)
            {
                if (offset >= bytes.Length)
                {
                    return false;
                }
                var hasMap = bytes[offset++];
                if (hasMap == 0)
                {
                    continue;
                }
                if (hasMap != 1)
                {
                    return false;
                }
                if (offset + 25 > bytes.Length)
                {
                    return false;
                }

                var sourceRef = BitConverter.ToUInt32(bytes, offset);
                var clamp = BitConverter.ToUInt32(bytes, offset + 4);
                var filter = BitConverter.ToUInt32(bytes, offset + 8);
                var uvSet = BitConverter.ToUInt32(bytes, offset + 12);
                var transform = bytes[offset + 20];
                var mapId = BitConverter.ToUInt32(bytes, offset + 21);
                if (!file.ObjectsByRef.TryGetValue(sourceRef, out var source) || source is not NiSourceTexture
                    || clamp > 3 || filter > 5 || uvSet > 15 || transform != 0 || mapId >= count)
                {
                    return false;
                }
                maps[i] = new ShaderTexture(sourceRef, (int)uvSet, mapId);
                offset += 25;
            }
            var usedMaps = maps.Where(map => map != null).ToArray();
            return usedMaps.Length > 0 && usedMaps.Select(map => map.MapId).Distinct().Count() == usedMaps.Length;
        }

        private static float[] GetBlend(NiGeometryData geometry, BaseTexture texture2)
        {
            if (texture2 == null || !geometry.HasVertexColors || geometry.VertexColors == null || geometry.VertexColors.Length != geometry.Vertices.Length)
            {
                return null;
            }
            return geometry.VertexColors.Select(color => color.Alpha).ToArray();
        }

        /// <summary>
        /// Diffuse material color as 0xRRGGBB, -1 if the mesh has none. Untextured meshes are colored by it.
        /// </summary>
        private int GetMaterialColor(NiAVObject node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                foreach (var property in current.Properties)
                {
                    if (property.IsValid() && property.Object is NiMaterialProperty material)
                    {
                        return (ToByte(material.DiffuseColor.Red) << 16) | (ToByte(material.DiffuseColor.Green) << 8) | ToByte(material.DiffuseColor.Blue);
                    }
                }
            }
            return -1;
        }

        private static int ToByte(float value)
        {
            return (int)Math.Round(Math.Clamp(value, 0f, 1f) * 255);
        }

        private static Vector2[] GetUvSet(NiGeometryData geometry, BaseTexture texture)
        {
            if (texture == null || geometry.UVSets == null || geometry.UVSets.Length == 0)
            {
                return null;
            }

            var uvSet = geometry.UVSets[texture.UvSetIndex < geometry.UVSets.Length ? texture.UvSetIndex : 0];
            return uvSet.Length == geometry.Vertices.Length ? uvSet : null;
        }

        private void ComputePolys(Triangle[] trianlges, Vector3[] vertices, Matrix transformation, string texture, Vector2[] uvSet, string texture2, Vector2[] uvSet2, float[] textureBlend, int materialColor)
        {
            // Transaform all vertices
            var verticesTransformed = new List<Vector3>();
            foreach (var vector in vertices) verticesTransformed.Add(Vector3.TransformCoordinate(vector, transformation));

            foreach (var triangle in trianlges)
            {
                var poly = new Polygon(
                                       new Vector3(verticesTransformed[triangle.X].X, verticesTransformed[triangle.X].Y, verticesTransformed[triangle.X].Z),
                                       new Vector3(verticesTransformed[triangle.Y].X, verticesTransformed[triangle.Y].Y, verticesTransformed[triangle.Y].Z),
                                       new Vector3(verticesTransformed[triangle.Z].X, verticesTransformed[triangle.Z].Y, verticesTransformed[triangle.Z].Z),
                                       texture,
                                       uvSet == null ? null : new[] { uvSet[triangle.X], uvSet[triangle.Y], uvSet[triangle.Z] }
                                      )
                           {
                               Texture2 = texture2,
                               Uvs2 = uvSet2 == null ? null : new[] { uvSet2[triangle.X], uvSet2[triangle.Y], uvSet2[triangle.Z] },
                               TextureBlend = textureBlend == null ? null : new[] { textureBlend[triangle.X], textureBlend[triangle.Y], textureBlend[triangle.Z] },
                               MaterialColor = materialColor
                           };
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
                    var textures = this.Polys.SelectMany(p => new[] { p.Texture, p.Texture2 }).Where(t => t != null).Distinct().ToList();

                    writer.Write(NifParser.POLY_FORMAT_MAGIC_V4);
                    writer.Write(textures.Count);
                    foreach (var texture in textures)
                    {
                        writer.Write(texture);
                    }

                    foreach (var poly in this.Polys)
                    {
                        writer.Write(poly.Texture == null ? -1 : textures.IndexOf(poly.Texture));
                        writer.Write(poly.Texture2 == null ? -1 : textures.IndexOf(poly.Texture2));

                        writer.Write(poly.P1.X);
                        writer.Write(poly.P1.Y);
                        writer.Write(poly.P1.Z);

                        writer.Write(poly.P2.X);
                        writer.Write(poly.P2.Y);
                        writer.Write(poly.P2.Z);

                        writer.Write(poly.P3.X);
                        writer.Write(poly.P3.Y);
                        writer.Write(poly.P3.Z);

                        writer.Write(poly.MaterialColor);
                        writer.Write(poly.Uvs != null);
                        if (poly.Uvs != null)
                        {
                            foreach (var uv in poly.Uvs)
                            {
                                writer.Write(uv.X);
                                writer.Write(uv.Y);
                            }
                        }
                        writer.Write(poly.Uvs2 != null);
                        if (poly.Uvs2 != null)
                        {
                            foreach (var uv in poly.Uvs2)
                            {
                                writer.Write(uv.X);
                                writer.Write(uv.Y);
                            }
                        }
                        writer.Write(poly.TextureBlend != null);
                        if (poly.TextureBlend != null)
                        {
                            foreach (var blend in poly.TextureBlend)
                            {
                                writer.Write(blend);
                            }
                        }
                    }
                }
            }
        }

    }
}
