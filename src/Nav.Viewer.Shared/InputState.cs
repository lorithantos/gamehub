using System.Numerics;

namespace Nav.Viewer;

[Flags]
public enum ViewerKeys
{
    None = 0,
    Space = 1,
    R = 2,
}

[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
}

/// <summary>
/// What the user did during one frame.
/// </summary>
/// <remarks>
/// The pressed flags are <em>edges</em>: true only on the frame a transition
/// happened, and true for exactly one frame.
/// </remarks>
public readonly struct InputState(Vector2 mousePosition, ViewerKeys keysPressed, MouseButtons buttonsPressed)
{
    public Vector2 MousePosition { get; } = mousePosition;

    public ViewerKeys KeysPressed { get; } = keysPressed;

    public MouseButtons ButtonsPressed { get; } = buttonsPressed;

    public bool IsPressed(ViewerKeys key) => (KeysPressed & key) != 0;

    public bool IsPressed(MouseButtons button) => (ButtonsPressed & button) != 0;
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

    public void SetMousePosition(Vector2 position) => _mousePosition = position;

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
        var state = new InputState(_mousePosition, _keysPressed, _buttonsPressed);
        _keysPressed = ViewerKeys.None;
        _buttonsPressed = MouseButtons.None;
        return state;
    }
}
