namespace Nav.Tactics.Interfaces;

/// <summary>
/// What an instrument may ASK about a side's perception. Three questions, and no
/// way from here to make a side look.
/// </summary>
/// <remarks>
/// <b>Resolving perception is a verb, and it reads exactly like a question.</b>
/// <see cref="DemoWorld.HostilesFor"/>, <see cref="DemoWorld.SightingsFor"/> and
/// <see cref="DemoWorld.RepairPointsFor"/> each bring every side's view of the
/// board up to date before answering, which is right for doctrine and wrong for
/// a panel: the sighting a panel provokes is stamped with the tick the PANEL
/// asked on, and <see cref="Models.Sighting.Tick"/> is the number doctrine
/// compares against to decide forgetting. So a side forgets on a different tick
/// on the runs where somebody was watching.
/// <para>
/// <b>A type rather than a convention, because a convention is what failed.</b>
/// <see cref="Core.Interfaces.IDistanceFieldView"/> exists for the same reason
/// one layer down -- a peek that sat beside the mutating member worked only for
/// as long as every caller chose the polite one -- and
/// <see cref="Core.Interfaces.IReservationView"/> is the same idiom, older.
/// Handing an instrument a reference that cannot resolve is the
/// same guarantee without the discipline.
/// </para>
/// <para>
/// <b>A reading here can be older than the board, and that is the point.</b> The
/// obvious objection is that a peek shows a side seeing less than it really
/// does, and that stale numbers are a lie in an instrument. It is the other way
/// round. What this answers is what doctrine LAST ACTED ON; a freshly resolved
/// answer would show the reader knowledge the doctrine did not have when it
/// decided, and a panel watching a side act on limited knowledge would be
/// reading the one thing that side never knew. The staleness is not a defect in
/// the view -- it is the gap between what is true and what is known, which is
/// the whole subject.
/// </para>
/// <para>
/// <b>A view nobody has resolved yet answers nothing.</b> Before the first
/// doctrine query there is no last resolution to read, so every side knows
/// nothing. That is not a wrong answer standing in for a right one: a side that
/// has never looked HAS seen nothing, and reporting emptiness is exactly what
/// happened.
/// </para>
/// <para>
/// It is the world's own <see cref="DemoWorld.View"/> and not a snapshot: what
/// it answers moves with the run, as each resolution leaves new state behind.
/// What it CANNOT do is cause one.
/// </para>
/// </remarks>
public interface IPerceptionView
{
    /// <summary>
    /// Cells hostile to <paramref name="side"/> as of the last resolution:
    /// scripted threats and other sides' units this side could see then,
    /// ascending. Empty for a side no resolution has covered.
    /// </summary>
    /// <remarks>
    /// Without <see cref="DemoWorld.Fog"/> there is nothing to resolve, so this
    /// is the omniscient answer <see cref="DemoWorld.HostilesFor"/> gives and is
    /// never stale: every scripted threat and every other side's unit, read off
    /// the board.
    /// </remarks>
    [Observes]
    IReadOnlyList<int> PeekHostiles(int side);

    /// <summary>
    /// What <paramref name="side"/> remembered about enemy units at the last
    /// resolution, by agent, ascending. Empty for a side no resolution has
    /// covered.
    /// </summary>
    /// <remarks>
    /// Empty without <see cref="DemoWorld.Fog"/>, as
    /// <see cref="DemoWorld.SightingsFor"/> is: a world that sees everything
    /// knows nothing it cannot currently see.
    /// <para>
    /// A sighting here carries the tick the RESOLUTION took it on, which is the
    /// number doctrine ages it against. Reading it can never move it.
    /// </para>
    /// </remarks>
    [Observes]
    IReadOnlyList<Sighting> PeekSightings(int side);

    /// <summary>
    /// Pads <paramref name="side"/> could see at the last resolution, and so
    /// could plan to reach. Empty for a side no resolution has covered.
    /// </summary>
    /// <remarks>
    /// Without <see cref="DemoWorld.Fog"/> this is every repair cell, as
    /// <see cref="DemoWorld.RepairPointsFor"/> gives, and is never stale.
    /// </remarks>
    [Observes]
    IReadOnlyList<int> PeekRepairPoints(int side);
}
