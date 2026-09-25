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

        #region Events
        public event IsNodeDrawableEventHandler IsNodeDrawable;
        #endregion

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
                    var conv = new ConvertPoly(this.nifFile);
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
        /// "PLY2" in little endian, starts a .poly file with texture names
        /// </summary>
        public const int POLY_FORMAT_MAGIC = 0x32594C50;

        public Polygon[] GetPolys()
        {
            if (this.polygons != null) return this.polygons;

            var conv = new ConvertPoly(this.nifFile);
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
                if (reader.BaseStream.Length >= 4 && reader.ReadInt32() == POLY_FORMAT_MAGIC)
                {
                    var textures = new string[reader.ReadInt32()];
                    for (var i = 0; i < textures.Length; i++)
                    {
                        textures[i] = reader.ReadString();
                    }

                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        var textureIndex = reader.ReadInt32();
                        polys.Add(new Polygon(
                                              new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                                              new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                                              new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                                              textureIndex >= 0 ? textures[textureIndex] : null
                                             ));
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
