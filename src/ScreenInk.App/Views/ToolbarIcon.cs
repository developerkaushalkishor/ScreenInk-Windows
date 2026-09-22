using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace ScreenInk.App.Views;

internal enum ToolbarIcon
{
    Drag, Cursor, Select, Pen, Highlighter, Eraser, Laser, Shapes, Line, Arrow,
    Rectangle, Ellipse, Diamond, Text, Palette, Width, Undo, Redo, Clear,
    Fade, Ink, Board, Screenshot, AutoHide, Power, Hide, More
}

internal static class ToolbarIcons
{
    private static readonly IReadOnlyDictionary<ToolbarIcon, string> Data =
        new Dictionary<ToolbarIcon, string>
        {
            [ToolbarIcon.Drag] = "M8,6 L8.01,6 M8,12 L8.01,12 M8,18 L8.01,18 M16,6 L16.01,6 M16,12 L16.01,12 M16,18 L16.01,18",
            [ToolbarIcon.Cursor] = "M5,3 L5,19 L10,14 L14,22 L17,20 L13,13 L20,13 Z",
            [ToolbarIcon.Select] = "M4,5 L9,5 M15,5 L20,5 M20,5 L20,10 M20,15 L20,19 M20,19 L15,19 M9,19 L4,19 M4,19 L4,14 M4,10 L4,5",
            [ToolbarIcon.Pen] = "M4,20 L7,14 L16,5 L19,8 L10,17 Z M14,7 L17,10 M4,20 L10,17",
            [ToolbarIcon.Highlighter] = "M5,16 L15,6 L19,10 L9,20 L4,20 Z M12,9 L16,13 M5,22 L15,22",
            [ToolbarIcon.Eraser] = "M4,15 L13,6 Q14,5 15,6 L20,11 Q21,12 20,13 L13,20 L8,20 L4,16 Z M9,20 L16,13",
            [ToolbarIcon.Laser] = "M4,20 L13,11 M10,8 L13,11 L16,8 M13,4 L13,1 M19,6 L22,3 M20,12 L23,12",
            [ToolbarIcon.Shapes] = "M3,5 L11,5 L11,13 L3,13 Z M14,11 A5,5 0 1 0 14,21 A5,5 0 1 0 14,11",
            [ToolbarIcon.Line] = "M4,19 L20,5",
            [ToolbarIcon.Arrow] = "M4,19 L20,5 M13,5 L20,5 L20,12",
            [ToolbarIcon.Rectangle] = "M4,6 Q4,4 6,4 L18,4 Q20,4 20,6 L20,18 Q20,20 18,20 L6,20 Q4,20 4,18 Z",
            [ToolbarIcon.Ellipse] = "M3,12 A9,7 0 1 0 21,12 A9,7 0 1 0 3,12",
            [ToolbarIcon.Diamond] = "M12,3 Q13,3 14,4 L20,10 Q21,12 20,14 L14,20 Q12,21 10,20 L4,14 Q3,12 4,10 L10,4 Q11,3 12,3 Z",
            [ToolbarIcon.Text] = "M5,5 L19,5 M12,5 L12,20 M8,20 L16,20",
            [ToolbarIcon.Palette] = "M12,3 A9,9 0 1 0 12,21 Q15,21 14,18 Q13,15 17,15 L19,15 Q21,15 21,12 A9,9 0 0 0 12,3 M8,8 L8.01,8 M13,7 L13.01,7 M17,10 L17.01,10 M7,14 L7.01,14",
            [ToolbarIcon.Width] = "M5,7 L19,7 M5,12 L19,12 M5,18 L19,18",
            [ToolbarIcon.Undo] = "M9,7 L4,12 L9,17 M5,12 L14,12 Q20,12 20,18",
            [ToolbarIcon.Redo] = "M15,7 L20,12 L15,17 M19,12 L10,12 Q4,12 4,18",
            [ToolbarIcon.Clear] = "M6,7 L18,7 M9,7 L9,4 L15,4 L15,7 M8,9 L9,21 L15,21 L16,9",
            [ToolbarIcon.Fade] = "M12,3 A9,9 0 1 0 21,12 M12,7 L12,12 L16,14 M18,4 L21,4 L21,7",
            [ToolbarIcon.Ink] = "M3,12 Q7,6 12,6 Q17,6 21,12 Q17,18 12,18 Q7,18 3,12 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9",
            [ToolbarIcon.Board] = "M3,5 L21,5 L21,19 L3,19 Z M6,8 L18,8 L18,16 L6,16 Z",
            [ToolbarIcon.Screenshot] = "M4,7 L8,7 L10,4 L15,4 L17,7 L21,7 L21,19 L4,19 Z M12,10 A3,3 0 1 0 12,16 A3,3 0 1 0 12,10",
            [ToolbarIcon.AutoHide] = "M3,12 Q7,6 12,6 Q17,6 21,12 Q17,18 12,18 Q7,18 3,12 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 M17,4 L21,4 L21,8",
            [ToolbarIcon.Power] = "M12,3 L12,12 M7,5 Q3,8 3,13 A9,9 0 1 0 17,5",
            [ToolbarIcon.Hide] = "M5,15 L12,8 L19,15",
            [ToolbarIcon.More] = "M5,12 A1,1 0 1 0 7,12 A1,1 0 1 0 5,12 M11,12 A1,1 0 1 0 13,12 A1,1 0 1 0 11,12 M17,12 A1,1 0 1 0 19,12 A1,1 0 1 0 17,12"
        };

    internal static FrameworkElement Create(ToolbarIcon icon, Brush? stroke = null)
    {
        var path = new ShapePath
        {
            Data = Geometry.Parse(Data[icon]),
            Stroke = stroke ?? Brushes.White,
            StrokeThickness = 1.75,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.None
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = 20, Height = 20, Child = canvas };
    }
}
