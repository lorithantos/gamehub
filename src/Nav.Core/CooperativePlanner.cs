namespace Nav.Core;

/// <summary>
/// A* over <c>(cell, tick)</c> instead of <c>cell</c>, respecting what other
/// agents have already reserved.
/// </summary>
/// <remarks>
/// This is <see cref="PathFinder"/> with one dimension added, and deliberately
/// little else. Three things differ:
/// <list type="number">
/// <item><description>A state is a cell <em>at a tick</em>, so the same cell at two
/// times is two states.</description></item>
/// <item><description>Waiting is a ninth action. Without it an agent cannot yield
/// right of way, and instances that are trivially solvable report as
/// unsolvable.</description></item>
/// <item><description>A neighbour must be unreserved as well as passable, and the
/// move must not swap through an agent coming the other way.</description></item>
/// </list>
/// <para>
/// The octile heuristic survives unchanged and stays admissible: it ignores
/// reservations and waiting, and both can only make a plan longer.
/// </para>
/// <para>
/// Search is bounded by the reservation window. Beyond it nothing is reserved, so
/// searching further would be ordinary A* with the cooperation switched off --
/// which is the planner silently answering a different question. A goal beyond the
/// window yields a partial plan instead, and the agent replans once the window has
/// moved.
/// </para>
/// <para>
/// <b>The search itself lives in <see cref="BudgetedSearch"/>, and this is a
/// wrapper that runs it to completion.</b> One implementation, so a plan built in
/// instalments and a plan built in one call cannot drift apart -- there is no
/// second code path for them to drift in.
/// </para>
/// </remarks>
public static class CooperativePlanner
{
    /// <summary>
    /// Plans a route through space and time, running the search to completion.
    /// </summary>
    /// <remarks>
    /// Use <see cref="BudgetedSearch"/> directly where the caller has a frame to
    /// fit inside; this is the convenience for callers that do not.
    /// </remarks>
    public static PlanResult FindPlan(
        Grid grid,
        ReservationTable reservations,
        int agent,
        int start,
        int goal,
        int startTick,
        SearchWorkspace workspace) =>
        new BudgetedSearch(grid, reservations, agent, start, goal, startTick, workspace).RunToCompletion();
}
