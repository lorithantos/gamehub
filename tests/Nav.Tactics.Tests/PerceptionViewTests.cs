namespace Nav.Tactics.Tests;

/// <summary>
/// The peek view over <see cref="DemoWorld"/>: the three perception questions
/// with no way to resolve perception.
/// </summary>
/// <remarks>
/// <b>Looking must not cause.</b> <see cref="DemoWorld.HostilesFor"/>,
/// <see cref="DemoWorld.SightingsFor"/> and
/// <see cref="DemoWorld.RepairPointsFor"/> bring every side's view up to date
/// before answering, which is right for doctrine and wrong for a panel: a
/// sighting a panel provoked carries the tick the PANEL asked on, and that tick
/// is what doctrine ages a memory against.
/// <para>
/// So the view answers from what the last resolution left. It can be older than
/// the board, and that is the point of it -- an instrument watching a side act
/// on limited knowledge wants the knowledge the side acted on, not a fresher
/// set the doctrine never had.
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

    /// <summary>A room, a world, and one enlisted unit per entry.</summary>
    private static (MovementSystem System, Grid Grid, DemoWorld World) Scene(
        bool fog = true,
        params (int X, int Y, int Side, string Kit)[] at)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(grid, combat: Shipped(), fog: fog);
        foreach (var (x, y, side, kit) in at)
        {
            world.Enlist(system.AddAgent(grid.Index(x, y), side), kit);
        }

        world.Listen(system);
        return (system, grid, world);
    }

    [Fact]
    public void ThePeekLeavesAnOutstandingLookOutstanding()
    {
        var (system, grid, world) = Scene(at: [(1, 6, 0, "rifleman"), (24, 6, 1, "rifleman")]);
        var enemy = grid.Index(24, 6);
        var post = grid.Index(18, 6);
        var pad = grid.Index(1, 1);
        world.RepairCells.Add(pad);

        // Nothing has resolved yet, so there is no last resolution to read and
        // the side knows nothing -- not even the pad standing on its own
        // ground. Honest emptiness rather than a wrong answer: a side that has
        // never looked HAS seen nothing.
        Assert.Empty(world.View.PeekHostiles(0));
        Assert.Empty(world.View.PeekSightings(0));
        Assert.Empty(world.View.PeekRepairPoints(0));

        // Walk into sight. Every step is broadcast, so the world owes a look
        // and nobody has paid it.
        system.Order([0], post);
        for (var tick = 0; tick < 60 && system.Agents[0].Cell != post; tick++)
        {
            system.Tick();
        }

        Assert.Equal(post, system.Agents[0].Cell);
        var arrived = system.CurrentTick;

        // The peek reads what the last resolution left -- still nothing -- and
        // leaves the debt where it found it.
        Assert.Empty(world.View.PeekHostiles(0));
        Assert.Empty(world.View.PeekSightings(0));

        // Quiet ticks: nobody steps, so nothing is broadcast, and the world is
        // stale for exactly the reason it was before the peek.
        var events = 0;
        system.Happened += _ => events++;
        for (var tick = 0; tick < 3; tick++)
        {
            system.Tick();
        }

        Assert.Equal(0, events);
        Assert.True(system.CurrentTick > arrived, "the clock should have moved on");

        // Doctrine asks, doctrine resolves, and the sighting carries the tick
        // DOCTRINE asked on. Had the peek resolved, it would read `arrived`.
        var sighting = Assert.Single(world.SightingsFor(0));
        Assert.Equal(enemy, sighting.Cell);
        Assert.Equal(system.CurrentTick, sighting.Tick);
        Assert.True(sighting.Tick > arrived, "the peek must not have stamped the sighting");

        // And what that resolution left is what the peek reports from here on.
        Assert.Equal([enemy], world.View.PeekHostiles(0));
        Assert.Equal([pad], world.View.PeekRepairPoints(0));
    }

    [Fact]
    public void ThePeekAnswersWhatTheLastResolutionLeftRatherThanWhatIsTrue()
    {
        var (system, grid, world) = Scene(at: [(18, 6, 0, "rifleman"), (23, 6, 1, "rifleman")]);
        var enemy = grid.Index(23, 6);
        var away = grid.Index(28, 12);

        // One resolution, on the doctrine path, with the enemy in plain view.
        Assert.Equal([enemy], world.HostilesFor(0));

        // It walks out of range while the watcher holds still. Nothing has
        // resolved since, so what the world holds is the old answer.
        system.Order([1], away);
        for (var tick = 0; tick < 60 && system.Agents[1].Cell != away; tick++)
        {
            system.Tick();
        }

        Assert.Equal(away, system.Agents[1].Cell);

        // An enemy reported standing where it no longer is: exactly what
        // doctrine last acted on, which is the number an instrument watching a
        // side act on limited knowledge wants.
        Assert.Equal([enemy], world.View.PeekHostiles(0));
        var remembered = Assert.Single(world.View.PeekSightings(0));
        Assert.Equal(enemy, remembered.Cell);

        // The doctrine path, asked now, refutes both -- that ground is in plain
        // view and there is nothing on it. The peek was a tick behind, not
        // wrong.
        Assert.Empty(world.HostilesFor(0));
        Assert.Empty(world.SightingsFor(0));
    }

    [Fact]
    public void WithoutFogThePeekIsTheOmniscientAnswerAndNeverStale()
    {
        // A world without fog resolves nothing, so there is no debt for an
        // instrument to settle and nothing for the peek to lag behind. It
        // mirrors the three queries exactly: every hostile, no memory, every
        // pad.
        var (system, grid, world) = Scene(
            fog: false,
            at: [(1, 1, 0, "rifleman"), (27, 11, 1, "rifleman")]);
        var pad = grid.Index(14, 6);
        var moved = grid.Index(20, 11);
        world.RepairCells.Add(pad);

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
    }
}
