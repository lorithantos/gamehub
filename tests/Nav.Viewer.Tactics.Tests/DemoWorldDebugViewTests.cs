using Nav.Tactics.Interfaces;

namespace Nav.Viewer.Tactics.Tests;

/// <summary>
/// The seam a panel reads a fight through: rows that carry the number AND what
/// it means, taken as of the last clock edge and never provoking one.
/// </summary>
/// <remarks>
/// The assertions look for what a row SAYS rather than for its exact wording,
/// which is what <see cref="IDebugView"/> asks for -- the wording is free to
/// change with any commit that changes what is worth looking at, and a test that
/// froze it would take that away.
/// <para>
/// What is pinned is the other thing: that the value carries its own units and
/// its own meaning, so a human reading a panel does not have to go and find a
/// clock or a hit-point table to know what they are looking at.
/// </para>
/// </remarks>
public sealed class DemoWorldDebugViewTests
{
    private static string ConfigDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config");

    private static Combat Shipped() => Combat.From(Ini.FromFile(Path.Combine(ConfigDir, "combat.ini")));

    private const string Room =
        """
        type octile
        height 13
        width 29
        map
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        """;

    /// <summary>
    /// Two riflemen three cells apart on opposite sides, a pad, and a scripted
    /// threat close enough to expose one of them and not the other.
    /// </summary>
    /// <remarks>
    /// Under fog, and with everything enlisted, because a fog world refuses to
    /// settle with a blind unit standing.
    /// </remarks>
    private static (MovementSystem System, Grid Grid, DemoWorld World) Scene(bool fog = true)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(
            grid,
            repairPerTick: 0.02,
            exposureRadius: 4.0,
            damagePerTick: 0.01,
            selfHealPerTick: 0.005,
            combat: Shipped(),
            fog: fog);

        world.RepairCells.Add(grid.Index(10, 4));
        world.HostileCells.Add(grid.Index(10, 9));

        world.Enlist(system.AddAgent(grid.Index(10, 6), 0), "rifleman");
        world.Enlist(system.AddAgent(grid.Index(13, 6), 1), "rifleman");

        world.Listen(system);
        return (system, grid, world);
    }

    /// <summary>Whole ticks -- the system's step and the world settling.</summary>
    private static void Play(MovementSystem system, DemoWorld world, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            system.Tick();
            world.Settle();
        }
    }

    private static string Row(IReadOnlyList<DebugRow> rows, string group, string key)
    {
        var found = rows.Where(r => r.Group == group && r.Key == key).ToList();
        Assert.True(found.Count == 1, $"expected one '{group}/{key}' row, found {found.Count} in:\n{Dump(rows)}");
        return found[0].Value;
    }

    /// <summary>A row's explanatory half, which is a separate member from its value.</summary>
    private static string Note(IReadOnlyList<DebugRow> rows, string group, string key)
    {
        var found = rows.Where(r => r.Group == group && r.Key == key).ToList();
        Assert.True(found.Count == 1, $"expected one '{group}/{key}' row, found {found.Count} in:\n{Dump(rows)}");
        Assert.True(found[0].Note is not null, $"the '{group}/{key}' row carries no note");
        return found[0].Note!;
    }

    private static string Dump(IReadOnlyList<DebugRow> rows) =>
        string.Join("\n", rows.Select(r => $"{r.Group,-11} {r.Key,-14} {r.Value,-24} {r.Note}"));

    [Fact]
    public void AUnitWithAKitATargetAndASideUnderFogSaysAllOfIt()
    {
        var (system, _, world) = Scene();
        Play(system, world, 4);

        var rows = new DemoWorldDebugView(world).DebugFor(0).Describe();
        var report = Dump(rows);

        // Health as a fraction AND in hit points, because the seam carries the
        // fraction and the kit carries what a fraction is worth. Either one on
        // its own is a number a reader has to go and pair up by hand.
        Assert.Contains("%", Row(rows, "Condition", "health"), StringComparison.Ordinal);
        var health = Note(rows, "Condition", "health");
        Assert.Contains("50", health, StringComparison.Ordinal);
        Assert.Contains("hit points", health, StringComparison.Ordinal);

        // The kit's own numbers, all seven of them, each saying what it measures.
        Assert.Equal("rifleman", Row(rows, "Kit", "name"));
        Assert.Equal("unarmoured", Row(rows, "Kit", "armour"));

        Assert.Contains("rifle", Row(rows, "Kit", "weapon"), StringComparison.Ordinal);
        var weapon = Note(rows, "Kit", "weapon");
        Assert.Contains("6", weapon, StringComparison.Ordinal);
        Assert.Contains("single target", weapon, StringComparison.Ordinal);

        Assert.Contains("4", Row(rows, "Kit", "range"), StringComparison.Ordinal);
        Assert.Contains("it can shoot", Note(rows, "Kit", "range"), StringComparison.Ordinal);

        // Sight against reach, which is the pair the config says is the whole
        // argument for a scout: a bare 6 beside a bare 4 is two numbers.
        Assert.Contains("6", Row(rows, "Kit", "sight"), StringComparison.Ordinal);
        Assert.Contains("2 past its own reach", Note(rows, "Kit", "sight"), StringComparison.Ordinal);

        Assert.Equal("2", Row(rows, "Kit", "rate of fire"));
        Assert.Equal("shots a second", Note(rows, "Kit", "rate of fire"));
        Assert.Contains("50", Row(rows, "Kit", "hit points"), StringComparison.Ordinal);

        // Standing three cells apart with four cells of reach, so each is the
        // other's target and the row names it and its side.
        Assert.Equal(1, world.TargetOf(0));
        Assert.Equal("unit 1", Row(rows, "Fight", "target"));
        Assert.Contains("side 1", Note(rows, "Fight", "target"), StringComparison.Ordinal);

        // The side is what the perception rows are read through, so it has to be
        // on the same card as them.
        Assert.Contains("0", Row(rows, "Condition", "side"), StringComparison.Ordinal);

        // Rank and contribution: the points banked and how far off the next rank
        // is, because a bare point count means nothing without the table.
        Assert.Contains("of 2", Row(rows, "Condition", "rank"), StringComparison.Ordinal);
        Assert.Contains("points", Row(rows, "Condition", "contribution"), StringComparison.Ordinal);
        var contribution = Note(rows, "Condition", "contribution");
        Assert.Contains("banked", contribution, StringComparison.Ordinal);
        Assert.Contains("short of rank 1", contribution, StringComparison.Ordinal);

        // Exposure: unit 0 is three cells from the threat and the radius is four,
        // so it has been standing in it since the first edge.
        Assert.Equal("4 ticks", Row(rows, "Condition", "exposed"));
        Assert.Contains("scripted threat", Note(rows, "Condition", "exposed"), StringComparison.Ordinal);

        // What this side can see and what it remembers, which is the reason any
        // of this exists. The enemy and the scripted threat are both in view; only
        // the enemy is remembered, because a threat has no id to hang a memory on.
        Assert.Equal("2 cells", Row(rows, "Perception", "can see"));
        Assert.Equal("hostile to side 0", Note(rows, "Perception", "can see"));
        Assert.Equal("1 enemy units", Row(rows, "Perception", "remembers"));
        Assert.Equal("1", Row(rows, "Perception", "pads in view"));
        Assert.Equal("it can plan to reach", Note(rows, "Perception", "pads in view"));

        // Rows arrive already in group order, because a panel renders headings by
        // watching this change rather than by sorting.
        Assert.Equal(
            ["Squad", "Condition", "Kit", "Fight", "Perception"],
            rows.Select(r => r.Group).Distinct().ToList());

        // Every value says something, never a bare number with no units on it.
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Value), report));
    }

    [Fact]
    public void AUnitShootingAtNobodyReadsAsHavingNone()
    {
        var (system, grid, world) = Scene();

        // Twelve cells apart, and a rifleman reaches four. Nothing is in range,
        // so Fire chooses nobody and the row has to say so rather than print -1.
        system.Order([1], grid.Index(25, 6));
        for (var tick = 0; tick < 60 && system.Agents[1].Cell != grid.Index(25, 6); tick++)
        {
            system.Tick();
            world.Settle();
        }

        Assert.Equal(grid.Index(25, 6), system.Agents[1].Cell);
        Assert.Equal(-1, world.TargetOf(0));

        var target = Row(new DemoWorldDebugView(world).DebugFor(0).Describe(), "Fight", "target");
        Assert.Contains("none", target, StringComparison.Ordinal);
        Assert.DoesNotContain("-1", target, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAnswerIsStampedWithTheEdgeItWasTakenAt()
    {
        var (system, _, world) = Scene();
        var view = new DemoWorldDebugView(world);

        // The opening edge is Listen, before any tick has ended.
        Assert.Equal(0, world.View.AsOf);
        Assert.Contains("tick 0", Row(view.Describe(), "World", "as of"), StringComparison.Ordinal);
        Assert.Contains("tick 0", Row(view.DebugFor(0).Describe(), "Perception", "as of"), StringComparison.Ordinal);

        Play(system, world, 5);

        // Five edges later, both stamps have followed the clock and neither the
        // world row nor the unit row was the one that moved it.
        Assert.Equal(5, world.View.AsOf);
        Assert.Equal(system.CurrentTick, world.View.AsOf);
        Assert.Contains("tick 5", Row(view.Describe(), "World", "as of"), StringComparison.Ordinal);
        Assert.Contains("tick 5", Row(view.DebugFor(0).Describe(), "Perception", "as of"), StringComparison.Ordinal);

        // Reading twice moves nothing: the second reading is the same edge.
        view.Describe();
        view.DebugFor(0).Describe();
        Assert.Equal(5, world.View.AsOf);
        Assert.Equal(5, system.CurrentTick);
    }

    [Fact]
    public void AnUnknownAgentIsAnsweredRatherThanRefused()
    {
        var (system, _, world) = Scene();
        Play(system, world, 2);
        var view = new DemoWorldDebugView(world);

        // A negative id cannot be a unit anywhere, so it is named as such and
        // gets one row rather than a page of defaults dressed as facts.
        var negative = view.DebugFor(-4).Describe();
        var only = Assert.Single(negative);
        Assert.Equal("id", only.Key);
        Assert.Equal("-4", only.Value);
        Assert.Contains("no such unit", only.Note, StringComparison.Ordinal);

        // A world keeps no roster -- it learns units from movement events -- so
        // an id it has never heard of is answered with what it actually knows,
        // which is that nobody enlisted it. Answered, not thrown at the panel.
        var stranger = view.DebugFor(9999).Describe();
        Assert.Contains(stranger, r => r.Group == "Kit" && r.Value.Contains("none", StringComparison.Ordinal));
        Assert.Equal("9999", Row(stranger, "Condition", "id"));
        Assert.Contains("none", Row(stranger, "Fight", "target"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorldRowsCarryTheRatesAndTheRankTableAUnitIsMeasuredAgainst()
    {
        var (system, _, world) = Scene();
        Play(system, world, 3);

        var rows = new DemoWorldDebugView(world).Describe();

        Assert.Contains("on", Row(rows, "World", "fog"), StringComparison.Ordinal);
        Assert.Equal("1 cells", Row(rows, "World", "threats"));
        Assert.Contains("scripted", Note(rows, "World", "threats"), StringComparison.Ordinal);
        Assert.Equal("1 cells", Row(rows, "World", "pads"));
        Assert.Contains("repair cells", Note(rows, "World", "pads"), StringComparison.Ordinal);

        // The rates a tick applies, each with the tick in it, because a rate
        // without its period is not a rate.
        Assert.Contains("a tick", Note(rows, "Rates", "repair"), StringComparison.Ordinal);
        Assert.Contains("a tick", Note(rows, "Rates", "damage"), StringComparison.Ordinal);
        Assert.Contains("a tick", Note(rows, "Rates", "self-heal"), StringComparison.Ordinal);
        Assert.Contains("4", Row(rows, "Rates", "exposure radius"), StringComparison.Ordinal);

        // The table itself, in order, because the thresholds only mean anything
        // beside each other and beside a unit's banked points.
        Assert.Equal("2 ranks", Row(rows, "Rank", "table"));
        var table = Note(rows, "Rank", "table");
        Assert.Contains("50", table, StringComparison.Ordinal);
        Assert.Contains("150", table, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutFogTheSideRemembersNothingBecauseItSeesEverything()
    {
        var (system, _, world) = Scene(fog: false);
        Play(system, world, 2);

        var rows = new DemoWorldDebugView(world).DebugFor(0).Describe();

        Assert.Contains("off", Row(new DemoWorldDebugView(world).Describe(), "World", "fog"), StringComparison.Ordinal);
        Assert.Contains("nothing", Row(rows, "Perception", "remembers"), StringComparison.Ordinal);

        // And it can still see: the scripted threat and the other side's unit,
        // read straight off the board.
        Assert.Equal("2 cells", Row(rows, "Perception", "can see"));
        Assert.Equal("hostile to side 0", Note(rows, "Perception", "can see"));
    }

    /// <summary>
    /// Two squads on one board: three riflemen of side 0 and two of side 1,
    /// with a pad in the corner and no fog, so both sides see the whole map and
    /// nothing in these tests turns on what is hidden.
    /// </summary>
    /// <remarks>
    /// Nobody's doctrine is advanced here. What the squad rows read is the seam,
    /// and the seam answers about where units stand and what the world says
    /// about them -- so the tests move units and hurt them directly, and the
    /// doctrine each squad happens to carry never runs.
    /// </remarks>
    private static (MovementSystem System, Grid Grid, DemoWorld World, Squad Guards, Squad Raiders) TwoSquads()
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(
            grid,
            repairPerTick: 0.02,
            exposureRadius: 4.0,
            combat: Shipped(),
            fog: false);

        world.RepairCells.Add(grid.Index(2, 2));

        for (var i = 0; i < 3; i++)
        {
            world.Enlist(system.AddAgent(grid.Index(5 + i, 6), 0), "rifleman");
        }

        for (var i = 0; i < 2; i++)
        {
            world.Enlist(system.AddAgent(grid.Index(24 - i, 6), 1), "rifleman");
        }

        world.Listen(system);

        return (
            system,
            grid,
            world,
            new Squad("guard", [0, 1, 2], new GuardDoctrine(grid.Index(6, 6))),
            new Squad("raiders", [3, 4], new GuardDoctrine(grid.Index(23, 6))));
    }

    /// <summary>Each squad seen the way its own side's doctrine would be handed it.</summary>
    private static IReadOnlyList<ISquadView> Views(
        MovementSystem system, DemoWorld world, Squad guards, Squad raiders) =>
        [guards.ViewFor(system, world.ViewFor(0)), raiders.ViewFor(system, world.ViewFor(1))];

    [Fact]
    public void TheSquadRowsDescribeTheSquadTheWatchedAgentIsActuallyIn()
    {
        var (system, grid, world, guards, raiders) = TwoSquads();
        guards.MoveAll(system, grid.Index(6, 6));
        raiders.MoveAll(system, grid.Index(23, 6));
        Play(system, world, 2);

        var view = new DemoWorldDebugView(world, Views(system, world, guards, raiders));

        // A guard is described by the guard squad, and the numbers are the
        // guard squad's: three members standing on the station it was moved to.
        var guard = view.DebugFor(1).Describe();
        Assert.Equal("guard", Row(guard, "Squad", "squad"));
        Assert.Equal("3", Row(guard, "Squad", "members"));
        Assert.Equal("3", Row(guard, "Squad", "on station"));
        Assert.Contains($"6,6 (#{grid.Index(6, 6)})", Row(guard, "Squad", "anchor"), StringComparison.Ordinal);

        // WATCH SOMEBODY ELSE AND THE WHOLE BLOCK MOVES. This is the assertion
        // the group exists for: the rows are about the squad, not about the
        // board, so a different squad's member gets a different squad's answers.
        var raider = view.DebugFor(3).Describe();
        Assert.Equal("raiders", Row(raider, "Squad", "squad"));
        Assert.Equal("2", Row(raider, "Squad", "members"));
        Assert.Contains($"23,6 (#{grid.Index(23, 6)})", Row(raider, "Squad", "anchor"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAgentInNoSquadIsAnsweredRatherThanTheGroupGoingMissing()
    {
        var (system, grid, world, guards, raiders) = TwoSquads();
        guards.MoveAll(system, grid.Index(6, 6));
        Play(system, world, 1);

        // An enlisted unit nobody put in a squad. A missing block would read as
        // a fault in the panel, so the group answers instead.
        var loner = system.AddAgent(grid.Index(12, 10), side: 0);
        world.Enlist(loner, "rifleman");
        Play(system, world, 1);

        var withSquads = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(loner).Describe();

        Assert.Equal("none", Row(withSquads, "Squad", "squad"));
        Assert.Contains("2", Note(withSquads, "Squad", "squad"), StringComparison.Ordinal);
        Assert.Contains("no doctrine", Note(withSquads, "Squad", "squad"), StringComparison.Ordinal);

        // And a view nobody handed a squad at all says THAT, rather than saying
        // the unit is in none of nothing.
        var without = new DemoWorldDebugView(world).DebugFor(0).Describe();
        Assert.Equal("none", Row(without, "Squad", "squad"));
        Assert.Contains("nobody handed this view a squad", Note(without, "Squad", "squad"), StringComparison.Ordinal);

        // The group is still first, so nothing below it moved either way.
        Assert.Equal("Squad", without[0].Group);
    }

    [Fact]
    public void AMemberAwayOnAnErrandIsCountedApartFromOneWithTheSquad()
    {
        var (system, grid, world, guards, raiders) = TwoSquads();
        var station = grid.Index(6, 6);
        var pad = grid.Index(2, 2);
        guards.MoveAll(system, station);
        Play(system, world, 2);

        // Detached the way a repair policy detaches: still a member, still moved
        // by the next group move, and no longer standing with the squad.
        system.Dispatch(1, pad);
        Play(system, world, 1);

        var rows = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(0).Describe();

        // THE DISTINCTION THE AWAY LIST EXISTS FOR. Three members, one of them
        // away, so two are standing -- and the count that stops a repair policy
        // detaching a third is the on-station one, not the roster.
        Assert.Equal("3", Row(rows, "Squad", "members"));
        Assert.Equal("2", Row(rows, "Squad", "on station"));
        Assert.Equal("1", Row(rows, "Squad", "away"));

        // And WHERE it went, because a pad is a repair errand and anywhere else
        // is not.
        var errands = Note(rows, "Squad", "away");
        Assert.Contains("agent 1", errands, StringComparison.Ordinal);
        Assert.Contains($"2,2 (#{pad})", errands, StringComparison.Ordinal);

        // The other squad has nobody away, and says so rather than printing 0.
        var raider = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(4).Describe();
        Assert.Equal("none", Row(raider, "Squad", "away"));
        Assert.Equal("2", Row(raider, "Squad", "on station"));
    }

    [Fact]
    public void ASquadNobodyHasMovedYetSaysItHasNoAnchorInsteadOfPrintingMinusOne()
    {
        var (system, grid, world, guards, raiders) = TwoSquads();
        Play(system, world, 1);

        var before = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(0).Describe();

        // A guard's first act is to march to its own station, and the reason is
        // this row: the anchor is what it checks and it has not got one.
        Assert.Equal("none", Row(before, "Squad", "anchor"));
        Assert.DoesNotContain("-1", Row(before, "Squad", "anchor"), StringComparison.Ordinal);
        Assert.Contains("never moved as a group", Note(before, "Squad", "anchor"), StringComparison.Ordinal);

        guards.MoveAll(system, grid.Index(9, 9));
        Play(system, world, 1);

        var after = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(0).Describe();
        Assert.Equal($"9,9 (#{grid.Index(9, 9)})", Row(after, "Squad", "anchor"));
    }

    [Fact]
    public void TheWorstHealthInTheSquadIsNamedWithTheRankItIsMeasuredAgainst()
    {
        var (system, grid, world, guards, raiders) = TwoSquads();
        guards.MoveAll(system, grid.Index(6, 6));
        Play(system, world, 1);

        world.SetHealth(0, 0.8);
        world.SetHealth(2, 0.35);
        Play(system, world, 1);

        var rows = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(0).Describe();

        // Named, not just counted: which member is under the most pressure is
        // the fact a retreat threshold is about to be compared against, and no
        // per-unit row can carry it because it is about somebody else.
        var weakest = Row(rows, "Squad", "weakest");
        Assert.Contains("agent 2", weakest, StringComparison.Ordinal);
        Assert.Contains("35", weakest, StringComparison.Ordinal);
        Assert.Contains("rank 0", Note(rows, "Squad", "weakest"), StringComparison.Ordinal);
        Assert.Contains("on station", Note(rows, "Squad", "weakest"), StringComparison.Ordinal);
    }

    [Fact]
    public void ACasualtyLeavesTheSquadRowsTheWayItLeavesTheDoctrinesOwnView()
    {
        var (system, grid, world, guards, raiders) = TwoSquads();
        guards.MoveAll(system, grid.Index(6, 6));
        Play(system, world, 1);

        system.Remove(2);
        Play(system, world, 1);

        var rows = new DemoWorldDebugView(world, Views(system, world, guards, raiders))
            .DebugFor(0).Describe();

        // The seam hands a doctrine who can ACT, so a casualty stops appearing
        // and the panel counts what the doctrine counts. Two, not three.
        Assert.Equal("2", Row(rows, "Squad", "members"));
        Assert.Equal("2", Row(rows, "Squad", "on station"));
    }

    [Fact]
    public void NoSquadRowWeldsItsOwnGlossOntoTheEndOfItself()
    {
        // The same invariant the viewer sweeps for, applied where the rows are
        // written: a value is the fact and a note is the sentence, and a value
        // carrying " -- " is a column the panel clips.
        var (system, grid, world, guards, raiders) = TwoSquads();
        guards.MoveAll(system, grid.Index(6, 6));
        Play(system, world, 2);
        system.Dispatch(1, grid.Index(2, 2));
        world.SetHealth(2, 0.3);
        Play(system, world, 1);

        var view = new DemoWorldDebugView(world, Views(system, world, guards, raiders));
        var seen = 0;
        foreach (var id in new[] { 0, 1, 2, 3, 4, 9999 })
        {
            foreach (var row in view.DebugFor(id).Describe())
            {
                seen++;
                Assert.DoesNotContain(" -- ", row.Value, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(row.Value), $"{row.Group}/{row.Key} said nothing");
            }
        }

        Assert.True(seen > 60, $"the sweep only saw {seen} rows");
    }
}
