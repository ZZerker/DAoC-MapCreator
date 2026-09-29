using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapCreator.Classes
{
    /// <summary>
    /// The named presets, stored as JSON in %LOCALAPPDATA%\MapCreator\presets.json (written on Save, not created before)
    /// </summary>
    internal sealed class PresetStore
    {
        public static string DefaultPath { get; } = Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath), "presets.json");

        private readonly string path;
        private readonly List<Preset> presets;
        private readonly bool writable;

        private PresetStore(string path, List<Preset> presets, bool writable = true)
        {
            this.path = path;
            this.presets = presets;
            this.writable = writable;
        }

        public IReadOnlyList<Preset> Presets => this.presets;

        /// <summary>
        /// A missing file gives no presets; a broken one is kept as .bad and gives no presets either; an unreadable one (locked) is never overwritten
        /// </summary>
        public static PresetStore Load(string path)
        {
            var presets = new List<Preset>();
            if (File.Exists(path))
            {
                string error;
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(path), AppSettings.JsonOptions);
                    if (loaded != null)
                    {
                        foreach (var preset in loaded)
                        {
                            if (preset == null)
                            {
                                continue;
                            }

                            preset.Name = (preset.Name ?? "").Trim();
                            if (preset.Name.Length == 0 || presets.Any(p => SameName(p.Name, preset.Name)))
                            {
                                continue;
                            }

                            preset.FillInvalid();
                            presets.Add(preset);
                        }

                        return new PresetStore(path, presets);
                    }

                    error = "empty";
                }
                catch (JsonException ex)
                {
                    error = ex.Message;
                    presets.Clear();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppLog.Log(string.Format("Presets file {0} could not be read, presets are not saved this session ({1})", path, ex.Message), LogLevel.Warning);
                    return new PresetStore(path, new List<Preset>(), false);
                }

                AppLog.Log(string.Format("Presets file {0} is unreadable, starting without presets ({1})", path, error), LogLevel.Warning);
                AppSettings.KeepBadCopy(path);
            }

            return new PresetStore(path, presets);
        }

        public Preset Find(string name)
        {
            return this.presets.FirstOrDefault(p => SameName(p.Name, name));
        }

        /// <summary>
        /// Appends a preset with the current options; returns an error text or null
        /// </summary>
        public string Add(string name, AppSettings settings)
        {
            name = (name ?? "").Trim();
            var error = this.Validate(name, null);
            if (error != null)
            {
                return error;
            }

            var preset = new Preset { Name = name };
            preset.CopyFrom(settings);
            this.presets.Add(preset);
            return null;
        }

        public void Overwrite(string name, AppSettings settings)
        {
            this.Find(name)?.CopyFrom(settings);
        }

        /// <summary>
        /// Returns an error text or null
        /// </summary>
        public string Rename(string name, string newName)
        {
            var preset = this.Find(name);
            newName = (newName ?? "").Trim();
            var error = this.Validate(newName, preset);
            if (error != null || preset == null)
            {
                return error;
            }

            preset.Name = newName;
            return null;
        }

        public void Delete(string name)
        {
            this.presets.RemoveAll(p => SameName(p.Name, name));
        }

        /// <summary>
        /// Throws on IO errors, and when the file could not be read at load
        /// </summary>
        public void Save()
        {
            if (!this.writable)
            {
                throw new InvalidOperationException("The presets file could not be read, so it is not overwritten");
            }

            AppSettings.WriteAtomic(this.path, JsonSerializer.Serialize(this.presets, AppSettings.JsonOptions));
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private string Validate(string name, Preset except)
        {
            if (name.Length == 0)
            {
                return "Enter a name";
            }

            if (this.presets.Any(p => p != except && SameName(p.Name, name)))
            {
                return "A preset with this name exists";
            }

            return null;
        }
    }
}
