using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ImageMagick;

namespace MapCreator.Classes.MapCreation
{
	internal class WaterConfiguration
	{

		public readonly string Name;
		public string Texture;
		public string Multitexture;
		public string Flow;
		public int Height;
		public int Bankpoints;
		public Color Color;
		public string ExtendPosX;
		public string ExtendPosY;
		public string ExtendNegX;
		public string ExtendNegY;
		public string Tesselation;
		public string Type;

		public readonly List<PointD> LeftCoordinates = new List<PointD>();
		public readonly List<PointD> RightCoordinates = new List<PointD>();

		public WaterConfiguration(string name)
		{
			this.Name = name;
		}

		public List<PointD> GetCoordinates()
		{
			var newCoordinates = new List<PointD>();
			newCoordinates.AddRange(this.LeftCoordinates);
			newCoordinates.AddRange(this.RightCoordinates.Reverse<PointD>());
			return newCoordinates;
		}
	}
}
