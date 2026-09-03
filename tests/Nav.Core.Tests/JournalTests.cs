namespace Nav.Core.Tests;

/// <summary>
/// The broadcast: everything that happens to an agent is written once, in
/// order, and a reader can rebuild the board from nothing but the journal.
/// </summary>
public sealed class JournalTests
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

    [Fact]
    public void TheJournalSaysWhatHappenedAndInWhatOrder()
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        system.AddAgent(grid.Index(0, 0));
        system.AddAgent(grid.Index(1, 0));

        Assert.Equal(
            [
                new MovementEvent(0, MovementEventKind.Added, 0, grid.Index(0, 0)),
                new MovementEvent(0, MovementEventKind.Added, 1, grid.Index(1, 0)),
            ],
            system.Journal);

        system.Order([0], grid.Index(4, 4));
        for (var tick = 0; tick < 30; tick++)
        {
            system.Tick();
        }

        system.Remove(1);

        // Every move is a step from where the agent was to where it is, the
        // ticks never run backwards, and the removal is last, at its cell.
        var moves = system.Journal.Where(e => e.Kind == MovementEventKind.Moved).ToArray();
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
            system.Journal[^1]);
    }

    [Fact]
    public void AQuietTickWritesNothing()
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        system.AddAgent(grid.Index(0, 0));
        var before = system.Journal.Count;

        for (var tick = 0; tick < 50; tick++)
        {
            system.Tick();
        }

        Assert.Equal(before, system.Journal.Count);
    }

    [Fact]
    public void TheBoardCanBeRebuiltFromTheJournalAlone()
    {
        // What a reader with a cursor does, done here by hand: apply every
        // event and compare with what the system says. If these ever differ,
        // the broadcast is lying.
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
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
        foreach (var e in system.Journal)
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
}
