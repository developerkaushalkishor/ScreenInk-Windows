using System.Windows.Interop;
using ScreenInk.App.Interop;

namespace ScreenInk.App.Services;

internal sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0x5349;
    private HwndSource? _source;
    private readonly Action _action;

    internal GlobalHotKey(Action action) => _action = action;

    internal void Attach(nint handle)
    {
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(Hook);
        NativeMethods.RegisterHotKey(handle, HotKeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift, 0x44);
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            _action();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_source is null) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotKeyId);
        _source.RemoveHook(Hook);
        _source = null;
    }
}
