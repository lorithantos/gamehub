namespace Nav.Core;

/// <summary>
/// Where a <see cref="DistanceField"/> comes from when somebody asks for one.
/// </summary>
/// <remarks>
/// One verb, because that is the whole relationship: a caller names a destination
/// and gets the field for it. How the field arrived -- built just now, built at
/// load, handed over from a shared pool -- is the source's business and nobody
/// else's.
/// <para>
/// <b>The economy this protects is that K fields serve N units.</b> A field is
/// O(cells) of memory and a match's set of live destinations is small but
/// unbounded, so something has to decide what is worth keeping. Making that a
/// contract rather than a class means the decision can change without the
/// movement system knowing: an LRU cache today (<see cref="FieldCache"/>), fields
/// precomputed at load for known rally points, or one source shared by several
/// systems over the same map.
/// </para>
/// <para>
/// It is also the natural place to hang a decorator that COUNTS. The capacity is
/// currently a guess at what a match needs, and a source that wraps another and
/// records how often it is asked for something it no longer holds is how that
/// guess becomes a measurement.
/// </para>
/// <para>
/// <b>Determinism is part of the contract, not an implementation detail.</b>
/// Replay must not depend on what happens to be cached, so a source must return
/// equal fields for equal destinations regardless of what has been asked before,
/// and any eviction it performs must be a deterministic function of the call
/// sequence alone.
/// </para>
/// </remarks>
public interface IDistanceFieldSource
{
    /// <summary>
    /// Fields held right now. A memory reading -- one <c>double</c> per cell
    /// apiece -- and not a progress one. Zero is legitimate for a source that
    /// builds on demand and keeps nothing.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// The field for this destination. Callers may be handed the SAME instance,
    /// so a field is to be read and never held past the call that wanted it.
    /// </summary>
    /// <param name="destination">The cell every cost in the field is measured to.</param>
    DistanceField For(int destination);
}
