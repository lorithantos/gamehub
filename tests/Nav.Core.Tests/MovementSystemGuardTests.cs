namespace Nav.Core.Tests;

/// <summary>
/// The two guards that keep a bad call from outliving itself: an order that
/// refuses before it mutates, and a claim that does not retract what it never
/// held.
/// </summary>
/// <remarks>
/// Both were found by the overnight adversarial review, and neither was reachable
/// by any existing test -- the first because no test ever passed a bad id, the
/// second because it needs three members near the ring at once and only shows up
/// as an arrival count.
/// </remarks>
public sealed class MovementSystemGuardTests
{
    private const string Room =
        """
        type octile
        height 9
        width 9
        map
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        """;

    private static (MovementSystem System, Grid Grid) Scene(int agents = 3)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        for (var i = 0; i < agents; i++)
        {
            system.AddAgent(grid.Index(i, 0));
        }

        return (system, grid);
    }

    [Fact]
    public void AnOrderNamingAnUnknownAgentIsRefused()
    {
        var (system, grid) = Scene();

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Order([0, 99], grid.Index(4, 4)));
    }

    [Fact]
    public void ARefusedOrderLeavesTheSystemTickingForever()
    {
        // THE ACTUAL DEFECT. Refusing was never the problem -- the problem was
        // refusing halfway. Agent 0 had already been re-goaled onto a group that
        // was never registered, its old group was emptied and never pruned, and
        // ElectLeader dereferenced member zero of that empty group on every later
        // tick. One unknown id turned a catchable exception into a system that
        // could not advance again.
        var (system, grid) = Scene();
        system.Order([0, 1, 2], grid.Index(4, 4));

        for (var tick = 0; tick < 5; tick++)
        {
            system.Tick();
        }

        var before = system.Agents.Select(a => a.Goal).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Order([0, 99], grid.Index(8, 8)));

        // Nothing was half-applied: every goal is where the refused order found it.
        Assert.Equal(before, system.Agents.Select(a => a.Goal));

        // And the system still works, which is the whole point of the test.
        for (var tick = 0; tick < 40; tick++)
        {
            system.Tick();
        }

        Assert.Equal(3, system.Agents.Count);
    }

    [Fact]
    public void AValidOrderAfterARefusedOneStillWorks()
    {
        var (system, grid) = Scene();

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Order([0, 42], grid.Index(4, 4)));

        system.Order([0, 1, 2], grid.Index(4, 4));
        for (var tick = 0; tick < 60; tick++)
        {
            system.Tick();
        }

        Assert.All(system.Agents, a => Assert.False(a.Stuck));
    }

    /// <summary>
    /// Claims two slots on the real seam and reports whether the first survived.
    /// </summary>
    /// <remarks>
    /// It has to be a doctrine and not a FakeGroupOps: the fake mirrors the fixed
    /// behaviour, so a test driving the fake asserts the fake and passes whatever
    /// production does. Only a real <c>GroupOps</c>, handed out by a real tick, can
    /// fail against the old code.
    /// </remarks>
    private sealed class ClaimTwo : GroupDoctrine
    {
        private bool _done;

        /// <summary>Null until the pass has run; then whether slot 0 is still claimed.</summary>
        public bool? FirstClaimSurvived { get; private set; }

        public override void Advance(IGroupOps ops)
        {
            if (_done) { return; }

            _done = true;
            var first = ops.Slots[0];
            ops.ClaimSlot(ops.Members[0], first);
            ops.ClaimSlot(ops.Members[1], ops.Slots[1]);
            FirstClaimSurvived = ops.IsClaimed(first);
        }
    }

    [Fact]
    public void ClaimingASlotDoesNotRetractAClaimTheMemberNeverHeld()
    {
        // Order gives every member of a group the SAME walking target -- slots[0]
        // -- and no slot. So when the second member claims a different cell, its
        // stale Goal is the first member's legitimate claim, and the retraction
        // used to delete it. A third member then found that cell unclaimed and took
        // it, and two members held one cell for good.
        var (system, grid) = Scene();
        var doctrine = new ClaimTwo();

        system.Order([0, 1, 2], grid.Index(4, 4), doctrine);
        system.Tick();

        Assert.True(doctrine.FirstClaimSurvived, "the second claim retracted the first member's slot");
    }

    [Fact]
    public void NoTwoMembersOfASettledGroupShareAGoal()
    {
        // The consequence the guard exists to prevent, asserted on the outcome
        // rather than the mechanism: run a gather to completion and no two units
        // may be aimed at one cell.
        var (system, grid) = Scene(agents: 6);

        system.Order([0, 1, 2, 3, 4, 5], grid.Index(4, 4));
        for (var tick = 0; tick < 120; tick++)
        {
            system.Tick();
        }

        var goals = system.Agents.Select(a => a.Goal).ToArray();
        Assert.Equal(goals.Length, goals.Distinct().Count());
    }
}
