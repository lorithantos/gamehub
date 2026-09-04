using Nav.Core;

namespace Nav.Viewer.Interfaces;

/// <summary>
/// What ONE SIDE can see and what it remembers, in cells and ints: the board as
/// somebody in the fight has it rather than as it is.
/// </summary>
/// <remarks>
/// <b>Named for the contract and not for whoever satisfies it</b>, exactly as
/// <see cref="IWorldDebugView"/> is. This project references Nav.Core alone, so
/// nothing here can name a world, a kit or a sighting -- and a name that pointed
/// at one anyway would claim a relationship the compiler has been set up to
/// forbid. What the viewer holds is this; what implements it is a host's
/// business.
/// <para>
/// <b>The viewer draws the true board, and that is the fault this closes.</b>
/// Each side acts on limited knowledge; with nothing but the truth on screen a
/// doctrine acting on knowledge it should not have looks identical to one that
/// earned it. Handing the app one of these lets it redraw the same run through
/// one side's eyes without learning what fog is made of.
/// </para>
/// <para>
/// <b>Everything here is in CELLS -- flat <c>y * Width + x</c> indices into the
/// grid the viewer is already drawing -- and SIDES are plain ints.</b> Neither
/// needs a type from anywhere else, which is what lets this interface sit on
/// the Nav.Core side of the seam at all.
/// </para>
/// <para>
/// <b>It observes and must cause nothing.</b> Every member is
/// <see cref="ObservesAttribute"/>, on the interface rather than on an
/// implementation, so the reachable-mutation walk marks every implementer and
/// every decorator with it. An implementation that resolved a side's perception
/// on the way past would move the run on exactly the frames somebody was
/// watching it.
/// </para>
/// <para>
/// <b>Answers are as of the last clock edge and are free to be rebuilt per
/// call.</b> The app compares what comes back against what it drew last time,
/// so an implementation may hand back a fresh list every frame -- it must not
/// hand back a cached one it later writes to.
/// </para>
/// </remarks>
public interface IVisibilityView
{
    /// <summary>
    /// The sides whose eyes can be looked through, ascending, without repeats.
    /// Empty for a board nobody is fighting over.
    /// </summary>
    /// <remarks>
    /// The side NUMBERS rather than a count, because a side is an int and
    /// nothing promises they run 0, 1, 2 with no gaps. The viewer cycles
    /// through this in order and needs no opinion about how many there are.
    /// </remarks>
    [Observes]
    IReadOnlyList<int> Sides { get; }

    /// <summary>
    /// Every cell <paramref name="side"/> can currently see, ascending. Empty
    /// for a side that has never looked.
    /// </summary>
    /// <remarks>
    /// GROUND, not contacts: this is the ring of map the side's units and its
    /// own installations are lighting up, whether or not there is anything
    /// standing in it. A side always sees the cell each of its own units stands
    /// on, so its own roster is never fogged out from under it.
    /// <para>
    /// Ascending and without repeats, so two answers can be compared element by
    /// element -- which is how the viewer decides whether the fog it drew last
    /// frame is still the right picture.
    /// </para>
    /// </remarks>
    /// <param name="side">Whose eyes. Any int; one nobody fights for answers empty.</param>
    [Observes]
    IReadOnlyList<int> VisibleCells(int side);

    /// <summary>
    /// The repair pads <paramref name="side"/> can see, and so can plan to
    /// reach, ascending.
    /// </summary>
    /// <remarks>
    /// A pad the side cannot see is not in here, which is the whole point: a
    /// retreat is planned to ground the side has actually found, and a viewer
    /// that drew every pad would show a fallback nobody in the fight knows
    /// about.
    /// </remarks>
    [Observes]
    IReadOnlyList<int> RepairPoints(int side);

    /// <summary>
    /// What <paramref name="side"/> remembers about enemy units it has seen, by
    /// agent, ascending. Empty for a side that is told everything and so
    /// remembers nothing.
    /// </summary>
    [Observes]
    IReadOnlyList<RememberedUnit> Remembered(int side);
}
