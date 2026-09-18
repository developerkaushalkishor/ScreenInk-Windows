namespace ScreenInk.Core;

public static class InkGeometry
{
    public static InkRect Bounds(IEnumerable<InkPoint> points)
    {
        var values = points.ToArray();
        if (values.Length == 0) return new InkRect(0, 0, 0, 0);
        var minX = values.Min(point => point.X);
        var minY = values.Min(point => point.Y);
        var maxX = values.Max(point => point.X);
        var maxY = values.Max(point => point.Y);
        return new InkRect(minX, minY, maxX - minX, maxY - minY);
    }

    public static InkPoint Constrain(InkPoint point, InkRect bounds, double margin = 0) =>
        new(Math.Clamp(point.X, bounds.Left + margin, bounds.Right - margin),
            Math.Clamp(point.Y, bounds.Top + margin, bounds.Bottom - margin));

    public static InkPoint ConstrainShape(InkPoint start, InkPoint end, bool shift)
    {
        if (!shift) return end;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new InkPoint(start.X + Math.CopySign(size, dx == 0 ? 1 : dx),
            start.Y + Math.CopySign(size, dy == 0 ? 1 : dy));
    }

    public static InkPoint SnapLine(InkPoint start, InkPoint end, bool shift)
    {
        if (!shift) return end;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0) return end;
        var angle = Math.Atan2(dy, dx);
        var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        return new InkPoint(start.X + Math.Cos(snapped) * length,
            start.Y + Math.Sin(snapped) * length);
    }

    public static void AppendSample(List<InkPoint> points, InkPoint point,
        double minimumDistance, bool force = false)
    {
        if (points.Count == 0 || (InkPoint.Distance(points[^1], point) > 0 &&
            (force || InkPoint.Distance(points[^1], point) >= minimumDistance)))
            points.Add(point);
    }
}
