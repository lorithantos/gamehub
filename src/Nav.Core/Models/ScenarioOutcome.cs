namespace Nav.Core.Models;

/// <param name="Ticks">Ticks simulated, including tick zero.</param>
/// <param name="FinalCells">Where each agent ended, indexed by agent id.</param>
/// <param name="Trajectories">Every agent's actual position on every tick.</param>
/// <param name="Conflicts">Collisions found in what actually happened, not in what was planned.</param>
/// <param name="TotalExpanded">Search nodes spent across the whole run.</param>
/// <param name="Arrived">How many agents finished on their goal.</param>
/// <param name="Stuck">How many ended with an order they were making no progress on.</param>
/// <param name="MaxStalledTicks">
/// The longest any agent went without getting closer to its goal.
/// </param>
/// <remarks>
/// <paramref name="MaxStalledTicks"/> is reported because a deadlock is otherwise
/// indistinguishable from a run that simply had not finished. A scenario can end
/// with nobody colliding, nobody erroring, and nothing having happened for four
/// hundred ticks.
/// </remarks>
public sealed record ScenarioOutcome(
    int Ticks,
    IReadOnlyList<int> FinalCells,
    IReadOnlyList<AgentPlan> Trajectories,
    ConflictReport Conflicts,
    long TotalExpanded,
    int Arrived,
    int Stuck,
    int MaxStalledTicks)
{
    /// <summary>
    /// Every agent was standing on its goal cell when the run ended. It says
    /// nothing about the journey: a run can be <c>AllArrived</c> and still have
    /// walked units straight through each other, which is why
    /// <see cref="Conflicts"/> is a separate verdict and both are asserted.
    /// </summary>
    public bool AllArrived => Arrived == FinalCells.Count;
}
