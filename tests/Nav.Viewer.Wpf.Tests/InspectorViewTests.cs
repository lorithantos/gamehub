using System.Windows;
using System.Windows.Controls;

using Nav.Viewer.Models;

namespace Nav.Viewer.Wpf.Tests;

/// <summary>
/// The inspector panel as elements: what it rebuilds, what it only writes into,
/// and what it remembers between frames.
/// </summary>
/// <remarks>
/// The rows themselves are data and are asserted where they are produced. What
/// is here is the part a running window shows and a row test cannot: that a
/// frame which only changed values does not throw the elements away, and that a
/// group the reader folded is still folded on the next frame.
/// <para>
/// Those two are the same property. The panel is driven sixty times a second, so
/// a tree rebuilt per frame would not merely be wasteful -- it would drop the
/// fold state every frame, and present as folders that will not stay shut.
/// </para>
/// <para>
/// The rows here are written by hand rather than taken from a world, which is
/// what lets this file arrange the one case a real composition does not currently
/// produce: the same group name under two sections. That case is the whole reason
/// the fold key grew a section in it.
/// </para>
/// </remarks>
public sealed class InspectorViewTests
{
    private const string Movement = InspectorLayout.MovementSection;
    private const string Tactics = InspectorLayout.TacticsSection;

    /// <summary>A panel's worth of rows, with the values a caller supplies.</summary>
    private static DebugRow[] Rows(string alive, string cell) =>
    [
        new("Unit", "id", "0") { Section = Movement },
        new("Unit", "alive", alive, "in the world and holding its cell") { Section = Movement },
        new("Unit", "cell", cell) { Section = Movement },
        new("Progress", "retry gate", "open", "it may start a search this tick") { Section = Movement },
        new("Progress", "stalled", "no") { Section = Movement },
    ];

    /// <summary>The heading strip of one section, in the order they are laid out.</summary>
    private static Border SectionHeadingOf(Panel host, int section) => (Border)host.Children[section * 2];

    /// <summary>The panel a section folds away: every group heading and body in it.</summary>
    private static StackPanel SectionBodyOf(Panel host, int section) =>
        (StackPanel)host.Children[(section * 2) + 1];

    private static int SectionCount(Panel host) => host.Children.Count / 2;

    /// <summary>What a section heading says, chevron aside.</summary>
    private static string SectionCaption(Panel host, int section) =>
        ((TextBlock)((StackPanel)SectionHeadingOf(host, section).Child).Children[1]).Text;

    private static Border HeadingOf(Panel host, int section, int group) =>
        (Border)SectionBodyOf(host, section).Children[group * 2];

    private static StackPanel BodyOf(Panel host, int section, int group) =>
        (StackPanel)SectionBodyOf(host, section).Children[(group * 2) + 1];

    private static int GroupCount(Panel host, int section) => SectionBodyOf(host, section).Children.Count / 2;

    /// <summary>The row grids under a heading, in the order they are laid out.</summary>
    private static List<Grid> RowsUnder(Panel host, int section, int group) =>
        [.. BodyOf(host, section, group).Children.OfType<Grid>()];

    private static string TextAt(Grid row, int column) => ((TextBlock)row.Children[column]).Text;

    [Fact]
    public void AFrameThatOnlyChangesValuesDoesNotRebuildTheElements()
    {
        // THE RULE THE PANEL IS BUILT AROUND. Identity is asserted rather than a
        // counter alone, because a rebuild that happened to produce the same
        // number of elements would pass a count and fail a reader.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);

            panel.Update(Rows("yes", "1,1 (#12)"));
            Assert.Equal(1, panel.Rebuilds);

            var before = RowsUnder(host, 0, 0);
            var sectionBefore = SectionHeadingOf(host, 0);
            var sectionBodyBefore = SectionBodyOf(host, 0);
            var headingBefore = HeadingOf(host, 0, 0);

            panel.Update(Rows("no", "2,1 (#13)"));

            Assert.Equal(1, panel.Rebuilds);

            // The section level joins the discipline: its heading and the panel
            // it folds away are the SAME elements, not new ones that happen to
            // look alike.
            Assert.Same(sectionBefore, SectionHeadingOf(host, 0));
            Assert.Same(sectionBodyBefore, SectionBodyOf(host, 0));
            Assert.Same(headingBefore, HeadingOf(host, 0, 0));

            Assert.Equal(before.Count, RowsUnder(host, 0, 0).Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.Same(before[i], RowsUnder(host, 0, 0)[i]);
            }

            // And the new values did land, so "not rebuilt" is not "not updated".
            Assert.Equal("no", TextAt(before[1], 1));
            Assert.Equal("2,1 (#13)", TextAt(before[2], 1));
        });
    }

    [Fact]
    public void AChangeToTheSetOfKeysDoesRebuild()
    {
        // The other half of the rule: a row that appeared has nowhere to be
        // written, so the tree has to be built again. A panel that compared
        // values instead of shape would rebuild on every tick.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);

            panel.Update(Rows("yes", "1,1 (#12)"));
            Assert.Equal(1, panel.Rebuilds);

            List<DebugRow> longer =
            [
                .. Rows("yes", "1,1 (#12)"),
                new DebugRow("Plan", "cells", "17", "one per tick") { Section = Movement },
            ];
            panel.Update(longer);

            Assert.Equal(2, panel.Rebuilds);
            Assert.Equal(1, SectionCount(host));
            Assert.Equal(3, GroupCount(host, 0));
        });
    }

    [Fact]
    public void AChangeToTheSetOfSectionsDoesRebuild()
    {
        // A section is part of the shape for the same reason a group is: rows
        // that arrived under a new caption have nowhere to be written.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);

            panel.Update(Rows("yes", "1,1 (#12)"));
            Assert.Equal(1, SectionCount(host));

            List<DebugRow> wider =
            [
                .. Rows("yes", "1,1 (#12)"),
                new DebugRow("Squad", "squad", "guard") { Section = Tactics },
            ];
            panel.Update(wider);

            Assert.Equal(2, panel.Rebuilds);
            Assert.Equal(2, SectionCount(host));
            Assert.Equal(1, GroupCount(host, 1));

            // AND A ROW THAT ONLY CHANGED SECTION IS A SHAPE CHANGE TOO. Same
            // group, same key, a different caption over it -- so a panel that
            // compared group and key alone would leave MOVEMENT standing over
            // rows the tactics layer answered, and never notice.
            panel.Update([new DebugRow("Unit", "id", "0") { Section = Movement }]);
            Assert.Equal(3, panel.Rebuilds);
            Assert.Equal("MOVEMENT", SectionCaption(host, 0));

            panel.Update([new DebugRow("Unit", "id", "0") { Section = Tactics }]);

            Assert.Equal(4, panel.Rebuilds);
            Assert.Equal("TACTICS", SectionCaption(host, 0));
        });
    }

    [Fact]
    public void EverySectionAndEveryGroupStartsUnfolded()
    {
        // Today's behaviour, kept, at both levels. Folding is the reader's tool;
        // a panel that opened shut would make them find that out.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            Assert.False(panel.IsSectionFolded(Movement));
            Assert.False(panel.IsFolded(Movement, "Unit"));
            Assert.False(panel.IsFolded(Movement, "Progress"));
            Assert.Equal(Visibility.Visible, SectionBodyOf(host, 0).Visibility);
            Assert.Equal(Visibility.Visible, BodyOf(host, 0, 0).Visibility);
            Assert.Equal(Visibility.Visible, BodyOf(host, 0, 1).Visibility);
        });
    }

    [Fact]
    public void AFoldedGroupIsStillFoldedAfterAFrame()
    {
        // THE FAILURE THIS PREVENTS, in the words it would be reported in: the
        // folders will not stay closed. A panel rebuilt per frame loses this
        // sixty times a second and looks like a bug in the click handler.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            panel.Fold(Movement, "Unit", shut: true);
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0, 0).Visibility);

            panel.Update(Rows("no", "2,1 (#13)"));

            Assert.True(panel.IsFolded(Movement, "Unit"));
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0, 0).Visibility);

            // Its neighbour was never folded and must not have been dragged shut.
            Assert.False(panel.IsFolded(Movement, "Progress"));
            Assert.Equal(Visibility.Visible, BodyOf(host, 0, 1).Visibility);
        });
    }

    [Fact]
    public void AFoldedGroupComesBackFoldedAfterARebuild()
    {
        // A shape change is a new element tree, and the fold state is kept by
        // section and group name rather than by element -- so a group that
        // survives the change comes back the way the reader left it.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));
            panel.Fold(Movement, "Unit", shut: true);

            List<DebugRow> longer =
            [
                .. Rows("yes", "1,1 (#12)"),
                new DebugRow("Plan", "cells", "17", "one per tick") { Section = Movement },
            ];
            panel.Update(longer);

            Assert.Equal(2, panel.Rebuilds);
            Assert.True(panel.IsFolded(Movement, "Unit"));
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0, 0).Visibility);
        });
    }

    /// <summary>Two sections that both call a group "Unit", and nothing else.</summary>
    /// <remarks>
    /// The case a flat panel could not produce and the sections make ordinary:
    /// two layers describing the same thing under the same word. What a source
    /// calls a group is not the viewer's to choose, so this is a shape the panel
    /// has to survive rather than one it can rule out.
    /// </remarks>
    private static DebugRow[] SameNameInTwoSections() =>
    [
        new("Unit", "id", "0") { Section = Movement },
        new("Unit", "cell", "1,1") { Section = Movement },
        new("Unit", "squad", "guard") { Section = Tactics },
        new("Unit", "hurt", "no") { Section = Tactics },
    ];

    [Fact]
    public void TwoGroupsOfTheSameNameInDifferentSectionsFoldIndependently()
    {
        // THE BUG TWO LEVELS MAKES POSSIBLE. Keyed by group name alone, folding
        // MOVEMENT > UNIT would fold TACTICS > UNIT with it, and the reader would
        // have no way to open one without the other.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(SameNameInTwoSections());

            Assert.Equal(2, SectionCount(host));
            Assert.Equal(1, GroupCount(host, 0));
            Assert.Equal(1, GroupCount(host, 1));

            panel.Fold(Movement, "Unit", shut: true);

            Assert.True(panel.IsFolded(Movement, "Unit"));
            Assert.False(panel.IsFolded(Tactics, "Unit"));
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0, 0).Visibility);
            Assert.Equal(Visibility.Visible, BodyOf(host, 1, 0).Visibility);

            // And the other way round, so this cannot pass by the second group
            // simply never being touched.
            panel.Fold(Movement, "Unit", shut: false);
            panel.Fold(Tactics, "Unit", shut: true);

            Assert.False(panel.IsFolded(Movement, "Unit"));
            Assert.True(panel.IsFolded(Tactics, "Unit"));
            Assert.Equal(Visibility.Visible, BodyOf(host, 0, 0).Visibility);
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 1, 0).Visibility);
        });
    }

    [Fact]
    public void SameNamedGroupsInTwoSectionsStayIndependentAcrossAFrameAndARebuild()
    {
        // The fold key survives a shape change, and it has to survive it PER
        // SECTION: a rebuild that put both back from one entry would look exactly
        // like the panel forgetting one of them.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(SameNameInTwoSections());
            panel.Fold(Tactics, "Unit", shut: true);

            panel.Update(SameNameInTwoSections());
            Assert.Equal(1, panel.Rebuilds);
            Assert.False(panel.IsFolded(Movement, "Unit"));
            Assert.True(panel.IsFolded(Tactics, "Unit"));

            List<DebugRow> longer =
            [
                .. SameNameInTwoSections(),
                new DebugRow("Unit", "kit", "rifle") { Section = Tactics },
            ];
            panel.Update(longer);

            Assert.Equal(2, panel.Rebuilds);
            Assert.False(panel.IsFolded(Movement, "Unit"));
            Assert.True(panel.IsFolded(Tactics, "Unit"));
            Assert.Equal(Visibility.Visible, BodyOf(host, 0, 0).Visibility);
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 1, 0).Visibility);
        });
    }

    [Fact]
    public void AFoldedSectionHidesItsGroupsAndSurvivesAFrameAndARebuild()
    {
        // A section folds the way a group does, and the state is kept the same
        // way -- by name, in the host, so a shape change gives it back.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(SameNameInTwoSections());

            panel.FoldSection(Movement, shut: true);

            Assert.True(panel.IsSectionFolded(Movement));
            Assert.Equal(Visibility.Collapsed, SectionBodyOf(host, 0).Visibility);
            Assert.False(panel.IsSectionFolded(Tactics));
            Assert.Equal(Visibility.Visible, SectionBodyOf(host, 1).Visibility);

            panel.Update(SameNameInTwoSections());
            Assert.Equal(1, panel.Rebuilds);
            Assert.Equal(Visibility.Collapsed, SectionBodyOf(host, 0).Visibility);

            List<DebugRow> longer =
            [
                .. SameNameInTwoSections(),
                new DebugRow("Unit", "kit", "rifle") { Section = Tactics },
            ];
            panel.Update(longer);

            Assert.Equal(2, panel.Rebuilds);
            Assert.True(panel.IsSectionFolded(Movement));
            Assert.Equal(Visibility.Collapsed, SectionBodyOf(host, 0).Visibility);
            Assert.Equal(Visibility.Visible, SectionBodyOf(host, 1).Visibility);
        });
    }

    [Fact]
    public void FoldingASectionLeavesItsGroupsFoldedHoweverTheReaderLeftThem()
    {
        // The two states are independent in BOTH directions. A section that
        // folded its groups on the way shut would hand the reader back a
        // different panel from the one they closed.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            panel.Fold(Movement, "Progress", shut: true);
            panel.FoldSection(Movement, shut: true);
            panel.FoldSection(Movement, shut: false);

            Assert.False(panel.IsSectionFolded(Movement));
            Assert.Equal(Visibility.Visible, SectionBodyOf(host, 0).Visibility);

            Assert.False(panel.IsFolded(Movement, "Unit"));
            Assert.Equal(Visibility.Visible, BodyOf(host, 0, 0).Visibility);
            Assert.True(panel.IsFolded(Movement, "Progress"));
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0, 1).Visibility);
        });
    }

    [Fact]
    public void ARowWithNoNoteGetsNoTooltipAtAll()
    {
        // An empty tooltip is a grey box that pops up over the panel and says
        // nothing, which is worse than the row having nothing to add.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            var rows = RowsUnder(host, 0, 0);
            Assert.Null(rows[0].ToolTip);
            Assert.Equal("in the world and holding its cell", rows[1].ToolTip);
            Assert.Null(rows[2].ToolTip);

            Assert.Null(RowsUnder(host, 0, 1)[1].ToolTip);
        });
    }

    [Fact]
    public void ANoteThatGoesAwayTakesItsTooltipWithIt()
    {
        // The same guard in the other direction: a row that stops having anything
        // to say must not keep yesterday's sentence on hover.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);

            panel.Update([
                new DebugRow("Unit", "alive", "yes", "in the world and holding its cell") { Section = Movement }]);
            Assert.Equal("in the world and holding its cell", RowsUnder(host, 0, 0)[0].ToolTip);

            panel.Update([new DebugRow("Unit", "alive", "no") { Section = Movement }]);

            Assert.Equal(1, panel.Rebuilds);
            Assert.Null(RowsUnder(host, 0, 0)[0].ToolTip);
        });
    }

    [Fact]
    public void AHeadingSaysHowManyRowsItHidesAndNothingItCannotKnow()
    {
        // Group names come from sources this host has never heard of, so the
        // heading tooltip says only what is true of any group: how many rows are
        // under it and that it folds.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            Assert.Equal("3 rows -- click to fold", HeadingOf(host, 0, 0).ToolTip);
            Assert.Equal("2 rows -- click to fold", HeadingOf(host, 0, 1).ToolTip);
        });
    }

    [Fact]
    public void ASectionSaysHowManyGroupsItHidesAndNothingItCannotKnow()
    {
        // The same rule one level up, counting groups rather than rows. A section
        // caption is a name decided upstream, so this host says nothing about
        // what the layer IS either.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(SameNameInTwoSections());

            Assert.Equal("1 group -- click to fold", SectionHeadingOf(host, 0).ToolTip);

            panel.Update([
                .. Rows("yes", "1,1 (#12)"),
                new DebugRow("Unit", "squad", "guard") { Section = Tactics }]);

            Assert.Equal("2 groups -- click to fold", SectionHeadingOf(host, 0).ToolTip);
            Assert.Equal("1 group -- click to fold", SectionHeadingOf(host, 1).ToolTip);
        });
    }

    [Fact]
    public void AnEmptyPanelIsEmptyRatherThanAStaleOne()
    {
        // Nothing selected is a real state and the commonest one on startup. The
        // rows a previous selection left behind must not survive it.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            panel.Update([]);

            Assert.Empty(host.Children);
        });
    }
}
