namespace Nav.Core.Interfaces;

/// <summary>
/// Where a <see cref="DistanceField"/> comes from when somebody asks for one.
/// </summary>
/// <remarks>
/// One verb for everything that MOVES: a caller names a destination and gets the
/// field for it. How the field arrived -- built just now, built at load, handed
/// over from a shared pool -- is the source's business and nobody else's.
/// <para>
/// Looking without asking is a different contract and lives on a different
/// reference: see <see cref="View"/>.
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
    /// This same source as the reference an INSTRUMENT is handed: it can ask what
    /// is already held and it cannot build, because <see cref="For"/> is not on it.
    /// </summary>
    /// <remarks>
    /// The reason a panel gets a different type rather than a rule about which
    /// member to call is on <see cref="IDistanceFieldView"/>, and it is the reason
    /// <see cref="IReservationView"/> exists.
    /// <para>
    /// A decorator hands back a view of what it WRAPS -- its own, forwarding, or
    /// the inner one. A view of the wrapper's empty pockets would report "not
    /// held" about a field that is held.
    /// </para>
    /// </remarks>
    IDistanceFieldView View { get; }
}
