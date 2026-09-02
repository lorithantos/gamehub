namespace Nav.Demos;

/// <summary>
/// The patrol that cannot be baited: three units walk a route past a wood, a
/// lure appears beside them, they go for it as one body, it runs, and they
/// break off and resume the route.
/// </summary>
/// <remarks>
/// Watch for the leash. The lure's first position is inside it, so the whole
/// patrol commits; its second is outside, so the patrol stops being interested
/// and turns round rather than being walked into the corner. And watch that
/// nobody goes alone: the doctrine has no verb for sending one unit, so the
/// three move together or not at all.
/// </remarks>
internal sealed class PatrolBaitDemo : Demo
{
    private const string Map =
        """
        type octile
        height 15
        width 29
        map
        .............................
        .............................
        ....@@@@@@.......@@@@@@@@....
        ....@@@@@@.......@@@@@@@@....
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        ....@@@@@@@@@........@@@@....
        ....@@@@@@@@@........@@@@....
        .............................
        .............................
        .............................
        """;

    public override string Name => "patrol-bait";

    public override string Description =>
        "A patrol walks its route, takes the bait as one body, and breaks off when the lure runs.";

    public override int Ticks => 360;

    public override Run Play(TextWriter trace)
    {
        var grid = Grid.FromMapText(Map);
        var system = new MovementSystem(grid);

        var west = grid.Index(3, 7);
        var east = grid.Index(25, 7);
        int[] route = [west, east];
        const double leash = 5.0;

        var world = new DemoWorld();

        int[] starts = [grid.Index(1, 12), grid.Index(2, 13), grid.Index(1, 14)];
        foreach (var cell in starts)
        {
            system.AddAgent(cell);
        }

        var doctrine = new PatrolDoctrine(route, leash);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);

        DemoTrace.WriteHeader(trace, Name, Description, grid, world.RepairPoints, Ticks, route, leash);

        // The lure: close enough to be worth going after, then away to the far
        // corner once the patrol has committed.
        var near = grid.Index(13, 11);
        var far = grid.Index(28, 1);

        var wasEngaged = false;
        var lastWaypoint = doctrine.CurrentWaypoint;
        var engagements = 0;
        var brokeOff = 0;
        var legs = 0;

        for (var tick = 0; tick < Ticks; tick++)
        {
            string? note = null;

            if (tick == 120)
            {
                world.HostileCells.Add(near);
                note = "a lure appears south of the route";
            }
            else if (tick == 190)
            {
                world.HostileCells.Clear();
                world.HostileCells.Add(far);
                note = "the lure withdraws to the far corner";
            }

            squad.Advance(system, world);
            system.Tick();

            var engaged = doctrine.Target >= 0;
            if (engaged && !wasEngaged)
            {
                engagements++;
                note = "the whole patrol moves to engage";
            }
            else if (!engaged && wasEngaged)
            {
                brokeOff++;
                note = "beyond the leash: the patrol breaks off";
            }
            else if (!engaged && doctrine.CurrentWaypoint != lastWaypoint)
            {
                legs++;
                note = doctrine.CurrentWaypoint == west
                    ? "waypoint reached, turning west"
                    : "waypoint reached, turning east";
            }
            else if (tick == 0)
            {
                note = "the patrol takes up its route";
            }

            wasEngaged = engaged;
            lastWaypoint = doctrine.CurrentWaypoint;

            DemoTrace.WriteTick(trace, grid, tick, system.Agents, world, doctrine.CurrentWaypoint, note);
        }

        return new Run(
            Ticks, system.Agents, world,
            $"{legs} waypoints reached, engaged {engagements}x as one body, broke off at the leash {brokeOff}x");
    }
}
