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
using System.Text.RegularExpressions;
using MPKLib;

namespace MapCreator.Classes
{
	internal static class MpkWrapper
    {

        public static bool CheckGamePath()
        {
            var checkFile = string.Format("{0}\\{1}", Properties.Settings.Default.game_path, "camelot.exe");
            if (!File.Exists(checkFile))
            {
                MainForm.Log("camelot.exe not found in gamepath!");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets zones from zones.dat
        /// </summary>
        /// <returns></returns>
        public static Dictionary<int, string> GetZones()
        {
            if (!CheckGamePath())
            {
                return null;
            }

            var zones = new Dictionary<int, string>();
            var zonesMpk = string.Format("{0}\\zones\\zones.mpk", Properties.Settings.Default.game_path);

            var mpak = new MPAK();
            mpak.Load(zonesMpk);

            var zonesFile = mpak.GetFile("zones.dat");

            using (Stream stream = new MemoryStream(zonesFile.Data))
            {
                using (var reader = new StreamReader(stream))
                {
                    string row;
                    var recording = false;

                    var currentZone = 0;
                    var currentZoneName = "";
                    Match match;

                    while ((row = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(row.Trim())) continue;

                        // Sections
                        // ALBION

                        if (row.StartsWith("[zone"))
                        {
                            recording = true;
                            var regex = new Regex(@"\[zone(.*)\]", RegexOptions.IgnoreCase);
                            if ((match = regex.Match(row)) != null)
                            {
                                currentZone = Convert.ToInt32(match.Groups[1].Value);
                            }
                        }

                        if (recording && row.StartsWith("name="))
                        {
                            var regex = new Regex(@"name=(.*)", RegexOptions.IgnoreCase);
                            if ((match = regex.Match(row)) != null)
                            {
                                currentZoneName = match.Groups[1].Value;
                            }
                        }

                        if (recording && currentZone > 0 && currentZoneName != "")
                        {
                            zones.Add(currentZone, currentZoneName);
                            currentZone = 0;
                            currentZoneName = "";
                            recording = false;
                        }

                    }
                }
            }
            
            return zones;
        }

        public static StreamReader GetFileFromMpk(string mpk, string filename)
        {
            var mpak = new MPAK();
            mpak.Load(mpk);

            if (mpak.Files.Any(f => f.Name.ToLower() == filename.ToLower()))
            {
                return new StreamReader(new MemoryStream(mpak.GetFile(filename).Data));
            }

            return null;
        }

        public static Byte[] GetFileBytesFromMpk(string mpk, string filename)
        {
            var mpak = new MPAK();
            mpak.Load(mpk);
            return mpak.GetFile(filename).Data;
        }


    }
}
