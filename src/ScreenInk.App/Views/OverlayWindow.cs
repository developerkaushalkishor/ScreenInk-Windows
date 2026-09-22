using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenInk.App.Interop;
using ScreenInk.App.Models;
using ScreenInk.App.Services;

namespace ScreenInk.App.Views;

internal sealed class OverlayWindow : Window
{
    private readonly AppState _state;
    private bool _placementQueued;
    private bool _closed;
    internal DisplayInfo Display { get; private set; }
    internal InkSurface Surface { get; }

    internal OverlayWindow(DisplayInfo display, AppState state)
    {
        Display = display;
        _state = state;
        Title = $"ScreenInk Canvas — {display.Id}";
        Width = display.Width / display.DpiScaleX;
        Height = display.Height / display.DpiScaleY;
        ShowActivated = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Surface = new InkSurface(state);
        Content = Surface;
        SourceInitialized += (_, _) => { UpdateDisplay(Display); ApplyInteractionMode(); };
        Loaded += (_, _) => QueuePlacement();
        ContentRendered += (_, _) => QueuePlacement();
        SizeChanged += (_, _) => QueuePlacement();
        IsVisibleChanged += (_, _) => { if (IsVisible) QueuePlacement(); };
        Activated += (_, _) => QueuePlacement();
        Deactivated += (_, _) => QueuePlacement();
        Closed += (_, _) => { _closed = true; _state.Changed -= ApplyState; Surface.Dispose(); };
        _state.Changed += ApplyState;
    }

    internal void UpdateDisplay(DisplayInfo display)
    {
        Display = display;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        // WPF may apply a DPI-suggested rectangle after our initial native placement.
        // Reconcile actual HWND bounds even when the monitor descriptor has not changed.
        if (!NativeMethods.GetWindowRect(handle, out var rect) || rect.Left != display.Left ||
            rect.Top != display.Top || rect.Right - rect.Left != display.Width || rect.Bottom - rect.Top != display.Height)
        {
            NativeMethods.SetWindowPos(handle, 0, (int)display.Left, (int)display.Top,
                (int)display.Width, (int)display.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
        }
        ApplyInteractionMode();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        QueuePlacement();
    }

    private void QueuePlacement()
    {
        if (_closed || _placementQueued) return;
        _placementQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _placementQueued = false;
            if (!_closed) UpdateDisplay(Display);
        }));
    }

    private void ApplyState()
    {
        if (!_state.Settings.IsEnabled) Hide();
        else if (!IsVisible) Show();
        ApplyInteractionMode();
        Surface.InvalidateVisual();
    }

    private void ApplyInteractionMode()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var original = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        var style = original;
        style |= NativeMethods.WsExToolWindow;
        if (!_state.IsDrawing) style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        else style &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        if (style == original) return;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, (nint)style);
        NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);
    }
}
