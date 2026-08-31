namespace Nav.Core;

/// <summary>
/// Scratch space for one search at a time, owned by the caller.
/// </summary>
/// <remarks>
/// A* needs three arrays sized to the grid plus a heap. Allocating them per call
/// is simple and, on a large map, dominates: the runtime must hand back zeroed
/// memory, so the clearing is paid whether the arrays are fresh or reused, and
/// the allocation adds a Large Object Heap collection on top for nothing. A grid
/// over about 6,500 cells puts every one of these arrays on the LOH.
/// <para>
/// <b>Explicit and caller-owned, deliberately.</b> The obvious alternatives both
/// cost more than they look. A <c>[ThreadStatic]</c> workspace leaks one buffer
/// per pool thread, sized to the largest grid ever seen and held for the life of
/// the process. A rent-and-return pool makes correctness depend on a paired
/// acquire and release. Passing the workspace in keeps the search a function of
/// its arguments, which is what lets many searches run at once: one workspace per
/// worker, no synchronisation, and <see cref="Grid"/> stays immutable and shared.
/// </para>
/// <para>
/// Not thread-safe, and that is the point -- one workspace belongs to one search
/// at a time. Sharing one across threads is the mistake this type makes visible
/// rather than the one it prevents.
/// </para>
/// </remarks>
public sealed class SearchWorkspace
{
    internal double[] Cost;
    internal int[] Parent;
    internal byte[] State;
    internal int[] Stamp;
    internal BinaryHeap Frontier;

    /// <summary>
    /// Which search the stamps refer to. Starts at zero so that the first search,
    /// running as generation one, finds no cell live in a freshly zeroed
    /// <see cref="Stamp"/>.
    /// </summary>
    internal int Generation;

    public SearchWorkspace(int cellCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellCount);

        Cost = new double[cellCount];
        Parent = new int[cellCount];
        State = new byte[cellCount];
        Stamp = new int[cellCount];
        Frontier = new BinaryHeap();
    }

    /// <summary>Cells this workspace can currently serve without regrowing.</summary>
    public int Capacity => Cost.Length;

    /// <summary>
    /// Grows to fit <paramref name="cellCount"/>, keeping whatever is already
    /// large enough.
    /// </summary>
    /// <remarks>
    /// A workspace reused across differently sized maps settles at the largest,
    /// which is the intended behaviour: the buffers are scratch, not state.
    /// </remarks>
    internal void EnsureCapacity(int cellCount)
    {
        if (Cost.Length >= cellCount)
        {
            return;
        }

        Cost = new double[cellCount];
        Parent = new int[cellCount];
        State = new byte[cellCount];

        // Zeroed, so nothing in it matches the current generation and every cell
        // correctly reads as untouched. Generation is deliberately NOT reset.
        Stamp = new int[cellCount];
    }

    /// <summary>
    /// Begins a search. Every cell becomes untouched again in constant time.
    /// </summary>
    /// <remarks>
    /// The reset is the whole point. Clearing three grid-sized arrays costs the
    /// same on a search that expands forty nodes as on one that expands forty
    /// thousand, and across the benchmark corpus it works out at 23 cells
    /// initialised for every node actually expanded. Bumping a counter instead
    /// makes a cell's staleness a comparison rather than a write.
    /// <para>
    /// The cost is four bytes a cell held permanently, and one wrap-around case
    /// -- after two billion searches on one workspace the counter is rolled and
    /// the stamps are cleared once, which is the only O(cells) reset that
    /// remains.
    /// </para>
    /// </remarks>
    internal void NextGeneration()
    {
        if (Generation == int.MaxValue)
        {
            Array.Clear(Stamp);
            Generation = 0;
        }

        Generation++;
        Frontier.Clear();
    }

    /// <summary>True if <paramref name="cell"/> has been touched by the current search.</summary>
    internal bool IsLive(int cell) => Stamp[cell] == Generation;
}
