using System.Numerics;

namespace Nav.Viewer.Tests;

/// <summary>
/// Edge derivation, which is the one piece of input handling shared by every
/// host. If this were per-host code, three copies would drift.
/// </summary>
public sealed class InputAccumulatorTests
{
    [Fact]
    public void ARisingEdgeIsReportedOnceAndThenNotAgain()
    {
        var input = new InputAccumulator();

        input.SetKeyState(ViewerKeys.Space, down: true);
        Assert.True(input.Snapshot().IsPressed(ViewerKeys.Space));

        // Still held, but no longer a transition.
        input.SetKeyState(ViewerKeys.Space, down: true);
        Assert.False(input.Snapshot().IsPressed(ViewerKeys.Space));
    }

    [Fact]
    public void RepeatedDownReportsNoFurtherEdgeEvenWithoutASnapshotBetween()
    {
        // WPF auto-repeat: KeyDown fires roughly thirty times a second while a
        // key is held, and several can land inside one frame.
        var input = new InputAccumulator();

        for (var i = 0; i < 30; i++)
        {
            input.SetKeyState(ViewerKeys.Space, down: true);
        }

        Assert.True(input.Snapshot().IsPressed(ViewerKeys.Space));
        Assert.False(input.Snapshot().IsPressed(ViewerKeys.Space));
    }

    [Fact]
    public void ReleasingRearmsTheEdge()
    {
        var input = new InputAccumulator();

        input.SetKeyState(ViewerKeys.R, down: true);
        input.Snapshot();
        input.SetKeyState(ViewerKeys.R, down: false);
        input.Snapshot();
        input.SetKeyState(ViewerKeys.R, down: true);

        Assert.True(input.Snapshot().IsPressed(ViewerKeys.R));
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
        Assert.True(input.Snapshot().IsPressed(MouseButtons.Left));
        Assert.False(input.Snapshot().IsPressed(MouseButtons.Left));

        input.SetMouseButtonState(MouseButtons.Left, down: false);
        input.SetMouseButtonState(MouseButtons.Left, down: true);
        Assert.True(input.Snapshot().IsPressed(MouseButtons.Left));
    }

    [Fact]
    public void SnapshotPreservesThePositionButDrainsTheEdges()
    {
        var input = new InputAccumulator();
        input.SetMousePosition(new Vector2(42, 17));
        input.SetMouseButtonState(MouseButtons.Right, down: true);

        var first = input.Snapshot();
        var second = input.Snapshot();

        Assert.Equal(new Vector2(42, 17), first.MousePosition);
        Assert.Equal(new Vector2(42, 17), second.MousePosition);
        Assert.True(first.IsPressed(MouseButtons.Right));
        Assert.False(second.IsPressed(MouseButtons.Right));
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
