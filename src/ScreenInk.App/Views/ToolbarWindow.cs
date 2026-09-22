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
    private readonly Dictionary<DrawingTool, List<Button>> _toolButtons = [];
    private readonly List<UIElement> _overflowCandidates = [];
    private readonly List<(Button Button, Func<bool> Selected)> _toggleButtons = [];
    private Button _normalButton = null!;
    private Button _shapeButton = null!;
    private int _animationVersion;
    private bool _configuring;
    private readonly Dictionary<uint, List<Border>> _colorIndicators = [];
    private readonly List<Button> _quickColors = [];
    private readonly List<Popup> _popups = [];
    private bool _hideAnimationRunning;

    internal event Action<BoardStyle, bool, bool>? BoardRequested;
    internal event Action? UndoRequested;
    internal event Action? RedoRequested;
    internal event Action? ClearRequested;
    internal event Action<bool, bool>? ScreenshotRequested;
    internal bool IsCommandActive { get; set; }
    internal event Action? DisableRequested;
    internal event Action? HideRequested;
    internal event Action? DragCompleted;

    internal string? DisplayId { get; private set; }
    internal bool IsInteractionActive => IsCommandActive || IsMouseOver || _popups.Any(popup => popup.IsOpen) || Mouse.Captured is not null;

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
        ShowActivated = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

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
        _normalButton = AddButton(_panel, ToolbarIcon.Cursor, "Normal mode — interact with apps", (_, _) => _state.SetDrawing(false));
        AddTool(_panel, DrawingTool.Select, ToolbarIcon.Select, "Select and move annotations");
        AddTool(_panel, DrawingTool.Pen, ToolbarIcon.Pen, "Pen — draw permanent ink");
        AddTool(_panel, DrawingTool.Highlighter, ToolbarIcon.Highlighter, "Highlighter — translucent ink");
        AddTool(_panel, DrawingTool.Eraser, ToolbarIcon.Eraser, "Whole-stroke eraser");

        var shapeButton = AddButton(_panel, ToolbarIcon.Shapes, "Shapes", (_, _) => { });
        _shapeButton = shapeButton;
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
        // Keep normal mode, pen and recovery visible; every collapsed action remains in More.
        foreach (UIElement item in _panel.Children)
            if (item != _panel.Children[0] && item != _normalButton && item != _toolButtons[DrawingTool.Pen][0])
                _overflowCandidates.Add(item);
        var moreButton = AddButton(_panel, ToolbarIcon.More, "More tools and presentation controls", (_, _) => { });
        var morePopup = CreatePopup(moreButton, CreateMoreGrid());
        moreButton.Click += (_, _) => TogglePopup(morePopup);
        moreButton.MouseEnter += (_, _) => { ClosePopups(); morePopup.IsOpen = true; };
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
        if (_configuring) return;
        _configuring = true;
        try
        {
            DisplayId = display.Id;
            MaxWidth = Math.Max(1, display.LogicalWidth - (ToolbarGeometry.ScreenMargin * 2));
            foreach (var item in _overflowCandidates) item.Visibility = Visibility.Visible;
            if (!ToolbarGeometry.ShowQuickColors(display.LogicalWidth))
                foreach (var button in _quickColors) button.Visibility = Visibility.Collapsed;
            _panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            foreach (var item in _overflowCandidates.AsEnumerable().Reverse())
            {
                if (_panel.DesiredSize.Width + 2 <= MaxWidth) break;
                item.Visibility = Visibility.Collapsed;
                _panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
            Measure(new Size(MaxWidth, double.PositiveInfinity));
            UpdateLayout();
        }
        finally { _configuring = false; }
    }

    internal void PlaceTopCenter(DisplayInfo display)
    {
        // Establish destination DPI before measuring a previously hidden toolbar.
        MoveToPhysicalPixel(display.Left + 12, display.Top + 12);
        ConfigureForDisplay(display);
        var widthPixels = Math.Max(1, Math.Min(MaxWidth, _panel.DesiredSize.Width + 2) * display.DpiScaleX);
        var heightPixels = Math.Max(1, (_panel.DesiredSize.Height + 2) * display.DpiScaleY);
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
        if (IsVisible && !_hideAnimationRunning) { ActivateTopmostWithoutFocus(); return; }
        _animationVersion++;
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
        _animationVersion++;
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
        var version = ++_animationVersion;
        ClosePopups();
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120));
        animation.Completed += (_, _) =>
        {
            if (version != _animationVersion) return;
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
        var grid = PopupGrid(4);
        AddTool(grid, DrawingTool.Select, ToolbarIcon.Select, "Select / resize");
        AddTool(grid, DrawingTool.Pen, ToolbarIcon.Pen, "Pen");
        AddTool(grid, DrawingTool.Highlighter, ToolbarIcon.Highlighter, "Highlighter");
        AddTool(grid, DrawingTool.Eraser, ToolbarIcon.Eraser, "Eraser");
        AddTool(grid, DrawingTool.Line, ToolbarIcon.Line, "Line");
        AddTool(grid, DrawingTool.Arrow, ToolbarIcon.Arrow, "Arrow");
        AddTool(grid, DrawingTool.Rectangle, ToolbarIcon.Rectangle, "Rectangle");
        AddTool(grid, DrawingTool.Ellipse, ToolbarIcon.Ellipse, "Ellipse");
        AddTool(grid, DrawingTool.Diamond, ToolbarIcon.Diamond, "Diamond");
        AddButton(grid, ToolbarIcon.Width, "Pen width", (_, _) => CycleWidth());
        AddButton(grid, ToolbarIcon.Undo, "Undo", (_, _) => UndoRequested?.Invoke());
        AddButton(grid, ToolbarIcon.Redo, "Redo", (_, _) => RedoRequested?.Invoke());
        AddButton(grid, ToolbarIcon.Clear, "Clear", (_, _) => ClearRequested?.Invoke());
        var palette = AddButton(grid, ToolbarIcon.Palette, "Colors", (_, _) => { });
        var palettePopup = CreatePopup(palette, CreatePalette());
        palette.Click += (_, _) => TogglePopup(palettePopup);
        AddTool(grid, DrawingTool.Laser, ToolbarIcon.Laser, "Laser pointer");
        AddTool(grid, DrawingTool.Text, ToolbarIcon.Text, "Text");
        AddToggle(grid, ToolbarIcon.Cursor, "Cursor halo", () => _state.Settings.CursorHalo, (_, _) =>
        { _state.Settings.CursorHalo = !_state.Settings.CursorHalo; _state.Settings.Save(); _state.Notify(); });
        AddToggle(grid, ToolbarIcon.AutoHide, "Click ripple", () => _state.Settings.ClickAnimations, (_, _) =>
        { _state.Settings.ClickAnimations = !_state.Settings.ClickAnimations; _state.Settings.Save(); _state.Notify(); });
        AddToggle(grid, ToolbarIcon.Fade, "Fading ink", () => _state.FadingInk, (_, _) =>
        {
            _state.FadingInk = !_state.FadingInk;
            _state.Notify();
        });
        AddToggle(grid, ToolbarIcon.Ink, "Ink visible", () => _state.InkVisible, (_, _) =>
        {
            _state.InkVisible = !_state.InkVisible;
            _state.Notify();
        });
        var boardButton = AddButton(grid, ToolbarIcon.Board, "Boards", (_, _) => { });
        var boards = new StackPanel { Margin = new Thickness(8) };
        var scopes = new System.Windows.Controls.ComboBox { Width = 210, Margin = new Thickness(4),
            ItemsSource = new[] { "This display", "All displays", "Draw a region" }, SelectedIndex = 0 };
        boards.Children.Add(scopes);
        foreach (var style in Enum.GetValues<BoardStyle>())
        {
            var captured = style;
            var button = new Button { Content = style == BoardStyle.Screen ? "Remove boards" : style.ToString(),
                Margin = new Thickness(4), Padding = new Thickness(8) };
            button.Click += (_, _) =>
            {
                BoardRequested?.Invoke(captured, scopes.SelectedIndex == 1, scopes.SelectedIndex == 2);
                ClosePopups();
            };
            boards.Children.Add(button);
        }
        var boardPopup = CreatePopup(boardButton, boards);
        boardButton.Click += (_, _) => TogglePopup(boardPopup);
        var typographyButton = AddButton(grid, ToolbarIcon.Text, "Typography", (_, _) => { });
        var typographyPopup = CreatePopup(typographyButton, CreateTypography());
        typographyButton.Click += (_, _) => TogglePopup(typographyPopup);
        var screenshot = AddButton(grid, ToolbarIcon.Screenshot, "Screenshot", (_, _) => { });
        var captureOptions = new StackPanel { Width = 230, Margin = new Thickness(8) };
        foreach (var option in new[] { ("Save display as PNG", false, false), ("Copy display", false, true),
            ("Save region as PNG", true, false), ("Copy region", true, true) })
        {
            var button = new Button { Content = option.Item1, Margin = new Thickness(4), Padding = new Thickness(8) };
            button.Click += (_, _) => ScreenshotRequested?.Invoke(option.Item2, option.Item3);
            captureOptions.Children.Add(button);
        }
        var capturePopup = CreatePopup(screenshot, captureOptions);
        screenshot.Click += (_, _) => TogglePopup(capturePopup);
        AddToggle(grid, ToolbarIcon.AutoHide, "Auto-hide", () => _state.Settings.AutoHideToolbar, (_, _) =>
        {
            _state.Settings.AutoHideToolbar = !_state.Settings.AutoHideToolbar;
            _state.Settings.Save();
            _state.Notify();
        });
        AddButton(grid, ToolbarIcon.Power, "Disable ScreenInk; re-enable from tray", (_, _) => DisableRequested?.Invoke());
        return grid;
    }

    private UIElement CreateTypography()
    {
        var panel = new StackPanel { Width = 230, Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "Font", Foreground = Brushes.White });
        var fonts = new System.Windows.Controls.ComboBox { ItemsSource = new[]
            { "Segoe Print", "Segoe Script", "Comic Sans MS", "Segoe UI", "Arial", "Georgia", "Consolas" },
            SelectedItem = _state.Settings.FontFamily, Margin = new Thickness(0, 6, 0, 10) };
        fonts.SelectionChanged += (_, _) =>
        {
            if (fonts.SelectedItem is not string font) return;
            _state.Settings.FontFamily = font;
            _state.SelectTool(DrawingTool.Text);
        };
        panel.Children.Add(fonts);
        panel.Children.Add(new TextBlock { Text = "Text size", Foreground = Brushes.White });
        var sizes = new System.Windows.Controls.ComboBox { ItemsSource = new double[] { 16, 20, 24, 28, 36, 48, 64, 96 },
            SelectedItem = _state.Settings.FontSize, Margin = new Thickness(0, 6, 0, 10) };
        sizes.SelectionChanged += (_, _) =>
        {
            if (sizes.SelectedItem is not double size) return;
            _state.Settings.FontSize = size; _state.Settings.Save(); _state.Notify();
        };
        panel.Children.Add(sizes);
        var alignment = new System.Windows.Controls.ComboBox { ItemsSource = Enum.GetValues<InkTextAlignment>(),
            SelectedItem = _state.Settings.TextAlignment, Margin = new Thickness(0, 6, 0, 10) };
        alignment.SelectionChanged += (_, _) =>
        {
            if (alignment.SelectedItem is not InkTextAlignment value) return;
            _state.Settings.TextAlignment = value; _state.Settings.Save(); _state.Notify();
        };
        panel.Children.Add(new TextBlock { Text = "Alignment", Foreground = Brushes.White });
        panel.Children.Add(alignment);
        return panel;
    }

    private void AddToggle(Panel panel, ToolbarIcon icon, string label, Func<bool> selected, RoutedEventHandler action)
    {
        var button = AddButton(panel, icon, label, action);
        _toggleButtons.Add((button, selected));
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
        foreach (var other in _popups)
            if (other != popup && !(other.Child is FrameworkElement root && root.IsAncestorOf(popup.PlacementTarget)))
                other.IsOpen = false;
        popup.IsOpen = open;
    }

    private void AddTool(Panel target, DrawingTool tool, ToolbarIcon icon, string tooltip)
    {
        var button = AddButton(target, icon, tooltip, (_, _) =>
        {
            _state.SelectTool(tool);
            ClosePopups();
        });
        if (!_toolButtons.TryGetValue(tool, out var buttons)) _toolButtons[tool] = buttons = [];
        buttons.Add(button);
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
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
            Style = CreateButtonStyle()
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        if (target != _panel)
        {
            button.Width = 70;
            button.Height = 58;
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var artwork = (UIElement)button.Content;
            button.Content = null;
            content.Children.Add(artwork);
            content.Children.Add(new TextBlock
            {
                Text = tooltip.Split(" — ")[0], FontSize = 10, MaxWidth = 68,
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 0)
            });
            button.Content = content;
        }
        var scale = new ScaleTransform(1, 1);
        button.RenderTransform = scale;
        button.RenderTransformOrigin = new Point(.5, .5);
        button.PreviewMouseLeftButtonDown += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.88, TimeSpan.FromMilliseconds(60)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.88, TimeSpan.FromMilliseconds(60)));
        };
        button.LostMouseCapture += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
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
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(3),
            Child = new System.Windows.Shapes.Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(value) },
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(color == _state.Settings.Color ? 1.5 : 0)
        };
        var button = new Button
        {
            Content = circle,
            Width = 30,
            Height = 34,
            Margin = new Thickness(1),
            Padding = new Thickness(5),
            ToolTip = $"Color #{color & 0xFFFFFF:X6}",
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
        style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        var selected = new Trigger { Property = Button.TagProperty, Value = true };
        selected.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromArgb(110, 10, 132, 255))));
        style.Triggers.Add(selected);
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
            foreach (var button in pair.Value)
                SetSelected(button, pair.Key == _state.Settings.Tool && _state.IsDrawing);
        SetSelected(_normalButton, !_state.IsDrawing);
        SetSelected(_shapeButton, _state.IsDrawing && _state.Settings.Tool is
            DrawingTool.Line or DrawingTool.Arrow or DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Diamond);
        foreach (var toggle in _toggleButtons) SetSelected(toggle.Button, toggle.Selected());
        foreach (var pair in _colorIndicators)
            foreach (var indicator in pair.Value)
                indicator.BorderThickness = new Thickness(pair.Key == _state.Settings.Color ? 1.5 : 0);
    }

    private static void SetSelected(Button button, bool selected)
    {
        button.Tag = selected;
        System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Selected" : "Not selected");
    }

    private void ActivateTopmostWithoutFocus()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }
}
