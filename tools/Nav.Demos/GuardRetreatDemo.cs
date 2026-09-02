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
/// <para>
/// <b>The damage here is middling on purpose.</b> Both casualties are hurt to
/// around half, which is above the threshold this demo used to run with, so
/// under the old numbers neither would have moved -- the guard only left when
/// it was nearly dead, and the retreat was never a decision, just a last
/// resort. The played doctrine is <em>retreat at middling damage, return as
/// soon as it is worth it</em>: leave early, take the short trip, be back on
/// the line quickly. A unit here is away for a fraction of the time it used
/// to be, and that is the whole behaviour.
/// </para>
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

        var world = new DemoWorld(grid, repairPerTick: 0.03);
        world.RepairCells.Add(padNorth);
        world.RepairCells.Add(padSouth);

        // Four guards, starting scattered on the left so the march to station is
        // itself worth watching.
        int[] starts = [grid.Index(1, 6), grid.Index(1, 9), grid.Index(2, 12), grid.Index(1, 14)];
        foreach (var cell in starts)
        {
            system.AddAgent(cell);
        }

        var squad = new Squad("guard", [0, 1, 2, 3], new GuardDoctrine(station, retreatBelow: 0.55, returnAbove: 0.8));

        DemoTrace.WriteHeader(trace, Name, Description, grid, world.RepairPoints, Ticks);

        var wasAway = new bool[4];
        var wasArrived = new bool[4];
        var ticksAway = new int[4];

        for (var tick = 0; tick < Ticks; tick++)
        {
            string? note = null;

            // Two casualties, and the second lands while the first is still at
            // its pad. That overlap is the point: with one pad already spoken
            // for, the doctrine has to send the second unit to the other one
            // rather than queueing both at the nearest. Neither is anywhere
            // near dead -- half health and a little under -- so what sends them
            // is the doctrine's judgement that a hurt unit is worth pulling,
            // not the arithmetic of a unit about to be lost.
            if (tick == 70)
            {
                world.SetHealth(2, 0.5);
                note = "unit 2 takes fire, down to half";
            }
            else if (tick == 88)
            {
                world.SetHealth(0, 0.45);
                note = "unit 0 takes fire, with unit 2 still at the pad";
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

                if (agent.Away)
                {
                    ticksAway[agent.Id]++;
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

        // Time off the line is the number this doctrine is judged on, not final
        // health: a unit that comes back at 0.8 having been gone thirty ticks is
        // the intended outcome, and counting units "at full health" would score
        // that as a failure.
        var final = system.Agents;
        var away = ticksAway.Where(t => t > 0).ToArray();
        return new Run(
            Ticks, final, world,
            $"2 casualties at middling damage, both back on the line; {final.Count(a => a.Arrived)}/{final.Count} in place, "
                + $"off the line {string.Join(" and ", away)} ticks of {Ticks}");
    }
}
