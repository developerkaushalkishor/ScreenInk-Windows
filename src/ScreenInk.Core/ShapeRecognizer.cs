namespace ScreenInk.Core;

public readonly record struct RecognizedShape(StrokeKind Kind, InkPoint Start, InkPoint End);

public static class ShapeRecognizer
{
    public static RecognizedShape? Recognize(IReadOnlyList<InkPoint> points)
    {
        if (points.Count < 12) return null;
        var bounds = InkGeometry.Bounds(points);
        if (bounds.Width < 24 || bounds.Height < 24) return null;
        var diagonal = Math.Sqrt((bounds.Width * bounds.Width) + (bounds.Height * bounds.Height));
        if (InkPoint.Distance(points[0], points[^1]) > diagonal * 0.24) return null;

        var center = new InkPoint(bounds.CenterX, bounds.CenterY);
        var radii = points.Select(point =>
        {
            var normalizedX = (point.X - center.X) / Math.Max(1, bounds.Width / 2);
            var normalizedY = (point.Y - center.Y) / Math.Max(1, bounds.Height / 2);
            return Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
        }).ToArray();
        var radialError = radii.Average(radius => Math.Abs(radius - 1));
        if (radialError < 0.22)
            return new RecognizedShape(StrokeKind.Ellipse,
                new InkPoint(bounds.Left, bounds.Top), new InkPoint(bounds.Right, bounds.Bottom));

        var cornerDistance = points.Average(point =>
        {
            var corners = new[] {
                new InkPoint(bounds.Left, bounds.Top), new InkPoint(bounds.Right, bounds.Top),
                new InkPoint(bounds.Right, bounds.Bottom), new InkPoint(bounds.Left, bounds.Bottom)
            };
            return corners.Min(corner => InkPoint.Distance(point, corner));
        });
        if (cornerDistance / diagonal < 0.34)
            return new RecognizedShape(StrokeKind.Rectangle,
                new InkPoint(bounds.Left, bounds.Top), new InkPoint(bounds.Right, bounds.Bottom));
        return null;
    }
}
