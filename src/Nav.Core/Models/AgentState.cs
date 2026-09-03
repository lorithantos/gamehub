namespace Nav.Core.Models;

/// <param name="Id">Stable for the life of the system, and the planning order.</param>
/// <param name="Cell">Where the agent is now.</param>
/// <param name="Goal">Where it is trying to get to, or its own cell if it has no order.</param>
/// <param name="Arrived">Standing on its goal.</param>
/// <param name="StalledTicks">Consecutive replans that got it no closer to its goal.</param>
/// <param name="Thinking">A search is in flight for this agent; it holds position until it lands.</param>
/// <param name="Waiting">
/// Gated from planning until an event or its backstop: queued by a doctrine,
/// backing off after a failed replan, or held short of a gate. WAITING IS NOT
/// FAILING — a unit doing nothing visible reads as "I've refused the order"
/// unless the display can say "I'm in the queue", which is what this flag is for.
/// </param>
/// <param name="Errand">
/// Where the agent is going on its own, sent by
/// <see cref="MovementSystem.Dispatch"/>, or -1 while it is with its formation.
/// The formation still counts it as a member; see <see cref="Away"/>.
/// </param>
/// <remarks>
/// <see cref="Stuck"/> means NO PROGRESS, not "no plan".
/// <para>
/// An agent that can stand still always HAS a plan — the one-cell plan of staying
/// put — so a check for "did the planner return anything" reports two agents
/// deadlocked nose to nose as perfectly healthy.
/// </para>
/// <para>They have plans. The plans go nowhere.</para>
/// </remarks>
public readonly record struct AgentState(
    int Id,
    int Cell,
    int Goal,
    bool Arrived,
    int StalledTicks,
    bool Thinking,
    bool Waiting,
    int Errand = -1)
{
    /// <summary>Has an order it is making no progress on.</summary>
    public bool Stuck => !Arrived && StalledTicks > 0;

    /// <summary>
    /// Away from its formation on an errand of its own. Still a member of it: the
    /// formation keeps its place, and the next group order takes it along.
    /// </summary>
    public bool Away => Errand >= 0;
}
