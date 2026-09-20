using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ScreenInk.App.Interop;
using ScreenInk.App.Models;
using ScreenInk.App.Services;
using ScreenInk.Core;
using Button = System.Windows.Controls.Button;

namespace ScreenInk.App.Views;

internal sealed class ToolbarWindow : Window
{
    private static readonly uint[] Colors =
    [
        0xFFBF5AF2, 0xFFFF453A, 0xFFFFD60A, 0xFF30D158, 0xFF0A84FF, 0xFFFFFFFF,
        0xFFFF9F0A, 0xFFFF375F, 0xFF64D2FF, 0xFF5E5CE6, 0xFFAC8E68, 0xFF8E8E93,
        0xFF7D3CFF, 0xFFD70015, 0xFFFFB340, 0xFF00A83B, 0xFF007AFF, 0xFFE5E5EA,
        0xFF5B2C6F, 0xFF8B1A1A, 0xFF8A6D00, 0xFF1F6B3A, 0xFF003F88, 0xFF1C1C1E
    ];

    private readonly AppState _state;
    private readonly StackPanel _panel;
    private readonly Border _root;
    private readonly TranslateTransform _revealTransform = new();
    private readonly Dictionary<DrawingTool, Button> _toolButtons = [];
    private readonly Dictionary<uint, List<Border>> _colorIndicators = [];
    private readonly List<Button> _quickColors = [];
    private readonly List<Popup> _popups = [];
    private bool _hideAnimationRunning;

    internal event Action? UndoRequested;
    internal event Action? RedoRequested;
    internal event Action? ClearRequested;
    internal event Action? ScreenshotRequested;
    internal event Action? DisableRequested;
    internal event Action? HideRequested;
    internal event Action? DragCompleted;

    internal string? DisplayId { get; private set; }
    internal bool IsInteractionActive => IsMouseOver || _popups.Any(popup => popup.IsOpen) || Mouse.Captured is not null;

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
        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 28, 28, 34)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = _panel,
            RenderTransform = _revealTransform,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.38 }
        };
        Content = _root;

        AddDragHandle();
        AddButton(_panel, ToolbarIcon.Cursor, "Normal mode — interact with apps", (_, _) => _state.SetDrawing(false));
        AddTool(_panel, DrawingTool.Select, ToolbarIcon.Select, "Select and move annotations");
        AddTool(_panel, DrawingTool.Pen, ToolbarIcon.Pen, "Pen — draw permanent ink");
        AddTool(_panel, DrawingTool.Highlighter, ToolbarIcon.Highlighter, "Highlighter — translucent ink");
        AddTool(_panel, DrawingTool.Eraser, ToolbarIcon.Eraser, "Whole-stroke eraser");

        var shapeButton = AddButton(_panel, ToolbarIcon.Shapes, "Shapes", (_, _) => { });
        var shapePopup = CreatePopup(shapeButton, CreateShapeGrid());
        shapeButton.Click += (_, _) => TogglePopup(shapePopup);

        AddSeparator(_panel);
        foreach (var color in Colors.Take(6))
            _quickColors.Add(AddColor(_panel, color));
        var paletteButton = AddButton(_panel, ToolbarIcon.Palette, "Open 24-color palette", (_, _) => { });
        var palettePopup = CreatePopup(paletteButton, CreatePalette());
        paletteButton.Click += (_, _) => TogglePopup(palettePopup);

        AddSeparator(_panel);
        AddButton(_panel, ToolbarIcon.Width, "Cycle pen width", (_, _) => CycleWidth());
        AddButton(_panel, ToolbarIcon.Undo, "Undo on active display", (_, _) => UndoRequested?.Invoke());
        AddButton(_panel, ToolbarIcon.Redo, "Redo on active display", (_, _) => RedoRequested?.Invoke());
        AddButton(_panel, ToolbarIcon.Clear, "Clear active display", (_, _) => ClearRequested?.Invoke());

        AddSeparator(_panel);
        var moreButton = AddButton(_panel, ToolbarIcon.More, "More tools and presentation controls", (_, _) => { });
        var morePopup = CreatePopup(moreButton, CreateMoreGrid());
        moreButton.Click += (_, _) => TogglePopup(morePopup);
        AddButton(_panel, ToolbarIcon.Hide, "Hide toolbar", (_, _) => HideRequested?.Invoke());

        _state.Changed += RefreshState;
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) _state.SetDrawing(false);
        };
        Closed += (_, _) => _state.Changed -= RefreshState;
        RefreshState();
    }

    internal void ConfigureForDisplay(DisplayInfo display)
    {
        DisplayId = display.Id;
        var showQuickColors = ToolbarGeometry.ShowQuickColors(display.LogicalWidth);
        foreach (var button in _quickColors)
            button.Visibility = showQuickColors ? Visibility.Visible : Visibility.Collapsed;
        MaxWidth = Math.Max(1, display.LogicalWidth - (ToolbarGeometry.ScreenMargin * 2));
        InvalidateMeasure();
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        UpdateLayout();
    }

    internal void PlaceTopCenter(DisplayInfo display)
    {
        ConfigureForDisplay(display);
        var widthPixels = Math.Max(1, ActualWidth * display.DpiScaleX);
        var heightPixels = Math.Max(1, ActualHeight * display.DpiScaleY);
        var frame = ToolbarGeometry.TopCenter(
            new ToolbarFrame(display.Left, display.Top, display.Width, display.Height),
            widthPixels, heightPixels, ToolbarGeometry.ScreenMargin * display.DpiScaleY);
        MoveToPhysicalPixel(frame.Left, frame.Top);
    }

    internal void MoveToPhysicalPixel(double left, double top)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost,
            (int)Math.Round(left), (int)Math.Round(top), 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    internal ToolbarFrame PhysicalFrame()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        return NativeMethods.GetWindowRect(handle, out var rect)
            ? new ToolbarFrame(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : new ToolbarFrame(Left, Top, ActualWidth, ActualHeight);
    }

    internal void ShowAnimated()
    {
        _hideAnimationRunning = false;
        BeginAnimation(OpacityProperty, null);
        _revealTransform.BeginAnimation(TranslateTransform.YProperty, null);
        if (!IsVisible) Show();
        Opacity = 1;
        _revealTransform.Y = 0;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        { EasingFunction = easing });
        _revealTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        ActivateTopmostWithoutFocus();
    }

    internal void ShowImmediately()
    {
        _hideAnimationRunning = false;
        BeginAnimation(OpacityProperty, null);
        _revealTransform.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 1;
        _revealTransform.Y = 0;
        if (!IsVisible) Show();
        ActivateTopmostWithoutFocus();
    }

    internal void HideAnimated()
    {
        if (!IsVisible || _hideAnimationRunning) return;
        _hideAnimationRunning = true;
        ClosePopups();
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120));
        animation.Completed += (_, _) =>
        {
            _hideAnimationRunning = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
        };
        BeginAnimation(OpacityProperty, animation);
    }

    internal void ClosePopups()
    {
        foreach (var popup in _popups) popup.IsOpen = false;
    }

    private void AddDragHandle()
    {
        var button = AddButton(_panel, ToolbarIcon.Drag, "Drag toolbar", (_, _) => { });
        button.Cursor = Cursors.SizeAll;
        button.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); }
            finally { DragCompleted?.Invoke(); }
        };
    }

    private UIElement CreateShapeGrid()
    {
        var grid = PopupGrid(5);
        AddTool(grid, DrawingTool.Line, ToolbarIcon.Line, "Line");
        AddTool(grid, DrawingTool.Arrow, ToolbarIcon.Arrow, "Arrow");
        AddTool(grid, DrawingTool.Rectangle, ToolbarIcon.Rectangle, "Rounded rectangle");
        AddTool(grid, DrawingTool.Ellipse, ToolbarIcon.Ellipse, "Ellipse");
        AddTool(grid, DrawingTool.Diamond, ToolbarIcon.Diamond, "Diamond");
        return grid;
    }

    private UIElement CreatePalette()
    {
        var grid = PopupGrid(6);
        foreach (var color in Colors) AddColor(grid, color);
        return grid;
    }

    private UIElement CreateMoreGrid()
    {
        var grid = PopupGrid(5);
        AddTool(grid, DrawingTool.Laser, ToolbarIcon.Laser, "Laser pointer");
        AddTool(grid, DrawingTool.Text, ToolbarIcon.Text, "Text");
        AddButton(grid, ToolbarIcon.Fade, "Toggle fading ink", (_, _) =>
        {
            _state.FadingInk = !_state.FadingInk;
            _state.Notify();
        });
        AddButton(grid, ToolbarIcon.Ink, "Show or hide ink", (_, _) =>
        {
            _state.InkVisible = !_state.InkVisible;
            _state.Notify();
        });
        AddButton(grid, ToolbarIcon.Board, "Cycle screen, whiteboard and blackboard", (_, _) =>
        {
            _state.BoardStyle = _state.BoardStyle switch
            {
                BoardStyle.Screen => BoardStyle.Whiteboard,
                BoardStyle.Whiteboard => BoardStyle.Blackboard,
                _ => BoardStyle.Screen
            };
            _state.Notify();
        });
        AddButton(grid, ToolbarIcon.Screenshot, "Capture active display", (_, _) => ScreenshotRequested?.Invoke());
        AddButton(grid, ToolbarIcon.AutoHide, "Toggle auto-hide", (_, _) =>
        {
            _state.Settings.AutoHideToolbar = !_state.Settings.AutoHideToolbar;
            _state.Settings.Save();
            _state.Notify();
        });
        AddButton(grid, ToolbarIcon.Power, "Disable ScreenInk; re-enable from tray", (_, _) => DisableRequested?.Invoke());
        return grid;
    }

    private static UniformGrid PopupGrid(int columns) => new()
    {
        Columns = columns,
        Margin = new Thickness(8)
    };

    private Popup CreatePopup(Button target, UIElement content)
    {
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
            Child = new Border
            {
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(250, 30, 30, 36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = content,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.4 }
            }
        };
        _popups.Add(popup);
        return popup;
    }

    private void TogglePopup(Popup popup)
    {
        var open = !popup.IsOpen;
        ClosePopups();
        popup.IsOpen = open;
    }

    private void AddTool(Panel target, DrawingTool tool, ToolbarIcon icon, string tooltip)
    {
        var button = AddButton(target, icon, tooltip, (_, _) =>
        {
            _state.SelectTool(tool);
            ClosePopups();
        });
        _toolButtons[tool] = button;
    }

    private Button AddButton(Panel target, ToolbarIcon icon, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = ToolbarIcons.Create(icon),
            ToolTip = tooltip,
            Width = 34,
            Height = 34,
            Margin = new Thickness(2),
            Padding = new Thickness(7),
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            Style = CreateButtonStyle()
        };
        button.Click += handler;
        target.Children.Add(button);
        return button;
    }

    private Button AddColor(Panel target, uint color)
    {
        var value = Color.FromArgb((byte)(color >> 24), (byte)(color >> 16),
            (byte)(color >> 8), (byte)color);
        var circle = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(value),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(color == _state.Settings.Color ? 2 : 0.75)
        };
        var button = new Button
        {
            Content = circle,
            Width = 30,
            Height = 34,
            Margin = new Thickness(1),
            Padding = new Thickness(5),
            ToolTip = $"Color #{color & 0xFFFFFF:X6}",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Focusable = false,
            Cursor = Cursors.Hand,
            Style = CreateButtonStyle()
        };
        if (!_colorIndicators.TryGetValue(color, out var indicators))
        {
            indicators = [];
            _colorIndicators[color] = indicators;
        }
        indicators.Add(circle);
        button.Click += (_, _) =>
        {
            _state.Settings.Color = color;
            _state.Settings.Save();
            _state.Notify();
            ClosePopups();
        };
        target.Children.Add(button);
        return button;
    }

    private static Style CreateButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.TemplateProperty, CreateButtonTemplate()));
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(48, 255, 255, 255))));
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(90, 10, 132, 255))));
        pressed.Setters.Add(new Setter(Button.OpacityProperty, 0.72));
        style.Triggers.Add(hover);
        style.Triggers.Add(pressed);
        return style;
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static void AddSeparator(Panel panel) => panel.Children.Add(new Border
    {
        Width = 1,
        Height = 24,
        Margin = new Thickness(5),
        Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255))
    });

    private void CycleWidth()
    {
        _state.Settings.Width = _state.Settings.Width switch { <= 2 => 4, <= 4 => 8, _ => 2 };
        _state.Settings.Save();
        _state.Notify();
    }

    private void RefreshState()
    {
        foreach (var pair in _toolButtons)
            pair.Value.Background = pair.Key == _state.Settings.Tool && _state.IsDrawing
                ? new SolidColorBrush(Color.FromArgb(110, 10, 132, 255))
                : Brushes.Transparent;
        foreach (var pair in _colorIndicators)
            foreach (var indicator in pair.Value)
                indicator.BorderThickness = new Thickness(pair.Key == _state.Settings.Color ? 2 : 0.75);
    }

    private void ActivateTopmostWithoutFocus()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }
}
