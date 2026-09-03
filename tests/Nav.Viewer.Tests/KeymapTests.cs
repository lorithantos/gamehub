using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// Which keycap does what, and the status line's promise about it.
/// </summary>
/// <remarks>
/// The map replaced a hard-coded switch per host and a hard-coded hint string in
/// the app — three copies of the same fact, free to drift. The defaults are
/// pinned here against the bindings they replaced, because a rebindable viewer
/// that changed anyone's keys on the way in would have been a regression sold as
/// a feature.
/// </remarks>
public sealed class KeymapTests
{
    private const int StatusHeight = 26;

    private static ViewerApp App(Keymap? keys = null)
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        return new ViewerApp(grid, GridLayout.Fit(grid, 1000, 1000 - StatusHeight), squad: 4, keys: keys);
    }

    [Fact]
    public void TheDefaultsAreTheKeysTheViewerAlreadyHad()
    {
        var map = Keymap.Default;

        Assert.Equal(ViewerKeys.Space, map.Action(PhysicalKey.Space));
        Assert.Equal(ViewerKeys.R, map.Action(PhysicalKey.R));
        Assert.Equal(ViewerKeys.Step, map.Action(PhysicalKey.S));
        Assert.Equal(ViewerKeys.Pace, map.Action(PhysicalKey.T));
        Assert.Equal(ViewerKeys.PanLeft, map.Action(PhysicalKey.Left));
        Assert.Equal(ViewerKeys.PanRight, map.Action(PhysicalKey.Right));
        Assert.Equal(ViewerKeys.PanUp, map.Action(PhysicalKey.Up));
        Assert.Equal(ViewerKeys.PanDown, map.Action(PhysicalKey.Down));
        Assert.Equal(ViewerKeys.ZoomIn, map.Action(PhysicalKey.Plus));
        Assert.Equal(ViewerKeys.ZoomOut, map.Action(PhysicalKey.Minus));
        Assert.Equal(ViewerKeys.ResetView, map.Action(PhysicalKey.Home));
    }

    [Fact]
    public void TheThreeNewActionsArriveBoundAndDoNothingYet()
    {
        // Carried so that the fog and LOS work changes behaviour and not wiring.
        var map = Keymap.Default;

        Assert.Equal(ViewerKeys.Viewpoint, map.Action(PhysicalKey.V));
        Assert.Equal(ViewerKeys.PathOverlay, map.Action(PhysicalKey.P));
        Assert.Equal(ViewerKeys.LosOverlay, map.Action(PhysicalKey.L));
    }

    [Fact]
    public void AKeyNothingIsBoundToAsksForNothing()
    {
        // Hosts feed this straight to the accumulator, so None has to be a
        // usable answer rather than something they must check for first.
        Assert.Equal(ViewerKeys.None, Keymap.Default.Action(PhysicalKey.None));
    }

    [Fact]
    public void ReboundLeavesTheMapItCameFromAlone()
    {
        var rebound = Keymap.Default.Rebound(PhysicalKey.P, ViewerKeys.Space);

        Assert.Equal(ViewerKeys.PathOverlay, Keymap.Default.Action(PhysicalKey.P));
        Assert.Equal(ViewerKeys.Space, rebound.Action(PhysicalKey.P));
    }

    [Fact]
    public void TheHintsAreGeneratedFromTheMapRatherThanWrittenOut()
    {
        Assert.Contains("SPACE pause", App().StatusText, StringComparison.Ordinal);

        // Pause moves to P. A literal hint would go on promising SPACE, and a
        // status line that is confidently wrong about a key is worse than one
        // that never mentioned it.
        var moved = App(Keymap.Default
            .Rebound(PhysicalKey.Space, ViewerKeys.None)
            .Rebound(PhysicalKey.P, ViewerKeys.Space));

        Assert.DoesNotContain("SPACE pause", moved.StatusText, StringComparison.Ordinal);
        Assert.Contains("P pause", moved.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionNothingReachesIsHintedAsUnbound()
    {
        var stripped = App(Keymap.Default.Rebound(PhysicalKey.T, ViewerKeys.None));

        Assert.Contains("- pace", stripped.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void RebindingTheHintDoesNotRebindTheKey()
    {
        // The app still hears ViewerKeys, whatever keycap produced them: the
        // translation is the host's single step, and ScriptedHost speaks the
        // action directly. This pins that the keymap did not quietly become a
        // second place the app filters input.
        var app = App(Keymap.Default.Rebound(PhysicalKey.P, ViewerKeys.Space));

        using var host = new ScriptedHost([new ScriptedFrame(KeysDown: ViewerKeys.Space)], new RecordingRenderer());
        host.Run(app);

        Assert.False(app.Running);
    }
}
