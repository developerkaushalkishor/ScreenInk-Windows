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

internal sealed class InkSurface : FrameworkElement, IDisposable
{
    private readonly AppState _state;
    private readonly DispatcherTimer _animationTimer;
    private readonly List<(InkPoint Point, double Time)> _laser = [];
    private List<InkPoint>? _currentPoints;
    private InkPoint _start;
    private readonly HashSet<int> _selected = [];
    private Dictionary<int, InkStroke> _selectionPreview = [];
    private Dictionary<int, InkStroke> _selectionOriginals = [];
    private Rect _selectionBounds;
    private Rect? _marquee;
    private int _resizeCorner = -1;
    private uint _lastColor;
    private DrawingTool? _gestureTool;
    private HashSet<int> _pendingErase = [];
    private BoardStyle? _pendingBoard;
    private Guid? _drawingBoard;
    private InkRect? _drawingBounds;
    private bool _boardGesture;
    private string _lastFont;
    private double _lastFontSize;
    private InkTextAlignment _lastAlignment;
    private InkPoint _cursorPosition;
    private readonly List<(InkPoint Point, double Time)> _clicks = [];
    private bool _leftDown;
    private bool _effectsWereVisible;
    private int? _editingIndex;

    internal StrokeStore Store { get; } = new();
    internal event Action? RequestNormalMode;

    internal InkSurface(AppState state)
    {
        _state = state;
        _lastColor = state.Settings.Color;
        _lastFont = state.Settings.FontFamily;
        _lastFontSize = state.Settings.FontSize;
        _lastAlignment = state.Settings.TextAlignment;
        Focusable = true;
        Cursor = Cursors.Pen;
        _state.Changed += OnStateChanged;
        Store.Changed += InvalidateVisual;
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
        if (_lastColor != _state.Settings.Color && _state.Settings.Tool == DrawingTool.Select)
        {
            Store.Replace(_selected.Where(i => i < Store.Strokes.Count).ToDictionary(i => i,
                i => Store.Strokes[i] with { Color = _state.Settings.Color }));
        }
        if (_lastFont != _state.Settings.FontFamily || _lastFontSize != _state.Settings.FontSize ||
            _lastAlignment != _state.Settings.TextAlignment)
        {
            Store.Replace(_selected.Where(i => i < Store.Strokes.Count && Store.Strokes[i].Kind == StrokeKind.Text)
                .ToDictionary(i => i, i => Store.Strokes[i] with
                {
                    FontFamily = _state.Settings.FontFamily,
                    FontSize = _state.Settings.FontSize,
                    TextAlignment = _state.Settings.TextAlignment
                }));
            _lastFont = _state.Settings.FontFamily;
            _lastFontSize = _state.Settings.FontSize;
            _lastAlignment = _state.Settings.TextAlignment;
        }
        _lastColor = _state.Settings.Color;
        if (!_state.IsDrawing || (_gestureTool is { } tool && tool != _state.Settings.Tool)) CancelGesture();
        Cursor = _state.IsDrawing ? ToolCursors.For(_state.Settings.Tool) : Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!_state.IsDrawing) return;
        e.Handled = true;
        Focus();
        CaptureMouse();
        _start = Point(e.GetPosition(this));
        var tool = _state.Settings.Tool;
        _gestureTool = tool;
        var board = Store.Strokes.LastOrDefault(s => s.Kind == StrokeKind.Board &&
            BoardInterior(s).Contains(_start));
        _drawingBoard = board?.Id;
        _drawingBounds = board is null ? null : BoardInterior(board);
        if (_pendingBoard is not null)
        {
            _boardGesture = true;
            _currentPoints = [_start, _start];
            return;
        }
        if (tool == DrawingTool.Eraser)
        {
            _pendingErase.Clear();
            CollectErasures(_start);
        }
        else if (tool == DrawingTool.Select)
        {
            BeginSelection(_start);
        }
        else if (tool == DrawingTool.Text)
        {
            ReleaseMouseCapture();
            var existing = HitStroke(_start);
            var original = existing is { } textIndex && Store.Strokes[textIndex].Kind == StrokeKind.Text
                ? Store.Strokes[textIndex] : CreateStroke([_start], StrokeKind.Text);
            var origin = original.Points[0];
            var dialog = new TextEntryWindow(original, PointToScreen(new Point(origin.X, origin.Y)),
                Math.Max(40, (_drawingBounds?.Right ?? ActualWidth) - origin.X - 8));
            _editingIndex = existing is { } index && Store.Strokes[index].Kind == StrokeKind.Text ? existing : null;
            InvalidateVisual();
            bool saved;
            try { saved = dialog.ShowDialog() == true; }
            finally { _editingIndex = null; InvalidateVisual(); }
            if (saved && !string.IsNullOrWhiteSpace(dialog.Value))
            {
                var updated = original with { Text = dialog.Value, TextWidth = dialog.TextWidth };
                if (existing is { } i && Store.Strokes[i].Kind == StrokeKind.Text)
                    Store.Replace(new Dictionary<int, InkStroke> { [i] = updated });
                else Store.Append(updated);
            }
            _gestureTool = null;
        }
        else if (tool == DrawingTool.Laser)
        {
            _laser.Clear();
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
        if (!_state.IsDrawing || !IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var point = GesturePoint(e.GetPosition(this));
        if (_boardGesture && _currentPoints is not null)
        {
            _currentPoints[^1] = Point(e.GetPosition(this));
            InvalidateVisual();
            return;
        }
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
                if (_laser.Count > 2048) _laser.RemoveAt(0);
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
        if (!_state.IsDrawing || _gestureTool is not { } tool) return;
        _gestureTool = null;
        ReleaseMouseCapture();
        if (_boardGesture && _pendingBoard is { } style)
        {
            var bounds = RectFrom(_start, Point(e.GetPosition(this)));
            if (bounds.Width >= 80 && bounds.Height >= 60) AppendBoard(style, bounds);
            _boardGesture = false;
            _pendingBoard = null;
            Cursor = ToolCursors.For(_state.Settings.Tool);
            _currentPoints = null;
            InvalidateVisual();
            return;
        }
        if (tool == DrawingTool.Eraser)
        {
            Store.Remove(_pendingErase);
            _pendingErase.Clear();
        }
        else if (tool == DrawingTool.Select)
        {
            if (_selectionPreview.Count > 0) Store.Replace(_selectionPreview);
            _selectionPreview.Clear();
            _selectionOriginals.Clear();
            _marquee = null;
        }
        else if (tool != DrawingTool.Laser && tool != DrawingTool.Text && _currentPoints is { Count: > 0 })
        {
            if (IsShape(tool))
                _currentPoints[^1] = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? ConstrainedEnd(tool, _start, GesturePoint(e.GetPosition(this))) : GesturePoint(e.GetPosition(this));
            else InkGeometry.AppendSample(_currentPoints, GesturePoint(e.GetPosition(this)), 0, true);
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
        CancelGesture();
        RequestNormalMode?.Invoke();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // A zero-alpha layered window passes native input to applications below it.
        // Only drawing mode paints an almost invisible, nonzero-alpha input surface.
        if (_state.IsDrawing)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null,
                new Rect(RenderSize));
        foreach (var index in Enumerable.Range(0, Store.Strokes.Count).Where(i => Store.Strokes[i].Kind == StrokeKind.Board))
            DrawBoard(dc, _selectionPreview.GetValueOrDefault(index, Store.Strokes[index]));
        if (!_state.InkVisible) return;
        var now = Now;
        for (var index = 0; index < Store.Strokes.Count; index++)
        {
            if (_editingIndex == index || _pendingErase.Contains(index) || Store.Strokes[index].Kind == StrokeKind.Board) continue;
            DrawContainedStroke(dc, _selectionPreview.GetValueOrDefault(index, Store.Strokes[index]), now);
        }
        if (_currentPoints is { Count: > 0 })
        {
            if (_boardGesture) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(50, 10, 132, 255)),
                new Pen(Brushes.DeepSkyBlue, 2), RectFrom(_start, _currentPoints[^1]), 12, 12);
            else DrawContainedStroke(dc, CreateStroke(_currentPoints, ToolKind(_state.Settings.Tool)), now);
        }
        DrawLaser(dc, now);
        DrawCursorEffects(dc, now);
        if (_state.IsDrawing && _state.Settings.Tool == DrawingTool.Select) DrawSelection(dc);
    }

    internal void Undo() { CancelGesture(); _selected.Clear(); Store.Undo(); }
    internal void Redo() { CancelGesture(); _selected.Clear(); Store.Redo(); }
    internal void Clear() { CancelGesture(); _selected.Clear(); Store.Clear(); }

    internal void SetBoard(BoardStyle style, bool region)
    {
        if (style == BoardStyle.Screen)
        {
            Store.Remove(Enumerable.Range(0, Store.Strokes.Count).Where(i => Store.Strokes[i].Kind == StrokeKind.Board));
            return;
        }
        if (region)
        {
            _state.SelectTool(DrawingTool.Select);
            _pendingBoard = style;
            Cursor = Cursors.Cross;
        }
        else AppendBoard(style, new Rect(18, 18, Math.Max(80, ActualWidth - 36), Math.Max(60, ActualHeight - 36)));
    }

    private void AppendBoard(BoardStyle style, Rect rect) => Store.Append(new InkStroke
    {
        Kind = StrokeKind.Board,
        BoardStyle = style,
        Width = 0,
        Points = [new InkPoint(rect.Left, rect.Top), new InkPoint(rect.Right, rect.Bottom)]
    });

    private static InkRect BoardInterior(InkStroke board)
    {
        var bounds = InkGeometry.Bounds(board.Points);
        return new InkRect(bounds.X + 12, bounds.Y + 12, Math.Max(1, bounds.Width - 24), Math.Max(1, bounds.Height - 24));
    }

    private void DrawBoard(DrawingContext dc, InkStroke board)
    {
        var bounds = RectFrom(board.Points[0], board.Points[^1]);
        var white = board.BoardStyle == BoardStyle.Whiteboard;
        var frame = new LinearGradientBrush(white ? Color.FromRgb(220, 225, 232) : Color.FromRgb(142, 92, 53),
            white ? Color.FromRgb(120, 130, 145) : Color.FromRgb(83, 49, 27), 90);
        dc.DrawRoundedRectangle(frame, new Pen(Brushes.Black, .5), bounds, 16, 16);
        bounds.Inflate(-12, -12);
        if (bounds.IsEmpty) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(white ? Color.FromRgb(248, 247, 242) : Color.FromRgb(31, 37, 34)),
            new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1), bounds, 7, 7);
    }

    private void DrawContainedStroke(DrawingContext dc, InkStroke stroke, double now)
    {
        var boardIndex = Enumerable.Range(0, Store.Strokes.Count)
            .FirstOrDefault(i => Store.Strokes[i].Id == stroke.ParentBoardId, -1);
        var clip = boardIndex >= 0;
        if (clip)
        {
            var bounds = BoardInterior(_selectionPreview.GetValueOrDefault(boardIndex, Store.Strokes[boardIndex]));
            dc.PushClip(new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), 7, 7));
        }
        DrawStroke(dc, stroke, now, false);
        if (clip) dc.Pop();
    }

    private InkPoint GesturePoint(Point point)
    {
        var value = new InkPoint(point.X, point.Y);
        return _drawingBounds is { } bounds && _state.Settings.Tool != DrawingTool.Select
            ? InkGeometry.Constrain(value, bounds) : value;
    }

    private void DrawStroke(DrawingContext dc, InkStroke stroke, double time, bool selected)
    {
        var opacity = stroke.VisibleOpacity(time);
        if (opacity <= 0 || stroke.Points.Count == 0) return;
        // Apply opacity once to the complete stroke so highlighter overlaps stay uniform.
        dc.PushOpacity(opacity);
        var color = ToColor(stroke.Color, 1);
        if (stroke.Kind == StrokeKind.Text)
        {
            var text = StrokeVisual.Text(stroke, VisualTreeHelper.GetDpi(this).PixelsPerDip, new SolidColorBrush(color));
            dc.DrawText(text, new Point(stroke.Points[0].X, stroke.Points[0].Y));
            dc.Pop();
            return;
        }
        var pen = new Pen(new SolidColorBrush(color), stroke.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        DrawGeometryForStroke(dc, stroke, pen);
        dc.Pop();
        if (selected)
        {
            var bounds = InkGeometry.Bounds(stroke.Points);
            dc.DrawRectangle(null, new Pen(Brushes.Cyan, 1) { DashStyle = DashStyles.Dash },
                new Rect(bounds.X - 5, bounds.Y - 5, bounds.Width + 10, bounds.Height + 10));
        }
    }

    private static void DrawGeometryForStroke(DrawingContext dc, InkStroke stroke, Pen pen) =>
        dc.DrawGeometry(null, pen, StrokeVisual.Geometry(stroke));

    private void DrawLaser(DrawingContext dc, double time)
    {
        const double lifetime = 1.3;
        for (var i = 1; i < _laser.Count; i++)
        {
            var a = _laser[i - 1];
            var b = _laser[i];
            var endAge = (time - b.Time) / lifetime;
            if (endAge >= 1) continue;
            var start = a.Point;
            var cutoff = time - lifetime;
            if (a.Time < cutoff && b.Time > a.Time)
            {
                var t = (cutoff - a.Time) / (b.Time - a.Time);
                start = new InkPoint(a.Point.X + (b.Point.X - a.Point.X) * t,
                    a.Point.Y + (b.Point.Y - a.Point.Y) * t);
            }
            var strength = Math.Pow(Math.Clamp(1 - endAge, 0, 1), .65);
            var alpha = Math.Clamp((1 - endAge) * 3, 0, 1);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 255, 64, 90)),
                Math.Max(.01, _state.Settings.Width * 1.6 * strength))
            { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Round };
            dc.DrawLine(pen, new Point(start.X, start.Y), new Point(b.Point.X, b.Point.Y));
        }
    }

    private void DrawCursorEffects(DrawingContext dc, double time)
    {
        if (!_state.Settings.IsEnabled) return;
        if (_state.Settings.CursorHalo)
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(55, 255, 210, 50)),
                new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 210, 50)), 1.5),
                new Point(_cursorPosition.X, _cursorPosition.Y), 20, 20);
        foreach (var click in _clicks)
        {
            var progress = Math.Clamp((time - click.Time) / .5, 0, 1);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)((1 - progress) * 200),
                100, 210, 255)), 2), new Point(click.Point.X, click.Point.Y), 8 + progress * 24, 8 + progress * 24);
        }
    }

    private void RefreshAnimation()
    {
        var now = Now;
        // Keep one point preceding the cutoff so the disappearing tail can interpolate continuously.
        var removedLaser = 0;
        while (_laser.Count > 1 && now - _laser[1].Time >= 1.3)
        { _laser.RemoveAt(0); removedLaser++; }
        if (_laser.Count == 1 && now - _laser[0].Time >= 1.3) { _laser.Clear(); removedLaser++; }
        var effects = _state.Settings.IsEnabled && (_state.Settings.CursorHalo || _state.Settings.ClickAnimations);
        if (effects && PresentationSource.FromVisual(this) is not null)
        {
            var position = System.Windows.Forms.Cursor.Position;
            _cursorPosition = Point(PointFromScreen(new Point(position.X, position.Y)));
            var down = (ScreenInk.App.Interop.NativeMethods.GetAsyncKeyState(1) & 0x8000) != 0;
            if (_state.Settings.ClickAnimations && down && !_leftDown) _clicks.Add((_cursorPosition, now));
            _leftDown = down;
        }
        var removedClicks = _clicks.RemoveAll(click => now - click.Time >= .5);
        if (!effects) _clicks.Clear();
        var changed = Store.RemoveExpiredFadingStrokes(now) > 0 || removedLaser > 0 || _laser.Count > 0 ||
            effects || _effectsWereVisible || removedClicks > 0 || Store.Strokes.Any(stroke => stroke.IsActivelyFading(now));
        _effectsWereVisible = effects;
        if (changed) InvalidateVisual();
    }

    private void CollectErasures(InkPoint point)
    {
        for (var index = 0; index < Store.Strokes.Count; index++)
            if (Store.Strokes[index].Kind != StrokeKind.Board && StrokeVisual.Hit(Store.Strokes[index], point, 8, VisualTreeHelper.GetDpi(this).PixelsPerDip, false)) _pendingErase.Add(index);
    }

    private bool VisibleAt(InkStroke stroke, InkPoint point)
    {
        var board = Store.Strokes.FirstOrDefault(s => s.Id == stroke.ParentBoardId);
        return board is null || BoardInterior(board).Contains(point);
    }

    private int? HitStroke(InkPoint point)
    {
        for (var index = Store.Strokes.Count - 1; index >= 0; index--)
            if (VisibleAt(Store.Strokes[index], point) && StrokeVisual.Hit(Store.Strokes[index], point, 7, VisualTreeHelper.GetDpi(this).PixelsPerDip, true)) return index;
        return null;
    }

    private Rect SelectionBounds()
    {
        var result = Rect.Empty;
        foreach (var index in _selected.Where(i => i < Store.Strokes.Count))
            result.Union(StrokeVisual.Bounds(_selectionPreview.GetValueOrDefault(index, Store.Strokes[index]),
                VisualTreeHelper.GetDpi(this).PixelsPerDip));
        return result;
    }

    private static Point[] Corners(Rect bounds) =>
        [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];

    private void BeginSelection(InkPoint point)
    {
        _selected.RemoveWhere(i => i >= Store.Strokes.Count);
        _selectionBounds = SelectionBounds();
        _resizeCorner = _selectionBounds.IsEmpty ? -1 : Array.FindIndex(Corners(_selectionBounds),
            corner => InkPoint.Distance(new InkPoint(corner.X, corner.Y), point) <= 9);
        var hit = HitStroke(point);
        var extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (_resizeCorner < 0)
        {
            if (hit is { } index)
            {
                if (extend && _selected.Contains(index)) _selected.Remove(index);
                else { if (!extend && !_selected.Contains(index)) _selected.Clear(); _selected.Add(index); }
            }
            else
            {
                if (!extend) _selected.Clear();
                _marquee = new Rect(new Point(point.X, point.Y), new Size());
            }
        }
        _selectionBounds = SelectionBounds();
        _selectionOriginals = _selected.ToDictionary(i => i, i => Store.Strokes[i]);
        var boardIds = _selectionOriginals.Values.Where(s => s.Kind == StrokeKind.Board).Select(s => s.Id).ToHashSet();
        for (var i = 0; i < Store.Strokes.Count; i++)
            if (Store.Strokes[i].ParentBoardId is { } parent && boardIds.Contains(parent))
                _selectionOriginals[i] = Store.Strokes[i];
    }

    private void UpdateSelection(InkPoint point)
    {
        if (_marquee is not null)
        {
            _marquee = RectFrom(_start, point);
            _selected.Clear();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _selected.UnionWith(_selectionOriginals.Keys);
            for (var i = 0; i < Store.Strokes.Count; i++)
                if (_marquee.Value.Contains(StrokeVisual.Bounds(Store.Strokes[i], VisualTreeHelper.GetDpi(this).PixelsPerDip)))
                    _selected.Add(i);
            return;
        }
        if (_selectionBounds.IsEmpty) return;
        var target = _selectionBounds;
        if (_resizeCorner >= 0)
        {
            var anchor = Corners(_selectionBounds)[(_resizeCorner + 2) % 4];
            target = new Rect(anchor, new Point(point.X, point.Y));
            if (target.Width < 8 || target.Height < 8) return;
        }
        else target.Offset(point.X - _start.X, point.Y - _start.Y);
        var movingBoards = _selectionOriginals.Values.Where(s => s.Kind == StrokeKind.Board).Select(s => s.Id).ToHashSet();
        if (movingBoards.Count > 0 && (target.Width < 80 || target.Height < 60)) return;
        var parents = _selectionOriginals.Values.Where(s => s.ParentBoardId is not null && !movingBoards.Contains(s.ParentBoardId.Value))
            .Select(s => s.ParentBoardId!.Value).Distinct().ToArray();
        foreach (var parent in parents)
        {
            var board = Store.Strokes.FirstOrDefault(s => s.Id == parent);
            if (board is null) continue;
            var bounds = BoardInterior(board);
            if (target.Width > bounds.Width || target.Height > bounds.Height) return;
            target.X = Math.Clamp(target.X, bounds.Left, bounds.Right - target.Width);
            target.Y = Math.Clamp(target.Y, bounds.Top, bounds.Bottom - target.Height);
        }
        _selectionPreview = _selectionOriginals.ToDictionary(pair => pair.Key, pair => SelectionTransform.Apply(
            pair.Value, new InkRect(_selectionBounds.X, _selectionBounds.Y, _selectionBounds.Width, _selectionBounds.Height),
            new InkRect(target.X, target.Y, target.Width, target.Height)));
    }

    private void DrawSelection(DrawingContext dc)
    {
        var bounds = SelectionBounds();
        var border = new Pen(Brushes.DeepSkyBlue, 1.5);
        if (!bounds.IsEmpty)
        {
            dc.DrawRectangle(null, border, bounds);
            foreach (var point in Corners(bounds))
                dc.DrawRoundedRectangle(Brushes.White, border, new Rect(point.X - 4, point.Y - 4, 8, 8), 2, 2);
        }
        if (_marquee is { } marquee)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, 10, 132, 255)), border, marquee);
    }

    private void CancelGesture()
    {
        _gestureTool = null;
        _currentPoints = null;
        _pendingErase.Clear();
        _selectionPreview.Clear();
        _selectionOriginals.Clear();
        _marquee = null;
        _boardGesture = false;
        _pendingBoard = null;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_gestureTool is not null) CancelGesture();
        base.OnLostMouseCapture(e);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape) { CancelGesture(); RequestNormalMode?.Invoke(); }
        else if (control && e.Key == Key.Z) { CancelGesture(); Store.Undo(); _selected.Clear(); }
        else if (control && e.Key == Key.Y) { CancelGesture(); Store.Redo(); _selected.Clear(); }
        else if (control && e.Key == Key.A && _state.Settings.Tool == DrawingTool.Select)
            _selected.UnionWith(Enumerable.Range(0, Store.Strokes.Count));
        else if (e.Key is Key.Delete or Key.Back) { Store.Remove(_selected); _selected.Clear(); }
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
        InvalidateVisual();
    }

    private InkStroke CreateStroke(IReadOnlyList<InkPoint> points, StrokeKind kind)
    {
        var highlighter = _state.Settings.Tool == DrawingTool.Highlighter;
        return new InkStroke
        {
            Points = [.. points],
            ParentBoardId = _drawingBoard,
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

    public void Dispose()
    {
        _animationTimer.Stop();
        _state.Changed -= OnStateChanged;
        Store.Changed -= InvalidateVisual;
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
