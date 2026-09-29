using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using MapCreator.Classes;
using MapCreator.Classes.Rendering;

namespace MapCreator.Ui.ViewModels
{
    /// <summary>
    /// Shows the current render of the selected zone, with the labels drawn over it; reloads when the zone's status changes
    /// </summary>
    public sealed partial class MapViewerViewModel : ViewModelBase
    {
        private readonly ZoneBrowserViewModel zoneBrowser;
        private readonly RenderViewModel render;
        private ZoneRowViewModel row;
        private int loadGeneration;

        [ObservableProperty]
        private Bitmap image;

        [ObservableProperty]
        private string title = "";

        [ObservableProperty]
        private string infoText = "";

        [ObservableProperty]
        private string frameText = "";

        [ObservableProperty]
        private string filePath = "";

        [ObservableProperty]
        private string message = "Select a zone";

        [ObservableProperty]
        private bool showLabels = true;

        internal MapViewerViewModel(ZoneBrowserViewModel zoneBrowser, RenderViewModel render)
        {
            this.zoneBrowser = zoneBrowser;
            this.render = render;
            this.zoneBrowser.PropertyChanged += this.OnZoneBrowserChanged;
            this.render.PropertyChanged += this.OnRenderChanged;
        }

        /// <summary>
        /// The zone the current Image belongs to; set before Image changes, so the view can tell a refresh from another zone
        /// </summary>
        public string ShownZoneId { get; private set; }

        partial void OnShowLabelsChanged(bool value)
        {
            this.Reload();
        }

        private bool CanRenderThisZone()
        {
            return this.row != null && !this.render.IsRendering;
        }

        [RelayCommand(CanExecute = nameof(CanRenderThisZone))]
        private void RenderThisZone()
        {
            this.render.RenderZone(this.row.Id);
        }

        [RelayCommand]
        private void OpenFolder()
        {
            try
            {
                var renderSettings = RenderSettings.FromSettings(AppSettings.Current);
                var file = this.row == null ? null : ZoneRenderer.GetTargetFile(renderSettings, this.row.Zone);
                var arguments = file != null && file.Exists ? "/select,\"" + file.FullName + "\"" : "\"" + renderSettings.TargetPath + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arguments));
            }
            catch (Exception ex)
            {
                AppLog.Log("Could not open the folder: " + ex.Message, LogLevel.Warning);
            }
        }

        private void OnZoneBrowserChanged(object sender, PropertyChangedEventArgs e)
        {
            var selected = this.zoneBrowser.SelectedRow;
            if (e.PropertyName != nameof(ZoneBrowserViewModel.SelectedRow) || selected == null || selected == this.row)
            {
                return;
            }

            if (this.row != null)
            {
                this.row.PropertyChanged -= this.OnRowChanged;
            }

            this.row = selected;
            this.row.PropertyChanged += this.OnRowChanged;
            this.Title = this.row.Id + " " + this.row.Name;
            this.RenderThisZoneCommand.NotifyCanExecuteChanged();
            this.Reload();
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ZoneRowViewModel.Status))
            {
                this.Reload();
            }
        }

        private void OnRenderChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RenderViewModel.IsRendering))
            {
                this.RenderThisZoneCommand.NotifyCanExecuteChanged();
            }
        }

        private void Reload()
        {
            var generation = Interlocked.Increment(ref this.loadGeneration);
            var zoneRow = this.row;
            if (zoneRow == null)
            {
                return;
            }

            var withLabels = this.ShowLabels;
            Task.Run(() =>
            {
                var result = Load(zoneRow.Zone, withLabels);
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.loadGeneration != generation)
                    {
                        result.Image?.Dispose();
                        return;
                    }

                    var old = this.Image;
                    this.ShownZoneId = zoneRow.Id;
                    this.InfoText = result.InfoText;
                    this.FrameText = result.FrameText;
                    this.FilePath = result.FilePath;
                    this.Message = result.Message;
                    this.Image = result.Image;
                    old?.Dispose();
                });
            });
        }

        // Runs on a pool thread
        private static LoadResult Load(ZoneSelection zone, bool withLabels)
        {
            try
            {
                var file = ZoneRenderer.GetTargetFile(RenderSettings.FromSettings(AppSettings.Current), zone);
                var frameText = GetFrameText(zone, file);
                if (!file.Exists)
                {
                    return new LoadResult(null, "Not rendered yet", "", frameText, file.FullName);
                }

                var bitmap = withLabels ? LoadWithLabels(file) : LoadPlain(file);
                var info = string.Format(CultureInfo.InvariantCulture, "{0} x {1}, rendered {2:yyyy-MM-dd HH:mm}", bitmap.PixelSize.Width, bitmap.PixelSize.Height, file.LastWriteTime);
                return new LoadResult(bitmap, "", info, frameText, file.FullName);
            }
            catch (Exception ex)
            {
                // Any decoder failure ends as a message, never as an unhandled exception on the pool thread
                return new LoadResult(null, "Cannot read the map: " + ex.Message, "", "", "");
            }
        }

        private static Bitmap LoadPlain(FileInfo file)
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new Bitmap(stream);
        }

        // SHORTCUT: labels are burned in at the map's own pixel size, so their text scales with the zoom instead of staying sharp; revisit with a vector overlay if that is too soft
        private static Bitmap LoadWithLabels(FileInfo file)
        {
            var labels = MapLabels.Read(file);
            if (labels == null)
            {
                return LoadPlain(file);
            }

            using var magick = new MagickImage(file);
            MapLabelPainter.Draw(magick, labels);
            using var stream = new MemoryStream();
            magick.Write(stream, MagickFormat.Png);
            stream.Position = 0;
            return new Bitmap(stream);
        }

        private static string GetFrameText(ZoneSelection zone, FileInfo file)
        {
            try
            {
                var frame = MapFrame.Read(zone.Id, file) ?? MapFrame.Get(zone.Id);
                if (frame == null)
                {
                    return "";
                }

                return string.Format(CultureInfo.InvariantCulture, "Frame x {0:0}, y {1:0}, {2:0} wide", frame.OffsetX, frame.OffsetY, frame.Width);
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                return "";
            }
        }

        private sealed record LoadResult(Bitmap Image, string Message, string InfoText, string FrameText, string FilePath);
    }
}
