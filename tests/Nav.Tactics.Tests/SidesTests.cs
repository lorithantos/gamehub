namespace Nav.Tactics.Tests;

/// <summary>
/// Two sides in one world: each perceives the other's living units as
/// hostiles, neither perceives its own, and a doctrine written against
/// scripted threat cells is baited by a real enemy unit without changing.
/// </summary>
public sealed class SidesTests
{
    private const string Room =
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

    private static (MovementSystem System, Grid Grid) Scene(params (int X, int Y, int Side)[] at)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        foreach (var (x, y, side) in at)
        {
            system.AddAgent(grid.Index(x, y), side);
        }

        return (system, grid);
    }

    [Fact]
    public void EachSideSeesTheOtherAndNotItself()
    {
        var (system, grid) = Scene((1, 1, 0), (1, 2, 0), (20, 8, 1), (20, 9, 1));
        var world = new DemoWorld(grid);
        world.Listen(system);

        Assert.Equal([grid.Index(20, 8), grid.Index(20, 9)], world.ViewFor(0).Hostiles);
        Assert.Equal([grid.Index(1, 1), grid.Index(1, 2)], world.ViewFor(1).Hostiles);

        // The world is side 0's own view, so everything written against it
        // before there were sides still reads the same.
        Assert.Equal(world.ViewFor(0).Hostiles, world.Hostiles);
        Assert.Same(world, world.ViewFor(0));

        // A third side is at war with both.
        Assert.Equal(4, world.ViewFor(2).Hostiles.Count);
    }

    [Fact]
    public void AScriptedThreatIsEverybodysEnemy()
    {
        var (system, grid) = Scene((1, 1, 0), (20, 8, 1));
        var world = new DemoWorld(grid);
        world.Listen(system);
        var threat = grid.Index(10, 10);
        world.HostileCells.Add(threat);

        Assert.Contains(threat, world.ViewFor(0).Hostiles);
        Assert.Contains(threat, world.ViewFor(1).Hostiles);
        Assert.Equal([grid.Index(20, 8), threat], world.ViewFor(0).Hostiles);
    }

    [Fact]
    public void TheDeadStopBeingHostile()
    {
        var (system, grid) = Scene((1, 1, 0), (20, 8, 1));
        var world = new DemoWorld(grid);
        world.Listen(system);
        Assert.Contains(grid.Index(20, 8), world.ViewFor(0).Hostiles);

        system.Remove(1);
        system.Tick();
        world.Listen(system);
        world.Settle();

        Assert.Empty(world.ViewFor(0).Hostiles);
    }

    [Fact]
    public void BeforeTheFirstObservationNobodyIsSeen()
    {
        // Positions are what the world was last told, not what it guesses. A
        // demo that wants tick-0 sight calls Observe first, and this is the
        // test that says why.
        var (system, grid) = Scene((1, 1, 0), (20, 8, 1));
        var world = new DemoWorld(grid);

        Assert.Empty(world.ViewFor(0).Hostiles);

        world.Listen(system);
        Assert.Single(world.ViewFor(0).Hostiles);
    }

    [Fact]
    public void AnEnemyUnitBaitsThePatrolLikeAnyHostile()
    {
        // The same shape as the scripted-bait test in PatrolDoctrineTests,
        // with the bait a unit on the other side standing where the cell used
        // to be. PatrolDoctrine is not told about sides; it reads Hostiles.
        var (system, grid) = Scene((1, 1, 0), (1, 2, 0), (1, 3, 0), (9, 10, 1));
        var west = grid.Index(4, 6);
        var east = grid.Index(24, 6);
        var bait = grid.Index(9, 10);
        var doctrine = new PatrolDoctrine([west, east], leash: 8.0);
        var patrol = new Squad("patrol", [0, 1, 2], doctrine);
        var world = new DemoWorld(grid);
        world.Listen(system);

        for (var tick = 0; tick < 150; tick++)
        {
            patrol.Advance(system, world.ViewFor(0));
            system.Tick();
            world.Listen(system);
            world.Settle();
        }

        Assert.Equal(bait, doctrine.Target);

        // The whole patrol came, and it is standing round a unit rather than
        // on an empty cell, so nobody can be ON the bait.
        foreach (var agent in system.Agents.Where(a => a.Id < 3))
        {
            var distance = Movement.OctileDistance(
                grid.ColumnOf(agent.Cell), grid.RowOf(agent.Cell), grid.ColumnOf(bait), grid.RowOf(bait));
            Assert.True(distance <= 3.0, $"agent {agent.Id} is {distance:F1} from the bait");
            Assert.NotEqual(bait, agent.Cell);
        }
    }
}
