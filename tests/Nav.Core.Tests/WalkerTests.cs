namespace Nav.Core.Tests;

/// <summary>
/// Acceptance criteria 8 and 9.
/// </summary>
public sealed class WalkerTests
{
    private const double Tolerance = 1e-4;
    private const int Width = 3;

    /// <summary>
    /// (0,0) east to (1,0), then south-east to (2,1): one cardinal step then one
    /// diagonal, which is the shortest path that can tell an arc-length walker
    /// from a per-segment one.
    /// </summary>
    private static readonly int[] CardinalThenDiagonal = [0, 1, 5];

    private static Walker OneCellPerSecond() => new(CardinalThenDiagonal, Width, speed: 1.0);

    [Fact]
    public void StartsAtTheFirstCell()
    {
        var walker = OneCellPerSecond();

        Assert.Equal(0.0, walker.X, Tolerance);
        Assert.Equal(0.0, walker.Y, Tolerance);
        Assert.False(walker.Arrived);
    }

    [Fact]
    public void PathLengthIsOnePlusRootTwo()
    {
        Assert.Equal(1.0 + Math.Sqrt(2.0), OneCellPerSecond().PathLength, 1e-12);
    }

    [Fact]
    public void AtOneSecondTheUnitIsAtTheCorner()
    {
        var walker = OneCellPerSecond();

        walker.Advance(1.0);

        Assert.Equal(1.0, walker.X, Tolerance);
        Assert.Equal(0.0, walker.Y, Tolerance);
        Assert.False(walker.Arrived);
    }

    [Fact]
    public void ArrivesAtOnePlusRootTwoSeconds()
    {
        var walker = OneCellPerSecond();

        walker.Advance(1.0 + Math.Sqrt(2.0));

        Assert.True(walker.Arrived);
        Assert.Equal(2.0, walker.X, Tolerance);
        Assert.Equal(1.0, walker.Y, Tolerance);
    }

    [Fact]
    public void HasNotArrivedJustBeforeTheEnd()
    {
        var walker = OneCellPerSecond();

        walker.Advance(1.0 + Math.Sqrt(2.0) - 0.01);

        Assert.False(walker.Arrived);
    }

    [Fact]
    public void TheDiagonalSegmentTakesRootTwoSecondsNotOne()
    {
        // Halfway along the diagonal in DISTANCE is halfway in TIME, which lands
        // the unit at (1.5, 0.5). A walker giving each segment equal wall-clock
        // time would be past that point, moving visibly faster on the diagonal.
        var walker = OneCellPerSecond();

        walker.Advance(1.0 + (Math.Sqrt(2.0) / 2.0));

        Assert.Equal(1.5, walker.X, Tolerance);
        Assert.Equal(0.5, walker.Y, Tolerance);

        // And at t = 2.0 -- where a per-segment walker would already have
        // arrived -- it is still short of the goal.
        var naive = OneCellPerSecond();
        naive.Advance(2.0);
        Assert.False(naive.Arrived);
    }

    [Fact]
    public void PositionIsTheSameWhateverTheFrameRate()
    {
        var single = OneCellPerSecond();
        single.Advance(1.0);

        var sixty = OneCellPerSecond();
        for (var i = 0; i < 60; i++)
        {
            sixty.Advance(1.0 / 60.0);
        }

        var sixHundred = OneCellPerSecond();
        for (var i = 0; i < 600; i++)
        {
            sixHundred.Advance(1.0 / 600.0);
        }

        Assert.Equal(single.X, sixty.X, Tolerance);
        Assert.Equal(single.Y, sixty.Y, Tolerance);
        Assert.Equal(single.X, sixHundred.X, Tolerance);
        Assert.Equal(single.Y, sixHundred.Y, Tolerance);
    }

    [Fact]
    public void FrameRateIndependenceHoldsPartWayIntoTheDiagonalToo()
    {
        const double target = 1.0 + (7.0 / 9.0);

        var single = OneCellPerSecond();
        single.Advance(target);

        var chopped = OneCellPerSecond();
        for (var i = 0; i < 900; i++)
        {
            chopped.Advance(target / 900.0);
        }

        Assert.Equal(single.X, chopped.X, Tolerance);
        Assert.Equal(single.Y, chopped.Y, Tolerance);
    }

    [Fact]
    public void ASingleDeltaCanCrossSeveralSegments()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var path = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5)).Cells;

        var oneJump = new Walker(path, grid.Width, speed: 2.0);
        oneJump.Advance(3.0);

        var stepped = new Walker(path, grid.Width, speed: 2.0);
        for (var i = 0; i < 180; i++)
        {
            stepped.Advance(3.0 / 180.0);
        }

        Assert.Equal(oneJump.X, stepped.X, Tolerance);
        Assert.Equal(oneJump.Y, stepped.Y, Tolerance);
        Assert.False(oneJump.Arrived);
    }

    [Fact]
    public void OvershootingClampsToTheGoal()
    {
        var walker = OneCellPerSecond();

        walker.Advance(1000.0);

        Assert.True(walker.Arrived);
        Assert.Equal(2.0, walker.X, Tolerance);
        Assert.Equal(1.0, walker.Y, Tolerance);

        // Still there after another huge advance, rather than drifting on.
        walker.Advance(1000.0);
        Assert.Equal(2.0, walker.X, Tolerance);
        Assert.Equal(1.0, walker.Y, Tolerance);
    }

    [Fact]
    public void SpeedScalesTheWalkExactly()
    {
        var fast = new Walker(CardinalThenDiagonal, Width, speed: 4.0);
        fast.Advance(0.25);

        var slow = OneCellPerSecond();
        slow.Advance(1.0);

        Assert.Equal(slow.X, fast.X, Tolerance);
        Assert.Equal(slow.Y, fast.Y, Tolerance);
    }

    [Fact]
    public void ResetReturnsTheUnitToTheStart()
    {
        var walker = OneCellPerSecond();
        walker.Advance(5.0);
        Assert.True(walker.Arrived);

        walker.Reset();

        Assert.Equal(0.0, walker.X, Tolerance);
        Assert.Equal(0.0, walker.Y, Tolerance);
        Assert.False(walker.Arrived);
        Assert.Equal(0.0, walker.Elapsed);
    }

    [Fact]
    public void ASingleCellPathHasArrivedAlready()
    {
        var walker = new Walker([4], Width, speed: 1.0);

        Assert.True(walker.Arrived);
        Assert.Equal(1.0, walker.X, Tolerance);
        Assert.Equal(1.0, walker.Y, Tolerance);
        Assert.Equal(0.0, walker.PathLength);
    }

    [Fact]
    public void ANonAdjacentPathIsRefused()
    {
        // (0,0) to (2,0) is a gap, not a step.
        var ex = Assert.Throws<ArgumentException>(() => new Walker([0, 2], Width, speed: 1.0));

        Assert.Contains("not adjacent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedCellIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Walker([0, 0], Width, speed: 1.0));
    }

    [Fact]
    public void AnEmptyPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Walker([], Width, speed: 1.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ANonPositiveSpeedIsRefused(double speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Walker(CardinalThenDiagonal, Width, speed));
    }

    [Fact]
    public void ANegativeDeltaIsRefused()
    {
        var walker = OneCellPerSecond();

        Assert.Throws<ArgumentOutOfRangeException>(() => walker.Advance(-0.1));
    }

    [Fact]
    public void WalkingTheFixturePathEndsOnTheGoal()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var result = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));

        var walker = new Walker(result.Cells, grid.Width, speed: 3.0);

        Assert.Equal(result.Cost, walker.PathLength, 1e-9);

        walker.Advance(result.Cost / 3.0);

        Assert.True(walker.Arrived);
        Assert.Equal(10.0, walker.X, Tolerance);
        Assert.Equal(5.0, walker.Y, Tolerance);
    }
}
