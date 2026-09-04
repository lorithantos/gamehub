using Nav.Viewer;
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
    private static ViewerApp GuardWorld()
    {
        var (session, sources, eyes) = Program.Compose("guard-retreat");
        return new ViewerApp(
            session,
            Program.MaxMapPixels,
            Program.MaxMapPixels - Program.StatusHeight,
            keys: null,
            sources,
            eyes,
            Program.Arrangement);
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
    public void TheHostsArrangementAndTheProducersCannotDriftApart()
    {
        // THE TEST THAT MAKES LIFTING THE CONSTANTS WORTH ANYTHING. The host
        // orders the panel by naming groups; a name that stops matching does not
        // throw and does not go missing -- the block drops to unknown-order and
        // sinks to the bottom of its section, silently, in a window nobody has
        // open. That was the failure mode while the names were quoted.
        //
        // Both directions are checked. A group the arrangement names that nobody
        // produces is a dead entry; a group produced that the arrangement does
        // not name is an unordered block. Naming them through MovementGroups and
        // DemoWorldGroups is what turns either into a build error instead, and
        // this is the assertion that says so out loud.
        var arrangement = Program.Arrangement;

        Assert.Equal(
            [InspectorLayout.MovementSection, InspectorLayout.TacticsSection, InspectorLayout.ViewerSection],
            arrangement.Sections.Select(s => s.Section));

        // Every heading each producer can emit, and no invented extras.
        Assert.Equal(MovementGroups.All.Order(), GroupsNamed(arrangement, InspectorLayout.MovementSection).Order());
        Assert.Equal(DemoWorldGroups.All.Order(), GroupsNamed(arrangement, InspectorLayout.TacticsSection).Order());
        Assert.Equal(
            new[] { InspectorLayout.SourcesGroup, InspectorLayout.ControlsGroup }.Order(),
            GroupsNamed(arrangement, InspectorLayout.ViewerSection).Order());

        // And the constants are the strings the producers actually emit, read
        // off the real composition rather than off the holders. A producer that
        // stopped saying "Squad" while the constant still said so would pass the
        // three assertions above and fail this one.
        var emitted = GuardWorld().Inspector
            .Select(r => (r.Section, r.Group))
            .Distinct()
            .ToList();

        Assert.True(emitted.Count > 12, $"the panel only produced {emitted.Count} headings");

        foreach (var (section, group) in emitted)
        {
            Assert.Contains(group, GroupsNamed(arrangement, section));
        }
    }

    /// <summary>The groups the arrangement names under one section.</summary>
    private static IReadOnlyList<string> GroupsNamed(InspectorArrangement arrangement, string section)
    {
        foreach (var (named, groups) in arrangement.Sections)
        {
            if (string.Equals(named, section, StringComparison.Ordinal))
            {
                return groups;
            }
        }

        return [];
    }
}
