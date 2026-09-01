namespace Nav.Core;

/// <summary>
/// What a planner may ASK about the reservation window. Questions only -- nothing
/// here books, releases or advances anything.
/// </summary>
/// <remarks>
/// The split was already there before it was written down: <see cref="BudgetedSearch"/>
/// calls exactly <see cref="IsFree"/>, <see cref="IsSwap"/> and
/// <see cref="IsHoldable"/> and never touches the table's mutators, while
/// <see cref="MovementSystem"/> calls <c>Reserve</c> and <c>Advance</c> and never
/// asks a predicate. Naming the read half separates the component that decides
/// where to walk from the component that owns what has been booked.
/// <para>
/// <b>The reason it is an interface rather than a comment</b> is that visibility
/// of reservations is a per-observer question, not a global fact. Today one table
/// serves everybody, so an agent plans against every other agent's committed
/// future -- correct for one commander, and mind-reading for two. A view that
/// wraps the real table and answers <c>free</c> for reservations the asker cannot
/// observe makes fog and multi-team planning a DECORATOR rather than a change to
/// the collision core. Everything underneath keeps its guarantees, because the
/// core never learns that anyone is filtering.
/// </para>
/// <para>
/// Other wraps this shape allows: a recording view, which answers what a search
/// consulted and is the cheapest way to see why an agent yielded; and an
/// always-free view, which measures what contention actually costs by removing it.
/// </para>
/// <para>
/// <c>HolderOf</c> is deliberately NOT here. It is a read, but no production code
/// calls it -- only tests, against the concrete table -- and a filtering view
/// would have to decide what identity to reveal, which is a harder question than
/// this contract needs to answer.
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
    int Horizon { get; }

    /// <summary>
    /// The earliest tick still tracked. The window is
    /// <c>[CurrentTick, CurrentTick + Horizon)</c>.
    /// </summary>
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
    bool IsFree(int cell, int tick, int agent);

    /// <summary>
    /// True if moving <paramref name="from"/> to <paramref name="to"/> across the
    /// tick beginning at <paramref name="tick"/> would pass through another agent
    /// coming the other way.
    /// </summary>
    /// <remarks>
    /// THE EDGE COLLISION, and the one a cell-occupancy check cannot see. Two
    /// agents exchanging places share no cell at either tick -- A is here then
    /// there, B is there then here -- and they walk through each other. A suite
    /// that checks only occupancy reports it as clean.
    /// </remarks>
    bool IsSwap(int from, int to, int tick, int agent);

    /// <summary>
    /// True if <paramref name="agent"/> could occupy <paramref name="cell"/> from
    /// <paramref name="fromTick"/> to the end of the window and not move again.
    /// </summary>
    /// <remarks>
    /// A plan does not only pass through cells, it ends on one -- and stopping is a
    /// commitment to stay. An agent that walks somewhere it may not remain parks in
    /// another agent's path, and because reserving holds the final cell for the
    /// rest of the window it does so by overwriting a reservation that was already
    /// there. Every step legal, the destination not.
    /// </remarks>
    bool IsHoldable(int cell, int fromTick, int agent);
}
