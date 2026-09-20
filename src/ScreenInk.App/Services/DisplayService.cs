using System.Windows.Forms;
using ScreenInk.App.Interop;

namespace ScreenInk.App.Services;

internal readonly record struct DisplayInfo(string Id, double Left, double Top,
    double Width, double Height, bool Primary, double DpiScaleX, double DpiScaleY)
{
    internal double LogicalWidth => Width / Math.Max(1, DpiScaleX);
}

internal static class DisplayService
{
    internal static IReadOnlyList<DisplayInfo> GetDisplays() => Screen.AllScreens.Select(screen =>
    {
        var center = new NativeMethods.NativePoint
        {
            X = screen.Bounds.Left + (screen.Bounds.Width / 2),
            Y = screen.Bounds.Top + (screen.Bounds.Height / 2)
        };
        var monitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MonitorDefaultToNearest);
        var scaleX = 1d;
        var scaleY = 1d;
        if (monitor != 0 && NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0)
        {
            scaleX = Math.Max(1, dpiX / 96d);
            scaleY = Math.Max(1, dpiY / 96d);
        }
        return new DisplayInfo(screen.DeviceName, screen.Bounds.Left, screen.Bounds.Top,
            screen.Bounds.Width, screen.Bounds.Height, screen.Primary, scaleX, scaleY);
    }).ToArray();
}
