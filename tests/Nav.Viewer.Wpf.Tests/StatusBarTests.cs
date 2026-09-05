using System.Globalization;
using System.Windows;
using System.Windows.Controls;

using Nav.Viewer;
using Nav.Viewer.Models;

using NavGrid = Nav.Core.Grid;

namespace Nav.Viewer.Wpf.Tests;

/// <summary>
/// The status line's box: how wide it is, what it is allowed to hide, and
/// whether the tick is still on screen at the size the guard world opens at.
/// </summary>
/// <remarks>
/// <b>The fault this file exists for, in the words it was reported in: "still
/// can't see ticks".</b> The bar sat in column 0 and was sized to the MAP's
/// pixel width while the inspector took column 1 beside it, and the TextBlock
/// trimmed with an ellipsis -- so the line was cut roughly in half and
/// everything from the tick rightwards, the run state, the pace, the selection
/// count and every key hint, was silently gone.
/// <para>
/// <b>What is asserted here and what is asserted elsewhere.</b> The STRING is
/// data and is pinned in <c>Nav.Viewer.Tests</c>, including the two tests that
/// hold its length constant. What is here is the box it is drawn in, which needs
/// PresentationFramework in the process and so cannot live there -- the same
/// split, and the same reason, as <see cref="InspectorViewTests"/>.
/// </para>
/// </remarks>
public sealed class StatusBarTests
{
    /// <summary>The guard world's shape, without needing the guard world.</summary>
    private static GridLayout Layout(int width = 49, int height = 33)
    {
        var lines = new List<string> { "type octile", $"height {height}", $"width {width}", "map" };
        for (var y = 0; y < height; y++)
        {
            lines.Add(new string('.', width));
        }

        return GridLayout.Fit(
            NavGrid.FromMapText(string.Join('\n', lines)),
            Program.MaxMapPixels,
            Program.MaxMapPixels - Program.StatusHeight);
    }

    /// <summary>The viewer the fault was reported against, composed as the exe composes it.</summary>
    private static ViewerApp GuardWorld()
    {
        var (session, sources, eyes) = Program.Compose("guard-retreat");
        return new ViewerApp(
            session,
            Program.MaxMapPixels,
            Program.MaxMapPixels - Program.StatusHeight,
            keys: null,
            sources,
            eyes);
    }

    /// <summary>The width the text has to draw in, inside the bar's padding.</summary>
    private static double Inner(MainWindow window) =>
        window.StatusBar.Width - window.StatusBar.Padding.Left - window.StatusBar.Padding.Right;

    /// <summary>How wide the whole string would be if nothing broke it.</summary>
    private static double Unbroken(TextBlock status)
    {
        var wrapping = status.TextWrapping;
        status.TextWrapping = TextWrapping.NoWrap;
        status.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = status.DesiredSize.Width;
        status.TextWrapping = wrapping;
        return width;
    }

    /// <summary>Where an element ended up in its parent, once the grid has been arranged.</summary>
    private static Rect Box(FrameworkElement element)
    {
        // TranslatePoint wants a UIElement, and Parent is typed as a
        // DependencyObject; the grid these two sit in is one.
        var origin = element.TranslatePoint(new Point(0, 0), (UIElement)element.Parent);
        return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
    }

    /// <summary>The window laid out at the size it asks for, so positions are real.</summary>
    private static MainWindow Arranged(ViewerApp app)
    {
        var window = new MainWindow();
        window.SizeTo(app.Layout, dpiScale: 1.0);
        window.Status.Text = app.StatusText;

        window.Chrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        window.Chrome.Arrange(new Rect(new Point(0, 0), window.Chrome.DesiredSize));
        window.Chrome.UpdateLayout();

        return window;
    }

    [Fact]
    public void TheInspectorDoesNotCoverTheEndOfTheStatusLine()
    {
        // The bar spans both columns, so it runs UNDER the panel's column. A
        // panel that reached into the bar's row was painted over the right-hand
        // end of the first wrapped line -- and being declared after the bar it
        // won, so thirty-nine characters of key hints were simply not there. The
        // panel's top is asserted too: an explicit height stretched across two
        // rows is centred in them, which held the panel half the bar's height
        // below the map and put a strip of black above it.
        Sta.Run(() =>
        {
            var window = Arranged(GuardWorld());

            var bar = Box(window.StatusBar);
            var panel = Box(window.InspectorPanel);

            // The AREA they share, not Rect.IntersectsWith, which counts a
            // shared edge as an intersection and so calls these two overlapping
            // however well they are stacked -- the bar's top IS the panel's
            // bottom when this is right, and that is the arrangement, not the
            // fault.
            var overlap = Rect.Intersect(bar, panel);

            Assert.False(
                overlap.Width > 0 && overlap.Height > 0,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the status bar at {bar} and the inspector at {panel} overlap in {overlap}"));

            Assert.True(
                panel.Top == 0,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the inspector starts {panel.Top} points below the top of the grid, not level with the map"));
        });
    }

    [Fact]
    public void TheStatusLineIsAsWideAsTheMapAndThePanelTogether()
    {
        // The whole window, not the map. It used to be the map's width, which is
        // the bug: the panel's column sat beside a line that had been cut to
        // make room for it.
        Sta.Run(() =>
        {
            var window = new MainWindow();
            window.SizeTo(Layout(), dpiScale: 1.0);

            Assert.Equal(
                window.Surface.Width + window.InspectorPanel.Width,
                window.StatusBar.Width);

            // And that is genuinely wider than the map, so the assertion above
            // is not two names for the same number.
            Assert.True(
                window.StatusBar.Width > window.Surface.Width,
                $"the bar is {window.StatusBar.Width} and the map is {window.Surface.Width}");
        });
    }

    [Fact]
    public void AWiderPanelWidensTheStatusLine()
    {
        // The panel's width is read from the panel rather than restated as a
        // constant. A 260 written in two places is right until somebody widens
        // the panel, and then the line is clipped again by a file nobody
        // touched.
        Sta.Run(() =>
        {
            var window = new MainWindow();
            var layout = Layout();

            window.SizeTo(layout, dpiScale: 1.0);
            var before = window.StatusBar.Width;

            window.InspectorPanel.Width += 140;
            window.SizeTo(layout, dpiScale: 1.0);

            Assert.Equal(before + 140, window.StatusBar.Width);
        });
    }

    [Fact]
    public void TheBarSpansBothColumnsRatherThanPushingTheWindowWider()
    {
        // The other half of widening it. A bar this wide sitting in column 0
        // alone would not be clipped -- it would make column 0 that wide and
        // shove the panel off the end, giving a window half again as wide as the
        // map with a strip of black beside the surface.
        Sta.Run(() =>
        {
            var window = new MainWindow();
            window.SizeTo(Layout(), dpiScale: 1.0);

            window.Chrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.Equal(
                window.Surface.Width + window.InspectorPanel.Width,
                window.Chrome.DesiredSize.Width);
        });
    }

    [Fact]
    public void NothingInTheStatusLineIsHiddenBehindAnEllipsis()
    {
        // THE FAULT, as a property. Trimming is what ate the tail: it draws what
        // fits, puts an ellipsis where the rest was, and there is no gesture
        // that opens it. Wrapping costs a second line and hides nothing, which
        // is the same call InspectorView makes for a row's value.
        Sta.Run(() =>
        {
            var window = new MainWindow();

            Assert.Equal(TextTrimming.None, window.Status.TextTrimming);
            Assert.Equal(TextWrapping.Wrap, window.Status.TextWrapping);
        });
    }

    [Fact]
    public void TheWholeGuardWorldStatusLineHasRoomToBeDrawn()
    {
        // The line is far wider than the window and always was -- 222 characters
        // of Consolas 12 is about 1464 points against the 1240 the map and the
        // panel come to -- so "it fits on one line" is not available at any
        // width. What is asserted instead is that the bar it wraps inside has
        // room for every line the text needs.
        Sta.Run(() =>
        {
            var app = GuardWorld();
            var window = new MainWindow();
            window.SizeTo(app.Layout, dpiScale: 1.0);
            window.Status.Text = app.StatusText;

            var inner = Inner(window);
            var needed = Math.Ceiling(Unbroken(window.Status) / inner);

            window.Status.Measure(new Size(inner, double.PositiveInfinity));
            var drawn = window.Status.DesiredSize.Height;

            Assert.True(
                drawn >= needed * window.Status.FontSize,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{app.StatusText.Length} characters need {needed} lines of {inner} points " +
                    $"and the bar drew {drawn} points of height"));

            // And it needed more than one, so a bar that had quietly dropped
            // everything past the first line would not pass this.
            Assert.True(needed > 1, $"the line fitted on one line, so this proves nothing: {needed}");
        });
    }

    [Fact]
    public void TheTickIsOnTheFirstLine()
    {
        // The complaint was about the CLOCK, and a clock that has scrolled onto
        // a second line under the map is only half fixed. The tick sits about a
        // hundred characters in, which is inside one line's worth -- this is
        // what goes red if it is ever moved to the end of the string.
        Sta.Run(() =>
        {
            var app = GuardWorld();
            var window = new MainWindow();
            window.SizeTo(app.Layout, dpiScale: 1.0);

            // The field itself, number and padding included, rather than the
            // word: "nodes/tick" carries the word too, and searching for that
            // measured a prefix ending eighteen characters SHORT of the clock --
            // which passed happily with the tick moved to the far end of the
            // line. Asking the app what tick it is on is what makes this an
            // assertion about the clock.
            var field = string.Create(CultureInfo.InvariantCulture, $"tick {app.CurrentTick,6}");
            var at = app.StatusText.IndexOf(field, StringComparison.Ordinal);
            Assert.True(at >= 0, $"no tick field in the status line at all: {app.StatusText}");

            window.Status.Text = app.StatusText[..(at + field.Length)];

            Assert.True(
                Unbroken(window.Status) <= Inner(window),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the tick ends {Unbroken(window.Status)} points in, past the {Inner(window)} of a line"));
        });
    }
}
