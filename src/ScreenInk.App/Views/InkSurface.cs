using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenInk.App.Models;
using ScreenInk.Core;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace ScreenInk.App.Views;

internal sealed class InkSurface : FrameworkElement
{
    private readonly AppState _state;
    private readonly DispatcherTimer _animationTimer;
    private readonly List<(InkPoint Point, double Time)> _laser = [];
    private List<InkPoint>? _currentPoints;
    private InkPoint _start;
    private int? _selectedIndex;
    private InkPoint? _selectionDragOrigin;
    private InkStroke? _selectionOriginal;
    private HashSet<int> _pendingErase = [];

    internal StrokeStore Store { get; } = new();
    internal event Action? RequestNormalMode;

    internal InkSurface(AppState state)
    {
        _state = state;
        Focusable = true;
        Cursor = Cursors.Pen;
        _state.Changed += OnStateChanged;
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += (_, _) => RefreshAnimation();
        _animationTimer.Start();
    }

    private static double Now => Environment.TickCount64 / 1000.0;

    private void OnStateChanged()
    {
        Cursor = _state.Settings.Tool switch
        {
            DrawingTool.Text => Cursors.IBeam,
            DrawingTool.Eraser => Cursors.Cross,
            DrawingTool.Select => Cursors.SizeAll,
            _ => Cursors.Pen
        };
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!_state.IsDrawing) return;
        Focus();
        CaptureMouse();
        _start = Point(e.GetPosition(this));
        var tool = _state.Settings.Tool;
        if (tool == DrawingTool.Eraser)
        {
            _pendingErase.Clear();
            CollectErasures(_start);
        }
        else if (tool == DrawingTool.Select)
        {
            _selectedIndex = HitStroke(_start);
            _selectionDragOrigin = _start;
            _selectionOriginal = _selectedIndex is { } index ? Store.Strokes[index] : null;
        }
        else if (tool == DrawingTool.Text)
        {
            ReleaseMouseCapture();
            var dialog = new TextEntryWindow(_state.Settings.FontFamily,
                _state.Settings.FontSize, _state.Settings.TextAlignment);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
                Store.Append(CreateStroke([_start], StrokeKind.Text) with { Text = dialog.Value });
        }
        else if (tool == DrawingTool.Laser)
        {
            _laser.Add((_start, Now));
        }
        else
        {
            _currentPoints = [_start];
            if (IsShape(tool)) _currentPoints.Add(_start);
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_state.IsDrawing || e.LeftButton != MouseButtonState.Pressed) return;
        var point = Point(e.GetPosition(this));
        switch (_state.Settings.Tool)
        {
            case DrawingTool.Eraser:
                CollectErasures(point);
                break;
            case DrawingTool.Select:
                UpdateSelection(point);
                break;
            case DrawingTool.Laser:
                if (_laser.Count == 0 || InkPoint.Distance(_laser[^1].Point, point) >= 1)
                    _laser.Add((point, Now));
                if (_laser.Count > 50) _laser.RemoveAt(0);
                break;
            default:
                if (_currentPoints is null) break;
                if (IsShape(_state.Settings.Tool))
                {
                    var end = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                        ? ConstrainedEnd(_state.Settings.Tool, _start, point) : point;
                    _currentPoints[^1] = end;
                }
                else
                {
                    InkGeometry.AppendSample(_currentPoints, point,
                        Math.Clamp(_state.Settings.Width * 0.2, 0.75, 2));
                }
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_state.IsDrawing) return;
        ReleaseMouseCapture();
        var tool = _state.Settings.Tool;
        if (tool == DrawingTool.Eraser)
        {
            Store.Remove(_pendingErase);
            _pendingErase.Clear();
        }
        else if (tool == DrawingTool.Select)
        {
            _selectionDragOrigin = null;
            _selectionOriginal = null;
        }
        else if (tool != DrawingTool.Laser && tool != DrawingTool.Text && _currentPoints is { Count: > 0 })
        {
            InkGeometry.AppendSample(_currentPoints, Point(e.GetPosition(this)), 0, true);
            var kind = ToolKind(tool);
            if (tool == DrawingTool.Pen && ShapeRecognizer.Recognize(_currentPoints) is { } recognized)
            {
                kind = recognized.Kind;
                _currentPoints = [recognized.Start, recognized.End];
            }
            Store.Append(CreateStroke(_currentPoints, kind));
            _currentPoints = null;
        }
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _currentPoints = null;
        ReleaseMouseCapture();
        RequestNormalMode?.Invoke();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        RenderBoard(dc);
        if (!_state.InkVisible) return;
        var now = Now;
        for (var index = 0; index < Store.Strokes.Count; index++)
        {
            if (_pendingErase.Contains(index)) continue;
            DrawStroke(dc, Store.Strokes[index], now, index == _selectedIndex);
        }
        if (_currentPoints is { Count: > 0 })
            DrawStroke(dc, CreateStroke(_currentPoints, ToolKind(_state.Settings.Tool)), now, false);
        DrawLaser(dc, now);
    }

    private void RenderBoard(DrawingContext dc)
    {
        if (_state.BoardStyle == BoardStyle.Screen) return;
        var bounds = new Rect(18, 18, Math.Max(1, ActualWidth - 36), Math.Max(1, ActualHeight - 36));
        var frame = _state.BoardStyle == BoardStyle.Whiteboard
            ? Color.FromRgb(180, 185, 194) : Color.FromRgb(104, 58, 30);
        var fill = _state.BoardStyle == BoardStyle.Whiteboard
            ? Color.FromRgb(248, 247, 242) : Color.FromRgb(31, 37, 34);
        dc.DrawRoundedRectangle(new SolidColorBrush(frame), null, bounds, 16, 16);
        dc.DrawRoundedRectangle(new SolidColorBrush(fill), null,
            new Rect(30, 30, Math.Max(1, ActualWidth - 60), Math.Max(1, ActualHeight - 60)), 9, 9);
    }

    private void DrawStroke(DrawingContext dc, InkStroke stroke, double time, bool selected)
    {
        var opacity = stroke.VisibleOpacity(time);
        if (opacity <= 0 || stroke.Points.Count == 0) return;
        var color = ToColor(stroke.Color, opacity);
        if (stroke.Kind == StrokeKind.Text)
        {
            var text = new FormattedText(stroke.Text ?? string.Empty,
                System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface(stroke.FontFamily), stroke.FontSize,
                new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(stroke.Points[0].X, stroke.Points[0].Y));
            return;
        }
        var pen = new Pen(new SolidColorBrush(color), stroke.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        DrawGeometryForStroke(dc, stroke, pen);
        if (selected)
        {
            var bounds = InkGeometry.Bounds(stroke.Points);
            dc.DrawRectangle(null, new Pen(Brushes.Cyan, 1) { DashStyle = DashStyles.Dash },
                new Rect(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 10));
        }
    }

    private static void DrawGeometryForStroke(DrawingContext dc, InkStroke stroke, Pen pen)
    {
        var points = stroke.Points;
        if (points.Count == 1)
        {
            dc.DrawEllipse(pen.Brush, null, new Point(points[0].X, points[0].Y),
                pen.Thickness / 2, pen.Thickness / 2);
            return;
        }
        var start = points[0];
        var end = points[^1];
        if (stroke.Kind is StrokeKind.Rectangle or StrokeKind.Ellipse or StrokeKind.Diamond)
        {
            var rect = RectFrom(start, end);
            if (stroke.Kind == StrokeKind.Rectangle) dc.DrawRoundedRectangle(null, pen, rect, 12, 12);
            else if (stroke.Kind == StrokeKind.Ellipse) dc.DrawEllipse(null, pen,
                new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
            else
            {
                var geometry = new StreamGeometry();
                using var context = geometry.Open();
                context.BeginFigure(new Point(rect.Left + rect.Width / 2, rect.Top), false, true);
                context.PolyLineTo([new Point(rect.Right, rect.Top + rect.Height / 2),
                    new Point(rect.Left + rect.Width / 2, rect.Bottom),
                    new Point(rect.Left, rect.Top + rect.Height / 2)], true, true);
                dc.DrawGeometry(null, pen, geometry);
            }
            return;
        }
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            context.BeginFigure(new Point(start.X, start.Y), false, false);
            context.PolyLineTo(points.Skip(1).Select(point => new Point(point.X, point.Y)).ToArray(), true, false);
        }
        dc.DrawGeometry(null, pen, path);
        if (stroke.Kind == StrokeKind.Arrow) DrawArrowHead(dc, pen, points[^2], end);
    }

    private static void DrawArrowHead(DrawingContext dc, Pen pen, InkPoint previous, InkPoint end)
    {
        var angle = Math.Atan2(end.Y - previous.Y, end.X - previous.X);
        const double size = 14;
        foreach (var offset in new[] { 2.55, -2.55 })
            dc.DrawLine(pen, new Point(end.X, end.Y),
                new Point(end.X + Math.Cos(angle + offset) * size,
                    end.Y + Math.Sin(angle + offset) * size));
    }

    private void DrawLaser(DrawingContext dc, double time)
    {
        for (var index = 1; index < _laser.Count; index++)
        {
            var age = time - _laser[index].Time;
            var alpha = Math.Clamp(1 - age, 0, 1);
            if (alpha <= 0) continue;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 255, 46, 84)),
                Math.Max(1, _state.Settings.Width * alpha * 1.6))
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(_laser[index - 1].Point.X, _laser[index - 1].Point.Y),
                new Point(_laser[index].Point.X, _laser[index].Point.Y));
        }
    }

    private void RefreshAnimation()
    {
        var now = Now;
        _laser.RemoveAll(sample => now - sample.Time >= 1);
        var changed = Store.RemoveExpiredFadingStrokes(now) > 0 || _laser.Count > 0 ||
            Store.Strokes.Any(stroke => stroke.IsActivelyFading(now));
        if (changed) InvalidateVisual();
    }

    private void CollectErasures(InkPoint point)
    {
        for (var index = 0; index < Store.Strokes.Count; index++)
            if (HitTest(Store.Strokes[index], point, 8)) _pendingErase.Add(index);
    }

    private int? HitStroke(InkPoint point)
    {
        for (var index = Store.Strokes.Count - 1; index >= 0; index--)
            if (HitTest(Store.Strokes[index], point, 7)) return index;
        return null;
    }

    private void UpdateSelection(InkPoint point)
    {
        if (_selectedIndex is not { } index || _selectionDragOrigin is not { } origin ||
            _selectionOriginal is not { } original) return;
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var moved = original with
        {
            Points = original.Points.Select(value => new InkPoint(value.X + dx, value.Y + dy)).ToArray()
        };
        Store.Replace(new Dictionary<int, InkStroke> { [index] = moved });
        _selectionOriginal = moved;
        _selectionDragOrigin = point;
    }

    private static bool HitTest(InkStroke stroke, InkPoint point, double tolerance)
    {
        if (stroke.Kind == StrokeKind.Text)
        {
            var origin = stroke.Points[0];
            return new InkRect(origin.X, origin.Y, Math.Max(30, (stroke.Text?.Length ?? 1) * stroke.FontSize * 0.55),
                stroke.FontSize * 1.3).Contains(point);
        }
        if (stroke.Points.Count == 1) return InkPoint.Distance(stroke.Points[0], point) <= tolerance + stroke.Width / 2;
        for (var index = 1; index < stroke.Points.Count; index++)
            if (DistanceToSegment(point, stroke.Points[index - 1], stroke.Points[index]) <= tolerance + stroke.Width / 2)
                return true;
        return false;
    }

    private InkStroke CreateStroke(IReadOnlyList<InkPoint> points, StrokeKind kind)
    {
        var highlighter = _state.Settings.Tool == DrawingTool.Highlighter;
        return new InkStroke
        {
            Points = [.. points],
            Kind = kind,
            Color = _state.Settings.Color,
            Width = highlighter ? Math.Max(14, _state.Settings.Width * 4) : _state.Settings.Width,
            Opacity = highlighter ? 0.28 : 1,
            CreatedAt = Now,
            FadeAfter = _state.FadingInk ? _state.Settings.FadeDelay : null,
            FontFamily = _state.Settings.FontFamily,
            FontSize = _state.Settings.FontSize,
            TextAlignment = _state.Settings.TextAlignment
        };
    }

    private static InkPoint Point(System.Windows.Point point) => new(point.X, point.Y);
    private static bool IsShape(DrawingTool tool) => tool is DrawingTool.Line or DrawingTool.Arrow or
        DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Diamond;
    private static StrokeKind ToolKind(DrawingTool tool) => tool switch
    {
        DrawingTool.Line => StrokeKind.Line,
        DrawingTool.Arrow => StrokeKind.Arrow,
        DrawingTool.Rectangle => StrokeKind.Rectangle,
        DrawingTool.Ellipse => StrokeKind.Ellipse,
        DrawingTool.Diamond => StrokeKind.Diamond,
        _ => StrokeKind.Freehand
    };
    private static InkPoint ConstrainedEnd(DrawingTool tool, InkPoint start, InkPoint end) =>
        tool is DrawingTool.Line or DrawingTool.Arrow
            ? InkGeometry.SnapLine(start, end, true) : InkGeometry.ConstrainShape(start, end, true);
    private static Rect RectFrom(InkPoint first, InkPoint second) => new(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Abs(second.X - first.X), Math.Abs(second.Y - first.Y));
    private static Color ToColor(uint color, double opacity) => Color.FromArgb(
        (byte)(Math.Clamp(opacity, 0, 1) * 255), (byte)((color >> 16) & 0xFF),
        (byte)((color >> 8) & 0xFF), (byte)(color & 0xFF));
    private static double DistanceToSegment(InkPoint point, InkPoint start, InkPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0) return InkPoint.Distance(point, start);
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) /
            (dx * dx + dy * dy), 0, 1);
        return InkPoint.Distance(point, new InkPoint(start.X + t * dx, start.Y + t * dy));
    }
}
