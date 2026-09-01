namespace Nav.Core;

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
    /// <summary>
    /// The world as one tick saw it: state <em>after</em> the orders due at
    /// <paramref name="Tick"/> were issued, <em>before</em> the tick advanced —
    /// the same instant the trajectory check records. <paramref name="Report"/>
    /// is the previous tick's spend, all zeros on tick 0.
    /// </summary>
    public sealed record TraceTick(int Tick, IReadOnlyList<AgentState> Agents, TickReport Report);

    /// <summary>
    /// Simulates a scenario from tick 0 through its end tick and reports what
    /// happened.
    /// </summary>
    /// <remarks>
    /// Coordinates are validated before anything runs: an agent off the map or on
    /// a wall throws rather than being clamped onto the nearest legal cell, and so
    /// does an order aimed off the map. A scenario paired with the wrong map is a
    /// broken test, not a harder one, and quietly moving the units would turn it
    /// into a plausible-looking pass.
    /// <para>
    /// An order aimed at a wall is <em>not</em> an error, and that asymmetry is
    /// deliberate: <see cref="MovementSystem.Order(IReadOnlyList{int}, int)"/> snaps an impassable
    /// destination to the nearest passable cell, because a click on a wall means
    /// the ground beside it. Off the map has no such reading.
    /// </para>
    /// <para>
    /// The whole run is deterministic, so playing one scenario twice must produce
    /// an identical <see cref="ScenarioOutcome"/> -- that equality is itself a
    /// test, and the cheapest determinism check there is.
    /// </para>
    /// </remarks>
    /// <param name="scenario">
    /// Placements and the order timeline, checked against <paramref name="grid"/>
    /// before the run starts: the map must be the size the scenario was recorded
    /// against (<see cref="RecordedScenario.EnsureMatches"/>), placements must be
    /// in bounds and passable, and order destinations in bounds.
    /// <para>
    /// The one wrong pairing that survives all of that is a map of the
    /// <em>same</em> size with different walls. Catching it would take a content
    /// fingerprint, which would also make these files impossible to write by hand,
    /// so it is deliberately not caught -- though a scenario whose units start
    /// inside the new walls still fails the passability check.
    /// </para>
    /// </param>
    /// <param name="grid">The map to run on.</param>
    /// <param name="horizon">
    /// Depth of the reservation window in ticks -- how far ahead agents reserve
    /// and plan. Passed straight through to <see cref="MovementSystem"/>.
    /// </param>
    /// <param name="onTick">
    /// Called once per tick with the state <em>after</em> that tick's orders were
    /// issued and <em>before</em> the world advanced, which is the same instant
    /// the trajectory check records. Null when nothing is tracing.
    /// </param>
    /// <returns>
    /// What happened: trajectories, the conflict verdict over them, and the
    /// counters that separate a finished run from a deadlocked one.
    /// </returns>
    /// <exception cref="MapFormatException">
    /// <paramref name="grid"/> is not the size <paramref name="scenario"/> was
    /// recorded against.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An agent is placed off the map or on an impassable cell, or an order sends
    /// agents to a cell off the map.
    /// </exception>
    public static ScenarioOutcome Play(
        RecordedScenario scenario, Grid grid, int horizon = 32, Action<TraceTick>? onTick = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(grid);

        // One call, and it is the only validation here: size, placements and
        // order destinations all live on the scenario, so playback and the viewer
        // cannot drift apart by one of them forgetting a check the other makes.
        scenario.EnsureMatches(grid);

        var system = new MovementSystem(grid, horizon);
        var trails = new List<int>[scenario.Agents.Count];

        foreach (var placement in scenario.Agents)
        {
            var id = system.AddAgent(grid.Index(placement.X, placement.Y));
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

            onTick?.Invoke(new TraceTick(tick, [.. system.Agents], system.LastTick));

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
            Stuck: final.Count(a => a.Stuck),
            MaxStalledTicks: final.Count == 0 ? 0 : final.Max(a => a.StalledTicks));
    }
}
