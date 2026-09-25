using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MapCreator.Classes
{
    /// <summary>
    /// Zones as the game client defines them in zones\zones.mpk\zones.dat
    /// </summary>
    internal static class ZoneCatalog
    {
        public const string REALM_NAME = "Client Zones";
        public const string EXPANSION_NAME = "All Expansions";

        // Folders below the game path that contain zoneNNN directories
        private static readonly string[] ZoneRoots = { "zones", "frontiers\\zones", "phousing\\zones", "Tutorial\\zones" };

        public record ClientZone(string Id, string Name, string Type);

        public static List<ClientZone> Load(string gamePath)
        {
            var zonesMpk = Path.Combine(gamePath, "zones", "zones.mpk");
            if (!File.Exists(zonesMpk))
            {
                return new List<ClientZone>();
            }

            var zonesDat = DatFile.FromMpk(zonesMpk, "zones.dat");
            if (zonesDat == null)
            {
                return new List<ClientZone>();
            }

            var zones = new List<ClientZone>();
            foreach (var section in zonesDat.Sections.Where(s => s.StartsWith("zone") && int.TryParse(s[4..], out _)))
            {
                var id = int.Parse(section[4..]).ToString("000");
                var name = zonesDat.Get(section, "name");
                zones.Add(new ClientZone(id, string.IsNullOrEmpty(name) ? "Zone " + id : name, GetTypeName(zonesDat.Get(section, "type"))));
            }
            return zones;
        }

        /// <summary>
        /// Searches all zone roots for the folder that holds datNNN.mpk
        /// </summary>
        public static string FindZoneDirectory(string gamePath, string zoneId)
        {
            return ZoneRoots
                .Select(root => Path.Combine(gamePath, root, "zone" + zoneId))
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "dat" + zoneId + ".mpk")));
        }

        // Names match the zone types in zones.xml
        private static string GetTypeName(string type)
        {
            return type switch
            {
                "1" => "Capitol",
                "2" => "Dungeons",
                "4" => "Indoor",
                _ => "Outdoor"
            };
        }
    }
}
