using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapCreator.Classes
{
    /// <summary>
    /// Finds a DAoC client folder (a folder containing camelot.exe)
    /// </summary>
    internal static class GameFolderLocator
    {
        public static bool IsGameFolder(string path)
        {
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "camelot.exe"));
        }

        public static string Find()
        {
            return Candidates().FirstOrDefault(IsGameFolder);
        }

        private static IEnumerable<string> Candidates()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return ReadLauncherKey(Path.Combine(appData, "eden-launcher", "config.json"), "gameDir");
            yield return ReadLauncherKey(Path.Combine(appData, "bt-launcher", "config.json"), "gamePath");

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFilesX86, "Electronic Arts", "Dark Age of Camelot");
            yield return Path.Combine(programFiles, "Electronic Arts", "Dark Age of Camelot");
            yield return @"C:\Spiele\Eden DAoC";
        }

        private static string ReadLauncherKey(string configPath, string key)
        {
            if (!File.Exists(configPath))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(configPath);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
            }

            return null;
        }
    }
}
