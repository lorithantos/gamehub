namespace Nav.Tactics.Tests;

/// <summary>
/// The guard that does not die beside the cannon: it holds its station, sends
/// the damaged to repair, and takes them back.
/// </summary>
public sealed class GuardDoctrineTests
{
    private const string Room =
        """
        type octile
        height 11
        width 11
        map
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        """;

    private static (MovementSystem System, Grid Grid) Scene(int agents)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        for (var i = 0; i < agents; i++)
        {
            system.AddAgent(grid.Index(i, 0));
        }

        return (system, grid);
    }

    /// <summary>Ticks the world, running <paramref name="between"/> before each pass with the tick number.</summary>
    private static void Run(Squad squad, MovementSystem system, ScriptedWorld world, int ticks, Action<int>? between = null)
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

    [Fact]
    public void AGuardTakesItsStationAndHoldsIt()
    {
        var (system, grid) = Scene(agents: 4);
        var station = grid.Index(5, 5);
        var world = new ScriptedWorld();
        var squad = new Squad("guard", [0, 1, 2, 3], new GuardDoctrine(station));

        Run(squad, system, world, ticks: 120);

        Assert.Equal(station, squad.Anchor);
        Assert.All(system.Agents, a =>
        {
            Assert.True(a.Arrived, $"agent {a.Id} did not arrive");
            Assert.True(Distance(grid, a.Cell, station) <= 2.0, $"agent {a.Id} parked far from the station");
        });
    }

    [Fact]
    public void AHealthyGuardIsNeverSentAnywhere()
    {
        var (system, grid) = Scene(agents: 4);
        var world = new ScriptedWorld { RepairCells = { grid.Index(10, 10) } };
        var squad = new Squad("guard", [0, 1, 2, 3], new GuardDoctrine(grid.Index(5, 5)));
        var everAway = false;

        Run(squad, system, world, ticks: 200, between: _ => everAway |= system.Agents.Any(a => a.Away));

        Assert.False(everAway);
    }

    [Fact]
    public void ADamagedGuardRetreatsToRepairAndReturnsWhenRepaired()
    {
        var (system, grid) = Scene(agents: 4);
        var station = grid.Index(5, 5);
        var pad = grid.Index(10, 10);
        var world = new ScriptedWorld { RepairCells = { pad } };
        var squad = new Squad("guard", [0, 1, 2, 3], new GuardDoctrine(station, retreatBelow: 0.4, returnAbove: 0.9));

        var leftAt = -1;
        var reachedPadAt = -1;
        var backAt = -1;

        Run(squad, system, world, ticks: 400, between: tick =>
        {
            var unit = system.Agents[2];

            // Tick 60: unit 2 takes damage while on station.
            if (tick == 60)
            {
                world.Health[2] = 0.3;
            }

            if (leftAt < 0 && unit.Away)
            {
                leftAt = tick;
                Assert.Equal(pad, unit.Errand);
            }

            // Standing on the pad heals it, a little each tick.
            if (unit.Cell == pad)
            {
                reachedPadAt = reachedPadAt < 0 ? tick : reachedPadAt;
                world.Health[2] = Math.Min(1.0, world.HealthOf(2) + 0.05);
            }

            if (leftAt >= 0 && backAt < 0 && !unit.Away)
            {
                backAt = tick;
            }
        });

        Assert.True(leftAt > 60, "the damaged guard never left");
        Assert.True(reachedPadAt > leftAt, "the damaged guard never reached the pad");
        Assert.True(backAt > reachedPadAt, "the repaired guard was never brought back");

        var agents = system.Agents;
        Assert.All(agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
        Assert.True(Distance(grid, agents[2].Cell, station) <= 2.0, "the returned guard is not back at the station");
        Assert.Equal(agents.Count, agents.Select(a => a.Goal).Distinct().Count());
    }

    [Fact]
    public void AGuardBelowThresholdWithNoRepairPointStaysPut()
    {
        // Nowhere to go is not a reason to wander. It stays on station, hurt.
        var (system, grid) = Scene(agents: 3);
        var world = new ScriptedWorld { Health = { [1] = 0.1 } };
        var squad = new Squad("guard", [0, 1, 2], new GuardDoctrine(grid.Index(5, 5)));
        var everAway = false;

        Run(squad, system, world, ticks: 150, between: _ => everAway |= system.Agents.Any(a => a.Away));

        Assert.False(everAway);
        Assert.True(system.Agents[1].Arrived);
    }

    [Fact]
    public void TwoCasualtiesInOnePassGoToDifferentPads()
    {
        var (system, grid) = Scene(agents: 4);
        var padA = grid.Index(10, 10);
        var padB = grid.Index(0, 10);
        var world = new ScriptedWorld { RepairCells = { padB, padA } };
        var squad = new Squad("guard", [0, 1, 2, 3], new GuardDoctrine(grid.Index(5, 5)));

        Run(squad, system, world, ticks: 80, between: tick =>
        {
            if (tick == 50)
            {
                world.Health[1] = 0.2;
                world.Health[2] = 0.2;
            }
        });

        var agents = system.Agents;
        Assert.True(agents[1].Away && agents[2].Away);
        Assert.NotEqual(agents[1].Errand, agents[2].Errand);
        Assert.Contains(agents[1].Errand, world.RepairCells);
        Assert.Contains(agents[2].Errand, world.RepairCells);
    }

    [Fact]
    public void APlayersGroupMoveRelocatesTheGuard()
    {
        // Selecting the squad and saying "move" is an input to the movement
        // engine; the guard then holds the NEW place, and does not march back.
        var (system, grid) = Scene(agents: 3);
        var first = grid.Index(5, 5);
        var second = grid.Index(2, 8);
        var world = new ScriptedWorld();
        var squad = new Squad("guard", [0, 1, 2], new GuardDoctrine(first));

        Run(squad, system, world, ticks: 80);
        squad.MoveAll(system, second);
        Run(squad, system, world, ticks: 120);

        Assert.Equal(second, squad.Anchor);
        Assert.All(system.Agents, a =>
        {
            Assert.True(a.Arrived, $"agent {a.Id} did not arrive");
            Assert.True(Distance(grid, a.Cell, second) <= 2.0, $"agent {a.Id} is not at the new station");
        });
    }

    [Fact]
    public void TheThresholdsMustNotFlap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuardDoctrine(0, retreatBelow: 0.5, returnAbove: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuardDoctrine(0, retreatBelow: 0.6, returnAbove: 0.4));
    }
}
