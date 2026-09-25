using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MapCreator.Classes.MapCreation.Fixtures
{
    /// <summary>
    /// Average colors of model textures, shared by all zones
    /// </summary>
    internal static class TextureColors
    {
        private static readonly string[] TextureExtensions = { ".dds", ".tga", ".bmp" };

        // Searched after the folder of the model's own archive
        private static readonly string[] SharedTextureFolders =
        {
            "zones\\Nifs", "frontiers\\NIFS", "zones\\Dnifs", "frontiers\\dnifs", "phousing\\nifs", "Newtowns\\zones\\Nifs",
            "Tutorial\\zones\\nifs", "zones\\textures", "frontiers\\zones\\textures", "phousing\\textures"
        };

        // Folder > texture name without extension > file
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> FolderIndex = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, Color?> Colors = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Average color of a texture, null if the file cannot be found or read
        /// </summary>
        public static Color? Get(string texture, string modelDirectory)
        {
            if (string.IsNullOrEmpty(texture))
            {
                return null;
            }

            var file = Find(Path.GetFileNameWithoutExtension(texture), modelDirectory);
            if (file == null)
            {
                return null;
            }

            return Colors.GetOrAdd(file, Average);
        }

        private static string Find(string name, string modelDirectory)
        {
            var gamePath = Properties.Settings.Default.game_path;
            var folders = SharedTextureFolders.Select(f => Path.Combine(gamePath, f));
            if (!string.IsNullOrEmpty(modelDirectory))
            {
                folders = folders.Prepend(modelDirectory);
            }

            foreach (var folder in folders)
            {
                if (GetIndex(folder).TryGetValue(name, out var file))
                {
                    return file;
                }
            }
            return null;
        }

        private static Dictionary<string, string> GetIndex(string folder)
        {
            return FolderIndex.GetOrAdd(folder, dir =>
            {
                var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!Directory.Exists(dir))
                {
                    return index;
                }

                foreach (var file in Directory.EnumerateFiles(dir).Where(f => TextureExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                {
                    index.TryAdd(Path.GetFileNameWithoutExtension(file), file);
                }
                return index;
            });
        }

        private static Color? Average(string file)
        {
            try
            {
                using var image = new ImageMagick.MagickImage(file);
                image.Resize(1, 1);
                // Opaque, the texture's alpha only masks leaves and fences
                return Color.FromArgb(255, image.GetPixels().First().ToColor().ToSystemColor());
            }
            catch (Exception ex) when (ex is ImageMagick.MagickException or IOException)
            {
                AppLog.Log(string.Format("Unable to read texture {0}: {1}", file, ex.Message), LogLevel.Warning);
                return null;
            }
        }
    }
}
