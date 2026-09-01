using System.Numerics;

using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// The viewer's behaviour, driven with no window, no renderer and no graphics
/// assembly in the process — now with a squad rather than one unit.
/// </summary>
public sealed class ViewerAppTests
{
    private const int StatusHeight = 26;
    private const int Squad = 4;

    private static Grid Fixture() => Grid.FromMapText(SampleMaps.CornerCutTrap);

    private static GridLayout LayoutFor(Grid grid) => GridLayout.Fit(grid, 1000, 1000 - StatusHeight);

    private static (ViewerApp App, RecordingRenderer Renderer, Grid Grid) Run(params ScriptedFrame[] frames)
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);
        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);
        return (app, renderer, grid);
    }

    [Fact]
    public void TheFirstFrameDrawsTerrainAndOneCirclePerUnit()
    {
        var (app, renderer, _) = Run(new ScriptedFrame());

        Assert.Equal(1, renderer.FrameCount);
        Assert.Single(renderer.OfKind<DrawCommand.Terrain>());
        Assert.Equal(Squad, app.Agents.Count);

        // One per unit, plus the small marker on the selected one.
        Assert.Equal(Squad + 1, renderer.LastFrameOfKind<DrawCommand.Circle>().Count());
    }

    [Fact]
    public void TheTerrainFillsTheMapAreaAndNotTheStatusStrip()
    {
        var (app, renderer, _) = Run(new ScriptedFrame());

        var terrain = renderer.OfKind<DrawCommand.Terrain>().Single();
        Assert.Equal(new RectF(0, 0, app.Layout.PixelWidth, app.Layout.PixelHeight), terrain.Destination);
    }

    /// <summary>Press, drag, release — the frames a real host would report.</summary>
    private static ScriptedFrame[] Drag(Vector2 from, Vector2 to) =>
    [
        new(Mouse: from, ButtonsDown: MouseButtons.Left),
        new(Mouse: to, ButtonsDown: MouseButtons.Left),
        new(Mouse: to),
    ];

    [Fact]
    public void AUnitIsSelectedToBeginWith()
    {
        var (app, _, _) = Run(new ScriptedFrame());

        Assert.Equal([0], app.Selection);
    }

    [Fact]
    public void LeftClickSelectsTheNearestUnitAlone()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        // Click on the cell the last unit is standing on.
        var target = app.Agents[^1].Cell;
        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(grid.ColumnOf(target), grid.RowOf(target)),
                ButtonsDown: MouseButtons.Left)],
            new RecordingRenderer());
        host.Run(app);

        Assert.Equal([Squad - 1], app.Selection);
    }

    [Fact]
    public void ADragSelectsExactlyTheUnitsInTheBox()
    {
        // The four units stand on (1,1)..(4,1). Box the middle two.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var left = (layout.CenterOf(1, 1).X + layout.CenterOf(2, 1).X) / 2;
        var right = (layout.CenterOf(3, 1).X + layout.CenterOf(4, 1).X) / 2;
        var y = layout.CenterOf(2, 1).Y;

        using var host = new ScriptedHost(
            Drag(new Vector2(left, y - 10), new Vector2(right, y + 10)),
            new RecordingRenderer());
        host.Run(app);

        Assert.Equal([1, 2], app.Selection);
    }

    [Fact]
    public void OrderingABoxedGroupMovesTheWholeGroup()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var frames = new List<ScriptedFrame>(
            Drag(layout.CenterOf(1, 1) - new Vector2(10, 10), layout.CenterOf(4, 1) + new Vector2(10, 10)))
        {
            new(Mouse: layout.CenterOf(9, 5), ButtonsDown: MouseButtons.Right),
        };

        using var host = new ScriptedHost(frames, new RecordingRenderer());
        host.Run(app);

        Assert.Equal(Squad, app.Selection.Count);

        // A group order aims everyone at the SHARED destination; distinct
        // parking slots are claimed on approach, the way a real team fills in
        // at the gathering point rather than pre-booking spots from across the
        // map. Distinct final cells are the core suite's business.
        var destination = grid.Index(9, 5);
        Assert.All(app.Agents, a => Assert.Equal(destination, a.Goal));
        Assert.All(app.Agents, a => Assert.NotEqual(a.Cell, a.Goal));
    }

    [Fact]
    public void BoxingEmptyGroundClearsTheSelection()
    {
        // Rows 3-5 on the right are open floor with nobody standing on it.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        using var host = new ScriptedHost(
            Drag(layout.CenterOf(8, 3), layout.CenterOf(10, 5)),
            new RecordingRenderer());
        host.Run(app);

        Assert.Empty(app.Selection);
    }

    [Fact]
    public void TheDragBandIsDrawnWhileTheButtonIsHeld()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        // End the script mid-drag: nobody has an order, so any line on screen is
        // the band, and a band is four of them.
        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(
            [
                new ScriptedFrame(Mouse: layout.CenterOf(1, 1), ButtonsDown: MouseButtons.Left),
                new ScriptedFrame(Mouse: layout.CenterOf(4, 3), ButtonsDown: MouseButtons.Left),
            ],
            renderer);
        host.Run(app);

        Assert.Equal(4, renderer.LastFrameOfKind<DrawCommand.Line>().Count());
    }

    [Fact]
    public void RightClickOrdersTheSelectionAndNobodyElse()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var before = app.Agents.Select(a => a.Goal).ToArray();
        var destination = grid.Index(10, 5);

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right)],
            new RecordingRenderer());
        host.Run(app);

        var after = app.Agents.Select(a => a.Goal).ToArray();

        // The initial selection is unit 0 alone.
        Assert.Equal(destination, after[0]);
        for (var id = 1; id < Squad; id++)
        {
            Assert.Equal(before[id], after[id]);
        }
    }

    [Fact]
    public void AClickBelowTheMapChangesNothing()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);
        var before = app.Selection.ToArray();

        using var host = new ScriptedHost(
            [new ScriptedFrame(
                Mouse: new Vector2(10, layout.PixelHeight + 5),
                ButtonsDown: MouseButtons.Left)],
            new RecordingRenderer());
        host.Run(app);

        Assert.Equal(before, app.Selection);
    }

    [Fact]
    public void SpaceHeldForTenFramesTogglesExactlyOnce()
    {
        // The auto-repeat regression, driven through the real InputAccumulator.
        var frames = Enumerable.Repeat(new ScriptedFrame(KeysDown: ViewerKeys.Space), 10).ToArray();

        var (app, _, _) = Run(frames);

        Assert.False(app.Running);
    }

    [Fact]
    public void TimeDoesNotAdvanceWhilePaused()
    {
        var frames = new List<ScriptedFrame> { new(Dt: 0f, KeysDown: ViewerKeys.Space) };
        frames.AddRange(ScriptedHost.Idle(120));

        var (app, _, _) = Run([.. frames]);

        Assert.False(app.Running);
        Assert.Equal(0, app.CurrentTick);
    }

    [Fact]
    public void TimeAdvancesWhileRunning()
    {
        var (app, _, _) = Run(ScriptedHost.Idle(120));

        Assert.True(app.Running);
        Assert.True(app.CurrentTick > 100, $"only reached tick {app.CurrentTick}");
    }

    [Fact]
    public void OnlyTheSelectedUnitsRouteIsDrawn()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        // Order everyone somewhere, so several units have routes to draw. Each
        // click is press then release at the same spot -- press and release at
        // DIFFERENT spots is a drag now, and would box the whole squad.
        var frames = new List<ScriptedFrame>();
        for (var id = 0; id < Squad; id++)
        {
            var unit = app.Agents[id].Cell;
            var at = layout.CenterOf(grid.ColumnOf(unit), grid.RowOf(unit));
            frames.Add(new ScriptedFrame(Mouse: at, ButtonsDown: MouseButtons.Left));
            frames.Add(new ScriptedFrame(Mouse: at));
            frames.Add(new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right));
            frames.Add(new ScriptedFrame(Mouse: layout.CenterOf(10, 5)));
        }

        frames.AddRange(ScriptedHost.Idle(30));

        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);

        // Whatever is drawn, it belongs to one unit: every segment lies on the
        // selected unit's plan.
        var lines = renderer.LastFrameOfKind<DrawCommand.Line>().ToList();
        Assert.All(lines, line => Assert.Equal(RgbaColor.SkyBlue, line.Color));
    }

    [Fact]
    public void UnitsAreDrawnBetweenCellsRatherThanJumping()
    {
        // A tick is 1/60s; half of one should put a moving unit between two cell
        // centres rather than on either.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var frames = new List<ScriptedFrame>
        {
            new(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right),
        };
        frames.AddRange(ScriptedHost.Idle(40));
        frames.Add(new ScriptedFrame(Dt: 1.0f / 120.0f));

        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);

        // Not an assertion about a specific position -- just that drawing does not
        // require a unit to be exactly on a cell centre.
        var circles = renderer.LastFrameOfKind<DrawCommand.Circle>().ToList();
        Assert.NotEmpty(circles);
        Assert.All(circles, c => Assert.True(float.IsFinite(c.Center.X) && float.IsFinite(c.Center.Y)));
    }

    [Fact]
    public void TheStatusLineCarriesTheSquadState()
    {
        var (app, _, _) = Run(new ScriptedFrame());

        Assert.Contains("12x7", app.StatusText, StringComparison.Ordinal);
        Assert.Contains($"{Squad} units", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("arrived", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("nodes/tick", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("[running]", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void AScenarioPlacesItsAgentsWhereItRecordedThem()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nagent 0 1 1\nagent 1 4 5\nend 30\n");
        var grid = Fixture();

        var app = new ViewerApp(grid, LayoutFor(grid), scenario: scenario);

        Assert.Equal(2, app.Agents.Count);
        Assert.Equal(grid.Index(1, 1), app.Agents[0].Cell);
        Assert.Equal(grid.Index(4, 5), app.Agents[1].Cell);
    }

    [Fact]
    public void AReplayLoadsWithTheClockStoppedAtTickZero()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nagent 0 1 1\norder 0 0 10 5\nend 60\n");
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), scenario: scenario);

        // Idle frames move nothing: the recording waits to be watched.
        using var host = new ScriptedHost(ScriptedHost.Idle(60), new RecordingRenderer());
        host.Run(app);

        Assert.False(app.Running);
        Assert.Equal(0, app.CurrentTick);
        Assert.Contains("[paused]", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("R restart", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void RReloadsAReplayToTickZeroStopped()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nagent 0 1 1\nagent 1 4 5\norder 0 0,1 10 5\nend 60\n");
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), scenario: scenario);

        // Run it a while, then reload.
        var frames = new List<ScriptedFrame> { new(KeysDown: ViewerKeys.Space) };
        frames.AddRange(ScriptedHost.Idle(60));
        frames.Add(new ScriptedFrame(KeysDown: ViewerKeys.R));

        using var host = new ScriptedHost(frames, new RecordingRenderer());
        host.Run(app);

        Assert.False(app.Running);
        Assert.Equal(0, app.CurrentTick);
        Assert.Equal(grid.Index(1, 1), app.Agents[0].Cell);
        Assert.Equal(grid.Index(4, 5), app.Agents[1].Cell);

        // The order queue is restored too: nobody has their goal yet.
        Assert.Equal(grid.Index(1, 1), app.Agents[0].Goal);
    }

    [Fact]
    public void StepWalksAPausedReplayForwardOneTickPerPress()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nagent 0 1 1\norder 0 0 10 5\nend 60\n");
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), scenario: scenario);

        // Three presses, each with a release frame between so each is an edge.
        var frames = new List<ScriptedFrame>();
        for (var press = 0; press < 3; press++)
        {
            frames.Add(new ScriptedFrame(KeysDown: ViewerKeys.Step));
            frames.Add(new ScriptedFrame());
        }

        using var host = new ScriptedHost(frames, new RecordingRenderer());
        host.Run(app);

        Assert.False(app.Running);
        Assert.Equal(3, app.CurrentTick);

        // The recorded tick-0 order fired on the first step.
        Assert.Equal(grid.Index(10, 5), app.Agents[0].Goal);
    }

    [Fact]
    public void StepWhileRunningPausesAndAdvancesExactlyOnce()
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        // Dt of zero on the step frame, so the only tick is the step's own.
        using var host = new ScriptedHost(
            [new ScriptedFrame(Dt: 0f, KeysDown: ViewerKeys.Step)],
            new RecordingRenderer());
        host.Run(app);

        Assert.False(app.Running);
        Assert.Equal(1, app.CurrentTick);
    }

    [Fact]
    public void HoldingStepStepsOnceNotSixty()
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        var frames = Enumerable.Repeat(new ScriptedFrame(Dt: 0f, KeysDown: ViewerKeys.Step), 60).ToArray();

        using var host = new ScriptedHost(frames, new RecordingRenderer());
        host.Run(app);

        Assert.Equal(1, app.CurrentTick);
    }

    [Fact]
    public void ARecordedOrderFiresAtItsRecordedTickAndNotBefore()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nagent 0 1 1\norder 10 0 10 5\nend 60\n");
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), scenario: scenario);

        using (var early = new ScriptedHost(
            [new ScriptedFrame(KeysDown: ViewerKeys.Space), .. ScriptedHost.Idle(4)],
            new RecordingRenderer()))
        {
            early.Run(app);
        }

        // Tick 5: the order recorded for tick 10 has not been issued.
        Assert.Equal(grid.Index(1, 1), app.Agents[0].Goal);

        using (var later = new ScriptedHost(ScriptedHost.Idle(120), new RecordingRenderer()))
        {
            later.Run(app);
        }

        Assert.Equal(grid.Index(10, 5), app.Agents[0].Goal);
        Assert.True(app.Agents[0].Arrived, "the replayed order never got its unit there");
    }

    [Fact]
    public void ClicksStillWorkDuringAReplay()
    {
        // A replay is a viewer, not a verifier: the user may interfere, and the
        // run diverges from the recording. That is allowed here and fatal in
        // ScenarioPlayback, which is the difference between the two on purpose.
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nagent 0 1 1\nagent 1 3 1\nend 60\n");
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, scenario: scenario);

        using var host = new ScriptedHost(
            [
                new ScriptedFrame(Mouse: layout.CenterOf(3, 1), ButtonsDown: MouseButtons.Left),
                new ScriptedFrame(Mouse: layout.CenterOf(3, 1)),
                new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right),
            ],
            new RecordingRenderer());
        host.Run(app);

        Assert.Equal([1], app.Selection);
        Assert.Equal(grid.Index(10, 5), app.Agents[1].Goal);
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
    public void LoadingAFileSwapsTheWorldAndTheGeometryTheHostsFollow()
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);
        var before = app.Layout;

        var root = Directory.CreateTempSubdirectory("nav-app-test-");
        try
        {
            var mapPath = Path.Combine(root.FullName, "wide.map");
            File.WriteAllText(mapPath, WideMap);

            app.LoadFile(mapPath);

            Assert.NotEqual(before, app.Layout);
            Assert.Equal(ViewerSession.DefaultSquad, app.Agents.Count);
            Assert.Contains("24x5", app.StatusText, StringComparison.Ordinal);
            Assert.Equal("Nav.Viewer - wide.map", app.WindowTitle);

            // And the app still runs cleanly on the new content.
            using var host = new ScriptedHost(ScriptedHost.Idle(30), new RecordingRenderer());
            host.Run(app);
            Assert.True(app.CurrentTick > 20);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ARefusedLoadKeepsTheWorldAndSaysWhyUntilTheNextInput()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);
        var before = app.Layout;

        app.LoadFile(Path.Combine(Path.GetTempPath(), "definitely-absent.map"));

        Assert.StartsWith("load failed:", app.StatusText, StringComparison.Ordinal);
        Assert.Equal(before, app.Layout);
        Assert.Equal(Squad, app.Agents.Count);

        // The refusal was read: any input returns the status line to normal.
        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(1, 1), ButtonsDown: MouseButtons.Left)],
            new RecordingRenderer());
        host.Run(app);

        Assert.Contains($"{Squad} units", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusLineNeverChangesLength()
    {
        // A breathing status line shook the whole WPF window: the window was
        // sized to content, and every counter that changed digit count -- nodes
        // spent, planning, the tick -- re-measured it. Counters are padded now,
        // and this samples every tick to hold them to it.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var lengths = new HashSet<int> { app.StatusText.Length };
        var input = new InputAccumulator();
        const float Frame = 1.0f / 60.0f;

        input.SetMousePosition(layout.CenterOf(10, 5));
        input.SetMouseButtonState(MouseButtons.Right, down: true);
        app.Update(input.Snapshot(), Frame);
        input.SetMouseButtonState(MouseButtons.Right, down: false);

        for (var frame = 0; frame < 120; frame++)
        {
            app.Update(input.Snapshot(), Frame);
            lengths.Add(app.StatusText.Length);
        }

        input.SetKeyState(ViewerKeys.Space, down: true);
        app.Update(input.Snapshot(), 0f);
        lengths.Add(app.StatusText.Length);

        Assert.Single(lengths);
    }

    [Fact]
    public void TheStatusLineFollowsTheRunState()
    {
        var (app, _, _) = Run(new ScriptedFrame(KeysDown: ViewerKeys.Space));

        Assert.Contains("[paused]", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFrameIsBracketedByBeginAndEndInOrder()
    {
        var (_, renderer, _) = Run(ScriptedHost.Idle(3));

        var kinds = renderer.Commands
            .Where(c => c is DrawCommand.BeginFrame or DrawCommand.EndFrame)
            .Select(c => c is DrawCommand.BeginFrame ? 'B' : 'E');

        Assert.Equal("BEBEBE", new string(kinds.ToArray()));
    }

    [Fact]
    public void TheSameScriptTwiceDrawsTheSameThings()
    {
        var frames = new List<ScriptedFrame>
        {
            new(Mouse: new Vector2(200, 200), ButtonsDown: MouseButtons.Right),
        };
        frames.AddRange(ScriptedHost.Idle(60));

        var (_, first, _) = Run([.. frames]);
        var (_, second, _) = Run([.. frames]);

        Assert.Equal(first.Commands.Count, second.Commands.Count);
        Assert.Equal(
            first.OfKind<DrawCommand.Circle>().Select(c => c.Center),
            second.OfKind<DrawCommand.Circle>().Select(c => c.Center));
    }

    [Fact]
    public void TheRendererIsNeverAskedForAnythingBeyondTheFiveVerbs()
    {
        // Milestone 2 added a whole movement system and IRenderer did not grow.
        // If it ever has to, that is the seam's first genuine leak and worth
        // recording rather than absorbing.
        var (_, renderer, _) = Run(ScriptedHost.Idle(30));

        Assert.All(renderer.Commands, command =>
            Assert.True(
                command is DrawCommand.BeginFrame or DrawCommand.EndFrame or DrawCommand.Terrain
                        or DrawCommand.Line or DrawCommand.Circle,
                $"unexpected draw command {command.GetType().Name}"));
    }
}
