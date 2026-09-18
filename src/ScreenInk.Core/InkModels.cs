namespace ScreenInk.Core;

public readonly record struct InkPoint(double X, double Y)
{
    public static double Distance(InkPoint a, InkPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

public readonly record struct InkRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public bool Contains(InkPoint point) => point.X >= Left && point.X <= Right &&
        point.Y >= Top && point.Y <= Bottom;
}

public enum DrawingTool
{
    Select, Pen, Highlighter, Eraser, Laser, Line, Arrow, Rectangle, Ellipse, Diamond, Text
}

public enum StrokeKind
{
    Freehand, Line, Arrow, Rectangle, Ellipse, Diamond, Text
}

public enum InkTextAlignment
{
    Left, Center, Right
}

public sealed record InkStroke
{
    public required IReadOnlyList<InkPoint> Points { get; init; }
    public uint Color { get; init; } = 0xFFBF5AF2;
    public double Width { get; init; } = 4;
    public double Opacity { get; init; } = 1;
    public double CreatedAt { get; init; }
    public double? FadeAfter { get; init; }
    public double FadeDuration { get; init; } = 1;
    public StrokeKind Kind { get; init; } = StrokeKind.Freehand;
    public string? Text { get; init; }
    public double FontSize { get; init; } = 28;
    public string FontFamily { get; init; } = "Segoe Print";
    public InkTextAlignment TextAlignment { get; init; }

    public double VisibleOpacity(double time)
    {
        if (FadeAfter is null) return Opacity;
        var progress = Math.Clamp((time - CreatedAt - FadeAfter.Value) / FadeDuration, 0, 1);
        return Opacity * (1 - progress);
    }

    public bool IsActivelyFading(double time) => FadeAfter is { } delay &&
        time - CreatedAt >= delay && time - CreatedAt < delay + FadeDuration;
}

public enum BoardStyle
{
    Screen, Whiteboard, Blackboard
}

public sealed record BoardState(BoardStyle Style = BoardStyle.Screen, InkRect? Region = null);
