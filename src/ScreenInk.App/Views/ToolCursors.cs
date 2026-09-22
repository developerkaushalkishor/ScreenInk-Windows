using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenInk.Core;

namespace ScreenInk.App.Views;

internal static class ToolCursors
{
    private static readonly Dictionary<DrawingTool, Cursor> Cache = [];
    internal static Cursor For(DrawingTool tool)
    {
        if (tool == DrawingTool.Text) return Cursors.IBeam;
        if (tool == DrawingTool.Select) return Cursors.Arrow;
        if (Cache.TryGetValue(tool, out var cursor)) return cursor;
        var icon = tool switch
        {
            DrawingTool.Eraser => ToolbarIcon.Eraser,
            DrawingTool.Highlighter => ToolbarIcon.Highlighter,
            DrawingTool.Laser => ToolbarIcon.Laser,
            DrawingTool.Line => ToolbarIcon.Line,
            DrawingTool.Arrow => ToolbarIcon.Arrow,
            DrawingTool.Rectangle => ToolbarIcon.Rectangle,
            DrawingTool.Ellipse => ToolbarIcon.Ellipse,
            DrawingTool.Diamond => ToolbarIcon.Diamond,
            _ => ToolbarIcon.Pen
        };
        var visual = new DrawingVisual();
        var element = ToolbarIcons.Create(icon);
        element.Measure(new Size(20, 20)); element.Arrange(new Rect(0, 0, 20, 20));
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225, 28, 28, 34)),
                new System.Windows.Media.Pen(Brushes.White, 1), new Rect(7, 7, 25, 25), 5, 5);
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(10, 10, 20, 20));
            dc.DrawLine(new System.Windows.Media.Pen(Brushes.Black, 3), new Point(0, 3), new Point(6, 3));
            dc.DrawLine(new System.Windows.Media.Pen(Brushes.Black, 3), new Point(3, 0), new Point(3, 6));
            dc.DrawLine(new System.Windows.Media.Pen(Brushes.White, 1), new Point(0, 3), new Point(6, 3));
            dc.DrawLine(new System.Windows.Media.Pen(Brushes.White, 1), new Point(3, 0), new Point(3, 6));
        }
        var bitmap = new RenderTargetBitmap(34, 34, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var image = new MemoryStream(); png.Save(image);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
            writer.Write((byte)34); writer.Write((byte)34); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)3); writer.Write((ushort)3); writer.Write((uint)image.Length); writer.Write((uint)22);
            writer.Write(image.ToArray());
        }
        stream.Position = 0;
        cursor = new Cursor(stream);
        Cache.Add(tool, cursor);
        return cursor;
    }
}
