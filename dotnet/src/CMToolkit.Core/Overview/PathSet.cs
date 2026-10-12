using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Overview;

/// <summary>
/// A Python <c>set[Path]</c>: one entry per path regardless of case (T-8), the first spelling added kept. Python
/// iterates a set in hash order, which nothing can reproduce, so this keeps insertion order and the parity goldens
/// compare these sets, and what is done in their order, as unordered (T-15).
/// </summary>
internal sealed class PathSet
{
    private readonly HashSet<PythonPath> _seen = new(PythonPath.Equality);
    private readonly List<PythonPath> _items = [];

    /// <summary>The paths, in the order they were first added.</summary>
    public IReadOnlyList<PythonPath> Items => _items;

    /// <summary><c>set.add(path)</c>: adds it unless an equal path is already there.</summary>
    public void Add(PythonPath path)
    {
        if (_seen.Add(path))
        {
            _items.Add(path);
        }
    }

    /// <summary>A copy of the paths, for a report.</summary>
    public IReadOnlyList<PythonPath> Snapshot() => [.. _items];
}
