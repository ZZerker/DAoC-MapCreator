using System;
using System.Collections.Generic;
using System.Linq;

namespace MapCreator.Classes
{
    /// <summary>
    /// Resolves the zone terms of the batch mode: zone ids, "all", realms, expansions, zone types and presets.
    /// Terms joined by "+" are intersected: "nf+outdoor" is New Frontiers without the battlegrounds.
    /// </summary>
    internal static class ZoneGroups
    {
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alb"] = "albion",
            ["mid"] = "midgard",
            ["hib"] = "hibernia",
            ["nf"] = "newfrontiers",
            ["of"] = "oldfrontiers",
            ["si"] = "shroudedisles",
            ["toa"] = "trialsofatlantis",
            ["cata"] = "catacombs",
            ["dr"] = "darknessrising",
            ["lotm"] = "labyrithoftheminotaur",
            ["city"] = "capitol",
            ["cities"] = "capitol",
            ["dungeon"] = "dungeons",
            ["instance"] = "instances",
            ["bg"] = "battlegrounds",
            ["client"] = "clientzones"
        };

        public static IEnumerable<string> Resolve(string terms, Action<string> warn)
        {
            var result = new List<string>();
            foreach (var term in terms.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))
            {
                HashSet<string> ids = null;
                foreach (var part in term.Split('+').Select(p => p.Trim()))
                {
                    var matches = ResolvePart(part);
                    if (matches == null)
                    {
                        warn(string.Format("Unknown zone or group \"{0}\"", part));
                        matches = new HashSet<string>();
                    }
                    if (ids == null)
                    {
                        ids = matches;
                    }
                    else
                    {
                        ids.IntersectWith(matches);
                    }
                }
                result.AddRange(ids.OrderBy(id => id).Where(id => !result.Contains(id)));
            }
            return result;
        }

        private static HashSet<string> ResolvePart(string part)
        {
            if (part.All(char.IsDigit))
            {
                return new HashSet<string> { part.PadLeft(3, '0') };
            }

            var key = Normalize(part);
            if (Aliases.TryGetValue(key, out var alias))
            {
                key = alias;
            }

            var zones = DataWrapper.GetAllZones();
            if (key == "all")
            {
                return zones.Select(z => z.Id).ToHashSet();
            }

            var matches = zones.Where(z => Normalize(z.Realm) == key || Normalize(z.Expansion) == key || Normalize(z.Type) == key).Select(z => z.Id).ToHashSet();
            if (matches.Count > 0)
            {
                return matches;
            }

            var preset = DataWrapper.GetPresetRows().FirstOrDefault(p => Normalize(p.Name) == key);
            return preset?.Zones.Split(',').Select(z => z.Trim()).Where(z => z.Length > 0).ToHashSet();
        }

        private static string Normalize(string name)
        {
            return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }
    }
}
