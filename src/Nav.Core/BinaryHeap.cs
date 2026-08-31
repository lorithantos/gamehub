namespace Nav.Core;

/// <summary>
/// A binary min-heap of search frontier entries, ordered by <c>f</c> and then by
/// <c>h</c>.
/// </summary>
/// <remarks>
/// Specialised rather than general: the entries are structs holding a cell index
/// and its two scores, so nothing is boxed, nothing is compared through an
/// interface, and the whole frontier is one flat array.
/// <para>
/// There is no decrease-key. A* pushes a duplicate entry whenever it improves a
/// cell and the search skips any popped cell it has already closed, which trades
/// a slightly larger heap for not having to track where each cell lives inside
/// it. That is the standard trade and it is the cheaper one here.
/// </para>
/// </remarks>
public sealed class BinaryHeap
{
    private readonly record struct Entry(int Cell, double F, double H);

    private Entry[] _entries;
    private int _count;

    public BinaryHeap(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _entries = new Entry[capacity];
    }

    public int Count => _count;

    public void Clear() => _count = 0;

    public void Push(int cell, double f, double h)
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        var index = _count++;
        _entries[index] = new Entry(cell, f, h);
        SiftUp(index);
    }

    /// <summary>Removes and returns the cell with the lowest <c>f</c>, breaking ties on lower <c>h</c>.</summary>
    public int Pop()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("The heap is empty.");
        }

        var cell = _entries[0].Cell;
        _count--;
        if (_count > 0)
        {
            _entries[0] = _entries[_count];
            SiftDown(0);
        }

        return cell;
    }

    /// <summary>
    /// Strict ordering: lower <c>f</c> wins, and equal <c>f</c> is settled by
    /// lower <c>h</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately an exact comparison, and the one place in this codebase where
    /// a tolerance would be wrong. A comparison with an epsilon is not
    /// transitive -- a and b can be "equal", b and c "equal", and a and c not --
    /// which is enough to corrupt a heap's invariant and silently return
    /// out-of-order pops. Tolerances belong where accumulated costs are compared
    /// for a verdict, not where they are compared for order.
    /// <para>
    /// Tie-breaking toward lower <c>h</c> means that among equally promising
    /// nodes the search prefers the one nearer the goal. On open terrain that is
    /// the difference between probing a plateau of identical <c>f</c> and walking
    /// through it, and it costs one comparison.
    /// </para>
    /// </remarks>
    private static bool IsBetter(in Entry candidate, in Entry incumbent) =>
        candidate.F < incumbent.F || (candidate.F == incumbent.F && candidate.H < incumbent.H);

    private void SiftUp(int index)
    {
        var entry = _entries[index];
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (!IsBetter(entry, _entries[parent]))
            {
                break;
            }

            _entries[index] = _entries[parent];
            index = parent;
        }

        _entries[index] = entry;
    }

    private void SiftDown(int index)
    {
        var entry = _entries[index];
        while (true)
        {
            var child = (2 * index) + 1;
            if (child >= _count)
            {
                break;
            }

            if (child + 1 < _count && IsBetter(_entries[child + 1], _entries[child]))
            {
                child++;
            }

            if (!IsBetter(_entries[child], entry))
            {
                break;
            }

            _entries[index] = _entries[child];
            index = child;
        }

        _entries[index] = entry;
    }
}
