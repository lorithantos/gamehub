namespace Nav.Core;

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
