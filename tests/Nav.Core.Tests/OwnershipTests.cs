namespace Nav.Core.Tests;

/// <summary>
/// Two sides on one grid: each plans against its own side's booked future and
/// sees the other only as the ground it stands on, so enemies advance until
/// they meet instead of yielding to each other's intentions, a line of bodies
/// holds, and nobody ever shares a cell or passes through anybody.
/// </summary>
public sealed class OwnershipTests
{
    /// <summary>One cell wide, nine long.</summary>
    private const string Lane =
        """
        type octile
        height 3
        width 9
        map
        @@@@@@@@@
        .........
        @@@@@@@@@
        """;

    /// <summary>Three cells wide, nine long.</summary>
    private const string Corridor =
        """
        type octile
        height 5
        width 9
        map
        @@@@@@@@@
        .........
        .........
        .........
        @@@@@@@@@
        """;

    private const string Room =
        """
        type octile
        height 9
        width 9
        map
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        """;

    private static void Run(MovementSystem system, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            system.Tick();
        }
    }

    [Fact]
    public void EnemiesAdvanceUntilTheyMeetRatherThanYieldingToEachOthersPlans()
    {
        // Head on in a lane. One commander: the loser plans against the
        // winner's whole route, sees it cannot get by, and stands at its start
        // from tick one. Two commanders: neither can read the other's route,
        // so both walk until they are nose to nose, and there they stop.
        var grid = Grid.FromMapText(Lane);
        var system = new MovementSystem(grid);
        var guard = system.AddAgent(grid.Index(0, 1), side: 0);
        var attacker = system.AddAgent(grid.Index(8, 1), side: 1);
        system.Order([guard], grid.Index(8, 1));
        system.Order([attacker], grid.Index(0, 1));

        Run(system, ticks: 40);

        var agents = system.Agents;
        var gx = grid.ColumnOf(agents[guard].Cell);
        var ax = grid.ColumnOf(agents[attacker].Cell);
        Assert.True(gx > 0, "the guard never moved");
        Assert.True(ax < 8, "the attacker never moved");
        Assert.True(gx < ax, "somebody passed through somebody");
        Assert.Equal(1, ax - gx);
    }

    [Fact]
    public void OneCommandersHeadOnStillYieldsAtOnce()
    {
        // The same lane, both units on one side: the cooperative answer is
        // unchanged. One of them stands at its start and the other walks up
        // to it, which is what every recorded scenario was made with.
        var grid = Grid.FromMapText(Lane);
        var system = new MovementSystem(grid);
        system.AddAgent(grid.Index(0, 1));
        system.AddAgent(grid.Index(8, 1));
        system.Order([0], grid.Index(8, 1));
        system.Order([1], grid.Index(0, 1));

        Run(system, ticks: 40);

        var agents = system.Agents;
        var stayed = agents.Count(a => a.Cell == grid.Index(grid.ColumnOf(a.Id == 0 ? grid.Index(0, 1) : grid.Index(8, 1)), 1));
        Assert.Equal(1, stayed);
    }

    [Fact]
    public void ALineOfBodiesHoldsACorridor()
    {
        // Three guards stand across the corridor and never move. Three
        // attackers are ordered straight through them. Nobody gets past:
        // the guards' cells are ground the attackers cannot plan through, and
        // there is no other ground.
        var grid = Grid.FromMapText(Corridor);
        var system = new MovementSystem(grid);
        for (var row = 1; row <= 3; row++)
        {
            system.AddAgent(grid.Index(4, row), side: 0);
        }

        var attackers = new List<int>();
        for (var row = 1; row <= 3; row++)
        {
            attackers.Add(system.AddAgent(grid.Index(8, row), side: 1));
        }

        for (var i = 0; i < attackers.Count; i++)
        {
            system.Order([attackers[i]], grid.Index(0, 1 + i));
        }

        Run(system, ticks: 150);

        var agents = system.Agents;
        foreach (var id in attackers)
        {
            Assert.True(grid.ColumnOf(agents[id].Cell) > 4, $"attacker {id} got past the line");
        }
    }

    [Fact]
    public void NoTwoUnitsOfAnySideShareACellOrPassThroughEachOther()
    {
        // Four each way across an open room, every unit on its own order so
        // nobody is anybody's follower. The two guarantees one table gives
        // one side must hold between sides too, and they hold at the step.
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        for (var row = 1; row <= 4; row++)
        {
            system.AddAgent(grid.Index(0, row), side: 0);
        }

        for (var row = 1; row <= 4; row++)
        {
            system.AddAgent(grid.Index(8, row), side: 1);
        }

        for (var row = 1; row <= 4; row++)
        {
            system.Order([row - 1], grid.Index(8, row));
            system.Order([row + 3], grid.Index(0, row));
        }

        var before = system.Agents.Select(a => a.Cell).ToArray();
        for (var tick = 0; tick < 120; tick++)
        {
            system.Tick();
            var now = system.Agents.Select(a => a.Cell).ToArray();

            Assert.Equal(now.Length, now.Distinct().Count());
            for (var i = 0; i < now.Length; i++)
            {
                for (var j = i + 1; j < now.Length; j++)
                {
                    Assert.False(
                        now[i] == before[j] && now[j] == before[i] && now[i] != before[i],
                        $"agents {i} and {j} passed through each other at tick {tick}");
                }
            }

            before = now;
        }

        // And they all got where they were going: the standoffs resolved.
        Assert.All(system.Agents, a => Assert.True(a.Arrived, $"agent {a.Id} never arrived"));
    }

    [Fact]
    public void TheSideIsFixedForLifeAndReportedEverywhere()
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var heard = new List<MovementEvent>();
        system.Happened += heard.Add;

        system.AddAgent(grid.Index(0, 0), side: 2);
        system.AddAgent(grid.Index(1, 0));

        Assert.Equal(2, system.Agents[0].Side);
        Assert.Equal(0, system.Agents[1].Side);
        Assert.Equal(2, heard[0].Side);
        Assert.Equal(0, heard[1].Side);
        Assert.Throws<ArgumentOutOfRangeException>(() => system.AddAgent(grid.Index(2, 0), side: -1));
    }
}
