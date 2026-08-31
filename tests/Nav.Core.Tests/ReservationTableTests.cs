namespace Nav.Core.Tests;

/// <summary>
/// The reservation table on its own, before anything plans against it.
/// </summary>
public sealed class ReservationTableTests
{
    private const int Cells = 20;
    private const int Horizon = 8;

    private static ReservationTable New() => new(Cells, Horizon);

    [Fact]
    public void AFreshTableHoldsNothing()
    {
        var table = New();

        Assert.Equal(0, table.CurrentTick);
        for (var tick = 0; tick < Horizon; tick++)
        {
            Assert.True(table.IsFree(cell: 3, tick, agent: 0));
            Assert.Equal(-1, table.HolderOf(cell: 3, tick));
        }
    }

    [Fact]
    public void AReservedPathOccupiesOneCellPerTick()
    {
        var table = New();

        table.Reserve([1, 2, 3], startTick: 0, agent: 7);

        Assert.Equal(7, table.HolderOf(1, 0));
        Assert.Equal(7, table.HolderOf(2, 1));
        Assert.Equal(7, table.HolderOf(3, 2));

        // Not at the wrong times.
        Assert.Equal(-1, table.HolderOf(1, 1));
        Assert.Equal(-1, table.HolderOf(3, 0));
    }

    [Fact]
    public void AnArrivedAgentKeepsHoldingItsGoalForTheRestOfTheWindow()
    {
        // The most common way a reservation table is quietly wrong: a unit that
        // stopped moving stops reserving, and everyone else plans through it.
        var table = New();

        table.Reserve([1, 2], startTick: 0, agent: 4);

        for (var tick = 1; tick < Horizon; tick++)
        {
            Assert.Equal(4, table.HolderOf(2, tick));
        }
    }

    [Fact]
    public void APlanReachingTheWindowEdgeDoesNotOverHold()
    {
        var table = New();
        var path = Enumerable.Range(0, Horizon).ToArray();

        table.Reserve(path, startTick: 0, agent: 1);

        Assert.Equal(1, table.HolderOf(path[^1], Horizon - 1));

        // Beyond the horizon nothing is reserved by anybody, held or not.
        Assert.Equal(-1, table.HolderOf(path[^1], Horizon));
    }

    [Fact]
    public void APlanStartingLaterLeavesTheEarlierTicksFree()
    {
        var table = New();

        table.Reserve([5, 6], startTick: 3, agent: 2);

        Assert.True(table.IsFree(5, 0, agent: 9));
        Assert.True(table.IsFree(5, 2, agent: 9));
        Assert.Equal(2, table.HolderOf(5, 3));
    }

    [Fact]
    public void AnAgentNeverConflictsWithItself()
    {
        // Otherwise replanning is blocked by the plan it is replacing.
        var table = New();
        table.Reserve([1, 2, 3], startTick: 0, agent: 5);

        Assert.True(table.IsFree(2, 1, agent: 5));
        Assert.False(table.IsFree(2, 1, agent: 6));
    }

    [Fact]
    public void BeyondTheHorizonEverythingReadsFree()
    {
        var table = New();
        table.Reserve([1, 1, 1], startTick: 0, agent: 0);

        Assert.True(table.IsFree(1, Horizon, agent: 1));
        Assert.True(table.IsFree(1, Horizon + 100, agent: 1));
    }

    [Fact]
    public void QueryingBehindTheWindowIsAnError()
    {
        var table = New();
        table.Advance(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.IsFree(1, tick: 2, agent: 0));
    }

    [Fact]
    public void ReservingBehindTheWindowIsAnError()
    {
        var table = New();
        table.Advance(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.Reserve([1], startTick: 1, agent: 0));
    }

    // --- the swap, which occupancy alone cannot see -------------------------

    [Fact]
    public void AHeadOnExchangeIsASwap()
    {
        var table = New();

        // Agent 1 walks 6 -> 5 across the tick beginning at 0.
        table.Reserve([6, 5], startTick: 0, agent: 1);

        // Agent 2 attempting 5 -> 6 across the same tick passes through it.
        Assert.True(table.IsSwap(from: 5, to: 6, tick: 0, agent: 2));
    }

    [Fact]
    public void FollowingSomeoneInTheSameDirectionIsNotASwap()
    {
        var table = New();
        table.Reserve([5, 6], startTick: 0, agent: 1);

        // Agent 2 moving 4 -> 5 is walking into the cell agent 1 has left.
        Assert.False(table.IsSwap(from: 4, to: 5, tick: 0, agent: 2));
    }

    [Fact]
    public void MovingIntoACellItsHolderIsStayingInIsNotASwap()
    {
        // Not a swap -- it is a vertex conflict, which IsFree is responsible for.
        // Keeping the two checks distinct is what makes a failure name itself.
        var table = New();
        table.Reserve([5], startTick: 0, agent: 1);

        Assert.False(table.IsSwap(from: 4, to: 5, tick: 0, agent: 2));
        Assert.False(table.IsFree(5, 1, agent: 2));
    }

    [Fact]
    public void AnAgentCannotSwapWithItself()
    {
        var table = New();
        table.Reserve([6, 5], startTick: 0, agent: 1);

        Assert.False(table.IsSwap(from: 5, to: 6, tick: 0, agent: 1));
    }

    // --- the ring ------------------------------------------------------------

    [Fact]
    public void AdvanceForgetsTheTickThatFallsOffTheBack()
    {
        var table = New();
        table.Reserve([1, 2, 3], startTick: 0, agent: 0);

        table.Advance();

        Assert.Equal(1, table.CurrentTick);
        Assert.Equal(0, table.HolderOf(2, 1));

        // Tick 0 is gone; its ring slot is now the far end of the window.
        Assert.True(table.IsFree(1, Horizon, agent: 9));
    }

    [Fact]
    public void AdvancingPastTheWholeHorizonForgetsEverything()
    {
        var table = New();
        table.Reserve([1, 2, 3], startTick: 0, agent: 0);

        table.Advance(Horizon + 5);

        Assert.Equal(Horizon + 5, table.CurrentTick);
        for (var tick = table.CurrentTick; tick < table.CurrentTick + Horizon; tick++)
        {
            Assert.Equal(-1, table.HolderOf(2, tick));
        }
    }

    // --- release, and the aliasing it has to survive -------------------------

    [Fact]
    public void ReleaseDropsOnlyThatAgentsHolds()
    {
        var table = New();
        table.Reserve([1, 2], startTick: 0, agent: 0);
        table.Reserve([5, 6], startTick: 0, agent: 1);

        table.Release(0);

        Assert.Equal(-1, table.HolderOf(1, 0));
        Assert.Equal(1, table.HolderOf(5, 0));
    }

    [Fact]
    public void ReleasingAnUnknownAgentIsHarmless()
    {
        var table = New();
        table.Reserve([1], startTick: 0, agent: 0);

        table.Release(99);

        Assert.Equal(0, table.HolderOf(1, 0));
    }

    [Fact]
    public void ReserveReplacesWhateverTheAgentHeldBefore()
    {
        var table = New();
        table.Reserve([1, 2], startTick: 0, agent: 0);

        table.Reserve([7, 8], startTick: 0, agent: 0);

        Assert.Equal(-1, table.HolderOf(1, 0));
        Assert.Equal(0, table.HolderOf(7, 0));
    }

    [Fact]
    public void ReleaseDoesNotClearAReservationThatMerelySharesARingSlot()
    {
        // The aliasing hazard. Tick T and tick T+Horizon are the same ring slot.
        // Agent 0's stale record of tick 0 must not delete agent 1's live
        // reservation of tick Horizon, which sits in the same place.
        var table = New();
        table.Reserve([3], startTick: 0, agent: 0);

        table.Advance(Horizon);
        table.Reserve([3], startTick: Horizon, agent: 1);

        table.Release(0);

        Assert.Equal(1, table.HolderOf(3, Horizon));
    }

    [Fact]
    public void ReleaseDoesNotClearACellAnotherAgentHasSinceTakenOver()
    {
        // Reserve does not police conflicts -- that is the planner's job -- so an
        // agent's own record can name a cell somebody else now holds. Releasing
        // must not take it from them.
        var table = New();
        table.Reserve([3], startTick: 2, agent: 0);
        table.Reserve([3], startTick: 2, agent: 1);

        table.Release(0);

        Assert.Equal(1, table.HolderOf(3, 2));
    }

    [Fact]
    public void AHorizonBelowTwoIsRefused()
    {
        // A swap is a question about two consecutive ticks; one tick of lookahead
        // cannot answer it.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReservationTable(Cells, horizon: 1));
    }
}
