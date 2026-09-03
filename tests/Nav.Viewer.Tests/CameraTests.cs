using System.Numerics;

using Nav.Core;

using Xunit.Abstractions;

namespace Nav.Viewer.Tests;

/// <summary>
/// The viewport: what makes a map bigger than its window watchable.
/// </summary>
/// <remarks>
/// The viewer never failed at scale, which is what made this worth building. It
/// floors at one pixel per cell, so a 512x512 map renders — and every mark drawn
/// is sized from the cell, so units, ids, health bars and routes all collapse
/// into that pixel. The map is visible and the simulation is not.
/// <para>
/// That matters more than a rendering nicety: reading a replay tick by tick is
/// how the largest movement win so far was found, so an instrument that goes
/// dark at the scale the work is moving to costs more than it looks like it
/// costs. See <c>docs/viewer.md</c>.
/// </para>
/// </remarks>
public sealed class CameraTests(ITestOutputHelper output)
{
    private const int StatusHeight = 26;

    private static Grid Big() => MapGenerator.Generate(512, 512, seed: 5).Grid;

    private static Grid Small() => Grid.FromMapText(SampleMaps.CornerCutTrap);

    [Fact]
    public void FittingABigMapIsWhyThisExists()
    {
        // Not a failure to fix -- a true statement about the old behaviour, kept
        // as the reason the rest of this file is here.
        var layout = GridLayout.Fit(Big(), 1000, 1000 - StatusHeight);

        output.WriteLine($"512x512 fitted: {layout.CellSize} px per cell, window {layout.PixelWidth}x{layout.PixelHeight}");
        Assert.Equal(1, layout.CellSize);
    }

    [Fact]
    public void UnzoomedItIsExactlyWhatFitGave()
    {
        // The compatibility claim the whole change rests on: the viewer computes
        // its layout through Viewing always, so this has to reproduce Fit to the
        // pixel or every existing test is measuring something new.
        foreach (var grid in new[] { Small(), Big() })
        {
            var fit = GridLayout.Fit(grid, 1000, 1000 - StatusHeight);
            var viewing = GridLayout.Viewing(
                grid, fit.CellSize, fit.PixelWidth, fit.PixelHeight, grid.Index(grid.Width / 2, grid.Height / 2));

            Assert.Equal(fit.CellSize, viewing.CellSize);
            Assert.Equal(fit.PixelWidth, viewing.PixelWidth);
            Assert.Equal(fit.PixelHeight, viewing.PixelHeight);
            Assert.Equal(0, viewing.OriginX);
            Assert.Equal(0, viewing.OriginY);
        }
    }

    [Fact]
    public void ZoomingInMakesTheSimulationLegible()
    {
        var grid = Big();
        var fit = GridLayout.Fit(grid, 1000, 1000 - StatusHeight);
        var zoomed = GridLayout.Viewing(grid, 16, fit.PixelWidth, fit.PixelHeight, grid.Index(256, 256));

        var acrossX = zoomed.PixelWidth / zoomed.CellSize;
        var acrossY = zoomed.PixelHeight / zoomed.CellSize;
        output.WriteLine($"at 16 px per cell the window holds {acrossX}x{acrossY} cells of a 512x512 map");

        Assert.Equal(16, zoomed.CellSize);
        Assert.True(acrossX is > 8 and < 512, $"{acrossX} cells across is not a useful view");

        // Scrolled, not centred: the map is far bigger than the window.
        Assert.True(zoomed.OriginX < 0 && zoomed.OriginY < 0);
    }

    [Fact]
    public void AFocusNearAnEdgeDoesNotScrollPastIt()
    {
        // So a caller may focus anything without knowing how clamping works --
        // including a corner, which naively centred would show a window half
        // full of void.
        var grid = Big();
        var corner = GridLayout.Viewing(grid, 16, 800, 800, grid.Index(0, 0));
        var far = GridLayout.Viewing(grid, 16, 800, 800, grid.Index(511, 511));

        Assert.Equal(0, corner.OriginX);
        Assert.Equal(0, corner.OriginY);
        Assert.Equal(800 - (512 * 16), far.OriginX);
        Assert.Equal(800 - (512 * 16), far.OriginY);
    }

    [Fact]
    public void ASmallMapIsCentredRatherThanCornered()
    {
        var grid = Small();
        var layout = GridLayout.Viewing(grid, 4, 600, 400, grid.Index(0, 0));

        Assert.Equal((600 - (grid.Width * 4)) / 2, layout.OriginX);
        Assert.Equal((400 - (grid.Height * 4)) / 2, layout.OriginY);
    }

    [Fact]
    public void DrawingAndPickingStayAgreedWhenScrolled()
    {
        // The reason the transform lives in one type. Drawing goes one way and
        // picking goes the other, and an origin applied to one and not the other
        // is how they end up a screenful apart rather than half a cell.
        var grid = Big();
        var layout = GridLayout.Viewing(grid, 12, 900, 700, grid.Index(300, 200));

        Assert.True(layout.OriginX < 0, "the test is not exercising a scrolled view");

        foreach (var (x, y) in new[] { (300, 200), (296, 198), (305, 204) })
        {
            var screen = layout.CenterOf(x, y);
            Assert.True(layout.TryPick(screen, grid, out var picked), $"({x},{y}) drew outside the window");
            Assert.Equal(grid.Index(x, y), picked);
        }
    }

    [Fact]
    public void PickingRejectsTheMarginAroundASmallMap()
    {
        // Inside the window but outside the map. Answering "the nearest cell"
        // would let a click on the margin order units as though the map went on.
        var grid = Small();
        var layout = GridLayout.Viewing(grid, 4, 600, 400, grid.Index(0, 0));

        Assert.False(layout.TryPick(new Vector2(2, 2), grid, out _));
        Assert.False(layout.TryPick(new Vector2(598, 398), grid, out _));
        Assert.True(layout.TryPick(layout.CenterOf(1, 1), grid, out _));
    }

    [Fact]
    public void TheViewerZoomsPansAndComesBack()
    {
        var grid = Big();
        var app = new ViewerApp(grid, GridLayout.Fit(grid, 1000, 1000 - StatusHeight), squad: 4);
        var renderer = new RecordingRenderer();

        var fitted = app.Layout;
        Assert.Equal(1, fitted.CellSize);

        Play(app, renderer, ViewerKeys.ZoomIn, ViewerKeys.ZoomIn, ViewerKeys.ZoomIn);
        var zoomed = app.Layout;
        output.WriteLine($"after three zooms: {zoomed.CellSize} px per cell");
        Assert.True(zoomed.CellSize > fitted.CellSize, "zooming in did nothing");

        Play(app, renderer, ViewerKeys.PanRight, ViewerKeys.PanDown);
        Assert.NotEqual(zoomed.OriginX, app.Layout.OriginX);

        Play(app, renderer, ViewerKeys.ResetView);
        Assert.Equal(fitted, app.Layout);

        // The window never moved, whatever the camera did -- both hosts size
        // themselves from it and rebuild when it changes.
        Assert.Equal(fitted.PixelWidth, zoomed.PixelWidth);
        Assert.Equal(fitted.PixelHeight, zoomed.PixelHeight);
    }

    [Fact]
    public void ZoomingOutStopsAtTheWholeMap()
    {
        var grid = Small();
        var app = new ViewerApp(grid, GridLayout.Fit(grid, 1000, 1000 - StatusHeight), squad: 4);
        var renderer = new RecordingRenderer();
        var fitted = app.Layout;

        Play(app, renderer, ViewerKeys.ZoomOut, ViewerKeys.ZoomOut, ViewerKeys.ZoomOut);

        Assert.Equal(fitted.CellSize, app.Layout.CellSize);
    }

    /// <summary>Presses each key for one frame, releasing between so edges fire.</summary>
    private static void Play(ViewerApp app, RecordingRenderer renderer, params ViewerKeys[] keys)
    {
        var frames = keys
            .SelectMany(k => new[] { new ScriptedFrame(KeysDown: k), new ScriptedFrame() })
            .ToArray();

        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);
    }
}
