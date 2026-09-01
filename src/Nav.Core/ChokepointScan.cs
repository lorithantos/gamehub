namespace Nav.Core;

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
