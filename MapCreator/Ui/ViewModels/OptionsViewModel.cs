using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using MapCreator.Classes;

namespace MapCreator.Ui.ViewModels
{
    /// <summary>
    /// The render options, read from and written to AppSettings.Current
    /// </summary>
    public sealed partial class OptionsViewModel : ViewModelBase
    {
        private static readonly int[] StandardSizes = { 512, 1024, 2048, 4096 };

        private readonly AppSettings settings;
        private readonly List<int> sizes;
        private string riversColorText;
        private string boundsColorText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEditable))]
        private bool isRendering;

        [ObservableProperty]
        private bool isRiversColorInvalid;

        [ObservableProperty]
        private bool isBoundsColorInvalid;

        internal OptionsViewModel(AppSettings settings)
            : this(settings, PresetStore.Load(PresetStore.DefaultPath))
        {
        }

        internal OptionsViewModel(AppSettings settings, PresetStore presetStore)
        {
            this.settings = settings;
            this.sizes = StandardSizes.Union(new[] { settings.MapSize }).Union(presetStore.Presets.Select(p => p.MapSize)).OrderBy(s => s).ToList();
            this.SizeChoices = this.sizes.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToList();
            this.riversColorText = AppSettings.ToHexColor(settings.RiversColor);
            this.boundsColorText = AppSettings.ToHexColor(settings.BoundsColor);
            this.Presets = new PresetsViewModel(this, settings, presetStore);
        }

        /// <summary>
        /// Raised when an option changes where or under which name the maps are written
        /// </summary>
        public event Action OutputChanged;

        public bool IsEditable => !this.IsRendering;

        public PresetsViewModel Presets { get; }

        public IReadOnlyList<string> SizeChoices { get; }

        // Output
        public string TargetPath
        {
            get => this.settings.TargetPath;
            set => this.UpdateOutput(this.settings.TargetPath, value ?? "", v => this.settings.TargetPath = v);
        }

        public string TargetDirectoryPattern
        {
            get => this.settings.TargetDirectoryPattern;
            set => this.UpdateOutput(this.settings.TargetDirectoryPattern, value ?? "", v => this.settings.TargetDirectoryPattern = v);
        }

        public string MapFilePattern
        {
            get => this.settings.MapFilePattern;
            set => this.UpdateOutput(this.settings.MapFilePattern, value ?? "", v => this.settings.MapFilePattern = v);
        }

        // 0 PNG, 1 JPG; the setting keeps the old names
        public int FormatIndex
        {
            get => this.settings.MapType == "PNG" ? 0 : 1;
            set
            {
                if (value < 0)
                {
                    return;
                }

                if (this.UpdateOutput(this.settings.MapType, value == 0 ? "PNG" : "JPEG", v => this.settings.MapType = v))
                {
                    this.OnPropertyChanged(nameof(this.IsQualityEnabled));
                }
            }
        }

        public bool IsQualityEnabled => this.settings.MapType != "PNG";

        public decimal MapQuality
        {
            get => this.settings.MapQuality;
            set => this.Update(this.settings.MapQuality, (int)value, v => this.settings.MapQuality = v);
        }

        public string SelectedSize
        {
            get => this.settings.MapSize.ToString(CultureInfo.InvariantCulture);
            set
            {
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && this.sizes.Contains(size))
                {
                    this.UpdateOutput(this.settings.MapSize, size, v => this.settings.MapSize = v);
                }
            }
        }

        public decimal RenderParallel
        {
            get => this.settings.RenderParallel;
            set => this.Update(this.settings.RenderParallel, (int)value, v => this.settings.RenderParallel = v);
        }

        public bool SkipIfFileExists
        {
            get => this.settings.SkipIfFileExists;
            set => this.Update(this.settings.SkipIfFileExists, value, v => this.settings.SkipIfFileExists = v);
        }

        // Terrain and light
        public bool DrawBackground
        {
            get => this.settings.DrawBackground;
            set => this.Update(this.settings.DrawBackground, value, v => this.settings.DrawBackground = v);
        }

        public bool Lightmap
        {
            get => this.settings.Lightmap;
            set => this.Update(this.settings.Lightmap, value, v => this.settings.Lightmap = v);
        }

        public decimal LightmapZScale
        {
            get => (decimal)this.settings.LightmapZScale;
            set => this.Update(this.settings.LightmapZScale, (double)value, v => this.settings.LightmapZScale = v);
        }

        public decimal LightmapMinLight
        {
            get => (decimal)this.settings.LightmapMinLight;
            set => this.Update(this.settings.LightmapMinLight, (double)value, v => this.settings.LightmapMinLight = v);
        }

        public decimal LightmapMaxLight
        {
            get => (decimal)this.settings.LightmapMaxLight;
            set => this.Update(this.settings.LightmapMaxLight, (double)value, v => this.settings.LightmapMaxLight = v);
        }

        public decimal LightmapZVectorX
        {
            get => (decimal)this.settings.LightmapZVector[0];
            set => this.Update(this.settings.LightmapZVector[0], (double)value, v => this.SetZVector(0, v));
        }

        public decimal LightmapZVectorY
        {
            get => (decimal)this.settings.LightmapZVector[1];
            set => this.Update(this.settings.LightmapZVector[1], (double)value, v => this.SetZVector(1, v));
        }

        public decimal LightmapZVectorZ
        {
            get => (decimal)this.settings.LightmapZVector[2];
            set => this.Update(this.settings.LightmapZVector[2], (double)value, v => this.SetZVector(2, v));
        }

        // Water
        public bool Rivers
        {
            get => this.settings.Rivers;
            set
            {
                if (this.Update(this.settings.Rivers, value, v => this.settings.Rivers = v))
                {
                    this.OnPropertyChanged(nameof(this.IsRiversColorEnabled));
                }
            }
        }

        public bool RiversUseDefaultColor
        {
            get => this.settings.RiversUseDefaultColor;
            set
            {
                if (this.Update(this.settings.RiversUseDefaultColor, value, v => this.settings.RiversUseDefaultColor = v))
                {
                    this.OnPropertyChanged(nameof(this.IsRiversColorEnabled));
                }
            }
        }

        public bool IsRiversColorEnabled => this.settings.Rivers && !this.settings.RiversUseDefaultColor;

        public string RiversColorText
        {
            get => this.riversColorText;
            set
            {
                if (this.SetProperty(ref this.riversColorText, value))
                {
                    this.IsRiversColorInvalid = !this.UpdateColor(value, this.settings.RiversColor, c => this.settings.RiversColor = c, nameof(this.RiversSwatch));
                }
            }
        }

        public IBrush RiversSwatch => ToBrush(this.settings.RiversColor);

        public decimal RiverOpacity
        {
            get => this.settings.RiverOpacity;
            set => this.Update(this.settings.RiverOpacity, (int)value, v => this.settings.RiverOpacity = v);
        }

        public bool DepthShadedWater
        {
            get => this.settings.DepthShadedWater;
            set => this.Update(this.settings.DepthShadedWater, value, v => this.settings.DepthShadedWater = v);
        }

        // Bounds
        public bool Bounds
        {
            get => this.settings.Bounds;
            set => this.Update(this.settings.Bounds, value, v => this.settings.Bounds = v);
        }

        public string BoundsColorText
        {
            get => this.boundsColorText;
            set
            {
                if (this.SetProperty(ref this.boundsColorText, value))
                {
                    this.IsBoundsColorInvalid = !this.UpdateColor(value, this.settings.BoundsColor, c => this.settings.BoundsColor = c, nameof(this.BoundsSwatch));
                }
            }
        }

        public IBrush BoundsSwatch => ToBrush(this.settings.BoundsColor);

        public decimal BoundsOpacity
        {
            get => this.settings.BoundsOpacity;
            set => this.Update(this.settings.BoundsOpacity, (int)value, v => this.settings.BoundsOpacity = v);
        }

        public bool ExcludeBoundsFromMap
        {
            get => this.settings.ExcludeBoundsFromMap;
            set => this.Update(this.settings.ExcludeBoundsFromMap, value, v => this.settings.ExcludeBoundsFromMap = v);
        }

        // Models
        public bool DrawFixtures
        {
            get => this.settings.DrawFixtures;
            set => this.Update(this.settings.DrawFixtures, value, v => this.settings.DrawFixtures = v);
        }

        public bool DrawFixturesBelowWater
        {
            get => this.settings.DrawFixturesBelowWater;
            set => this.Update(this.settings.DrawFixturesBelowWater, value, v => this.settings.DrawFixturesBelowWater = v);
        }

        public bool DrawKeeps
        {
            get => this.settings.DrawKeeps;
            set => this.Update(this.settings.DrawKeeps, value, v => this.settings.DrawKeeps = v);
        }

        public bool ObliqueKeeps
        {
            get => this.settings.ObliqueKeeps;
            set => this.Update(this.settings.ObliqueKeeps, value, v => this.settings.ObliqueKeeps = v);
        }

        public bool AmbientOcclusion
        {
            get => this.settings.AmbientOcclusion;
            set => this.Update(this.settings.AmbientOcclusion, value, v => this.settings.AmbientOcclusion = v);
        }

        public bool DrawTrees
        {
            get => this.settings.DrawTrees;
            set => this.Update(this.settings.DrawTrees, value, v => this.settings.DrawTrees = v);
        }

        public decimal TreeTransparency
        {
            get => this.settings.TreeTransparency;
            set => this.Update(this.settings.TreeTransparency, (int)value, v => this.settings.TreeTransparency = v);
        }

        // Labels
        public string LabelDirectory
        {
            get => this.settings.LabelDirectory;
            set
            {
                if (this.Update(this.settings.LabelDirectory, value ?? "", v => this.settings.LabelDirectory = v))
                {
                    this.OnPropertyChanged(nameof(this.HasLabelDirectory));
                }
            }
        }

        public bool HasLabelDirectory => !string.IsNullOrEmpty(this.settings.LabelDirectory);

        public decimal LabelSize
        {
            get => this.settings.LabelSize;
            set => this.Update(this.settings.LabelSize, (int)value, v => this.settings.LabelSize = v);
        }

        /// <summary>
        /// Shows the values in AppSettings after a preset was copied into it
        /// </summary>
        internal void ReloadFromSettings()
        {
            this.riversColorText = AppSettings.ToHexColor(this.settings.RiversColor);
            this.boundsColorText = AppSettings.ToHexColor(this.settings.BoundsColor);
            this.IsRiversColorInvalid = false;
            this.IsBoundsColorInvalid = false;
            this.OnPropertyChanged(string.Empty);
            this.OutputChanged?.Invoke();
            SettingsSaver.Request();
        }

        /// <summary>
        /// "#RRGGBB", the '#' may be left out
        /// </summary>
        internal static bool TryParseColor(string text, out System.Drawing.Color color)
        {
            var trimmed = (text ?? "").Trim();
            if (!trimmed.StartsWith('#'))
            {
                trimmed = "#" + trimmed;
            }

            return AppSettings.TryParseHexColor(trimmed, out color);
        }

        private static IBrush ToBrush(System.Drawing.Color color)
        {
            return new ImmutableSolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }

        private bool UpdateColor(string text, System.Drawing.Color current, Action<System.Drawing.Color> assign, string swatchName)
        {
            if (!TryParseColor(text, out var color))
            {
                return false;
            }

            this.Update(current.ToArgb(), color.ToArgb(), v => assign(color), swatchName);
            return true;
        }

        private void SetZVector(int index, double value)
        {
            var vector = (double[])this.settings.LightmapZVector.Clone();
            vector[index] = value;
            this.settings.LightmapZVector = vector;
        }

        private bool UpdateOutput<T>(T current, T value, Action<T> assign, [CallerMemberName] string propertyName = null)
        {
            if (!this.Update(current, value, assign, propertyName))
            {
                return false;
            }

            this.OutputChanged?.Invoke();
            return true;
        }

        private bool Update<T>(T current, T value, Action<T> assign, [CallerMemberName] string propertyName = null)
        {
            if (this.IsRendering || EqualityComparer<T>.Default.Equals(current, value))
            {
                return false;
            }

            assign(value);
            this.OnPropertyChanged(propertyName);
            SettingsSaver.Request();
            return true;
        }
    }
}
