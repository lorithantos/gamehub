namespace Nav.Demos;

/// <summary>
/// The guard that does not die beside the cannon: four units hold a position,
/// two of them are hurt at different moments, each falls back to a repair pad
/// and returns to the line.
/// </summary>
/// <remarks>
/// The C&amp;C behaviour this project was started over. Watch for three things:
/// the guard takes its station and stops; a damaged unit LEAVES while the rest
/// hold, so the line never abandons the position; and it comes back to the
/// formation rather than standing at the pad forever.
/// </remarks>
internal sealed class GuardRetreatDemo : Demo
{
    private const string Map =
        """
        type octile
        height 17
        width 25
        map
        .........................
        .........................
        ....@@@@@@@@.....@@@@....
        ....@......@.....@..@....
        ....@......@.....@..@....
        ....@......@.....@@@@....
        ....@@@@@@@@.............
        .........................
        .........................
        .........................
        ....@@@@.................
        ....@..@.....@@@@@@@@....
        ....@..@.....@......@....
        ....@@@@.....@......@....
        .............@@@@@@@@....
        .........................
        .........................
        """;

    public override string Name => "guard-retreat";

    public override string Description =>
        "Four guards hold a position; the damaged fall back to repair and return to the line.";

    public override int Ticks => 320;

    public override Run Play(TextWriter trace)
    {
        var grid = Grid.FromMapText(Map);
        var system = new MovementSystem(grid);

        var station = grid.Index(12, 8);
        var padNorth = grid.Index(2, 1);
        var padSouth = grid.Index(22, 15);

        var world = new DemoWorld(repairPerTick: 0.03);
        world.RepairCells.Add(padNorth);
        world.RepairCells.Add(padSouth);

        // Four guards, starting scattered on the left so the march to station is
        // itself worth watching.
        int[] starts = [grid.Index(1, 6), grid.Index(1, 9), grid.Index(2, 12), grid.Index(1, 14)];
        foreach (var cell in starts)
        {
            system.AddAgent(cell);
        }

        var squad = new Squad("guard", [0, 1, 2, 3], new GuardDoctrine(station, retreatBelow: 0.4, returnAbove: 0.95));

        DemoTrace.WriteHeader(trace, Name, Description, grid, world.RepairPoints, Ticks);

        var wasAway = new bool[4];
        var wasArrived = new bool[4];

        for (var tick = 0; tick < Ticks; tick++)
        {
            string? note = null;

            // Two casualties, at moments far enough apart to be told from each
            // other: the first once the line has formed, the second while the
            // first is still away.
            if (tick == 70)
            {
                world.SetHealth(2, 0.25);
                note = "unit 2 takes fire";
            }
            else if (tick == 150)
            {
                world.SetHealth(0, 0.2);
                note = "unit 0 takes fire";
            }

            squad.Advance(system, world);
            system.Tick();

            var agents = system.Agents;
            world.Settle(agents);

            // Narrate the doctrine's decisions as they become visible.
            foreach (var agent in agents)
            {
                if (agent.Away && !wasAway[agent.Id])
                {
                    note = $"unit {agent.Id} falls back to repair";
                }
                else if (!agent.Away && wasAway[agent.Id])
                {
                    note = $"unit {agent.Id} rejoins the line";
                }
                else if (agent.Away && agent.Arrived && !wasArrived[agent.Id])
                {
                    note = $"unit {agent.Id} reaches the pad";
                }

                wasAway[agent.Id] = agent.Away;
                wasArrived[agent.Id] = agent.Arrived;
            }

            if (note is null && tick == 0)
            {
                note = "the squad is ordered to hold the centre";
            }

            DemoTrace.WriteTick(trace, grid, tick, agents, world, squad.Anchor, note);
        }

        var final = system.Agents;
        var repaired = final.Count(a => world.HealthOf(a.Id) >= 0.99);
        return new Run(
            Ticks, final, world,
            $"2 casualties, both repaired and back on the line; {final.Count(a => a.Arrived)}/{final.Count} in place, {repaired}/{final.Count} at full health");
    }
}
