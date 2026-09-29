using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapCreator.Ui.ViewModels;

namespace MapCreator.Ui.Views
{
    public partial class MapViewerView : UserControl
    {
        private const double MIN_ZOOM = 0.05;
        private const double MAX_ZOOM = 8;
        private const double WHEEL_FACTOR = 1.15;

        private MapViewerViewModel viewModel;
        private Bitmap bitmap;
        private string shownZoneId;
        private double zoom = 1;
        private bool isFit = true;
        private bool isDragging;
        private Point dragStart;
        private Vector dragOffset;
        private Cursor handCursor;

        public MapViewerView()
        {
            this.InitializeComponent();
            this.DataContextChanged += this.OnDataContextChanged;
            this.Scroller.SizeChanged += this.OnScrollerSizeChanged;
            this.Scroller.AddHandler(PointerWheelChangedEvent, this.OnWheel, RoutingStrategies.Tunnel);
            this.Scroller.PointerPressed += this.OnPointerPressed;
            this.Scroller.PointerMoved += this.OnPointerMoved;
            this.Scroller.PointerReleased += this.OnPointerReleased;
            this.Scroller.PointerCaptureLost += this.OnPointerCaptureLost;
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (this.viewModel != null)
            {
                this.viewModel.PropertyChanged -= this.OnViewModelChanged;
            }

            this.viewModel = this.DataContext as MapViewerViewModel;
            if (this.viewModel != null)
            {
                this.viewModel.PropertyChanged += this.OnViewModelChanged;
                this.OnImageChanged();
            }
        }

        private void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MapViewerViewModel.Image))
            {
                this.OnImageChanged();
            }
        }

        private void OnImageChanged()
        {
            this.bitmap = this.viewModel.Image;
            if (this.bitmap == null)
            {
                this.shownZoneId = null;
                return;
            }

            // A refreshed image of the same zone keeps zoom and offset unless the view fits
            if (this.viewModel.ShownZoneId != this.shownZoneId)
            {
                this.isFit = true;
            }

            this.shownZoneId = this.viewModel.ShownZoneId;
            this.Apply();
        }

        private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.isFit && this.bitmap != null)
            {
                this.Apply();
            }
        }

        private void OnFitClick(object sender, RoutedEventArgs e)
        {
            this.isFit = true;
            this.Apply();
        }

        private void OnActualSizeClick(object sender, RoutedEventArgs e)
        {
            this.ZoomTo(1, new Point(this.Scroller.Bounds.Width / 2, this.Scroller.Bounds.Height / 2));
        }

        private void OnWheel(object sender, PointerWheelEventArgs e)
        {
            e.Handled = true;
            if (this.bitmap != null)
            {
                this.ZoomTo(this.zoom * Math.Pow(WHEEL_FACTOR, e.Delta.Y), e.GetPosition(this.Scroller));
            }
        }

        // Keeps the image point under the anchor (in scroller coordinates) where it is
        private void ZoomTo(double newZoom, Point anchor)
        {
            if (this.bitmap == null)
            {
                return;
            }

            var oldWidth = this.bitmap.PixelSize.Width * this.zoom;
            var oldHeight = this.bitmap.PixelSize.Height * this.zoom;
            var onImage = this.Scroller.TranslatePoint(anchor, this.MapImage) ?? new Point(oldWidth / 2, oldHeight / 2);
            var fractionX = Math.Clamp(onImage.X / oldWidth, 0, 1);
            var fractionY = Math.Clamp(onImage.Y / oldHeight, 0, 1);

            this.isFit = false;
            this.zoom = Math.Clamp(newZoom, MIN_ZOOM, MAX_ZOOM);
            this.Apply();
            this.Scroller.UpdateLayout();
            this.Scroller.Offset = new Vector(
                Math.Max(0, fractionX * this.bitmap.PixelSize.Width * this.zoom - anchor.X),
                Math.Max(0, fractionY * this.bitmap.PixelSize.Height * this.zoom - anchor.Y));
        }

        private void Apply()
        {
            if (this.bitmap == null)
            {
                return;
            }

            if (this.isFit)
            {
                var width = this.Scroller.Bounds.Width - 2;
                var height = this.Scroller.Bounds.Height - 2;
                if (width <= 0 || height <= 0)
                {
                    return;
                }

                this.zoom = Math.Clamp(Math.Min(width / this.bitmap.PixelSize.Width, height / this.bitmap.PixelSize.Height), MIN_ZOOM, MAX_ZOOM);
            }

            this.MapImage.Width = this.bitmap.PixelSize.Width * this.zoom;
            this.MapImage.Height = this.bitmap.PixelSize.Height * this.zoom;
            RenderOptions.SetBitmapInterpolationMode(this.MapImage, this.zoom < 1 ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.MediumQuality);
            this.ZoomText.Text = string.Format(CultureInfo.InvariantCulture, "{0:0}%", this.zoom * 100);
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this.Scroller).Properties.IsLeftButtonPressed)
            {
                return;
            }

            this.isDragging = true;
            this.dragStart = e.GetPosition(this.Scroller);
            this.dragOffset = this.Scroller.Offset;
            this.handCursor ??= new Cursor(StandardCursorType.Hand);
            this.Scroller.Cursor = this.handCursor;
            e.Pointer.Capture(this.Scroller);
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (!this.isDragging)
            {
                return;
            }

            var delta = e.GetPosition(this.Scroller) - this.dragStart;
            this.Scroller.Offset = new Vector(Math.Max(0, this.dragOffset.X - delta.X), Math.Max(0, this.dragOffset.Y - delta.Y));
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            this.EndDrag();
            e.Pointer.Capture(null);
        }

        private void OnPointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
        {
            this.EndDrag();
        }

        private void EndDrag()
        {
            this.isDragging = false;
            this.Scroller.Cursor = null;
        }
    }
}
