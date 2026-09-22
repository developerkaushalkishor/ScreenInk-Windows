namespace ScreenInk.Core;

public static class SelectionTransform
{
    public static InkStroke Apply(InkStroke stroke, InkRect source, InkRect target)
    {
        var scaleX = source.Width > 0 ? target.Width / source.Width : 1;
        var scaleY = source.Height > 0 ? target.Height / source.Height : 1;
        var scale = Math.Max(0.01, Math.Min(Math.Abs(scaleX), Math.Abs(scaleY)));
        return stroke with
        {
            Points = stroke.Points.Select(point => new InkPoint(
                target.X + (point.X - source.X) * scaleX,
                target.Y + (point.Y - source.Y) * scaleY)).ToArray(),
            FontSize = Math.Clamp(stroke.FontSize * scale, 8, 240),
            TextWidth = Math.Max(20, stroke.TextWidth * Math.Abs(scaleX)),
            Width = Math.Clamp(stroke.Width * scale, 1, 80)
        };
    }
}
