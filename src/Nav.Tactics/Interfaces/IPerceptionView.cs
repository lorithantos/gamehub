namespace Nav.Tactics.Interfaces;

/// <summary>
/// What an instrument may ASK about a side's perception: three questions, the
/// tick they are all answered as of, and no verb anywhere on the type.
/// </summary>
/// <remarks>
/// <b>The simulation is a synchronous digital system, and the only state anybody
/// can see is the state at a clock edge.</b> A stop lands at the end of a tick,
/// where <see cref="DemoWorld.Settle"/> has already had every side look at what
/// the shots and the deaths left. So there is nothing here to resolve and no
/// value caught mid-transition -- <see cref="AsOf"/> names the edge, and the
/// three answers below are that one edge read three ways.
/// <para>
/// <b>A type rather than a convention, because a convention is what failed.</b>
/// <see cref="Core.Interfaces.IDistanceFieldView"/> exists for the same reason
/// one layer down -- a peek that sat beside the mutating member worked only for
/// as long as every caller chose the polite one -- and
/// <see cref="Core.Interfaces.IReservationView"/> is the same idiom, older.
/// Handing an instrument a reference that has no verb on it is the same
/// guarantee without the discipline.
/// </para>
/// <para>
/// <b>It used to be a peek that could lag the board, and that was the fault.</b>
/// <see cref="DemoWorld.HostilesFor"/>, <see cref="DemoWorld.SightingsFor"/> and
/// <see cref="DemoWorld.RepairPointsFor"/> each brought every side's view up to
/// date before answering, so an instrument reading them stamped sightings with
/// the tick IT asked on, and this type existed to keep the instrument off that
/// path at the price of answering from a resolution that could be a tick old.
/// The resolve moved to the edge instead, which is where the model always said
/// it was: nobody provokes one now, so nobody has to be kept away from one, and
/// what an instrument reads is what doctrine reads.
/// </para>
/// <para>
/// It is the world's own <see cref="DemoWorld.View"/> and not a snapshot: what
/// it answers moves with the run, one edge at a time. What it CANNOT do is move
/// it.
/// </para>
/// </remarks>
public interface IPerceptionView
{
    /// <summary>
    /// The tick every answer here is as of: the edge the last
    /// <see cref="DemoWorld.Settle"/> left, or -1 for a world not yet listening
    /// to a movement system, which has had no edge to be as of.
    /// </summary>
    /// <remarks>
    /// <b>One number for the whole view, not one per member.</b> A clock edge
    /// makes every output valid together, so the three questions below are parts
    /// of ONE reading; stamping each of them separately would say the view was
    /// three snapshots that happened to arrive together.
    /// <para>
    /// <b>Not <see cref="Models.Sighting.Tick"/>, which answers a different
    /// question.</b> This is when the side last looked; that is when the side
    /// last SAW one particular unit. The gap between them, <c>AsOf</c> minus the
    /// sighting's tick, is how stale a side's knowledge of that one enemy is,
    /// and neither number can be read off the other.
    /// </para>
    /// </remarks>
    [Observes]
    int AsOf { get; }

    /// <summary>
    /// Cells hostile to <paramref name="side"/> as of <see cref="AsOf"/>:
    /// scripted threats and other sides' units this side can see, ascending.
    /// Empty for a side no edge has covered.
    /// </summary>
    /// <remarks>
    /// Without <see cref="DemoWorld.Fog"/> there is nothing to resolve, so this
    /// is the omniscient answer <see cref="DemoWorld.HostilesFor"/> gives: every
    /// scripted threat and every other side's unit, read off the board.
    /// </remarks>
    [Observes]
    IReadOnlyList<int> PeekHostiles(int side);

    /// <summary>
    /// What <paramref name="side"/> remembers about enemy units as of
    /// <see cref="AsOf"/>, by agent, ascending. Empty for a side no edge has
    /// covered.
    /// </summary>
    /// <remarks>
    /// Empty without <see cref="DemoWorld.Fog"/>, as
    /// <see cref="DemoWorld.SightingsFor"/> is: a world that sees everything
    /// knows nothing it cannot currently see.
    /// <para>
    /// A sighting carries the tick the side last saw THAT unit on, which is the
    /// number doctrine ages it against. Reading it can never move it.
    /// </para>
    /// </remarks>
    [Observes]
    IReadOnlyList<Sighting> PeekSightings(int side);

    /// <summary>
    /// Pads <paramref name="side"/> can see as of <see cref="AsOf"/>, and so can
    /// plan to reach. Empty for a side no edge has covered.
    /// </summary>
    /// <remarks>
    /// Without <see cref="DemoWorld.Fog"/> this is every repair cell, as
    /// <see cref="DemoWorld.RepairPointsFor"/> gives.
    /// <para>
    /// Copied on the way out, where <see cref="DemoWorld.RepairPointsFor"/> hands
    /// back the list it holds. An answer that changes after it was given is not a
    /// stale answer, it is an answer to a question nobody asked.
    /// </para>
    /// </remarks>
    [Observes]
    IReadOnlyList<int> PeekRepairPoints(int side);
}
