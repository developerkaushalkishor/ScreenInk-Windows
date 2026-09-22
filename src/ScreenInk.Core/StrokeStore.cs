namespace ScreenInk.Core;

public sealed class StrokeStore
{
    private const int HistoryLimit = 100;
    private List<InkStroke> _strokes = [];
    private readonly List<List<InkStroke>> _undo = [];
    private readonly List<List<InkStroke>> _redo = [];

    public event Action? Changed;

    public IReadOnlyList<InkStroke> Strokes => _strokes;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Append(InkStroke stroke)
    {
        if (stroke.Points.Count == 0) return;
        Checkpoint();
        _strokes.Add(stroke);
        Changed?.Invoke();
    }

    public void Replace(IReadOnlyDictionary<int, InkStroke> replacements)
    {
        var valid = replacements.Where(pair => pair.Key >= 0 && pair.Key < _strokes.Count &&
            pair.Value.Points.Count > 0).ToArray();
        if (valid.Length == 0) return;
        Checkpoint();
        foreach (var pair in valid) _strokes[pair.Key] = pair.Value;
        Changed?.Invoke();
    }

    public void Remove(IEnumerable<int> indices)
    {
        var valid = indices.Where(index => index >= 0 && index < _strokes.Count)
            .Distinct().OrderDescending().ToArray();
        if (valid.Length == 0) return;
        Checkpoint();
        foreach (var index in valid) _strokes.RemoveAt(index);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_strokes.Count == 0) return;
        Checkpoint();
        _strokes.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add([.. _strokes]);
        _strokes = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add([.. _strokes]);
        _strokes = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke();
    }

    public int RemoveExpiredFadingStrokes(double time)
    {
        var before = _strokes.Count;
        _strokes.RemoveAll(stroke => stroke.FadeAfter is not null && stroke.VisibleOpacity(time) <= 0);
        PruneHistory(_undo, time);
        PruneHistory(_redo, time);
        if (before != _strokes.Count) Changed?.Invoke();
        return before - _strokes.Count;
    }

    private static void PruneHistory(List<List<InkStroke>> history, double time)
    {
        foreach (var snapshot in history)
            snapshot.RemoveAll(stroke => stroke.FadeAfter is not null && stroke.VisibleOpacity(time) <= 0);
    }

    private void Checkpoint()
    {
        _undo.Add([.. _strokes]);
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
    }
}
