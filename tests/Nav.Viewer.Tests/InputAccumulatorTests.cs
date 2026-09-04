using System.Numerics;

namespace Nav.Viewer.Tests;

/// <summary>
/// Edge derivation, which is the one piece of input handling shared by every
/// host. If this were per-host code, three copies would drift.
/// </summary>
/// <remarks>
/// Two members and two kinds of test. <see cref="InputAccumulator.Drain"/> is the
/// frame boundary, so anything about an edge lasting exactly one frame goes
/// through it; <see cref="InputAccumulator.Snapshot"/> only looks, so a test that
/// wanted to see the state and never cared about consuming it asks that instead
/// and no longer depends on the drain to mean what it means.
/// </remarks>
public sealed class InputAccumulatorTests
{
    [Fact]
    public void ARisingEdgeIsReportedOnceAndThenNotAgain()
    {
        var input = new InputAccumulator();

        input.SetKeyState(ViewerKeys.Space, down: true);
        Assert.True(input.Drain().IsPressed(ViewerKeys.Space));

        // Still held, but no longer a transition.
        input.SetKeyState(ViewerKeys.Space, down: true);
        Assert.False(input.Drain().IsPressed(ViewerKeys.Space));
    }

    [Fact]
    public void RepeatedDownReportsNoFurtherEdgeEvenWithoutADrainBetween()
    {
        // WPF auto-repeat: KeyDown fires roughly thirty times a second while a
        // key is held, and several can land inside one frame.
        var input = new InputAccumulator();

        for (var i = 0; i < 30; i++)
        {
            input.SetKeyState(ViewerKeys.Space, down: true);
        }

        Assert.True(input.Drain().IsPressed(ViewerKeys.Space));
        Assert.False(input.Drain().IsPressed(ViewerKeys.Space));
    }

    [Fact]
    public void ReleasingRearmsTheEdge()
    {
        var input = new InputAccumulator();

        input.SetKeyState(ViewerKeys.R, down: true);
        input.Drain();
        input.SetKeyState(ViewerKeys.R, down: false);
        input.Drain();
        input.SetKeyState(ViewerKeys.R, down: true);

        Assert.True(input.Drain().IsPressed(ViewerKeys.R));
    }

    [Fact]
    public void KeysAreTrackedIndependently()
    {
        var input = new InputAccumulator();

        input.SetKeyState(ViewerKeys.Space, down: true);
        input.SetKeyState(ViewerKeys.R, down: true);

        var state = input.Snapshot();

        Assert.True(state.IsPressed(ViewerKeys.Space));
        Assert.True(state.IsPressed(ViewerKeys.R));
    }

    [Fact]
    public void MouseButtonsBehaveLikeKeys()
    {
        var input = new InputAccumulator();

        input.SetMouseButtonState(MouseButtons.Left, down: true);
        Assert.True(input.Drain().IsPressed(MouseButtons.Left));
        Assert.False(input.Drain().IsPressed(MouseButtons.Left));

        input.SetMouseButtonState(MouseButtons.Left, down: false);
        input.SetMouseButtonState(MouseButtons.Left, down: true);
        Assert.True(input.Drain().IsPressed(MouseButtons.Left));
    }

    [Fact]
    public void DrainPreservesThePositionButEmptiesTheEdges()
    {
        var input = new InputAccumulator();
        input.SetMousePosition(new Vector2(42, 17));
        input.SetMouseButtonState(MouseButtons.Right, down: true);

        var first = input.Drain();
        var second = input.Drain();

        Assert.Equal(new Vector2(42, 17), first.MousePosition);
        Assert.Equal(new Vector2(42, 17), second.MousePosition);
        Assert.True(first.IsPressed(MouseButtons.Right));
        Assert.False(second.IsPressed(MouseButtons.Right));
    }

    /// <summary>
    /// The property the split exists to create, and the one the old shape could
    /// not offer: two reads in a row answer the same, and the frame the host has
    /// not taken yet is still there afterwards for it to take.
    /// </summary>
    [Fact]
    public void SnapshotAnswersTheSameTwiceAndLeavesTheFrameForTheHost()
    {
        var input = new InputAccumulator();
        input.SetMousePosition(new Vector2(8, 9));
        input.SetKeyState(ViewerKeys.Space, down: true);
        input.SetMouseButtonState(MouseButtons.Left, down: true);

        var first = input.Snapshot();
        var second = input.Snapshot();

        Assert.Equal(first.MousePosition, second.MousePosition);
        Assert.Equal(first.KeysPressed, second.KeysPressed);
        Assert.Equal(first.ButtonsPressed, second.ButtonsPressed);
        Assert.Equal(first.ButtonsDown, second.ButtonsDown);
        Assert.True(second.IsPressed(ViewerKeys.Space));
        Assert.True(second.IsPressed(MouseButtons.Left));

        var frame = input.Drain();

        Assert.True(frame.IsPressed(ViewerKeys.Space));
        Assert.True(frame.IsPressed(MouseButtons.Left));
    }

    [Fact]
    public void AFreshAccumulatorReportsNothingPressed()
    {
        var state = new InputAccumulator().Snapshot();

        Assert.Equal(ViewerKeys.None, state.KeysPressed);
        Assert.Equal(MouseButtons.None, state.ButtonsPressed);
        Assert.Equal(Vector2.Zero, state.MousePosition);
    }
}
