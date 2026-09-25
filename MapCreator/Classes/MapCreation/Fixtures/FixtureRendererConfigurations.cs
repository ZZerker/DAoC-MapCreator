//
// MapCreator
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

namespace MapCreator.Classes.MapCreation.Fixtures
{
	internal static class FixtureRendererConfigurations
    {
        private static readonly XDocument FixturesXml;

        private static readonly List<FixtureRendererConfiguration2> RendererCategories = new List<FixtureRendererConfiguration2>();

        private static readonly Dictionary<string, FixtureRendererConfiguration2> Configurations = new Dictionary<string, FixtureRendererConfiguration2>();
        private static FixtureRendererConfiguration2 defaultConfiguration;

        internal static FixtureRendererConfiguration2 DefaultConfiguration
        {
            get => FixtureRendererConfigurations.defaultConfiguration;
            set => FixtureRendererConfigurations.defaultConfiguration = value;
        }

        /// <summary>
        /// Static constructor
        /// </summary>
        static FixtureRendererConfigurations()
        {
            // Read Zones
            FixturesXml = XDocument.Load(string.Format("{0}\\fixtures.xml", Application.StartupPath));
            ParseFixturesXml();
        }

        /// <summary>
        /// Get the renderer configuration of a nifname
        /// </summary>
        /// <param name="nifname"></param>
        /// <returns></returns>
        public static FixtureRendererConfiguration2? GetFixtureRendererConfiguration(string nifname)
        {
            foreach (var renderer in Configurations)
            {
                var regex = new Regex(renderer.Key.ToLower(), RegexOptions.IgnoreCase);
                if (regex.IsMatch(nifname))
                {
                    return renderer.Value;
                }
            }
            return null;
        }

        public static FixtureRendererConfiguration2 GetRendererById(string id)
        {
            var conf = RendererCategories.Where(c => c.Name == id);
            if (!conf.Any()) return defaultConfiguration;
            else return conf.FirstOrDefault();
        }

        /// <summary>
        /// Parses all filters to configurations
        /// </summary>
        private static void ParseFixturesXml()
        {
            var categories = FixturesXml.Descendants("FixtureCategory");
            //List<FixtureRendererConfiguration2> rendererConfigurations = new List<FixtureRendererConfiguration2>();
            foreach (var category in categories)
            {
                var rendererConfiguration = ParseRendererConfigurationFromXmlNode(category);
                if (rendererConfiguration == null) continue;
                RendererCategories.Add(rendererConfiguration.GetValueOrDefault());
            }

            var fixtures = FixturesXml.Descendants("Fixture");
            foreach (var fixture in fixtures)
            {
                var patternNode = fixture.Descendants("pattern");
                if (!patternNode.Any() || string.IsNullOrEmpty(patternNode.First().Value))
                {
                    MainForm.Log(string.Format("Fixtures: Error in fixtures.xml, no file pattern set."), MainForm.LogLevel.Error);
                    continue;
                }

                // Check pattern
                try
                {
                    var regexTest = new Regex(patternNode.First().Value);
                }
                catch
                {
                    MainForm.Log(string.Format("Fixtures: Error in fixtures.xml, the pattern \"{0}\" is not valid.", patternNode.First().Value), MainForm.LogLevel.Error);
                    continue;
                }

                var categoryNode = fixture.Descendants("category");
                if (!patternNode.Any() || string.IsNullOrEmpty(patternNode.First().Value))
                {
                    MainForm.Log(string.Format("Fixtures: Error in fixtures.xml, no category set."), MainForm.LogLevel.Error);
                    continue;
                }

                var pattern = patternNode.First().Value;
                var category = categoryNode.First().Value;

                // Get category
                var categoryResult = RendererCategories.Where(c => c.Name == category);
                if (categoryResult.Any())
                {
                    Configurations.Add(pattern, categoryResult.First());
                }
            }
        }

        private static FixtureRendererConfiguration2? ParseRendererConfigurationFromXmlNode(XElement node)
        {
            // Create a NumberFormatInfo object for floats and set some of its properties.
            var provider = new System.Globalization.NumberFormatInfo
                           {
		                           NumberDecimalSeparator = ".",
		                           NumberGroupSeparator = "",
		                           NumberGroupSizes = new int[] { 2 }
                           };

            try
            {
                var conf = new FixtureRendererConfiguration2();

                if (node.Descendants("id").Any())
                {
                    conf.Name = node.Descendants("id").First().Value;
                }
                else if(node.Descendants("pattern").Any())
                {
                    conf.Name = node.Descendants("pattern").First().Value;
                }

                conf.Renderer = GetRendererType(node.Descendants("renderer").First().Value);
                conf.Color = new ImageMagick.MagickColor((node.Descendants("color").Any()) ? node.Descendants("color").First().Value : "#FFF");
                conf.Transparency = Convert.ToInt32((node.Descendants("transparency").Any()) ? node.Descendants("transparency").First().Value : "0");

                var lightElement = node.Descendants("light");
                if (lightElement.Any())
                {
                    var light = lightElement.First();
                    conf.HasLight = Convert.ToBoolean(light.Value);
                    conf.LightMin = Convert.ToDouble(!string.IsNullOrEmpty(light.Attribute("min").Value) ? light.Attribute("min").Value : "0.6", provider);
                    conf.LightMax = Convert.ToDouble(!string.IsNullOrEmpty(light.Attribute("max").Value) ? light.Attribute("max").Value : "1.0", provider);
                    conf.LightVector = GetLightVector(string.IsNullOrEmpty(light.Attribute("direction").Value) ? light.Attribute("direction").Value : "1,1,-1");
                }

                var shadowElement = node.Descendants("shadow");
                if (shadowElement.Any())
                {
                    var shadow = shadowElement.First();
                    conf.HasShadow = Convert.ToBoolean(shadow.Value);
                    conf.ShadowColor = new ImageMagick.MagickColor((!string.IsNullOrEmpty(shadow.Attribute("color").Value)) ? shadow.Attribute("color").Value : "#000");
                    conf.ShadowOffsetX = Convert.ToInt32(!string.IsNullOrEmpty(shadow.Attribute("offset_x").Value) ? shadow.Attribute("offset_x").Value : "0");
                    conf.ShadowOffsetY = Convert.ToInt32(!string.IsNullOrEmpty(shadow.Attribute("offset_y").Value) ? shadow.Attribute("offset_y").Value : "0");
                    conf.ShadowSize = Convert.ToDouble(!string.IsNullOrEmpty(shadow.Attribute("size").Value) ? shadow.Attribute("size").Value : "1", provider);
                    conf.ShadowTransparency = Convert.ToInt32(!string.IsNullOrEmpty(shadow.Attribute("transparency").Value) ? shadow.Attribute("transparency").Value : "75");
                }

                if (!string.IsNullOrEmpty(node.Attribute("is_default").Value) && Convert.ToBoolean(node.Attribute("is_default").Value) == true)
                {
                    defaultConfiguration = conf;
                }

                return conf;
            }
            catch(Exception ex)
            {
                MainForm.Log("Error in fixtures XML", MainForm.LogLevel.Error);
                MainForm.Log(ex.Message, MainForm.LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Gets the enum value out a text value
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private static FixtureRenderererType GetRendererType(string name)
        {
            // Parse the textual representation of the renderer to its enum value
            var renderer = FixtureRenderererType.Shaded;
            try
            {
                renderer = (FixtureRenderererType)Enum.Parse(typeof(FixtureRenderererType), name);
            }
            catch
            {
                MainForm.Log(string.Format("The renderer \"{0}\" is invalid!", name), MainForm.LogLevel.Error);
            }

            return renderer;
        }

        /// <summary>
        /// Gets the Vector out of a string
        /// </summary>
        /// <param name="lightVector"></param>
        /// <returns></returns>
        private static SharpDX.Vector3 GetLightVector(string lightVector)
        {
            if (lightVector == "") return new SharpDX.Vector3(1f, 1f, -1f);

            var parts = lightVector.Split(',');
            if (parts.Length != 3)
            {
                MainForm.Log(string.Format("LightVector \"{0}\" is invalid.", lightVector), MainForm.LogLevel.Warning);
                return new SharpDX.Vector3(1f, 1f, -1f);
            }

            try
            {
                return new SharpDX.Vector3(Convert.ToSingle(parts[0]), Convert.ToSingle(parts[1]), Convert.ToSingle(parts[2]));
            }
            catch
            {
                MainForm.Log(string.Format("LightVector \"{0}\" is invalid.", lightVector), MainForm.LogLevel.Warning);
                return new SharpDX.Vector3(1f, 1f, -1f);
            }
        }


    }

	internal struct FixtureRendererConfiguration2
    {
        public string Name;
        public FixtureRenderererType Renderer;
        public ImageMagick.MagickColor Color;
        public int Transparency;

        // Light
        public bool HasLight;
        public double LightMin;
        public double LightMax;
        public SharpDX.Vector3 LightVector;

        // Shadow
        public bool HasShadow;
        public ImageMagick.MagickColor ShadowColor;
        public int ShadowOffsetX;
        public int ShadowOffsetY;
        public double ShadowSize;
        public int ShadowTransparency;

        public FixtureRendererConfiguration2(string name)
        {
            this.Name = name;
            this.Renderer = FixtureRenderererType.Shaded;
            this.Color = new ImageMagick.MagickColor("#FFF");
            this.Transparency = 0;

            this.HasLight = true;
            this.LightMin = 0.6;
            this.LightMax = 1.0;
            this.LightVector = new SharpDX.Vector3(1f, 1f, -1f);

            this.HasShadow = true;
            this.ShadowColor = new ImageMagick.MagickColor("#000");
            this.ShadowOffsetX = 0;
            this.ShadowOffsetY = 0;
            this.ShadowSize = 1.0;
            this.ShadowTransparency = 75;
        }
    }
}
