using System.Runtime.InteropServices;
using ScreenInk.App.Interop;

namespace ScreenInk.App.Services;

internal readonly record struct DisplayInfo(string Id, double Left, double Top,
    double Width, double Height, bool Primary, double DpiScaleX, double DpiScaleY)
{
    internal double LogicalWidth => Width / Math.Max(1, DpiScaleX);
}

internal static class DisplayService
{
    internal static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        // Read the current desktop topology directly; do not retain WinForms Screen snapshots.
        var displays = new List<DisplayInfo>();
        NativeMethods.MonitorCallback callback = (nint monitor, nint dc, ref NativeMethods.NativeRect bounds, nint data) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(), Device = string.Empty };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return true;
            var rect = info.Monitor;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
            var scaleX = 1d; var scaleY = 1d;
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0)
            { scaleX = Math.Max(1, dpiX / 96d); scaleY = Math.Max(1, dpiY / 96d); }
            displays.Add(new DisplayInfo(info.Device, rect.Left, rect.Top, rect.Right - rect.Left,
                rect.Bottom - rect.Top, (info.Flags & 1) != 0, scaleX, scaleY));
            return true;
        };
        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0))
            throw new InvalidOperationException("Windows could not enumerate the connected displays.");
        return displays;
    }
}
