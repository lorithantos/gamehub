namespace Nav.Core.Tests;

/// <summary>
/// Acceptance criteria 3, 6 and 7.
/// </summary>
public sealed class PathFinderTests
{
    private const double Tolerance = 1e-6;

    /// <summary>Column 2 is a solid wall, so the two halves cannot reach each other.</summary>
    private const string Severed =
        """
        type octile
        height 3
        width 5
        map
        ..@..
        ..@..
        ..@..
        """;

    [Fact]
    public void FixturePathHasThePublishedOptimalCost()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var result = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));

        Assert.True(result.Found);

        // 9 + 2*sqrt(2). Compared against the computed value rather than the
        // brief's 11.82843: that is rounded to five places and sits 3e-6 from the
        // true optimum, which this 1e-6 assertion would reject.
        Assert.Equal(9.0 + (2.0 * Math.Sqrt(2.0)), result.Cost, Tolerance);
        Assert.Equal(11, result.StepCount);
        Assert.Equal(12, result.Cells.Count);
    }

    [Fact]
    public void FixturePathIsNotTheCornerCuttingAnswer()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var result = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));

        // 10.65685 is what an implementation permitting diagonal squeezes
        // returns here. Asserting its absence names the bug in the failure
        // message rather than leaving a bare "expected 11.82 got 10.65".
        Assert.False(
            Math.Abs(result.Cost - (13.0 + ((Math.Sqrt(2.0) - 2.0) * 4.0))) < Tolerance,
            "cost matches the corner-cutting answer -- Movement.IsLegalStep is wrong");
    }

    [Fact]
    public void FixturePathExpandsTheNumberOfNodesTheBriefPredicts()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var result = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));

        // Section 4 of the brief: 21 under the tie-breaking rule in section 6.
        // This pins the tie-break, not the answer -- the path is optimal either
        // way, but a search that expands a different number of nodes is ordering
        // its frontier differently from the one the brief describes.
        Assert.Equal(21, result.Expanded);
    }

    [Fact]
    public void FixturePathStartsAtTheStartAndEndsAtTheGoal()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var start = grid.Index(1, 1);
        var goal = grid.Index(10, 5);

        var result = PathFinder.FindPath(grid, start, goal);

        Assert.Equal(start, result.Cells[0]);
        Assert.Equal(goal, result.Cells[^1]);
    }

    [Fact]
    public void EveryStepOfTheFixturePathIsLegal()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var result = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));

        AssertPathIsWalkable(grid, result);
    }

    [Fact]
    public void ReportedCostMatchesWalkingTheStepsOneAtATime()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var result = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));

        var summed = 0.0;
        for (var i = 1; i < result.Cells.Count; i++)
        {
            var deltaX = grid.ColumnOf(result.Cells[i]) - grid.ColumnOf(result.Cells[i - 1]);
            var deltaY = grid.RowOf(result.Cells[i]) - grid.RowOf(result.Cells[i - 1]);
            summed += deltaX != 0 && deltaY != 0 ? Movement.DiagonalCost : Movement.CardinalCost;
        }

        Assert.Equal(summed, result.Cost, Tolerance);
    }

    [Fact]
    public void UnreachableGoalIsAnAnswerNotAnException()
    {
        var grid = Grid.FromMapText(Severed);

        var result = PathFinder.FindPath(grid, grid.Index(0, 0), grid.Index(4, 2));

        Assert.False(result.Found);
        Assert.Empty(result.Cells);
        Assert.Equal(0.0, result.Cost);

        // It exhausted the reachable half rather than the whole grid.
        Assert.Equal(6, result.Expanded);
    }

    [Fact]
    public void ABlockedStartOrGoalIsNotFound()
    {
        var grid = Grid.FromMapText(Severed);

        Assert.False(PathFinder.FindPath(grid, grid.Index(2, 1), grid.Index(0, 0)).Found);
        Assert.False(PathFinder.FindPath(grid, grid.Index(0, 0), grid.Index(2, 1)).Found);
    }

    [Fact]
    public void StartEqualToGoalIsAZeroCostPathOfOneCell()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var cell = grid.Index(1, 1);

        var result = PathFinder.FindPath(grid, cell, cell);

        Assert.True(result.Found);
        Assert.Equal(0.0, result.Cost);
        Assert.Equal([cell], result.Cells);
        Assert.Equal(0, result.StepCount);
    }

    [Fact]
    public void TheSameQueryTwiceGivesTheSameAnswer()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var start = grid.Index(1, 1);
        var goal = grid.Index(10, 5);

        var first = PathFinder.FindPath(grid, start, goal);
        var second = PathFinder.FindPath(grid, start, goal);

        Assert.Equal(first.Cells, second.Cells);
        Assert.Equal(first.Cost, second.Cost);
        Assert.Equal(first.Expanded, second.Expanded);
    }

    [Fact]
    public void CostIsSymmetricBetweenAPairOfCells()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var a = grid.Index(1, 1);
        var b = grid.Index(10, 5);

        var forward = PathFinder.FindPath(grid, a, b);
        var backward = PathFinder.FindPath(grid, b, a);

        Assert.Equal(forward.Cost, backward.Cost, Tolerance);
    }

    [Fact]
    public void AStraightRunAcrossOpenGroundCostsItsLength()
    {
        var grid = Grid.FromMapText(
            """
            type octile
            height 3
            width 5
            map
            .....
            .....
            .....
            """);

        var result = PathFinder.FindPath(grid, grid.Index(0, 0), grid.Index(4, 0));

        Assert.True(result.Found);
        Assert.Equal(4.0, result.Cost, Tolerance);
        Assert.Equal(4, result.StepCount);
    }

    [Fact]
    public void ADiagonalRunAcrossOpenGroundCostsRootTwoPerStep()
    {
        var grid = Grid.FromMapText(
            """
            type octile
            height 3
            width 3
            map
            ...
            ...
            ...
            """);

        var result = PathFinder.FindPath(grid, grid.Index(0, 0), grid.Index(2, 2));

        Assert.True(result.Found);
        Assert.Equal(2.0 * Math.Sqrt(2.0), result.Cost, Tolerance);
        Assert.Equal(2, result.StepCount);
    }

    [Fact]
    public void APathAroundAWallRespectsTheCornerRule()
    {
        // Getting from (0,0) to (2,2) has to go round the blocked corner cells
        // rather than squeezing diagonally between them.
        var grid = Grid.FromMapText(
            """
            type octile
            height 3
            width 3
            map
            ...
            .@.
            ...
            """);

        var result = PathFinder.FindPath(grid, grid.Index(0, 0), grid.Index(2, 2));

        Assert.True(result.Found);
        AssertPathIsWalkable(grid, result);
        Assert.DoesNotContain(grid.Index(1, 1), result.Cells);
    }

    private static void AssertPathIsWalkable(Grid grid, PathResult result)
    {
        Assert.All(result.Cells, cell => Assert.True(grid.IsPassable(cell), $"cell {cell} is not passable"));

        for (var i = 1; i < result.Cells.Count; i++)
        {
            var previous = result.Cells[i - 1];
            var x = grid.ColumnOf(previous);
            var y = grid.RowOf(previous);
            var deltaX = grid.ColumnOf(result.Cells[i]) - x;
            var deltaY = grid.RowOf(result.Cells[i]) - y;

            Assert.InRange(deltaX, -1, 1);
            Assert.InRange(deltaY, -1, 1);
            Assert.True(
                Movement.IsLegalStep(grid, x, y, deltaX, deltaY),
                $"step {i} from ({x},{y}) by ({deltaX},{deltaY}) is not a legal move");
        }
    }
}
