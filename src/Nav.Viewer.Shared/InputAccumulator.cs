using System.Numerics;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// Turns whatever a host reports into the per-frame snapshot the app consumes.
/// </summary>
/// <remarks>
/// The one design decision here does all the work: <b>hosts report held state,
/// never edges.</b> This class derives the edges, so the "pressed this frame"
/// rule is one piece of tested code rather than two per-host reimplementations
/// that drift apart.
/// <para>
/// It reconciles two opposite input models. WPF is evented and REPEATS: holding
/// Space fires KeyDown thirty times a second, and a host turning each event into
/// an edge would toggle run/pause every frame.
/// </para>
/// <para>
/// Here a repeat is a no-op, because the key was already recorded as down.
/// Raylib is polled, so its host samples <c>IsKeyDown</c> once per frame and
/// feeds the same method.
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
    /// Last call before <see cref="Drain"/> wins, and the position is never
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
    /// What the accumulator is holding right now: the pointer, the edges derived
    /// since the last <see cref="Drain"/>, and whatever is still down. Answers
    /// the same thing twice in a row, because it moves nothing.
    /// </summary>
    [Observes]
    public InputState Snapshot() =>
        new(_mousePosition, _keysPressed, _buttonsPressed, _buttonsDown);

    /// <summary>
    /// Takes the frame's input and empties the edges. Held state and the mouse
    /// position survive; the pressed bits do not, which is what makes "pressed
    /// this frame" true for exactly one <see cref="IViewerApp.Update"/>.
    /// </summary>
    /// <remarks>
    /// <b>The verb is named as a verb because it is one.</b> This was
    /// <c>Snapshot</c>, and the drain was a side effect of being read: a second
    /// caller got an empty frame and the name promised them it could not happen.
    /// The innocent name belongs to the innocent member, so <c>Snapshot</c> kept
    /// it and stopped causing, while what is left here says on the tin that it
    /// takes something away.
    /// <para>
    /// A host calls this once per frame and passes the result straight to the
    /// app. Anything else -- a panel, a test looking at the state -- wants
    /// <see cref="Snapshot"/>.
    /// </para>
    /// </remarks>
    public InputState Drain()
    {
        var state = Snapshot();
        _keysPressed = ViewerKeys.None;
        _buttonsPressed = MouseButtons.None;
        return state;
    }
}
