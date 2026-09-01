namespace Nav.Core.Tests;

/// <summary>
/// The reservation ring refuses to overwrite another agent's live booking.
/// </summary>
/// <remarks>
/// Not a behaviour the search can reach today: every committed plan was
/// validated cell by cell against the table, and 320 fuzzed orderings across
/// three budgets never produced an overwrite. These pin the assertion itself, so
/// that if a future change makes it reachable the failure is an exception at the
/// commit that made the bad reservation rather than a collision ticks later.
/// </remarks>
public sealed class ReservationOverwriteTests
{
    private const int Cells = 20;
    private const int Horizon = 8;

    [Fact]
    public void ReservingACellAnotherAgentHoldsAtThatTickThrows()
    {
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2, 3], startTick: 0, agent: 0);

        // Agent 1 wants cell 2 at tick 1, which agent 0 is walking through.
        var ex = Assert.Throws<InvalidOperationException>(() => table.Reserve([5, 2, 6], startTick: 0, agent: 1));

        Assert.Contains("agent 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("cell 2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("tick 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("agent 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservingACellAnotherAgentIsParkedOnThrows()
    {
        // The park lives in _parked rather than the ring, which is exactly why the
        // check goes through Occupant: a ring-only check would let this through.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2], startTick: 0, agent: 0);   // parks on 2 from tick 1

        Assert.Throws<InvalidOperationException>(() => table.Reserve([7, 8, 2], startTick: 0, agent: 1));
    }

    [Fact]
    public void AnAgentMayReserveOverItsOwnEarlierPlan()
    {
        // Replanning replaces what the agent held, cell for cell, and must never
        // trip over itself. Reserve releases the caller first, which is what makes
        // a same-agent overlap impossible to see at Mark -- but the property is
        // what matters, so it is asserted from the outside.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2, 3], startTick: 0, agent: 0);

        table.Reserve([1, 2, 4], startTick: 0, agent: 0);

        Assert.Equal(0, table.HolderOf(2, 1));
        Assert.Equal(0, table.HolderOf(4, 2));
        Assert.Equal(-1, table.HolderOf(3, 2));
    }

    [Fact]
    public void ARefusedReservationIsFullyReversibleAndHarmsNobodyElse()
    {
        // Agent 0 crosses cell 3 at tick 2. Agent 1's path reaches cell 3 at tick
        // 2 too, after cells 10 and 11 were already marked for it. Two things must
        // hold. The incumbent is untouched: agent 0's booking on cell 3 survives
        // the refusal. And the half-written marks are not phantoms: they were
        // recorded against agent 1, so Release clears them, and the table cannot
        // be left holding slots nothing can free.
        var table = new ReservationTable(Cells, Horizon);
        table.Reserve([1, 2, 3, 4], startTick: 0, agent: 0);

        Assert.Throws<InvalidOperationException>(() => table.Reserve([10, 11, 3], startTick: 0, agent: 1));

        Assert.Equal(0, table.HolderOf(3, 2));

        table.Release(1);

        Assert.Equal(-1, table.HolderOf(10, 0));
        Assert.Equal(-1, table.HolderOf(11, 1));
        Assert.Equal(0, table.HolderOf(3, 2));
    }
}
