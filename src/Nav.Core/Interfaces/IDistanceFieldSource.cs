using System.Diagnostics.CodeAnalysis;

namespace Nav.Core.Interfaces;

/// <summary>
/// Where a <see cref="DistanceField"/> comes from when somebody asks for one.
/// </summary>
/// <remarks>
/// One verb for everything that MOVES: a caller names a destination and gets the
/// field for it. How the field arrived -- built just now, built at load, handed
/// over from a shared pool -- is the source's business and nobody else's.
/// <para>
/// <see cref="TryPeek"/> is not a second way to get a field. It is the way to
/// LOOK at the source without becoming part of what it decides, which only an
/// instrument ever wants.
/// </para>
/// <para>
/// <b>The economy it protects is that K fields serve N units.</b> A field is
/// O(cells) and live destinations are few but unbounded, so something has to
/// decide what is worth keeping.
/// </para>
/// <para>
/// A contract rather than a class means that decision can change without the
/// movement system knowing — an LRU cache today, precomputed rally points
/// tomorrow, or one source shared across systems.
/// </para>
/// <para>
/// It is also where a COUNTING decorator hangs. Capacity is a guess, and a
/// source recording how often it is asked for what it dropped is how a guess
/// becomes a measurement.
/// </para>
/// <para>
/// <b>Determinism is part of the contract.</b> Equal destinations must give
/// equal fields regardless of what was asked before, and any eviction must be a
/// deterministic function of the call sequence alone.
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

    /// <summary>
    /// The field for this destination IF the source already holds one: no build,
    /// and no mark of use. False means nothing is held, and the caller is given
    /// no field rather than an empty one.
    /// </summary>
    /// <remarks>
    /// <b>FOR INSTRUMENTS, and for nothing else.</b> A source keeping K fields
    /// decides what to drop from the order it was asked in, so a panel reading a
    /// field through <see cref="For"/> becomes part of that decision: the field it
    /// looked at survives, a colder one is dropped in its place, and the drop is
    /// paid for later as a rebuild. The node and field counts this project decides
    /// things on would then be a function of whether anybody was watching, and an
    /// instrument that moves the number it reports is measuring itself.
    /// <para>
    /// It is not a fast path and not an optimisation. A caller that NEEDS the
    /// field wants it built and wants the ask to count, which is <see cref="For"/>.
    /// </para>
    /// <para>
    /// A decorator forwards this to what it wraps. Answering false because the
    /// wrapper keeps nothing of its own would report "not held" about a field that
    /// is held -- a lie in the one direction an instrument cannot detect.
    /// </para>
    /// </remarks>
    /// <param name="destination">The cell the wanted field is keyed by.</param>
    /// <param name="field">The held field, or null.</param>
    bool TryPeek(int destination, [NotNullWhen(true)] out DistanceField? field);
}
