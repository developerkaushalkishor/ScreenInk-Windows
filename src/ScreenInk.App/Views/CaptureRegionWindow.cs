using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenInk.App.Interop;
using ScreenInk.App.Services;

namespace ScreenInk.App.Views;

internal sealed class CaptureRegionWindow : Window
{
    internal Int32Rect Region { get; private set; }
    internal CaptureRegionWindow(DisplayInfo display, BitmapSource image)
    {
        Title = "ScreenInk — drag a screenshot region; Escape to cancel";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true;
        Width = display.Width / display.DpiScaleX; Height = display.Height / display.DpiScaleY;
        var surface = new RegionSurface(image);
        Content = surface;
        surface.Completed += rect =>
        {
            var sx = image.PixelWidth / surface.ActualWidth;
            var sy = image.PixelHeight / surface.ActualHeight;
            var left = Math.Clamp((int)Math.Floor(rect.Left * sx), 0, image.PixelWidth - 1);
            var top = Math.Clamp((int)Math.Floor(rect.Top * sy), 0, image.PixelHeight - 1);
            var right = Math.Clamp((int)Math.Ceiling(rect.Right * sx), left + 1, image.PixelWidth);
            var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom * sy), top + 1, image.PixelHeight);
            Region = new Int32Rect(left, top, right - left, bottom - top);
            DialogResult = true;
        };
        SourceInitialized += (_, _) => NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle,
            NativeMethods.HwndTopmost, (int)display.Left, (int)display.Top, (int)display.Width, (int)display.Height, 0);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        MouseRightButtonDown += (_, _) => DialogResult = false;
    }

    private sealed class RegionSurface(BitmapSource image) : FrameworkElement
    {
        private Point? _start;
        private Rect _region;
        internal event Action<Rect>? Completed;
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawImage(image, new Rect(RenderSize));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null, new Rect(RenderSize));
            if (_start is null) return;
            dc.PushClip(new RectangleGeometry(_region));
            dc.DrawImage(image, new Rect(RenderSize)); dc.Pop();
            dc.DrawRectangle(null, new System.Windows.Media.Pen(Brushes.DeepSkyBlue, 2), _region);
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        { Cursor = Cursors.Cross; _start = e.GetPosition(this); CaptureMouse(); }
        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            if (_start is not { } start || !IsMouseCaptured) return;
            var end = e.GetPosition(this);
            end.X = Math.Clamp(end.X, 0, ActualWidth); end.Y = Math.Clamp(end.Y, 0, ActualHeight);
            _region = new Rect(start, end); InvalidateVisual();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        { ReleaseMouseCapture(); if (_region.Width >= 2 && _region.Height >= 2) Completed?.Invoke(_region); }
    }
}
