using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MapCreator.Classes.MapCreation.Fixtures.Objects;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// New Frontiers keeps and towers: pieces from data\Keeps.csv (DOL db-public), models and textures from the client tables in frontiers.mpk
    /// </summary>
    internal static class KeepPieces
    {
        // Undamaged keeps of the highest tier (keep level 10)
        private const int TIER = 4;

        // Keep pieces sit on a grid of this many units (DOL GameKeepComponent)
        private const double GRID = 148;

        private const int FIRST_NIF_ID = 100000;

        private static readonly string[] Realms = { "alb", "mid", "hib" };

        private static readonly Lazy<Tables> ClientTables = new Lazy<Tables>(LoadTables);

        public sealed record Piece(NifRow Nif, FixtureRow Fixture, double Heading);

        private sealed record KeepRow(int Region, string Name, int Realm, double X, double Y, double Z, double Heading, int Skin, int PieceX, int PieceY, int PieceHeading);

        private sealed record Tables(Dictionary<int, string> ModelBySkin, Dictionary<string, string[]> Materials, Dictionary<int, string> Textures);

        /// <summary>
        /// Keep pieces inside the zone, in zone coordinates
        /// </summary>
        public static List<Piece> Load(string zoneId, IRenderReporter reporter)
        {
            var pieces = new List<Piece>();
            var zone = GetZoneArea(zoneId);
            if (zone == null)
            {
                return pieces;
            }

            var (region, left, top, size) = zone.Value;
            var rows = LoadKeepRows().Where(r => r.Region == region).ToList();
            if (rows.Count == 0)
            {
                return pieces;
            }

            var tables = ClientTables.Value;
            var nifRows = new Dictionary<string, NifRow>();
            foreach (var row in rows)
            {
                var angle = row.Heading * Math.PI / 180d;
                var offsetX = GRID * row.PieceX;
                var offsetY = -GRID * row.PieceY;
                var x = row.X + Math.Cos(angle) * offsetX - Math.Sin(angle) * offsetY - left;
                var y = row.Y + Math.Cos(angle) * offsetY + Math.Sin(angle) * offsetX - top;
                if (x < 0 || y < 0 || x >= size || y >= size)
                {
                    continue;
                }

                if (!tables.ModelBySkin.TryGetValue(row.Skin, out var model))
                {
                    reporter.Log(string.Format("Keep piece skin {0} of {1} not in fr_tiers.csv", row.Skin, row.Name), LogLevel.Warning);
                    continue;
                }

                var realm = Math.Clamp(row.Realm, 1, 3) - 1;
                var key = model + "_" + Realms[realm];
                if (!nifRows.TryGetValue(key, out var nifRow))
                {
                    nifRow = new NifRow
                             {
                                 NifId = FIRST_NIF_ID + nifRows.Count,
                                 TextualName = model,
                                 Filename = GetNifName(model),
                                 Variant = Realms[realm] + TIER,
                                 IsNodeDrawable = name => IsNodeDrawable(name, Realms[realm]),
                                 ResolveTexture = (material, texture) => ResolveTexture(tables, material, texture, realm)
                             };
                    nifRows[key] = nifRow;
                }

                var fixture = new FixtureRow { Id = FIRST_NIF_ID + pieces.Count, NifId = nifRow.NifId, TextualName = row.Name, X = x, Y = y, Z = row.Z, Scale = 100 };
                pieces.Add(new Piece(nifRow, fixture, row.Heading + row.PieceHeading * 90));
            }

            reporter.Log(string.Format("{0} keep pieces", pieces.Count), LogLevel.Notice);
            return pieces;
        }

        /// <summary>
        /// Keep models hold every realm, tier and damage state as named node groups: "!Tiers_3-4_ buttresses", "!Alb_cake_undamaged", "!Tiers_4_hib01 DAM"
        /// </summary>
        private static bool IsNodeDrawable(string nodeName, string realm)
        {
            var name = nodeName.ToLowerInvariant();
            if (name.StartsWith("!collidee") || name.StartsWith("targetchest") || name.StartsWith("sclimb") || name.StartsWith("!_distbld"))
            {
                return false;
            }

            var tiers = Regex.Match(name, @"^!tiers_([\d-]+)");
            if (tiers.Success && !tiers.Groups[1].Value.Split('-').Contains(TIER.ToString()))
            {
                return false;
            }

            foreach (var token in Regex.Split(name, @"[^a-z0-9]+"))
            {
                if (token == "dam" || token == "damaged" || token.StartsWith("rubble"))
                {
                    return false;
                }

                var other = Realms.FirstOrDefault(r => r != realm && token.StartsWith(r) && token.Length <= 5);
                if (other != null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Placeholder materials ("!nf_main_wall_longwall") take their texture by realm and tier from fr_materials.csv
        /// </summary>
        private static string ResolveTexture(Tables tables, string material, string texture, int realm)
        {
            if (material == null || !tables.Materials.TryGetValue(material, out var fields))
            {
                return texture;
            }

            // id, name, then Base/Dark/Detail/Glow for Alb, Mid, Hib and for each realm and tier
            var column = 14 + (realm * 4 + TIER - 1) * 4;
            if (column < fields.Length && int.TryParse(fields[column], out var textureId) && tables.Textures.TryGetValue(textureId, out var file))
            {
                return file + ".dds";
            }
            return texture;
        }

        // Archive entries differ in case (mainKeep_lvl4.NIF)
        private static string GetNifName(string model)
        {
            var archive = Path.Combine(Properties.Settings.Default.game_path, "frontiers", "NIFS", model + ".npk");
            return File.Exists(archive) ? MpkWrapper.Open(archive).Files.Select(f => f.Name).FirstOrDefault(n => n.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) ?? model + ".nif" : model + ".nif";
        }

        private static (int Region, double Left, double Top, double Size)? GetZoneArea(string zoneId)
        {
            var zonesDat = DatFile.FromMpk(Path.Combine(Properties.Settings.Default.game_path, "zones", "zones.mpk"), "zones.dat");
            var section = "zone" + zoneId;
            if (zonesDat == null || zonesDat.Get(section, "frontiers") != "1"
                || !int.TryParse(zonesDat.Get(section, "region"), out var region)
                || !int.TryParse(zonesDat.Get(section, "region_offset_x"), out var offsetX)
                || !int.TryParse(zonesDat.Get(section, "region_offset_y"), out var offsetY)
                || !int.TryParse(zonesDat.Get(section, "width"), out var width))
            {
                return null;
            }
            return (region, offsetX * 8192d, offsetY * 8192d, width * 8192d);
        }

        private static List<KeepRow> LoadKeepRows()
        {
            var rows = new List<KeepRow>();
            var file = Path.Combine(System.Windows.Forms.Application.StartupPath, "data", "Keeps.csv");
            if (!File.Exists(file))
            {
                return rows;
            }

            foreach (var line in File.ReadLines(file).Skip(1).Where(l => l.Length > 0 && !l.StartsWith('#')))
            {
                var fields = line.Split(',');
                if (fields.Length < 12 || !int.TryParse(fields[0], out var region))
                {
                    continue;
                }

                var numbers = fields.Skip(3).Select(f => double.Parse(f, CultureInfo.InvariantCulture)).ToArray();
                rows.Add(new KeepRow(region, fields[2], (int)numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], (int)numbers[5], (int)numbers[6], (int)numbers[7], (int)numbers[8]));
            }
            return rows;
        }

        private static Tables LoadTables()
        {
            var mpk = Path.Combine(Properties.Settings.Default.game_path, "frontiers", "frontiers.mpk");
            var tables = new Tables(new Dictionary<int, string>(), new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase), new Dictionary<int, string>());

            var pieceFiles = new Dictionary<int, string>();
            foreach (var fields in ReadCsv(mpk, "fr_pieces.csv"))
            {
                if (fields.Length > 2 && int.TryParse(fields[0], out var id))
                {
                    pieceFiles[id] = fields[2];
                }
            }

            // Row per skin, one column per tier; values are zero based piece ids
            foreach (var fields in ReadCsv(mpk, "fr_tiers.csv"))
            {
                if (fields.Length > TIER + 1 && int.TryParse(fields[0], out var skin) && int.TryParse(fields[TIER + 1], out var piece) && pieceFiles.TryGetValue(piece + 1, out var file))
                {
                    tables.ModelBySkin[skin] = file;
                }
            }

            foreach (var fields in ReadCsv(mpk, "fr_materials.csv"))
            {
                if (fields.Length > 2 && int.TryParse(fields[0], out _))
                {
                    tables.Materials[fields[1]] = fields;
                }
            }

            foreach (var fields in ReadCsv(mpk, "fr_textures.csv"))
            {
                if (fields.Length > 2 && int.TryParse(fields[0], out var id))
                {
                    tables.Textures[id] = fields[2];
                }
            }
            return tables;
        }

        private static IEnumerable<string[]> ReadCsv(string mpk, string filename)
        {
            return DataWrapper.GetFileContent(mpk, filename).Select(line => line.Split(',').Select(f => f.Trim()).ToArray());
        }
    }
}
