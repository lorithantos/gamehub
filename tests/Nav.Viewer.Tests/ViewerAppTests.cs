using System.Numerics;

using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// The viewer's behaviour, driven with no window, no renderer and no graphics
/// assembly in the process.
/// </summary>
public sealed class ViewerAppTests
{
    private const int StatusHeight = 26;
    private const float Tolerance = 1e-4f;

    private static Grid Fixture() => Grid.FromMapText(SampleMaps.CornerCutTrap);

    private static GridLayout LayoutFor(Grid grid) => GridLayout.Fit(grid, 1000, 1000 - StatusHeight);

    private static (ViewerApp App, RecordingRenderer Renderer, Grid Grid) Run(params ScriptedFrame[] frames)
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid));
        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);
        return (app, renderer, grid);
    }

    [Fact]
    public void TheFirstFrameDrawsTerrainAndBothMarkers()
    {
        var (app, renderer, _) = Run(new ScriptedFrame());

        Assert.Equal(1, renderer.FrameCount);
        Assert.Single(renderer.OfKind<DrawCommand.Terrain>());

        var circles = renderer.LastFrameOfKind<DrawCommand.Circle>().ToList();
        Assert.Contains(circles, c => c.Color == RgbaColor.Green);
        Assert.Contains(circles, c => c.Color == RgbaColor.Red);

        // The terrain fills the map area exactly, and does not extend into the
        // strip the host reserves for its status text.
        var terrain = renderer.OfKind<DrawCommand.Terrain>().Single();
        Assert.Equal(new RectF(0, 0, app.Layout.PixelWidth, app.Layout.PixelHeight), terrain.Destination);
    }

    [Fact]
    public void ADefaultSessionSolvesTheFixture()
    {
        var (app, _, _) = Run(new ScriptedFrame());

        Assert.True(app.Result.Found);
        Assert.Equal(9.0 + (2.0 * Math.Sqrt(2.0)), app.Result.Cost, 1e-6);
        Assert.Equal(11, app.Result.StepCount);
    }

    [Fact]
    public void LeftClickMovesTheStartToThePickedCell()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var target = layout.CenterOf(3, 5);

        var (app, renderer, _) = Run(new ScriptedFrame(Mouse: target, ButtonsDown: MouseButtons.Left));

        Assert.Equal(grid.Index(3, 5), app.Start);

        var green = renderer.LastFrameOfKind<DrawCommand.Circle>().Single(c => c.Color == RgbaColor.Green);
        Assert.Equal(layout.CenterOf(3, 5).X, green.Center.X, Tolerance);
        Assert.Equal(layout.CenterOf(3, 5).Y, green.Center.Y, Tolerance);
    }

    [Fact]
    public void CellCentresArePinnedToArithmeticAndNotToGridLayoutItself()
    {
        // The click tests above assert that the marker lands where GridLayout
        // says it should -- which both sides compute with the SAME method, so a
        // wrong half-cell offset moves both and passes. This pins the numbers
        // independently: the 12x7 fixture in a 1000x974 budget gives a cell size
        // of min(1000/12, 974/7) = 83, so a cell centre is (index + 0.5) * 83.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        Assert.Equal(83, layout.CellSize);

        var somewhereInsideCell = new Vector2((3 * 83) + 40, (5 * 83) + 40);
        var (app, renderer, _) = Run(new ScriptedFrame(Mouse: somewhereInsideCell, ButtonsDown: MouseButtons.Left));

        Assert.Equal(grid.Index(3, 5), app.Start);

        var green = renderer.LastFrameOfKind<DrawCommand.Circle>().Single(c => c.Color == RgbaColor.Green);
        Assert.Equal(3.5f * 83f, green.Center.X, Tolerance);
        Assert.Equal(5.5f * 83f, green.Center.Y, Tolerance);
    }

    [Fact]
    public void RightClickMovesTheGoalToThePickedCell()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);

        var (app, renderer, _) = Run(new ScriptedFrame(Mouse: layout.CenterOf(1, 5), ButtonsDown: MouseButtons.Right));

        Assert.Equal(grid.Index(1, 5), app.Goal);

        var red = renderer.LastFrameOfKind<DrawCommand.Circle>().Single(c => c.Color == RgbaColor.Red);
        Assert.Equal(layout.CenterOf(1, 5).X, red.Center.X, Tolerance);
    }

    [Fact]
    public void AClickOnAWallStillMovesTheMarkerAndReportsNoPath()
    {
        // (0,0) is a wall on this fixture. Picking succeeds -- it is on the map --
        // and the search then honestly reports that nothing connects.
        var grid = Fixture();
        var layout = LayoutFor(grid);

        var (app, renderer, _) = Run(new ScriptedFrame(Mouse: layout.CenterOf(0, 0), ButtonsDown: MouseButtons.Left));

        Assert.Equal(grid.Index(0, 0), app.Start);
        Assert.False(app.Result.Found);
        Assert.Empty(renderer.LastFrameOfKind<DrawCommand.Line>());
        Assert.Contains("no path", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void AClickBelowTheMapChangesNothing()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var inStatusStrip = new Vector2(10, layout.PixelHeight + 5);

        var app = new ViewerApp(grid, layout);
        var before = app.Result;

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: inStatusStrip, ButtonsDown: MouseButtons.Left)],
            new RecordingRenderer());
        host.Run(app);

        // Same reference, not merely an equal cost: nothing recomputed at all.
        Assert.Same(before, app.Result);
    }

    [Fact]
    public void SpaceHeldForTenFramesTogglesExactlyOnce()
    {
        // The auto-repeat regression, driven through the real InputAccumulator.
        // A host translating every key event into an edge would flip run/pause
        // on every one of these frames and end where it started.
        var frames = Enumerable.Repeat(new ScriptedFrame(KeysDown: ViewerKeys.Space), 10).ToArray();

        var (app, _, _) = Run(frames);

        Assert.True(app.Running);
    }

    [Fact]
    public void ReleasingAndPressingAgainTogglesASecondTime()
    {
        ScriptedFrame[] frames =
        [
            new(KeysDown: ViewerKeys.Space),
            new(KeysDown: ViewerKeys.Space),
            new(),                              // released
            new(KeysDown: ViewerKeys.Space),
        ];

        var (app, _, _) = Run(frames);

        Assert.False(app.Running);
    }

    [Fact]
    public void WhileNotRunningTheWalkerDoesNotMove()
    {
        var (app, _, grid) = Run(ScriptedHost.Idle(100));

        Assert.NotNull(app.Walker);
        Assert.Equal(grid.ColumnOf(app.Start), app.Walker!.X, Tolerance);
        Assert.Equal(grid.RowOf(app.Start), app.Walker.Y, Tolerance);
        Assert.Equal(0.0, app.Walker.Elapsed);
    }

    [Fact]
    public void WhileRunningTheWalkerMatchesADirectlyDrivenOne()
    {
        const int steps = 90;

        // The trigger frame is zero-length on purpose. Update handles input and
        // THEN advances the clock, so a normal-length frame here would advance
        // the walk once before the counted frames begin -- which is correct
        // behaviour and an off-by-one in the oracle.
        var frames = new List<ScriptedFrame> { new(Dt: 0f, KeysDown: ViewerKeys.Space) };
        frames.AddRange(ScriptedHost.Idle(steps));

        var (app, _, grid) = Run([.. frames]);

        // The oracle: the same path walked by Nav.Core directly, with the same
        // fixed timestep. If these disagree, ViewerApp is doing its own timing.
        var expected = new Walker(app.Result.Cells, grid.Width, 4.0);
        var clock = new FixedTimestep();
        for (var i = 0; i < steps; i++)
        {
            var due = clock.Accumulate(1.0f / 60.0f);
            for (var s = 0; s < due; s++)
            {
                expected.Advance(clock.Step);
            }
        }

        Assert.True(app.Running);
        Assert.Equal(expected.X, app.Walker!.X, Tolerance);
        Assert.Equal(expected.Y, app.Walker.Y, Tolerance);
        Assert.True(app.Walker.Elapsed > 0.0, "the walker should have advanced");
    }

    [Fact]
    public void RResetsTheWalkerAndStopsIt()
    {
        var frames = new List<ScriptedFrame> { new(KeysDown: ViewerKeys.Space) };
        frames.AddRange(ScriptedHost.Idle(60));
        frames.Add(new ScriptedFrame(KeysDown: ViewerKeys.R));

        var (app, _, grid) = Run([.. frames]);

        Assert.False(app.Running);
        Assert.Equal(0.0, app.Walker!.Elapsed);
        Assert.Equal(grid.ColumnOf(app.Start), app.Walker.X, Tolerance);
    }

    [Fact]
    public void ALongFrameIsClampedToTheStepCap()
    {
        // Half a second at 1/60 would be 30 steps. FixedTimestep caps it at 8,
        // and that circuit breaker is the difference between a stall producing a
        // jump and a stall producing a freeze.
        var (app, _, grid) = Run(
            new ScriptedFrame(Dt: 0f, KeysDown: ViewerKeys.Space),
            new ScriptedFrame(Dt: 0.5f));

        var capped = new Walker(app.Result.Cells, grid.Width, 4.0);
        capped.Advance(8 * (1.0 / 60.0));

        Assert.Equal(capped.Elapsed, app.Walker!.Elapsed, 1e-9);
    }

    [Fact]
    public void ThePathIsDrawnAsOneSegmentPerStep()
    {
        var (app, renderer, _) = Run(new ScriptedFrame());

        Assert.Equal(app.Result.StepCount, renderer.LastFrameOfKind<DrawCommand.Line>().Count());
        Assert.All(renderer.LastFrameOfKind<DrawCommand.Line>(), line => Assert.Equal(RgbaColor.SkyBlue, line.Color));
    }

    [Fact]
    public void TheStatusLineCarriesDimensionsCostAndFocus()
    {
        var (app, _, _) = Run(new ScriptedFrame());

        Assert.Contains("12x7", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("steps 11", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("expanded 21", app.StatusText, StringComparison.Ordinal);
        Assert.Contains("[paused]", app.StatusText, StringComparison.Ordinal);

        // Invariant culture: on a comma-decimal machine an uninvariant format
        // would render 11,82843 here and nothing would notice.
        Assert.Contains("11.82843", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusLineFollowsTheRunState()
    {
        var (app, _, _) = Run(new ScriptedFrame(KeysDown: ViewerKeys.Space));

        Assert.Contains("[running]", app.StatusText, StringComparison.Ordinal);
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
}
