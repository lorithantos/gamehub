namespace Nav.Core.Tests;

/// <summary>
/// Standing beats planning: the table's contract once followers could change it
/// every tick without a search.
/// </summary>
/// <remarks>
/// Until followers, every plan in the table had been validated cell by cell by
/// the search that produced it, and the ring refused to overwrite another
/// agent's booking -- an invariant broken was an exception at the commit. A
/// follower checks only the next tick and then stands, so a committed plan can
/// go stale honestly: a unit stopped on a cell the plan was going to cross. The
/// ring now overwrites, the parked unit answers for a cell before any plan
/// through it, and the guarantee lives at the move: a unit steps only if it
/// holds the cell at that tick. These pin the pieces of that contract that a
/// collision found missing, one each.
/// </remarks>
public sealed class ReservationOverwriteTests
{
    private const int Cells = 20;
    private const int Horizon = 8;

    [Fact]
    public void ALaterReservationOverwritesTheRing()
    {
        // The earlier plan is stale, not refused: its owner will not take the
        // step, because it no longer holds the cell.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2, 3], startTick: 0, agent: 0);

        table.Reserve([5, 2, 6], startTick: 0, agent: 1);

        Assert.Equal(1, table.HolderOf(2, 1));
        Assert.False(table.IsFree(2, 1, agent: 0));
    }

    [Fact]
    public void AParkedAgentAnswersForItsCellBeforeAnyPlanThroughIt()
    {
        // Agent 0 ends on cell 2 at tick 1 and stays. A plan that later books
        // cell 2 at tick 2 is a stale intention; the standing unit holds it.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2], startTick: 0, agent: 0);

        table.Reserve([7, 8, 2], startTick: 0, agent: 1);

        Assert.Equal(0, table.HolderOf(2, 2));
        Assert.False(table.IsFree(2, 2, agent: 1));
    }

    [Fact]
    public void AParkIsAClaimOnTheCellNotJustOnATick()
    {
        // Agent 0's plan ends on cell 8 at tick 7. Cell 8 is free to PASS
        // through at tick 3, but nobody else may end on it, at any tick: one
        // park per cell is all the table keeps, and a second would erase the
        // first on its way out. The arena found that as two units standing on
        // one cell for good.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2, 3, 4, 5, 6, 7, 8], startTick: 0, agent: 0);

        Assert.True(table.IsFree(8, 3, agent: 1));
        Assert.True(table.WillBeParkedOn(8, agent: 1));
        Assert.False(table.WillBeParkedOn(8, agent: 0));
        Assert.False(table.IsHoldable(8, 0, agent: 1));
        Assert.False(table.TryPark(8, agent: 1));
    }

    [Fact]
    public void ASwapIsSeenThroughTheAskersOwnPark()
    {
        // Agent 0 stands parked on cell 1. Agent 1 is on cell 2 and booked to
        // step onto cell 1 next tick. Agent 0 asking whether stepping onto cell 2
        // is a swap must see agent 1's intention -- which lives in the ring under
        // agent 0's own park, and with standing answering first would otherwise
        // be hidden. Two followers swapped through each other that way once.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1], startTick: 0, agent: 0);
        table.Reserve([2, 1], startTick: 0, agent: 1);

        Assert.True(table.IsSwap(from: 1, to: 2, tick: 0, agent: 0));
    }

    [Fact]
    public void AnAgentMayReserveOverItsOwnEarlierPlan()
    {
        // Replanning replaces what the agent held, cell for cell, and must never
        // trip over itself.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2, 3], startTick: 0, agent: 0);

        table.Reserve([1, 2, 4], startTick: 0, agent: 0);

        Assert.Equal(0, table.HolderOf(2, 1));
        Assert.Equal(0, table.HolderOf(4, 2));
        Assert.Equal(-1, table.HolderOf(3, 2));
    }
}
