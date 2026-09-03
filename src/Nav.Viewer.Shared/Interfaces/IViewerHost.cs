namespace Nav.Viewer.Interfaces;

/// <summary>
/// A window and a loop. Implementations own both; the app owns neither.
/// </summary>
/// <remarks>
/// The interface is trivial and the contract below is the actual design, because
/// the two hosts have opposite control flow.
/// <para>
/// Raylib's loop belongs to the application; WPF's belongs to the framework,
/// with per-frame work hanging off a compositor callback. An abstraction over
/// drawing alone would not survive that — this one inverts control.
/// </para>
/// <para>
/// <b>The contract every host is held to:</b>
/// </para>
/// <list type="number">
/// <item><description><see cref="Run"/> opens a window and returns only when the
/// user closes it.</description></item>
/// <item><description>Per frame, exactly once, in this order: snapshot input →
/// <see cref="IViewerApp.Update"/> → <see cref="IViewerApp.Render"/>.</description></item>
/// <item><description>The <em>app</em> owns the renderer's frame and brackets it
/// itself, so a host must <b>not</b> bracket it again. A host brackets only its
/// own presentation, which is a different pair of verbs.</description></item>
/// <item><description><c>deltaSeconds</c> is <b>raw</b> wall-clock time since the
/// previous Update, never negative. Hosts must not clamp, smooth or filter it —
/// <c>FixedTimestep</c> is already the circuit breaker.</description></item>
/// <item><description>Edge flags in <see cref="InputState"/> are true iff the
/// transition happened since the previous Update, and true for exactly one
/// frame.</description></item>
/// <item><description>Update and Render are never re-entrant, and always on one
/// thread.</description></item>
/// </list>
/// <para>
/// The window title is a host constructor argument; window <em>size</em> follows
/// <see cref="IViewerApp.Layout"/>.
/// </para>
/// <para>
/// A windowed host reads it after every Update and resizes when it changed —
/// mid-session loading changes the map, and the map decides the geometry. A host
/// with no window ignores it, which is legal.
/// </para>
/// </remarks>
public interface IViewerHost : IDisposable
{
    /// <summary>
    /// Runs <paramref name="app"/> to completion, blocking until it is over.
    /// Everything a host promises -- frame order, raw <c>deltaSeconds</c>,
    /// one-frame edges, no re-entrancy -- is the contract on
    /// <see cref="IViewerHost"/>, and this is the only method that has to keep
    /// it.
    /// </summary>
    /// <remarks>
    /// Called once per host instance. "Over" is the user closing the window for
    /// a windowed host and the script running out for one without a window, so a
    /// caller can treat this as run-to-completion whichever it got.
    /// </remarks>
    void Run(IViewerApp app);
}
