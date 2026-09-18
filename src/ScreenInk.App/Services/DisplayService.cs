using System.Windows.Forms;

namespace ScreenInk.App.Services;

internal readonly record struct DisplayInfo(string Id, double Left, double Top,
    double Width, double Height, bool Primary);

internal static class DisplayService
{
    internal static IReadOnlyList<DisplayInfo> GetDisplays() => Screen.AllScreens.Select(screen =>
        new DisplayInfo(screen.DeviceName, screen.Bounds.Left, screen.Bounds.Top,
            screen.Bounds.Width, screen.Bounds.Height, screen.Primary)).ToArray();
}
