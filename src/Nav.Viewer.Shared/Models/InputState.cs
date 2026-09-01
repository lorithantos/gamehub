using System.Numerics;

namespace Nav.Viewer.Models;

/// <summary>
/// What the user did during one frame.
/// </summary>
/// <remarks>
/// The pressed flags are <em>edges</em>: true only on the frame a transition
/// happened, and true for exactly one frame. <see cref="ButtonsDown"/> is
/// <em>held</em> state, true for as long as the button is. The snapshot did not
/// carry held state until drag-box selection needed it -- the accumulator
/// always tracked it and threw it away, so growing the snapshot cost the hosts
/// nothing. That is the recorded seam finding: the drawing verbs held for
/// milestone 2, the input snapshot did not.
/// </remarks>
public readonly struct InputState(
    Vector2 mousePosition, ViewerKeys keysPressed, MouseButtons buttonsPressed, MouseButtons buttonsDown)
{
    /// <summary>
    /// Where the pointer is, in the physical pixels the map is drawn in, so
    /// <see cref="GridLayout.TryPick"/> takes it unconverted. It is level state,
    /// not an edge: the last position the host reported survives every snapshot,
    /// so it is still meaningful on a frame where the mouse did not move.
    /// </summary>
    public Vector2 MousePosition { get; } = mousePosition;

    /// <summary>
    /// Keys that went <em>down</em> since the previous frame. An edge: a key held
    /// for a hundred frames appears here on one of them and never again until it
    /// is released and pressed anew.
    /// </summary>
    public ViewerKeys KeysPressed { get; } = keysPressed;

    /// <summary>
    /// Buttons that went down since the previous frame, under the same one-frame
    /// edge rule as <see cref="KeysPressed"/>. This is a click, not a drag.
    /// </summary>
    public MouseButtons ButtonsPressed { get; } = buttonsPressed;

    /// <summary>
    /// Buttons still held -- true on the frame the press happened and on every
    /// frame after it until release.
    /// </summary>
    /// <remarks>
    /// The one field the seam had to grow for milestone 2. Drag-box selection
    /// needs "still going", and no amount of edge data answers that: an edge tells
    /// you a gesture started, never that it has not finished.
    /// </remarks>
    public MouseButtons ButtonsDown { get; } = buttonsDown;

    /// <summary>
    /// True on the single frame <paramref name="key"/> went down. Tested as a
    /// mask, so passing two keys at once asks "either of these".
    /// </summary>
    public bool IsPressed(ViewerKeys key) => (KeysPressed & key) != 0;

    /// <summary>
    /// True on the single frame <paramref name="button"/> went down -- the test a
    /// click uses, and the one a drag uses to find its anchor.
    /// </summary>
    public bool IsPressed(MouseButtons button) => (ButtonsPressed & button) != 0;

    /// <summary>
    /// True for as long as <paramref name="button"/> is held, the frame of the
    /// press included -- so a drag started by <see cref="IsPressed(MouseButtons)"/>
    /// can keep asking this until it comes back false.
    /// </summary>
    public bool IsDown(MouseButtons button) => (ButtonsDown & button) != 0;
}
