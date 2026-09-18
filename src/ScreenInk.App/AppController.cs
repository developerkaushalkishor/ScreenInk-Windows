using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenInk.App.Models;
using ScreenInk.App.Services;
using ScreenInk.App.Views;

namespace ScreenInk.App;

internal sealed class AppController : IDisposable
{
    private readonly AppState _state = new(AppSettings.Load());
    private readonly Dictionary<string, OverlayWindow> _overlays = [];
    private readonly DispatcherTimer _displayTimer;
    private readonly DispatcherTimer _toolbarTimer;
    private ToolbarWindow? _toolbar;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _availabilityItem;
    private GlobalHotKey? _hotKey;
    private DateTime _lastToolbarInteraction = DateTime.UtcNow;

    internal AppController()
    {
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _displayTimer.Tick += (_, _) => ReconcileDisplays();
        _toolbarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _toolbarTimer.Tick += (_, _) => TrackToolbar();
    }

    internal void Start()
    {
        ReconcileDisplays();
        _toolbar = new ToolbarWindow(_state);
        _toolbar.UndoRequested += () => ActiveOverlay()?.Surface.Store.Undo();
        _toolbar.RedoRequested += () => ActiveOverlay()?.Surface.Store.Redo();
        _toolbar.ClearRequested += () => ActiveOverlay()?.Surface.Store.Clear();
        _toolbar.ScreenshotRequested += () =>
        {
            if (ActiveOverlay() is { } overlay) ScreenshotService.Capture(overlay.Display, _toolbar);
        };
        _toolbar.DisableRequested += () => _state.SetEnabled(false);
        _toolbar.MouseEnter += (_, _) => _lastToolbarInteraction = DateTime.UtcNow;
        _toolbar.SourceInitialized += (_, _) =>
        {
            _hotKey = new GlobalHotKey(() => _state.SetDrawing(!_state.IsDrawing));
            _hotKey.Attach(new WindowInteropHelper(_toolbar).Handle);
        };
        _state.Changed += ApplyState;
        CreateTrayIcon();
        ApplyState();
        _displayTimer.Start();
        _toolbarTimer.Start();
    }

    private void ReconcileDisplays()
    {
        var displays = DisplayService.GetDisplays();
        var activeIds = displays.Select(display => display.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var orphan in _overlays.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _overlays[orphan].Close();
            _overlays.Remove(orphan);
        }
        foreach (var display in displays)
        {
            if (_overlays.ContainsKey(display.Id)) continue;
            var overlay = new OverlayWindow(display, _state);
            overlay.Surface.RequestNormalMode += () => _state.SetDrawing(false);
            _overlays.Add(display.Id, overlay);
            if (_state.Settings.IsEnabled) overlay.Show();
        }
        if (_toolbar is not null && displays.FirstOrDefault(display => display.Primary) is { } primary)
            _toolbar.PlaceTopCenter(primary);
    }

    private void ApplyState()
    {
        if (_toolbar is null) return;
        if (_state.Settings.IsEnabled)
        {
            foreach (var overlay in _overlays.Values) if (!overlay.IsVisible) overlay.Show();
            if (!_toolbar.IsVisible) _toolbar.Show();
        }
        else
        {
            _toolbar.Hide();
            foreach (var overlay in _overlays.Values) overlay.Hide();
        }
        if (_availabilityItem is not null)
            _availabilityItem.Text = _state.Settings.IsEnabled ? "Disable ScreenInk" : "Enable ScreenInk";
    }

    private void TrackToolbar()
    {
        if (_toolbar is null || !_state.Settings.IsEnabled) return;
        var cursor = System.Windows.Forms.Cursor.Position;
        var display = DisplayService.GetDisplays().FirstOrDefault(value => cursor.X >= value.Left &&
            cursor.X <= value.Left + value.Width && cursor.Y >= value.Top && cursor.Y <= value.Top + value.Height);
        if (display.Width > 0)
        {
            var triggerWidth = Math.Max(320, _toolbar.ActualWidth);
            var atEdge = cursor.Y <= display.Top + 2 && cursor.X >= display.Left + (display.Width - triggerWidth) / 2 &&
                cursor.X <= display.Left + (display.Width + triggerWidth) / 2;
            if (atEdge)
            {
                _toolbar.PlaceTopCenter(display);
                _toolbar.Show();
                _lastToolbarInteraction = DateTime.UtcNow;
            }
        }
        if (_state.Settings.AutoHideToolbar && _toolbar.IsVisible && !_toolbar.IsMouseOver &&
            DateTime.UtcNow - _lastToolbarInteraction > TimeSpan.FromSeconds(2))
            _toolbar.Hide();
    }

    private OverlayWindow? ActiveOverlay()
    {
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
        menu.Items.Add("Show Toolbar", null, (_, _) => { if (_state.Settings.IsEnabled) _toolbar?.Show(); });
        menu.Items.Add("Toggle Drawing (Ctrl+Alt+Shift+D)", null,
            (_, _) => _state.SetDrawing(!_state.IsDrawing));
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
        _tray.DoubleClick += (_, _) => { if (_state.Settings.IsEnabled) _toolbar?.Show(); };
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
