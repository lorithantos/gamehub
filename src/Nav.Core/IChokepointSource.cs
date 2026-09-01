namespace Nav.Core;

/// <summary>
/// Where the gates on a map come from.
/// </summary>
/// <remarks>
/// Chokepoints are <b>annotations, not structure</b>: nothing routes over them
/// and no hierarchy exists. The group layer reads them for metering; the search
/// never sees them. That is exactly what makes them substitutable -- getting them
/// wrong changes pacing and cannot break a plan.
/// <para>
/// Detection is a guess, and a good one, but a guess: betweenness sampling
/// crossed with a width veto over a strided set of terminals. A map author knows
/// where the gates are without sampling anything, and on a map shipped with the
/// game the answer is the same every run and worth computing once, at build time,
/// rather than at load. Both are implementations of this.
/// </para>
/// <para>
/// The third implementation is the interesting one: a source that returns nothing
/// turns metering off <em>structurally</em>. <c>MeteredGatherDoctrine</c> finds no
/// gate and returns, so a caller can disable pacing without swapping doctrines or
/// adding a flag to one.
/// </para>
/// <para>
/// <b>Determinism binds here too.</b> Two calls for one map must agree exactly,
/// because metering decisions hang off this and replay determinism hangs off
/// those.
/// </para>
/// </remarks>
public interface IChokepointSource
{
    /// <summary>
    /// The gates on this map, in ascending cell order. Empty is the honest answer
    /// for open ground, and the common one.
    /// </summary>
    /// <param name="grid">The map to answer for.</param>
    IReadOnlyList<Chokepoint> For(Grid grid);
}

/// <summary>
/// The default: find the gates by looking at the map.
/// </summary>
/// <remarks>
/// A thin object over <see cref="ChokepointMap.Find"/>, and it earns its keep by
/// making the sampling density a value a caller can hold rather than a default
/// buried in a call. Detection costs grow with the SQUARE of the terminal count,
/// so a large map and a small one do not want the same number.
/// </remarks>
/// <param name="terminals">
/// How many sampling points to stride through the passable cells. Two at minimum;
/// 24 is the default the corpus was tuned against.
/// </param>
public sealed class ChokepointScan(int terminals = 24) : IChokepointSource
{
    /// <inheritdoc/>
    public IReadOnlyList<Chokepoint> For(Grid grid) => ChokepointMap.Find(grid, terminals);
}

/// <summary>
/// No gates, ever -- which switches metering off by giving it nothing to meter.
/// </summary>
/// <remarks>
/// Useful for open-ground maps where the scan is wasted work, and for isolating
/// whether a movement problem is pacing or planning: run the same scenario with
/// this and the difference is exactly what metering was contributing.
/// </remarks>
public sealed class NoChokepoints : IChokepointSource
{
    /// <inheritdoc/>
    public IReadOnlyList<Chokepoint> For(Grid grid) => [];
}
