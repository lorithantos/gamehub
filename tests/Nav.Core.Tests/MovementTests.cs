namespace Nav.Core.Tests;

/// <summary>
/// Acceptance criterion 4, and the cost/heuristic pairing everything downstream
/// assumes.
/// </summary>
public sealed class MovementTests
{
    /// <summary>Open ground, so any refusal is the rule and not the terrain.</summary>
    private const string Open3x3 =
        """
        type octile
        height 3
        width 3
        map
        ...
        ...
        ...
        """;

    /// <summary>
    /// One shoulder blocked. This is the discriminating case: an implementation
    /// that refuses a diagonal only when BOTH neighbours are blocked lets this
    /// one through, and that is the bug worth 1.17 cost units on the fixture.
    /// </summary>
    private const string OneShoulderBlocked =
        """
        type octile
        height 3
        width 3
        map
        .@.
        ...
        ...
        """;

    /// <summary>
    /// A 2x2 block of terrain with the two shoulders blocked -- the squeeze the
    /// brief names explicitly.
    /// </summary>
    private const string BothShouldersBlocked =
        """
        type octile
        height 3
        width 3
        map
        .@.
        @..
        ...
        """;

    [Fact]
    public void DiagonalAcrossOpenGroundIsLegal()
    {
        var grid = Grid.FromMapText(Open3x3);

        Assert.True(Movement.IsLegalStep(grid, 0, 0, 1, 1));
    }

    [Fact]
    public void DiagonalSqueezingBetweenTwoBlockedCellsIsRejected()
    {
        var grid = Grid.FromMapText(BothShouldersBlocked);

        // (1,1) itself is open, so nothing but the corner rule can refuse this.
        Assert.True(grid.IsPassable(1, 1));
        Assert.False(Movement.IsLegalStep(grid, 0, 0, 1, 1));
    }

    [Fact]
    public void DiagonalPastASingleBlockedShoulderIsAlsoRejected()
    {
        var grid = Grid.FromMapText(OneShoulderBlocked);

        Assert.False(grid.IsPassable(1, 0));
        Assert.True(grid.IsPassable(0, 1));
        Assert.True(grid.IsPassable(1, 1));

        Assert.False(Movement.IsLegalStep(grid, 0, 0, 1, 1));
    }

    [Fact]
    public void TheOtherShoulderCountsToo()
    {
        // Mirror of the case above: block (0,1) instead of (1,0).
        var grid = Grid.FromMapText(
            """
            type octile
            height 3
            width 3
            map
            ...
            @..
            ...
            """);

        Assert.False(Movement.IsLegalStep(grid, 0, 0, 1, 1));
    }

    [Fact]
    public void CardinalStepsIgnoreTheirShoulders()
    {
        var grid = Grid.FromMapText(OneShoulderBlocked);

        // Walking east from (0,1) to (1,1) passes right under the blocked cell,
        // which is fine -- the corner rule is about diagonals only.
        Assert.True(Movement.IsLegalStep(grid, 0, 1, 1, 0));
    }

    [Fact]
    public void AStepIntoABlockedCellIsRejected()
    {
        var grid = Grid.FromMapText(OneShoulderBlocked);

        Assert.False(Movement.IsLegalStep(grid, 0, 0, 1, 0));
    }

    [Fact]
    public void AStepOffTheMapIsRejected()
    {
        var grid = Grid.FromMapText(Open3x3);

        Assert.False(Movement.IsLegalStep(grid, 0, 0, -1, 0));
        Assert.False(Movement.IsLegalStep(grid, 2, 2, 1, 1));
    }

    [Fact]
    public void TheStepTableHasEightDistinctMovesWithTheirCostsBakedIn()
    {
        var steps = Movement.Steps;

        Assert.Equal(8, steps.Length);

        var seen = new HashSet<(int, int)>();
        var cardinals = 0;
        var diagonals = 0;

        foreach (var step in steps)
        {
            Assert.True(seen.Add((step.DeltaX, step.DeltaY)), "duplicate offset in the step table");
            Assert.InRange(step.DeltaX, -1, 1);
            Assert.InRange(step.DeltaY, -1, 1);
            Assert.False(step is { DeltaX: 0, DeltaY: 0 }, "the table must not contain a null move");

            if (step.DeltaX == 0 || step.DeltaY == 0)
            {
                cardinals++;
                Assert.Equal(1.0, step.Cost, 1e-12);
            }
            else
            {
                diagonals++;
                Assert.Equal(Math.Sqrt(2.0), step.Cost, 1e-12);
            }
        }

        Assert.Equal(4, cardinals);
        Assert.Equal(4, diagonals);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0.0)]
    [InlineData(0, 0, 3, 0, 3.0)]
    [InlineData(0, 0, 0, 3, 3.0)]
    public void OctileDistanceOnAStraightLineIsTheCardinalCount(int ax, int ay, int bx, int by, double expected)
    {
        Assert.Equal(expected, Movement.OctileDistance(ax, ay, bx, by), 1e-12);
    }

    [Fact]
    public void OctileDistanceOnADiagonalIsPureDiagonalCost()
    {
        Assert.Equal(3.0 * Math.Sqrt(2.0), Movement.OctileDistance(0, 0, 3, 3), 1e-12);
    }

    [Fact]
    public void OctileDistanceIsSymmetric()
    {
        Assert.Equal(
            Movement.OctileDistance(1, 1, 10, 5),
            Movement.OctileDistance(10, 5, 1, 1),
            1e-12);
    }

    [Fact]
    public void OctileDistanceUnderestimatesTheFixturePath()
    {
        // dx=9, dy=4, so h = 13 + (sqrt(2)-2)*4 = 10.65685...
        //
        // That number is worth recognising: it is exactly what a corner-cutting
        // A* returns for this query, because with the corner rule removed the
        // fixture offers an unobstructed octile path. The true optimum is
        // 9 + 2*sqrt(2) = 11.82843, so the heuristic underestimates and A* stays
        // optimal -- but the gap between the two is the whole test suite's grip
        // on the corner-cutting bug.
        var heuristic = Movement.OctileDistance(1, 1, 10, 5);
        var optimal = Movement.ExactCost(9, 2);

        Assert.Equal(10.65685424949238, heuristic, 1e-9);
        Assert.True(heuristic < optimal, "the octile heuristic must not overestimate");
    }

    [Fact]
    public void ExactCostMatchesSummingTheStepsOneAtATime()
    {
        var summed = 0.0;
        for (var i = 0; i < 9; i++)
        {
            summed += Movement.CardinalCost;
        }

        for (var i = 0; i < 2; i++)
        {
            summed += Movement.DiagonalCost;
        }

        Assert.Equal(summed, Movement.ExactCost(9, 2), 1e-12);

        // And it is the fixture's published optimum. Compared against the
        // computed value, not the brief's 11.82843: that is rounded to five
        // places and sits 3e-6 away, which a 1e-6 assertion would reject.
        Assert.Equal(9.0 + (2.0 * Math.Sqrt(2.0)), Movement.ExactCost(9, 2), 1e-12);
    }
}
