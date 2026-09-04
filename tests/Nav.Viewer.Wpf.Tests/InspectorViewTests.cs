using System.Windows;
using System.Windows.Controls;

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
/// </remarks>
public sealed class InspectorViewTests
{
    /// <summary>A panel's worth of rows, with the values a caller supplies.</summary>
    private static DebugRow[] Rows(string alive, string cell) =>
    [
        new("Unit", "id", "0"),
        new("Unit", "alive", alive, "in the world and holding its cell"),
        new("Unit", "cell", cell),
        new("Progress", "retry gate", "open", "it may start a search this tick"),
        new("Progress", "stalled", "no"),
    ];

    /// <summary>The row grids under a heading, in the order they are laid out.</summary>
    private static List<Grid> RowsUnder(Panel host, int group) =>
        [.. ((StackPanel)host.Children[(group * 2) + 1]).Children.OfType<Grid>()];

    private static StackPanel BodyOf(Panel host, int group) => (StackPanel)host.Children[(group * 2) + 1];

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

            var before = RowsUnder(host, 0);
            var headingBefore = host.Children[0];

            panel.Update(Rows("no", "2,1 (#13)"));

            Assert.Equal(1, panel.Rebuilds);
            Assert.Same(headingBefore, host.Children[0]);
            Assert.Equal(before.Count, RowsUnder(host, 0).Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.Same(before[i], RowsUnder(host, 0)[i]);
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

            List<DebugRow> longer = [.. Rows("yes", "1,1 (#12)"), new("Plan", "cells", "17", "one per tick")];
            panel.Update(longer);

            Assert.Equal(2, panel.Rebuilds);
            Assert.Equal(3, host.Children.Count / 2);
        });
    }

    [Fact]
    public void EveryGroupStartsUnfolded()
    {
        // Today's behaviour, kept. Folding is the reader's tool; a panel that
        // opened shut would make them find that out.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));

            Assert.False(panel.IsFolded("Unit"));
            Assert.False(panel.IsFolded("Progress"));
            Assert.Equal(Visibility.Visible, BodyOf(host, 0).Visibility);
            Assert.Equal(Visibility.Visible, BodyOf(host, 1).Visibility);
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

            panel.Fold("Unit", shut: true);
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0).Visibility);

            panel.Update(Rows("no", "2,1 (#13)"));

            Assert.True(panel.IsFolded("Unit"));
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0).Visibility);

            // Its neighbour was never folded and must not have been dragged shut.
            Assert.False(panel.IsFolded("Progress"));
            Assert.Equal(Visibility.Visible, BodyOf(host, 1).Visibility);
        });
    }

    [Fact]
    public void AFoldedGroupComesBackFoldedAfterARebuild()
    {
        // A shape change is a new element tree, and the fold state is kept by
        // group name rather than by element -- so a group that survives the
        // change comes back the way the reader left it.
        Sta.Run(() =>
        {
            var host = new StackPanel();
            var panel = new InspectorView(host);
            panel.Update(Rows("yes", "1,1 (#12)"));
            panel.Fold("Unit", shut: true);

            List<DebugRow> longer = [.. Rows("yes", "1,1 (#12)"), new("Plan", "cells", "17", "one per tick")];
            panel.Update(longer);

            Assert.Equal(2, panel.Rebuilds);
            Assert.True(panel.IsFolded("Unit"));
            Assert.Equal(Visibility.Collapsed, BodyOf(host, 0).Visibility);
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

            var rows = RowsUnder(host, 0);
            Assert.Null(rows[0].ToolTip);
            Assert.Equal("in the world and holding its cell", rows[1].ToolTip);
            Assert.Null(rows[2].ToolTip);

            Assert.Null(RowsUnder(host, 1)[1].ToolTip);
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

            panel.Update([new DebugRow("Unit", "alive", "yes", "in the world and holding its cell")]);
            Assert.Equal("in the world and holding its cell", RowsUnder(host, 0)[0].ToolTip);

            panel.Update([new DebugRow("Unit", "alive", "no")]);

            Assert.Equal(1, panel.Rebuilds);
            Assert.Null(RowsUnder(host, 0)[0].ToolTip);
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

            Assert.Equal("3 rows -- click to fold", ((Border)host.Children[0]).ToolTip);
            Assert.Equal("2 rows -- click to fold", ((Border)host.Children[2]).ToolTip);
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
