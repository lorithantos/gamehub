using System.Globalization;
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

    /// <summary>
    /// Idle frames, one simulation step each, so a count reads in TICKS.
    /// </summary>
    /// <remarks>
    /// These tests asserted tick counts after N frames of a sixtieth of a second,
    /// which silently encoded the project''s clock speed into a test about
    /// something else. Calibrating the world -- a tick became a quarter second,
    /// because one cell per tick at 60 Hz is 432 km/h -- turned three of them
    /// red without anything they were testing having changed.
    /// </remarks>
    private static ScriptedFrame[] Ticks(int count) =>
        ScriptedHost.Idle(count, (float)WorldScale.Default.SecondsPerTick);

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
    public void TheAppOpensAndClosesTheFrameExactlyOncePerFrame()
    {
        // Frame ownership is the app's, and this pins it. The WPF host used to
        // bracket Render a second time, which cost an extra full-target clear and
        // -- because EndFrame flushed the batch without emptying it -- submitted
        // every line and circle twice. Nothing caught it, because a host is not
        // otherwise observable and opaque geometry drawn twice looks identical.
        var (_, renderer, _) = Run(new ScriptedFrame(), new ScriptedFrame(), new ScriptedFrame());

        Assert.Equal(3, renderer.OfKind<DrawCommand.BeginFrame>().Count());
        Assert.Equal(3, renderer.OfKind<DrawCommand.EndFrame>().Count());

        // And in the right order: every frame opens before it draws and closes
        // after, so a Begin is never adjacent to another Begin.
        var brackets = renderer.Commands
            .Where(c => c is DrawCommand.BeginFrame or DrawCommand.EndFrame)
            .Select(c => c is DrawCommand.BeginFrame)
            .ToArray();

        Assert.Equal([true, false, true, false, true, false], brackets);
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
        var (app, _, _) = Run(Ticks(120));

        Assert.True(app.Running);
        Assert.True(app.CurrentTick > 100, $"only reached tick {app.CurrentTick}");
    }

    [Fact]
    public void ThePaceKeyCyclesFullThenTwoThenOnePerSecond()
    {
        // One press per pair of frames: the key must be released between
        // presses or the accumulator reports one edge, which is the same
        // auto-repeat rule Space is held to.
        var frames = new List<ScriptedFrame>();
        var labels = new List<string>();

        var renderer = new RecordingRenderer();
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        foreach (var _ in Enumerable.Range(0, 4))
        {
            labels.Add(app.PaceLabel);
            using var host = new ScriptedHost(
                [new ScriptedFrame(Dt: 0f, KeysDown: ViewerKeys.Pace), new ScriptedFrame(Dt: 0f)], renderer);
            host.Run(app);
        }

        Assert.Equal(["full", "2/s", "1/s", "full"], labels);
    }

    [Fact]
    public void ASlowPaceAdvancesFewerTicksForTheSameWallClock()
    {
        // Two seconds of wall clock. At the map's own rate that is 120 ticks;
        // at two ticks a second it is 4, which is the whole point -- the same
        // run, slow enough to read. The simulation is driven by tick count, so
        // nothing about what happens changes.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        var frames = new List<ScriptedFrame>
        {
            new(Dt: 0f, KeysDown: ViewerKeys.Pace),
            new(Dt: 0f),
        };
        frames.AddRange(ScriptedHost.Idle(120));

        using var host = new ScriptedHost(frames, new RecordingRenderer());
        host.Run(app);

        Assert.Equal("2/s", app.PaceLabel);
        Assert.InRange(app.CurrentTick, 3, 5);
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
        // selected unit's plan, so every segment wears that plan's colour.
        //
        // Named rather than derived from IsPartial. All four were sent to the
        // same cell and only one can have it, so most of them are planning to
        // the edge of the reservation window -- which the map has stopped
        // colouring differently, precisely because that is the usual case.
        var route = app.Session.CurrentPlans().First(p => p.Agent == app.Selection[0]).Plan;
        Assert.False(route.IsStuck);

        var lines = renderer.LastFrameOfKind<DrawCommand.Line>().ToList();
        Assert.All(lines, line => Assert.Equal(RgbaColor.SkyBlue, line.Color));
    }

    /// <summary>A corridor with the far end three steps away — well inside the window.</summary>
    private const string ShortCorridor =
        """
        type octile
        height 3
        width 6
        map
        @@@@@@
        @....@
        @@@@@@
        """;

    /// <summary>
    /// A corridor longer than the reservation window is deep, so an order to the
    /// far end cannot be planned all the way.
    /// </summary>
    private const string LongCorridor =
        """
        type octile
        height 3
        width 44
        map
        @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
        @..........................................@
        @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
        """;

    /// <summary>One unit, ordered somewhere, one tick in.</summary>
    private static (ViewerApp App, RecordingRenderer Renderer) Ordered(string map, int goalX)
    {
        var grid = Grid.FromMapText(map);
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, squad: 1);
        var renderer = new RecordingRenderer();

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(goalX, 1), ButtonsDown: MouseButtons.Right), .. Ticks(1)],
            renderer);
        host.Run(app);

        return (app, renderer);
    }

    private static PlanResult PlanOf(ViewerApp app, int agent) =>
        app.Session.CurrentPlans().First(p => p.Agent == agent).Plan;

    /// <summary>
    /// The plan's repeated cells -- one tick of standing still apiece -- as the
    /// pixel centres a mark would land on. Every one of them, which is what the
    /// inspector's <c>waits</c> row counts.
    /// </summary>
    private static List<Vector2> WaitsIn(ViewerApp app, PlanResult plan) =>
        [.. Enumerable.Range(1, Math.Max(0, plan.Cells.Count - 1))
            .Where(i => plan.Cells[i - 1] == plan.Cells[i])
            .Select(i => app.CenterOfCell(plan.Cells[i]))];

    /// <summary>
    /// The index of the plan's first actual step -- the end of the run of
    /// repeats the planner's latency pads the head with.
    /// </summary>
    private static int FirstStepIn(PlanResult plan)
    {
        var first = 1;
        while (first < plan.Cells.Count && plan.Cells[first - 1] == plan.Cells[first])
        {
            first++;
        }

        return first;
    }

    /// <summary>
    /// The repeats the map marks: the ones after the plan's first step. The run
    /// at the head is latency and is not a wait, however much it looks like one.
    /// </summary>
    private static List<Vector2> InteriorWaitsIn(ViewerApp app, PlanResult plan) =>
        [.. Enumerable.Range(1, Math.Max(0, plan.Cells.Count - 1))
            .Where(i => i > FirstStepIn(plan) && plan.Cells[i - 1] == plan.Cells[i])
            .Select(i => app.CenterOfCell(plan.Cells[i]))];

    /// <summary>
    /// The wait marks in the last frame drawn, identified by the route's colour
    /// rather than by size: no unit wears it, and a radius is a presentation
    /// detail these tests should not pin.
    /// </summary>
    private static List<Vector2> MarksIn(RecordingRenderer renderer) =>
        [.. renderer.LastFrameOfKind<DrawCommand.Circle>()
            .Where(c => c.Color == RgbaColor.SkyBlue)
            .Select(c => c.Center)];

    /// <summary>
    /// A one-wide corridor with room to walk down before running into anybody.
    /// </summary>
    private const string QueueCorridor =
        """
        type octile
        height 3
        width 12
        map
        @@@@@@@@@@@@
        @..........@
        @@@@@@@@@@@@
        """;

    /// <summary>
    /// Two units in <see cref="QueueCorridor"/>: the front one sent to a cell
    /// short of the rear one's goal, so the rear one walks up behind it and then
    /// stands still for the rest of its plan. Queued, not refused.
    /// </summary>
    /// <remarks>
    /// The point of the walk is that the wait lands in the plan's INTERIOR,
    /// after real steps, which is the only shape the map marks.
    /// <para>
    /// It has to be caught in the middle. The fixture this replaced ordered the
    /// rear unit straight into a blocker's back and its plan came out as five
    /// repeats of the cell it was already standing on -- every one of them
    /// indistinguishable from the planner's latency pad, and marked only because
    /// the map was marking the pad too. This one is read while it is still
    /// closing the gap: earlier its plan is all head, later it has closed up and
    /// its whole plan is standing still again.
    /// </para>
    /// </remarks>
    private static (ViewerApp App, RecordingRenderer Renderer) Queued()
    {
        var grid = Grid.FromMapText(QueueCorridor);
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, squad: 2);
        var renderer = new RecordingRenderer();

        var front = app.Agents[1].Cell;
        var rear = app.Agents[0].Cell;

        // Select and order each in turn -- press and release on the same spot,
        // because two different spots is a drag and would box them both.
        ScriptedFrame[] OrderFrom(int unit, int goalX) =>
        [
            new(Mouse: layout.CenterOf(grid.ColumnOf(unit), grid.RowOf(unit)), ButtonsDown: MouseButtons.Left),
            new(Mouse: layout.CenterOf(grid.ColumnOf(unit), grid.RowOf(unit))),
            new(Mouse: layout.CenterOf(goalX, 1), ButtonsDown: MouseButtons.Right),
            new(Mouse: layout.CenterOf(goalX, 1)),
        ];

        // The rear unit is ordered last, so it is the one left selected and the
        // one whose route is drawn.
        using var host = new ScriptedHost(
            [.. OrderFrom(front, goalX: 6), .. OrderFrom(rear, goalX: 10), .. Ticks(12)],
            renderer);
        host.Run(app);

        return (app, renderer);
    }

    /// <summary>
    /// Open ground, walled at the edge, big enough that a corner-to-corner order
    /// outruns the reservation window. The unit replans on the move, so there
    /// are ticks where a search is in flight while the plan it will replace is
    /// still the one being drawn.
    /// </summary>
    private static Grid OpenField()
    {
        const int Width = 60;
        const int Height = 40;

        var lines = new List<string> { "type octile", $"height {Height}", $"width {Width}", "map" };
        for (var y = 0; y < Height; y++)
        {
            lines.Add(y == 0 || y == Height - 1
                ? new string('@', Width)
                : $"@{new string('.', Width - 2)}@");
        }

        return Grid.FromMapText(string.Join("\n", lines));
    }

    /// <summary>One unit crossing <see cref="OpenField"/>, some way in.</summary>
    private static (ViewerApp App, RecordingRenderer Renderer) CrossingTheField(int ticks)
    {
        var grid = OpenField();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, squad: 1);
        var renderer = new RecordingRenderer();

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(58, 38), ButtonsDown: MouseButtons.Right), .. Ticks(ticks)],
            renderer);
        host.Run(app);

        return (app, renderer);
    }

    [Fact]
    public void AWaitInThePlanDrawsAMarkRatherThanNothing()
    {
        // A repeated cell is a deliberate tick of standing still -- here a unit
        // that has walked up behind a slower one and can go no further. Every
        // one of them used to draw nothing at all, so the route had a silent gap
        // exactly where the waiting was, and a queued unit read as one that had
        // stopped caring about the order.
        var (app, renderer) = Queued();
        var plan = PlanOf(app, 0);

        // The shape the mark now depends on, and no flag: the plan takes a real
        // step before it starts repeating, so the repeats are its own and not
        // the latency the planner pads every fresh plan's head with.
        Assert.Equal(1, FirstStepIn(plan));

        var waits = InteriorWaitsIn(app, plan);
        Assert.NotEmpty(waits);

        var marks = MarksIn(renderer);
        Assert.Equal(waits.Count, marks.Count);
        Assert.All(waits, wait => Assert.Contains(wait, marks));
    }

    [Fact]
    public void TheThinkingFlagNeverSeparatedTheLatencyPadFromAWait()
    {
        // The rejected fix, kept because it looked right and measured false.
        //
        // The repeats at the head of a plan are the planner's latency: Commit
        // pads from the current tick out to AnchorTick, which is CurrentTick +
        // Latency, and the search's own first cell repeats the anchor cell
        // again. Marking them dropped a cluster of dots on the ground under the
        // unit's own feet on every route.
        //
        // Gating the mark on AgentState.Thinking was supposed to close that, on
        // the reading that a repeat while a search is in flight is the unit
        // holding for a plan of its own. It closed nothing: the pad is written
        // in when the plan is COMMITTED, so by the time it is drawn the search
        // that caused it has finished and Thinking is already false. Here are
        // the two ticks that show it -- the same plan, the same repeats, the
        // flag opposite in each, and the pad drawn in both.
        var (quiet, quietRenderer) = CrossingTheField(ticks: 10);
        var (searching, searchingRenderer) = CrossingTheField(ticks: 30);

        Assert.False(quiet.Agents[0].Thinking);
        Assert.True(searching.Agents[0].Thinking, "the fixture stopped catching a search in flight");

        // Same plan in both, so the only thing that differs is the flag.
        var plan = PlanOf(quiet, 0);
        Assert.Equal(plan.Cells, PlanOf(searching, 0).Cells);

        // It does have repeats -- they are simply all in the head run, which is
        // the half the flag was blind to.
        Assert.NotEmpty(WaitsIn(quiet, plan));
        Assert.Empty(InteriorWaitsIn(quiet, plan));

        Assert.Empty(MarksIn(quietRenderer));
        Assert.Empty(MarksIn(searchingRenderer));
    }

    [Fact]
    public void TheHeadOfAPlanIsLatencyAndOnlyItsInteriorIsAWait()
    {
        // The distinction the map draws, both sides of it in one place.
        //
        // A unit that has just been ordered across open ground has a plan whose
        // head is nothing but repeats and whose interior has none: it is not
        // waiting for anybody, it is waiting for its own search, and it draws no
        // marks at all. A unit queued behind a slower one has stepped first and
        // then stopped, and every repeat after that first step is marked.
        var (crossing, crossingRenderer) = CrossingTheField(ticks: 10);
        var crossingPlan = PlanOf(crossing, 0);

        Assert.True(FirstStepIn(crossingPlan) > 1, "the fixture stopped starting with a latency pad");
        Assert.Empty(MarksIn(crossingRenderer));

        var (queued, queuedRenderer) = Queued();
        var queuedPlan = PlanOf(queued, 0);

        Assert.NotEmpty(InteriorWaitsIn(queued, queuedPlan));
        Assert.Equal(InteriorWaitsIn(queued, queuedPlan).Count, MarksIn(queuedRenderer).Count);
    }

    [Fact]
    public void APartialPlanLooksExactlyLikeAWholeOne()
    {
        // Planning is bounded by the reservation window, so a plan that stops
        // short of the goal is ordinary progress -- the agent walks as far as it
        // booked and replans when the window moves. The map used to say so in
        // orange, and saying so cost the colour its meaning: a unit under a
        // group order plans a step at a time and is partial almost continuously,
        // so almost every route was orange almost always. The flag is a row in
        // the inspector now, and the map keeps colours for things that differ.
        var (whole, wholeRenderer) = Ordered(ShortCorridor, goalX: 4);
        Assert.False(PlanOf(whole, 0).IsPartial, "three steps should be inside anybody's window");

        var wholeLines = wholeRenderer.LastFrameOfKind<DrawCommand.Line>().ToList();
        Assert.NotEmpty(wholeLines);
        Assert.All(wholeLines, line => Assert.Equal(RgbaColor.SkyBlue, line.Color));

        // Forty-one cells away with thirty-two ticks of window: the same unit
        // doing the same thing, unable to plan the whole way.
        var (edge, edgeRenderer) = Ordered(LongCorridor, goalX: 42);
        Assert.True(PlanOf(edge, 0).IsPartial, "the fixture stopped outrunning the reservation window");

        var edgeLines = edgeRenderer.LastFrameOfKind<DrawCommand.Line>().ToList();
        Assert.NotEmpty(edgeLines);
        Assert.All(edgeLines, line => Assert.Equal(RgbaColor.SkyBlue, line.Color));
    }

    [Fact]
    public void TheReasonAStuckPlanNeverReachesTheOverlay()
    {
        // The route wears ONE colour, and this is why there is nothing for a
        // second one to say.
        //
        // A stuck plan gave up entirely, and for a while the overlay drew it
        // orange. That branch could never run. A stuck result has NO cells -- not
        // even a tick of standing still -- so Render's own "fewer than two cells"
        // guard would skip it, and it does not get that far anyway: MovementSystem
        // leaves a stuck agent's Plan null rather than storing the result, so it
        // never appears in CurrentPlans at all. The branch came out; an unreachable
        // colour is dead code on a map whose whole job is to show what is
        // happening.
        //
        // THE GAP IS NARROWER THAN IT WAS. A unit that has given up shows on the
        // map as a unit with no route -- and that no longer looks like an arrived
        // or an unordered unit, because having no plan is precisely what the
        // no-route cross draws and those two are guarded out of it. See
        // AUnitWithNoRouteIsMarked.
        //
        // What is still missing is the REASON. The cross says "nothing to walk",
        // which is true of a unit that gave up and equally true of one that was
        // ordered a moment ago and has not been planned yet; only the inspector
        // separates them. Distinguishing them on the map needs the stuck result
        // itself, which is discarded before the viewer sees it. If a change ever
        // routes one into CurrentPlans, this test is where that decision was
        // left, and it is worth doing properly rather than by reviving the colour.
        var stuck = PlanResult.Stuck(startTick: 0, expanded: 0);
        Assert.Empty(stuck.Cells);

        // Which is under the two cells a segment needs, so Render's own guard
        // would skip it even if it did arrive.
        Assert.True(stuck.Cells.Count < 2);

        // And it does not arrive. Four units sent to one cell, which only one of
        // them can have, is as crowded as this fixture gets -- and no plan the
        // viewer can see is ever stuck, because MovementSystem drops the result
        // and leaves the agent's plan null instead of publishing it.
        //
        // Checked EVERY tick rather than at the end. A stuck plan would be one
        // frame wide, and looking only at the last frame is how a branch comes to
        // be believed reachable in the first place.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);
        app.Session.Select([.. Enumerable.Range(0, Squad)]);

        using (var host = new ScriptedHost(
            [
                new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right),
                new ScriptedFrame(Mouse: layout.CenterOf(10, 5)),
            ],
            new RecordingRenderer()))
        {
            host.Run(app);
        }

        var idle = new InputState(layout.CenterOf(10, 5), ViewerKeys.None, MouseButtons.None, MouseButtons.None);
        for (var tick = 0; tick < 120; tick++)
        {
            app.Update(idle, (float)WorldScale.Default.SecondsPerTick);
            Assert.All(app.Session.CurrentPlans(), p => Assert.NotEmpty(p.Plan.Cells));
        }
    }

    /// <summary>
    /// The no-route crosses in the last frame drawn, one point per marked unit.
    /// </summary>
    /// <remarks>
    /// Each mark is two orange lines through a unit's centre, so both arms share
    /// that centre as their midpoint and the pair collapses to the one point a
    /// test actually wants to name: WHICH UNIT was marked. Identified by colour
    /// rather than by length, because an arm radius is presentation and these
    /// tests should not pin it.
    /// <para>
    /// Midpoints are compared with a tolerance rather than for equality: the arms
    /// are built as centre plus and minus an offset, and float addition does not
    /// promise to give the centre back exactly.
    /// </para>
    /// </remarks>
    private static List<Vector2> NoRouteMarksIn(RecordingRenderer renderer)
    {
        var centres = new List<Vector2>();
        foreach (var line in renderer.LastFrameOfKind<DrawCommand.Line>()
            .Where(l => l.Color == RgbaColor.Orange))
        {
            var centre = (line.From + line.To) / 2f;
            if (!centres.Exists(c => Marks(c, centre)))
            {
                centres.Add(centre);
            }
        }

        return centres;
    }

    private static bool Marks(Vector2 mark, Vector2 unit) => Vector2.Distance(mark, unit) < 0.5f;

    /// <summary>
    /// One unit, ordered, with the clock given nothing to spend: the order lands
    /// and no tick is bought to plan it in.
    /// </summary>
    /// <remarks>
    /// This is the routeless state at its cleanest. The goal is elsewhere so the
    /// unit has not arrived; no search has started, so it is not thinking; and
    /// planning is queued rather than done inline on the order, so
    /// <c>CurrentPlans</c> has nothing for it. It has somewhere to be and no route
    /// there, which is precisely what the mark is for.
    /// </remarks>
    private static (ViewerApp App, RecordingRenderer Renderer) OrderedButUnplanned()
    {
        var grid = Grid.FromMapText(ShortCorridor);
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, squad: 1);
        var renderer = new RecordingRenderer();

        using var host = new ScriptedHost(
            [new ScriptedFrame(Dt: 0f, Mouse: layout.CenterOf(4, 1), ButtonsDown: MouseButtons.Right)],
            renderer);
        host.Run(app);

        return (app, renderer);
    }

    [Fact]
    public void AUnitWithNoRouteIsMarked()
    {
        // The absence CurrentPlans was already reporting and the viewer was
        // throwing away: it lists only agents that HAVE a plan, so an agent in
        // Agents and missing from it has nothing to walk. Until now that looked
        // exactly like a unit standing on its goal.
        var (app, renderer) = OrderedButUnplanned();
        var unit = app.Agents[0];

        // All four conditions, stated rather than assumed, so a fixture that
        // drifts into some other state fails here instead of silently testing it.
        Assert.Empty(app.Session.CurrentPlans());
        Assert.True(unit.Alive);
        Assert.False(unit.Arrived);
        Assert.False(unit.Thinking);

        var marks = NoRouteMarksIn(renderer);
        var mark = Assert.Single(marks);
        Assert.True(Marks(mark, app.CenterOfCell(unit.Cell)), $"the cross landed at {mark}, not on the unit");
    }

    [Fact]
    public void AnArrivedUnitIsNotMarked()
    {
        // No plan because none is needed. This is the state the mark would
        // otherwise be indistinguishable from -- and it is the COMMON one, which
        // is the whole reason an unqualified "has no plan" is not worth drawing.
        //
        // A unit that has never been ordered is the shape that matters, and it is
        // the one this test was rebuilt around. Ordering a unit and running it to
        // its goal proves nothing: it arrives still holding the plan that got it
        // there, so it is excluded by being in CurrentPlans and the arrived guard
        // is never consulted. Measured, not assumed -- with the guard deleted that
        // version still passed. A freshly placed unit has its own cell for a goal
        // and no plan at all, so it is arrived AND absent from CurrentPlans, which
        // is exactly the pair the guard exists to separate.
        var (fresh, freshRenderer, _) = Run(new ScriptedFrame());

        Assert.All(fresh.Agents, a => Assert.True(a.Arrived));
        Assert.Empty(fresh.Session.CurrentPlans());
        Assert.Empty(NoRouteMarksIn(freshRenderer));

        // And the earned arrival too, for the other half of the state.
        var grid = Grid.FromMapText(ShortCorridor);
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, squad: 1);
        var renderer = new RecordingRenderer();

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(4, 1), ButtonsDown: MouseButtons.Right), .. Ticks(60)],
            renderer);
        host.Run(app);

        Assert.True(app.Agents[0].Arrived, "the fixture never got its unit there");
        Assert.Empty(NoRouteMarksIn(renderer));
    }

    [Fact]
    public void AThinkingUnitIsNotMarked()
    {
        // A search in flight is not "no route" -- it is "not yet". Nothing is
        // committed because the answer has not landed, and marking that would put
        // a cross on every unit for the first tick or two after every order,
        // which is the same always-on distinction the partial-plan colour was
        // taken off the map for.
        //
        // Twenty-four units ordered SEPARATELY, each to its own far cell, which
        // is what makes the state findable at all. One unit on open ground
        // finishes its first search inside one tick's node budget, so the state
        // never appears; and one GROUP order does not produce it either, because
        // a group shares a flow field and its members follow it without spending
        // a node on a search. Two dozen individual orders are two dozen searches
        // against one tick's budget, and the ones at the back of the queue spend
        // ticks thinking with nothing committed.
        var grid = OpenField();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, ViewerSession.DefaultSquad);
        var renderer = new RecordingRenderer();

        for (var id = 0; id < ViewerSession.DefaultSquad; id++)
        {
            app.Session.Select([id]);
            app.Session.OrderSelection(grid.Index(58, 2 + id));
        }

        var caught = 0;
        for (var tick = 0; tick < 40; tick++)
        {
            renderer.Clear();
            using (var step = new ScriptedHost(Ticks(1), renderer))
            {
                step.Run(app);
            }

            var planned = app.Session.CurrentPlans().Select(p => p.Agent).ToHashSet();
            var thinking = app.Agents
                .Where(a => a.Thinking && a.Alive && !a.Arrived && !planned.Contains(a.Id))
                .ToList();

            caught += thinking.Count;

            // Every OTHER condition holds for these units -- they are live,
            // unarrived and planless -- so the thinking flag is the only thing
            // keeping the cross off them.
            var marks = NoRouteMarksIn(renderer);
            Assert.All(thinking, unit =>
                Assert.DoesNotContain(marks, mark => Marks(mark, app.CenterOfCell(unit.Cell))));
        }

        Assert.True(caught > 0, "the fixture never caught a search in flight with nothing committed");
    }

    [Fact]
    public void ARemovedUnitIsNotMarkedWhileALiveRoutelessOneIs()
    {
        // THE GUARD THAT MATTERS. A removed unit keeps its id and its last cell
        // for the life of the system and will never have a plan again, so an
        // unguarded "no plan" marks every body on the field -- and the map where
        // bodies pile up is exactly the map where somebody is trying to count who
        // is still moving.
        //
        // Both halves in one fixture on purpose: a test that only showed the
        // corpse going unmarked would pass just as well if the mark had stopped
        // being drawn at all.
        var grid = Grid.FromMapText(QueueCorridor);
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, squad: 2);
        var renderer = new RecordingRenderer();

        // Ordered, then one of them taken out of the world, then drawn before the
        // clock buys a tick to plan either of them in.
        app.Session.Select([0, 1]);
        app.Session.OrderSelection(grid.Index(10, 1));
        app.Session.Remove(1);

        using var host = new ScriptedHost([new ScriptedFrame(Dt: 0f)], renderer);
        host.Run(app);

        var live = app.Agents[0];
        var body = app.Agents[1];

        Assert.True(live.Alive);
        Assert.False(body.Alive);
        Assert.Empty(app.Session.CurrentPlans());
        Assert.NotEqual(live.Cell, body.Cell);

        var marks = NoRouteMarksIn(renderer);
        var mark = Assert.Single(marks);
        Assert.True(Marks(mark, app.CenterOfCell(live.Cell)), "the live routeless unit lost its mark");
        Assert.False(Marks(mark, app.CenterOfCell(body.Cell)), "a body was marked as having no route");
    }

    private static string InspectorValue(ViewerApp app, string label) =>
        app.Inspector.Single(row => row.Group == "Route" && row.Label == label).Value;

    [Fact]
    public void TheInspectorCarriesTheRouteFactsTheMapStoppedDrawing()
    {
        // Partial and the wait count came off the map because they were true
        // almost always, and a distinction that is always on is not one. They
        // are still facts about the plan, and a row that usually reads the same
        // is fine -- it is a colour that usually reads the same which is noise.
        //
        // Here rather than in InspectorTests because it is the other half of the
        // two tests above: the same two corridors, one showing what the map
        // stopped saying and this one showing where it went.
        var (whole, _) = Ordered(ShortCorridor, goalX: 4);
        var wholePlan = PlanOf(whole, 0);

        Assert.False(wholePlan.IsPartial);
        Assert.Equal("no", InspectorValue(whole, "partial"));
        Assert.NotEmpty(WaitsIn(whole, wholePlan));
        Assert.Equal(WaitsIn(whole, wholePlan).Count.ToString(CultureInfo.InvariantCulture),
            InspectorValue(whole, "waits"));

        var (edge, _) = Ordered(LongCorridor, goalX: 42);
        var edgePlan = PlanOf(edge, 0);

        Assert.True(edgePlan.IsPartial);
        Assert.Equal("yes", InspectorValue(edge, "partial"));
        Assert.Equal(WaitsIn(edge, edgePlan).Count.ToString(CultureInfo.InvariantCulture),
            InspectorValue(edge, "waits"));
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
        Assert.Contains("fields", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("nodes/tick", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("[running]", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLeaderWearsTheDoubledDot()
    {
        // A group order elects a leader; the leader's mark is two extra
        // concentric circles. With all four selected the last frame carries
        // four unit circles, four selection dots, and the leader's pair.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var frames = new List<ScriptedFrame>(
            Drag(layout.CenterOf(1, 1) - new Vector2(10, 10), layout.CenterOf(4, 1) + new Vector2(10, 10)))
        {
            new(Mouse: layout.CenterOf(9, 5), ButtonsDown: MouseButtons.Right),
            new(),
        };

        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);

        Assert.Single(app.Session.Leaders);
        Assert.Equal(Squad + 4 + 2, renderer.LastFrameOfKind<DrawCommand.Circle>().Count());
    }

    [Fact]
    public void AScenarioPlacesItsAgentsWhereItRecordedThem()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\nagent 1 4 5\nend 30\n");
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
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\norder 0 0 10 5\nend 60\n");
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
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\nagent 1 4 5\norder 0 0,1 10 5\nend 60\n");
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
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\norder 0 0 10 5\nend 60\n");
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
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\norder 10 0 10 5\nend 60\n");
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), scenario: scenario);

        using (var early = new ScriptedHost(
            [new ScriptedFrame(KeysDown: ViewerKeys.Space), .. Ticks(4)],
            new RecordingRenderer()))
        {
            early.Run(app);
        }

        // Tick 5: the order recorded for tick 10 has not been issued.
        Assert.Equal(grid.Index(1, 1), app.Agents[0].Goal);

        using (var later = new ScriptedHost(Ticks(120), new RecordingRenderer()))
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
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\nagent 1 3 1\nend 60\n");
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
            using var host = new ScriptedHost(Ticks(30), new RecordingRenderer());
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
