namespace Nav.Core;

/// <summary>
/// The search frontier, ordered by <c>f</c> and then by <c>h</c> -- now a thin
/// wrapper over <see cref="PriorityQueue{TElement, TPriority}"/>.
/// </summary>
/// <remarks>
/// SPIKE. The hand-rolled heap this replaces was written before the runtime had
/// a priority queue worth using; the question is whether the framework's is at
/// least as fast on this workload and pops in the same order. The surface --
/// <see cref="Push"/>, <see cref="Pop"/>, <see cref="Count"/>, <see cref="Clear"/>
/// -- is kept exactly, so the two search loops and the workspace reset do not
/// change and the benchmark measures one variable.
/// <para>
/// <b>The discriminator is the tuple.</b> The priority is <c>(F, H)</c>, and
/// <see cref="ValueTuple{T1, T2}"/> compares lexicographically: <c>F</c> first,
/// <c>H</c> only to settle a tie. That is precisely the ordering the old
/// <c>IsBetter</c> implemented, and no comparer is passed on purpose -- with a
/// value-type priority and no comparer, <see cref="PriorityQueue{TElement, TPriority}"/>
/// takes a specialised path that avoids the interface call per comparison.
/// </para>
/// <para>
/// <b>What the framework's "lazy" operations do NOT buy here.</b>
/// <c>EnqueueDequeue</c> and <c>DequeueEnqueue</c> skip a sift when a push is
/// immediately followed by a pop, or a pop by a push. A* has neither shape: it
/// pops one node and pushes several, so every call is a plain enqueue or a plain
/// dequeue. The laziness that DOES apply is lazy DELETION -- there is still no
/// decrease-key, so an improved cell is pushed again and a stale pop is skipped by
/// the search, exactly as before.
/// </para>
/// <para>
/// <see cref="Clear"/> stays O(1). The node type holds no references, so the
/// framework resets the count without touching the array.
/// </para>
/// </remarks>
internal sealed class BinaryHeap
{
    private readonly PriorityQueue<int, (double F, double H)> _queue;

    /// <summary>
    /// An empty heap. <paramref name="capacity"/> is a starting size only -- the
    /// queue grows on demand -- so it buys nothing but the copies a growing
    /// frontier would otherwise pay for.
    /// </summary>
    /// <param name="capacity">Initial room, in entries. At least one.</param>
    public BinaryHeap(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _queue = new PriorityQueue<int, (double F, double H)>(capacity);
    }

    /// <summary>
    /// Entries still to be popped, which is not the number of distinct cells:
    /// with no decrease-key, one cell can sit in here several times over.
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Forgets every entry and keeps the capacity, so a heap reused across
    /// searches settles at the largest frontier it has met and stops regrowing.
    /// </summary>
    public void Clear() => _queue.Clear();

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
    public void Push(int cell, double f, double h) => _queue.Enqueue(cell, (f, h));

    /// <summary>Removes and returns the cell with the lowest <c>f</c>, breaking ties on lower <c>h</c>.</summary>
    public int Pop()
    {
        if (!_queue.TryDequeue(out var cell, out _))
        {
            throw new InvalidOperationException("The heap is empty.");
        }

        return cell;
    }
}
