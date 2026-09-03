namespace Nav.Core.Tests;

/// <summary>
/// An agent leaving the world for good: its cell is free the same tick, its
/// id keeps meaning what it meant, and every verb refuses it afterwards.
/// </summary>
/// <remarks>
/// Death is the one thing the tactical layer will do to the movement layer
/// that it cannot undo, so what is pinned here is that it changes nothing
/// except the one unit. The living finish their orders exactly as if the
/// casualty had never been ordered with them.
/// </remarks>
public sealed class RemoveTests
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

    private static void TickUntil(MovementSystem system, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++)
        {
            system.Tick();
        }
    }

    [Fact]
    public void ARemovedAgentFreesItsCellAndKeepsItsId()
    {
        // Agent 0 stands parked on (0,0) from the moment it was added, so
        // nobody else can arrive there. Remove it and the next unit can.
        var (system, grid) = Scene(agents: 2);
        var cell = grid.Index(0, 0);

        system.Remove(0);
        system.Order([1], cell);
        TickUntil(system, () => system.Agents[1].Cell == cell, limit: 20);

        var agents = system.Agents;
        Assert.Equal(cell, agents[1].Cell);
        Assert.Equal(2, agents.Count);
        Assert.False(agents[0].Alive);
        Assert.True(agents[1].Alive);

        // The id is spent, not recycled: a later arrival is 2, never 0.
        Assert.Equal(2, system.AddAgent(grid.Index(4, 4)));
    }

    [Fact]
    public void TheLivingFinishAnOrderTheirCasualtyWasPartOf()
    {
        var (system, grid) = Scene(agents: 4);
        system.Order([0, 1, 2, 3], grid.Index(4, 4));
        for (var tick = 0; tick < 6; tick++)
        {
            system.Tick();
        }

        var where = system.Agents[1].Cell;
        system.Remove(1);

        TickUntil(system, () => system.Agents.Where(a => a.Alive).All(a => a.Arrived), limit: 150);

        var agents = system.Agents;
        foreach (var id in new[] { 0, 2, 3 })
        {
            Assert.True(agents[id].Arrived, $"agent {id} did not arrive");
        }

        // The dead one is where it fell, wants nothing, and holds no slot in
        // the ring: no two of the living share a goal, and none of them is
        // aimed at the corpse's cell.
        Assert.Equal(where, agents[1].Cell);
        Assert.Equal(where, agents[1].Goal);
        var goals = agents.Where(a => a.Alive).Select(a => a.Goal).ToArray();
        Assert.Equal(3, goals.Distinct().Count());
        Assert.DoesNotContain(1, system.Leaders);
    }

    [Fact]
    public void AVerbOnARemovedAgentIsRefusedRatherThanMovingACorpse()
    {
        var (system, grid) = Scene(agents: 3);
        system.Order([0, 1, 2], grid.Index(4, 4));
        system.Tick();

        system.Remove(0);

        Assert.Throws<InvalidOperationException>(() => system.Order([0], grid.Index(8, 8)));
        Assert.Throws<InvalidOperationException>(() => system.Order([0, 1], grid.Index(8, 8)));
        Assert.Throws<InvalidOperationException>(() => system.Dispatch(0, grid.Index(8, 8)));
        Assert.Throws<InvalidOperationException>(() => system.Recall(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Remove(9));

        // A refused order touched nobody: 1 and 2 are still on the first one.
        Assert.Equal(grid.Index(4, 4), system.Agents[1].Goal);
        Assert.Equal(grid.Index(4, 4), system.Agents[2].Goal);
    }

    [Fact]
    public void RemovingTwiceChangesNothing()
    {
        var (system, grid) = Scene(agents: 2);
        system.Order([0, 1], grid.Index(4, 4));
        system.Tick();

        system.Remove(0);
        var once = system.Agents;
        system.Remove(0);
        system.Tick();

        Assert.Equal(once[0], system.Agents[0]);
    }

    [Fact]
    public void ACellSomebodyDiedOnCanBeSteppedIntoTheSameTick()
    {
        // Agent 1 wants to cross (0,0), which agent 0 is parked on. The route
        // plans round it. Once 0 is removed the cell reads free at once, and a
        // fresh order from beside it lands there in one step.
        var (system, grid) = Scene(agents: 2);
        var cell = grid.Index(0, 0);

        system.Remove(0);
        system.Order([1], cell);
        system.Tick();
        TickUntil(system, () => system.Agents[1].Cell == cell, limit: 12);

        Assert.Equal(cell, system.Agents[1].Cell);
    }
}
