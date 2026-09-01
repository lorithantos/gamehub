namespace Nav.Core.Tests;

/// <summary>
/// Detachment on the real seam: a member leaves its group for an errand of its
/// own and comes back, and the group model does not notice.
/// </summary>
/// <remarks>
/// The spike question this answers is whether detachment needs membership to
/// change. It does not: an errand is a goal and a field, the member stays in
/// the group, and the seam merely stops listing it as on station. Confinement,
/// the per-group claim assertion, and every existing pass are untouched.
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

    /// <summary>
    /// Gathers normally, except that on its first pass it sends the lowest member
    /// to <paramref name="errand"/>, and five ticks after that member arrives it
    /// recalls it. Records what the seam said about the runner while it was away.
    /// </summary>
    private sealed class SendOneAway(int errand) : GatherDoctrine
    {
        public int Runner { get; private set; } = -1;
        public int DispatchedAt { get; private set; } = -1;
        public int ArrivedAt { get; private set; } = -1;
        public int RecalledAt { get; private set; } = -1;
        public bool ListedOnStationWhileAway { get; private set; }
        public bool ListedDispatchedWhileAway { get; private set; }

        public override void Advance(IGroupOps ops)
        {
            if (Runner < 0)
            {
                Runner = ops.Members[0];
                ops.Dispatch(Runner, errand);
                DispatchedAt = ops.CurrentTick;
            }
            else if (RecalledAt < 0)
            {
                ListedOnStationWhileAway |= ops.Members.Contains(Runner);
                ListedDispatchedWhileAway |= ops.Dispatched.Contains(Runner) && ops.ErrandOf(Runner) == errand;

                if (ops.CellOf(Runner) == errand)
                {
                    if (ArrivedAt < 0)
                    {
                        ArrivedAt = ops.CurrentTick;
                    }
                    else if (ops.CurrentTick >= ArrivedAt + 5)
                    {
                        ops.Recall(Runner);
                        RecalledAt = ops.CurrentTick;
                    }
                }
            }

            base.Advance(ops);
        }
    }

    [Fact]
    public void AMemberCanLeaveOnAnErrandAndRejoinTheRing()
    {
        var (system, grid) = Scene(agents: 4);
        var errand = grid.Index(8, 8);
        var doctrine = new SendOneAway(errand);

        system.Order([0, 1, 2, 3], grid.Index(4, 4), doctrine);
        for (var tick = 0; tick < 200; tick++)
        {
            system.Tick();
        }

        // The runner went, was listed as away and never as on station, and came back.
        Assert.True(doctrine.ArrivedAt > doctrine.DispatchedAt, "the runner never reached its errand");
        Assert.True(doctrine.RecalledAt > doctrine.ArrivedAt, "the runner was never recalled");
        Assert.False(doctrine.ListedOnStationWhileAway, "a dispatched member appeared in Members");
        Assert.True(doctrine.ListedDispatchedWhileAway, "a dispatched member was missing from Dispatched");

        // And the group is whole again: everyone parked, the runner on a ring cell
        // rather than its errand, and no two members aimed at one cell.
        var agents = system.Agents;
        Assert.All(agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
        Assert.NotEqual(errand, agents[doctrine.Runner].Goal);
        Assert.Equal(agents.Count, agents.Select(a => a.Goal).Distinct().Count());
    }

    /// <summary>Records who the seam lists as on station, once.</summary>
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
        // never list it on station and no pass would ever claim for it.
        var (system, grid) = Scene(agents: 4);
        var doctrine = new SendOneAway(grid.Index(8, 8));

        system.Order([0, 1, 2], grid.Index(4, 4), doctrine);
        system.Tick();
        Assert.Equal(0, doctrine.Runner);

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
        ArgumentOutOfRangeException? refusal = null;
        var doctrine = new Probe(ops =>
        {
            try { ops.Dispatch(ops.Members[0], grid.CellCount + 5); }
            catch (ArgumentOutOfRangeException ex) { refusal = ex; }
        });

        system.Order([0, 1], grid.Index(4, 4), doctrine);
        system.Tick();

        Assert.NotNull(refusal);
    }

    /// <summary>Runs one action on the first pass and nothing after.</summary>
    private sealed class Probe(Action<IGroupOps> action) : GroupDoctrine
    {
        private bool _done;

        public override void Advance(IGroupOps ops)
        {
            if (_done) { return; }

            _done = true;
            action(ops);
        }
    }
}
