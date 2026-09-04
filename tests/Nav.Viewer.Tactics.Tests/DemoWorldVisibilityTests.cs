namespace Nav.Viewer.Tactics.Tests;

/// <summary>
/// The tactics half of the fog seam: a world's per-side knowledge as cells,
/// sides and three ints per memory, with nothing from Nav.Tactics on the way
/// out.
/// </summary>
/// <remarks>
/// What the viewer's own tests cannot reach. They drive the app against a fake
/// built on Nav.Core alone, because the viewer project is compiled with no sight
/// of a world at all -- so the question "does side 0 actually see that cell"
/// can only be asked here, where both halves of the seam are visible at once.
/// </remarks>
public sealed class DemoWorldVisibilityTests
{
    private static string ConfigDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config");

    private static Combat Shipped() => Combat.From(Ini.FromFile(Path.Combine(ConfigDir, "combat.ini")));

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

    /// <summary>
    /// Two riflemen four cells apart on opposite sides, and one pad. A rifleman
    /// sees six, so the two watch each other and most of the room is dark.
    /// </summary>
    private static (Grid Grid, DemoWorld World) Scene(bool fog = true)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(
            grid,
            repairPerTick: 0.02,
            exposureRadius: 4.0,
            combat: Shipped(),
            fog: fog);

        world.RepairCells.Add(grid.Index(10, 4));
        world.Enlist(system.AddAgent(grid.Index(10, 6), 0), "rifleman");
        world.Enlist(system.AddAgent(grid.Index(14, 6), 1), "rifleman");

        world.Listen(system);
        return (grid, world);
    }

    [Fact]
    public void WhatASideCanSeeIsGroundAndNotContacts()
    {
        var (grid, world) = Scene();
        var eyes = new DemoWorldVisibility(world);

        var empty = grid.Index(12, 6);
        var far = grid.Index(25, 11);
        var visible = eyes.VisibleCells(0);

        // The difference fog is made of. A cell two steps away with nothing
        // standing in it is GROUND SIDE 0 HAS SWEPT, and no contact list can say
        // so -- an empty hostiles list means "found nothing" whether the side
        // looked or not.
        Assert.Contains(empty, visible);
        Assert.DoesNotContain(empty, world.View.PeekHostiles(0));

        // And what nobody is looking at is in neither.
        Assert.DoesNotContain(far, visible);
        Assert.DoesNotContain(far, world.View.PeekHostiles(0));
    }

    [Fact]
    public void ASideAlwaysSeesTheGroundItsOwnUnitsStandOn()
    {
        var (grid, world) = Scene();
        var eyes = new DemoWorldVisibility(world);

        // Otherwise a viewer drawing through a side's eyes would fog out that
        // side's own roster, and the picture would be unreadable exactly where
        // it has to be readable.
        Assert.Contains(grid.Index(10, 6), eyes.VisibleCells(0));
        Assert.Contains(grid.Index(14, 6), eyes.VisibleCells(1));
    }

    [Fact]
    public void TheVisibleSetIsAscendingAndWithoutRepeats()
    {
        var (_, world) = Scene();
        var visible = new DemoWorldVisibility(world).VisibleCells(0);

        // The viewer compares two of these element by element to decide whether
        // the fog it drew is still the right picture, so an answer that came
        // back in a different order for the same board would be a texture
        // upload every frame.
        Assert.NotEmpty(visible);
        for (var i = 1; i < visible.Count; i++)
        {
            Assert.True(visible[i - 1] < visible[i], $"cell {visible[i]} came after {visible[i - 1]}");
        }
    }

    [Fact]
    public void AMemoryCarriesTheCellAndTheTickTheSightingWasTakenOn()
    {
        var (grid, world) = Scene();
        var remembered = new DemoWorldVisibility(world).Remembered(0);

        // The whole value of a ghost is that it is a MEMORY: where the enemy was
        // and when, so a watcher can see a side acting on a picture that has
        // stopped being true.
        var ghost = Assert.Single(remembered);
        Assert.Equal(1, ghost.Agent);
        Assert.Equal(grid.Index(14, 6), ghost.Cell);
        Assert.Equal(world.View.AsOf, ghost.Tick);
    }

    [Fact]
    public void TheSidesAreWhoeverHasAUnitOnTheBoard()
    {
        var (_, world) = Scene();

        // Read off the roster rather than counted: the viewer cycles through
        // these in order, so a side missing here is a side nobody can look
        // through.
        Assert.Equal([0, 1], new DemoWorldVisibility(world).Sides);
    }

    [Fact]
    public void AWorldWithoutFogHidesNoGroundFromAnybody()
    {
        var (grid, world) = Scene(fog: false);
        var eyes = new DemoWorldVisibility(world);

        // A world that tells every side about every unit is not keeping the
        // ground back either, so the fog image it produces dims nothing.
        Assert.Equal(grid.CellCount, eyes.VisibleCells(0).Count);
        Assert.Equal(grid.CellCount, eyes.VisibleCells(1).Count);
    }

    [Fact]
    public void ASideNobodyFightsForSeesNothingRatherThanThrowing()
    {
        var (_, world) = Scene();
        var eyes = new DemoWorldVisibility(world);

        // An instrument pointed at a side that does not exist should say so, not
        // throw at the viewer -- the same rule every other view here keeps.
        Assert.Empty(eyes.VisibleCells(7));
        Assert.Empty(eyes.Remembered(7));
    }
}
