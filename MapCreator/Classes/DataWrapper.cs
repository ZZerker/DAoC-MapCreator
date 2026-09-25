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
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using MapCreator.data;

namespace MapCreator.Classes
{
	internal class DataWrapper
    {
        private static readonly XDocument ZonesXml = null;

        private static readonly string PresetsDataFile;

        private static MapCreatorData mapCreatorData = new MapCreatorData();
        public static MapCreatorData MapCreatorData
        {
            get => DataWrapper.mapCreatorData;
            set => DataWrapper.mapCreatorData = value;
        }

        static DataWrapper()
        {
            // Read Zones
            ZonesXml = XDocument.Load(string.Format("{0}\\data\\zones.xml", Application.StartupPath));

            // Create/Read data xml file
            PresetsDataFile = string.Format("{0}\\presets.xml", Application.StartupPath);
            if (!File.Exists(PresetsDataFile))
            {
                mapCreatorData.ZoneSelectionPresets.WriteXml(PresetsDataFile);
            }
            else
            {
                mapCreatorData.ZoneSelectionPresets.ReadXml(PresetsDataFile);
            }
        }

        #region MapCreatorData Presets

        public static void LoadPresets()
        {
            mapCreatorData.ZoneSelectionPresets.Clear();
            mapCreatorData.ZoneSelectionPresets.ReadXml(PresetsDataFile);
        }

        public static void SavePresets()
        {
            mapCreatorData.ZoneSelectionPresets.WriteXml(PresetsDataFile);
        }

        public static void AddPresetRow(string name, List<string> zoneIds)
        {
            var row = mapCreatorData.ZoneSelectionPresets.NewZoneSelectionPresetsRow();
            row.Name = name;
            row.Zones = String.Join(",", zoneIds);
            mapCreatorData.ZoneSelectionPresets.AddZoneSelectionPresetsRow(row);
            SavePresets();
        }

        public static void RemovePreset(MapCreatorData.ZoneSelectionPresetsRow row)
        {
            mapCreatorData.ZoneSelectionPresets.RemoveZoneSelectionPresetsRow(row);
            SavePresets();
        }

        public static MapCreatorData.ZoneSelectionPresetsRow[] GetPresetRows()
        {
            return mapCreatorData.ZoneSelectionPresets.ToArray();
        }

        #endregion

        #region Zones.xml

        /// <summary>
        /// Gets all realms
        /// </summary>
        /// <returns></returns>
        public static List<string> GetRealms()
        {
            return ZonesXml.Descendants("realm").Attributes("name").Select(x => x.Value).ToList();
        }

        /// <summary>
        /// Gets all Expansion of a realm
        /// </summary>
        /// <param name="realm"></param>
        /// <returns></returns>
        public static List<string> GetExpansionsByRealm(string realm)
        {
            var expansions = ZonesXml.Descendants("expansion")
                                     .Where(r => r.Parent.Attribute("name").Value == realm)
                                     .Select(e => e.Attribute("name").Value)
                                     .ToList();

            return expansions;
        }

        /// <summary>
        /// Gets all ZoneTypes of an Realms and Expansion
        /// </summary>
        /// <param name="realm"></param>
        /// <param name="expansion"></param>
        /// <returns></returns>
        public static List<string> GetZoneTypesByRealmAndExpansion(string realm, string expansion)
        {
            if (string.IsNullOrEmpty(realm)) return new List<string>();
            if (string.IsNullOrEmpty(expansion)) return new List<string>();

            var zoneTypes = ZonesXml.Descendants("zone")
                .Where(r => r.Parent.Attribute("name").Value == expansion && r.Parent.Parent.Attribute("name").Value == realm)
                .OrderBy(e => e.Attribute("type").Value)
                .Select(e => e.Attribute("type").Value)
                .Distinct()
                .ToList();

            return zoneTypes;
        }

        /// <summary>
        /// Gets all zones by realm, expansion and zony type
        /// </summary>
        /// <param name="realm"></param>
        /// <param name="expansion"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static Dictionary<string, string> GetZonesByRealmAndExpansionAndType(string realm, string expansion, string type)
        {
            if (string.IsNullOrEmpty(realm)) return new Dictionary<string, string>();
            if (string.IsNullOrEmpty(expansion)) return new Dictionary<string, string>();
            if (string.IsNullOrEmpty(type)) return new Dictionary<string, string>();

            var zones = ZonesXml.Descendants("zone")
                .Where(r => r.Parent.Attribute("name").Value == expansion && r.Parent.Parent.Attribute("name").Value == realm && r.Attribute("type").Value == type)
                .OrderBy(e => e.Attribute("id").Value)
                .ToDictionary(e => e.Attribute("id").Value, e => e.Value);

            return zones;
        }

        /// <summary>
        /// Gets an expansion of a zone
        /// </summary>
        /// <param name="zoneId"></param>
        /// <returns></returns>
        public static GameExpansion GetExpansionByZone(string zoneId)
        {
            var results = ZonesXml.Descendants("zone").Where(z => z.Attribute("id").Value == zoneId).Select(e => e.Parent.Attribute("name").Value);

            if (results.Any())
            {
                return (GameExpansion)Enum.Parse(typeof(GameExpansion), results.First().Replace(" ", ""), true);
            }

            return GameExpansion.Unknown;
        }

        public static ZoneSelection GetZoneSelectionByZoneId(string zoneId)
        {
            var results = ZonesXml.Descendants("zone").Where(z => z.Attribute("id").Value == zoneId);

            if (results.Any())
            {
                var expansion = results.First().Parent.Attribute("name").Value;
                var realm = results.First().Parent.Parent.Attribute("name").Value;
                return new ZoneSelection(zoneId, results.First().Value, expansion, realm, results.First().Attribute("type").Value);
            }
            else
            {
                throw new Exception("Unable to resolve zone.");
            }
        }

        #endregion

        #region Values from Dat-Files

        public static List<string> GetFileContent(string mpkFile, string filename)
        {
            var lines = new List<string>();

            using (var csv = MpkWrapper.GetFileFromMpk(mpkFile, filename))
            {
                string row;
                while ((row = csv.ReadLine()) != null)
                {
                    lines.Add(row);
                }
            }

            return lines;
        }

        #endregion

    }
}
