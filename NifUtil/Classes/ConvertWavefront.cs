
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
using ImageMagick;
using Niflib;
using SharpDX;

namespace NifUtil.Classes
{
	internal class ConvertWavefront : Convert
    {
        private int triangleCounter = 1;
        private readonly List<string> mtlExport = new List<string>();
        private readonly List<string> textures = new List<string>();

        public ConvertWavefront(NiFile file)
            : base(file)
        {
        }

        public void Start()
        {
            this.Export.Add("# Build with NifParser by Merec");
            this.Export.Add("# special thanks to Schaf");
            this.Export.Add("");

            this.WalkNodes(this.File.FindRoot());
        }

        private void WalkNodes(NiAVObject node)
        {
            // Ignore some node names
            if (!this.IsValidNode(node)) return;

            // Render Children
            if (node is NiTriShape shape)
            {
                this.Export.Add("");
                this.Export.Add(this.ParseShape(shape));
                this.Export.Add("");
            }
            else if (node is NiTriStrips strips)
            {
                this.Export.Add("");
                this.Export.Add(this.ParseStrips(strips));
                this.Export.Add("");
            }

            var currentNode = node as NiNode;
            if(currentNode == null)
            {
	            return;
            }

            if(currentNode.Children.Length <= 0)
            {
	            return;
            }

            foreach (var child in currentNode.Children)
            {
	            if (child.IsValid())
	            {
		            this.WalkNodes(child.Object);
	            }
            }
        }

        private string ParseStrips(NiTriStrips strips)
        {
            if (!strips.Data.IsValid()) return "";

            var geometry = (NiTriStripsData)strips.Data.Object;

            var transformationMatrix = this.ComputeWorldMatrix(strips);

            // The final text
            var export = new List<string>();

            // Set Object name
            export.Add("g Strip " + strips.Name + Environment.NewLine);

            // Verticles (v)
            if (geometry.HasVertices && geometry.NumVertices >= 3)
            {
                export.Add(this.PrintVertices(geometry.Vertices, transformationMatrix));
            }

            // Texture coordinates (vt)
            if (geometry.UVSets.Length > 0)
            {
                export.Add(this.PrintUvSets(geometry.UVSets));
            }

            // Normals (vn)
            if (geometry.HasNormals)
            {
                export.Add(this.PrintNormals(geometry.Normals, transformationMatrix));
            }

            if(geometry.Points.Length <= 0)
            {
	            return string.Join(Environment.NewLine, export);
            }

            var triangles = new List<Triangle>();

            foreach (var points in geometry.Points)
            {
	            var t = false;
	            var j = 1;

	            var p1 = points[0];
	            var p2 = points[1];

	            while (j < points.Length - 1)
	            {
		            var p3 = points[j+1];

		            if (p1 != p2 && p1 != p3 && p2 != p3)
		            {
			            triangles.Add(t?new Triangle(p1, p3, p2):new Triangle(p1, p2, p3));
		            }

		            j = j + 1;
		            p1 = p2;
		            p2 = p3;
		            t = !t;
	            }
            }

            export.Add(this.PrintTriangles(triangles.ToArray(), (geometry.UVSets.Length > 0)));

            return string.Join(Environment.NewLine, export);
        }

        private string ParseShape(NiTriShape shape)
        {
            if (!shape.Data.IsValid()) return "";

            var geometry = (NiTriShapeData)shape.Data.Object;

            // The final text
            var export = new List<string>();

            var transformationMatrix = this.ComputeWorldMatrix(shape);

            // Set Object name
            export.Add("g Shape " + shape.Name + Environment.NewLine);

            NiMaterialProperty material = null;
            NiTexturingProperty texture = null;
            foreach (var property in shape.Properties)
            {
                if (property.Object is NiMaterialProperty propertyObject)
                {
                    material = propertyObject;
                }
                if (property.Object is NiTexturingProperty texturingProperty)
                {
                    texture = texturingProperty;
                }
            }

            if (material != null && texture != null)
            {
                export.Add(this.PrintMaterial(material, texture));
            }

            // Verticles (v)
            if (geometry.HasVertices && geometry.NumVertices >= 3)
            {
                export.Add(this.PrintVertices(geometry.Vertices, transformationMatrix));
            }

            // Texture coordinates (vt)
            if (geometry.UVSets.Length > 0)
            {
                export.Add(this.PrintUvSets(geometry.UVSets));
            }

            // Normals (vn)
            if (geometry.HasNormals)
            {
                export.Add(this.PrintNormals(geometry.Normals, transformationMatrix));
            }

            // Parameter space vertices (vp)

            // Face Definitions (f)
            export.Add(this.PrintTriangles(geometry.Triangles, (geometry.UVSets.Length > 0)));

            return string.Join(Environment.NewLine, export);
        }

        private string PrintMaterial(NiMaterialProperty material, NiTexturingProperty texture)
        {
            if (this.mtlExport.Count > 0) this.mtlExport.Add(Environment.NewLine);

            var name = material.Name.ToString();

            this.mtlExport.Add("newmtl " + name);

            
            this.mtlExport.Add(string.Format("Ka {0} {1} {2}", material.AmbientColor.Red, material.AmbientColor.Green, material.AmbientColor.Blue));            
            this.mtlExport.Add(string.Format("Kd {0} {1} {2}", material.DiffuseColor.Red, material.DiffuseColor.Green, material.DiffuseColor.Blue));            
            this.mtlExport.Add(string.Format("Ks {0} {1} {2}", material.SpecularColor.Red, material.SpecularColor.Green, material.SpecularColor.Blue));            
            this.mtlExport.Add(string.Format("d {0}", material.Alpha));
            //mtlExport.Add(string.Format("Tr {0}", material.Alpha));

            this.PrintTexture(texture);

            var export = "# Material" + Environment.NewLine;
            export += "usemtl " + name + Environment.NewLine;
            return export;
        }

        private void PrintTexture(NiTexturingProperty texture)
        {
            var source = texture.File.ObjectsByRef.First(o => o.Key == texture.BaseTexture.Source.RefId).Value as NiSourceTexture;

            var fileName = source.FileName.ToString().ToLower();

            var offset = string.Format("-o {0} {1}", texture.BaseTexture.CenterOffset.X, texture.BaseTexture.CenterOffset.Y);

            this.mtlExport.Add("# original file " + fileName);
            this.mtlExport.Add(string.Format("map_Ka {0}.tga", Path.GetFileNameWithoutExtension(fileName), offset));
            this.mtlExport.Add(string.Format("map_Kd {0}.tga", Path.GetFileNameWithoutExtension(fileName), offset));
            //mtlExport.Add(string.Format("map_Ks {0}.tga", Path.GetFileNameWithoutExtension(fileName), offset));
            this.textures.Add(fileName);

            if (texture.BumpMapTexture != null)
            {
                var bumbTexture = texture.File.ObjectsByRef.First(o => o.Key == texture.BumpMapTexture.Source.RefId).Value as NiSourceTexture;
                this.mtlExport.Add(string.Format("map_bump {0}.tga", Path.GetFileNameWithoutExtension(bumbTexture.FileName.ToString().ToLower())));
                this.textures.Add(bumbTexture.FileName.ToString());
            }


        }

        private string PrintVertices(Vector3[] vertices, Matrix transformation)
        {
            var export = "";
            foreach (var verticle in vertices)
            {
                var vectorTransformed = Vector3.TransformCoordinate(verticle, transformation);
                export += string.Format("v {0} {1} {2}", vectorTransformed.X, vectorTransformed.Y, vectorTransformed.Z) + Environment.NewLine;
            }
            return export;
        }

        private string PrintUvSets(Vector2[][] uvsets)
        {
            var export = "";

            /*
            foreach (Vector2[] uvset in uvsets.Reverse())
            {
                foreach (Vector2 uv in uvset)
                {
                    export += string.Format("vt {0} {1}", uv.X, 1f - uv.Y) + Environment.NewLine;
                }
            }
             * */

            // Test: Draw only the first set without reverse
            foreach (var uvset in uvsets)
            {
                foreach (var uv in uvset)
                {
                    export += string.Format("vt {0} {1}", uv.X, 1f - uv.Y) + Environment.NewLine;
                }

                break;
            }

            return export;
        }

        private string PrintNormals(Vector3[]normals, Matrix transformation)
        {
            var export = "";
            foreach (var normal in normals)
            {
                var vectorTransformed = Vector3.TransformNormal(normal, transformation);
                export += string.Format("vn {0} {1} {2}", vectorTransformed.X, vectorTransformed.Y, vectorTransformed.Z) + Environment.NewLine;
            }
            return export;
        }

        private string PrintTriangles(Triangle[] triangles, bool hasUvSets)
        {
            var export = "";

            var format = "f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}";
            if (!hasUvSets)
            {
                format = "f {0}//{0} {1}//{1} {2}//{2}";
            }

            var max = this.triangleCounter;
            foreach (var face in triangles)
            {
                export += string.Format(format, face.X + this.triangleCounter, face.Y + this.triangleCounter, face.Z + this.triangleCounter) + Environment.NewLine;
                if (face.X + this.triangleCounter > max) max = face.X + this.triangleCounter;
                if (face.Y + this.triangleCounter > max) max = face.Y + this.triangleCounter;
                if (face.Z + this.triangleCounter > max) max = face.Z + this.triangleCounter;
            }
            this.triangleCounter = max + 1;

            /*
            foreach (Triangle face in triangles)
            {
                export += string.Format(format, face.X, face.Y, face.Z) + Environment.NewLine;
            }
             * */

            return export;
        }

        public override void Write(string filename)
        {
            var fileInfo = new FileInfo(filename);

            if (this.mtlExport.Count > 0)
            {
                var mtlFileName = Path.GetFileNameWithoutExtension(filename) + ".mtl";
                this.Export.Insert(3, "# Textures " + Environment.NewLine + "mtllib " + mtlFileName);

                using (var writer = new StreamWriter(fileInfo.Directory + "\\" + mtlFileName))
                {
                    writer.WriteLine(string.Join(Environment.NewLine, this.mtlExport));
                }

                using (var writer = new StreamWriter(fileInfo.Directory + "\\" + mtlFileName))
                {
                    writer.WriteLine(string.Join(Environment.NewLine, this.mtlExport));
                }
            }

            using (var writer = new StreamWriter(filename))
            {
                writer.WriteLine(string.Join(Environment.NewLine, this.Export));
            }

            // Load textues
            if (this.textures.Count > 0)
            {
                var textureLocations = new List<string>()
                                       {
		                                       "D:\\DAoC Extracted\\figures\\Mskins",
		                                       "D:\\DAoC Extracted\\figures\\skins",
		                                       "D:\\DAoC Extracted\\items\\pskins",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\Nifs",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\Dnifs",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\sky",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\TerrainTex",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\textures",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\trees",
		                                       "D:\\Games\\Dark Age of Camelot\\frontiers\\dnifs",
		                                       "D:\\Games\\Dark Age of Camelot\\frontiers\\items",
		                                       "D:\\Games\\Dark Age of Camelot\\frontiers\\NIFS",
		                                       "D:\\Games\\Dark Age of Camelot\\frontiers\\zones\\TerrainTex",
		                                       "D:\\Games\\Dark Age of Camelot\\frontiers\\zones\\textures",
		                                       "D:\\Games\\Dark Age of Camelot\\phousing\\nifs",
		                                       "D:\\Games\\Dark Age of Camelot\\phousing\\textures",
		                                       "D:\\Games\\Dark Age of Camelot\\pregame",
		                                       "D:\\Games\\Dark Age of Camelot\\Tutorial\\zones\\nifs",
		                                       "D:\\Games\\Dark Age of Camelot\\Tutorial\\zones\\terraintex",
		                                       "D:\\Games\\Dark Age of Camelot\\insignia",
		                                       "D:\\Games\\Dark Age of Camelot\\items",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\zone026\\nifs",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\zone050\\nifs",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\zone120\\nifs",
		                                       "D:\\Games\\Dark Age of Camelot\\zones\\zone209\\nifs",

                                       };


                foreach (var texture in this.textures)
                {
                    foreach (var loc in textureLocations)
                    {
                        var dir = new DirectoryInfo(loc);
                        var files = dir.GetFiles(Path.GetFileNameWithoutExtension(texture) + ".*");

                        if(files.Length <= 0)
                        {
	                        continue;
                        }

                        var sourceFileName = files.First().FullName;
                        var targetFileName = fileInfo.Directory + "\\" + Path.GetFileNameWithoutExtension(sourceFileName) + ".tga";
                        var targetFileNamePng = fileInfo.Directory + "\\" + Path.GetFileNameWithoutExtension(sourceFileName) + ".png";

                        using (var image = new MagickImage(files.First().FullName))
                        {
	                        image.Write(targetFileName.ToLower());
	                        image.Write(targetFileNamePng.ToLower());
                        }
                        break;
                    }
                }

            }

        }

    }
}
