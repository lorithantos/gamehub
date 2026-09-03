namespace Nav.Tactics.Tests;

/// <summary>
/// A squad that has taken losses: the seam stops listing the dead, the reserve
/// counts the living, and a group move leaves the fallen where they fell.
/// </summary>
public sealed class CasualtyTests
{
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

    /// <summary>Records what the seam listed on the pass named, and moves on tick 0.</summary>
    private sealed class Probe(int station, int onTick) : SquadDoctrine
    {
        public IReadOnlyList<int>? Members { get; private set; }
        public IReadOnlyList<int>? Away { get; private set; }

        public override void Advance(ISquadOps ops)
        {
            if (ops.CurrentTick == 0)
            {
                ops.MoveAll(station);
            }

            if (ops.CurrentTick == onTick)
            {
                Members = ops.Members;
                Away = ops.Away;
            }
        }
    }

    [Fact]
    public void TheSeamStopsListingTheDead()
    {
        var (system, grid) = Scene(agents: 4);
        var probe = new Probe(grid.Index(4, 4), onTick: 5);
        var squad = new Squad("1", [0, 1, 2, 3], probe);

        for (var tick = 0; tick < 5; tick++)
        {
            squad.Advance(system);
            system.Tick();
        }

        system.Remove(2);
        squad.Advance(system);

        // Still a member of the squad -- membership is the player's list, and
        // the fallen are not struck off it -- but not somebody a doctrine can
        // act on, so not listed.
        Assert.Contains(2, squad.Members);
        Assert.Equal([0, 1, 3], probe.Members);
        Assert.DoesNotContain(2, probe.Away!);
    }

    [Fact]
    public void TheReserveCountsTheLiving()
    {
        // Four guards, a reserve of three, and one of them dead. That leaves
        // exactly three standing, so the hurt survivor must hold: sending it
        // would leave two, and the reserve says three however hurt anybody is.
        var (system, grid) = Scene(agents: 4);
        var pad = grid.Index(8, 8);
        var world = new DemoWorld(grid);
        world.RepairCells.Add(pad);

        var squad = new Squad(
            "guard", [0, 1, 2, 3],
            new GuardDoctrine(grid.Index(4, 4), new RepairPolicy([0.5], returnAbove: 0.9, reserve: 3)));

        for (var tick = 0; tick < 40; tick++)
        {
            squad.Advance(system, world);
            system.Tick();
        }

        system.Remove(2);
        world.SetHealth(1, 0.2);

        for (var tick = 0; tick < 40; tick++)
        {
            squad.Advance(system, world);
            system.Tick();
        }

        var agents = system.Agents;
        Assert.False(agents[1].Away, "the reserve counted a corpse as standing");
        Assert.False(agents[2].Away);
        Assert.Equal(-1, agents[2].Errand);
    }

    [Fact]
    public void AGroupMoveLeavesTheDeadWhereTheyFell()
    {
        var (system, grid) = Scene(agents: 4);
        var first = grid.Index(4, 4);
        var second = grid.Index(1, 7);
        var squad = new Squad("1", [0, 1, 2, 3], new Probe(first, onTick: 0));

        for (var tick = 0; tick < 30; tick++)
        {
            squad.Advance(system);
            system.Tick();
        }

        var fell = system.Agents[3].Cell;
        system.Remove(3);
        squad.MoveAll(system, second);

        for (var tick = 0; tick < 100; tick++)
        {
            squad.Advance(system);
            system.Tick();
        }

        var agents = system.Agents;
        Assert.Equal(second, squad.Anchor);
        Assert.Equal(fell, agents[3].Cell);
        foreach (var id in new[] { 0, 1, 2 })
        {
            Assert.True(agents[id].Arrived, $"agent {id} did not arrive");
            Assert.NotEqual(fell, agents[id].Cell);
        }
    }
}
