namespace Nav.Core.Interfaces;

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
