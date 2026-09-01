namespace Nav.Core.Tests;

/// <summary>
/// A reservation view the planner cannot tell from the real table, wrapping one
/// and hiding a single agent's bookings.
/// </summary>
/// <remarks>
/// The smallest useful shape of the thing the multi-team work needs: today every
/// agent plans against every other agent's committed future, which is correct for
/// one commander and mind-reading for two. This is that filter, in twenty lines,
/// composed over the real table rather than carved into it.
/// <para>
/// It takes the concrete <see cref="ReservationTable"/> rather than an
/// <see cref="IReservationView"/> because it needs <c>HolderOf</c> to know WHOSE
/// booking it is looking at, and identity is deliberately not on the view -- a
/// real fog implementation would filter on team membership held elsewhere.
/// </para>
/// </remarks>
internal sealed class BlindTo(ReservationTable inner, int hidden) : IReservationView
{
    public int Horizon => inner.Horizon;

    public int CurrentTick => inner.CurrentTick;

    public bool IsFree(int cell, int tick, int agent)
    {
        var holder = inner.HolderOf(cell, tick);
        return holder < 0 || holder == agent || holder == hidden;
    }

    public bool IsSwap(int from, int to, int tick, int agent)
    {
        var other = inner.HolderOf(to, tick);
        if (other < 0 || other == agent || other == hidden)
        {
            return false;
        }

        return inner.HolderOf(from, tick + 1) == other;
    }

    public bool IsHoldable(int cell, int fromTick, int agent)
    {
        var last = CurrentTick + Horizon - 1;
        for (var tick = Math.Max(fromTick, CurrentTick); tick <= last; tick++)
        {
            if (!IsFree(cell, tick, agent))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The reservation seam, exercised by composition rather than by inheritance of
/// behaviour: the same search, the same table, two different views of it.
/// </summary>
public sealed class ReservationViewCompositionTests
{
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

    private const int Horizon = 16;

    private static (Grid Grid, ReservationTable Table) Scene()
    {
        var grid = Grid.FromMapText(Corridor);
        var table = new ReservationTable(grid.CellCount, Horizon);

        // Agent 0 parks in the middle of the one-wide corridor and stays: Reserve
        // holds a plan's final cell for the rest of the window.
        table.Reserve([grid.Index(3, 1)], startTick: 0, agent: 0);
        return (grid, table);
    }

    private static PlanResult PlanFor(Grid grid, IReservationView view) =>
        new BudgetedSearch(
            grid, view, agent: 1,
            start: grid.Index(1, 1), goal: grid.Index(5, 1),
            startTick: 0, workspace: new SearchWorkspace())
            .RunToCompletion();

    [Fact]
    public void AgainstTheRealTableTheParkedAgentBlocksTheCorridor()
    {
        var (grid, table) = Scene();

        var plan = PlanFor(grid, table);

        // One-wide corridor, somebody standing in it who never leaves: there is no
        // route to the far end and the search says so rather than walking through.
        Assert.DoesNotContain(grid.Index(3, 1), plan.Cells);
        Assert.NotEqual(grid.Index(5, 1), plan.Cells[^1]);
    }

    [Fact]
    public void AViewThatHidesThatAgentPlansStraightThroughIt()
    {
        // THE POINT OF THE SEAM. Same grid, same table, same search -- only the
        // VIEW differs, and the planner cannot tell. Nothing in the collision core
        // knows anyone is filtering.
        var (grid, table) = Scene();

        var plan = PlanFor(grid, new BlindTo(table, hidden: 0));

        Assert.True(plan.Found);
        Assert.Equal(grid.Index(5, 1), plan.Cells[^1]);
        Assert.Contains(grid.Index(3, 1), plan.Cells);
    }

    [Fact]
    public void HidingAnAgentNobodyIsBlockedByChangesNothing()
    {
        // The filter is not a licence to walk through people generally -- hide an
        // agent that holds nothing and the answer is identical to the real table's.
        var (grid, table) = Scene();

        var real = PlanFor(grid, table);
        var blind = PlanFor(grid, new BlindTo(table, hidden: 99));

        Assert.Equal(real.Cells, blind.Cells);
        Assert.Equal(real.Found, blind.Found);
    }
}
