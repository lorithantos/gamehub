namespace Nav.Core;

/// <param name="Ticks">Ticks simulated, including tick zero.</param>
/// <param name="FinalCells">Where each agent ended, indexed by agent id.</param>
/// <param name="Trajectories">Every agent's actual position on every tick.</param>
/// <param name="Conflicts">Collisions found in what actually happened, not in what was planned.</param>
/// <param name="TotalExpanded">Search nodes spent across the whole run.</param>
/// <param name="Arrived">How many agents finished on their goal.</param>
/// <param name="Stuck">How many ended with an order they could make no progress on.</param>
public sealed record ScenarioOutcome(
    int Ticks,
    IReadOnlyList<int> FinalCells,
    IReadOnlyList<AgentPlan> Trajectories,
    ConflictReport Conflicts,
    long TotalExpanded,
    int Arrived,
    int Stuck)
{
    public bool AllArrived => Arrived == FinalCells.Count;
}

/// <summary>
/// Runs a recorded scenario and reports what happened.
/// </summary>
/// <remarks>
/// Headless, with no reference to any renderer or host. That is what lets the test
/// suite run it, and it is the same discipline the viewer seam established: a
/// viewer may later drive playback, but playback must never need one.
/// <para>
/// Collisions are checked against the TRAJECTORIES — where agents actually went —
/// rather than against their plans. Plans are replaced constantly by windowed
/// replanning, so a set of plans that never conflicted with each other could still
/// have described a run in which two units overlapped. What happened is the thing
/// worth checking.
/// </para>
/// </remarks>
public static class ScenarioPlayback
{
    public static ScenarioOutcome Play(RecordedScenario scenario, Grid grid, int horizon = 32)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(grid);

        var system = new MovementSystem(grid, horizon);
        var trails = new List<int>[scenario.Agents.Count];

        foreach (var placement in scenario.Agents)
        {
            if (!grid.InBounds(placement.X, placement.Y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    $"agent {placement.Id} is placed at ({placement.X},{placement.Y}), off a {grid.Width}x{grid.Height} map.");
            }

            var cell = grid.Index(placement.X, placement.Y);
            if (!grid.IsPassable(cell))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    $"agent {placement.Id} is placed at ({placement.X},{placement.Y}), which is not passable.");
            }

            var id = system.AddAgent(cell);
            trails[id] = [];
        }

        var pending = 0;
        for (var tick = 0; tick <= scenario.EndTick; tick++)
        {
            while (pending < scenario.Orders.Count && scenario.Orders[pending].Tick == tick)
            {
                var order = scenario.Orders[pending++];
                system.Order(order.Agents, grid.Index(order.X, order.Y));
            }

            foreach (var agent in system.Agents)
            {
                trails[agent.Id].Add(agent.Cell);
            }

            if (tick < scenario.EndTick)
            {
                system.Tick();
            }
        }

        var trajectories = trails
            .Select((cells, id) => new AgentPlan(id, new PlanResult(cells, 0, 0.0, 0, Found: true)))
            .ToArray();

        var final = system.Agents;
        return new ScenarioOutcome(
            Ticks: scenario.EndTick + 1,
            FinalCells: [.. final.Select(a => a.Cell)],
            Trajectories: trajectories,
            Conflicts: CollisionCheck.Inspect(trajectories),
            TotalExpanded: system.TotalExpanded,
            Arrived: final.Count(a => a.Arrived),
            Stuck: final.Count(a => a.Stuck));
    }
}
