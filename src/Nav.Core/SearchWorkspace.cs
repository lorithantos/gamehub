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

    /// <summary>
    /// A workspace pre-sized for <paramref name="cellCount"/> cells. Zero is a
    /// legitimate size: every search grows the buffers to what it needs before
    /// touching them, so a size given here only saves the first search its one grow.
    /// </summary>
    /// <param name="cellCount">
    /// Cells to make room for, not negative. A space-time search wants
    /// <c>horizon * cells</c> rather than <c>cells</c>, because its state is a
    /// cell and a tick.
    /// </param>
    /// <param name="tieBreakSeed">
    /// Seeds the frontier's third ordering key, so exact <c>(f, h)</c> ties pop
    /// in a different but fixed order. Null, the default, is the production
    /// ordering. See <see cref="BinaryHeap"/> for why this exists.
    /// </param>
    public SearchWorkspace(int cellCount = 0, int? tieBreakSeed = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cellCount);

        Cost = new double[cellCount];
        Parent = new int[cellCount];
        State = new byte[cellCount];
        Stamp = new int[cellCount];
        Frontier = new BinaryHeap(tieBreakSeed: tieBreakSeed);
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
    /// The reset is the whole point: clearing three grid-sized arrays costs the
    /// same on a search that expands three nodes as on one that expands forty
    /// thousand. Bumping a counter instead makes a cell's staleness a comparison
    /// rather than a write.
    /// <para>
    /// HOW MUCH THIS IS WORTH DEPENDS ENTIRELY ON PATH LENGTH, and an aggregate
    /// over a benchmark corpus hides it: long cross-map problems clear few cells
    /// per expanded node and short ones clear tens of thousands. Restricted to
    /// short paths, which is what most movement in a game actually is, it is
    /// worth about 4.9x. The banded measurement is in
    /// <c>docs/search-and-movement.md</c>.
    /// </para>
    /// <para>
    /// The cost is four bytes a cell held permanently, and one wrap-around case:
    /// after two billion searches on one workspace the counter is rolled and the
    /// stamps cleared once, which is the only O(cells) reset that remains.
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
