namespace Nav.Tactics.Tests;

/// <summary>
/// Membership above movement: a squad outlives every order, a group move takes a
/// detached member along, and a detached member comes back to its formation.
/// </summary>
public sealed class SquadTests
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

    private static void Run(Squad squad, MovementSystem system, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            squad.Advance(system);
            system.Tick();
        }
    }

    /// <summary>Runs each step on the tick it names, once.</summary>
    private sealed class Script(params (int Tick, Action<ISquadOps> Do)[] steps) : SquadDoctrine
    {
        public override void Advance(ISquadOps ops)
        {
            foreach (var (tick, act) in steps)
            {
                if (tick == ops.CurrentTick)
                {
                    act(ops);
                }
            }
        }
    }

    [Fact]
    public void AGroupMoveTakesADetachedMemberAlong()
    {
        // The C&C rule. Unit 0 is away at the corner when the squad is moved
        // again; it goes with the squad, its errand over, and settles on the new
        // ring like everybody else.
        var (system, grid) = Scene(agents: 4);
        var corner = grid.Index(8, 8);
        var second = grid.Index(1, 7);
        var squad = new Squad("1", [0, 1, 2, 3], new Script(
            (0, ops => ops.MoveAll(grid.Index(4, 4))),
            (30, ops => ops.Detach(0, corner)),
            (90, ops => ops.MoveAll(second))));

        Run(squad, system, ticks: 220);

        Assert.Equal(second, squad.Anchor);
        Assert.Equal([0, 1, 2, 3], squad.Members);

        var agents = system.Agents;
        Assert.All(agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
        Assert.Equal(-1, agents[0].Errand);
        Assert.NotEqual(corner, agents[0].Cell);
        Assert.Equal(agents.Count, agents.Select(a => a.Goal).Distinct().Count());
    }

    /// <summary>
    /// Stations the squad, sends one member to the pad, and brings it back five
    /// ticks after it gets there -- recording what the seam said meanwhile.
    /// </summary>
    private sealed class RetreatAndReturn(int unit, int station, int pad) : SquadDoctrine
    {
        private int _arrivedAt = -1;

        public bool Returned { get; private set; }
        public bool AlwaysAMember { get; private set; } = true;
        public bool ListedAwayWhileOut { get; private set; }
        public bool ListedAwayAfterReturn { get; private set; }

        public override void Advance(ISquadOps ops)
        {
            AlwaysAMember &= ops.Members.Contains(unit);

            if (ops.CurrentTick == 0)
            {
                ops.MoveAll(station);
            }
            else if (ops.CurrentTick == 30)
            {
                ops.Detach(unit, pad);
            }
            else if (!Returned && ops.ErrandOf(unit) == pad)
            {
                ListedAwayWhileOut |= ops.Away.Contains(unit);
                if (ops.CellOf(unit) == pad)
                {
                    if (_arrivedAt < 0)
                    {
                        _arrivedAt = ops.CurrentTick;
                    }
                    else if (ops.CurrentTick >= _arrivedAt + 5)
                    {
                        ops.Rejoin(unit);
                        Returned = true;
                    }
                }
            }
            else if (Returned)
            {
                ListedAwayAfterReturn |= ops.Away.Contains(unit);
            }
        }
    }

    [Fact]
    public void ADetachedMemberStaysAMemberAndReturnsToItsFormation()
    {
        var (system, grid) = Scene(agents: 4);
        var pad = grid.Index(8, 8);
        var doctrine = new RetreatAndReturn(unit: 0, station: grid.Index(4, 4), pad);
        var squad = new Squad("1", [0, 1, 2, 3], doctrine);

        Run(squad, system, ticks: 250);

        Assert.True(doctrine.Returned, "the unit never reached the pad and came back");
        Assert.True(doctrine.AlwaysAMember);
        Assert.True(doctrine.ListedAwayWhileOut);
        Assert.False(doctrine.ListedAwayAfterReturn);

        var agents = system.Agents;
        Assert.All(agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
        Assert.NotEqual(pad, agents[0].Goal);
        Assert.Equal(agents.Count, agents.Select(a => a.Goal).Distinct().Count());
    }

    [Fact]
    public void MembershipOutlivesEveryOrder()
    {
        // The movement layer may re-group units however orders fall; the squad
        // is not consulted and does not change.
        var (system, grid) = Scene(agents: 4);
        var squad = new Squad("1", [0, 1, 2, 3], new Script((0, ops => ops.MoveAll(grid.Index(4, 4)))));
        Run(squad, system, ticks: 5);

        system.Order([0], grid.Index(8, 8));
        system.Order([1, 2], grid.Index(0, 8));
        system.Dispatch(3, grid.Index(8, 0));
        Run(squad, system, ticks: 5);

        Assert.Equal([0, 1, 2, 3], squad.Members);
    }

    [Fact]
    public void TheVerbsAreConfinedToTheSquad()
    {
        var (system, grid) = Scene(agents: 4);
        Exception? detach = null;
        Exception? rejoin = null;
        var squad = new Squad("1", [0, 1], new Script(
            (0, ops => ops.MoveAll(grid.Index(4, 4))),
            (1, ops =>
            {
                try { ops.Detach(2, grid.Index(8, 8)); } catch (ArgumentOutOfRangeException ex) { detach = ex; }
                try { ops.Rejoin(3); } catch (ArgumentOutOfRangeException ex) { rejoin = ex; }
            })));

        Run(squad, system, ticks: 3);

        Assert.NotNull(detach);
        Assert.NotNull(rejoin);
    }

    [Fact]
    public void DetachingBeforeAnyGroupMoveIsRefused()
    {
        // An errand is a departure from a formation, and a squad that was never
        // moved as a group has none. The refusal names the squad.
        var (system, grid) = Scene(agents: 2);
        InvalidOperationException? refusal = null;
        var squad = new Squad("1", [0, 1], new Script(
            (0, ops => { try { ops.Detach(0, grid.Index(8, 8)); } catch (InvalidOperationException ex) { refusal = ex; } })));

        Run(squad, system, ticks: 1);

        Assert.NotNull(refusal);
        Assert.Contains("'1'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASquadRefusesANegativeMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Squad("1", [0, -1], new Script()));
    }

    [Fact]
    public void ASortieLeavesAwayMembersOnTheirErrands()
    {
        // The difference between a group move and a doctrine's move. Unit 0 is
        // at the pad when the rest are sent elsewhere; it stays there, still
        // away, and the anchor is still the station.
        var (system, grid) = Scene(agents: 4);
        var station = grid.Index(4, 4);
        var pad = grid.Index(8, 8);
        var elsewhere = grid.Index(1, 7);
        var squad = new Squad("1", [0, 1, 2, 3], new Script(
            (0, ops => ops.MoveAll(station)),
            (30, ops => ops.Detach(0, pad)),
            (70, ops => ops.Sortie(elsewhere))));

        Run(squad, system, ticks: 160);

        var agents = system.Agents;
        Assert.Equal(station, squad.Anchor);
        Assert.Equal(pad, agents[0].Cell);
        Assert.True(agents[0].Away);
        Assert.All(agents.Skip(1), a => Assert.True(a.Arrived && Near(grid, a.Cell, elsewhere), $"agent {a.Id} is not parked near the sortie"));

        static bool Near(Grid g, int cell, int target) =>
            Movement.OctileDistance(g.ColumnOf(cell), g.RowOf(cell), g.ColumnOf(target), g.RowOf(target)) <= 2.0;
    }

    [Fact]
    public void RejoiningAfterASortieEntersTheFormationTheSquadIsInNow()
    {
        // Unit 0 left from the station. By the time it rejoins, its fellows have
        // sortied elsewhere; it must go to THEM, not back to the empty station.
        var (system, grid) = Scene(agents: 4);
        var station = grid.Index(4, 4);
        var pad = grid.Index(8, 8);
        var elsewhere = grid.Index(1, 7);
        var squad = new Squad("1", [0, 1, 2, 3], new Script(
            (0, ops => ops.MoveAll(station)),
            (30, ops => ops.Detach(0, pad)),
            (70, ops => ops.Sortie(elsewhere)),
            (130, ops => ops.Rejoin(0))));

        Run(squad, system, ticks: 300);

        var agents = system.Agents;
        Assert.All(agents, a => Assert.True(a.Arrived, $"agent {a.Id} did not arrive"));
        Assert.False(agents[0].Away);
        var d = Movement.OctileDistance(
            grid.ColumnOf(agents[0].Cell), grid.RowOf(agents[0].Cell), grid.ColumnOf(elsewhere), grid.RowOf(elsewhere));
        Assert.True(d <= 3.0, $"unit 0 rejoined at distance {d:F1} from the squad");
        Assert.Equal(agents.Count, agents.Select(a => a.Goal).Distinct().Count());
    }

    /// <summary>A world a test writes down: health per unit, hostile cells, repair cells.</summary>
    private sealed class ScriptedPerception : IPerception
    {
        public Dictionary<int, double> Health { get; } = [];
        public Dictionary<int, int> Rank { get; } = [];
        public List<int> HostileCells { get; } = [];
        public List<int> RepairCells { get; } = [];

        public double HealthOf(int agent) => Health.TryGetValue(agent, out var h) ? h : 1.0;
        public int RankOf(int agent) => Rank.GetValueOrDefault(agent);
        public IReadOnlyList<int> Hostiles => HostileCells;
        public IReadOnlyList<int> RepairPoints => RepairCells;
    }

    [Fact]
    public void PerceptionReadsThroughTheView()
    {
        var (system, grid) = Scene(agents: 2);
        var world = new ScriptedPerception { Health = { [0] = 0.25 }, HostileCells = { grid.Index(7, 7) }, RepairCells = { grid.Index(0, 8) } };
        double? health = null;
        IReadOnlyList<int>? hostiles = null;
        IReadOnlyList<int>? repair = null;
        double? distance = null;
        var squad = new Squad("1", [0, 1], new Script((0, ops =>
        {
            health = ops.HealthOf(0);
            hostiles = ops.Hostiles;
            repair = ops.RepairPoints;
            distance = ops.Distance(grid.Index(0, 0), grid.Index(3, 4));
        })));

        squad.Advance(system, world);

        Assert.Equal(0.25, health);
        Assert.Equal([grid.Index(7, 7)], hostiles);
        Assert.Equal([grid.Index(0, 8)], repair);
        Assert.Equal(Movement.OctileDistance(0, 0, 3, 4), distance);
    }

    [Fact]
    public void AQuietWorldIsTheDefault()
    {
        var (system, _) = Scene(agents: 1);
        double? health = null;
        var squad = new Squad("1", [0], new Script((0, ops => health = ops.HealthOf(0))));

        squad.Advance(system);

        Assert.Equal(1.0, health);
    }
}
