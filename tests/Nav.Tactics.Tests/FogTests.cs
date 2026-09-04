using Nav.Core;

namespace Nav.Tactics.Tests;

/// <summary>
/// Limited perception: a side is told only what its own units could have
/// witnessed, and remembers what it can no longer see.
/// </summary>
/// <remarks>
/// The rule underneath every test here is that <b>what a side can see changes
/// only when the board changes, and every board change is broadcast</b> —
/// including the movement of the WATCHER. So there is no clock in this filter
/// and no sweep on a quiet tick: a unit standing still for a hundred ticks is
/// discovered by somebody walking up to it, and the walking is the event.
/// <para>
/// WHEN the looking happens is the clock's business, and there is one answer:
/// the end of <see cref="DemoWorld.Settle"/>. That is why the loops here settle
/// as well as tick. A run that only stepped the system would never finish a
/// tick, and every side would still be answering for the last edge it had.
/// </para>
/// </remarks>
public sealed class FogTests
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

    /// <summary>A room, a world with fog, its pads, and one enlisted unit per entry.</summary>
    /// <remarks>
    /// Pads go in before <see cref="DemoWorld.Listen"/>, because Listen is the
    /// run's opening edge and a pad written to the list afterwards has no event
    /// behind it to say so -- the same hole <see cref="DemoWorld.HostileCells"/>
    /// has, and the reason a fog world re-looks whenever it holds one.
    /// </remarks>
    private static (MovementSystem System, Grid Grid, DemoWorld World) Scene(
        ISight? sight = null,
        bool fog = true,
        IEnumerable<(int X, int Y)>? pads = null,
        params (int X, int Y, int Side, string Kit)[] at)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(grid, combat: Shipped(), fog: fog, sight: sight);
        foreach (var (x, y) in pads ?? [])
        {
            world.RepairCells.Add(grid.Index(x, y));
        }

        foreach (var (x, y, side, kit) in at)
        {
            world.Enlist(system.AddAgent(grid.Index(x, y), side), kit);
        }

        world.Listen(system);
        return (system, grid, world);
    }

    /// <summary>
    /// Whole ticks, up to <paramref name="limit"/> of them, stopping as soon as
    /// <paramref name="until"/> is true.
    /// </summary>
    /// <remarks>
    /// A tick is the system's step AND the world settling, and the settle is
    /// what ends it: perception resolves at that edge and nowhere else. A loop
    /// that only stepped the system would leave every side answering for the
    /// last tick anybody finished, which is a place no observer can stand.
    /// </remarks>
    private static void Play(MovementSystem system, DemoWorld world, int limit, Func<bool> until)
    {
        for (var tick = 0; tick < limit && !until(); tick++)
        {
            system.Tick();
            world.Settle();
        }
    }

    [Fact]
    public void AStationaryEnemyIsDiscoveredByTheWatcherWalkingUpToIt()
    {
        // The point of the whole design. Nothing about the enemy changes for
        // the length of this test -- it never steps, so it never broadcasts --
        // and it is found anyway, because seeing it is a consequence of MY
        // unit's movement and my unit's movement is an ordinary event.
        var (system, grid, world) = Scene(
            at: [(1, 6, 0, "rifleman"), (24, 6, 1, "rifleman")]);
        var enemy = grid.Index(24, 6);

        Assert.Empty(world.ViewFor(0).Hostiles);
        Assert.Empty(world.ViewFor(0).Sightings);

        // A rifleman sees 6 and shoots 4, so it halts one sight-radius out and
        // the discovery is not muddled by anybody opening fire.
        system.Order([0], grid.Index(18, 6));
        Play(system, world, 60, () => system.Agents[0].Cell == grid.Index(18, 6));

        Assert.Equal(grid.Index(18, 6), system.Agents[0].Cell);
        Assert.Equal(enemy, system.Agents[1].Cell);
        Assert.Equal([enemy], world.ViewFor(0).Hostiles);

        var sighting = Assert.Single(world.ViewFor(0).Sightings);
        Assert.Equal(1, sighting.Agent);
        Assert.Equal(enemy, sighting.Cell);
        Assert.Equal(system.CurrentTick, sighting.Tick);
    }

    [Fact]
    public void AWatcherThatBacksAwayKeepsTheSightingItCanNoLongerConfirm()
    {
        // The ghost, and the only way to get one under a plain radius: the
        // watcher leaves. Standing where it was is the one thing that would
        // settle the question, and the unit that could have is walking away
        // from it.
        var (system, grid, world) = Scene(
            at: [(18, 6, 0, "rifleman"), (24, 6, 1, "rifleman")]);
        var enemy = grid.Index(24, 6);

        Assert.Equal([enemy], world.ViewFor(0).Hostiles);

        // The tick the enemy was actually seen on, taken from the sighting the
        // opening edge left rather than off the clock. One step west puts the
        // watcher out of range, so no later edge can refresh it.
        var seenAt = Assert.Single(world.ViewFor(0).Sightings).Tick;
        var home = grid.Index(1, 6);
        system.Order([0], home);
        Play(system, world, 60, () => system.Agents[0].Cell == home);
        Assert.Equal(home, system.Agents[0].Cell);

        // Out of sight, so out of Hostiles at once -- a doctrine reading only
        // Hostiles behaves exactly as it did before fog existed.
        Assert.Empty(world.ViewFor(0).Hostiles);

        // And still known, at the cell it was last actually seen on, stamped
        // with the tick that saw it. How stale is too stale is nobody's
        // business here.
        var sighting = Assert.Single(world.ViewFor(0).Sightings);
        Assert.Equal(1, sighting.Agent);
        Assert.Equal(enemy, sighting.Cell);
        Assert.Equal(seenAt, sighting.Tick);
        Assert.True(sighting.Tick < system.CurrentTick, "the sighting should have aged");
    }

    [Fact]
    public void WatchingItLeaveIsKnowledgeToo()
    {
        // The refutation rule. The watcher holds still and the enemy walks out
        // of range: every cell it was ever seen on is a cell still in plain
        // view, so there is no ghost to keep. Without this the last sighting
        // would pin an enemy forever to a patch of empty ground the watcher is
        // looking straight at.
        var (system, grid, world) = Scene(
            at: [(18, 6, 0, "rifleman"), (23, 6, 1, "rifleman")]);

        Assert.Single(world.ViewFor(0).Hostiles);

        system.Order([1], grid.Index(28, 12));
        Play(system, world, 60, () => system.Agents[1].Cell == grid.Index(28, 12));

        Assert.Empty(world.ViewFor(0).Hostiles);
        Assert.Empty(world.ViewFor(0).Sightings);
    }

    [Fact]
    public void TheScoutSeesFurtherThanItShoots()
    {
        // Sight is a kit's own number rather than a multiple of its reach, and
        // this is the reason for it: one unit whose job is looking.
        var combat = Shipped();
        var buggy = combat.KitFor("buggy");
        var tank = combat.KitFor("tank");

        Assert.True(buggy.Sight > buggy.Range * 1.5, "the buggy is meant to be a scout");
        Assert.True(buggy.Sight > tank.Sight, "the scout should out-see the tank");
        Assert.True(buggy.Range < tank.Range, "and still lose the fight it starts");
    }

    [Fact]
    public void EveryKitSeesAtLeastAsFarAsItShoots()
    {
        // WHEN THIS TEST FAILS, READ THIS. It pins an invariant that the
        // shipped config happens to have and the model does not require: a kit
        // could see less than it shoots, and would then need somebody else to
        // spot for it. Nothing spots for anybody yet.
        //
        // While it holds, fog cannot make a unit fire at something it cannot
        // see, which is why DemoWorld.TargetFor is not fog-aware. A
        // line-of-sight ISight BREAKS it -- a wall would block the sight and
        // not the range check -- and this test failing is how that arrives,
        // rather than a unit quietly shooting through a hill.
        var combat = Shipped();
        foreach (var name in new[] { "rifleman", "buggy", "tank", "rocketbike" })
        {
            var kit = combat.KitFor(name);
            Assert.True(kit.Sight >= kit.Range, $"{name} shoots {kit.Range} and sees {kit.Sight}");
        }
    }

    [Fact]
    public void SightIsASeamAndTheRadiusIsJustOneImplementation()
    {
        // Nothing above ISight knows what sight is made of. Two implementations
        // that answer nothing and everything move the whole board in and out of
        // view without a caller changing, which is what makes line-of-sight a
        // drop-in later rather than a rewrite.
        var adjacent = new (int X, int Y, int Side, string Kit)[] { (5, 6, 0, "tank"), (6, 6, 1, "tank") };

        var (_, _, blind) = Scene(sight: new Blindfold(), at: adjacent);
        Assert.Empty(blind.ViewFor(0).Hostiles);
        Assert.Empty(blind.ViewFor(0).Sightings);

        var (_, grid, keen) = Scene(sight: new AllSeeing(), at: [(1, 1, 0, "tank"), (27, 11, 1, "tank")]);
        Assert.Equal([grid.Index(27, 11)], keen.ViewFor(0).Hostiles);
    }

    [Fact]
    public void AFogWorldWillNotSettleWithAUnitThatHasNoEyes()
    {
        // Sight 0 is a real answer and almost never the intended one. A side
        // blinded by a forgotten Enlist looks exactly like a doctrine that has
        // stopped working, so the world refuses instead.
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(grid, combat: Shipped(), fog: true);
        system.AddAgent(grid.Index(5, 5), side: 0);
        world.Listen(system);

        var refused = Assert.Throws<InvalidOperationException>(world.Settle);
        Assert.Contains("no kit", refused.Message);
    }

    [Fact]
    public void WithoutFogNothingChangesAndNothingIsRemembered()
    {
        // The omniscient world is the filter passing everything, not a separate
        // path. Everything written before fog existed reads the same.
        var (_, grid, world) = Scene(
            fog: false,
            at: [(1, 1, 0, "rifleman"), (27, 11, 1, "rifleman")]);

        Assert.Equal([grid.Index(27, 11)], world.ViewFor(0).Hostiles);
        Assert.Equal([grid.Index(1, 1)], world.ViewFor(1).Hostiles);
        Assert.Empty(world.ViewFor(0).Sightings);
        Assert.Empty(world.Sightings);
    }

    [Fact]
    public void EachSideIsBlindOnItsOwnTerms()
    {
        // Sight is per kit, so the same distance is a sighting for one side and
        // nothing for the other. A buggy sees 9 and a rifleman sees 6, and at 8
        // apart exactly one of them knows anything.
        var (_, grid, world) = Scene(
            at: [(10, 6, 0, "buggy"), (18, 6, 1, "rifleman")]);

        Assert.Equal([grid.Index(18, 6)], world.ViewFor(0).Hostiles);
        Assert.Empty(world.ViewFor(1).Hostiles);
        Assert.Empty(world.ViewFor(1).Sightings);
    }

    [Fact]
    public void APadIsKnownWhereverItIsBecauseItStandsOnItsOwnGround()
    {
        // A side that cannot see a pad cannot plan a retreat to one, and would
        // simply stop retreating -- no error, no complaint, a whole mechanic
        // gone. Measured on the guard demo: pads that grant no vision took it
        // from 4 rotations through repair to ZERO, with the headline otherwise
        // unchanged, which is the silent degradation this project keeps
        // meeting. A pad watches the ground it stands on, so it is never lost.
        var (_, grid, world) = Scene(pads: [(27, 11)], at: [(1, 1, 0, "rifleman")]);

        Assert.Equal([grid.Index(27, 11)], world.ViewFor(0).RepairPoints);
    }

    [Theory]
    [InlineData(5.0, true)]
    [InlineData(1.0, false)]
    public void APadWatchesTheGroundAroundIt(double padSight, bool spotted)
    {
        // The reason a pad's reach is a number and not a boolean. An enemy
        // creeping up on the armory is seen BY THE ARMORY, with no unit of ours
        // anywhere near it -- and a pad that sees only its own doorstep is not,
        // which is what makes where a pad goes a decision rather than a
        // formality.
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(grid, combat: Shipped(), fog: true) { PadSight = padSight };
        var pad = grid.Index(24, 11);
        world.RepairCells.Add(pad);

        // Ours is twenty-three cells away and sees six, so nothing it can see
        // is in play. The creeper is three from the pad.
        world.Enlist(system.AddAgent(grid.Index(1, 1), side: 0), "rifleman");
        var creeper = grid.Index(24, 8);
        world.Enlist(system.AddAgent(creeper, side: 1), "rifleman");
        world.Listen(system);

        Assert.Equal(spotted ? [creeper] : (int[])[], world.ViewFor(0).Hostiles);

        // Either way the pad itself is known, because it stands on its own
        // ground. Losing sight of the approach is not losing the armory.
        Assert.Equal([pad], world.ViewFor(0).RepairPoints);
    }

    /// <summary>Sees nothing, ever. Not even the ground underfoot.</summary>
    private sealed class Blindfold : ISight
    {
        public bool CanSee(int from, int to, double range) => false;
    }

    /// <summary>Sees the whole map, whatever the range says.</summary>
    private sealed class AllSeeing : ISight
    {
        public bool CanSee(int from, int to, double range) => true;
    }
}
