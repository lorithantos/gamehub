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

    private const string Movement = InspectorLayout.MovementSection;
    private const string Tactics = InspectorLayout.TacticsSection;
    private const string Viewer = InspectorLayout.ViewerSection;

    /// <summary>
    /// An arrangement, standing in for the one a composition root hands over.
    /// </summary>
    /// <remarks>
    /// <b>WRITTEN HERE BECAUSE THE ARRANGEMENT IS THE COMPOSER'S, NOT THE
    /// VIEWER'S.</b> Nav.Viewer.Shared holds the mechanism and names no group;
    /// the order comes in as data. So a test that wants a panel in a particular
    /// order composes one, exactly as a host does.
    /// <para>
    /// The movement section is named through <see cref="MovementGroups"/>, which
    /// is the layer's own vocabulary and reachable here because Nav.Core is.
    /// The tactics section is named with the fixture <see cref="Source"/>'s own
    /// invented headings -- this project cannot see a real tactics source and
    /// must not learn its words to test the ordering.
    /// </para>
    /// </remarks>
    private static readonly InspectorArrangement Arranged = new(
    [
        (Movement, new[]
        {
            MovementGroups.Agent,
            MovementGroups.Progress,
            MovementGroups.Plan,
            MovementGroups.Formation,
            MovementGroups.Field,
            MovementGroups.Planning,
        }),
        (Tactics, new[] { "Fight", "Fight world", "Ranked" }),
        (Viewer, new[] { InspectorLayout.SourcesGroup, InspectorLayout.ControlsGroup }),
    ]);

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

    /// <summary>
    /// The same runs with the section each was printed under, which is what a
    /// heading actually IS now that the panel has two levels.
    /// </summary>
    private static List<(string Section, string Group)> HeadingRuns(IReadOnlyList<DebugRow> rows)
    {
        var runs = new List<(string Section, string Group)>();
        foreach (var row in rows)
        {
            if (runs.Count == 0 ||
                !string.Equals(runs[^1].Section, row.Section, StringComparison.Ordinal) ||
                !string.Equals(runs[^1].Group, row.Group, StringComparison.Ordinal))
            {
                runs.Add((row.Section, row.Group));
            }
        }

        return runs;
    }

    /// <summary>The sections a host would print, in the order it would print them.</summary>
    private static List<string> SectionRuns(IReadOnlyList<DebugRow> rows)
    {
        var runs = new List<string>();
        foreach (var row in rows)
        {
            if (runs.Count == 0 || !string.Equals(runs[^1], row.Section, StringComparison.Ordinal))
            {
                runs.Add(row.Section);
            }
        }

        return runs;
    }

    private static string ValueIn(IReadOnlyList<DebugRow> rows, string section, string group, string key) =>
        rows.Single(r =>
            string.Equals(r.Section, section, StringComparison.Ordinal) &&
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal)).Value;

    private static bool HasKeyIn(IReadOnlyList<DebugRow> rows, string section, string group, string key) =>
        rows.Any(r =>
            string.Equals(r.Section, section, StringComparison.Ordinal) &&
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal));

    [Fact]
    public void TheWatchedUnitIsSpelledOut()
    {
        // Unit 0 is selected from the start, standing on (1,1) with no order.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);
        var rows = app.Inspector;

        Assert.Equal("0", Value(rows, "Agent", "id"));
        Assert.Equal("0", Value(rows, "Agent", "side"));

        // Cells read col,row, with the flat index after it. Nobody reads a map in
        // flat indices, and nobody debugs one without them.
        Assert.Equal($"1,1 (#{grid.Index(1, 1)})", Value(rows, "Agent", "cell"));
        Assert.Equal($"1,1 (#{grid.Index(1, 1)})", Value(rows, "Agent", "goal"));

        // Its goal is its own cell, because nobody has ordered it anywhere --
        // and standing on your goal is what arrived means.
        Assert.Equal("yes", Value(rows, "Agent", "arrived"));

        // No errand row at all rather than a row reading "-": an absent fact is
        // reported by its absence here, the way an absent plan and an absent
        // formation are.
        Assert.False(HasKey(rows, "Agent", "errand"), "a unit on no errand still reported one");

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

        Assert.Equal("2", Value(app.Inspector, "Agent", "id"));

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

        Assert.Equal("2", Value(app.Inspector, "Agent", "id"));
        Assert.StartsWith("none", Value(app.Inspector, "Progress", "slot"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheViewersOwnGroupCarriesTheFactsOnlyTheViewerKnows()
    {
        // What got DRAWN and what got SELECTED, which the movement layer cannot
        // answer because neither is a fact about the unit. They are in one group
        // of their own so a reader can tell them from the simulation's rows.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad);

        // Standing on its goal: nothing to walk, and nothing missing either.
        Assert.Equal("no", Value(app.Inspector, "Sources", "no route"));
        Assert.Equal("-", Value(app.Inspector, "Sources", "waits"));

        // Ordered, and drawn before the clock buys a tick to plan it in: it has
        // somewhere to be and no route there, which is exactly the state the
        // map crosses out.
        using var ordered = new ScriptedHost(
            [new ScriptedFrame(Dt: 0f, Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right)],
            new RecordingRenderer());
        ordered.Run(app);

        Assert.Empty(app.Session.CurrentPlans());
        Assert.StartsWith("yes", Value(app.Inspector, "Sources", "no route"), StringComparison.Ordinal);

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
        Assert.Equal(waits.ToString(), Value(app.Inspector, "Sources", "waits"));
        Assert.Equal("no", Value(app.Inspector, "Sources", "no route"));
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
    public void NothingSelectedSaysNothingAboutAUnitAndStillShowsTheControls()
    {
        // Rows 3-5 on the right are open floor with nobody standing on it, and
        // boxing empty ground clears the selection.
        //
        // THE CONTROLS SURVIVE IT, and that is the point of them: this is the
        // state the viewer opens in and the state a reader lands back in every
        // time they miss a unit, so a key list that needed a selection to appear
        // would be missing on precisely the occasions it was wanted.
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

        Assert.Equal(["Controls"], GroupRuns(app.Inspector));
        Assert.Equal("pause", Value(app.Inspector, "Controls", "SPACE"));
    }

    [Fact]
    public void EveryKeyTheMapBindsGetsARowAndNoRowIsInventedBesides()
    {
        // THE TEST THAT STOPS THE TWO LISTS DRIFTING. The folder is generated
        // from the keymap, so this compares it against the keymap rather than
        // against a list written here -- a copy of the keys in this file would
        // go stale in exactly the way the folder is built not to.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        Assert.Equal(
            app.Keys.Bindings.Select(b => b.Keycap),
            app.Inspector.Where(r => string.Equals(r.Group, "Controls", StringComparison.Ordinal))
                         .Select(r => r.Key));

        // And the map had something to say, so an empty folder matching an empty
        // map would not pass this.
        Assert.Equal(14, app.Keys.Bindings.Count);
    }

    [Fact]
    public void AKeyReboundMovesInTheFolderAndInTheStatusLineWithNobodyEditingEither()
    {
        // Both readings of the same map, so neither can be left claiming a key
        // that no longer does the thing. Two keys swap jobs here rather than one
        // moving, because a folder that printed the ACTION's name against a
        // fixed keycap would pass a one-way move by coincidence.
        var grid = Fixture();
        var keys = Keymap.Default
            .Rebound(PhysicalKey.Space, ViewerKeys.ResetView)
            .Rebound(PhysicalKey.Home, ViewerKeys.Space);
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, keys: keys);

        Assert.Equal("whole map again", Value(app.Inspector, "Controls", "SPACE"));
        Assert.Equal("pause", Value(app.Inspector, "Controls", "HOME"));

        Assert.Contains("HOME pause", app.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("SPACE pause", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyNothingIsBoundToIsNotInTheFolderUntilSomethingIs()
    {
        // A key ARRIVING is the case the folder has to get right without anyone
        // touching it, and unbinding one and binding it back is the only way
        // this project can arrange that -- every PhysicalKey the viewer has is
        // already bound in the default map.
        var grid = Fixture();

        var unbound = new ViewerApp(
            grid, LayoutFor(grid), Squad,
            keys: Keymap.Default.Rebound(PhysicalKey.Home, ViewerKeys.None));

        Assert.False(
            HasKey(unbound.Inspector, "Controls", "HOME"),
            "a key bound to nothing was still listed as doing something");

        var rebound = new ViewerApp(
            grid, LayoutFor(grid), Squad,
            keys: Keymap.Default
                .Rebound(PhysicalKey.Home, ViewerKeys.None)
                .Rebound(PhysicalKey.Home, ViewerKeys.ResetView));

        Assert.Equal("whole map again", Value(rebound.Inspector, "Controls", "HOME"));
    }

    [Fact]
    public void TheStatusLineAndTheFolderCannotDisagreeAboutWhatAKeyDoes()
    {
        // One vocabulary, asserted from the folder's side: whatever the row
        // says, the hint says, keycap included. A second wording for the same
        // action is how a panel and a status line come to contradict each other
        // about a key, which is worse than either of them saying nothing.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        foreach (var action in new[] { ViewerKeys.Space, ViewerKeys.Step, ViewerKeys.Pace, ViewerKeys.R })
        {
            var keycap = app.Keys.KeycapFor(action);
            var says = Value(app.Inspector, "Controls", keycap);
            Assert.Contains($"{keycap} {says}", app.StatusText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AKeyThatDoesNothingYetSaysSoRatherThanNamingSomethingItCannotDo()
    {
        // The status line leaves these out, and a folder cannot: a list of the
        // controls that quietly omits three of the fourteen is a list the reader
        // stops believing. So they are listed, and what is listed is what
        // pressing them actually gets you today.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad);

        Assert.Equal("route overlay (not wired yet)", Value(app.Inspector, "Controls", "P"));
        Assert.Equal("sight overlay (not wired yet)", Value(app.Inspector, "Controls", "L"));

        // Nobody lent this viewer eyes, so the viewpoint key has nothing to
        // cycle -- the same rule the status line follows by staying quiet.
        Assert.Equal("cycle viewpoint (nothing to cycle here)", Value(app.Inspector, "Controls", "V"));
        Assert.DoesNotContain("V view", app.StatusText, StringComparison.Ordinal);
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
        Assert.Equal("0", Value(app.Inspector, "Agent", "id"));
        Assert.Equal("3 also selected", Value(app.Inspector, "Sources", "others"));
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
            ["Agent", "Progress", "Plan", "Formation", "Planning", "Sources", "Controls"],
            GroupRuns(app.Inspector));

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right), new ScriptedFrame()],
            new RecordingRenderer());
        host.Run(app);

        // Ordered, which adds a formation and the field rows measured against it.
        // No heading may repeat, and the viewer's own group stays LAST OF THE
        // ONES ABOUT THE UNIT -- after everything the movement layer had to say,
        // never interleaved with it.
        //
        // Controls is behind it and is not part of that ordering at all: it is
        // the one group that is not about the unit, it is the same fourteen rows
        // whoever is selected, and it is last so that adding it moved nothing.
        var runs = GroupRuns(app.Inspector);
        Assert.Equal(runs.Count, runs.Distinct().Count());
        Assert.Contains("Formation", runs);
        Assert.Equal("Controls", runs[^1]);
        Assert.Equal("Sources", runs[^2]);
    }

    [Fact]
    public void EveryRowSaysWhichLayerAnsweredAndNothingElseEverAppearsAsASection()
    {
        // THE SWEEP ONE LEVEL UP FROM THE " -- " ONE. A row with no section is a
        // row the panel has nowhere to put, and a section nobody recognises is
        // the app inventing a caption -- and neither shows up anywhere but in a
        // running window. Every panel this project can arrange is swept, so a
        // block somebody adds later and forgets to stamp fails here.
        string[] layers = [Movement, Tactics, Viewer];
        var seen = 0;
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (state, rows) in EveryPanel())
        {
            foreach (var row in rows)
            {
                seen++;
                Assert.False(
                    string.IsNullOrEmpty(row.Section),
                    $"'{state}' left {row.Group}/{row.Key} with no section on it");
                Assert.Contains(row.Section, layers);
                found.Add(row.Section);
            }
        }

        Assert.True(seen > 100, $"the sweep only saw {seen} rows");

        // And all three layers did answer somewhere, so this cannot pass by two
        // of them having quietly stopped contributing.
        Assert.Equal(layers.Order(), found.Order());
    }

    [Fact]
    public void AGroupSitsUnderTheSectionOfTheLayerThatProducedIt()
    {
        // A real row from each of the three, asserted by SECTION as well as by
        // group. A row that drifted into the wrong caption still reads fine in a
        // flat list, which is exactly why this is worth pinning.
        //
        // The source here never writes a section of its own -- see Source, which
        // is written out of the interface and knows nothing about layers -- so
        // this also proves the stamp is the app's and not the producer's.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, sources: [new Source("Fight", 0)]);

        Assert.Equal("0", ValueIn(app.Inspector, Movement, "Agent", "id"));
        Assert.Equal("no", ValueIn(app.Inspector, Movement, "Progress", "searching"));
        Assert.Equal("0 by Fight", ValueIn(app.Inspector, Tactics, "Fight", "watched"));
        Assert.Equal("Fight", ValueIn(app.Inspector, Tactics, "Fight world", "source"));
        Assert.Equal("no", ValueIn(app.Inspector, Viewer, "Sources", "no route"));
        Assert.Equal("pause", ValueIn(app.Inspector, Viewer, "Controls", "SPACE"));

        // Each layer is one run, in table order, so a host prints three captions.
        Assert.Equal([Movement, Tactics, Viewer], SectionRuns(app.Inspector));
    }

    [Fact]
    public void AGroupTheTableNeverHeardOfStillAppearsAndSortsAfterTheOnesItKnows()
    {
        // A source may name a group anything, so a group the arrangement has
        // never heard of is a NORMAL case rather than an error: it keeps the
        // section of the layer that produced it and goes after every group the
        // table does know, in the order it arrived.
        //
        // "Ranked" is in the arrangement and arrives SECOND here, behind a name
        // that is not -- so a panel that laid blocks out in arrival order fails
        // this.
        var grid = Fixture();
        var app = new ViewerApp(
            grid,
            LayoutFor(grid),
            Squad,
            sources: [new Source("Zebra", 0) { UnitGroup = "Zebra", WorldGroup = "Ranked" }],
            arrangement: Arranged);

        Assert.Equal(
            [(Tactics, "Ranked"), (Tactics, "Zebra")],
            HeadingRuns(app.Inspector).Where(r => string.Equals(r.Section, Tactics, StringComparison.Ordinal)));

        // Nothing vanished on the way.
        Assert.Equal("0 by Zebra", ValueIn(app.Inspector, Tactics, "Zebra", "watched"));
        Assert.Equal("Zebra", ValueIn(app.Inspector, Tactics, "Ranked", "source"));
    }

    [Fact]
    public void TheSectionsAndTheGroupsInThemComeOutInTheOrderTheTableDeclares()
    {
        // ORDER IS THE ARRANGEMENT'S, not the order blocks happened to be
        // appended in. Checked against Arranged rather than against a list
        // written out again below, so a reader who reorders the arrangement gets
        // a panel that reordered with it and this test goes on being true.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(
            grid, layout, Squad, sources: [new Source("Fight", 0)], arrangement: Arranged);

        using var host = new ScriptedHost(
            [new ScriptedFrame(Mouse: layout.CenterOf(10, 5), ButtonsDown: MouseButtons.Right), new ScriptedFrame()],
            new RecordingRenderer());
        host.Run(app);

        var runs = HeadingRuns(app.Inspector);
        Assert.True(runs.Count > 8, $"only {runs.Count} headings, so this swept almost nothing");

        for (var i = 1; i < runs.Count; i++)
        {
            var (beforeSection, beforeGroup) = (SectionRank(runs[i - 1].Section), GroupRank(runs[i - 1]));
            var (afterSection, afterGroup) = (SectionRank(runs[i].Section), GroupRank(runs[i]));

            Assert.True(
                beforeSection < afterSection || (beforeSection == afterSection && beforeGroup <= afterGroup),
                $"'{runs[i - 1]}' came out before '{runs[i]}', which the table does not say");
        }

        // And the movement layer's own groups are in the table's order rather
        // than merely in SOME order, so a table that listed them backwards would
        // have to show a panel that read backwards.
        Assert.Equal(
            Arranged.Sections[0].Groups.Where(g => runs.Contains((Movement, g))),
            runs.Where(r => string.Equals(r.Section, Movement, StringComparison.Ordinal)).Select(r => r.Group));
    }

    [Fact]
    public void TheArrangementTheComposerHandsOverIsWhatDrivesTheOrder()
    {
        // CHANGE THE COMPOSER'S TABLE, CHANGE THE PANEL. Two apps over identical
        // content, an identical source and identical rows, differing in nothing
        // but the arrangement handed to the constructor -- which is the whole
        // claim of moving the table out of this project and into a host.
        var grid = Fixture();
        var layout = LayoutFor(grid);

        var forward = new InspectorArrangement(
        [
            (Movement, new[] { MovementGroups.Agent, MovementGroups.Progress }),
            (Tactics, new[] { "Fight", "Fight world" }),
        ]);

        var backward = new InspectorArrangement(
        [
            (Tactics, new[] { "Fight world", "Fight" }),
            (Movement, new[] { MovementGroups.Progress, MovementGroups.Agent }),
        ]);

        var first = new ViewerApp(
            grid, layout, Squad, sources: [new Source("Fight", 0)], arrangement: forward);
        var second = new ViewerApp(
            grid, layout, Squad, sources: [new Source("Fight", 0)], arrangement: backward);

        // Named groups in the arrangement's order, then everything it did not
        // name after them in the order it arrived.
        Assert.Equal(
            [
                (Movement, MovementGroups.Agent),
                (Movement, MovementGroups.Progress),
                (Movement, MovementGroups.Plan),
                (Movement, MovementGroups.Formation),
                (Movement, MovementGroups.Planning),
                (Tactics, "Fight"),
                (Tactics, "Fight world"),
                (Viewer, InspectorLayout.SourcesGroup),
                (Viewer, InspectorLayout.ControlsGroup),
            ],
            HeadingRuns(first.Inspector));

        // Sections swapped, groups swapped inside both of them, and the section
        // neither arrangement names still last.
        Assert.Equal(
            [
                (Tactics, "Fight world"),
                (Tactics, "Fight"),
                (Movement, MovementGroups.Progress),
                (Movement, MovementGroups.Agent),
                (Movement, MovementGroups.Plan),
                (Movement, MovementGroups.Formation),
                (Movement, MovementGroups.Planning),
                (Viewer, InspectorLayout.SourcesGroup),
                (Viewer, InspectorLayout.ControlsGroup),
            ],
            HeadingRuns(second.Inspector));

        // The same rows either way: an arrangement moves blocks and creates,
        // drops and rewrites nothing.
        Assert.Equal(
            first.Inspector.Order(RowOrder).ToList(),
            second.Inspector.Order(RowOrder).ToList());
    }

    [Fact]
    public void TheOrderComesOffTheVIEWSRatherThanOffAListSomebodyWroteTwice()
    {
        // ASK, DO NOT RESTATE. A composer builds its arrangement out of what the
        // views say they can produce, so changing what a view DECLARES changes
        // the panel with nothing else touched -- which is the whole of what
        // putting the vocabulary on the interface buys.
        var grid = Fixture();
        var layout = LayoutFor(grid);

        // Both sources emit the unit block first and the world block second.
        // This one says so; the next one says the opposite. Nothing else differs,
        // and the arrival order is identical, so a derivation that fell back on
        // arrival order gives the same answer twice and fails here.
        var asWritten = new Source("Fight", 0);
        var reversed = new Source("Fight", 0) { Declares = ["Fight world", "Fight"] };

        var first = new ViewerApp(
            grid,
            layout,
            Squad,
            sources: [asWritten],
            arrangement: InspectorArrangement.Derived([(Tactics, asWritten.Groups)]));

        var second = new ViewerApp(
            grid,
            layout,
            Squad,
            sources: [reversed],
            arrangement: InspectorArrangement.Derived([(Tactics, reversed.Groups)]));

        Assert.Equal(
            [(Tactics, "Fight"), (Tactics, "Fight world")],
            HeadingRuns(first.Inspector).Where(r => string.Equals(r.Section, Tactics, StringComparison.Ordinal)));

        Assert.Equal(
            [(Tactics, "Fight world"), (Tactics, "Fight")],
            HeadingRuns(second.Inspector).Where(r => string.Equals(r.Section, Tactics, StringComparison.Ordinal)));
    }

    [Fact]
    public void APreferenceBeatsWhatTheViewDeclared()
    {
        // DERIVED BY DEFAULT IS NOT FIXED. A composer that disagrees with a
        // producer about where a block goes says so in one line and keeps
        // everything it did not mention -- so this is the same source as above,
        // declaring the same order, laid out the other way round by preference
        // alone.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var source = new Source("Fight", 0);

        var app = new ViewerApp(
            grid,
            layout,
            Squad,
            sources: [source],
            arrangement: InspectorArrangement.Derived(
                [(Tactics, source.Groups)],
                [(Tactics, new[] { "Fight world" })]));

        Assert.Equal(
            [(Tactics, "Fight world"), (Tactics, "Fight")],
            HeadingRuns(app.Inspector).Where(r => string.Equals(r.Section, Tactics, StringComparison.Ordinal)));

        // The hoist moved one block and invented nothing: the group it did not
        // mention is still there, and still under the caption its view answers
        // under.
        Assert.Equal(
            new[] { "Fight world", "Fight" },
            InspectorArrangement.Derived(
                [(Tactics, source.Groups)],
                [(Tactics, new[] { "Fight world" })]).Sections.Single().Groups);
    }

    [Fact]
    public void ASectionKeepsItsPlaceWhenASecondViewAnswersUnderIt()
    {
        // Two sources under one caption is one section, not two, and the second
        // one's headings land after the first one's. The captions are also in the
        // order they were handed over, which is the composer's own decision and
        // the one thing a derivation must not reshuffle.
        var derived = InspectorArrangement.Derived(
        [
            (Tactics, new[] { "Fight", "Fight world" }),
            (Movement, new[] { MovementGroups.Agent }),
            (Tactics, new[] { "Supply", "Fight" }),
        ]);

        Assert.Equal([Tactics, Movement], derived.Sections.Select(s => s.Section));
        Assert.Equal(["Fight", "Fight world", "Supply"], derived.Sections[0].Groups);
    }

    [Fact]
    public void AViewerHandedNoArrangementLaysItsBlocksOutInTheOrderTheyArrived()
    {
        // THE CONTRACT DebugRow ALREADY STATES, restored rather than fallen back
        // on: rows arrive already in group order, so a panel renders headings by
        // watching the group change rather than by sorting. A host with no
        // inspector -- the raylib one -- composes a ViewerApp without knowing an
        // arrangement exists, and this is what it gets.
        var grid = Fixture();
        var layout = LayoutFor(grid);
        var app = new ViewerApp(grid, layout, Squad, sources: [new Source("Fight", 0)]);

        Assert.Equal(
            [
                (Movement, MovementGroups.Agent),
                (Movement, MovementGroups.Progress),
                (Movement, MovementGroups.Plan),
                (Movement, MovementGroups.Formation),
                (Movement, MovementGroups.Planning),
                (Tactics, "Fight"),
                (Tactics, "Fight world"),
                (Viewer, InspectorLayout.SourcesGroup),
                (Viewer, InspectorLayout.ControlsGroup),
            ],
            HeadingRuns(app.Inspector));

        // And the source's two blocks DO move when somebody asks for them the
        // other way round, so the sequence above is a result and not the only
        // order this panel is capable of.
        var reordered = new ViewerApp(
            grid,
            layout,
            Squad,
            sources: [new Source("Fight", 0)],
            arrangement: new InspectorArrangement([(Tactics, new[] { "Fight world", "Fight" })]));

        Assert.Equal(
            [(Tactics, "Fight world"), (Tactics, "Fight")],
            HeadingRuns(reordered.Inspector)
                .Where(r => string.Equals(r.Section, Tactics, StringComparison.Ordinal)));
    }

    /// <summary>A total order over rows, for comparing two panels as sets.</summary>
    private static Comparer<DebugRow> RowOrder { get; } = Comparer<DebugRow>.Create((a, b) =>
    {
        var section = string.CompareOrdinal(a.Section, b.Section);
        if (section != 0)
        {
            return section;
        }

        var group = string.CompareOrdinal(a.Group, b.Group);
        return group != 0 ? group : string.CompareOrdinal(a.Key, b.Key);
    });

    /// <summary>Where the arrangement puts a section, or after every one it names.</summary>
    private static int SectionRank(string section)
    {
        for (var i = 0; i < Arranged.Sections.Count; i++)
        {
            if (string.Equals(Arranged.Sections[i].Section, section, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return Arranged.Sections.Count;
    }

    /// <summary>Where the arrangement puts a group inside its section, or after every one it names.</summary>
    private static int GroupRank((string Section, string Group) heading)
    {
        foreach (var (section, groups) in Arranged.Sections)
        {
            if (!string.Equals(section, heading.Section, StringComparison.Ordinal))
            {
                continue;
            }

            for (var i = 0; i < groups.Count; i++)
            {
                if (string.Equals(groups[i], heading.Group, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return int.MaxValue;
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
            ["Agent", "Progress", "Plan", "Formation", "Planning", "Sources", "Controls"],
            GroupRuns(plain.Inspector));

        // Nothing renamed, and no source reported broken, because there was
        // nothing to rename and nothing to break.
        Assert.DoesNotContain(plain.Inspector, r => r.Group.Contains('('));
        Assert.Equal(
            ["waits", "no route"],
            plain.Inspector.Where(r => string.Equals(r.Group, "Sources", StringComparison.Ordinal))
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
            ["Agent", "Progress", "Plan", "Formation", "Planning", "Fight", "Fight world", "Sources", "Controls"],
            GroupRuns(app.Inspector));

        Assert.Equal("0 by Fight", Value(app.Inspector, "Fight", "watched"));
        Assert.Equal("Fight", Value(app.Inspector, "Fight world", "source"));

        // And the movement layer's own rows are exactly what they were.
        Assert.Equal("0", Value(app.Inspector, "Agent", "id"));
        Assert.Equal("no", Value(app.Inspector, "Progress", "searching"));
        Assert.Equal("no", Value(app.Inspector, "Sources", "no route"));
    }

    [Fact]
    public void TwoSourcesArriveInTheOrderTheyWereHandedOverWhereTheTableSaysNothing()
    {
        // Supply order, not name order and not whichever answered first: the
        // composer decided, and a panel that reshuffled between frames would be
        // unreadable however it sorted.
        //
        // NEITHER OF THESE NAMES IS IN THE ARRANGEMENT, deliberately. Supply
        // order is what breaks a tie the table does not break, so a fixture that
        // borrowed a name the table ranks would be testing the table instead --
        // which is what "Fight" used to do here by coincidence, and it is ranked.
        var grid = Fixture();
        var forward = new ViewerApp(
            grid, LayoutFor(grid), Squad, sources: [new Source("Recon", 0), new Source("Supply", 0)]);
        var backward = new ViewerApp(
            grid, LayoutFor(grid), Squad, sources: [new Source("Supply", 0), new Source("Recon", 0)]);

        Assert.Equal(
            ["Agent", "Progress", "Plan", "Formation", "Planning",
             "Recon", "Recon world", "Supply", "Supply world", "Sources", "Controls"],
            GroupRuns(forward.Inspector));

        Assert.Equal(
            ["Agent", "Progress", "Plan", "Formation", "Planning",
             "Supply", "Supply world", "Recon", "Recon world", "Sources", "Controls"],
            GroupRuns(backward.Inspector));

        Assert.Equal("0 by Supply", Value(forward.Inspector, "Supply", "watched"));
    }

    [Fact]
    public void AGroupNameThePanelAlreadyUsesIsRenamedRatherThanMergedIntoIt()
    {
        // Two sources both calling their unit block "Agent" and both calling
        // their world block "Progress". They land in ONE section, so interleaved
        // they would read as each other's answers -- two identical headings under
        // TACTICS, printed twice by a host that prints one on each change, and
        // folding as one in a panel that keys its fold state by section and name.
        //
        // NO REAL PAIR OF SOURCES COLLIDES TODAY -- the movement layer says
        // Agent and the tactics view says Condition, since a panel heading of
        // "Unit (2)" turned out to mean nothing to anybody reading it. So this
        // fixture is the whole of the proof that the mechanism still works, and
        // it is written here out of the interface for exactly that reason: a
        // source is somebody else's code and may name a group anything.
        var grid = Fixture();
        var app = new ViewerApp(grid, LayoutFor(grid), Squad, sources:
        [
            new Source("Fight", 0) { UnitGroup = "Agent", WorldGroup = "Progress" },
            new Source("Supply", 0) { UnitGroup = "Agent", WorldGroup = "Progress" },
        ]);

        // No heading repeats, where a heading is now its section AND its name.
        var runs = HeadingRuns(app.Inspector);
        Assert.Equal(runs.Count, runs.Distinct().Count());

        // The movement layer keeps its headings and its rows, and neither source
        // is inside them.
        Assert.Equal("0", ValueIn(app.Inspector, Movement, "Agent", "id"));
        Assert.Equal("no", ValueIn(app.Inspector, Movement, "Progress", "searching"));
        Assert.False(
            HasKeyIn(app.Inspector, Movement, "Agent", "watched"),
            "a source landed in the movement layer's group");

        // ACROSS SECTIONS NOTHING IS RENAMED, and that is the change two levels
        // bought. MOVEMENT > AGENT and TACTICS > AGENT are two visibly different
        // headings, so numbering one of them would put a "(2)" on the panel that
        // says nothing -- the exact complaint that got the names pulled apart.
        Assert.Equal("0 by Fight", ValueIn(app.Inspector, Tactics, "Agent", "watched"));
        Assert.Equal("Fight", ValueIn(app.Inspector, Tactics, "Progress", "source"));

        // WITHIN a section it still is. The second source finds both of its names
        // taken and is numbered in the order it was handed over, and nothing of
        // what it said is lost.
        Assert.Equal("0 by Supply", ValueIn(app.Inspector, Tactics, "Agent (2)", "watched"));
        Assert.Equal("Supply", ValueIn(app.Inspector, Tactics, "Progress (2)", "source"));

        // The viewer's own two groups are in a section of their own, so no source
        // can be inside them however it names itself, and they are still last.
        Assert.Equal((Viewer, "Controls"), runs[^1]);
        Assert.Equal((Viewer, "Sources"), runs[^2]);
        Assert.Equal("no", ValueIn(app.Inspector, Viewer, "Sources", "no route"));
    }

    [Fact]
    public void ASourceThatTakesTheControlsNameLandsInItsOwnSectionRatherThanTheFolder()
    {
        // Controls used to be RESERVED, because a heading was its name alone and
        // a source's rows under CONTROLS would have read as things the keyboard
        // does -- the one heading in the panel a reader acts on. The section says
        // that now: the source's block is under TACTICS, the folder is under
        // VIEWER, and the reader can see which is which without a "(2)".
        var grid = Fixture();
        var app = new ViewerApp(
            grid, LayoutFor(grid), Squad, sources: [new Source("Fight", 0) { UnitGroup = "Controls" }]);

        Assert.Equal("0 by Fight", ValueIn(app.Inspector, Tactics, "Controls", "watched"));
        Assert.False(
            HasKeyIn(app.Inspector, Viewer, "Controls", "watched"),
            "a source landed in the viewer's own controls folder");

        // And the folder is still the folder.
        Assert.Equal("pause", ValueIn(app.Inspector, Viewer, "Controls", "SPACE"));
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
        Assert.Equal("0", Value(app.Inspector, "Agent", "id"));
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
            app.Inspector.Where(r => string.Equals(r.Group, "Sources", StringComparison.Ordinal))
                         .Select(r => r.Key));

        // The type is the value and the message is the note: an exception message
        // is somebody else's arbitrary-length string, and the panel column cannot
        // be sized for one.
        Assert.Equal("threw InvalidOperationException", Value(app.Inspector, "Sources", "source 1"));
        Assert.Equal("Early will not answer for unit 0", Note(app.Inspector, "Sources", "source 1"));
        Assert.Equal("threw InvalidOperationException", Value(app.Inspector, "Sources", "source 2"));
        Assert.Equal("Late cannot read unit 0", Note(app.Inspector, "Sources", "source 2"));
        Assert.Equal("threw InvalidOperationException", Value(app.Inspector, "Sources", "source 3"));
        Assert.Equal("Own cannot read itself", Note(app.Inspector, "Sources", "source 3"));
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
        Assert.Equal("0", Value(app.Inspector, "Agent", "id"));
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

        Assert.Equal("yes", Value(app.Inspector, "Agent", "alive"));
        Assert.Equal("in the world and holding its cell", Note(app.Inspector, "Agent", "alive"));

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
        Assert.Null(Row(app.Inspector, "Agent", "id").Note);
        Assert.Null(Row(app.Inspector, "Agent", "cell").Note);
        Assert.Null(Row(app.Inspector, "Agent", "arrived").Note);
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

        /// <summary>
        /// What this source says it can produce, or null for the two groups it
        /// actually writes, in the order it writes them.
        /// </summary>
        /// <remarks>
        /// Settable so a test can hand over a source whose DECLARED order is not
        /// its arrival order. A derived arrangement that quietly kept arrival
        /// order would pass every assertion made against a source that declares
        /// what it emits in the order it emits it.
        /// </remarks>
        public IReadOnlyList<string>? Declares { get; init; }

        public IReadOnlyList<string> Groups => Declares ?? [UnitGroup, WorldGroup];

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
            public IReadOnlyList<string> Groups => [source.UnitGroup];

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
