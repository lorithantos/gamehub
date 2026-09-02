namespace Nav.Core.Tests;

/// <summary>
/// An order into a doorway, or down a corridor, is an order: the doorway rule
/// that keeps a ring clear of chokepoints yields to it rather than refusing it.
/// </summary>
/// <remarks>
/// Found while staging the validated-park corridor test: a one-wide corridor is
/// chokepoints end to end, the ring kept clear of every one of them, and
/// <see cref="MovementSystem.Order(IReadOnlyList{int}, int)"/> returned with
/// nothing seated, no group and no signal. The same symptom the wall-snap was
/// written to end. A squad sent to hold an entryway is the case this project
/// exists for, so the rule now gives way whenever the destination is itself a
/// doorway or the ring it leaves is too small for the group. The single-unit
/// exemption already followed the same reasoning.
/// </remarks>
public sealed class CorridorOrderTests
{
    private const string Corridor =
        """
        type octile
        height 3
        width 12
        map
        @@@@@@@@@@@@
        ............
        @@@@@@@@@@@@
        """;

    private const string TwoRooms =
        """
        type octile
        height 5
        width 11
        map
        @@@@@@@@@@@
        @....@....@
        @.........@
        @....@....@
        @@@@@@@@@@@
        """;

    private static MovementSystem Scene(Grid grid, params (int X, int Y)[] at)
    {
        // Default chokepoint detection, deliberately: the rule under test is
        // the one that runs when the map HAS doorways.
        var system = new MovementSystem(grid);
        foreach (var (x, y) in at)
        {
            system.AddAgent(grid.Index(x, y));
        }

        return system;
    }

    private static void Run(MovementSystem system, int ticks)
    {
        for (var tick = 0; tick < ticks && !system.Agents.All(a => a.Arrived); tick++)
        {
            system.Tick();
        }
    }

    [Fact]
    public void AGroupOrderedDownACorridorIsSeatedInIt()
    {
        var grid = Grid.FromMapText(Corridor);
        var system = Scene(grid, (1, 1), (2, 1), (3, 1));

        system.Order([0, 1, 2], grid.Index(10, 1));
        Run(system, ticks: 80);

        Assert.All(system.Agents, a => Assert.True(a.Arrived, $"agent {a.Id} never arrived"));
        Assert.Equal(3, system.Agents.Select(a => a.Cell).Distinct().Count());
        Assert.All(system.Agents, a => Assert.Equal(1, grid.RowOf(a.Cell)));
        Assert.Contains(system.Agents, a => a.Cell == grid.Index(10, 1));
    }

    [Fact]
    public void AGroupOrderedOntoADoorwayHoldsTheDoorway()
    {
        // The destination is the gap between the rooms. Left to the doorway
        // rule, the ring would be built beside it in one room or the other and
        // the squad sent to hold the door would stand next to it instead.
        var grid = Grid.FromMapText(TwoRooms);
        var door = grid.Index(5, 2);
        var system = Scene(grid, (1, 1), (1, 2), (1, 3));

        system.Order([0, 1, 2], door);
        Run(system, ticks: 80);

        Assert.All(system.Agents, a => Assert.True(a.Arrived, $"agent {a.Id} never arrived"));
        Assert.Contains(system.Agents, a => a.Cell == door);
    }
}
