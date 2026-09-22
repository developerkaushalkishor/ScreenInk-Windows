using System.Windows.Interop;
using System.Windows.Threading;
using ScreenInk.App.Models;
using ScreenInk.App.Services;
using ScreenInk.App.Views;
using ScreenInk.Core;

namespace ScreenInk.App;

internal sealed class AppController : IDisposable
{
    private readonly AppState _state = new(AppSettings.Load());
    private readonly Dictionary<string, OverlayWindow> _overlays = [];
    private readonly ToolbarRevealTracker _revealTracker = new();
    private readonly DispatcherTimer _displayTimer;
    private readonly DispatcherTimer _toolbarTimer;
    private ToolbarWindow? _toolbar;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _availabilityItem;
    private GlobalHotKey? _hotKey;
    private DateTime _lastToolbarInteraction = DateTime.UtcNow;
    private bool _toolbarPlacementInitialized;
    private bool _wasEnabled;
    private IReadOnlyList<DisplayInfo> _displays = [];

    internal AppController()
    {
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _displayTimer.Tick += (_, _) => ReconcileDisplays();
        _toolbarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _toolbarTimer.Tick += (_, _) => TrackToolbar();
    }

    internal void Start()
    {
        ReconcileDisplays();
        _toolbar = new ToolbarWindow(_state);
        _toolbar.BoardRequested += (style, all, region) =>
        {
            if (all) foreach (var overlay in _overlays.Values) overlay.Surface.SetBoard(style, false);
            else ActiveOverlay()?.Surface.SetBoard(style, region);
        };
        _toolbar.UndoRequested += () => ActiveOverlay()?.Surface.Undo();
        _toolbar.RedoRequested += () => ActiveOverlay()?.Surface.Redo();
        _toolbar.ClearRequested += () => ActiveOverlay()?.Surface.Clear();
        _toolbar.ScreenshotRequested += async (region, clipboard) =>
        {
            if (_toolbar.IsCommandActive || ActiveOverlay() is not { } overlay) return;
            _toolbar.IsCommandActive = true;
            try { await ScreenshotService.CaptureAsync(overlay.Display, _toolbar, region, clipboard); }
            finally { _toolbar.IsCommandActive = false; MarkToolbarInteraction(); }
        };
        _toolbar.DisableRequested += () => _state.SetEnabled(false);
        _toolbar.HideRequested += HideToolbarManually;
        _toolbar.DragCompleted += SaveDraggedToolbarPosition;
        _toolbar.MouseEnter += (_, _) => MarkToolbarInteraction();
        _toolbar.SourceInitialized += (_, _) =>
        {
            _hotKey = new GlobalHotKey(() =>
            {
                _state.SetDrawing(!_state.IsDrawing);
                ShowToolbarFromCommand();
            });
            _hotKey.Attach(new WindowInteropHelper(_toolbar).Handle);
        };
        _state.Changed += ApplyState;
        CreateTrayIcon();
        InitializeToolbarPlacement();
        ApplyState();
        _displayTimer.Start();
        _toolbarTimer.Start();
    }

    private void ReconcileDisplays()
    {
        var displays = DisplayService.GetDisplays();
        _displays = displays;
        var activeIds = displays.Select(display => display.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var orphan in _overlays.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _overlays[orphan].Close();
            _overlays.Remove(orphan);
        }
        foreach (var display in displays)
        {
            if (_overlays.TryGetValue(display.Id, out var existing))
            {
                if (existing.Display != display) existing.UpdateDisplay(display);
                continue;
            }
            var overlay = new OverlayWindow(display, _state);
            overlay.Activated += (_, _) => { if (_toolbar?.IsVisible == true) _toolbar.ActivateTopmostWithoutFocus(); };
            overlay.Surface.RequestNormalMode += () => _state.SetDrawing(false);
            _overlays.Add(display.Id, overlay);
            if (_state.Settings.IsEnabled) overlay.Show();
        }

        if (_toolbar is null || !_toolbarPlacementInitialized || displays.Count == 0) return;
        if (displays.Any(display => string.Equals(display.Id, _toolbar.DisplayId,
            StringComparison.OrdinalIgnoreCase))) return;
        var fallback = displays.FirstOrDefault(display => display.Primary);
        if (fallback.Width <= 0) fallback = displays[0];
        _toolbar.PlaceTopCenter(fallback);
        PersistToolbarPosition(fallback);
    }

    private void InitializeToolbarPlacement()
    {
        if (_toolbar is null) return;
        var displays = DisplayService.GetDisplays();
        if (displays.Count == 0) return;
        var selected = displays.FirstOrDefault(display => string.Equals(display.Id,
            _state.Settings.ToolbarDisplayId, StringComparison.OrdinalIgnoreCase));
        if (selected.Width <= 0) selected = displays.FirstOrDefault(display => display.Primary);
        if (selected.Width <= 0) selected = displays[0];

        _toolbar.ConfigureForDisplay(selected);
        if (_state.Settings.ToolbarLeft is { } left && _state.Settings.ToolbarTop is { } top &&
            PointInsideDisplay(left, top, selected))
        {
            _toolbar.MoveToPhysicalPixel(left, top);
            ClampAndPersistToolbar(selected);
        }
        else
        {
            _toolbar.PlaceTopCenter(selected);
            PersistToolbarPosition(selected);
        }
        _toolbarPlacementInitialized = true;
    }

    private void ApplyState()
    {
        if (_toolbar is null) return;
        if (_state.Settings.IsEnabled)
        {
            foreach (var overlay in _overlays.Values)
                if (!overlay.IsVisible) overlay.Show();
            if (!_wasEnabled) _toolbar.ShowImmediately();
        }
        else
        {
            _toolbar.ClosePopups();
            _toolbar.Hide();
            foreach (var overlay in _overlays.Values) overlay.Hide();
        }
        _wasEnabled = _state.Settings.IsEnabled;
        if (_availabilityItem is not null)
            _availabilityItem.Text = _state.Settings.IsEnabled ? "Disable ScreenInk" : "Enable ScreenInk";
    }

    private void TrackToolbar()
    {
        if (_toolbar is null || !_state.Settings.IsEnabled || _toolbar.IsCommandActive) return;
        var cursor = System.Windows.Forms.Cursor.Position;
        var display = DisplayAt(cursor.X, cursor.Y);
        var atEdge = false;
        if (display.Width > 0)
        {
            var toolbarWidthPixels = Math.Max(320, _toolbar.ActualWidth * display.DpiScaleX);
            var triggerWidth = Math.Min(display.Width - 24, toolbarWidthPixels);
            atEdge = cursor.Y >= display.Top && cursor.Y <= display.Top + 1 &&
                cursor.X >= display.Left + ((display.Width - triggerWidth) / 2) &&
                cursor.X <= display.Left + ((display.Width + triggerWidth) / 2);
        }

        if (_revealTracker.Enter(atEdge, atEdge ? display.Id : null))
        {
            if (!_toolbar.IsVisible || !string.Equals(_toolbar.DisplayId, display.Id,
                StringComparison.OrdinalIgnoreCase))
            {
                _toolbar.PlaceTopCenter(display);
                PersistToolbarPosition(display);
            }
            MarkToolbarInteraction();
            _toolbar.ShowAnimated();
        }

        if (_toolbar.IsInteractionActive || atEdge) MarkToolbarInteraction();
        if (_state.Settings.AutoHideToolbar && _toolbar.IsVisible && !_toolbar.IsInteractionActive && !atEdge &&
            DateTime.UtcNow - _lastToolbarInteraction > TimeSpan.FromSeconds(2))
            _toolbar.HideAnimated();
    }

    private void HideToolbarManually()
    {
        _state.SetDrawing(false);
        _toolbar?.HideAnimated();
        _revealTracker.RequireExitBeforeReveal();
    }

    private void ShowToolbarFromCommand()
    {
        if (_toolbar is null || !_state.Settings.IsEnabled) return;
        MarkToolbarInteraction();
        _toolbar.ShowAnimated();
    }

    private void SaveDraggedToolbarPosition()
    {
        if (_toolbar is null) return;
        var frame = _toolbar.PhysicalFrame();
        var display = DisplayAt(frame.Left + (frame.Width / 2), frame.Top + (frame.Height / 2));
        if (display.Width <= 0) return;
        _toolbar.ConfigureForDisplay(display);
        ClampAndPersistToolbar(display);
        MarkToolbarInteraction();
    }

    private void ClampAndPersistToolbar(DisplayInfo display)
    {
        if (_toolbar is null) return;
        var frame = _toolbar.PhysicalFrame();
        var marginX = ToolbarGeometry.ScreenMargin * display.DpiScaleX;
        var marginY = ToolbarGeometry.ScreenMargin * display.DpiScaleY;
        var left = Math.Clamp(frame.Left, display.Left + marginX,
            Math.Max(display.Left + marginX, display.Left + display.Width - frame.Width - marginX));
        var top = Math.Clamp(frame.Top, display.Top + marginY,
            Math.Max(display.Top + marginY, display.Top + display.Height - frame.Height - marginY));
        _toolbar.MoveToPhysicalPixel(left, top);
        PersistToolbarPosition(display);
    }

    private void PersistToolbarPosition(DisplayInfo display)
    {
        if (_toolbar is null) return;
        var frame = _toolbar.PhysicalFrame();
        _state.Settings.ToolbarDisplayId = display.Id;
        _state.Settings.ToolbarLeft = frame.Left;
        _state.Settings.ToolbarTop = frame.Top;
        _state.Settings.Save();
    }

    private static bool PointInsideDisplay(double x, double y, DisplayInfo display) =>
        x >= display.Left && x < display.Left + display.Width &&
        y >= display.Top && y < display.Top + display.Height;

    private DisplayInfo DisplayAt(double x, double y)
    {
        var displays = _displays;
        var display = displays.FirstOrDefault(value => x >= value.Left && x < value.Left + value.Width &&
            y >= value.Top && y < value.Top + value.Height);
        return display.Width > 0 ? display : displays.FirstOrDefault(value => value.Primary);
    }

    private void MarkToolbarInteraction() => _lastToolbarInteraction = DateTime.UtcNow;

    private OverlayWindow? ActiveOverlay()
    {
        if (_toolbar?.DisplayId is { } id && _overlays.TryGetValue(id, out var target)) return target;
        var cursor = System.Windows.Forms.Cursor.Position;
        return _overlays.Values.FirstOrDefault(overlay => cursor.X >= overlay.Display.Left &&
            cursor.X <= overlay.Display.Left + overlay.Display.Width && cursor.Y >= overlay.Display.Top &&
            cursor.Y <= overlay.Display.Top + overlay.Display.Height) ?? _overlays.Values.FirstOrDefault();
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        _availabilityItem = new System.Windows.Forms.ToolStripMenuItem();
        _availabilityItem.Click += (_, _) => _state.SetEnabled(!_state.Settings.IsEnabled);
        menu.Items.Add(_availabilityItem);
        menu.Items.Add("Show Toolbar", null, (_, _) => ShowToolbarFromCommand());
        menu.Items.Add("Toggle Drawing (Ctrl+Alt+Shift+D)", null, (_, _) =>
        {
            _state.SetDrawing(!_state.IsDrawing);
            ShowToolbarFromCommand();
        });
        menu.Items.Add("Quit ScreenInk", null, (_, _) => System.Windows.Application.Current.Shutdown());
        var executableIcon = Environment.ProcessPath is { } path
            ? System.Drawing.Icon.ExtractAssociatedIcon(path)
            : null;
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "ScreenInk — screen annotation",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = executableIcon ?? System.Drawing.SystemIcons.Application
        };
        _tray.DoubleClick += (_, _) => ShowToolbarFromCommand();
    }

    public void Dispose()
    {
        _displayTimer.Stop();
        _toolbarTimer.Stop();
        _hotKey?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        foreach (var overlay in _overlays.Values) overlay.Close();
        _toolbar?.Close();
    }
}
