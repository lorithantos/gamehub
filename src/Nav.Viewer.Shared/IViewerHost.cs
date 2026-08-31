namespace Nav.Viewer;

/// <summary>
/// A window and a loop. Implementations own both; the app owns neither.
/// </summary>
/// <remarks>
/// The interface is trivial and the contract below is the actual design, because
/// the two hosts have opposite control flow. Raylib's loop belongs to the
/// application (<c>while (!WindowShouldClose())</c>); WPF's belongs to the
/// framework, with per-frame work hanging off a compositor callback. An
/// abstraction over drawing alone would not have survived that difference — this
/// one has to invert control, which is the interesting half of the experiment.
/// <para>
/// <b>The contract every host is held to:</b>
/// </para>
/// <list type="number">
/// <item><description><see cref="Run"/> opens a window and returns only when the
/// user closes it.</description></item>
/// <item><description>Per frame, exactly once, in this order: snapshot input →
/// <see cref="IViewerApp.Update"/> → <see cref="IViewerApp.Render"/>, with
/// Render bracketed by the renderer's begin and end frame.</description></item>
/// <item><description><c>deltaSeconds</c> is <b>raw</b> wall-clock time since the
/// previous Update, and never negative. Hosts must not clamp, smooth or filter
/// it: <c>FixedTimestep.MaxStepsPerFrame</c> is already the circuit breaker, and
/// a second one in a host would make the two diverge.</description></item>
/// <item><description>Edge flags in <see cref="InputState"/> are true iff the
/// transition happened since the previous Update, and true for exactly one
/// frame.</description></item>
/// <item><description>Update and Render are never re-entrant, and always on one
/// thread.</description></item>
/// </list>
/// <para>
/// Window size and title are host constructor arguments, not app concerns. Both
/// hosts size themselves from the same <see cref="GridLayout.Fit"/> call, so
/// their geometry comes from one code path rather than two that agree by
/// coincidence.
/// </para>
/// </remarks>
public interface IViewerHost : IDisposable
{
    void Run(IViewerApp app);
}

/// <summary>
/// The application, as a host sees it: something to tick, something to draw, and
/// a line of text to display however the host displays text.
/// </summary>
public interface IViewerApp
{
    /// <summary>
    /// The status line. A <em>string</em>, deliberately — the app decides what it
    /// says, the host decides how it looks.
    /// </summary>
    string StatusText { get; }

    void Update(in InputState input, float deltaSeconds);

    void Render(IRenderer renderer);
}
