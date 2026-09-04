using Nav.Core;

namespace Nav.Viewer.Tactics;

/// <summary>
/// A tactics world's fog as the viewer can hold it: cells, sides and three ints
/// per memory, with no sighting and no world on the way out.
/// </summary>
/// <remarks>
/// <b>The other half of the same seam <see cref="DemoWorldDebugView"/> crosses,
/// for the half of the instrument that DRAWS rather than describes.</b> A world
/// goes in and <see cref="IVisibilityView"/> comes out; the viewer above never
/// learns there was a <c>Sighting</c> involved, because Nav.Viewer.Shared has no
/// reference it could learn through. This project is the only one that can hold
/// both, which is why the translation happens here.
/// <para>
/// <b>Named for what it wraps, where its interface is named for what it
/// promises.</b> Same as the debug view, and the same reason: a caller picking
/// this out of a composition is the only thing that has to know a
/// <see cref="DemoWorld"/> is behind it.
/// </para>
/// <para>
/// <b>Everything it reports is as of the last clock edge</b>, read through
/// <see cref="IPerceptionView"/> -- the type an instrument is MEANT to hold,
/// with no verb on it. Nothing here resolves, refreshes or provokes an edge. The
/// one thing read off the world directly is <see cref="DemoWorld.Sides"/>, which
/// is a fact about who is fighting rather than about what anybody perceives.
/// </para>
/// <para>
/// A fresh list per call, because that is what the peeks hand back. The viewer
/// compares what it is given against what it last drew, so a new list costs a
/// comparison and never a redraw.
/// </para>
/// </remarks>
public sealed class DemoWorldVisibility : IVisibilityView
{
    private readonly DemoWorld _world;

    /// <param name="world">The world to read. Never written to.</param>
    public DemoWorldVisibility(DemoWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<int> Sides => _world.Sides;

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<int> VisibleCells(int side) => _world.View.PeekVisibleCells(side);

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<int> RepairPoints(int side) => _world.View.PeekRepairPoints(side);

    /// <inheritdoc/>
    /// <remarks>
    /// The one place a <c>Sighting</c> is spent. Three ints go up; the type does
    /// not.
    /// </remarks>
    [Observes]
    public IReadOnlyList<RememberedUnit> Remembered(int side)
    {
        var known = _world.View.PeekSightings(side);
        var remembered = new RememberedUnit[known.Count];
        for (var i = 0; i < known.Count; i++)
        {
            remembered[i] = new RememberedUnit(known[i].Agent, known[i].Cell, known[i].Tick);
        }

        return remembered;
    }
}
