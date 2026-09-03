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
    /// ordered by a third key from a generator seeded with this value, so the
    /// heap pops an equally good frontier the caller did not choose.
    /// <para>
    /// Null — the production default — makes the third key always zero, and the
    /// ordering identical to a heap without it.
    /// </para>
    /// </param>
    /// <remarks>
    /// A search's answer must not depend on which of several equal-priority
    /// entries pops first, and the only way to know that is to pop a different
    /// one.
    /// <para>
    /// A collision under one seed and not another is a REAL defect: every path is
    /// still optimal, and collision-freedom has to hold for every valid
    /// tie-break.
    /// </para>
    /// <para>
    /// Each seed is one fixed, replayable ordering, so a failing seed fails the
    /// same way forever.
    /// </para>
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
    /// Deliberately EXACT, and the one place in this codebase where a tolerance
    /// would be wrong.
    /// <para>
    /// An epsilon comparison is not transitive — a and b "equal", b and c
    /// "equal", a and c not — which corrupts a heap's invariant and silently
    /// returns out-of-order pops.
    /// </para>
    /// <para>
    /// Tolerances belong where accumulated costs are compared for a VERDICT, not
    /// where they are compared for ORDER.
    /// </para>
    /// <para>
    /// Tie-breaking toward lower <c>h</c> prefers the node nearer the goal among
    /// equally promising ones — on open terrain, the difference between probing
    /// a plateau of identical <c>f</c> and walking through it.
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
