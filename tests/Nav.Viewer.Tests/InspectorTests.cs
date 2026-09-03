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
/// <para>
/// Most of the rows are <c>MovementSystem.DebugFor</c>'s own, so their wording is
/// pinned in <c>DebugViewTests</c> rather than here. What this file is about is
/// the WIRING: that the panel describes the unit being watched, that the viewer's
/// own facts sit in their own group beside them, and that a host can still print a
/// heading by watching the group change.
/// </para>
/// </remarks>
public sealed class InspectorTests
{
    private const int StatusHeight = 26;
    private const int Squad = 4;

    private static Grid Fixture() => Grid.FromMapText(SampleMaps.CornerCutTrap);

    private static GridLayout LayoutFor(Grid grid) => GridLayout.Fit(grid, 1000, 1000 - StatusHeight);

    private static string Value(IReadOnlyList<DebugRow> rows, string group, string key) =>
        rows.Single(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal)).Value;

    private static bool HasKey(IReadOnlyList<DebugRow> rows, string group, string key) =>
        rows.Any(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal));

    /// <summary>The headings a host would print, in the order it would print them.</summary>
    private static List<string> GroupRuns(IReadOnlyList<DebugRow> rows)
    {
        var runs = new List<string>();
        foreach (var row in rows)
        {
            if (runs.Count == 0 || !string.Equals(runs[^1], row.Group, StringComparison.Ordinal))
            {
                runs.Add(row.Group);
            }
        }

        return runs;
    }

    [Fact]
    public void TheWatchedUnitIsSpelledOut()
    {
        // Unit 0 is selected from the start, standing on (1,1) with no order.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);
        var rows = app.Inspector;

        Assert.Equal("0", Value(rows, "Unit", "id"));
        Assert.Equal("0", Value(rows, "Unit", "side"));

        // Cells read col,row, with the flat index after it. Nobody reads a map in
        // flat indices, and nobody debugs one without them.
        Assert.Equal($"1,1 (#{grid.Index(1, 1)})", Value(rows, "Unit", "cell"));
        Assert.Equal($"1,1 (#{grid.Index(1, 1)})", Value(rows, "Unit", "goal"));

        // Its goal is its own cell, because nobody has ordered it anywhere --
        // and standing on your goal is what arrived means.
        Assert.Equal("yes", Value(rows, "Unit", "arrived"));

        // No errand row at all rather than a row reading "-": an absent fact is
        // reported by its absence here, the way an absent plan and an absent
        // formation are.
        Assert.False(HasKey(rows, "Unit", "errand"), "a unit on no errand still reported one");

        Assert.Equal("no", Value(rows, "Progress", "searching"));
        Assert.Equal("no", Value(rows, "Progress", "stalled"));

        // Nothing has been ordered anywhere, so there is no route and no
        // formation to describe -- and saying so beats leaving the group off and
        // letting the panel look like it failed to read one.
        Assert.StartsWith("none", Value(rows, "Plan", "plan"), StringComparison.Ordinal);
        Assert.StartsWith("none", Value(rows, "Formation", "formation"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchedUnitCarriesTheMovementFactsTheOldPanelCouldNotReach()
    {
        // THE REASON THE PANEL WAS REWIRED. Neither the parking slot nor the
        // retry gate is on AgentState, so a panel hand-built from the per-tick
        // snapshot could not have shown either at any price -- and between them
        // they are most of the answer to "why is that unit not moving".
        //
        // Watched unit 2, not 0, so a panel that described the first agent
        // whatever was selected fails here rather than passing by coincidence.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);
        app.Session.Select([2]);

        using var host = new ScriptedHost([new ScriptedFrame(Dt: 0f)], new RecordingRenderer());
        host.Run(app);

        Assert.Equal("2", Value(app.Inspector, "Unit", "id"));

        // Never ordered, so it holds the cell it stands on -- and the row names
        // THAT cell, which is (3,1) for unit 2 and nobody else's. A row wired to
        // the wrong agent, or reporting a constant, cannot produce this.
        Assert.Equal(
            $"held: 3,1 (#{grid.Index(3, 1)})", Value(app.Inspector, "Progress", "slot"));
        Assert.StartsWith("open", Value(app.Inspector, "Progress", "retry gate"), StringComparison.Ordinal);

        // Now put it in a formation. A group member starts WITHOUT a slot and
        // claims one on approach, so the same row flips -- and that flip is the
        // whole answer to "why is that unit still walking when the formation
        // looks settled", which no AgentState field could ever have given.
        app.Session.Select([0, 1, 2, 3]);
        app.Session.OrderSelection(grid.Index(10, 5));
        app.Session.Select([2]);

        using var ordered = new ScriptedHost([new ScriptedFrame(Dt: 0f)], new RecordingRenderer());
        ordered.Run(app);

        Assert.Equal("2", Value(app.Inspector, "Unit", "id"));
        Assert.StartsWith("none", Value(app.Inspector, "Progress", "slot"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheViewerGroupCarriesTheFactsOnlyTheViewerKnows()
    {
        // What got DRAWN and what got SELECTED, which the movement layer cannot
        // answer because neither is a fact about the unit. They are in one group
        // of their own so a reader can tell them from the simulation's rows.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        // Standing on its goal: nothing to walk, and nothing missing either.
        Assert.Equal("no", Value(app.Inspector, "Viewer", "no route"));
        Assert.Equal("-", Value(app.Inspector, "Viewer", "waits"));

        // Ordered, and drawn before the clock buys a tick to plan it in: it has
        // somewhere to be and no route there, which is exactly the state the
        // map crosses out.
        using var ordered = new ScriptedHost(
            [new ScriptedFrame(Dt: 0f, Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right)],
            new RecordingRenderer());
        ordered.Run(app);

        Assert.Empty(app.Session.CurrentPlans());
        Assert.StartsWith("yes", Value(app.Inspector, "Viewer", "no route"), StringComparison.Ordinal);

        // Then let it plan. The wait count is every repeated cell in the plan --
        // one tick of standing still apiece -- and the panel carries the whole
        // number so what the map drew can be read against what there was to draw.
        using var planned = new ScriptedHost(
            ScriptedHost.Idle(4, (float)WorldScale.Default.SecondsPerTick), new RecordingRenderer());
        planned.Run(app);

        var plan = app.Session.CurrentPlans().First(p => p.Agent == 0).Plan;
        var waits = Enumerable.Range(1, Math.Max(0, plan.Cells.Count - 1))
            .Count(i => plan.Cells[i - 1] == plan.Cells[i]);

        Assert.True(waits > 0, "the fixture stopped producing a plan with any repeats in it");
        Assert.Equal(waits.ToString(), Value(app.Inspector, "Viewer", "waits"));
        Assert.Equal("no", Value(app.Inspector, "Viewer", "no route"));
    }

    [Fact]
    public void TheRouteRowsSayWhatThePlannerSaid()
    {
        // The plan rows are the movement layer's own wording now, so what is
        // being checked here is the WIRING: that they describe the plan this
        // unit is actually walking rather than some other agent's, or a plan it
        // had two ticks ago.
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

        Assert.Equal($"{plan.Cells.Count}, one per tick", Value(rows, "Plan", "cells"));
        Assert.Equal($"{plan.StartTick}, where the booked route begins", Value(rows, "Plan", "start tick"));
        Assert.StartsWith($"{plan.LastTick},", Value(rows, "Plan", "last tick"), StringComparison.Ordinal);
        Assert.Equal($"{plan.Expanded} nodes", Value(rows, "Plan", "expanded"));
        Assert.StartsWith(
            plan.Found ? "yes" : "no", Value(rows, "Plan", "found"), StringComparison.Ordinal);
        Assert.StartsWith(
            plan.IsPartial ? "yes" : "no", Value(rows, "Plan", "partial"), StringComparison.Ordinal);

        // Where it goes NEXT, not where it has been: the row is cut at the
        // current tick, so it says what is about to happen.
        var next = plan.CellAt(app.CurrentTick + 1);
        Assert.Equal(
            next == app.Agents[0].Cell ? "stands" : $"{grid.ColumnOf(next)},{grid.RowOf(next)} (#{next})",
            Value(rows, "Plan", "next"));
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
        Assert.Equal("0", Value(app.Inspector, "Unit", "id"));
        Assert.Equal("3 also selected", Value(app.Inspector, "Viewer", "others"));
    }

    [Fact]
    public void OneSelectedMentionsNoOthers()
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        Assert.Single(app.Selection);
        Assert.DoesNotContain(app.Inspector, r => string.Equals(r.Key, "others", StringComparison.Ordinal));
    }

    [Fact]
    public void RowsArriveGroupedSoAHostNeedNotSortThem()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        // A host renders a heading by watching the group change, so a group that
        // came back after another one had intervened would print twice. Unit 0
        // has never been ordered here, so it has no formation and therefore no
        // field rows, and the sequence is exact.
        Assert.Equal(
            ["Unit", "Progress", "Plan", "Formation", "Planning", "Viewer"],
            GroupRuns(app.Inspector));

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right), new ScriptedFrame()],
            new RecordingRenderer());
        host.Run(app);

        // Ordered, which adds a formation and the field rows measured against it.
        // No heading may repeat, and the viewer's own group stays LAST -- after
        // everything the movement layer had to say, never interleaved with it.
        var runs = GroupRuns(app.Inspector);
        Assert.Equal(runs.Count, runs.Distinct().Count());
        Assert.Contains("Formation", runs);
        Assert.Equal("Viewer", runs[^1]);
    }
}
