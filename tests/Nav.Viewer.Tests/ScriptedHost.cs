using System.Numerics;

namespace Nav.Viewer.Tests;

/// <summary>
/// One frame of a scripted session: how long it lasted, and what was held down
/// while it did.
/// </summary>
/// <remarks>
/// HELD state, not edges — the same thing a real host reports. A frame descriptor
/// that said "Space was pressed" would let the test invent the edge semantics it
/// is supposed to be checking.
/// </remarks>
public sealed record ScriptedFrame(
    float Dt = 1.0f / 60.0f,
    Vector2 Mouse = default,
    ViewerKeys KeysDown = ViewerKeys.None,
    MouseButtons ButtonsDown = MouseButtons.None);

/// <summary>
/// A third implementation of <see cref="IViewerHost"/>, with no window.
/// </summary>
/// <remarks>
/// This type is the point of the exercise, and it exists before the second real
/// host does. One implementation of an interface is a guess, two are a
/// hypothesis, three are a design — and if the host contract had smuggled in any
/// windowing concept, this class could not compile in a project that references
/// no graphics assembly at all.
/// <para>
/// It honours the same contract the real hosts do, in the same order: feed held
/// state to an <see cref="InputAccumulator"/>, snapshot it, Update with a raw
/// delta, then Render between the renderer's begin and end frame. The
/// accumulator is the real one, so edge derivation — including the auto-repeat
/// rule — is exercised here rather than simulated.
/// </para>
/// </remarks>
public sealed class ScriptedHost(IReadOnlyList<ScriptedFrame> frames, RecordingRenderer renderer) : IViewerHost
{
    private static readonly ViewerKeys[] Keys =
        [.. Enum.GetValues<ViewerKeys>().Where(k => k != ViewerKeys.None)];

    private readonly InputAccumulator _input = new();

    public int FramesRun { get; private set; }

    public void Run(IViewerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        foreach (var frame in frames)
        {
            _input.SetMousePosition(frame.Mouse);

            // Every key the enum defines, not a list written out here. A hand
            // written list silently stops reporting a key added later: the pace
            // key was added and two tests failed with the app never seeing a
            // press, which looks exactly like a broken feature.
            foreach (var key in Keys)
            {
                _input.SetKeyState(key, (frame.KeysDown & key) != 0);
            }

            foreach (var button in new[] { MouseButtons.Left, MouseButtons.Right })
            {
                _input.SetMouseButtonState(button, (frame.ButtonsDown & button) != 0);
            }

            app.Update(_input.Snapshot(), frame.Dt);
            app.Render(renderer);
            FramesRun++;
        }
    }

    public void Dispose()
    {
        // Nothing to release: that is the whole claim this class is making.
    }

    /// <summary>N frames of nothing happening.</summary>
    public static ScriptedFrame[] Idle(int count, float dt = 1.0f / 60.0f) =>
        [.. Enumerable.Repeat(new ScriptedFrame(dt), count)];
}
