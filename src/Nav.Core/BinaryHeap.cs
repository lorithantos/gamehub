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
internal sealed class BinaryHeap
{
    private readonly record struct Entry(int Cell, double F, double H, int R);

    private Entry[] _entries;
    private int _count;
    private readonly Random? _tieBreak;

    /// <summary>
    /// An empty heap. <paramref name="capacity"/> is a starting size only -- the
    /// backing array doubles on demand -- so it buys nothing but the copies a
    /// growing frontier would otherwise pay for.
    /// </summary>
    /// <param name="capacity">Initial room, in entries. At least one.</param>
    /// <param name="tieBreakSeed">
    /// When given, entries that tie EXACTLY on both <c>f</c> and <c>h</c> are
    /// ordered by a third key drawn from a generator seeded with this value, so
    /// the heap pops one of the equally good frontiers the caller did not choose.
    /// When null -- the production default -- the third key is always zero and
    /// the ordering is identical to a heap without it.
    /// </param>
    /// <remarks>
    /// The seed exists because a search's answer must not depend on which of
    /// several equal-priority entries happens to pop first, and the only way to
    /// know that is to pop a different one. A collision that appears under one
    /// seed and not another is a real defect: every path is still optimal, and
    /// collision-freedom has to hold for every valid tie-break. Each seed is one
    /// fixed, replayable ordering -- the draws happen in push order, which is
    /// itself deterministic -- so a failing seed fails the same way forever.
    /// </remarks>
    public BinaryHeap(int capacity = 128, int? tieBreakSeed = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _entries = new Entry[capacity];
        _tieBreak = tieBreakSeed is { } seed ? new Random(seed) : null;
    }

    /// <summary>
    /// Entries still to be popped, which is not the number of distinct cells:
    /// with no decrease-key, one cell can sit in here several times over.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Forgets every entry and keeps the capacity, so a heap reused across
    /// searches settles at the largest frontier it has met and stops regrowing.
    /// </summary>
    public void Clear() => _count = 0;

    /// <summary>
    /// Adds an entry. Ordering reads <paramref name="f"/> first and consults
    /// <paramref name="h"/> only to settle a tie; nothing here checks that the
    /// two are consistent with each other.
    /// </summary>
    /// <param name="cell">
    /// Whatever the caller uses as state identity -- a cell index for a plain
    /// search, a cell-and-tick state for a space-time one. The heap only carries it.
    /// </param>
    /// <param name="f">The priority. Lowest is popped first.</param>
    /// <param name="h">The tie-break: among equal <paramref name="f"/>, lower wins.</param>
    public void Push(int cell, double f, double h)
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        var index = _count++;
        _entries[index] = new Entry(cell, f, h, _tieBreak?.Next() ?? 0);
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
    /// <para>
    /// The third key is consulted only when both <c>f</c> and <c>h</c> are exactly
    /// equal, and in production it is always zero, so this is the same two
    /// comparisons as before on every path that mattered before.
    /// </para>
    /// </remarks>
    private static bool IsBetter(in Entry candidate, in Entry incumbent) =>
        candidate.F < incumbent.F ||
        (candidate.F == incumbent.F &&
            (candidate.H < incumbent.H || (candidate.H == incumbent.H && candidate.R < incumbent.R)));

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
