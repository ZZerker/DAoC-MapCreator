using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MapCreator.Classes.Rendering
{
    /// <summary>
    /// A folder with zone maps under the output folder
    /// </summary>
    public sealed record RenderFolder(string Path, string Name, int ZoneCount, DateTime LastWrite)
    {
        public string Label => string.Format(CultureInfo.InvariantCulture, "{0}  ·  {1:yyyy-MM-dd HH:mm}  ·  {2} zones", this.Name, this.LastWrite, this.ZoneCount);
    }

    /// <summary>
    /// Finds the render folders and a zone's map in one of them
    /// </summary>
    internal static class RenderFolders
    {
        // z163.png (batch), zone163_2048.png (window default pattern)
        private static readonly Regex MapName = new(@"^z(?:one)?(\d{3})(?:_\d+)?\.(?:png|jpe?g)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The direct subfolders of the output folder that hold zone maps, newest map first
        /// </summary>
        public static List<RenderFolder> List(string outputPath)
        {
            var folders = new List<RenderFolder>();
            if (string.IsNullOrEmpty(outputPath) || !Directory.Exists(outputPath))
            {
                return folders;
            }

            foreach (var directory in new DirectoryInfo(outputPath).EnumerateDirectories())
            {
                try
                {
                    var maps = directory.EnumerateFiles().Where(f => MapName.IsMatch(f.Name)).ToList();
                    if (maps.Count == 0)
                    {
                        continue;
                    }

                    var zones = maps.Select(f => MapName.Match(f.Name).Groups[1].Value).Distinct().Count();
                    folders.Add(new RenderFolder(directory.FullName, directory.Name, zones, maps.Max(f => f.LastWriteTime)));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A folder that cannot be read is not offered
                }
            }

            return folders.OrderByDescending(f => f.LastWrite).ToList();
        }

        /// <summary>
        /// The zone's map in the folder: the name the file pattern gives, else z{id}.png or .jpg; a missing map returns the pattern name
        /// </summary>
        public static FileInfo FindMap(string folder, RenderSettings settings, ZoneSelection zone)
        {
            var patternName = ZoneRenderer.GetTargetFile(settings, zone).Name;
            var candidates = new[] { patternName, "z" + zone.Id + ".png", "z" + zone.Id + ".jpg" };
            foreach (var name in candidates)
            {
                var file = new FileInfo(System.IO.Path.Combine(folder, name));
                if (file.Exists)
                {
                    return file;
                }
            }

            return new FileInfo(System.IO.Path.Combine(folder, patternName));
        }

        /// <summary>
        /// The folder a render with these settings writes into (null when the subfolder pattern depends on the zone)
        /// </summary>
        public static string TargetFolder(RenderSettings settings)
        {
            var pattern = string.IsNullOrEmpty(settings.DirectoryPattern) ? "maps" : settings.DirectoryPattern;
            return pattern.Contains('{') ? null : System.IO.Path.Combine(settings.TargetPath, Tools.MakeValidDirectoryName(pattern));
        }
    }
}
