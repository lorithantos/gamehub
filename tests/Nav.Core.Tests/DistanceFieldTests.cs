using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Milestone 3 criterion 1: fields are exact, checked cell by cell against
/// milestone 1's verified A* — the borrow-the-verified-thing move, third use.
/// </summary>
public sealed class DistanceFieldTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every passable destination, every passable cell, on every small committed
    /// map: the field's answer IS the search's answer.
    /// </summary>
    [Theory]
    [InlineData("corridor.map")]
    [InlineData("gap.map")]
    [InlineData("hall.map")]
    [InlineData("crosscut.map")]
    [InlineData("empty-8-8.map")]
    public void TheFieldMatchesTheVerifiedSearchEverywhere(string map)
    {
        var grid = Grid.FromMapFile(Fixtures.Map(map));

        var checkedCells = 0;
        for (var destination = 0; destination < grid.CellCount; destination++)
        {
            if (!grid.IsPassable(destination))
            {
                continue;
            }

            var field = DistanceField.Build(grid, destination);
            for (var cell = 0; cell < grid.CellCount; cell++)
            {
                if (!grid.IsPassable(cell))
                {
                    continue;
                }

                var path = PathFinder.FindPath(grid, cell, destination);
                Assert.Equal(path.Found, field.Reaches(cell));
                if (path.Found)
                {
                    Assert.True(
                        Math.Abs(path.Cost - field.CostFrom(cell)) <= 1e-6,
                        $"{map}: field says {field.CostFrom(cell):F9} from cell {cell} " +
                        $"to {destination}, search says {path.Cost:F9}");
                }

                checkedCells++;
            }
        }

        output.WriteLine($"{map}: {checkedCells:N0} (cell, destination) pairs agree");
    }

    /// <summary>
    /// Arena is 49x49, so destinations are strided rather than exhaustive; every
    /// passable cell is still checked against each sampled destination.
    /// </summary>
    [Fact]
    public void TheFieldMatchesTheVerifiedSearchOnArena()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));

        var passable = Enumerable.Range(0, grid.CellCount).Where(cell => grid.IsPassable(cell)).ToArray();
        var destinations = passable.Where((_, i) => i % 97 == 0).ToArray();

        var checkedCells = 0;
        foreach (var destination in destinations)
        {
            var field = DistanceField.Build(grid, destination);
            foreach (var cell in passable)
            {
                var path = PathFinder.FindPath(grid, cell, destination);
                Assert.Equal(path.Found, field.Reaches(cell));
                if (path.Found)
                {
                    Assert.True(
                        Math.Abs(path.Cost - field.CostFrom(cell)) <= 1e-6,
                        $"arena: field {field.CostFrom(cell):F9} vs search {path.Cost:F9} " +
                        $"from {cell} to {destination}");
                }

                checkedCells++;
            }
        }

        output.WriteLine(
            $"arena: {destinations.Length} strided destinations, {checkedCells:N0} pairs agree");
    }

    [Fact]
    public void AWalledOffCellIsUnreachableInBothDirections()
    {
        // gap.map's two chambers connect only through (6,4); block nothing and
        // everything reaches. The unreachable case needs a genuinely split map.
        var grid = Grid.FromMapText(
            """
            type octile
            height 3
            width 7
            map
            @@@@@@@
            @.@.@.@
            @@@@@@@
            """);

        var field = DistanceField.Build(grid, grid.Index(1, 1));

        Assert.True(field.Reaches(grid.Index(1, 1)));
        Assert.False(field.Reaches(grid.Index(3, 1)));
        Assert.False(field.Reaches(grid.Index(5, 1)));
        Assert.True(double.IsPositiveInfinity(field.CostFrom(grid.Index(3, 1))));
    }

    [Fact]
    public void AnImpassableDestinationIsRefused()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("gap.map"));

        Assert.Throws<ArgumentOutOfRangeException>(() => DistanceField.Build(grid, grid.Index(0, 0)));
    }

    /// <summary>
    /// Criterion 2: field guidance changes cost, not answers. Every found plan
    /// has the identical cost either way; expansions can only shrink or hold.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT asserted: cell-for-cell path identity. Two consistent
    /// heuristics may break ties between equal-cost optimal paths differently,
    /// so the guarantee is cost and reachability, and the suite's legality and
    /// collision checks hold whichever route is chosen.
    /// </remarks>
    [Theory]
    [InlineData("gap.map")]
    [InlineData("crosscut.map")]
    [InlineData("empty-8-8.map")]
    public void FieldGuidanceChangesCostNotAnswers(string map)
    {
        var grid = Grid.FromMapFile(Fixtures.Map(map));
        var passable = Enumerable.Range(0, grid.CellCount).Where(cell => grid.IsPassable(cell)).ToArray();
        var workspace = new SearchWorkspace();

        long octileTotal = 0;
        long fieldTotal = 0;
        var plansChecked = 0;

        // Every 5th passable start against every 11th passable goal keeps the
        // sweep broad without taking minutes.
        foreach (var start in passable.Where((_, i) => i % 5 == 0))
        {
            foreach (var goal in passable.Where((_, i) => i % 11 == 0))
            {
                var field = DistanceField.Build(grid, goal);

                var plain = CooperativePlanner.FindPlan(
                    grid, new ReservationTable(grid.CellCount, 32), 0, start, goal, 0, workspace);
                var guided = CooperativePlanner.FindPlan(
                    grid, new ReservationTable(grid.CellCount, 32), 0, start, goal, 0, workspace, field);

                Assert.Equal(plain.Found, guided.Found);
                if (plain.Found)
                {
                    Assert.True(
                        Math.Abs(plain.Cost - guided.Cost) <= 1e-9,
                        $"{map}: {start}->{goal} octile {plain.Cost:F9} vs field {guided.Cost:F9}");
                }

                octileTotal += plain.Expanded;
                fieldTotal += guided.Expanded;
                plansChecked++;
            }
        }

        output.WriteLine(
            $"{map}: {plansChecked} plans; expansions octile {octileTotal:N0} vs field {fieldTotal:N0} " +
            $"({(double)octileTotal / fieldTotal:F2}x)");

        Assert.True(fieldTotal <= octileTotal, "the dominant heuristic expanded more overall");
    }

    [Fact]
    public void TheCacheSharesAndEvictsDeterministically()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("hall.map"));
        var cache = new FieldCache(grid, capacity: 2);

        var d1 = grid.Index(1, 1);
        var d2 = grid.Index(10, 10);
        var d3 = grid.Index(5, 5);

        var first = cache.For(d1);
        var second = cache.For(d2);

        // A repeat is the same instance: that is the sharing the type exists for.
        Assert.Same(first, cache.For(d1));

        // d1 was just touched, so d2 is the least recent and is the one evicted.
        cache.For(d3);
        Assert.Equal(2, cache.Count);
        Assert.NotSame(second, cache.For(d2));   // rebuilt: it had been evicted
    }
}
