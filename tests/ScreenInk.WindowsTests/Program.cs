using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenInk.App.Interop;
using ScreenInk.App.Models;
using ScreenInk.App.Services;
using ScreenInk.App.Views;
using ScreenInk.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            TestGeometry();
            TestNativeInput();
            TestToolbar();
            Console.WriteLine("All Windows UI regression checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { app.Shutdown(); }
    }

    private static void TestGeometry()
    {
        foreach (var kind in new[] { StrokeKind.Rectangle, StrokeKind.Ellipse, StrokeKind.Diamond })
        {
            var stroke = new InkStroke { Kind = kind, Points = [new(50, 50), new(250, 150)] };
            Assert(StrokeVisual.Hit(stroke, new(150, 50), 8, 1, false), $"{kind} outline must be erasable.");
            Assert(StrokeVisual.Hit(stroke, new(150, 100), 8, 1, true), $"{kind} interior must be selectable.");
            Assert(!StrokeVisual.Hit(stroke, new(150, 100), 8, 1, false), $"{kind} diagonal is not its outline.");
        }
        var board = new InkStroke { Kind = StrokeKind.Board, Points = [new(0, 0), new(300, 200)] };
        Assert(StrokeVisual.Hit(board, new(5, 100), 7, 1, true), "Board frame must select.");
        Assert(!StrokeVisual.Hit(board, new(150, 100), 7, 1, true), "Board interior must leave elements selectable.");
        foreach (var tool in Enum.GetValues<DrawingTool>()) Assert(ToolCursors.For(tool) is not null, $"Cursor {tool}");
        Console.WriteLine("PASS rendered shape hit testing, board frame selection, all tool cursors");
    }

    private static void TestNativeInput()
    {
        var display = DisplayService.GetDisplays().First();
        var settings = new AppSettings { AutoHideToolbar = false };
        var state = new AppState(settings);
        var behind = new Window { Left = 20, Top = 20, Width = 500, Height = 400,
            Background = Brushes.White, Title = "ScreenInk test background" };
        var overlay = new OverlayWindow(display, state);
        overlay.Surface.RequestNormalMode += () => state.SetDrawing(false);
        try
        {
            behind.Show(); overlay.Show(); Pump();
            var hwnd = new WindowInteropHelper(overlay).Handle;
            var point = overlay.Surface.PointToScreen(new Point(160, 180));
            var location = new NativePoint { X = (int)point.X, Y = (int)point.Y };
            state.SelectTool(DrawingTool.Pen); Pump();
            Assert(WindowFromPoint(location) == hwnd, "Blank drawing canvas must receive native mouse input.");
            Assert(overlay.Surface.InputHitTest(new Point(160, 180)) == overlay.Surface,
                "Blank drawing canvas must receive WPF mouse input.");
            Assert((NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTransparent) == 0,
                "Drawing must remove native click-through.");
            Drag(overlay.Surface, new Point(160, 180), new Point(290, 240));
            Assert(overlay.Surface.Store.Strokes.Count == 1, "Physical pen drag must create one stroke.");
            state.SelectTool(DrawingTool.Rectangle); Pump();
            Drag(overlay.Surface, new Point(100, 280), new Point(260, 360));
            Assert(overlay.Surface.Store.Strokes.Count == 2, "Physical rectangle drag must create a shape.");
            Assert(overlay.Surface.Store.Strokes[1].Points.Count == 2, "Shapes must retain exactly two endpoints.");
            state.SelectTool(DrawingTool.Select); Pump();
            Drag(overlay.Surface, new Point(180, 320), new Point(240, 320));
            Assert(overlay.Surface.Store.Strokes[1].Points[0].X > 140, "Selected shape interior must move.");
            overlay.Surface.Store.Undo(); Pump();
            Assert(Math.Abs(overlay.Surface.Store.Strokes[1].Points[0].X - 100) < 3, "One Undo must revert the complete drag.");
            overlay.Surface.Store.Redo(); Pump();
            state.SelectTool(DrawingTool.Eraser); Pump();
            Drag(overlay.Surface, new Point(230, 280), new Point(240, 280));
            Assert(overlay.Surface.Store.Strokes.Count == 1, "Eraser must hit the rectangle outline.");
            overlay.Surface.Store.Undo(); Pump();
            Assert(overlay.Surface.Store.Strokes.Count == 2, "Undo must restore the erased shape.");
            overlay.Surface.Store.Clear(); Pump();
            Assert(overlay.Surface.Store.Strokes.Count == 0, "Clear must remove all ink.");
            var bitmap = Render(overlay.Surface);
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(160, 180, 1, 1), pixel, 4, 0);
            Assert(pixel[3] > 0, "Blank canvas needs nonzero alpha in drawing mode.");
            MouseAt(overlay.Surface, new Point(150, 180));
            mouse_event(0x0008, 0, 0, 0, 0); mouse_event(0x0010, 0, 0, 0, 0); Pump();
            Assert(!state.IsDrawing, "Right click must exit drawing.");
            Assert(WindowFromPoint(location) != hwnd, "Normal mode must pass native mouse input through.");
            Console.WriteLine("PASS native pen, shapes, selection, eraser, history, clear, right-click and click-through");
        }
        finally { overlay.Close(); behind.Close(); }
    }

    private static void TestToolbar()
    {
        var state = new AppState(new AppSettings { AutoHideToolbar = false });
        var display = DisplayService.GetDisplays().First();
        var toolbar = new ToolbarWindow(state);
        try
        {
            toolbar.PlaceTopCenter(display); toolbar.ShowImmediately(); Pump();
            var root = (Border)toolbar.Content;
            var row = (StackPanel)root.Child;
            var pen = row.Children.OfType<Button>().First(b => b.ToolTip?.ToString()?.StartsWith("Pen —") == true);
            var peer = new ButtonAutomationPeer(pen);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); Pump();
            Assert(state.IsDrawing && state.Settings.Tool == DrawingTool.Pen, "Toolbar Pen must enable drawing.");
            Assert(Equals(pen.Tag, true), "Selected tool must show feedback.");
            var frame = toolbar.PhysicalFrame();
            toolbar.HideAnimated(); toolbar.ShowAnimated(); Pump();
            Assert(toolbar.IsVisible, "A cancelled hide must not hide a newly revealed toolbar.");
            Assert(Math.Abs(toolbar.PhysicalFrame().Left - frame.Left) < 2, "Reveal must preserve position.");
            Save(Render(root), "toolbar-wide.png");
            toolbar.ConfigureForDisplay(display with { Width = 380 * display.DpiScaleX }); Pump();
            Assert(toolbar.ActualWidth <= 356.5, "Narrow toolbar must fit the display.");
            var more = row.Children.OfType<Button>().First(b => b.ToolTip?.ToString()?.StartsWith("More tools") == true);
            Assert(more.IsVisible, "More must remain accessible on narrow displays.");
            Save(Render(root), "toolbar-narrow.png");
            Console.WriteLine("PASS toolbar actions, selection feedback, reveal cancellation and narrow display layout");
        }
        finally { toolbar.Close(); }
    }

    private static void Drag(FrameworkElement surface, Point start, Point end)
    {
        MouseAt(surface, start); mouse_event(0x0002, 0, 0, 0, 0); Pump(40);
        for (var i = 1; i <= 12; i++)
        {
            MouseAt(surface, new Point(start.X + (end.X - start.X) * i / 12,
                start.Y + (end.Y - start.Y) * i / 12)); Pump(20);
        }
        mouse_event(0x0004, 0, 0, 0, 0); Pump();
    }
    private static void MouseAt(FrameworkElement surface, Point point)
    { var value = surface.PointToScreen(point); SetCursorPos((int)value.X, (int)value.Y); Pump(20); }
    private static void Pump(int milliseconds = 250)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static RenderTargetBitmap Render(FrameworkElement view)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight),
            96, 96, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap;
    }
    private static void Save(BitmapSource image, string filename)
    {
        Directory.CreateDirectory("artifacts/ui");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine("artifacts/ui", filename)); encoder.Save(stream);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
}
