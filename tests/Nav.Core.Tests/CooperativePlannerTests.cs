namespace Nav.Core.Tests;

/// <summary>
/// Space-time A* with a single agent, before any cooperation is exercised.
/// </summary>
public sealed class CooperativePlannerTests
{
    private const int Horizon = 32;

    /// <summary>Open ground with walls, so an agent has room to route around a block.</summary>
    private const string Room =
        """
        type octile
        height 5
        width 7
        map
        @@@@@@@
        @.....@
        @.....@
        @.....@
        @@@@@@@
        """;

    /// <summary>One cell wide, so nothing can pass anything.</summary>
    private const string Corridor =
        """
        type octile
        height 3
        width 7
        map
        @@@@@@@
        @.....@
        @@@@@@@
        """;

    private static PlanResult Plan(
        Grid grid,
        ReservationTable table,
        int agent,
        int start,
        int goal,
        int startTick = 0) =>
        CooperativePlanner.FindPlan(grid, table, agent, start, goal, startTick, new SearchWorkspace());

    // --- with nothing reserved, it must agree with milestone 1 ---------------

    [Fact]
    public void WithNothingReservedItCostsExactlyWhatSingleAgentAStarCosts()
    {
        // The tie back to milestone 1, whose answers are verified against 367,010
        // published optima. Adding a time axis must not change what a path costs
        // when there is nobody to cooperate with.
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var table = new ReservationTable(grid.CellCount, Horizon);

        var expected = PathFinder.FindPath(grid, grid.Index(1, 1), grid.Index(10, 5));
        var plan = Plan(grid, table, agent: 0, grid.Index(1, 1), grid.Index(10, 5));

        Assert.True(plan.Found);
        Assert.Equal(expected.Cost, plan.Cost, 1e-6);
        Assert.Equal(expected.Cells.Count, plan.Cells.Count);
        Assert.Equal(grid.Index(10, 5), plan.Cells[^1]);
    }

    [Fact]
    public void EveryStepOfAPlanIsALegalMoveOrAWait()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var table = new ReservationTable(grid.CellCount, Horizon);

        var plan = Plan(grid, table, agent: 0, grid.Index(1, 1), grid.Index(10, 5));

        AssertWalkable(grid, plan);
    }

    [Fact]
    public void APlanOccupiesOneCellPerTickStartingAtTheGivenTick()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);

        var plan = Plan(grid, table, agent: 0, Cell(grid, 1, 1), Cell(grid, 5, 1), startTick: 3);

        Assert.Equal(3, plan.StartTick);
        Assert.Equal(3 + plan.Cells.Count - 1, plan.LastTick);
    }

    [Fact]
    public void StartEqualToGoalIsAOneCellPlan()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var cell = Cell(grid, 2, 2);

        var plan = Plan(grid, table, agent: 0, cell, cell);

        Assert.True(plan.Found);
        Assert.Equal([cell], plan.Cells);
        Assert.Equal(0.0, plan.Cost);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnImpassableEndpointIsStuck(bool blockTheStart)
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var wall = Cell(grid, 0, 0);
        var open = Cell(grid, 2, 2);

        var plan = blockTheStart
            ? Plan(grid, table, agent: 0, wall, open)
            : Plan(grid, table, agent: 0, open, wall);

        Assert.True(plan.IsStuck);
        Assert.False(plan.Found);
    }

    // --- cooperation ---------------------------------------------------------

    [Fact]
    public void APlanNeverEntersACellSomebodyElseHasReserved()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);

        // Agent 0 parks in the middle of the room, permanently.
        table.Reserve([Cell(grid, 3, 2)], startTick: 0, agent: 0);

        var plan = Plan(grid, table, agent: 1, Cell(grid, 1, 2), Cell(grid, 5, 2));

        Assert.True(plan.Found);
        AssertRespectsReservations(table, plan, agent: 1);
        Assert.DoesNotContain(Cell(grid, 3, 2), plan.Cells);
    }

    [Fact]
    public void ItWaitsWhenWaitingIsTheOnlyWayThrough()
    {
        // A one-wide corridor with a blocker that moves off in three ticks. The
        // only plan is to hang back, which is exactly what the wait action exists
        // for -- without it this instance reports unsolvable.
        var grid = Grid.FromMapText(Corridor);
        var table = new ReservationTable(grid.CellCount, Horizon);

        var a = Cell(grid, 1, 1);
        var b = Cell(grid, 2, 1);
        var c = Cell(grid, 3, 1);
        var d = Cell(grid, 4, 1);
        var e = Cell(grid, 5, 1);

        // Agent 0 sits on c for three ticks, then walks out to e.
        table.Reserve([c, c, c, d, e], startTick: 0, agent: 0);

        var plan = Plan(grid, table, agent: 1, a, d);

        Assert.True(plan.Found);
        AssertWalkable(grid, plan);
        AssertRespectsReservations(table, plan, agent: 1);

        // A repeated cell IS the wait.
        var waited = plan.Cells.Where((cell, i) => i > 0 && plan.Cells[i - 1] == cell).Any();
        Assert.True(waited, "the plan should have waited for the corridor to clear");
    }

    [Fact]
    public void ItWillNotSwapThroughAnAgentComingTheOtherWay()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);

        var left = Cell(grid, 2, 2);
        var right = Cell(grid, 3, 2);

        // Agent 0 walks right -> left across the first tick.
        table.Reserve([right, left], startTick: 0, agent: 0);

        var plan = Plan(grid, table, agent: 1, left, Cell(grid, 5, 2));

        // The direct move would exchange places with agent 0. It must not.
        Assert.True(table.IsSwap(left, right, tick: 0, agent: 1));
        Assert.NotEqual(right, plan.Cells[1]);
        AssertRespectsReservations(table, plan, agent: 1);
    }

    // --- the window ----------------------------------------------------------

    [Fact]
    public void AGoalBeyondTheWindowGivesAPartialPlanRatherThanNothing()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var table = new ReservationTable(grid.CellCount, horizon: 4);

        var plan = Plan(grid, table, agent: 0, grid.Index(1, 1), grid.Index(10, 5));

        Assert.False(plan.Found);
        Assert.True(plan.IsPartial);
        Assert.Equal(4, plan.Cells.Count);
        AssertWalkable(grid, plan);
    }

    [Fact]
    public void APartialPlanMakesProgressTowardTheGoal()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var table = new ReservationTable(grid.CellCount, horizon: 4);
        var goal = grid.Index(10, 5);

        var plan = Plan(grid, table, agent: 0, grid.Index(1, 1), goal);

        var before = Movement.OctileDistance(1, 1, 10, 5);
        var after = Movement.OctileDistance(
            grid.ColumnOf(plan.Cells[^1]), grid.RowOf(plan.Cells[^1]), 10, 5);

        Assert.True(after < before, $"expected progress: {before} -> {after}");
    }

    [Fact]
    public void PlanningPastTheEndOfTheWindowIsStuck()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, horizon: 4);

        var plan = Plan(grid, table, agent: 0, Cell(grid, 1, 1), Cell(grid, 5, 1), startTick: 9);

        Assert.True(plan.IsStuck);
    }

    [Fact]
    public void PlanningBehindTheWindowIsAnError()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        table.Advance(5);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Plan(grid, table, agent: 0, Cell(grid, 1, 1), Cell(grid, 5, 1), startTick: 2));
    }

    [Fact]
    public void TheSamePlanTwiceIsIdentical()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        var table = new ReservationTable(grid.CellCount, Horizon);
        table.Reserve([grid.Index(5, 1)], startTick: 0, agent: 0);

        var first = Plan(grid, table, agent: 1, grid.Index(1, 1), grid.Index(10, 5));
        var second = Plan(grid, table, agent: 1, grid.Index(1, 1), grid.Index(10, 5));

        Assert.Equal(first.Cells, second.Cells);
        Assert.Equal(first.Cost, second.Cost);
        Assert.Equal(first.Expanded, second.Expanded);
    }

    [Fact]
    public void OneWorkspaceServesManyPlansInSequence()
    {
        // The workspace is scratch reused across searches; a second plan must not
        // inherit anything from the first.
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var workspace = new SearchWorkspace();

        var shared = CooperativePlanner.FindPlan(grid, table, 0, Cell(grid, 1, 1), Cell(grid, 5, 3), 0, workspace);
        var second = CooperativePlanner.FindPlan(grid, table, 0, Cell(grid, 1, 1), Cell(grid, 5, 3), 0, workspace);
        var fresh = Plan(grid, table, agent: 0, Cell(grid, 1, 1), Cell(grid, 5, 3));

        Assert.Equal(fresh.Cells, shared.Cells);
        Assert.Equal(fresh.Cells, second.Cells);
        Assert.Equal(fresh.Expanded, second.Expanded);
    }

    // --- helpers -------------------------------------------------------------

    private static int Cell(Grid grid, int x, int y) => grid.Index(x, y);

    private static void AssertWalkable(Grid grid, PlanResult plan)
    {
        Assert.All(plan.Cells, cell => Assert.True(grid.IsPassable(cell), $"cell {cell} is not passable"));

        for (var i = 1; i < plan.Cells.Count; i++)
        {
            var previous = plan.Cells[i - 1];
            if (previous == plan.Cells[i])
            {
                continue;   // a wait
            }

            var x = grid.ColumnOf(previous);
            var y = grid.RowOf(previous);
            var deltaX = grid.ColumnOf(plan.Cells[i]) - x;
            var deltaY = grid.RowOf(plan.Cells[i]) - y;

            Assert.True(
                Movement.IsLegalStep(grid, x, y, deltaX, deltaY),
                $"tick {i}: ({x},{y}) by ({deltaX},{deltaY}) is not a legal move");
        }
    }

    private static void AssertRespectsReservations(ReservationTable table, PlanResult plan, int agent)
    {
        for (var i = 0; i < plan.Cells.Count; i++)
        {
            var tick = plan.StartTick + i;
            Assert.True(
                table.IsFree(plan.Cells[i], tick, agent),
                $"tick {tick}: cell {plan.Cells[i]} is held by agent {table.HolderOf(plan.Cells[i], tick)}");
        }
    }
}
