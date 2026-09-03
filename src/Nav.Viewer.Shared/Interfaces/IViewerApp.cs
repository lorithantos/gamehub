namespace Nav.Viewer.Interfaces;

/// <summary>
/// The application, as a host sees it: something to tick, something to draw, and
/// a line of text to display however the host displays text.
/// </summary>
/// <remarks>
/// <see cref="Layout"/> and <see cref="LoadFile"/> exist because content is
/// loadable mid-session.
/// <para>
/// The host owns the GESTURE that produces a file — a dialog, a drop — and the
/// app owns what the file MEANS.
/// </para>
/// <para>
/// The map then dictates the window size, so geometry has to be observable
/// rather than a construction-time constant.
/// </para>
/// </remarks>
public interface IViewerApp
{
    /// <summary>
    /// The status line. A <em>string</em>, deliberately — the app decides what it
    /// says, the host decides how it looks.
    /// </summary>
    string StatusText { get; }

    /// <summary>
    /// Everything worth knowing about the watched unit, as rows. The same split
    /// as the status line — the app decides what it says, the host decides how
    /// it looks.
    /// </summary>
    /// <remarks>
    /// A <em>second</em> property rather than more <see cref="StatusText"/>, and
    /// that is the whole reason it exists: the status line's length is pinned so
    /// a window sized to content cannot shake, and a unit's state is far too much
    /// text to live under that rule.
    /// <para>
    /// Empty when nothing is selected, which is a host's cue to show nothing
    /// rather than an empty frame.
    /// </para>
    /// <para>
    /// <see cref="DebugRow"/> rather than a viewer-owned row type: the movement
    /// layer already publishes exactly this shape through
    /// <see cref="IDebugView"/>, and it supplies most of what is in here. Two
    /// identical three-string records one project apart is one to keep in step
    /// for nothing.
    /// </para>
    /// <para>
    /// Everything <see cref="IDebugView"/> forbids applies: a host lays these
    /// out and nothing branches on them or parses a value back into a number.
    /// </para>
    /// </remarks>
    IReadOnlyList<DebugRow> Inspector { get; }

    /// <summary>
    /// Which keycap does what. The host reads it to translate its own key type;
    /// the app reads it to write the hints in <see cref="StatusText"/>, so a
    /// rebind cannot leave the hints lying.
    /// </summary>
    Keymap Keys { get; }

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
