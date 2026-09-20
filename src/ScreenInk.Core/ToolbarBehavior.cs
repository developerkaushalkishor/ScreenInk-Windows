namespace ScreenInk.Core;

public readonly record struct ToolbarFrame(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class ToolbarGeometry
{
    public const double ScreenMargin = 12;
    public const double QuickColorMinimumWidth = 900;

    public static bool ShowQuickColors(double displayWidth) =>
        displayWidth >= QuickColorMinimumWidth;

    public static double FitWidth(double desiredWidth, double displayWidth) =>
        Math.Min(desiredWidth, Math.Max(1, displayWidth - (ScreenMargin * 2)));

    public static ToolbarFrame TopCenter(ToolbarFrame display, double toolbarWidth,
        double toolbarHeight, double topInset = ScreenMargin)
    {
        var width = FitWidth(toolbarWidth, display.Width);
        return new ToolbarFrame(
            display.Left + ((display.Width - width) / 2),
            display.Top + topInset,
            width,
            toolbarHeight);
    }
}

public sealed class ToolbarRevealTracker
{
    private bool _insideEdge;
    private string? _displayId;

    public bool Enter(bool atEdge, string? displayId)
    {
        if (!atEdge || string.IsNullOrWhiteSpace(displayId))
        {
            _insideEdge = false;
            _displayId = null;
            return false;
        }

        if (_insideEdge && string.Equals(_displayId, displayId, StringComparison.Ordinal))
            return false;

        _insideEdge = true;
        _displayId = displayId;
        return true;
    }

    public void RequireExitBeforeReveal() => _insideEdge = true;
}
