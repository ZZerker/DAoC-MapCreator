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
using Niflib;
using NifUtil.Classes;
using NifUtil.Objects;
using SharpDX;

namespace NifUtil
{
    public class NifParser : IDisposable
    {
        string fileName;

        private StreamReader fileReader;

        private NiFile nifFile;

        private Polygon[] polygons;

        private byte[] nifData;

        #region Events
        public event IsNodeDrawableEventHandler IsNodeDrawable;
        #endregion

        /// <summary>
        /// Replaces the base texture of a mesh by its material name and texture name (keep pieces take their textures by realm and tier)
        /// </summary>
        public Func<string, string, string> ResolveTexture { get; set; }

        public NifParser()
        {
            // Language settings
            var ci = new System.Globalization.CultureInfo("en-US");
            System.Threading.Thread.CurrentThread.CurrentCulture = ci;
            System.Threading.Thread.CurrentThread.CurrentUICulture = ci;
        }

        /// <summary>
        /// Loads a NIF file
        /// </summary>
        /// <param name="nifFile"></param>
        public void Load(string nifFile)
        {
            if (!System.IO.File.Exists(nifFile))
            {
                throw new FileNotFoundException("NIF File not found!");
            }

            this.fileName = nifFile;
            this.fileReader = new StreamReader(nifFile);
            this.ReadNifFile();
        }

        /// <summary>
        /// Loads a NIF file
        /// </summary>
        /// <param name="nifFileStream"></param>
        public void Load(StreamReader nifFileStream)
        {
	        this.fileReader = nifFileStream;
	        this.ReadNifFile();
        }

        /// <summary>
        /// Reads the added nif File
        /// </summary>
        private void ReadNifFile()
        {
            using (var data = new MemoryStream())
            {
                this.fileReader.BaseStream.CopyTo(data);
                this.nifData = data.ToArray();
                this.fileReader.BaseStream.Position = 0;
            }

            using (var br = new BinaryReader(this.fileReader.BaseStream))
            {
	            this.nifFile = new NiFile(br);
            }
        }

        /// <summary>
        /// Converts a nif
        /// </summary>
        /// <param name="type"></param>
        /// <param name="targetFilename"></param>
        public void Convert(ConvertType type, string targetFilename)
        {
            switch (type)
            {
                case ConvertType.WaveFrontObject:
                    var wf = new ConvertWavefront(this.nifFile);
                    if(this.IsNodeDrawable != null)
                    {
                        wf.IsNodeDrawable += delegate(NiAVObject node)
                        {
                            return this.IsNodeDrawable(node);
                        };
                    }

                    wf.Start();
                    wf.Write(targetFilename);
                    break;
                case ConvertType.PolyText:
                case ConvertType.Poly:
                    var conv = new ConvertPoly(this.nifFile, this.nifData) { ResolveTexture = this.ResolveTexture };
                    if (this.IsNodeDrawable != null)
                    {
                        conv.IsNodeDrawable += delegate (NiAVObject node)
                        {
                            return this.IsNodeDrawable(node);
                        };
                    }

                    conv.Start();
                    this.polygons = conv.Polys.ToArray();

                    if (type == ConvertType.PolyText) conv.WritePlain(targetFilename);
                    else conv.Write(targetFilename);

                    break;
            }
        }

        /// <summary>
        /// "PLY3" in little endian, starts a .poly file with texture names, material colors and texture coordinates
        /// </summary>
        public const int POLY_FORMAT_MAGIC = 0x33594C50;

        public const int POLY_FORMAT_MAGIC_V4 = 0x34594C50;

        /// <summary>
        /// "PLY5": PLY4 plus vertex colors
        /// </summary>
        public const int POLY_FORMAT_MAGIC_V5 = 0x35594C50;

        /// <summary>
        /// "PLY2": texture names without texture coordinates
        /// </summary>
        private const int POLY_FORMAT_MAGIC_V2 = 0x32594C50;

        public Polygon[] GetPolys()
        {
            if (this.polygons != null) return this.polygons;

            var conv = new ConvertPoly(this.nifFile, this.nifData);
            return conv.Polys.ToArray();
        }

        public void Dispose()
        {
            if (this.fileReader != null)
            {
	            this.fileReader.Dispose();
            }
        }

        public static Polygon[] ReadPoly(string polyFile)
        {
            using (var reader = new StreamReader(polyFile))
            {
                return ReadPoly(reader);
            }
        }

        public static Polygon[] ReadPoly(StreamReader polyFileReader)
        {
            var polys = new List<Polygon>();

            using (var reader = new BinaryReader(polyFileReader.BaseStream))
            {
                var magic = reader.BaseStream.Length >= 4 ? reader.ReadInt32() : 0;
                var layered = magic == POLY_FORMAT_MAGIC_V4 || magic == POLY_FORMAT_MAGIC_V5;
                if (layered || magic == POLY_FORMAT_MAGIC || magic == POLY_FORMAT_MAGIC_V2)
                {
                    var textures = new string[reader.ReadInt32()];
                    for (var i = 0; i < textures.Length; i++)
                    {
                        textures[i] = reader.ReadString();
                    }

                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        var textureIndex = reader.ReadInt32();
                        var texture2Index = layered ? reader.ReadInt32() : -1;
                        var p1 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var p2 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var p3 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                        var materialColor = magic == POLY_FORMAT_MAGIC || layered ? reader.ReadInt32() : -1;
                        Vector2[] uvs = null;
                        if ((magic == POLY_FORMAT_MAGIC || layered) && reader.ReadBoolean())
                        {
                            uvs = new[]
                                  {
                                      new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                                      new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                                      new Vector2(reader.ReadSingle(), reader.ReadSingle())
                                  };
                        }

                        Vector2[] uvs2 = null;
                        float[] textureBlend = null;
                        if (layered && reader.ReadBoolean())
                        {
                            uvs2 = new[] { new Vector2(reader.ReadSingle(), reader.ReadSingle()), new Vector2(reader.ReadSingle(), reader.ReadSingle()), new Vector2(reader.ReadSingle(), reader.ReadSingle()) };
                        }
                        if (layered && reader.ReadBoolean())
                        {
                            textureBlend = new[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() };
                        }
                        float[] vertexColors = null;
                        if (magic == POLY_FORMAT_MAGIC_V5 && reader.ReadBoolean())
                        {
                            vertexColors = new float[9];
                            for (var i = 0; i < vertexColors.Length; i++)
                            {
                                vertexColors[i] = reader.ReadSingle();
                            }
                        }

                        polys.Add(new Polygon(p1, p2, p3, textureIndex >= 0 ? textures[textureIndex] : null, uvs) { Texture2 = texture2Index >= 0 ? textures[texture2Index] : null, Uvs2 = uvs2, TextureBlend = textureBlend, VertexColors = vertexColors, MaterialColor = materialColor });
                    }
                    return polys.ToArray();
                }

                // Format without header: 9 floats per triangle
                reader.BaseStream.Position = 0;
                var points = new List<float>();

                while (reader.BaseStream.Position != reader.BaseStream.Length)
                {
                    points.Add(reader.ReadSingle());

                    if(points.Count != 9)
                    {
	                    continue;
                    }

                    polys.Add(new Polygon(
                                          new Vector3(points[0], points[1], points[2]),
                                          new Vector3(points[3], points[4], points[5]),
                                          new Vector3(points[6], points[7], points[8])
                                         ));

                    points.Clear();
                }
            }

            return polys.ToArray();
        }

    }

    public enum ConvertType
    {
        Poly,
        PolyText,
        WaveFrontObject,
        Collada
    }

}
