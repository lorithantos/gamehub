using Nav.Core;
using Nav.Core.Interfaces;

namespace Nav.Viewer.Tests;

/// <summary>
/// The session driving a live world: the tick it hands over, and what a restart
/// does to a thing that cannot be rewound.
/// </summary>
/// <remarks>
/// <b>The world here is a fake built on Nav.Core alone, and it has to be.</b>
/// This project references <c>Nav.Viewer.Shared</c> and nothing else, which is
/// the same absence the viewer itself is built behind — so a test that reached
/// for the real guard fight to check that <c>Step</c> gets called would have
/// been the seam leaking through the test project. What is being measured is the
/// session's half of the contract: one step per tick, ascending from zero, and a
/// factory asked again for a world nobody can wind back.
/// </remarks>
public sealed class WorldSessionTests
{
    private static Grid Fixture() => Grid.FromMapText(SampleMaps.CornerCutTrap);

    /// <summary>
    /// A walled empty room, sized to stand a three-digit roster in.
    /// </summary>
    /// <remarks>
    /// The sample map is 12x7 and holds 39 passable cells, which is short of the
    /// boundary that matters: the roster has to reach a hundred for the third
    /// digit to appear.
    /// </remarks>
    private static Grid OpenRoom(int width, int height)
    {
        var lines = new List<string> { "type octile", $"height {height}", $"width {width}", "map" };

        for (var y = 0; y < height; y++)
        {
            lines.Add(y == 0 || y == height - 1
                ? new string('@', width)
                : $"@{new string('.', width - 2)}@");
        }

        return Grid.FromMapText(string.Join('\n', lines));
    }

    /// <summary>How long one tick of a live world lasts, for frame-driven tests.</summary>
    private static float TickSeconds => (float)WorldScale.Default.SecondsPerTick;

    [Fact]
    public void ASessionOverAWorldStepsItOncePerTickCountingFromZero()
    {
        var world = new FakeWorld(Fixture());
        var session = ViewerSession.FromWorld(() => world, "fake");

        session.Tick();
        session.Tick();
        session.Tick();

        // Ascending from zero, one per Tick, no gaps: a world schedules its
        // arrivals against these numbers, so a skipped or repeated one is a wave
        // that never lands or lands twice.
        Assert.Equal([0, 1, 2], world.Steps);
        Assert.Equal(3, session.CurrentTick);
    }

    [Fact]
    public void AWorldSessionRunsFromTheStartAndIsNotAReplay()
    {
        var session = ViewerSession.FromWorld(() => new FakeWorld(Fixture()), "fake");

        Assert.True(session.Running);
        Assert.True(session.IsLiveWorld);
        Assert.True(session.CanRestart);
        Assert.False(session.IsReplay);
        Assert.Null(session.Scenario);
        Assert.Equal("fake", session.MapName);
    }

    [Fact]
    public void RestartAsksTheFactoryForANewWorldAndNeverStepsTheOldOneAgain()
    {
        var built = new List<FakeWorld>();
        var session = ViewerSession.FromWorld(
            () =>
            {
                var world = new FakeWorld(Fixture());
                built.Add(world);
                return world;
            },
            "fake");

        session.Tick();
        session.Tick();
        session.Restart();
        session.Tick();

        // Two worlds, not one wound back -- and the second is a DIFFERENT
        // object, because a factory handing back the instance it handed back
        // before would make R a key that did nothing.
        Assert.Equal(2, built.Count);
        Assert.NotSame(built[0], built[1]);

        // The dead world is left exactly where it stopped. Anything still
        // holding it -- a panel wired once at startup -- is holding history.
        Assert.Equal([0, 1], built[0].Steps);
        Assert.Equal([0], built[1].Steps);

        // And the session is answering for the live one.
        Assert.Equal(1, session.CurrentTick);
        Assert.True(session.Running);
        Assert.Equal([0], session.Selection);
    }

    [Fact]
    public void AFreeMapStillTicksItsOwnBoardAndStepsNoWorld()
    {
        var session = ViewerSession.FromMap(Fixture(), "m.map", squad: 2);

        Assert.False(session.IsLiveWorld);
        Assert.False(session.CanRestart);

        session.Tick();
        session.Tick();
        session.Tick();

        Assert.Equal(3, session.CurrentTick);
    }

    [Fact]
    public void ARecordedScenarioStillIssuesItsOrdersAtTheRecordedTick()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap any.map\nsize 12 7\nagent 0 1 1\norder 2 0 10 5\nend 60\n");
        var grid = Fixture();
        var session = ViewerSession.FromScenario(grid, "run.scenario", scenario);

        Assert.False(session.IsLiveWorld);
        Assert.True(session.CanRestart);

        session.Tick();
        session.Tick();

        // Recorded for tick 2, so nothing is aimed anywhere yet.
        Assert.Equal(grid.Index(1, 1), session.Agents[0].Goal);

        session.Tick();

        Assert.Equal(grid.Index(10, 5), session.Agents[0].Goal);
        Assert.Equal(3, session.CurrentTick);
    }

    [Fact]
    public void AWorldThatBringsUnitsOnMidRunIsDrawnRatherThanCrashingTheFrame()
    {
        // What a wave arriving is, to the viewer: ids on the board that did not
        // exist when the content was adopted. The cells a unit is drawn moving
        // FROM used to be an array sized once, and the frame after the first
        // arrival indexed off the end of it.
        var grid = Fixture();
        var arrival = grid.Index(10, 5);
        var app = new ViewerApp(
            ViewerSession.FromWorld(() => new FakeWorld(grid, agents: 2, arrivalTick: 1, arrivalCell: arrival),
            "fake"),
            1000,
            1000);
        var renderer = new RecordingRenderer();

        using var host = new ScriptedHost(ScriptedHost.Idle(3, TickSeconds), renderer);
        host.Run(app);

        Assert.Equal(3, app.Agents.Count);

        // One circle per unit, plus the marker on the selected one. The arrival
        // is drawn standing where it came onto the board rather than sliding in
        // from wherever the id below it happens to be.
        Assert.Equal(4, renderer.LastFrameOfKind<DrawCommand.Circle>().Count());
    }

    [Fact]
    public void TheStatusLineNeverChangesLengthWhileAWorldBringsUnitsOn()
    {
        // The half of the invariant a fixed roster cannot see, and the reason
        // the version in ViewerAppTests stayed green through the defect: the
        // padding width was the ROSTER's digit count, so a wave landing widened
        // arrived, stuck, planning and sel together -- and the roster count
        // itself was interpolated with no padding at all. In a window sized to
        // content that re-measures, which is the shaking.
        //
        // Two boundaries, because one is a coincidence away from passing: 8
        // units, six more on tick 0 the way the guard world's first wave lands,
        // then a hundred more.
        var grid = OpenRoom(24, 24);
        var app = new ViewerApp(
            ViewerSession.FromWorld(
                () => new FakeWorld(grid, agents: 8, waves: [(0, 6), (5, 100)]),
                "fake"),
            1000,
            1000);

        var lengths = new HashSet<int> { app.StatusText.Length };
        var rosters = new List<int>();
        var input = new InputAccumulator();

        for (var frame = 0; frame < 30; frame++)
        {
            app.Update(input.Drain(), TickSeconds);
            lengths.Add(app.StatusText.Length);
            rosters.Add(app.Agents.Count);
        }

        // Both boundaries were actually crossed. Without this the assertion
        // below is a claim about a world that never grew -- which is exactly
        // what the fixed-roster version of this test has always been.
        Assert.Equal(14, rosters[0]);
        Assert.Equal(114, rosters[^1]);

        Assert.Single(lengths);
    }

    [Fact]
    public void RBuildsTheWorldAgainAndTheViewerDrawsTheSmallerRoster()
    {
        var grid = Fixture();
        var arrival = grid.Index(10, 5);
        var built = new List<FakeWorld>();
        var session = ViewerSession.FromWorld(
            () =>
            {
                var world = new FakeWorld(grid, agents: 2, arrivalTick: 1, arrivalCell: arrival);
                built.Add(world);
                return world;
            },
            "fake");
        var app = new ViewerApp(session, 1000, 1000);
        var renderer = new RecordingRenderer();

        var frames = new List<ScriptedFrame>();
        frames.AddRange(ScriptedHost.Idle(3, TickSeconds));
        frames.Add(new ScriptedFrame(TickSeconds, KeysDown: ViewerKeys.R));

        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);

        // R on a live world is restart, not regroup: a second world, counting
        // from zero again, and the frame it happened on drew the roster that
        // world starts with rather than the one the dead world had grown to.
        Assert.Equal(2, built.Count);
        Assert.Equal([0, 1, 2], built[0].Steps);
        Assert.Equal([0], built[1].Steps);
        Assert.Equal(2, app.Agents.Count);
        Assert.Contains("restart", app.StatusText, StringComparison.Ordinal);
        Assert.Equal(3, renderer.LastFrameOfKind<DrawCommand.Circle>().Count());
    }

    /// <summary>
    /// A world made of nothing but Nav.Core: a board with a few units on it, a
    /// record of every tick it was handed, and the option of one more unit
    /// arriving mid-run.
    /// </summary>
    /// <remarks>
    /// It steps its own board, because that is what makes the tick number the
    /// session hands over ADVANCE -- a world that left the board alone would
    /// leave every step reading tick zero, and the ascending-ticks assertion
    /// above would then be a claim about this fake rather than about the
    /// session.
    /// </remarks>
    private sealed class FakeWorld : IWorld
    {
        private readonly int _arrivalTick;
        private readonly int _arrivalCell;
        private readonly IReadOnlyList<(int Tick, int Count)> _waves;

        /// <summary>The next empty cell a wave lands on, walked forward as they do.</summary>
        private int _next;

        public FakeWorld(
            Grid grid,
            int agents = 2,
            int arrivalTick = -1,
            int arrivalCell = -1,
            IReadOnlyList<(int Tick, int Count)>? waves = null)
        {
            Grid = grid;
            Board = new MovementSystem(grid);
            _arrivalTick = arrivalTick;
            _arrivalCell = arrivalCell;
            _waves = waves ?? [];

            Place(agents);
        }

        public Grid Grid { get; }

        public MovementSystem Board { get; }

        /// <summary>Every tick number this world was handed, in order.</summary>
        public List<int> Steps { get; } = [];

        public void Step(int tick)
        {
            Steps.Add(tick);

            if (tick == _arrivalTick)
            {
                Board.AddAgent(_arrivalCell);
            }

            foreach (var wave in _waves)
            {
                if (wave.Tick == tick)
                {
                    Place(wave.Count);
                }
            }

            Board.Tick();
        }

        /// <summary>Puts <paramref name="count"/> units on the next empty cells.</summary>
        private void Place(int count)
        {
            var placed = 0;
            for (; _next < Grid.CellCount && placed < count; _next++)
            {
                if (Grid.IsPassable(_next) && _next != _arrivalCell)
                {
                    Board.AddAgent(_next);
                    placed++;
                }
            }

            Assert.Equal(count, placed);
        }
    }
}
