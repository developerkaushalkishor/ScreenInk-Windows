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
            TestTextEditor();
            TestToolbar();
            TestCombinedApp();
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
        var behind = new Window
        {
            Left = 20,
            Top = 20,
            Width = 500,
            Height = 400,
            Background = Brushes.White,
            Title = "ScreenInk test background"
        };
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
            foreach (var tool in new[] { DrawingTool.Line, DrawingTool.Arrow, DrawingTool.Ellipse, DrawingTool.Diamond, DrawingTool.Highlighter })
            {
                state.SelectTool(tool); Pump();
                Drag(overlay.Surface, new Point(120, 120), new Point(300, 220));
                Assert(overlay.Surface.Store.Strokes.Count == 1, $"{tool} must create a stroke.");
                Save(Render(overlay.Surface), $"tool-{tool}.png");
                overlay.Surface.Store.Clear();
            }
            state.SelectTool(DrawingTool.Laser); Pump();
            Drag(overlay.Surface, new Point(120, 120), new Point(300, 220));
            Pump(1600);
            Assert(overlay.Surface.Store.Strokes.Count == 0, "Laser must leave no permanent stroke.");
            var laserImage = Render(overlay.Surface);
            var tail = new byte[4]; laserImage.CopyPixels(new Int32Rect(300, 220, 1, 1), tail, 4, 0);
            Assert(tail[3] <= 1, "Laser must redraw after its final sample disappears.");
            overlay.Surface.SetBoard(BoardStyle.Whiteboard, true); Pump();
            Drag(overlay.Surface, new Point(80, 80), new Point(500, 400));
            Assert(overlay.Surface.Store.Strokes.Count == 1 && overlay.Surface.Store.Strokes[0].Kind == StrokeKind.Board,
                "Region gesture must create a board.");
            state.SelectTool(DrawingTool.Pen); Pump();
            Drag(overlay.Surface, new Point(150, 150), new Point(550, 180));
            Assert(overlay.Surface.Store.Strokes[1].ParentBoardId == overlay.Surface.Store.Strokes[0].Id,
                "A stroke started inside a board must belong to it.");
            Assert(overlay.Surface.Store.Strokes[1].Points.All(p => p.X <= 488), "Board stroke must stay inside its edge.");
            state.SelectTool(DrawingTool.Select); Pump();
            var childBefore = overlay.Surface.Store.Strokes[1].Points[0];
            Drag(overlay.Surface, new Point(85, 260), new Point(125, 290));
            var childAfter = overlay.Surface.Store.Strokes[1].Points[0];
            Assert(Math.Abs(childAfter.X - childBefore.X - 40) < 3 && Math.Abs(childAfter.Y - childBefore.Y - 30) < 3,
                "Board drag must move its content by the same amount.");
            overlay.Surface.Store.Undo(); Pump();
            Assert(overlay.Surface.Store.Strokes[1].Points[0] == childBefore, "Board and content must undo together.");
            Save(Render(overlay.Surface), "board-and-ink.png");
            overlay.Surface.Store.Clear(); Pump();
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

    private static void TestTextEditor()
    {
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Text,
            Points = [new(80, 80)],
            Text = "Handwriting test",
            FontFamily = "Segoe Print",
            FontSize = 28,
            TextAlignment = InkTextAlignment.Left
        };
        var editor = new TextEntryWindow(stroke, new Point(120, 140), 320);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var box = (TextBox)((Border)editor.Content).Child;
                Assert(box.Text == stroke.Text && box.FontSize == stroke.FontSize, "Text editor must preserve content and font.");
                Assert(box.IsKeyboardFocused, "Text editor must receive keyboard input.");
                Assert(NativeMethods.GetWindowRect(new WindowInteropHelper(editor).Handle, out var frame) &&
                    Math.Abs(frame.Left - 119) <= 2 && Math.Abs(frame.Top - 139) <= 2, "Editor must open at the clicked physical position.");
                Save(Render((Border)editor.Content), "text-editor.png");
                box.Text = "Edited text";
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            }
            catch { editor.Close(); throw; }
        };
        timer.Start();
        Assert(editor.ShowDialog() == true && editor.Value == "Edited text", "Enter must save inline text.");
        Console.WriteLine("PASS inline text placement, keyboard focus and commit");
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

    private static void TestCombinedApp()
    {
        using var controller = new ScreenInk.App.AppController();
        controller.Start(); Pump();
        var toolbar = Application.Current.Windows.OfType<ToolbarWindow>().Single();
        var overlay = Application.Current.Windows.OfType<OverlayWindow>().First();
        var row = (StackPanel)((Border)toolbar.Content).Child;
        var pen = row.Children.OfType<Button>().First(b => b.ToolTip?.ToString()?.StartsWith("Pen —") == true);
        Click(pen); Pump();
        Drag(overlay.Surface, new Point(150, 250), new Point(290, 290));
        Assert(overlay.Surface.Store.Strokes.Count == 1, "Combined app must draw after physically clicking Pen.");
        var normal = row.Children.OfType<Button>().First(b => b.ToolTip?.ToString()?.StartsWith("Normal mode") == true);
        var location = normal.PointToScreen(new Point(normal.ActualWidth / 2, normal.ActualHeight / 2));
        Assert(WindowFromPoint(new NativePoint { X = (int)location.X, Y = (int)location.Y }) == new WindowInteropHelper(toolbar).Handle,
            "Drawing activation must not cover toolbar with the canvas.");
        Click(normal); Pump();
        Assert((NativeMethods.GetWindowLongPtr(new WindowInteropHelper(overlay).Handle, NativeMethods.GwlExStyle).ToInt64()
            & NativeMethods.WsExTransparent) != 0, "Physical normal-mode button must restore click-through.");
        var more = row.Children.OfType<Button>().First(b => b.ToolTip?.ToString()?.StartsWith("More tools") == true);
        MouseAt(more, new Point(more.ActualWidth / 2, more.ActualHeight / 2)); Pump();
        Assert(toolbar.IsInteractionActive, "More popover must hold auto-hide open.");
        Console.WriteLine("PASS combined app: physical toolbar input, drawing, z-order and normal mode");
    }

    private static void Click(FrameworkElement element)
    {
        MouseAt(element, new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        mouse_event(0x0002, 0, 0, 0, 0); Pump(30);
        mouse_event(0x0004, 0, 0, 0, 0); Pump();
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
