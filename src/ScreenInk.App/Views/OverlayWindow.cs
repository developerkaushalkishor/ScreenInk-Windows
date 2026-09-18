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
    internal DisplayInfo Display { get; }
    internal InkSurface Surface { get; }

    internal OverlayWindow(DisplayInfo display, AppState state)
    {
        Display = display;
        _state = state;
        Title = $"ScreenInk Canvas — {display.Id}";
        Left = display.Left;
        Top = display.Top;
        Width = display.Width;
        Height = display.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Surface = new InkSurface(state);
        Content = Surface;
        SourceInitialized += (_, _) => ApplyInteractionMode();
        _state.Changed += ApplyState;
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
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (!_state.IsDrawing) style |= NativeMethods.WsExTransparent;
        else style &= ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, (nint)style);
    }
}
