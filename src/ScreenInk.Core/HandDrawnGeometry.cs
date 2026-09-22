namespace ScreenInk.Core;

/// <summary>Deterministic shape contours matching the macOS InkCore shape generator.</summary>
public static class HandDrawnGeometry
{
    public static IReadOnlyList<InkPoint> Points(StrokeKind kind, InkPoint start, InkPoint end)
    {
        var x = Math.Min(start.X, end.X); var y = Math.Min(start.Y, end.Y);
        var w = Math.Abs(end.X - start.X); var h = Math.Abs(end.Y - start.Y);
        if (w == 0 || h == 0) return [start, end];
        if (kind == StrokeKind.Ellipse)
        {
            var rx = w / 2; var ry = h / 2;
            var roughness = Math.Min(.8, Math.Min(rx, ry) * .012);
            return Enumerable.Range(0, 97).Select(index =>
            {
                var angle = index / 96d * 2 * Math.PI;
                var wobble = roughness * (.62 * Math.Sin(angle * 3 + .7) + .38 * Math.Sin(angle * 7 + 1.9));
                return new InkPoint(x + rx + Math.Cos(angle) * (rx + wobble),
                    y + ry + Math.Sin(angle) * (ry + wobble));
            }).ToArray();
        }
        InkPoint[] vertices = kind == StrokeKind.Diamond
            ? [new(x + w / 2, y), new(x + w, y + h / 2), new(x + w / 2, y + h), new(x, y + h / 2)]
            : [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
        var radius = kind == StrokeKind.Diamond ? Math.Min(22, Math.Min(w, h) * .12) : Math.Min(18, Math.Min(w, h) * .18);
        var points = new List<InkPoint>();
        for (var i = 0; i < 4; i++)
        {
            var corner = vertices[i];
            var entry = Toward(corner, vertices[(i + 3) % 4], radius);
            var exit = Toward(corner, vertices[(i + 1) % 4], radius);
            if (points.Count == 0) points.Add(entry); else AppendLine(points, entry);
            for (var step = 1; step <= 6; step++)
            {
                var t = step / 6d; var inverse = 1 - t;
                points.Add(new InkPoint(inverse * inverse * entry.X + 2 * inverse * t * corner.X + t * t * exit.X,
                    inverse * inverse * entry.Y + 2 * inverse * t * corner.Y + t * t * exit.Y));
            }
        }
        AppendLine(points, points[0]);
        var adjusted = points.Select((point, index) =>
        {
            var previous = points[Math.Max(0, index - 1)]; var next = points[Math.Min(points.Count - 1, index + 1)];
            var dx = next.X - previous.X; var dy = next.Y - previous.Y;
            var length = Math.Max(.001, InkPoint.Distance(previous, next));
            var offset = .42 * Math.Sin(index * 1.73 + .4);
            return new InkPoint(point.X - dy / length * offset, point.Y + dx / length * offset);
        }).ToArray();
        adjusted[^1] = adjusted[0];
        return adjusted;
    }

    private static InkPoint Toward(InkPoint origin, InkPoint target, double distance)
    {
        var length = Math.Max(.001, InkPoint.Distance(origin, target));
        var t = Math.Min(distance, length / 2) / length;
        return new InkPoint(origin.X + (target.X - origin.X) * t, origin.Y + (target.Y - origin.Y) * t);
    }

    private static void AppendLine(List<InkPoint> points, InkPoint end)
    {
        var start = points[^1];
        var steps = Math.Max(1, (int)Math.Ceiling(InkPoint.Distance(start, end) / 12));
        for (var step = 1; step <= steps; step++)
            points.Add(new InkPoint(start.X + (end.X - start.X) * step / steps,
                start.Y + (end.Y - start.Y) * step / steps));
    }
}
