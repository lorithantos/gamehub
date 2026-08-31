namespace Nav.Core.Tests;

/// <summary>
/// Criterion 7: a plan built in instalments is the plan built in one go.
/// </summary>
/// <remarks>
/// The property is true by construction — <see cref="CooperativePlanner.FindPlan"/>
/// is a wrapper over <see cref="BudgetedSearch"/>, so there is no second code path
/// to diverge. These assert it anyway. "By construction" is a claim, and claims
/// are what this suite exists to check.
/// </remarks>
public sealed class BudgetedSearchTests
{
    private const int Horizon = 48;

    private const string Hall =
        """
        type octile
        height 12
        width 12
        map
        @@@@@@@@@@@@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @@@@@@@@@@@@
        """;

    private static (Grid Grid, ReservationTable Table) Scene()
    {
        var grid = Grid.FromMapText(Hall);
        var table = new ReservationTable(grid.CellCount, Horizon);

        // Something in the way, so the search is not a straight line.
        table.Reserve([grid.Index(5, 5), grid.Index(5, 5)], startTick: 0, agent: 99);
        return (grid, table);
    }

    private static BudgetedSearch New(Grid grid, ReservationTable table) =>
        new(grid, table, agent: 0, grid.Index(1, 1), grid.Index(10, 10), 0, new SearchWorkspace());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(1000)]
    public void ABudgetedPlanIsTheSameAsAnUnbudgetedOne(int budget)
    {
        var (grid, table) = Scene();

        var wholeHog = New(grid, table).RunToCompletion();

        var search = New(grid, table);
        var slices = 0;
        while (!search.Advance(budget))
        {
            slices++;
            Assert.True(slices < 100_000, "the search never finished");
        }

        Assert.Equal(wholeHog.Cells, search.Result.Cells);
        Assert.Equal(wholeHog.Cost, search.Result.Cost);
        Assert.Equal(wholeHog.Expanded, search.Result.Expanded);
        Assert.Equal(wholeHog.Found, search.Result.Found);
        Assert.Equal(wholeHog.StartTick, search.Result.StartTick);
    }

    [Fact]
    public void ASmallBudgetActuallySuspends()
    {
        // Otherwise the test above proves nothing: a search that always finishes
        // on the first Advance is not being interrupted.
        var (grid, table) = Scene();
        var search = New(grid, table);

        Assert.False(search.Advance(1));
        Assert.False(search.Finished);
        Assert.Equal(1, search.Expanded);
    }

    [Fact]
    public void ExpansionsAccumulateAcrossCalls()
    {
        var (grid, table) = Scene();
        var search = New(grid, table);

        search.Advance(3);
        var after3 = search.Expanded;
        search.Advance(3);

        Assert.Equal(3, after3);
        Assert.True(search.Expanded > after3, "the second slice expanded nothing");
    }

    [Fact]
    public void AdvancingAFinishedSearchChangesNothing()
    {
        var (grid, table) = Scene();
        var search = New(grid, table);

        var plan = search.RunToCompletion();
        var expanded = search.Expanded;

        Assert.True(search.Advance(100));
        Assert.Same(plan, search.Result);
        Assert.Equal(expanded, search.Expanded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveBudgetIsRefused(int budget)
    {
        var (grid, table) = Scene();
        var search = New(grid, table);

        Assert.Throws<ArgumentOutOfRangeException>(() => search.Advance(budget));
    }

    // --- answers available before a single node is expanded ------------------

    [Fact]
    public void StartEqualToGoalIsFinishedImmediately()
    {
        var (grid, table) = Scene();
        var cell = grid.Index(2, 2);

        var search = new BudgetedSearch(grid, table, 0, cell, cell, 0, new SearchWorkspace());

        Assert.True(search.Finished);
        Assert.Equal(0, search.Expanded);
        Assert.True(search.Result.Found);
    }

    [Fact]
    public void AnImpassableEndpointIsFinishedImmediately()
    {
        var (grid, table) = Scene();

        var search = new BudgetedSearch(grid, table, 0, grid.Index(1, 1), grid.Index(0, 0), 0, new SearchWorkspace());

        Assert.True(search.Finished);
        Assert.True(search.Result.IsStuck);
    }

    [Fact]
    public void AStartBeyondTheWindowIsFinishedImmediately()
    {
        var grid = Grid.FromMapText(Hall);
        var table = new ReservationTable(grid.CellCount, horizon: 4);

        var search = new BudgetedSearch(
            grid, table, 0, grid.Index(1, 1), grid.Index(10, 10), startTick: 99, new SearchWorkspace());

        Assert.True(search.Finished);
        Assert.True(search.Result.IsStuck);
    }

    // --- the wrapper and the class agree ------------------------------------

    [Fact]
    public void FindPlanAgreesWithRunningTheSearchDirectly()
    {
        var (grid, table) = Scene();

        var viaWrapper = CooperativePlanner.FindPlan(
            grid, table, 0, grid.Index(1, 1), grid.Index(10, 10), 0, new SearchWorkspace());
        var viaSearch = New(grid, table).RunToCompletion();

        Assert.Equal(viaWrapper.Cells, viaSearch.Cells);
        Assert.Equal(viaWrapper.Expanded, viaSearch.Expanded);
    }

    [Fact]
    public void ASearchThatWasInterruptedManyTimesStillRespectsReservations()
    {
        // Interruption must not lose the constraints; a resumed search is the same
        // search, not a fresh one that has forgotten who is where.
        var (grid, table) = Scene();
        var search = New(grid, table);

        while (!search.Advance(1))
        {
            // one node at a time
        }

        for (var i = 0; i < search.Result.Cells.Count; i++)
        {
            var tick = search.Result.StartTick + i;
            Assert.True(
                table.IsFree(search.Result.Cells[i], tick, agent: 0),
                $"tick {tick}: cell {search.Result.Cells[i]} is held by {table.HolderOf(search.Result.Cells[i], tick)}");
        }
    }
}
