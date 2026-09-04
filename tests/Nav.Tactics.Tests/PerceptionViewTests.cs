namespace Nav.Tactics.Tests;

/// <summary>
/// The view over <see cref="DemoWorld"/>: the perception questions, the tick
/// they are all answered as of, and no way from here to move any of it.
/// </summary>
/// <remarks>
/// <b>The only state anybody can see is the state at a clock edge.</b>
/// <see cref="DemoWorld.Settle"/> ends a tick by having every side look at what
/// the shots and the deaths left, so a reader that stops there is reading a
/// board that has already settled rather than provoking it to settle.
/// <see cref="IPerceptionView.AsOf"/> names the edge it is reading.
/// <para>
/// <b>These tests used to say the opposite, and that is the point of them.</b>
/// <see cref="DemoWorld.HostilesFor"/>, <see cref="DemoWorld.SightingsFor"/> and
/// <see cref="DemoWorld.RepairPointsFor"/> resolved perception as a side effect
/// of being asked, so a panel that read one stamped that side's sightings with
/// the tick the PANEL asked on -- and this view existed to keep instruments off
/// that path, at the price of answering from a resolution that could be a tick
/// behind the board. The resolve moved to the edge, where the model always had
/// it. There is now no path to keep anybody off and no price to pay: what an
/// instrument reads is what doctrine reads.
/// </para>
/// </remarks>
public sealed class PerceptionViewTests
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

    /// <summary>A room, a world, its pads, and one enlisted unit per entry.</summary>
    /// <remarks>
    /// Pads go in before <see cref="DemoWorld.Listen"/>, which is the run's
    /// opening edge: a pad written to the list after it has no event behind it
    /// to say so.
    /// </remarks>
    private static (MovementSystem System, Grid Grid, DemoWorld World) Scene(
        bool fog = true,
        IEnumerable<(int X, int Y)>? pads = null,
        params (int X, int Y, int Side, string Kit)[] at)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(grid, combat: Shipped(), fog: fog);
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

    /// <summary>Whole ticks -- the system's step and the world settling -- up to a limit.</summary>
    private static void Play(MovementSystem system, DemoWorld world, int limit, Func<bool> until)
    {
        for (var tick = 0; tick < limit && !until(); tick++)
        {
            system.Tick();
            world.Settle();
        }
    }

    [Fact]
    public void ASideIsNotBlindOnTheOpeningTick()
    {
        // THE TRAP IN MOVING THE RESOLVE, pinned. Every other look happens at
        // the end of a tick, and the first doctrine pass runs before any tick
        // has ended -- so a world whose only resolve was in Settle would open
        // every run with every side seeing nothing, for one tick, and nothing
        // would say so. Listen is the opening edge, and this is what it buys.
        var (system, grid, world) = Scene(
            pads: [(1, 1)],
            at: [(18, 6, 0, "rifleman"), (23, 6, 1, "rifleman")]);

        Assert.Equal(0, system.CurrentTick);
        Assert.Equal(0, world.View.AsOf);
        Assert.Equal([grid.Index(23, 6)], world.HostilesFor(0));
        Assert.Equal([grid.Index(18, 6)], world.HostilesFor(1));
        Assert.Equal([grid.Index(1, 1)], world.RepairPointsFor(0));
        Assert.Equal(0, Assert.Single(world.SightingsFor(0)).Tick);
    }

    [Fact]
    public void AtTheEdgeTheViewIsCurrentWithNoDoctrineQuestionAsked()
    {
        // Nothing here asks a doctrine question before the assertions. Under the
        // lazy resolve that meant the view still answered for the board as it
        // stood BEFORE this tick's movement, and the only thing that would have
        // brought it up to date was somebody asking -- so a stopped observer saw
        // the one state the model says cannot exist.
        var (system, grid, world) = Scene(at: [(1, 6, 0, "rifleman"), (24, 6, 1, "rifleman")]);
        var enemy = grid.Index(24, 6);
        var post = grid.Index(18, 6);

        // Twenty-three cells apart and a rifleman sees six, so the opening edge
        // is an honest nothing.
        Assert.Empty(world.View.PeekHostiles(0));

        system.Order([0], post);
        Play(system, world, 60, () => system.Agents[0].Cell == post);
        Assert.Equal(post, system.Agents[0].Cell);

        // Settle has returned, and that is all that has happened. The view is
        // stamped with the tick that just ended and the enemy is in it.
        Assert.Equal(system.CurrentTick, world.View.AsOf);
        Assert.Equal([enemy], world.View.PeekHostiles(0));
        var sighting = Assert.Single(world.View.PeekSightings(0));
        Assert.Equal(1, sighting.Agent);
        Assert.Equal(enemy, sighting.Cell);
        Assert.Equal(world.View.AsOf, sighting.Tick);
    }

    [Fact]
    public void AsOfIsOneNumberForTheWholeViewRatherThanOnePerAnswer()
    {
        // A clock edge makes every output valid together, so the three answers
        // are one reading taken three ways. Read them in one order, then the
        // other, and nothing about the view has moved: it is a snapshot rather
        // than three that happened to arrive together.
        var (system, grid, world) = Scene(
            pads: [(1, 1)],
            at: [(18, 6, 0, "rifleman"), (24, 6, 1, "rifleman")]);
        var pad = grid.Index(1, 1);
        var enemy = grid.Index(24, 6);
        var view = world.View;

        var asOf = view.AsOf;
        Assert.Equal(system.CurrentTick, asOf);
        Assert.Equal([enemy], view.PeekHostiles(0));
        Assert.Equal(asOf, Assert.Single(view.PeekSightings(0)).Tick);
        Assert.Equal([pad], view.PeekRepairPoints(0));

        Assert.Equal([pad], view.PeekRepairPoints(0));
        Assert.Equal(asOf, Assert.Single(view.PeekSightings(0)).Tick);
        Assert.Equal([enemy], view.PeekHostiles(0));
        Assert.Equal(asOf, view.AsOf);

        // The watcher backs out of range over many edges. AsOf follows every one
        // of them; the sighting keeps the tick it was taken on. Two numbers that
        // answer different questions -- when this side last LOOKED, and when it
        // last SAW that unit -- and the gap between them is how stale its
        // knowledge of that one enemy is.
        var home = grid.Index(1, 6);
        system.Order([0], home);
        Play(system, world, 60, () => system.Agents[0].Cell == home);

        Assert.Equal(system.CurrentTick, view.AsOf);
        var ghost = Assert.Single(view.PeekSightings(0));
        Assert.Equal(enemy, ghost.Cell);
        Assert.Equal(asOf, ghost.Tick);
        Assert.True(view.AsOf > ghost.Tick, "the view has moved on and the sighting has not");

        // And the stamp is still one number, not one the pads carry and another
        // the memory does: the pad has been in view the whole way and answers as
        // of the same edge the ghost is measured against.
        Assert.Equal([pad], view.PeekRepairPoints(0));
        Assert.Equal(system.CurrentTick, view.AsOf);

        // Three ticks where nobody steps. The look short-circuits -- nothing
        // moved, so nothing can have changed what anybody can see -- and the
        // stamp moves anyway, because the ticks ended. Unchanged is not out of
        // date, and a view stuck at the last tick something HAPPENED would say
        // it was.
        for (var quiet = 0; quiet < 3; quiet++)
        {
            system.Tick();
            world.Settle();
        }

        Assert.Equal(system.CurrentTick, view.AsOf);
        Assert.Equal(asOf, Assert.Single(view.PeekSightings(0)).Tick);
    }

    [Fact]
    public void TheViewMovesAtEdgesAndNowhereElse()
    {
        // What the lazy resolve used to be pinned by, the other way up. Ticking
        // the system and then asking was how a side learned anything; now the
        // settle is what teaches it, and a query asked in between answers for
        // the last edge and moves nothing.
        var (system, grid, world) = Scene(at: [(1, 6, 0, "rifleman"), (24, 6, 1, "rifleman")]);
        var enemy = grid.Index(24, 6);
        var post = grid.Index(18, 6);

        system.Order([0], post);
        for (var tick = 0; tick < 60 && system.Agents[0].Cell != post; tick++)
        {
            system.Tick();
        }

        // Steps without settles: the board moved, no tick ever ended, and the
        // doctrine query and the peek both go on answering for tick 0. Asking
        // twice does not teach anybody anything either.
        Assert.Equal(post, system.Agents[0].Cell);
        Assert.Equal(0, world.View.AsOf);
        Assert.Empty(world.HostilesFor(0));
        Assert.Empty(world.View.PeekHostiles(0));
        Assert.Empty(world.SightingsFor(0));
        Assert.Empty(world.HostilesFor(0));

        // One edge, and both learn the same thing at the same moment.
        world.Settle();
        Assert.Equal(system.CurrentTick, world.View.AsOf);
        Assert.Equal([enemy], world.HostilesFor(0));
        Assert.Equal([enemy], world.View.PeekHostiles(0));
        Assert.Equal(world.View.AsOf, Assert.Single(world.SightingsFor(0)).Tick);
    }

    [Fact]
    public void PeekRepairPointsCannotChangeAfterItIsHandedOver()
    {
        // The one peek that used to hand back live state, and it did it in both
        // branches: the side's pads under fog, RepairCells without. An answer
        // that changes after it was given is not a stale answer, it is an answer
        // to a question nobody asked -- and the other two peeks already copy.
        var (_, grid, plain) = Scene(fog: false, pads: [(14, 6)], at: [(1, 1, 0, "rifleman")]);
        var pad = grid.Index(14, 6);

        var answered = plain.View.PeekRepairPoints(0);
        plain.RepairCells.Add(grid.Index(20, 11));

        Assert.Equal([pad], answered);
        Assert.NotSame(plain.RepairCells, answered);

        // Under fog it is the side's own list, which the next look replaces
        // wholesale rather than edits -- so the copy is what makes the two
        // branches answer the same way instead of one of them happening to.
        var (_, _, fogged) = Scene(pads: [(14, 6)], at: [(1, 1, 0, "rifleman")]);
        Assert.NotSame(fogged.RepairPointsFor(0), fogged.View.PeekRepairPoints(0));
        Assert.Equal([pad], fogged.View.PeekRepairPoints(0));
    }

    [Fact]
    public void WithoutFogThePeekIsTheOmniscientAnswerAndNeverStale()
    {
        // A world without fog remembers nothing and filters nothing, so its
        // answers are read straight off the board and no edge stands between the
        // board and the reader. It mirrors the three queries exactly: every
        // hostile, no memory, every pad.
        var (system, grid, world) = Scene(
            fog: false,
            pads: [(14, 6)],
            at: [(1, 1, 0, "rifleman"), (27, 11, 1, "rifleman")]);
        var pad = grid.Index(14, 6);
        var moved = grid.Index(20, 11);

        Assert.Equal(0, world.View.AsOf);
        Assert.Equal(world.HostilesFor(0), world.View.PeekHostiles(0));
        Assert.Equal([grid.Index(27, 11)], world.View.PeekHostiles(0));
        Assert.Empty(world.View.PeekSightings(0));
        Assert.Equal([pad], world.View.PeekRepairPoints(0));

        system.Order([1], moved);
        for (var tick = 0; tick < 60 && system.Agents[1].Cell != moved; tick++)
        {
            system.Tick();
        }

        // Across the map, unwatched, and in the list the instant it steps:
        // without fog the answer is read off the board rather than remembered.
        Assert.Equal([moved], world.View.PeekHostiles(0));

        // The stamp is still the clock's, because it is about when the tick
        // ended and not about what the looking found. A world with nothing to
        // resolve ends its ticks like any other.
        world.Settle();
        Assert.Equal(system.CurrentTick, world.View.AsOf);
    }
}
