namespace Nav.Tactics.Tests;

/// <summary>
/// The patrol that cannot be baited: it walks its route, engages what comes
/// close to it, breaks off when the target runs, and never sends one unit alone.
/// </summary>
public sealed class PatrolDoctrineTests
{
    private const string Corridor =
        """
        type octile
        height 13
        width 29
        map
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        """;

    private static (MovementSystem System, Grid Grid) Scene(int agents)
    {
        var grid = Grid.FromMapText(Corridor);
        var system = new MovementSystem(grid);
        for (var i = 0; i < agents; i++)
        {
            system.AddAgent(grid.Index(1, 1 + i));
        }

        return (system, grid);
    }

    private static void Run(Squad squad, MovementSystem system, DemoWorld world, int ticks, Action<int>? between = null)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            between?.Invoke(system.CurrentTick);
            squad.Advance(system, world);
            system.Tick();
        }
    }

    private static double Distance(Grid grid, int a, int b) =>
        Movement.OctileDistance(grid.ColumnOf(a), grid.RowOf(a), grid.ColumnOf(b), grid.RowOf(b));

    /// <summary>How far the furthest on-station unit is from a cell.</summary>
    private static double Spread(MovementSystem system, Grid grid, int cell) =>
        system.Agents.Where(a => !a.Away).Max(a => Distance(grid, a.Cell, cell));

    [Fact]
    public void APatrolWalksItsRouteAndComesBackToTheStart()
    {
        var (system, grid) = Scene(agents: 3);
        var west = grid.Index(4, 6);
        var east = grid.Index(24, 6);
        var doctrine = new PatrolDoctrine([west, east]);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);
        var world = new DemoWorld();

        var visited = new List<int>();
        Run(squad, system, world, ticks: 260, between: _ =>
        {
            if (visited.Count == 0 || visited[^1] != doctrine.CurrentWaypoint)
            {
                visited.Add(doctrine.CurrentWaypoint);
            }
        });

        // Started west, reached it, turned for east, reached it, turned back.
        Assert.Equal([west, east, west], visited.Take(3));
    }

    [Fact]
    public void AHostileInsideTheLeashPullsTheWholePatrol()
    {
        var (system, grid) = Scene(agents: 3);
        var west = grid.Index(4, 6);
        var east = grid.Index(24, 6);
        var bait = grid.Index(9, 10);
        var doctrine = new PatrolDoctrine([west, east], leash: 8.0);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);
        var world = new DemoWorld();

        Run(squad, system, world, ticks: 60);
        world.HostileCells.Add(bait);
        Run(squad, system, world, ticks: 90);

        Assert.Equal(bait, doctrine.Target);

        // EVERY unit went, not one. The whole patrol is parked around the bait.
        Assert.True(Spread(system, grid, bait) <= 2.5, "the patrol did not converge on the target as one body");
        Assert.All(system.Agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
    }

    [Fact]
    public void AHostileBeyondTheLeashIsSomebodyElsesProblem()
    {
        var (system, grid) = Scene(agents: 3);
        var west = grid.Index(4, 6);
        var east = grid.Index(24, 6);
        var doctrine = new PatrolDoctrine([west, east], leash: 5.0);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);
        var world = new DemoWorld();

        // Far off the route, and it stays there.
        world.HostileCells.Add(grid.Index(27, 12));

        Run(squad, system, world, ticks: 120);

        Assert.Equal(-1, doctrine.Target);
    }

    [Fact]
    public void BaitThatWithdrawsPastTheLeashIsBrokenOffFrom()
    {
        // The bait. It shows up beside the route, the patrol comes for it, and
        // then it runs. Past the leash it stops being a target and the patrol
        // returns to its waypoint rather than being walked off the map.
        var (system, grid) = Scene(agents: 3);
        var west = grid.Index(4, 6);
        var east = grid.Index(24, 6);
        var doctrine = new PatrolDoctrine([west, east], leash: 5.0);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);
        var world = new DemoWorld();

        var lure = grid.Index(9, 9);
        world.HostileCells.Add(lure);

        var engaged = false;
        var backOnRoute = false;
        var corner = grid.Index(27, 0);
        var closestToCorner = double.PositiveInfinity;

        Run(squad, system, world, ticks: 240, between: tick =>
        {
            engaged |= doctrine.Target >= 0;

            // Checked every tick, not at the end. Where the patrol happens to
            // stand on its last tick says nothing: the east post is 7.2 from
            // the corner, so a patrol correctly walking its route is sometimes
            // closer to the lure than one that chased it. Being drawn off
            // means going THERE, and that is what this watches for.
            foreach (var agent in system.Agents.Where(a => !a.Away))
            {
                closestToCorner = Math.Min(closestToCorner, Distance(grid, agent.Cell, corner));
            }

            // Once they have committed, the lure withdraws to the far corner.
            if (tick == 90)
            {
                world.HostileCells.Clear();
                world.HostileCells.Add(grid.Index(27, 0));
            }

            // Standing on a waypoint again, with nothing engaged: the route
            // resumed. Checked as it happens rather than at the end, because a
            // patrol that has resumed is usually in transit to the NEXT
            // waypoint and so legitimately far from it.
            if (tick > 90 && doctrine.Target < 0 && Spread(system, grid, doctrine.CurrentWaypoint) <= 2.5)
            {
                backOnRoute = true;
            }
        });

        Assert.True(engaged, "the patrol never took the bait, so the test proves nothing");
        Assert.Equal(-1, doctrine.Target);
        Assert.True(backOnRoute, "the patrol never got back to its route");

        // And no unit ever went to the corner the lure ran to. Four cells is
        // comfortably outside the route's own closest approach to it.
        Assert.True(
            closestToCorner > 4.0,
            $"the patrol was walked off after the bait: a unit came within {closestToCorner:F1} of the corner");
    }

    [Fact]
    public void AMemberAwayOnAnErrandIsNotDraggedIntoTheFight()
    {
        // The two rules have to compose: a unit still repairing at the pad
        // stays there while the rest of its patrol engages. Still repairing --
        // at half health, below the return threshold -- because a patrol now
        // carries the repair policy, and a unit at the pad at FULL health is
        // exactly what that policy brings back.
        var (system, grid) = Scene(agents: 4);
        var west = grid.Index(4, 6);
        var east = grid.Index(24, 6);
        var pad = grid.Index(1, 12);
        var doctrine = new PatrolDoctrine([west, east], leash: 8.0);
        var squad = new Squad("patrol", [0, 1, 2, 3], doctrine);
        var world = new DemoWorld();
        world.RepairCells.Add(pad);

        Run(squad, system, world, ticks: 40);
        world.SetHealth(3, 0.5);
        system.Dispatch(3, pad);
        Run(squad, system, world, ticks: 40);

        world.HostileCells.Add(grid.Index(9, 9));
        Run(squad, system, world, ticks: 90);

        Assert.True(system.Agents[3].Away, "the errand was cancelled by the engagement");
        Assert.Equal(pad, system.Agents[3].Cell);
        Assert.True(doctrine.Target >= 0, "the rest of the patrol did not engage");
    }

    [Fact]
    public void ARouteNeedsMoreThanOneWaypoint()
    {
        Assert.Throws<ArgumentException>(() => new PatrolDoctrine([5]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatrolDoctrine([5, 9], leash: 0));
    }
}
