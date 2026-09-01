namespace Nav.Viewer.Interfaces;

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
