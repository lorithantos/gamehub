namespace Nav.Core;

/// <summary>
/// Runs a recorded scenario and reports what happened.
/// </summary>
/// <remarks>
/// Headless, with no reference to any renderer or host. That is what lets the test
/// suite run it, and it is the same discipline the viewer seam established: a
/// viewer may later drive playback, but playback must never need one.
/// <para>
/// Collisions are checked against the TRAJECTORIES — where agents actually went —
/// rather than against their plans.
/// </para>
/// <para>
/// Windowed replanning replaces plans constantly, so a set of plans that never
/// conflicted could still describe a run in which two units overlapped. What
/// happened is the thing worth checking.
/// </para>
/// </remarks>
public static class ScenarioPlayback
{
    /// <summary>
    /// Simulates a scenario from tick 0 through its end tick and reports what
    /// happened.
    /// </summary>
    /// <remarks>
    /// Coordinates are validated before anything runs. An agent off the map or on
    /// a wall throws rather than being clamped to the nearest legal cell.
    /// <para>
    /// A scenario paired with the wrong map is a broken test, not a harder one,
    /// and quietly moving the units would turn it into a plausible pass.
    /// </para>
    /// <para>
    /// An order aimed at a WALL is not an error, and the asymmetry is deliberate:
    /// an impassable destination snaps to the nearest passable cell, because a
    /// click on a wall means the ground beside it. Off the map has no such
    /// reading.
    /// </para>
    /// <para>
    /// The whole run is deterministic, so playing one scenario twice must produce
    /// an identical <see cref="ScenarioOutcome"/> -- that equality is itself a
    /// test, and the cheapest determinism check there is.
    /// </para>
    /// </remarks>
    /// <param name="scenario">
    /// Placements and the order timeline, checked against <paramref name="grid"/>
    /// before the run starts: the recorded size, placements in bounds and
    /// passable, order destinations in bounds.
    /// <para>
    /// The one wrong pairing that survives that is a map of the <em>same</em>
    /// size with different walls.
    /// </para>
    /// <para>
    /// Catching it would take a content fingerprint, which would also make these
    /// files impossible to write by hand — though a scenario whose units start
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
    /// <param name="tieBreakSeed">
    /// Passed through to <see cref="MovementSystem"/>: pops a different but fixed
    /// one of each search's equally good frontier entries. Null is the production
    /// ordering; any other value is one alternative valid A*, replayable forever,
    /// against which the collision verdict must still hold.
    /// </param>
    /// <param name="nodeBudgetPerTick">
    /// Passed through to <see cref="MovementSystem"/>. The default is the match
    /// setting.
    /// <para>
    /// A SMALL value is the regime where searches genuinely suspend across ticks,
    /// which is where a reservation made against a stale frontier could bite.
    /// </para>
    /// <para>
    /// So the tie-break fuzz has to run there as well as at the default, because
    /// at the default almost nothing ever suspends.
    /// </para>
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
        RecordedScenario scenario, Grid grid, int horizon = 32, Action<TraceTick>? onTick = null,
        int? tieBreakSeed = null, int nodeBudgetPerTick = 4000)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(grid);

        // One call, and it is the only validation here: size, placements and
        // order destinations all live on the scenario, so playback and the viewer
        // cannot drift apart by one of them forgetting a check the other makes.
        scenario.EnsureMatches(grid);

        var system = new MovementSystem(grid, horizon, nodeBudgetPerTick, tieBreakSeed: tieBreakSeed);
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
