using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenInk.App.Models;
using ScreenInk.App.Services;
using ScreenInk.Core;
using Button = System.Windows.Controls.Button;

namespace ScreenInk.App.Views;

internal sealed class ToolbarWindow : Window
{
    private readonly AppState _state;
    private readonly StackPanel _panel;
    private readonly Dictionary<DrawingTool, Button> _toolButtons = [];

    internal event Action? UndoRequested;
    internal event Action? RedoRequested;
    internal event Action? ClearRequested;
    internal event Action? ScreenshotRequested;
    internal event Action? DisableRequested;

    internal ToolbarWindow(AppState state)
    {
        _state = state;
        Title = "ScreenInk Toolbar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        _panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 28, 28, 34)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = _panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.35 }
        };
        Content = border;
        AddButton("⠿", "Drag toolbar", (_, args) => DragMove(), true);
        AddTool(DrawingTool.Select, "Select", "Select and move annotations");
        AddTool(DrawingTool.Pen, "Pen", "Draw ink");
        AddTool(DrawingTool.Highlighter, "Highlight", "Draw translucent ink");
        AddTool(DrawingTool.Eraser, "Erase", "Erase complete strokes");
        AddTool(DrawingTool.Laser, "Laser", "Temporary laser trail");
        AddTool(DrawingTool.Line, "Line", "Draw a line");
        AddTool(DrawingTool.Arrow, "Arrow", "Draw an arrow");
        AddTool(DrawingTool.Rectangle, "Rect", "Draw a rounded rectangle");
        AddTool(DrawingTool.Ellipse, "Ellipse", "Draw an ellipse");
        AddTool(DrawingTool.Diamond, "Diamond", "Draw a diamond");
        AddTool(DrawingTool.Text, "Text", "Place text");
        AddSeparator();
        foreach (var color in new[] { 0xFFBF5AF2u, 0xFFFF453Au, 0xFFFFD60Au,
                     0xFF30D158u, 0xFF0A84FFu, 0xFFFFFFFFu })
            AddColor(color);
        AddSeparator();
        AddButton("Width", "Cycle pen width", (_, _) =>
        {
            _state.Settings.Width = _state.Settings.Width switch { <= 2 => 4, <= 4 => 8, _ => 2 };
            _state.Settings.Save();
            _state.Notify();
        });
        AddButton("Undo", "Undo on active display", (_, _) => UndoRequested?.Invoke());
        AddButton("Redo", "Redo on active display", (_, _) => RedoRequested?.Invoke());
        AddButton("Clear", "Clear active display", (_, _) => ClearRequested?.Invoke());
        AddButton("Fade", "Toggle fading ink", (_, _) => { _state.FadingInk = !_state.FadingInk; _state.Notify(); });
        AddButton("Ink", "Show or hide ink", (_, _) => { _state.InkVisible = !_state.InkVisible; _state.Notify(); });
        AddButton("Board", "Cycle screen, whiteboard and blackboard", (_, _) =>
        {
            _state.BoardStyle = _state.BoardStyle switch
            { BoardStyle.Screen => BoardStyle.Whiteboard, BoardStyle.Whiteboard => BoardStyle.Blackboard, _ => BoardStyle.Screen };
            _state.Notify();
        });
        AddButton("Shot", "Capture active display", (_, _) => ScreenshotRequested?.Invoke());
        AddButton("Off", "Disable ScreenInk; re-enable from tray", (_, _) => DisableRequested?.Invoke());
        AddButton("Hide", "Hide toolbar", (_, _) => Hide());
        _state.Changed += RefreshState;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) _state.SetDrawing(false);
        };
    }

    internal void PlaceTopCenter(DisplayInfo display)
    {
        UpdateLayout();
        Left = display.Left + (display.Width - ActualWidth) / 2;
        Top = display.Top + 12;
    }

    private void AddTool(DrawingTool tool, string label, string tooltip)
    {
        var button = AddButton(label, tooltip, (_, _) => _state.SelectTool(tool));
        _toolButtons[tool] = button;
    }

    private Button AddButton(string text, string tooltip, MouseButtonEventHandler handler,
        bool dragHandle = false)
    {
        var button = new Button
        {
            Content = text,
            ToolTip = tooltip,
            Margin = new Thickness(2),
            Padding = new Thickness(7, 5, 7, 5),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Focusable = false
        };
        button.PreviewMouseLeftButtonDown += handler;
        if (dragHandle) button.Cursor = Cursors.SizeAll;
        _panel.Children.Add(button);
        return button;
    }

    private void AddColor(uint color)
    {
        var value = Color.FromArgb((byte)(color >> 24), (byte)(color >> 16),
            (byte)(color >> 8), (byte)color);
        var button = new Button
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(2),
            Background = new SolidColorBrush(value),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            ToolTip = $"Color #{color & 0xFFFFFF:X6}"
        };
        button.Click += (_, _) =>
        {
            _state.Settings.Color = color;
            _state.Settings.Save();
            _state.Notify();
        };
        _panel.Children.Add(button);
    }

    private void AddSeparator() => _panel.Children.Add(new Border
    {
        Width = 1,
        Height = 24,
        Margin = new Thickness(5, 2, 5, 2),
        Background = Brushes.DimGray
    });

    private void RefreshState()
    {
        foreach (var pair in _toolButtons)
            pair.Value.Background = pair.Key == _state.Settings.Tool && _state.IsDrawing
                ? new SolidColorBrush(Color.FromRgb(0, 180, 255)) : Brushes.Transparent;
    }
}
