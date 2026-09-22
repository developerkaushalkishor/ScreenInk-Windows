using ScreenInk.Core;

var tests = new (string Name, Action Run)[]
{
    ("undo redo and clear", TestHistory),
    ("fade cleanup", TestFadeCleanup),
    ("point sampling", TestSampling),
    ("shift constraints", TestConstraints),
    ("shape recognition", TestRecognition),
    ("responsive toolbar geometry", TestToolbarGeometry),
    ("edge reveal fires once per entry", TestToolbarReveal),
    ("history commands notify renderer", TestDocumentNotifications),
    ("selection transforms preserve board ownership", TestSelectionTransform)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"All {tests.Length} core tests passed.");

static InkStroke Stroke(double x, double? fade = null) => new()
{
    Points = [new InkPoint(x, 10)],
    CreatedAt = 10,
    FadeAfter = fade
};

static void TestHistory()
{
    var store = new StrokeStore();
    store.Append(Stroke(1));
    store.Append(Stroke(2));
    store.Clear();
    Equal(0, store.Strokes.Count);
    store.Undo();
    Equal(2, store.Strokes.Count);
    store.Redo();
    Equal(0, store.Strokes.Count);
}

static void TestFadeCleanup()
{
    var store = new StrokeStore();
    store.Append(Stroke(1));
    store.Append(Stroke(2, 2));
    Equal(1, store.RemoveExpiredFadingStrokes(20));
    Equal(1, store.Strokes.Count);
}

static void TestSampling()
{
    var points = new List<InkPoint> { new(0, 0) };
    InkGeometry.AppendSample(points, new InkPoint(.2, .2), 1);
    InkGeometry.AppendSample(points, new InkPoint(2, 0), 1);
    InkGeometry.AppendSample(points, new InkPoint(2.2, 0), 1, true);
    Equal(3, points.Count);
}

static void TestConstraints()
{
    var square = InkGeometry.ConstrainShape(new InkPoint(0, 0), new InkPoint(30, 10), true);
    Equal(30d, square.X);
    Equal(30d, square.Y);
    var line = InkGeometry.SnapLine(new InkPoint(0, 0), new InkPoint(40, 5), true);
    Assert(Math.Abs(line.Y) < .001, "Line should snap horizontally.");
}

static void TestRecognition()
{
    var circle = Enumerable.Range(0, 49).Select(index =>
    {
        var angle = index / 48d * Math.PI * 2;
        return new InkPoint(100 + Math.Cos(angle) * 50, 80 + Math.Sin(angle) * 35);
    }).ToArray();
    Assert(ShapeRecognizer.Recognize(circle)?.Kind == StrokeKind.Ellipse,
        "Closed circular stroke should be recognized as an ellipse.");
}

static void TestToolbarGeometry()
{
    var display = new ToolbarFrame(-1280, 0, 640, 800);
    var frame = ToolbarGeometry.TopCenter(display, 900, 54);
    Equal(616d, frame.Width);
    Equal(-1268d, frame.Left);
    Equal(12d, frame.Top);
    Assert(!ToolbarGeometry.ShowQuickColors(899), "Quick colors should collapse on narrow displays.");
    Assert(ToolbarGeometry.ShowQuickColors(900), "Quick colors should appear when space is available.");
}

static void TestToolbarReveal()
{
    var tracker = new ToolbarRevealTracker();
    Assert(tracker.Enter(true, "A"), "First edge entry should reveal.");
    Assert(!tracker.Enter(true, "A"), "Polling inside the same edge must not reveal repeatedly.");
    Assert(tracker.Enter(true, "B"), "Entering another display edge should relocate once.");
    Assert(!tracker.Enter(false, null), "Leaving the edge only rearms reveal.");
    Assert(tracker.Enter(true, "A"), "Re-entering after leaving should reveal again.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void TestDocumentNotifications()
{
    var store = new StrokeStore();
    var changes = 0;
    store.Changed += () => changes++;
    store.Append(Stroke(1));
    store.Replace(new Dictionary<int, InkStroke> { [0] = Stroke(2) });
    store.Remove([0]); store.Undo(); store.Redo(); store.Undo(); store.Clear();
    Equal(7, changes);
    store.Clear();
    Equal(7, changes);
}

static void TestSelectionTransform()
{
    var board = Guid.NewGuid();
    var stroke = Stroke(10) with { Kind = StrokeKind.Text, Text = "Test", FontSize = 20,
        TextWidth = 100, ParentBoardId = board };
    var transformed = SelectionTransform.Apply(stroke, new InkRect(0, 0, 100, 100), new InkRect(20, 30, 200, 200));
    Equal(new InkPoint(40, 50), transformed.Points[0]);
    Equal(40d, transformed.FontSize);
    Equal(200d, transformed.TextWidth);
    Assert(transformed.ParentBoardId == board && transformed.Id == stroke.Id, "Transforms must preserve ownership and identity.");
    var store = new StrokeStore(); store.Append(stroke);
    store.Replace(new Dictionary<int, InkStroke> { [0] = transformed });
    store.Undo(); Equal(stroke, store.Strokes[0]);
    store.Redo(); Equal(transformed, store.Strokes[0]);
}
