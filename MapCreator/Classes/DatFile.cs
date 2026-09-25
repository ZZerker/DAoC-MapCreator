using System;
using System.Collections.Generic;
using System.IO;

namespace MapCreator.Classes
{
    /// <summary>
    /// Ini style .dat file (sector.dat) parsed into sections
    /// </summary>
    public class DatFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.OrdinalIgnoreCase);

        public DatFile(TextReader reader)
        {
            Dictionary<string, string> section = null;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var row = line.Trim();
                if (row.Length == 0 || row.StartsWith(';'))
                {
                    continue;
                }

                if (row.StartsWith('['))
                {
                    var name = row.TrimStart('[').Split(']')[0];
                    if (!this.sections.TryGetValue(name, out section))
                    {
                        section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        this.sections.Add(name, section);
                    }
                    continue;
                }

                var separator = row.IndexOf('=');
                if (section == null || separator <= 0)
                {
                    continue;
                }

                section.TryAdd(row[..separator].Trim(), row[(separator + 1)..].Trim());
            }
        }

        public IEnumerable<string> Sections => this.sections.Keys;

        public static DatFile FromMpk(string mpk, string filename)
        {
            using var reader = MpkWrapper.GetFileFromMpk(mpk, filename);
            return reader == null ? null : new DatFile(reader);
        }

        /// <summary>
        /// Gets a value, or an empty string if the section or key does not exist
        /// </summary>
        public string Get(string section, string key)
        {
            if (this.sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value))
            {
                return value;
            }
            return "";
        }
    }
}
