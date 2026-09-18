using ScreenInk.Core;

namespace ScreenInk.App.Models;

public sealed class AppState
{
    public AppSettings Settings { get; }
    public bool IsDrawing { get; private set; }
    public bool FadingInk { get; set; }
    public bool InkVisible { get; set; } = true;
    public BoardStyle BoardStyle { get; set; }
    public event Action? Changed;

    public AppState(AppSettings settings) => Settings = settings;

    public void SetDrawing(bool drawing)
    {
        IsDrawing = Settings.IsEnabled && drawing;
        Notify();
    }

    public void SetEnabled(bool enabled)
    {
        Settings.IsEnabled = enabled;
        if (!enabled) IsDrawing = false;
        Settings.Save();
        Notify();
    }

    public void SelectTool(DrawingTool tool)
    {
        Settings.Tool = tool;
        IsDrawing = Settings.IsEnabled;
        Settings.Save();
        Notify();
    }

    public void Notify() => Changed?.Invoke();
}
