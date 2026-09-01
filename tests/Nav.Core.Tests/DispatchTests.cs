namespace Nav.Core.Tests;

/// <summary>
/// Detachment on the real system: an agent leaves its formation for an errand of
/// its own and comes back, and the formation keeps its place.
/// </summary>
/// <remarks>
/// Two things are pinned here. That an errand is a goal and a field key rather
/// than a change of membership, so confinement, the per-group claim assertion
/// and every existing pass are untouched. And that the verbs are the SYSTEM's,
/// not the movement doctrine's: sending a unit away is a decision membership
/// makes above the movement layer, and the formation only reports who is out.
/// </remarks>
public sealed class DispatchTests
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

    private static (MovementSystem System, Grid Grid) Scene(int agents)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        for (var i = 0; i < agents; i++)
        {
            system.AddAgent(grid.Index(i, 0));
        }

        return (system, grid);
    }

    private static void TickUntil(MovementSystem system, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++)
        {
            system.Tick();
        }
    }

    /// <summary>
    /// Gathers normally, and on every pass records how the seam listed
    /// <paramref name="runner"/> while its errand was live.
    /// </summary>
    private sealed class WatchRunner(int runner) : GatherDoctrine
    {
        public int PassesWhileAway { get; private set; }
        public bool ListedOnStationWhileAway { get; private set; }
        public bool ListedDispatchedWhileAway { get; private set; }

        public override void Advance(IGroupOps ops)
        {
            var errand = ops.ErrandOf(runner);
            if (errand >= 0)
            {
                PassesWhileAway++;
                ListedOnStationWhileAway |= ops.Members.Contains(runner);
                ListedDispatchedWhileAway |= ops.Dispatched.Contains(runner);
            }

            base.Advance(ops);
        }
    }

    [Fact]
    public void AnAgentCanLeaveOnAnErrandAndRejoinTheRing()
    {
        var (system, grid) = Scene(agents: 4);
        var errand = grid.Index(8, 8);
        var watcher = new WatchRunner(runner: 0);

        system.Order([0, 1, 2, 3], grid.Index(4, 4), watcher);
        system.Tick();

        system.Dispatch(0, errand);
        TickUntil(system, () => system.Agents[0].Cell == errand, limit: 100);
        Assert.Equal(errand, system.Agents[0].Cell);

        for (var tick = 0; tick < 5; tick++)
        {
            system.Tick();
        }

        system.Recall(0);
        TickUntil(system, () => system.Agents.All(a => a.Arrived), limit: 150);

        // While away: listed as dispatched on every pass, on station on none.
        Assert.True(watcher.PassesWhileAway > 0, "the formation never saw the runner as away");
        Assert.True(watcher.ListedDispatchedWhileAway);
        Assert.False(watcher.ListedOnStationWhileAway, "a dispatched agent appeared in Members");

        // Back: everyone parked, the runner on a ring cell rather than its errand,
        // and no two members aimed at one cell.
        var agents = system.Agents;
        Assert.All(agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
        Assert.NotEqual(errand, agents[0].Goal);
        Assert.Equal(agents.Count, agents.Select(a => a.Goal).Distinct().Count());
    }

    /// <summary>Records who the seam lists as on station and away, once.</summary>
    private sealed class ListMembers : GroupDoctrine
    {
        public IReadOnlyList<int>? Members { get; private set; }
        public IReadOnlyList<int>? Dispatched { get; private set; }

        public override void Advance(IGroupOps ops)
        {
            Members ??= ops.Members;
            Dispatched ??= ops.Dispatched;
        }
    }

    [Fact]
    public void ANewOrderClearsAnErrand()
    {
        // A dispatched unit re-ordered into another group must arrive there as an
        // ordinary member. If the errand survived the order, its new group would
        // never list it on station and no pass would ever claim for it. This is
        // also the C&C rule: a group move takes the detached unit with it.
        var (system, grid) = Scene(agents: 4);

        system.Order([0, 1, 2], grid.Index(4, 4));
        system.Dispatch(0, grid.Index(8, 8));
        system.Tick();

        var probe = new ListMembers();
        system.Order([0, 3], grid.Index(0, 8), probe);
        system.Tick();

        Assert.Equal([0, 3], probe.Members);
        Assert.Empty(probe.Dispatched!);
    }

    [Fact]
    public void AnErrandMustEndOnTheMap()
    {
        var (system, grid) = Scene(agents: 2);
        system.Order([0, 1], grid.Index(4, 4));

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Dispatch(0, grid.CellCount + 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Dispatch(0, -1));
    }

    [Fact]
    public void OnlyAnOrderedAgentCanBeDispatched()
    {
        // An errand is a departure FROM somewhere: an agent that was never
        // ordered has no formation to return to, so there is nothing to recall it
        // to and the call is refused rather than turned into a bare move.
        var (system, grid) = Scene(agents: 2);

        Assert.Throws<InvalidOperationException>(() => system.Dispatch(0, grid.Index(8, 8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Dispatch(7, grid.Index(8, 8)));
    }

    [Fact]
    public void RecallingAnAgentThatIsNotAwayChangesNothing()
    {
        var (system, grid) = Scene(agents: 2);
        system.Order([0, 1], grid.Index(4, 4));
        for (var tick = 0; tick < 40; tick++)
        {
            system.Tick();
        }

        var before = system.Agents.Select(a => (a.Cell, a.Goal)).ToArray();
        system.Recall(0);
        system.Tick();

        Assert.Equal(before, system.Agents.Select(a => (a.Cell, a.Goal)));
    }
}
