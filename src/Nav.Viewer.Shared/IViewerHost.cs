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
/// <see cref="IViewerApp.Update"/> → <see cref="IViewerApp.Render"/>. The
/// <em>app</em> owns the renderer's frame: <see cref="IViewerApp.Render"/> calls
/// <see cref="IRenderer.BeginFrame"/> and <see cref="IRenderer.EndFrame"/> itself,
/// so a host must <b>not</b> bracket it again. A host brackets only its own
/// presentation — raylib's <c>BeginDrawing</c>/<c>EndDrawing</c>, WPF's surface
/// lock — which is a different pair of verbs.</description></item>
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
/// The window title is a host constructor argument; window <em>size</em> follows
/// <see cref="IViewerApp.Layout"/>. A windowed host reads it after every Update
/// and resizes when it changed — mid-session loading changes the map, and the map
/// decides the geometry. A host with no window ignores it, which is legal.
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

/// <summary>
/// The application, as a host sees it: something to tick, something to draw, a
/// line of text to display however the host displays text — and, since
/// mid-session loading, the geometry the host should size its window to and a
/// way to hand the app a file.
/// </summary>
/// <remarks>
/// <see cref="Layout"/> and <see cref="LoadFile"/> are milestone-2's second
/// recorded seam change (the first was <see cref="InputState.ButtonsDown"/>).
/// Both exist because content became loadable mid-session: the host owns the
/// gesture that produces a file — a dialog, a drop — and the app owns what the
/// file means; the map then dictates the window size, so geometry had to become
/// observable rather than a construction-time constant. The drawing verbs were
/// untouched again.
/// </remarks>
public interface IViewerApp
{
    /// <summary>
    /// The status line. A <em>string</em>, deliberately — the app decides what it
    /// says, the host decides how it looks.
    /// </summary>
    string StatusText { get; }

    /// <summary>
    /// What the window should be called. The same split as the status line —
    /// and observable rather than a constructor argument, because a loaded file
    /// changes what the window is showing.
    /// </summary>
    string WindowTitle { get; }

    /// <summary>
    /// The map area's pixel geometry. Changes only when content is loaded, and
    /// only between frames — never during Update or Render.
    /// </summary>
    GridLayout Layout { get; }

    /// <summary>
    /// Load a map or scenario file the host's chrome produced. Never throws for
    /// a bad file: a refusal keeps the current content and says why in
    /// <see cref="StatusText"/>.
    /// </summary>
    void LoadFile(string path);

    /// <summary>
    /// One frame's worth of thinking. <paramref name="deltaSeconds"/> is raw
    /// wall-clock time, never negative and never smoothed by the host, and every
    /// edge flag in <paramref name="input"/> is true for exactly this one call --
    /// both per the contract on <see cref="IViewerHost"/>.
    /// </summary>
    /// <remarks>
    /// The app absorbs the raw delta itself, so a frame worth zero seconds (a
    /// duplicate compositor callback, a scripted frame) must be harmless rather
    /// than something a host filters out first.
    /// </remarks>
    void Update(in InputState input, float deltaSeconds);

    /// <summary>
    /// Draws what this frame's <see cref="Update"/> produced, always after it and
    /// on the same thread.
    /// </summary>
    /// <remarks>
    /// The app never learns which <see cref="IRenderer"/> it was handed and must
    /// not retain <paramref name="renderer"/> past the call -- a host is free to
    /// throw its renderer away and build another between frames, which is exactly
    /// what the WPF host does when a loaded map changes the surface size.
    /// </remarks>
    void Render(IRenderer renderer);
}
