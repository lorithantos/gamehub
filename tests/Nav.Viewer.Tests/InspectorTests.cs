using System.Numerics;

using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// What the viewer says about one unit.
/// </summary>
/// <remarks>
/// Rows are DATA, so nothing here renders anything — the whole point of splitting
/// the inspector off <c>StatusText</c> was that the app owns what is said and each
/// host owns how it looks, and a test that had to draw to read a value would have
/// been the first sign that split did not hold.
/// </remarks>
public sealed class InspectorTests
{
    private const int StatusHeight = 26;
    private const int Squad = 4;

    private static Grid Fixture() => Grid.FromMapText(SampleMaps.CornerCutTrap);

    private static GridLayout LayoutFor(Grid grid) => GridLayout.Fit(grid, 1000, 1000 - StatusHeight);

    private static string Value(IReadOnlyList<InspectorRow> rows, string group, string label) =>
        rows.Single(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Label, label, StringComparison.Ordinal)).Value;

    [Fact]
    public void TheWatchedUnitIsSpelledOut()
    {
        // Unit 0 is selected from the start, standing on (1,1) with no order.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);
        var rows = app.Inspector;

        Assert.Equal("0", Value(rows, "Identity", "id"));
        Assert.Equal("0", Value(rows, "Identity", "side"));
        Assert.Equal("yes", Value(rows, "Identity", "alive"));
        Assert.Equal("no", Value(rows, "Identity", "leader"));

        // Cells read col,row. Nobody reads a map in flat indices.
        Assert.Equal("1,1", Value(rows, "Position", "cell"));
        Assert.Equal("1,1", Value(rows, "Position", "goal"));

        // Its goal is its own cell, because nobody has ordered it anywhere --
        // and standing on your goal is what arrived means.
        Assert.Equal("yes", Value(rows, "Position", "arrived"));
        Assert.Equal("-", Value(rows, "Position", "errand"));

        Assert.Equal("no", Value(rows, "Planning", "thinking"));
        Assert.Equal("0", Value(rows, "Planning", "stalled"));
        Assert.Equal("no", Value(rows, "Planning", "stuck"));

        // Nothing has been ordered anywhere, so there is no route to describe --
        // and saying so beats leaving the group off and letting the panel look
        // like it failed to read one.
        Assert.Equal("none", Value(rows, "Route", "plan"));
    }

    [Fact]
    public void NothingSelectedSaysNothing()
    {
        // Rows 3-5 on the right are open floor with nobody standing on it, and
        // boxing empty ground clears the selection.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        using var host = new ScriptedHost(
            [
                new ScriptedFrame(Mouse: layout.CenterOf(8, 3), ButtonsDown: MouseButtons.Left),
                new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Left),
                new ScriptedFrame(Mouse: layout.CenterOf(10, 5)),
            ],
            new RecordingRenderer());
        host.Run(app);

        Assert.Empty(app.Selection);
        Assert.Empty(app.Inspector);
    }

    [Fact]
    public void SeveralSelectedWatchesTheLowestIdAndCountsTheRest()
    {
        // A boxed group would otherwise be described as though the box had caught
        // one unit, which is the reading that makes a panel worse than no panel.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        using var host = new ScriptedHost(
            [
                new ScriptedFrame(Mouse: layout.CenterOf(1, 1) - new Vector2(10, 10), ButtonsDown: MouseButtons.Left),
                new ScriptedFrame(Mouse: layout.CenterOf(4, 1) + new Vector2(10, 10), ButtonsDown: MouseButtons.Left),
                new ScriptedFrame(Mouse: layout.CenterOf(4, 1) + new Vector2(10, 10)),
            ],
            new RecordingRenderer());
        host.Run(app);

        Assert.Equal(Squad, app.Selection.Count);
        Assert.Equal("0", Value(app.Inspector, "Identity", "id"));
        Assert.Equal("3 also selected", Value(app.Inspector, "Identity", "others"));
    }

    [Fact]
    public void OneSelectedMentionsNoOthers()
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        Assert.Single(app.Selection);
        Assert.DoesNotContain(app.Inspector, r => string.Equals(r.Label, "others", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRouteRowsSayWhatThePlannerSaid()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        var frames = new List<ScriptedFrame>
        {
            new(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right),
        };
        frames.AddRange(ScriptedHost.Idle(4, (float)WorldScale.Default.SecondsPerTick));

        using var host = new ScriptedHost(frames, new RecordingRenderer());
        host.Run(app);

        var plan = app.Session.CurrentPlans().First(p => p.Agent == 0).Plan;
        var rows = app.Inspector;

        Assert.Equal(plan.Cells.Count.ToString(), Value(rows, "Route", "cells"));
        Assert.Equal(plan.Expanded.ToString(), Value(rows, "Route", "expanded"));
        Assert.Equal(plan.Found ? "yes" : "no", Value(rows, "Route", "found"));
        Assert.Equal(plan.IsPartial ? "yes" : "no", Value(rows, "Route", "partial"));
        Assert.Equal(plan.LastTick.ToString(), Value(rows, "Route", "last tick"));

        // Repeated cells: one tick of standing still apiece. The map marks only
        // the ones taken while the unit is not searching, so this is the count
        // to read the drawing against.
        var waits = Enumerable.Range(1, Math.Max(0, plan.Cells.Count - 1))
            .Count(i => plan.Cells[i - 1] == plan.Cells[i]);
        Assert.Equal(waits.ToString(), Value(rows, "Route", "waits"));

        // The next few cells, cut at the current tick: what happens next, not
        // what already happened.
        var next = app.Session.Grid;
        var here = plan.CellAt(app.CurrentTick);
        Assert.StartsWith(
            $"{next.ColumnOf(here)},{next.RowOf(here)}",
            Value(rows, "Route", "next"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RowsArriveGroupedSoAHostNeedNotSortThem()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right), new ScriptedFrame()],
            new RecordingRenderer());
        host.Run(app);

        // A host renders a heading by watching the group change, so a group that
        // came back after another one had intervened would print twice.
        var runs = new List<string>();
        foreach (var row in app.Inspector)
        {
            if (runs.Count == 0 || !string.Equals(runs[^1], row.Group, StringComparison.Ordinal))
            {
                runs.Add(row.Group);
            }
        }

        Assert.Equal(["Identity", "Position", "Planning", "Route"], runs);
    }
}
