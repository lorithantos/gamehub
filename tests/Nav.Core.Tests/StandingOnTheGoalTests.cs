namespace Nav.Core.Tests;

/// <summary>
/// A search that begins on its own goal is not finished until it has checked it
/// may stay there.
/// </summary>
/// <remarks>
/// Found by swapping the frontier heap for the framework's priority queue. Both
/// are correct min-heaps; the only thing that changed was which of several
/// equal-priority entries pops first, and that reordering produced a run in
/// which agent 13's old route left it standing on its newly claimed goal at
/// exactly its anchor tick while agent 16 held that cell for the following
/// tick. The constructor's start-equals-goal exit declared a one-cell plan found
/// without asking whether the cell could be held, and the two stood on it
/// together at tick 19. Collision-freedom must not depend on tie-breaking, so
/// the heap swap was a probe rather than the cause.
/// </remarks>
public sealed class StandingOnTheGoalTests
{
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

    private const int Horizon = 16;

    [Fact]
    public void AnAgentOnItsGoalDoesNotParkThereWhenSomebodyElseIsDueNextTick()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);

        var goal = grid.Index(3, 2);
        var west = grid.Index(2, 2);
        var east = grid.Index(4, 2);

        // Agent 1 walks west -> goal -> east over ticks 0..2 and parks east.
        // So the goal cell is taken at tick 1 and free again from tick 2.
        var other = new PlanResult([west, goal, east], 0, 2.0, 0, Found: true);
        table.Reserve(other.Cells, startTick: 0, agent: 1);

        // Agent 0 is standing ON the goal at tick 0 and is asked to be there.
        var plan = new BudgetedSearch(
            grid, table, agent: 0, start: goal, goal: goal, startTick: 0,
            workspace: new SearchWorkspace())
            .RunToCompletion();

        // Before the fix this was a one-cell plan: "I am here, done" -- and Commit
        // parked agent 0 on a cell agent 1 was about to walk into.
        Assert.True(plan.Found);
        Assert.NotEqual(1, plan.Cells.Count);
        Assert.Equal(goal, plan.Cells[^1]);
        Assert.True(table.IsHoldable(plan.Cells[^1], plan.LastTick, agent: 0), "the plan ends somewhere it may not stay");

        // The property that actually matters, asserted by the oracle rather than
        // inferred from the shape: the two plans never put both agents on one cell.
        var report = CollisionCheck.Inspect([new AgentPlan(0, plan), new AgentPlan(1, other)]);
        Assert.True(report.Clean, $"{report.Conflicts.Count} conflicts: {string.Join("; ", report.Conflicts)}");
    }

    [Fact]
    public void AnAgentOnItsGoalWithNobodyComingIsSimplyThere()
    {
        // The early exit still fires when it is correct to. A one-cell plan is the
        // right answer for a unit already parked where it was told to be.
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var goal = grid.Index(3, 2);

        var plan = new BudgetedSearch(
            grid, table, agent: 0, start: goal, goal: goal, startTick: 0,
            workspace: new SearchWorkspace())
            .RunToCompletion();

        Assert.True(plan.Found);
        Assert.Equal([goal], plan.Cells);
        Assert.Equal(0, plan.Expanded);
    }
}
