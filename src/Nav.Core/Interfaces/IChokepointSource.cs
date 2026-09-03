namespace Nav.Core.Interfaces;

/// <summary>
/// Where the gates on a map come from.
/// </summary>
/// <remarks>
/// Chokepoints are <b>annotations, not structure</b>. The group layer reads them
/// for metering; the search never sees them.
/// <para>
/// That is what makes them substitutable: getting them wrong changes pacing and
/// cannot break a plan.
/// </para>
/// <para>
/// Detection is a guess, and a good one. A map author knows where the gates are
/// without sampling anything, and on a shipped map the answer is worth computing
/// once at build time. Both are implementations of this.
/// </para>
/// <para>
/// The third is the interesting one: a source that returns NOTHING turns
/// metering off structurally, so a caller can disable pacing without swapping
/// doctrines or adding a flag.
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
