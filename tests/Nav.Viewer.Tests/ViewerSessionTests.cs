using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// The session: the single owner of what is loaded, and the one implementation
/// of the load-refusal path. These paths lived in the two host Mains before the
/// extraction, where nothing could test them.
/// </summary>
public sealed class ViewerSessionTests
{
    private const string AllWalls =
        """
        type octile
        height 2
        width 3
        map
        @@@
        @@@
        """;

    private static DirectoryInfo TempRoot() => Directory.CreateTempSubdirectory("nav-session-test-");

    [Fact]
    public void NoArgumentsLoadsTheEmbeddedFixtureWithADefaultSquad()
    {
        Assert.True(ViewerSession.TryLoad(new ViewerOptions(null, null, false), out var session, out _));

        Assert.Equal("(embedded fixture)", session.MapName);
        Assert.False(session.IsReplay);
        Assert.True(session.Running);
        Assert.Equal(ViewerSession.DefaultSquad, session.Agents.Count);
        Assert.Equal([0], session.Selection);
    }

    [Fact]
    public void AMissingMapFileIsRefusedWithTheMessage()
    {
        var options = new ViewerOptions(Path.Combine(TempRoot().FullName, "absent.map"), null, false);

        Assert.False(ViewerSession.TryLoad(options, out _, out var error));

        Assert.Contains("absent.map", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAllWallMapIsRefusedBeforeAWindowOpens()
    {
        var root = TempRoot();
        try
        {
            var mapPath = Path.Combine(root.FullName, "walls.map");
            File.WriteAllText(mapPath, AllWalls);

            Assert.False(ViewerSession.TryLoad(new ViewerOptions(mapPath, null, false), out _, out var error));

            Assert.Contains("no passable cell", error, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AScenarioAgentOnAWallIsRefusedWithTheCellNamed()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "m.map"), SampleMaps.CornerCutTrap);
            var scenarioPath = Path.Combine(root.FullName, "bad.scenario");
            File.WriteAllText(scenarioPath, "version 1\nmap m.map\nsize 12 7\nagent 0 0 0\nend 5\n");

            Assert.False(
                ViewerSession.TryLoad(new ViewerOptions(null, null, false, scenarioPath), out _, out var error));

            Assert.NotNull(error);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AScenarioLoadsPausedWithItsMapResolvedAndNamed()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "m.map"), SampleMaps.CornerCutTrap);
            var scenarioPath = Path.Combine(root.FullName, "run.scenario");
            File.WriteAllText(scenarioPath, "version 1\nmap m.map\nsize 12 7\nagent 0 1 1\norder 0 0 10 5\nend 30\n");

            Assert.True(
                ViewerSession.TryLoad(new ViewerOptions(null, null, false, scenarioPath), out var session, out _));

            Assert.True(session.IsReplay);
            Assert.False(session.Running);
            Assert.Equal(0, session.CurrentTick);
            Assert.Equal("run.scenario on m.map", session.MapName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private const string WideMap =
        """
        type octile
        height 5
        width 24
        map
        @@@@@@@@@@@@@@@@@@@@@@@@
        @......................@
        @......................@
        @......................@
        @@@@@@@@@@@@@@@@@@@@@@@@
        """;

    [Fact]
    public void LoadingAFileMidSessionReplacesTheWorldAndBumpsTheVersion()
    {
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);
        var root = TempRoot();
        try
        {
            var mapPath = Path.Combine(root.FullName, "wide.map");
            File.WriteAllText(mapPath, WideMap);

            Assert.True(session.TryLoadFile(mapPath, out _));

            Assert.Equal(1, session.Version);
            Assert.Equal(24, session.Grid.Width);
            Assert.Equal("wide.map", session.MapName);
            Assert.Equal(ViewerSession.DefaultSquad, session.Agents.Count);
            Assert.Equal([0], session.Selection);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadingAScenarioFileMidSessionBecomesAPausedReplay()
    {
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "m.map"), SampleMaps.CornerCutTrap);
            var scenarioPath = Path.Combine(root.FullName, "run.scenario");
            File.WriteAllText(scenarioPath, "version 1\nmap m.map\nsize 12 7\nagent 0 1 1\norder 0 0 10 5\nend 30\n");

            session.SetRunning(true);
            Assert.True(session.TryLoadFile(scenarioPath, out _));

            Assert.True(session.IsReplay);
            Assert.False(session.Running);
            Assert.Equal(0, session.CurrentTick);
            Assert.Single(session.Agents);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AMidSessionLoadRefusesAScenarioRecordedOnADifferentlySizedMap()
    {
        // The gap this closes: EnsureMatches used to sit on FromScenario, which
        // only the STARTUP path reaches. Dropping a file on the window went
        // TryLoadFile -> BuildWorld and skipped it, so the same scenario was
        // refused at launch and accepted by drag-and-drop. The check now lives in
        // BuildWorld, which every path -- startup, drop, restart -- goes through.
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "m.map"), SampleMaps.CornerCutTrap);
            var scenarioPath = Path.Combine(root.FullName, "wrong.scenario");
            File.WriteAllText(scenarioPath, "version 1\nmap m.map\nsize 40 40\nagent 0 1 1\nend 30\n");

            Assert.False(session.TryLoadFile(scenarioPath, out var error));

            Assert.Contains("40x40", error, StringComparison.Ordinal);
            Assert.Contains("12x7", error, StringComparison.Ordinal);
            Assert.False(session.IsReplay);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AMidSessionLoadRefusesAnOrderAimedOffTheMap()
    {
        // Order destinations were validated in ScenarioPlayback and nowhere else,
        // so the viewer issued them through Grid.Index -- unchecked, y * Width + x
        // -- and an off-map order became a plausible cell on another row. The
        // whole timeline is now checked once, before the world is built, rather
        // than as each order fires.
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "m.map"), SampleMaps.CornerCutTrap);
            var scenarioPath = Path.Combine(root.FullName, "wrong.scenario");
            File.WriteAllText(scenarioPath, "version 1\nmap m.map\nsize 12 7\nagent 0 1 1\norder 0 0 99 3\nend 30\n");

            Assert.False(session.TryLoadFile(scenarioPath, out var error));

            Assert.Contains("(99,3)", error, StringComparison.Ordinal);
            Assert.False(session.IsReplay);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ARefusedMidSessionLoadChangesAbsolutelyNothing()
    {
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);
        session.Select([1, 2]);
        session.Tick();

        var root = TempRoot();
        try
        {
            Assert.False(session.TryLoadFile(Path.Combine(root.FullName, "absent.map"), out var error));

            Assert.NotNull(error);
            Assert.Equal(0, session.Version);
            Assert.Equal("m.map", session.MapName);
            Assert.Equal(4, session.Agents.Count);
            Assert.Equal([1, 2], session.Selection);
            Assert.Equal(1, session.CurrentTick);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void RestartOnAFreeSquadIsRefusedNotIgnored()
    {
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 2);

        Assert.Throws<InvalidOperationException>(session.Restart);
    }

    [Fact]
    public void SelectingAnUnknownAgentIsRefused()
    {
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Select([7]));
    }

    [Fact]
    public void ASelectionIsKeptInIdOrderHoweverItArrives()
    {
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);

        session.Select([3, 0, 2]);

        Assert.Equal([0, 2, 3], session.Selection);
    }

    [Fact]
    public void RegroupingAfterACasualtyOrdersTheLivingRatherThanThrowing()
    {
        // ONE CASUALTY USED TO END THE REGROUP KEY. OrderEveryone handed
        // MovementSystem.Order every id there had ever been, and Order refuses a
        // removed agent -- it throws rather than quietly walking a corpse -- so
        // from the first death onward every press of R was an exception, for the
        // rest of the session.
        //
        // The unit is taken out through the middle of the squad rather than off
        // the end on purpose: the ids either side of it stay live, so a fix that
        // simply stopped at the first gap would be caught here too.
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 4);
        var goal = session.Grid.Index(10, 5);
        var fell = session.Agents[2].Cell;

        session.Remove(2);
        session.OrderEveryone(goal);

        // Exactly the living, and all of them: a group order aims every member at
        // the shared destination and claims distinct parking on approach, which is
        // the core suite's business rather than this one's.
        Assert.Equal([0, 1, 3], session.Agents.Where(a => a.Alive).Select(a => a.Id));
        Assert.All(session.Agents.Where(a => a.Alive), a => Assert.Equal(goal, a.Goal));

        // And the body was not ordered anywhere: Remove parks its goal on the cell
        // it fell on, and nothing here moved it.
        Assert.Equal(fell, session.Agents[2].Goal);
        Assert.Equal(fell, session.Agents[2].Cell);
    }

    [Fact]
    public void TickAdvancesEvenWhilePaused()
    {
        // Single-stepping a paused world is a legitimate thing for a caller to
        // do; Running gates the app's clock, not the session's mechanics.
        var session = ViewerSession.FromMap(Grid.FromMapText(SampleMaps.CornerCutTrap), "m.map", squad: 2);
        session.SetRunning(false);

        session.Tick();

        Assert.Equal(1, session.CurrentTick);
    }
}
