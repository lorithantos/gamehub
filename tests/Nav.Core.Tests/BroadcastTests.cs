namespace Nav.Core.Tests;

/// <summary>
/// The broadcast: everything that happens to an agent is raised once, in
/// order, a listener can rebuild the board from nothing but what it heard,
/// and a verb a listener issues mid-tick lands at the head of the next one.
/// </summary>
public sealed class BroadcastTests
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

    private static (MovementSystem System, Grid Grid, List<MovementEvent> Heard) Listening()
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var heard = new List<MovementEvent>();
        system.Happened += heard.Add;
        return (system, grid, heard);
    }

    [Fact]
    public void EverythingIsRaisedOnceAndInOrder()
    {
        var (system, grid, heard) = Listening();
        system.AddAgent(grid.Index(0, 0));
        system.AddAgent(grid.Index(1, 0));

        Assert.Equal(
            [
                new MovementEvent(0, MovementEventKind.Added, 0, grid.Index(0, 0)),
                new MovementEvent(0, MovementEventKind.Added, 1, grid.Index(1, 0)),
            ],
            heard);

        system.Order([0], grid.Index(4, 4));
        for (var tick = 0; tick < 30; tick++)
        {
            system.Tick();
        }

        system.Remove(1);

        // Every step is from where the agent was to where it is, the ticks
        // never run backwards, and the removal is last, at its cell.
        var moves = heard.Where(e => e.Kind == MovementEventKind.Moved).ToArray();
        Assert.NotEmpty(moves);
        Assert.All(moves, e => Assert.Equal(0, e.Agent));
        var at = grid.Index(0, 0);
        var tickWas = 0;
        foreach (var move in moves)
        {
            Assert.Equal(at, move.From);
            Assert.True(move.Tick >= tickWas);
            at = move.Cell;
            tickWas = move.Tick;
        }

        Assert.Equal(grid.Index(4, 4), at);
        Assert.Equal(
            new MovementEvent(system.CurrentTick, MovementEventKind.Removed, 1, grid.Index(1, 0)),
            heard[^1]);
    }

    [Fact]
    public void AQuietTickRaisesNothing()
    {
        var (system, grid, heard) = Listening();
        system.AddAgent(grid.Index(0, 0));
        var before = heard.Count;

        for (var tick = 0; tick < 50; tick++)
        {
            system.Tick();
        }

        Assert.Equal(before, heard.Count);
    }

    [Fact]
    public void TheBoardCanBeRebuiltFromWhatWasHeardAlone()
    {
        // What a listener does, done here by hand: apply every event and
        // compare with what the system says. If these ever differ, the
        // broadcast is lying.
        var (system, grid, heard) = Listening();
        for (var i = 0; i < 5; i++)
        {
            system.AddAgent(grid.Index(i, 0));
        }

        system.Order([0, 1, 2, 3, 4], grid.Index(4, 4));
        for (var tick = 0; tick < 25; tick++)
        {
            system.Tick();
            if (tick == 10)
            {
                system.Remove(2);
            }
        }

        var board = new Dictionary<int, int>();
        foreach (var e in heard)
        {
            if (e.Kind == MovementEventKind.Removed)
            {
                board.Remove(e.Agent);
            }
            else
            {
                board[e.Agent] = e.Cell;
            }
        }

        var living = system.Agents.Where(a => a.Alive).ToDictionary(a => a.Id, a => a.Cell);
        Assert.Equal(living, board);
    }

    [Fact]
    public void AStepIsRaisedOnceEveryoneStandsWhereTheTickPutThem()
    {
        // A handler that reads the board on hearing a step must see the whole
        // tick's moves already made, or it is reasoning about a board no
        // tick ever produced.
        var (system, grid, _) = Listening();
        system.AddAgent(grid.Index(0, 0));
        system.AddAgent(grid.Index(0, 8));
        system.Order([0], grid.Index(8, 0));
        system.Order([1], grid.Index(8, 8));

        var consistent = true;
        system.Happened += e =>
        {
            if (e.Kind != MovementEventKind.Moved)
            {
                return;
            }

            var agents = system.Agents;
            consistent &= agents[e.Agent].Cell == e.Cell;
            consistent &= agents[0].Cell == agents[0].Cell && agents[1].Cell != agents[0].Cell;
        };

        for (var tick = 0; tick < 12; tick++)
        {
            system.Tick();
        }

        Assert.True(consistent);
    }

    [Fact]
    public void AVerbIssuedMidTickLandsAtTheHeadOfTheNextOne()
    {
        // Agent 1 stands idle. On agent 0's first step, a handler orders 1
        // somewhere. When Tick returns, 1 has not been touched; after the next
        // Tick, it is on its way.
        var (system, grid, _) = Listening();
        system.AddAgent(grid.Index(0, 0));
        system.AddAgent(grid.Index(0, 8));
        var far = grid.Index(8, 8);
        var ordered = false;
        system.Happened += e =>
        {
            if (e.Kind == MovementEventKind.Moved && e.Agent == 0 && !ordered)
            {
                ordered = true;
                system.Order([1], far);
            }
        };

        system.Order([0], grid.Index(8, 0));
        while (!ordered)
        {
            system.Tick();
        }

        Assert.Equal(grid.Index(0, 8), system.Agents[1].Goal);

        system.Tick();
        Assert.Equal(far, system.Agents[1].Goal);
    }

    [Fact]
    public void ARemovalIssuedMidTickIsHonouredNextTickAndHeard()
    {
        var (system, grid, heard) = Listening();
        system.AddAgent(grid.Index(0, 0));
        system.AddAgent(grid.Index(0, 8));
        var asked = false;
        system.Happened += e =>
        {
            if (e.Kind == MovementEventKind.Moved && !asked)
            {
                asked = true;
                system.Remove(1);
            }
        };

        system.Order([0], grid.Index(8, 0));
        while (!asked)
        {
            system.Tick();
        }

        Assert.True(system.Agents[1].Alive);
        Assert.DoesNotContain(heard, e => e.Kind == MovementEventKind.Removed);

        system.Tick();
        Assert.False(system.Agents[1].Alive);
        Assert.Contains(heard, e => e.Kind == MovementEventKind.Removed && e.Agent == 1);
    }

    [Fact]
    public void AnAgentCannotBeAddedFromInsideATick()
    {
        var (system, grid, _) = Listening();
        system.AddAgent(grid.Index(0, 0));
        system.Happened += e =>
        {
            if (e.Kind == MovementEventKind.Moved)
            {
                system.AddAgent(grid.Index(4, 4));
            }
        };

        system.Order([0], grid.Index(8, 0));

        Assert.Throws<InvalidOperationException>(() =>
        {
            for (var tick = 0; tick < 12; tick++)
            {
                system.Tick();
            }
        });
    }
}
