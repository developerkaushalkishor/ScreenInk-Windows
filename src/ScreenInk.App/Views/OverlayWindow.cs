using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenInk.App.Interop;
using ScreenInk.App.Models;
using ScreenInk.App.Services;

namespace ScreenInk.App.Views;

internal sealed class OverlayWindow : Window
{
    private readonly AppState _state;
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
        SourceInitialized += (_, _) => { UpdateDisplay(display); ApplyInteractionMode(); };
        Loaded += (_, _) => UpdateDisplay(Display);
        Closed += (_, _) => { _state.Changed -= ApplyState; Surface.Dispose(); };
        _state.Changed += ApplyState;
    }

    internal void UpdateDisplay(DisplayInfo display)
    {
        Display = display;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        NativeMethods.SetWindowPos(handle, 0, (int)display.Left, (int)display.Top,
            (int)display.Width, (int)display.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
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
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow;
        if (!_state.IsDrawing) style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        else style &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, (nint)style);
        NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);
    }
}
