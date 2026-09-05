using Nav.Core.Interfaces;
using Nav.Viewer;
using Nav.Viewer.Interfaces;
using Nav.Viewer.Models;
using Nav.Viewer.Tactics;

namespace Nav.Viewer.Wpf.Tests;

/// <summary>
/// The headings the real guard world puts on the panel, read off the same
/// composition the window opens with.
/// </summary>
/// <remarks>
/// <b>Here because this is the only test project that can see both sources at
/// once.</b> Nav.Viewer.Tests references Nav.Viewer.Shared alone and cannot name
/// a tactics world; Nav.Viewer.Tactics.Tests can read the tactics view's rows
/// but never meets the movement layer's. What a heading COLLIDES with is a fact
/// about the two of them side by side, so it can only be asserted where both are
/// composed -- which is <see cref="Program.Compose"/>, and that lives here.
/// <para>
/// Nothing is drawn. The rows are data, and the window is not opened: this is
/// about what the panel is asked to render, not about how it renders it.
/// </para>
/// </remarks>
public sealed class InspectorHeadingTests
{
    private static ViewerApp GuardWorld() => GuardWorld(preferences: null).App;

    /// <summary>
    /// The window's own composition, with the panel arranged the way the host
    /// arranges it -- optionally with something hoisted, the way configuration
    /// would.
    /// </summary>
    private static (ViewerApp App, ViewerSession Session, IReadOnlyList<IWorldDebugView> Sources) GuardWorld(
        IReadOnlyList<(string Section, IReadOnlyList<string> First)>? preferences)
    {
        var (session, sources, eyes, _) = Program.Compose("guard-retreat");
        var app = new ViewerApp(
            session,
            Program.MaxMapPixels,
            Program.MaxMapPixels - Program.StatusHeight,
            keys: null,
            sources,
            eyes,
            Program.ArrangementFor(session, sources, preferences));

        return (app, session, sources);
    }

    /// <summary>The headings a host would print, in the order it would print them.</summary>
    private static List<string> Headings(IReadOnlyList<DebugRow> rows)
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

    /// <summary>The groups a host would print under one section, in order.</summary>
    private static List<string> GroupsUnder(IReadOnlyList<DebugRow> rows, string section)
    {
        var runs = new List<string>();
        foreach (var row in rows)
        {
            if (!string.Equals(row.Section, section, StringComparison.Ordinal))
            {
                continue;
            }

            if (runs.Count == 0 || !string.Equals(runs[^1], row.Group, StringComparison.Ordinal))
            {
                runs.Add(row.Group);
            }
        }

        return runs;
    }

    private static string Value(IReadOnlyList<DebugRow> rows, string group, string key) =>
        rows.Single(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal)).Value;

    [Fact]
    public void NoHeadingIsRenamedByTheDeCollisionBecauseNoneOfThemCollide()
    {
        // THE COMPLAINT, AS AN ASSERTION: a panel heading reading "UNIT (2)".
        // Both sources called their block Unit, so the second was renamed, and
        // the number meant nothing to anybody looking at it. The movement layer
        // says Agent now and the tactics view says Condition.
        var rows = GuardWorld().Inspector;
        var headings = Headings(rows);

        Assert.DoesNotContain(headings, h => h.Contains('(', StringComparison.Ordinal));
        Assert.DoesNotContain("Unit", headings);
        Assert.Equal(headings.Count, headings.Distinct(StringComparer.Ordinal).Count());

        // Both blocks are there under their own names, so this did not pass by
        // one of them having quietly stopped contributing rows.
        Assert.Contains("Agent", headings);
        Assert.Contains("Condition", headings);
    }

    [Fact]
    public void TheDoctrinesOwnLevelIsOnThePanelForTheUnitBeingWatched()
    {
        // Agent 0 is a guard, selected from the start. Between the movement
        // layer's rows about one unit and the world's rows about the whole
        // board, the panel now carries the level in between -- which is the one
        // a doctrine reasons at, and the only one the reader had no window on.
        var rows = GuardWorld().Inspector;

        Assert.Contains("Squad", Headings(rows));
        Assert.Equal("guard", Value(rows, "Squad", "squad"));
        Assert.Equal("8", Value(rows, "Squad", "members"));

        // Nobody has moved yet, so the squad has no anchor and a guard's next
        // act is the march to its station. That is exactly what the doctrine
        // branches on, read off the panel.
        Assert.Equal("none", Value(rows, "Squad", "anchor"));
    }

    [Fact]
    public void ThePanelIsThreeSectionsEachNamingTheLayerThatAnswered()
    {
        // THE WHOLE PANEL A READER OPENS, read off the composition the window
        // opens with. Sixteen headings as flat peers said nothing about where
        // one set of information started and the next began; three captions say
        // which layer answered, which is the seam this project is built around
        // made visible in the instrument.
        var rows = GuardWorld().Inspector;

        Assert.Equal(
            [InspectorLayout.MovementSection, InspectorLayout.TacticsSection, InspectorLayout.ViewerSection],
            SectionRuns(rows));

        Assert.Equal(
            ["Agent", "Progress", "Plan", "Formation", "Planning"],
            GroupsUnder(rows, InspectorLayout.MovementSection));

        Assert.Equal(
            ["Squad", "Condition", "Kit", "Fight", "Perception", "World", "Rates", "Rank"],
            GroupsUnder(rows, InspectorLayout.TacticsSection));

        Assert.Equal(
            [InspectorLayout.SourcesGroup, InspectorLayout.ControlsGroup],
            GroupsUnder(rows, InspectorLayout.ViewerSection));
    }

    [Fact]
    public void NoViewEmitsAGroupItNeverDeclared()
    {
        // WHAT THE ASKING BUYS, AS AN ASSERTION. The panel's order is derived
        // from IDebugView.Groups, so a view that emits a heading it did not
        // declare is a block nothing ranks: it drops to unknown-order and sinks
        // to the bottom of its section, silently, in a window nobody has open.
        // That was the failure mode a drift test used to guard against by
        // comparing two hand-kept lists; there is only one list now, and this is
        // the contract having one creates.
        //
        // THE VIEWS ARE ASKED DIRECTLY, not read off the panel. De-collision
        // rewrites a heading to "Agent (2)" as it merges, and a declaration is
        // about what a view emitted rather than about what the panel did with it.
        var (_, session, sources) = GuardWorld(preferences: null);

        // Run the fight. Half the rows on this panel are conditional -- a
        // casualty, a squad with somebody away, a unit with a target, a side that
        // remembers a sighting -- and a fixture at tick zero reaches none of
        // them. The waves land at 160.
        for (var tick = 0; tick < 220; tick++)
        {
            session.Tick();
        }

        // Live ids, plus the two answers every view promises for an id it has
        // never issued and one that cannot exist.
        var ids = new List<int> { -1, 9999 };
        for (var id = 0; id < session.Agents.Count; id++)
        {
            ids.Add(id);
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var read = 0;

        foreach (var source in sources)
        {
            Undeclared(source, emitted, ref read);
        }

        foreach (var id in ids)
        {
            Undeclared(session.DebugFor(id), emitted, ref read);
            foreach (var source in sources)
            {
                Undeclared(source.DebugFor(id), emitted, ref read);
            }
        }

        // A GREEN SWEEP THAT READ NOTHING IS NOT A GREEN SWEEP. The fight has to
        // have been played far enough for the conditional blocks to have turned
        // up, or every view above answered with four rows about a corpse and
        // agreed with itself.
        Assert.True(read > 30, $"only {read} views were read");

        // A FLOOR, DELIBERATELY UNDER THE TOTAL. All fourteen turn up in this
        // world today, and asserting fourteen would be asserting the reverse
        // direction by the back door: a group that becomes conditional -- emitted
        // only for a unit carrying a kit, only for a side that has seen
        // something -- is a legitimate declaration and not a fault, and this test
        // is about the direction that IS one.
        Assert.True(
            emitted.Count >= 12,
            $"only {emitted.Count} distinct groups were ever emitted: {string.Join(", ", emitted.Order())}");
    }

    /// <summary>
    /// Every row one view emitted, checked against what that view says it can
    /// produce, and the groups it reached added to <paramref name="emitted"/>.
    /// </summary>
    private static void Undeclared(IDebugView view, HashSet<string> emitted, ref int read)
    {
        var declared = view.Groups;
        read++;

        foreach (var row in view.Describe())
        {
            emitted.Add(row.Group);
            Assert.True(
                declared.Contains(row.Group, StringComparer.Ordinal),
                $"{view.GetType().Name} emitted '{row.Group}/{row.Key}' and declares only " +
                string.Join(", ", declared));
        }
    }

    [Fact]
    public void ThePanelIsOrderedByWhatTheViewsDeclaredAndNotByAListInTheHost()
    {
        // DERIVED, AND THE DERIVATION IS WHAT IS ASSERTED. Every heading under a
        // section comes out in the order the view that emits it declared, with
        // nothing the host wrote down: change MovementGroups.All and this panel
        // changes with no edit to Program.cs.
        Assert.Empty(Program.Preferences);

        var app = GuardWorld();
        var rows = app.Inspector;

        // Filtered to what turned up, because a declaration is what a view CAN
        // produce: agent 0 is unhurt and on station at tick zero, so the movement
        // layer's Field block is not on this panel and is not expected to be.
        Assert.Equal(
            MovementGroups.All.Where(g => GroupsUnder(rows, InspectorLayout.MovementSection).Contains(g)),
            GroupsUnder(rows, InspectorLayout.MovementSection));

        Assert.Equal(
            DemoWorldGroups.All.Where(g => GroupsUnder(rows, InspectorLayout.TacticsSection).Contains(g)),
            GroupsUnder(rows, InspectorLayout.TacticsSection));

        Assert.Equal(
            InspectorLayout.ViewerGroups.Where(g => GroupsUnder(rows, InspectorLayout.ViewerSection).Contains(g)),
            GroupsUnder(rows, InspectorLayout.ViewerSection));

        // Not vacuous: each section actually printed several blocks, so the three
        // assertions above compared sequences rather than empties.
        Assert.True(GroupsUnder(rows, InspectorLayout.MovementSection).Count > 3);
        Assert.True(GroupsUnder(rows, InspectorLayout.TacticsSection).Count > 3);
    }

    [Fact]
    public void APreferenceMovesABlockTheViewDeclaredElsewhere()
    {
        // DERIVED BY DEFAULT IS NOT FIXED. The tactics view declares the unit's
        // blocks before the board's; a host told to read the board first says so
        // in one line, and the seven groups it did not mention keep the order
        // their view gave them.
        var hoisted = GuardWorld(
        [
            (InspectorLayout.TacticsSection, new[] { DemoWorldGroups.World }),
        ]).App;

        var groups = GroupsUnder(hoisted.Inspector, InspectorLayout.TacticsSection);

        Assert.Equal(DemoWorldGroups.World, groups[0]);
        Assert.Equal(
            DemoWorldGroups.All.Where(g => g != DemoWorldGroups.World && groups.Contains(g)),
            groups.Skip(1));

        // And it is the preference doing it: the same composition without one
        // puts the same block where its view declared it, sixth.
        Assert.NotEqual(
            DemoWorldGroups.World,
            GroupsUnder(GuardWorld().Inspector, InspectorLayout.TacticsSection)[0]);
    }
}
