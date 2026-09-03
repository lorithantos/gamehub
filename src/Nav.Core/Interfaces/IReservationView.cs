namespace Nav.Core.Interfaces;

/// <summary>
/// What a planner may ASK about the reservation window. Questions only -- nothing
/// here books, releases or advances anything.
/// </summary>
/// <remarks>
/// It separates the component that decides where to walk from the component
/// that owns what has been booked.
/// <para>
/// <b>An interface rather than a convention, because visibility is per-observer.</b>
/// One table serves one side: an agent plans against every fellow's committed
/// future, which is correct for one commander and would be mind-reading for
/// two. Another side's units reach it only as the ground they stand on.
/// </para>
/// <para>
/// That is a DECORATOR over the side's own table rather than a change to the
/// collision core, and fog -- answering <c>free</c> for what the asker cannot
/// see -- is the same shape again.
/// </para>
/// <para>
/// <c>HolderOf</c> is deliberately NOT here: a filtering view would have to
/// decide what identity to reveal, which is a harder question than this contract
/// needs.
/// </para>
/// </remarks>
public interface IReservationView
{
    /// <summary>
    /// How many ticks the window covers. Fixed for the life of the view, never
    /// below 2, and the price of lookahead paid twice over: the ring holds this
    /// many grid-sized arrays, and a space-time search over the window has
    /// <c>Horizon * cellCount</c> states to work in.
    /// </summary>
    [Observes]
    int Horizon { get; }

    /// <summary>
    /// The earliest tick still tracked. The window is
    /// <c>[CurrentTick, CurrentTick + Horizon)</c>.
    /// </summary>
    [Observes]
    int CurrentTick { get; }

    /// <summary>
    /// True if <paramref name="agent"/> may occupy <paramref name="cell"/> at
    /// <paramref name="tick"/> -- either nobody holds it, or this agent already
    /// does.
    /// </summary>
    /// <remarks>
    /// An agent never conflicts with itself. Replanning would otherwise be blocked
    /// by the plan it is replacing.
    /// </remarks>
    [Observes]
    bool IsFree(int cell, int tick, int agent);

    /// <summary>
    /// True if moving <paramref name="from"/> to <paramref name="to"/> across the
    /// tick beginning at <paramref name="tick"/> would pass through another agent
    /// coming the other way.
    /// </summary>
    /// <remarks>
    /// THE EDGE COLLISION, and the one a cell-occupancy check cannot see.
    /// <para>
    /// Two agents exchanging places share no cell at either tick — A here then
    /// there, B there then here — so they walk through each other and a suite
    /// checking only occupancy reports it clean.
    /// </para>
    /// </remarks>
    [Observes]
    bool IsSwap(int from, int to, int tick, int agent);

    /// <summary>
    /// True if <paramref name="agent"/> could occupy <paramref name="cell"/> from
    /// <paramref name="fromTick"/> to the end of the window and not move again.
    /// </summary>
    /// <remarks>
    /// A plan does not only pass through cells, it ENDS on one — and stopping is
    /// a commitment to stay.
    /// <para>
    /// An agent that walks somewhere it may not remain parks in another agent's
    /// path, and since reserving holds the final cell for the rest of the window,
    /// it does so by overwriting a reservation already there.
    /// </para>
    /// <para>Every step legal, the destination not.</para>
    /// </remarks>
    [Observes]
    bool IsHoldable(int cell, int fromTick, int agent);
}
