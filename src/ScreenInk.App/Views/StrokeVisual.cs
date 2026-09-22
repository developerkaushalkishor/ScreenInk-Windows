using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScreenInk.Core;
using Pen = System.Windows.Media.Pen;

namespace ScreenInk.App.Views;

internal static class StrokeVisual
{
    internal static FormattedText Text(InkStroke stroke, double dpi, Brush brush)
    {
        var text = new FormattedText(stroke.Text ?? string.Empty, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(stroke.FontFamily), stroke.FontSize, brush, dpi)
        {
            MaxTextWidth = Math.Max(20, stroke.TextWidth),
            TextAlignment = stroke.TextAlignment switch
            {
                InkTextAlignment.Center => TextAlignment.Center,
                InkTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            }
        };
        return text;
    }

    internal static Geometry Geometry(InkStroke stroke)
    {
        var points = stroke.Points;
        if (points.Count == 0) return System.Windows.Media.Geometry.Empty;
        var a = points[0];
        var b = points[^1];
        var rect = new Rect(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new Size(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)));
        if (stroke.Kind == StrokeKind.Board) return new RectangleGeometry(rect, 16, 16);
        if (stroke.Kind is StrokeKind.Ellipse or StrokeKind.Rectangle or StrokeKind.Diamond)
        {
            var contour = HandDrawnGeometry.Points(stroke.Kind, a, b);
            var shape = new StreamGeometry();
            using (var context = shape.Open())
            {
                context.BeginFigure(new Point(contour[0].X, contour[0].Y), true, true);
                context.PolyLineTo(contour.Skip(1).Select(p => new Point(p.X, p.Y)).ToArray(), true, true);
            }
            return shape;
        }
        if (points.Count == 1)
            return new EllipseGeometry(new Point(a.X, a.Y), .01, .01);
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            context.BeginFigure(new Point(a.X, a.Y), false, false);
            context.PolyLineTo(points.Skip(1).Select(p => new Point(p.X, p.Y)).ToArray(), true, false);
            if (stroke.Kind == StrokeKind.Arrow)
            {
                var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                foreach (var offset in new[] { 2.55, -2.55 })
                {
                    context.BeginFigure(new Point(b.X, b.Y), false, false);
                    context.LineTo(new Point(b.X + Math.Cos(angle + offset) * 14,
                        b.Y + Math.Sin(angle + offset) * 14), true, false);
                }
            }
        }
        return path;
    }

    internal static Rect Bounds(InkStroke stroke, double dpi)
    {
        if (stroke.Points.Count == 0) return Rect.Empty;
        if (stroke.Kind == StrokeKind.Text)
        {
            var text = Text(stroke, dpi, Brushes.White);
            return new Rect(stroke.Points[0].X, stroke.Points[0].Y, stroke.TextWidth, text.Height);
        }
        var bounds = Geometry(stroke).Bounds;
        bounds.Inflate(stroke.Width / 2, stroke.Width / 2);
        return bounds;
    }

    internal static bool Hit(InkStroke stroke, InkPoint point, double tolerance, double dpi, bool interior)
    {
        if (stroke.Points.Count == 0) return false;
        var location = new Point(point.X, point.Y);
        if (stroke.Kind == StrokeKind.Text) return Bounds(stroke, dpi).Contains(location);
        var geometry = Geometry(stroke);
        if (stroke.Kind == StrokeKind.Board)
        {
            var bounds = geometry.Bounds;
            var inner = bounds;
            inner.Inflate(-12, -12);
            bounds.Inflate(tolerance, tolerance);
            return bounds.Contains(location) && !inner.Contains(location);
        }
        var pen = new Pen(Brushes.White, stroke.Width + tolerance * 2)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        return geometry.StrokeContains(pen, location) ||
            (interior && stroke.Kind is StrokeKind.Rectangle or StrokeKind.Ellipse or StrokeKind.Diamond &&
             geometry.FillContains(location));
    }

}
