using System.Numerics;

namespace Nav.Viewer;

/// <summary>
/// The only keyboard commands the viewer has an opinion about, named for what
/// they do rather than for the keycap.
/// </summary>
/// <remarks>
/// Flags, because one snapshot has to be able to carry several at once -- see
/// <see cref="InputState.KeysPressed"/>. Which physical key produces which
/// member is each host's business and appears nowhere in the shared project:
/// <see cref="Step"/> happens to be S in both hosts, and moving it would not
/// touch a line of this file. <see cref="Space"/> and <see cref="R"/> are named
/// after their key only because no better verb existed; treat the summary as the
/// meaning.
/// </remarks>
[Flags]
public enum ViewerKeys
{
    /// <summary>
    /// The zero value a <see cref="FlagsAttribute"/> enum needs, and what
    /// <see cref="InputState.KeysPressed"/> holds on a frame where no key went
    /// down -- which is nearly every frame.
    /// </summary>
    None = 0,

    /// <summary>
    /// Toggle between running and paused. The key that makes edge detection
    /// worth having: held down it must toggle once, not once per frame.
    /// </summary>
    Space = 1,

    /// <summary>
    /// Restart or regroup, depending on what is loaded: a replay goes back to
    /// tick zero with its clock stopped; a live map instead orders every unit,
    /// selected or not, to unit 0's cell.
    /// </summary>
    R = 2,

    /// <summary>
    /// Advance exactly one tick and stay paused. Pressing it while running
    /// pauses first, so a burst can be caught mid-flight and then walked
    /// forward a tick at a time.
    /// </summary>
    Step = 4,
}

/// <summary>
/// The two mouse buttons the viewer reads, in the RTS convention: left selects,
/// right orders.
/// </summary>
/// <remarks>
/// Flags, like <see cref="ViewerKeys"/>, and reported by <see cref="InputState"/>
/// twice over -- once as an edge in <see cref="InputState.ButtonsPressed"/> and
/// once as held state in <see cref="InputState.ButtonsDown"/> -- because a drag
/// needs to know both that it began and that it is still going.
/// </remarks>
[Flags]
public enum MouseButtons
{
    /// <summary>
    /// The zero value a <see cref="FlagsAttribute"/> enum needs. In
    /// <see cref="InputState.ButtonsPressed"/> it means nothing was clicked this
    /// frame; in <see cref="InputState.ButtonsDown"/> it means nothing is held,
    /// which is how a drag learns it has been released.
    /// </summary>
    None = 0,

    /// <summary>
    /// Select. The press picks the nearest unit at once; if the pointer then
    /// moves more than a few pixels while the button stays down, the release
    /// replaces that with everything inside the drag box -- and boxing empty
    /// ground clears the selection.
    /// </summary>
    Left = 1,

    /// <summary>
    /// Order: send the current selection to the cell under the pointer. Ignored
    /// off the map, and a no-op with nothing selected.
    /// </summary>
    Right = 2,
}

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

/// <summary>
/// Turns whatever a host reports into the per-frame snapshot the app consumes.
/// </summary>
/// <remarks>
/// The one design decision here does all the work: <b>hosts report held state,
/// never edges.</b> This class derives the edges, so the "pressed this frame"
/// rule is one piece of tested code rather than two per-host reimplementations
/// that drift apart.
/// <para>
/// It reconciles two opposite input models. WPF is evented and repeats: holding
/// Space fires KeyDown around thirty times a second, and a host translating each
/// event into an edge would toggle run/pause every frame. Here a repeat is a
/// no-op, because the key was already recorded as down. Raylib is polled, so its
/// host samples <c>IsKeyDown</c> once per frame and feeds the same method.
/// </para>
/// <para>
/// It also dissolves raylib's <c>CBool</c> problem. That type converts to
/// <c>bool</c> implicitly but cannot be an operand of <c>&amp;&amp;</c>; passed
/// as a single argument, the conversion applies and the nested-<c>if</c>
/// workaround the old viewer needed disappears.
/// </para>
/// <para>
/// Known and accepted: sampling once per frame misses a key tapped and released
/// inside one 16ms frame, which raylib's own edge detection would have caught.
/// </para>
/// </remarks>
public sealed class InputAccumulator
{
    private Vector2 _mousePosition;
    private ViewerKeys _keysDown;
    private ViewerKeys _keysPressed;
    private MouseButtons _buttonsDown;
    private MouseButtons _buttonsPressed;

    /// <summary>
    /// Records where the pointer is, in the physical pixels the map is drawn in.
    /// Last call before <see cref="Snapshot"/> wins, and the position is never
    /// drained -- it persists across frames until a host reports a new one.
    /// </summary>
    public void SetMousePosition(Vector2 position) => _mousePosition = position;

    /// <summary>
    /// Reports <paramref name="key"/>'s <em>current</em> state, as often as the
    /// host likes. Calling it with <paramref name="down"/> true for a key already
    /// recorded as down is a no-op, which is precisely what turns WPF's
    /// thirty-events-a-second auto-repeat into one edge instead of thirty
    /// run/pause toggles.
    /// </summary>
    public void SetKeyState(ViewerKeys key, bool down)
    {
        if (down)
        {
            if ((_keysDown & key) == 0)
            {
                _keysPressed |= key;
            }

            _keysDown |= key;
        }
        else
        {
            _keysDown &= ~key;
        }
    }

    /// <summary>
    /// The button twin of <see cref="SetKeyState"/>, with the same
    /// a-repeat-is-a-no-op rule -- so an evented host may call it on every
    /// <c>MouseDown</c> and a polled host once per frame, and the derived edge is
    /// identical.
    /// </summary>
    public void SetMouseButtonState(MouseButtons button, bool down)
    {
        if (down)
        {
            if ((_buttonsDown & button) == 0)
            {
                _buttonsPressed |= button;
            }

            _buttonsDown |= button;
        }
        else
        {
            _buttonsDown &= ~button;
        }
    }

    /// <summary>
    /// Takes the frame's snapshot and drains the edges. Held state and the mouse
    /// position survive; the pressed bits do not.
    /// </summary>
    public InputState Snapshot()
    {
        var state = new InputState(_mousePosition, _keysPressed, _buttonsPressed, _buttonsDown);
        _keysPressed = ViewerKeys.None;
        _buttonsPressed = MouseButtons.None;
        return state;
    }
}
