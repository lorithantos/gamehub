using System.Numerics;

using Nav.Core;
using Nav.Core.Interfaces;

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
/// <para>
/// The merge is exercised with <see cref="Source"/>, a source written in this
/// file out of nothing but the interface. That is deliberate: this project
/// references Nav.Viewer.Shared alone, so if testing the merge needed a real
/// tactics world the seam it is built on would not exist.
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

    private static DebugRow Row(IReadOnlyList<DebugRow> rows, string group, string key) =>
        rows.Single(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal));

    private static string Note(IReadOnlyList<DebugRow> rows, string group, string key) =>
        rows.Single(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal)).Note
        ?? throw new InvalidOperationException($"the '{group}/{key}' row carries no note");

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

        Assert.Equal($"{plan.Cells.Count}", Value(rows, "Plan", "cells"));
        Assert.Equal("one per tick", Note(rows, "Plan", "cells"));
        Assert.Equal($"{plan.StartTick}", Value(rows, "Plan", "start tick"));
        Assert.Equal("where the booked route begins", Note(rows, "Plan", "start tick"));
        Assert.Equal($"{plan.LastTick}", Value(rows, "Plan", "last tick"));
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

    [Fact]
    public void NoSourcesIsThePanelExactlyAsItWasBeforeThereWereAny()
    {
        // Both hosts and every other test in this suite hand over none, so this
        // is the case that must not have moved an inch.
        var grid = Fixture();
        var plain = new ViewerApp(grid, LayoutFor(grid), Squad);
        var empty = new ViewerApp(grid, LayoutFor(grid), Squad, sources: []);

        Assert.Equal(plain.Inspector, empty.Inspector);
        Assert.Equal(
            ["Unit", "Progress", "Plan", "Formation", "Planning", "Viewer"],
            GroupRuns(plain.Inspector));

        // Nothing renamed, and no source reported broken, because there was
        // nothing to rename and nothing to break.
        Assert.DoesNotContain(plain.Inspector, r => r.Group.Contains('('));
        Assert.Equal(
            ["waits", "no route"],
            plain.Inspector.Where(r => string.Equals(r.Group, "Viewer", StringComparison.Ordinal))
                           .Select(r => r.Key));
    }

    [Fact]
    public void ASourceLandsAfterTheMovementLayerAndBeforeTheViewersOwnGroup()
    {
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, sources: [new Source("Fight", 0)]);

        // The unit's rows first and the source's own rows after them, as one
        // block, between what the movement layer said and what the viewer says.
        Assert.Equal(
            ["Unit", "Progress", "Plan", "Formation", "Planning", "Fight", "Fight world", "Viewer"],
            GroupRuns(app.Inspector));

        Assert.Equal("0 by Fight", Value(app.Inspector, "Fight", "watched"));
        Assert.Equal("Fight", Value(app.Inspector, "Fight world", "source"));

        // And the movement layer's own rows are exactly what they were.
        Assert.Equal("0", Value(app.Inspector, "Unit", "id"));
        Assert.Equal("no", Value(app.Inspector, "Progress", "searching"));
        Assert.Equal("no", Value(app.Inspector, "Viewer", "no route"));
    }

    [Fact]
    public void TwoSourcesArriveInTheOrderTheyWereHandedOver()
    {
        // Supply order, not name order and not whichever answered first: the
        // composer decided, and a panel that reshuffled between frames would be
        // unreadable however it sorted.
        var grid = Fixture();
        var forward = new ViewerApp(
            grid, LayoutFor(grid), Squad, sources: [new Source("Fight", 0), new Source("Supply", 0)]);
        var backward = new ViewerApp(
            grid, LayoutFor(grid), Squad, sources: [new Source("Supply", 0), new Source("Fight", 0)]);

        Assert.Equal(
            ["Unit", "Progress", "Plan", "Formation", "Planning",
             "Fight", "Fight world", "Supply", "Supply world", "Viewer"],
            GroupRuns(forward.Inspector));

        Assert.Equal(
            ["Unit", "Progress", "Plan", "Formation", "Planning",
             "Supply", "Supply world", "Fight", "Fight world", "Viewer"],
            GroupRuns(backward.Inspector));

        Assert.Equal("0 by Supply", Value(forward.Inspector, "Supply", "watched"));
    }

    [Fact]
    public void AGroupNameThePanelAlreadyUsesIsRenamedRatherThanMergedIntoIt()
    {
        // Two sources both calling their group "Unit", which the movement layer
        // already uses. Interleaved they would read as the movement layer's own
        // answers about the unit, which is the worst thing this panel could say.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, sources:
        [
            new Source("Fight", 0) { UnitGroup = "Unit", WorldGroup = "Viewer" },
            new Source("Supply", 0) { UnitGroup = "Unit", WorldGroup = "Progress" },
        ]);

        var runs = GroupRuns(app.Inspector);
        Assert.Equal(runs.Count, runs.Distinct().Count());

        // The movement layer keeps its headings and its rows, and neither source
        // is inside them.
        Assert.Equal("0", Value(app.Inspector, "Unit", "id"));
        Assert.Equal("no", Value(app.Inspector, "Progress", "searching"));
        Assert.False(HasKey(app.Inspector, "Unit", "watched"), "a source landed in the movement layer's group");

        // Numbered in the order they were handed over, and nothing is lost.
        Assert.Equal("0 by Fight", Value(app.Inspector, "Unit (2)", "watched"));
        Assert.Equal("0 by Supply", Value(app.Inspector, "Unit (3)", "watched"));
        Assert.Equal("Fight", Value(app.Inspector, "Viewer (2)", "source"));
        Assert.Equal("Supply", Value(app.Inspector, "Progress (2)", "source"));

        // Viewer is reserved before a source is asked anything, so the viewer's
        // own group is still called Viewer and is still last.
        Assert.Equal("Viewer", runs[^1]);
        Assert.Equal("no", Value(app.Inspector, "Viewer", "no route"));
    }

    [Fact]
    public void ASourceThatThrowsLosesItsBlockAndSaysSoWithoutTakingThePanelDown()
    {
        // All three places a source can throw: handing out the unit's view,
        // describing the unit, and describing itself.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, sources:
        [
            new Source("Early", 0) { Fails = Fault.OnDebugFor },
            new Source("Late", 0) { Fails = Fault.OnUnitRows },
            new Source("Own", 0) { Fails = Fault.OnWorldRows },
            new Source("Fine", 0),
        ]);

        // The unit is still described and the source that works is still merged.
        Assert.Equal("0", Value(app.Inspector, "Unit", "id"));
        Assert.Equal("0 by Fine", Value(app.Inspector, "Fine", "watched"));

        // Nothing of a broken source survives -- not even the half of the block
        // that "Own" managed to build before it threw.
        Assert.DoesNotContain(app.Inspector, r => r.Group.StartsWith("Early", StringComparison.Ordinal));
        Assert.DoesNotContain(app.Inspector, r => r.Group.StartsWith("Late", StringComparison.Ordinal));
        Assert.DoesNotContain(app.Inspector, r => r.Group.StartsWith("Own", StringComparison.Ordinal));

        // Said out loud, counted from one in the order they were handed over, and
        // AFTER the viewer's own rows -- a source that breaks may not move a row
        // that works.
        Assert.Equal(
            ["waits", "no route", "source 1", "source 2", "source 3"],
            app.Inspector.Where(r => string.Equals(r.Group, "Viewer", StringComparison.Ordinal))
                         .Select(r => r.Key));

        // The type is the value and the message is the note: an exception message
        // is somebody else's arbitrary-length string, and the panel column cannot
        // be sized for one.
        Assert.Equal("threw InvalidOperationException", Value(app.Inspector, "Viewer", "source 1"));
        Assert.Equal("Early will not answer for unit 0", Note(app.Inspector, "Viewer", "source 1"));
        Assert.Equal("threw InvalidOperationException", Value(app.Inspector, "Viewer", "source 2"));
        Assert.Equal("Late cannot read unit 0", Note(app.Inspector, "Viewer", "source 2"));
        Assert.Equal("threw InvalidOperationException", Value(app.Inspector, "Viewer", "source 3"));
        Assert.Equal("Own cannot read itself", Note(app.Inspector, "Viewer", "source 3"));
    }

    [Fact]
    public void ASourceThatNeverHeardOfTheWatchedUnitPrintsNoHeadingForIt()
    {
        // The contract says any id is answered, so "never heard of it" comes back
        // as no rows rather than as a throw -- and no rows must mean no heading,
        // not an empty one a host would print a title over.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, sources: [new Source("Fight", 7)]);

        Assert.DoesNotContain(
            app.Inspector, r => string.Equals(r.Group, "Fight", StringComparison.Ordinal));

        // What it says about ITSELF is still worth showing: the setup does not
        // depend on who is being watched.
        Assert.Equal("Fight", Value(app.Inspector, "Fight world", "source"));
        Assert.Equal("0", Value(app.Inspector, "Unit", "id"));
    }

    [Fact]
    public void ANullSourceIsRefusedWhereTheApplicationIsComposed()
    {
        // A hole in the list is an unfinished wiring job, not a running world
        // behaving badly, and it is caught at the seam rather than survived on
        // every frame afterwards.
        var grid = Fixture();
        var layout = LayoutFor(grid);

        var refused = Assert.Throws<ArgumentException>(
            () => new ViewerApp(grid, layout, Squad, sources: [new Source("Fight", 0), null!]));

        Assert.Equal("sources", refused.ParamName);
        Assert.StartsWith("source 2 of 2 is null", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFactIsTheValueAndTheSentenceAboutItIsTheNote()
    {
        // WHAT THE SPLIT IS FOR. These rows used to read "yes -- in the world and
        // holding its cell" in a 260px column with no wrapping and no trimming,
        // so what a reader actually saw was "yes -- in the wo". The fact is now
        // short enough to line up and the sentence is somewhere a panel can put
        // it on demand.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        Assert.Equal("yes", Value(app.Inspector, "Unit", "alive"));
        Assert.Equal("in the world and holding its cell", Note(app.Inspector, "Unit", "alive"));

        Assert.Equal("no", Value(app.Inspector, "Progress", "follows"));
        Assert.Equal("it plans its own route", Note(app.Inspector, "Progress", "follows"));

        Assert.Equal("open", Value(app.Inspector, "Progress", "retry gate"));
        Assert.Equal("it may start a search this tick", Note(app.Inspector, "Progress", "retry gate"));

        Assert.Equal("none", Value(app.Inspector, "Plan", "plan"));
        Assert.Equal("it is standing where it is", Note(app.Inspector, "Plan", "plan"));

        Assert.Equal("none", Value(app.Inspector, "Formation", "formation"));
        Assert.Equal("it has never been ordered", Note(app.Inspector, "Formation", "formation"));

        // A FACT THAT SPEAKS FOR ITSELF CARRIES NO NOTE. Repeating the value into
        // the note would give every row a tooltip, most of them saying nothing.
        Assert.Null(Row(app.Inspector, "Unit", "id").Note);
        Assert.Null(Row(app.Inspector, "Unit", "cell").Note);
        Assert.Null(Row(app.Inspector, "Unit", "arrived").Note);
        Assert.Null(Row(app.Inspector, "Progress", "stalled").Note);

        // The route is the longest string the panel carries and the count is one
        // of its shortest, so the count is the value and the walk is the note.
        using var walking = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right),
             .. ScriptedHost.Idle(4, (float)WorldScale.Default.SecondsPerTick)],
            new RecordingRenderer());
        walking.Run(app);

        var remaining = Value(app.Inspector, "Plan", "remaining");
        Assert.EndsWith(" cells", remaining, StringComparison.Ordinal);
        Assert.StartsWith(
            "from this tick on: ", Note(app.Inspector, "Plan", "remaining"), StringComparison.Ordinal);
    }

    [Fact]
    public void NoValueTheAppCanProduceStillCarriesItsGlossWeldedOn()
    {
        // THE INVARIANT THAT KEEPS THIS FROM COMING BACK. A producer that goes on
        // writing "yes -- because" into Value is a row the panel clips again, and
        // the failure is invisible until somebody looks at a running window. So
        // every row the app can produce is swept rather than a chosen few.
        //
        // Notes are NOT swept for the dash: a note is prose and a route note is
        // full of arrows and dashes of its own.
        var seen = 0;
        var noted = 0;

        foreach (var (state, rows) in EveryPanel())
        {
            foreach (var row in rows)
            {
                seen++;
                if (row.Note is not null)
                {
                    noted++;
                }

                Assert.DoesNotContain(" -- ", row.Value, StringComparison.Ordinal);
            }
        }

        // The sweep has to have swept something, and the split has to have moved
        // something -- otherwise this passes on an empty panel.
        Assert.True(seen > 100, $"the sweep only saw {seen} rows");
        Assert.True(noted > 20, $"only {noted} of {seen} rows carried a note");
    }

    /// <summary>The panel in every state this project can arrange for it.</summary>
    /// <remarks>
    /// Not exhaustive over the movement layer's own branches -- those are swept
    /// where they are written, in <c>DebugViewTests</c>. What is here is every
    /// shape the VIEWER puts a panel into: nobody watched, one watched, a squad
    /// boxed, a unit walking a booked route inside a formation, sources that work
    /// and a source that throws.
    /// </remarks>
    private static IEnumerable<(string State, IReadOnlyList<DebugRow> Rows)> EveryPanel()
    {
        var grid = Fixture();
        var layout = LayoutFor(grid);

        var idle = new ViewerApp(grid, layout, Squad);
        yield return ("idle", idle.Inspector);

        var ordered = new ViewerApp(grid, layout, Squad);
        ordered.Session.Select([0, 1, 2, 3]);
        ordered.Session.OrderSelection(grid.Index(10, 5));
        yield return ("just ordered", ordered.Inspector);

        using (var walking = new ScriptedHost(
            ScriptedHost.Idle(6, (float)WorldScale.Default.SecondsPerTick), new RecordingRenderer()))
        {
            walking.Run(ordered);
        }

        yield return ("walking as a squad", ordered.Inspector);

        ordered.Session.Select([2]);
        yield return ("one member of a walking squad", ordered.Inspector);

        var sourced = new ViewerApp(
            grid, layout, Squad, sources: [new Source("Fight", 0), new Source("Supply", 0)]);
        yield return ("two sources", sourced.Inspector);

        var broken = new ViewerApp(
            grid,
            layout,
            Squad,
            sources:
            [
                new Source("Early", 0) { Fails = Fault.OnDebugFor },
                new Source("Late", 0) { Fails = Fault.OnUnitRows },
                new Source("Own", 0) { Fails = Fault.OnWorldRows },
            ]);
        yield return ("three broken sources", broken.Inspector);
    }

    /// <summary>Where a broken source breaks.</summary>
    private enum Fault
    {
        /// <summary>Nowhere: it answers everything asked of it.</summary>
        Never,

        /// <summary>On being asked for a unit's view at all.</summary>
        OnDebugFor,

        /// <summary>On the unit's view being read.</summary>
        OnUnitRows,

        /// <summary>On being asked to describe itself.</summary>
        OnWorldRows,
    }

    /// <summary>
    /// A source with no world behind it: a name, a row per unit it has heard of,
    /// a row about itself, and a way to throw on demand.
    /// </summary>
    /// <remarks>
    /// THE WHOLE OF WHAT A SOURCE HAS TO BE. Written here rather than borrowed
    /// from the tactics side, because a merge that could only be exercised by a
    /// real world would mean the viewer had learned what a world is.
    /// </remarks>
    private sealed class Source : IWorldDebugView
    {
        private readonly int[] _knows;

        public Source(string name, params int[] knows)
        {
            Name = name;
            _knows = knows;
            UnitGroup = name;
            WorldGroup = $"{name} world";
        }

        public string Name { get; }

        public string UnitGroup { get; init; }

        public string WorldGroup { get; init; }

        public Fault Fails { get; init; }

        public IReadOnlyList<DebugRow> Describe()
        {
            if (Fails == Fault.OnWorldRows)
            {
                throw new InvalidOperationException($"{Name} cannot read itself");
            }

            return [new DebugRow(WorldGroup, "source", Name)];
        }

        public IDebugView DebugFor(int agent)
        {
            if (Fails == Fault.OnDebugFor)
            {
                throw new InvalidOperationException($"{Name} will not answer for unit {agent}");
            }

            return new UnitRows(this, agent);
        }

        private sealed class UnitRows(Source source, int agent) : IDebugView
        {
            public IReadOnlyList<DebugRow> Describe()
            {
                if (source.Fails == Fault.OnUnitRows)
                {
                    throw new InvalidOperationException($"{source.Name} cannot read unit {agent}");
                }

                return source._knows.Contains(agent)
                    ? [new DebugRow(source.UnitGroup, "watched", $"{agent} by {source.Name}"),
                       new DebugRow(source.UnitGroup, "known", "yes")]
                    : [];
            }
        }
    }
}
