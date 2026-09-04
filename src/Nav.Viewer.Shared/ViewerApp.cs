using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The viewer, with no window and no renderer: input in, draw calls out.
/// </summary>
/// <remarks>
/// References nothing but <c>Nav.Core</c> and the seam, and compiles in a project
/// with no graphics package. That is the claim the two-renderer experiment was
/// built to test, and it still holds.
/// <para>
/// This class owns only PRESENTATION: layout, terrain image, wall-clock
/// accumulation, frame blending, the drag rectangle.
/// </para>
/// <para>
/// Everything with a decision in it — what is loaded, who is selected, whether
/// time runs — lives in <see cref="ViewerSession"/>. This class translates input
/// into session commands and session state into the five renderer verbs.
/// </para>
/// <para>
/// <b><see cref="IRenderer"/> did not have to grow.</b> Many units are
/// <c>DrawCircle</c> in a loop, a route is <c>DrawLine</c>, the drag band is
/// four lines. A display need requiring a sixth verb would have been the seam's
/// first genuine leak.
/// </para>
/// <para>
/// <b>Fog did not force one either.</b> Drawing the board through one side's
/// eyes is a second <see cref="TerrainImage"/> over the first -- one texel per
/// cell, which is exactly the granularity fog has -- plus a circle per
/// remembered enemy and a <c>continue</c> for the ones nobody found. What the
/// app cannot do is ask a world what a side knows, because this project cannot
/// name a world: it is handed an <see cref="IVisibilityView"/>, or it draws the
/// true board as it always did.
/// </para>
/// </remarks>
public sealed class ViewerApp : IViewerApp
{
    /// <summary>A drag smaller than this in both axes is a click.</summary>
    private const float ClickSlopPixels = 4.0f;

    /// <summary>
    /// How many digits every counter in the status line is padded to.
    /// </summary>
    /// <remarks>
    /// A CONSTANT, not the roster's digit count. A live world brings units on
    /// mid-run, so a width read off the roster steps up the moment a wave lands
    /// and drags every counter in the line up with it — which is the shaking
    /// this padding exists to prevent, arriving from the one number that was
    /// trusted to hold still. A high-water mark is the same step, later and
    /// harder to explain.
    /// <para>
    /// Four digits covers a roster of 9999, and the counters derived from it —
    /// arrived, stuck, planning, selected — can never exceed the roster. Past
    /// 9999 nothing is truncated: the number prints at its own width and the
    /// line grows, once, and stays at the new length.
    /// </para>
    /// </remarks>
    private const int CounterDigits = 4;

    /// <summary>
    /// The heading the controls folder sits under, and a name no source may
    /// take -- the same reservation "Viewer" has had, one block further down.
    /// </summary>
    private const string Controls = "Controls";

    private readonly ViewerSession _session;
    private readonly int _fitWidth;
    private readonly int _fitHeight;

    /// <summary>
    /// The other things describing themselves into the inspector, in the order
    /// whoever composed the application handed them over.
    /// </summary>
    /// <remarks>
    /// <b>The viewer knows nothing about any of them and must not learn.</b> This
    /// project references Nav.Core alone, so a source can only ever be rows here
    /// -- which is the point: an application can hand over a tactics world, a
    /// second world, or something written next year, and none of it reaches this
    /// file as a type.
    /// <para>
    /// Copied out of whatever was passed in, so a caller that keeps its own list
    /// and adds to it later cannot change what a frame is describing halfway
    /// through describing it.
    /// </para>
    /// </remarks>
    private readonly IWorldDebugView[] _sources;

    /// <summary>Never zoom past a cell this big; beyond it a screen holds nothing.</summary>
    private const int MaxCellSize = 48;

    /// <summary>
    /// Degrees of hue between one side's arc and the next. Side 0 comes out warm
    /// and side 1 cool, with a gap between them wider than any two ids inside one
    /// arc — which is the ordering that matters, and the reason
    /// <see cref="SideArcWidth"/> is narrower than this.
    /// </summary>
    /// <remarks>
    /// Tuned for the two sides a fight has. A third side wraps back into the
    /// first's arc, and a viewer that has to tell three apart wants a palette
    /// rather than a wider spacing.
    /// </remarks>
    private const float SideArcSpacing = 155f;

    /// <summary>How much of the wheel one side's units are spread across.</summary>
    private const float SideArcWidth = 110f;

    // The camera. Fit settles the window and the zoomed-out floor once per load;
    // these three are what panning and zooming move, and Layout is derived from
    // them rather than stored twice.
    private int _viewWidth;
    private int _viewHeight;
    private int _fitCellSize;
    private int _cellSize;
    private int _focusCell;

    // All derived from the session's content, and all rebuilt when its Version
    // moves: a load replaces the map, and everything below follows the map.
    private Grid _grid;
    private TerrainImage _terrain;
    private FixedTimestep _clock;
    private int[] _previousCells;
    private int _sessionVersion;

    /// <summary>
    /// Seconds per tick to run at, or null for whatever the content says.
    /// </summary>
    /// <remarks>
    /// The slow paces are for watching. At the content's own rate a gather, a
    /// retreat or a patrol turn is over in a second or two — fine for a soak
    /// test, useless for seeing what a doctrine did.
    /// <para>
    /// Two ticks a second is about reading speed; one is for watching a single
    /// decision land.
    /// </para>
    /// </remarks>
    private static readonly double?[] Paces = [null, 0.5, 1.0];

    private int _pace;

    /// <summary>
    /// Nobody's eyes: the true board, which is what this viewer has always drawn
    /// and what it draws until somebody asks for a side.
    /// </summary>
    /// <remarks>
    /// Not a side number, because a side is an int and every int is a side
    /// somebody could fight for. -1 is outside <see cref="IVisibilityView.Sides"/>
    /// by construction, so "whose eyes" and "no eyes" never collide.
    /// </remarks>
    private const int Observer = -1;

    /// <summary>
    /// How dark ground a side cannot see is drawn, as a multiplier on every
    /// colour channel.
    /// </summary>
    /// <remarks>
    /// Dark enough that the seen ring reads as the shape it is at a glance, and
    /// light enough that the walls are still walls -- a watcher has to be able to
    /// see the map a side is moving over, or the fog view is only useful for
    /// counting units.
    /// </remarks>
    private const float FogDim = 0.28f;

    /// <summary>
    /// How many ticks it takes a sighting to fade all the way into the fog.
    /// </summary>
    /// <remarks>
    /// A DISPLAY constant and emphatically not a doctrine's forgetting time.
    /// Nothing here decides when a side stops believing a ghost -- that is the
    /// doctrine's call, and a patrol and a guard have every reason to answer
    /// differently. This only says how long a ghost stays legible on screen, so
    /// that "seen a moment ago" and "seen a minute ago" do not look alike.
    /// </remarks>
    private const int GhostFade = 120;

    /// <summary>A pad a side can see, painted into the fog image.</summary>
    private static readonly RgbaColor PadColour = RgbaColor.Rgb(60, 150, 90);

    /// <summary>A sighting taken this tick.</summary>
    private static readonly RgbaColor GhostFresh = RgbaColor.Rgb(190, 120, 200);

    /// <summary>A sighting <see cref="GhostFade"/> ticks old or older: all but gone.</summary>
    private static readonly RgbaColor GhostStale = RgbaColor.Rgb(60, 45, 65);

    /// <summary>
    /// Whose knowledge the board is drawn from, or null for a viewer nobody
    /// wired one to -- which is every viewer that is not playing a fight.
    /// </summary>
    /// <remarks>
    /// Held rather than merged into <see cref="_sources"/> because it answers a
    /// different question. A source DESCRIBES; this one decides what is on
    /// screen at all.
    /// </remarks>
    private readonly IVisibilityView? _eyes;

    /// <summary>Whose eyes the board is drawn through, or <see cref="Observer"/>.</summary>
    private int _viewpoint = Observer;

    // The fog image and the exact answers it was built from. Both renderers
    // cache their upload by REFERENCE identity, so handing back the same
    // instance costs nothing and handing back a new one costs an upload -- which
    // is why the visible set is compared rather than the tick.
    private TerrainImage? _fog;
    private int[] _fogVisible = [];
    private int[] _fogPads = [];
    private int _fogViewpoint = Observer;
    private int _fogVersion = -1;

    /// <summary>The current viewpoint's visible cells, for the per-unit test.</summary>
    private HashSet<int> _visible = [];

    /// <summary>What the current viewpoint remembers, as of the last update.</summary>
    private IReadOnlyList<RememberedUnit> _remembered = [];

    /// <summary>How long one tick takes at the current pace.</summary>
    private double StepSeconds => Paces[_pace] ?? _session.TickSeconds;

    /// <summary>How far through the current tick we are, for drawing between cells.</summary>
    private float _blend;

    private bool _dragging;
    private Vector2 _dragAnchor;
    private Vector2 _dragCurrent;

    /// <summary>Why the last load was refused, shown until the next input.</summary>
    private string? _loadError;

    /// <summary>
    /// Presentation for <paramref name="session"/>, which keeps ownership of the
    /// content and the simulation -- this constructor only derives what is drawn
    /// from them.
    /// </summary>
    /// <remarks>
    /// The two maxima are a <em>budget</em>, not a size: <see cref="GridLayout.Fit"/>
    /// takes the largest whole-pixel cell that fits inside them, so the resulting
    /// <see cref="Layout"/> is usually smaller. They are kept, not consumed --
    /// every later load re-fits the new map into the same budget.
    /// <para>
    /// <c>sources</c> is everything else that can describe itself -- see
    /// <see cref="_sources"/> and <see cref="Inspector"/>. Handing over none, which
    /// is what both hosts do today, is a viewer that shows exactly what it showed
    /// before there was such a thing.
    /// </para>
    /// </remarks>
    public ViewerApp(
        ViewerSession session,
        int maxPixelWidth,
        int maxPixelHeight,
        Keymap? keys = null,
        IReadOnlyList<IWorldDebugView>? sources = null,
        IVisibilityView? eyes = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPixelHeight, 0);

        _session = session;
        _fitWidth = maxPixelWidth;
        _fitHeight = maxPixelHeight;
        _sources = Copy(sources);
        _eyes = eyes;
        Keys = keys ?? Keymap.Default;
        AdoptContent();
        StatusText = BuildStatus();
        Inspector = BuildInspector();
    }

    /// <summary>
    /// Convenience for tests and callers that have content rather than a
    /// session: wraps it in one. The layout's own pixel box is handed back as
    /// the fit budget, which reproduces the identical layout because
    /// <see cref="GridLayout.Fit"/> is a fixed point over its own output.
    /// </summary>
    public ViewerApp(
        Grid grid,
        GridLayout layout,
        int squad = ViewerSession.DefaultSquad,
        RecordedScenario? scenario = null,
        Keymap? keys = null,
        IReadOnlyList<IWorldDebugView>? sources = null,
        IVisibilityView? eyes = null)
        : this(BuildSession(grid, scenario, squad), layout.PixelWidth, layout.PixelHeight, keys, sources, eyes)
    {
    }

    /// <summary>
    /// The map's pixel geometry, re-fitted whenever the session's content
    /// changes. A windowed host watches it between frames and resizes when it
    /// moved -- see <see cref="IViewerHost"/>.
    /// </summary>
    public GridLayout Layout { get; private set; }

    /// <summary>
    /// The status line, rebuilt after every <see cref="Update"/> and every load.
    /// </summary>
    /// <remarks>
    /// The app owns the <em>string</em> because <see cref="IRenderer"/> has no
    /// text verb by design; each host owns how it is shown.
    /// <para>
    /// Its counters are padded to <see cref="CounterDigits"/> — a constant, and
    /// the roster count is one of them — so the line never changes length while
    /// the numbers do, or while a live world brings more units on. A breathing
    /// line shakes a window sized to content.
    /// </para>
    /// </remarks>
    public string StatusText { get; private set; }

    /// <summary>
    /// The watched unit, spelled out, and under it the controls. Rebuilt
    /// alongside <see cref="StatusText"/>, on the same occasions and for the
    /// same reason: it describes the state a frame is about to be drawn from.
    /// </summary>
    /// <remarks>
    /// The sole selection, or the lowest id when several are selected — one unit
    /// read properly beats forty summarised, and the lowest id is the one the
    /// box-select gesture puts first.
    /// <para>
    /// <b>The controls are here whether or not anything is selected</b>, and they
    /// are the one block that is not about a unit. A reader who has not yet
    /// worked out how to select anything is exactly the reader who needs to know
    /// which key does what, so a folder that only appeared once they had managed
    /// it would be missing on the only occasion it mattered.
    /// </para>
    /// <para>
    /// Mostly other people's words: these are <see cref="IDebugView.Describe"/>
    /// rows from the movement layer and from every source the application handed
    /// over, with the viewer's few additions in a group of their own. Nothing may
    /// branch on them or parse a value back into a number.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DebugRow> Inspector { get; private set; }

    /// <summary>
    /// Which keycap does what. Fixed for the life of the app: the hints in
    /// <see cref="StatusText"/> are generated from it, and a map that changed
    /// mid-session would change the status line's length.
    /// </summary>
    public Keymap Keys { get; }

    /// <summary>
    /// The window's name, following the session's <c>MapName</c> -- so loading a
    /// file mid-session renames the window without the host being told.
    /// </summary>
    public string WindowTitle => $"Nav.Viewer - {_session.MapName}";

    /// <summary>
    /// The state layer this app draws. Exposed so a host or a test can command
    /// content and clock directly; the app keeps no second copy of any of it.
    /// </summary>
    public ViewerSession Session => _session;

    /// <summary>The units orders go to, in id order.</summary>
    public IReadOnlyList<int> Selection => _session.Selection;

    /// <summary>
    /// How fast the clock is feeding ticks, as the status line says it: the
    /// content's own rate, or one of the slow paces for watching.
    /// </summary>
    public string PaceLabel => Paces[_pace] is { } seconds
        ? string.Create(CultureInfo.InvariantCulture, $"{1.0 / seconds:0.#}/s")
        : "full";

    /// <summary>
    /// <see cref="ViewerSession.Running"/>, forwarded. The app decides
    /// <em>when</em> to tick a running session; it never decides whether one is
    /// running.
    /// </summary>
    public bool Running => _session.Running;

    /// <summary>
    /// <see cref="ViewerSession.Agents"/>, forwarded. Read-only here -- units are
    /// moved by ticking the session, never by touching this.
    /// </summary>
    public IReadOnlyList<AgentState> Agents => _session.Agents;

    /// <summary>
    /// <see cref="ViewerSession.CurrentTick"/>, forwarded. Note it is the
    /// simulation's tick, not a frame count: many frames blend across one tick.
    /// </summary>
    public int CurrentTick => _session.CurrentTick;

    /// <summary>
    /// Whose knowledge the board is currently drawn from: a side number, or -1
    /// for the observer, who sees the true board.
    /// </summary>
    /// <remarks>
    /// -1 for every viewer that was not handed an <see cref="IVisibilityView"/>,
    /// and for those the viewpoint key cannot move it.
    /// </remarks>
    public int Viewpoint => _viewpoint;

    /// <summary>
    /// A file from the host's chrome — a dialog, a drop. A refusal keeps the
    /// current content and says why in the status line until the next input.
    /// </summary>
    public void LoadFile(string path)
    {
        if (_session.TryLoadFile(path, out var error))
        {
            _loadError = null;
            AdoptContent();
        }
        else
        {
            _loadError = error;
        }

        StatusText = BuildStatus();
        Inspector = BuildInspector();
    }

    /// <summary>
    /// One frame's worth of decisions: input becomes session commands, and
    /// <paramref name="deltaSeconds"/> of wall clock becomes however many whole
    /// ticks it buys, plus the leftover the units are drawn part-way through.
    /// </summary>
    /// <remarks>
    /// Ordering, one-frame edge flags, and the requirement that
    /// <paramref name="deltaSeconds"/> be raw unclamped wall-clock time are the
    /// host's contract -- see <see cref="IViewerHost"/>. Ticks are taken here
    /// only while the session is running; a paused session still advances one
    /// tick per press of the step key.
    /// </remarks>
    public void Update(in InputState input, float deltaSeconds)
    {
        // Defensive: LoadFile adopts eagerly, but if anyone else ever loads
        // into the session, the app must not draw a new grid with old geometry.
        if (_session.Version != _sessionVersion)
        {
            AdoptContent();
        }

        if (input.KeysPressed != ViewerKeys.None || input.ButtonsPressed != MouseButtons.None)
        {
            // Any input clears a lingering load refusal: it was read.
            _loadError = null;
        }

        if (input.IsPressed(MouseButtons.Left) && Layout.TryPick(input.MousePosition, _grid, out var picked))
        {
            // A press selects the nearest unit immediately, the way a click
            // always did. If it turns out to be a drag, the box replaces this
            // on release.
            var nearest = NearestAgentTo(picked);
            _session.Select(nearest >= 0 ? [nearest] : []);

            _dragging = true;
            _dragAnchor = input.MousePosition;
            _dragCurrent = input.MousePosition;
        }
        else if (_dragging && input.IsDown(MouseButtons.Left))
        {
            _dragCurrent = input.MousePosition;
        }
        else if (_dragging)
        {
            _dragCurrent = input.MousePosition;
            _dragging = false;
            CommitDrag();
        }

        if (input.IsPressed(MouseButtons.Right) &&
            Layout.TryPick(input.MousePosition, _grid, out var target))
        {
            _session.OrderSelection(target);
        }

        if (MoveCamera(input))
        {
            RefreshLayout();
        }

        if (input.IsPressed(ViewerKeys.Space))
        {
            _session.ToggleRunning();
        }

        if (input.IsPressed(ViewerKeys.Step))
        {
            // One tick per press, frozen before and after. Pressing it while
            // running pauses first, so a burst can be caught mid-flight and
            // walked forward from there.
            _session.SetRunning(false);
            RememberCells();

            _session.Tick();

            // Draw the completed tick, not a blend into it: a step should land
            // exactly on the state it produced.
            RememberCells();

            _blend = 0f;
            _clock.Reset();
        }

        if (input.IsPressed(ViewerKeys.Pace))
        {
            // Rate only. The simulation is driven by tick COUNT, so slowing the
            // clock changes what a watcher can follow and nothing about what
            // happens -- the same run, told slowly.
            _pace = (_pace + 1) % Paces.Length;
            _clock = new FixedTimestep(StepSeconds);
            _blend = 0f;
        }

        if (input.IsPressed(ViewerKeys.Viewpoint))
        {
            // Whose knowledge the board is drawn from, and nothing else: the
            // fight is not told anybody looked. The observer is both the start
            // and the end of the cycle, so a watcher who has lost track of
            // whose eyes they are on gets back to the truth by pressing on
            // rather than by pressing back.
            _viewpoint = NextViewpoint();
        }

        if (input.IsPressed(ViewerKeys.R))
        {
            if (_session.CanRestart)
            {
                // Back to tick zero: a recording reloaded and stopped, or a live
                // world built again and running. Which of the two it is, and
                // what state it comes back in, is the session's business.
                _session.Restart();
                RememberCells();

                _clock.Reset();
                _blend = 0f;
                _dragging = false;
            }
            else
            {
                // Everybody home. The nearest thing to a reset that means
                // anything once units have scattered.
                _session.OrderEveryone(_previousCells[0]);
            }
        }

        if (_session.Running)
        {
            var steps = _clock.Accumulate(deltaSeconds);
            for (var i = 0; i < steps; i++)
            {
                RememberCells();
                _session.Tick();
                AdmitArrivals();
            }

            // Whatever is left over is how far through the next tick we are, and
            // that is what stops units teleporting a cell at a time on screen.
            _blend = (float)(_clock.Pending / _clock.Step);
        }
        else
        {
            _clock.Reset();
            _blend = 0f;
        }

        // Last, so it sees this frame's viewpoint press AND this frame's ticks.
        // It lives here rather than in Render because rebuilding an image is a
        // change, and Render is marked as an instrument.
        RefreshFog();

        StatusText = BuildStatus();
        Inspector = BuildInspector();
    }

    /// <summary>
    /// The next viewpoint in the cycle: the observer, then each side in
    /// <see cref="IVisibilityView.Sides"/> in turn, then the observer again.
    /// </summary>
    /// <remarks>
    /// Walks the side NUMBERS rather than counting them, so a board whose sides
    /// are 0 and 7 cycles through both and a board with five cycles through
    /// five. Two is what exists today and two is not written down anywhere here.
    /// <para>
    /// A viewer with no <see cref="_eyes"/> never leaves the observer, which is
    /// what keeps the key honest: it is hinted only where it does something.
    /// </para>
    /// </remarks>
    private int NextViewpoint()
    {
        if (_eyes is null)
        {
            return Observer;
        }

        foreach (var side in _eyes.Sides)
        {
            if (side > _viewpoint)
            {
                return side;
            }
        }

        return Observer;
    }

    /// <summary>
    /// Rebuilds the fog image, but only when the visible set it was built from
    /// has actually changed.
    /// </summary>
    /// <remarks>
    /// <b>The comparison is on the ANSWER, not on the tick.</b> Both renderers
    /// key their upload cache on reference identity, so handing back the same
    /// instance costs nothing and handing back a new one costs a texture upload
    /// every frame. A fight spends most of its ticks with nobody crossing a
    /// sight line, and on all of those this returns having compared two ascending
    /// lists and touched nothing.
    /// <para>
    /// The viewpoint and the session's content version are compared with them,
    /// because either one changing makes the same visible set a different
    /// picture -- a load can even hand back the identical cells over a different
    /// map.
    /// </para>
    /// <para>
    /// <see cref="_remembered"/> is taken every time rather than cached: it is a
    /// short list, it changes on ticks the visible set does not, and it costs a
    /// read.
    /// </para>
    /// </remarks>
    private void RefreshFog()
    {
        if (_viewpoint < 0 || _eyes is null)
        {
            _fog = null;
            _fogVisible = [];
            _fogPads = [];
            _fogViewpoint = Observer;
            _visible = [];
            _remembered = [];
            return;
        }

        var visible = _eyes.VisibleCells(_viewpoint);
        var pads = _eyes.RepairPoints(_viewpoint);
        _remembered = _eyes.Remembered(_viewpoint);

        if (_fog is not null &&
            _fogViewpoint == _viewpoint &&
            _fogVersion == _sessionVersion &&
            Same(_fogVisible, visible) &&
            Same(_fogPads, pads))
        {
            return;
        }

        _fogVisible = [.. visible];
        _fogPads = [.. pads];
        _fogViewpoint = _viewpoint;
        _fogVersion = _sessionVersion;
        _visible = [.. visible];
        _fog = TerrainImage.Fogged(
            _grid, visible, pads, RgbaColor.RayWhite, RgbaColor.DarkGray, PadColour, FogDim);
    }

    /// <summary>Whether two cell answers hold the same cells in the same order.</summary>
    private static bool Same(int[] built, IReadOnlyList<int> current)
    {
        if (built.Length != current.Count)
        {
            return false;
        }

        for (var i = 0; i < built.Length; i++)
        {
            if (built[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the current viewpoint cannot see <paramref name="agent"/>, and so
    /// must not be shown it.
    /// </summary>
    /// <remarks>
    /// A side always sees the cell each of its own units stands on, so the side
    /// test is strictly redundant against the cell test -- and it is kept,
    /// because it is the exclusion this code MEANS. What is hidden is the ENEMY
    /// nobody has found, not whichever of my own units happens to have fallen
    /// out of the visible set for a reason I would then have to go and look for.
    /// <para>
    /// Always false for the observer, which is what makes the observer's frame
    /// byte-identical to a viewer that never heard of fog.
    /// </para>
    /// </remarks>
    private bool Hidden(AgentState agent) =>
        _fog is not null && agent.Side != _viewpoint && !_visible.Contains(agent.Cell);

    /// <summary>
    /// A sighting's colour, faded from <see cref="GhostFresh"/> toward
    /// <see cref="GhostStale"/> by how many ticks old it is.
    /// </summary>
    /// <remarks>
    /// Fading rather than hiding, because a stale sighting is the interesting
    /// one: a doctrine that keeps shooting at where an enemy WAS is the exact
    /// thing this view exists to catch, and it cannot be caught if the belief
    /// disappears the moment it stops being true.
    /// </remarks>
    private static RgbaColor Ghost(int age)
    {
        var faded = Math.Clamp(age / (float)GhostFade, 0f, 1f);
        static byte Mix(byte from, byte to, float t) => (byte)(from + ((to - from) * t));

        return RgbaColor.Rgb(
            Mix(GhostFresh.R, GhostStale.R, faded),
            Mix(GhostFresh.G, GhostStale.G, faded),
            Mix(GhostFresh.B, GhostStale.B, faded));
    }

    /// <summary>
    /// Draws the frame <see cref="Update"/> decided on: terrain, the sole
    /// selection's route, every unit lerped by the leftover blend, and the drag
    /// band. Reads state and moves nothing -- calling it twice draws the same
    /// picture.
    /// </summary>
    /// <remarks>
    /// Opens and closes the frame itself, so <paramref name="renderer"/> arrives
    /// unbracketed and leaves flushed. Ordering against <see cref="Update"/> is
    /// the host's contract -- see <see cref="IViewerHost"/>. Nothing here needs a
    /// verb <see cref="IRenderer"/> does not have: the band is four lines and the
    /// leader mark is circles.
    /// <para>
    /// Marked as an instrument, which is the widest read of the simulation the
    /// viewer makes: it touches the plans, the leaders, every agent and the
    /// debug view once a frame, and only on the runs somebody is watching. A
    /// drawing routine that began perturbing what it draws is the exact fault the
    /// walk exists to catch, and the drawing is not exempt from it.
    /// </para>
    /// </remarks>
    [Observes]
    public void Render(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        renderer.BeginFrame(RgbaColor.Black);
        // The map's own extent, placed at the camera's origin -- NOT the window,
        // which they stopped being the same rectangle when zooming arrived. The
        // renderer already took a destination rect, so scrolling and scaling cost
        // it nothing: it is one textured quad either way.
        var map = new RectF(Layout.OriginX, Layout.OriginY, Layout.MapWidth(_grid), Layout.MapHeight(_grid));
        renderer.DrawTerrain(_terrain, map);

        // The fog, as a SECOND terrain image over the first: one texel per cell,
        // no sixth verb, and under every line and circle in both hosts -- the
        // D3D11 renderer batches those and flushes them at EndFrame, so nothing
        // drawn below can be made to sit under this whatever order it is called
        // in. Null for the observer, which is what keeps the observer's frame
        // identical to the one this viewer drew before fog existed.
        var radius = Math.Max(2.0f, Layout.CellSize * 0.34f);
        if (_fog is { } fog)
        {
            renderer.DrawTerrain(fog, map);

            // What the side BELIEVES, under everything that is actually there:
            // a ghost stands at the cell the enemy was last seen on, which is
            // not where it is. One this side can currently see has its own unit
            // drawn over the top of it a moment later.
            foreach (var ghost in _remembered)
            {
                renderer.DrawCircle(
                    CenterOfCell(ghost.Cell), radius, Ghost(_session.CurrentTick - ghost.Tick));
            }
        }

        // A route is drawn only when exactly one unit is selected. Drawing every
        // route at two dozen units is a ball of yarn, and at two hundred it is a
        // solid colour -- a boxed group shows its motion, not its plans.
        var selection = _session.Selection;
        var soleSelection = selection.Count == 1 ? selection[0] : -1;
        var plans = _session.CurrentPlans();
        foreach (var (agent, plan) in plans)
        {
            if (agent != soleSelection || plan.Cells.Count < 2)
            {
                continue;
            }

            // A route belongs to the unit that walks it, so a unit this side
            // cannot see must not leave one on screen. Selecting an enemy under
            // the observer and then switching eyes is exactly how that would
            // otherwise happen, and a route is the loudest thing on the map.
            if (agent < _session.Agents.Count && Hidden(_session.Agents[agent]))
            {
                continue;
            }

            var thickness = Math.Max(1.0f, Layout.CellSize * 0.12f);

            // ONE COLOUR. A plan that stopped at the edge of the reservation
            // window is PROGRESS, not surrender -- the agent walks as far as it
            // booked and replans when the window moves. That used to be drawn
            // orange, which sounded informative and was not: a unit under a
            // group order plans a step at a time and reads IsPartial
            // continuously, so nearly every grouped unit was permanently orange
            // and the colour separated nobody from anybody. Partial is a raw
            // fact; it is in the inspector.
            //
            // IsStuck then took the second colour, on the argument that a plan
            // with no cells at all is the distinction worth one. It is -- but it
            // cannot arrive here: MovementSystem leaves a stuck agent's Plan
            // null rather than storing the result, so it never reaches
            // CurrentPlans, and the guard above skips anything under two cells
            // in any case. A colour that cannot be seen is dead code on a map
            // whose whole job is to show what is happening, so it came out.
            //
            // A unit which has given up is no longer invisible: having no plan is
            // exactly the state the no-route cross below draws, and a stuck agent
            // has none. The cross says "nothing to walk", not "gave up" -- the
            // reason is still only in the inspector -- but the unit is at least
            // pickable out of a crowd now. See
            // TheReasonAStuckPlanNeverReachesTheOverlay.
            var routeColour = RgbaColor.SkyBlue;
            var waitRadius = Math.Max(1.5f, Layout.CellSize * 0.18f);

            // Where the plan's own cells begin. Commit pads a new plan from the
            // current tick out to AnchorTick -- CurrentTick + Latency -- with
            // wherever the agent stands until then, and the search's first cell
            // repeats that anchor cell again. Every repeat in that head run is
            // the PLANNER's delay rather than the unit waiting for anybody, and
            // marking them put a cluster of dots on the ground under the unit's
            // own feet on every route that had just been ordered.
            //
            // Gating on AgentState.Thinking was tried first and does NOT work:
            // the padding is written in when the plan is committed, so by the
            // time it is drawn the search that caused it has finished and
            // Thinking is already false. The head run is what actually tells the
            // two apart, so the Thinking condition came out with it in --
            // keeping both would blink a genuine wait off on whichever ticks the
            // NEXT search happens to be in flight, which is a different unit's
            // business entirely.
            var interior = 1;
            while (interior < plan.Cells.Count && plan.Cells[interior - 1] == plan.Cells[interior])
            {
                interior++;
            }

            for (var i = 1; i < plan.Cells.Count; i++)
            {
                if (plan.Cells[i - 1] == plan.Cells[i])
                {
                    if (i > interior)
                    {
                        // A repeated cell past the first step is a deliberate tick
                        // of waiting -- the unit standing behind somebody -- and it
                        // used to draw nothing at all, so a queued unit's route had
                        // a silent gap in it exactly where the interesting decision
                        // was. QUEUED MUST NOT LOOK LIKE REFUSED, the same trap
                        // ColourFor guards, one layer down.
                        renderer.DrawCircle(CenterOfCell(plan.Cells[i]), waitRadius, routeColour);
                    }

                    continue;
                }

                renderer.DrawLine(
                    CenterOfCell(plan.Cells[i - 1]),
                    CenterOfCell(plan.Cells[i]),
                    thickness,
                    routeColour);
            }
        }

        // Who the planner currently has a route for. CurrentPlans is keyed by id
        // and carries ONLY agents that have a plan, so the ids missing from it are
        // the ones with nothing to walk -- and that absence is the only place the
        // viewer can read the fact from. Built once rather than searched per unit:
        // the loop below runs over every agent on the map.
        var routed = new HashSet<int>();
        foreach (var (agent, _) in plans)
        {
            routed.Add(agent);
        }

        var leaders = _session.Leaders;
        foreach (var agent in _session.Agents)
        {
            if (Hidden(agent))
            {
                // Not dimmed and not ghosted: NOT DRAWN. A side that has not
                // found a unit has no picture of it at all, and half a unit on
                // screen would be the viewer inventing knowledge nobody has.
                continue;
            }

            var from = CenterOfCell(_previousCells[agent.Id]);
            var to = CenterOfCell(agent.Cell);
            var at = Vector2.Lerp(from, to, _blend);

            renderer.DrawCircle(at, radius, ColourFor(agent));

            if (leaders.Contains(agent.Id))
            {
                // The leader's mark: the selection dot, doubled — a bullseye of
                // dot-in-dot in the unit's own colour. Still just circles;
                // five verbs is five verbs.
                renderer.DrawCircle(at, radius * 0.55f, RgbaColor.Black);
                renderer.DrawCircle(at, radius * 0.28f, ColourFor(agent));
            }

            if (selection.Contains(agent.Id))
            {
                // A ring, drawn as a slightly larger circle underneath would be —
                // but the seam has no stroke, so the selection is a second smaller
                // dot on top.
                renderer.DrawCircle(at, radius * 0.35f, RgbaColor.Black);
            }

            // NO ROUTE: a live, unarrived unit that is not mid-search and has no
            // plan. A unit with nothing to walk is the state you most want to pick
            // out of a crowd, and until now it looked identical to one standing on
            // its goal.
            //
            // All four conditions are needed, and each excludes a different
            // ordinary reason to have no plan:
            //   arrived   -- no plan because none is needed;
            //   thinking  -- a search is in flight and nothing is committed yet;
            //   not alive -- a removed unit keeps its id and its last cell forever
            //                and will NEVER have a plan, so without this guard
            //                every corpse on the field wears the mark -- a false
            //                signal on exactly the map where you would be counting
            //                bodies.
            //
            // THE ALIVE GUARD IS CURRENTLY REDUNDANT AND IS KEPT ANYWAY. Measured,
            // not assumed: MovementSystem.Remove parks a removed unit's goal on its
            // own cell, and AgentState.Arrived is Cell == Goal, so today every
            // corpse is already excluded by the arrived guard one line earlier and
            // dropping this condition fails no test. It stays because the exclusion
            // it makes is the one this code MEANS, while the arrived one is a
            // coincidence in a class a layer away: the day Remove keeps a
            // casualty's last goal -- which is a reasonable thing to want in a
            // casualty report -- every body on the field lights up, and the
            // instrument lies loudest on the map it was built for.
            //
            // Deliberately NOT AgentState.Stuck (!Arrived && StalledTicks > 0).
            // Stuck is a HISTORY -- "its replans keep failing" -- and this is a
            // PRESENT FACT -- "it has no route right now". They overlap heavily
            // and are not the same set: a unit can be stuck while still walking
            // the plan it has, and a unit can be routeless on the very tick its
            // stall counter is zero. An instrument shows the present fact.
            if (HasNoRoute(agent, routed.Contains(agent.Id)))
            {
                // A CROSS, not another circle. Every other mark at a unit's
                // position is a filled disc -- the unit, the leader's doubled dot,
                // the selection dot -- so one more concentric circle would land in
                // the middle of a vocabulary that already reads by radius, and at
                // small cell sizes a bullseye of three is indistinguishable from a
                // bullseye of two. Crossed lines share no shape with any of them,
                // and "crossed out" is what the state means: nothing to walk.
                //
                // The arms reach PAST the unit's own circle so the mark survives
                // being drawn over a disc of any hue, and it is drawn last so it
                // sits on top of the selection dot rather than under it.
                var arm = radius * 1.2f;
                var stroke = Math.Max(1.0f, Layout.CellSize * 0.08f);

                // Orange, which the overlay freed when the partial-plan colour
                // came out. Not SkyBlue (routes and the drag band), not Red (a
                // stuck unit's own circle), and unreachable by ColourFor -- unit
                // hues are floored at 0.35 in every channel and orange has none in
                // blue, so nothing on the map can wear it by accident.
                renderer.DrawLine(
                    at - new Vector2(arm, arm), at + new Vector2(arm, arm), stroke, RgbaColor.Orange);
                renderer.DrawLine(
                    at - new Vector2(arm, -arm), at + new Vector2(arm, -arm), stroke, RgbaColor.Orange);
            }
        }

        // The drag band: four lines, because the seam has no stroked rectangle
        // and does not need one.
        if (_dragging && IsBox(DragRect()))
        {
            var band = DragRect();
            var topLeft = new Vector2(band.X, band.Y);
            var topRight = new Vector2(band.Right, band.Y);
            var bottomLeft = new Vector2(band.X, band.Bottom);
            var bottomRight = new Vector2(band.Right, band.Bottom);

            renderer.DrawLine(topLeft, topRight, 1.0f, RgbaColor.SkyBlue);
            renderer.DrawLine(topRight, bottomRight, 1.0f, RgbaColor.SkyBlue);
            renderer.DrawLine(bottomRight, bottomLeft, 1.0f, RgbaColor.SkyBlue);
            renderer.DrawLine(bottomLeft, topLeft, 1.0f, RgbaColor.SkyBlue);
        }

        renderer.EndFrame();
    }

    /// <summary>
    /// A flat cell index -- <c>y * Width + x</c>, the one cell identity this
    /// codebase uses -- as the pixel its square is centred on, which is where a
    /// unit or a route vertex is drawn.
    /// </summary>
    /// <remarks>
    /// Depends on <see cref="Layout"/>, so a position taken before a load and
    /// used after one is wrong by however much the cell size changed.
    /// </remarks>
    public Vector2 CenterOfCell(int cell) => Layout.CenterOf(_grid.ColumnOf(cell), _grid.RowOf(cell));

    /// <summary>
    /// Where every unit stands now, kept as where it stood a tick ago once the
    /// next tick has been played. It is what a unit is drawn moving FROM.
    /// </summary>
    /// <remarks>
    /// <b>Resized rather than indexed into, because a roster is not fixed.</b> A
    /// map and a recording place everybody up front and never add another, and
    /// this was an array sized once for exactly that. A live world does not work
    /// that way: a wave arrives mid-run with ids above anything that existed
    /// when the world was adopted, and a restart takes the roster back down to
    /// the handful it started with. Both were an index off the end of this array
    /// on the frame after -- one on the first wave, the other on the first R.
    /// <para>
    /// A unit that has just appeared is remembered where it stands, so its first
    /// drawn frame is a unit standing still rather than one sliding in from
    /// wherever the id before it happened to be.
    /// </para>
    /// </remarks>
    private void RememberCells()
    {
        var agents = _session.Agents;
        if (_previousCells.Length != agents.Count)
        {
            _previousCells = new int[agents.Count];
        }

        for (var id = 0; id < agents.Count; id++)
        {
            _previousCells[id] = agents[id].Cell;
        }
    }

    /// <summary>
    /// Units that came onto the board during the tick just played, remembered
    /// where they now stand. Everybody else keeps the cell they were remembered
    /// in, because that is what they are being drawn moving out of.
    /// </summary>
    /// <remarks>
    /// This runs between a tick and the frame that draws it, which is the only
    /// window in which an id can exist on the board and not in the array beside
    /// it. Costs a comparison on every tick that brought nobody new, which is
    /// almost all of them.
    /// </remarks>
    private void AdmitArrivals()
    {
        var agents = _session.Agents;
        if (agents.Count <= _previousCells.Length)
        {
            return;
        }

        var grown = new int[agents.Count];
        Array.Copy(_previousCells, grown, _previousCells.Length);
        for (var id = _previousCells.Length; id < agents.Count; id++)
        {
            grown[id] = agents[id].Cell;
        }

        _previousCells = grown;
    }

    /// <summary>
    /// The sources as this class will hold them: its own array, checked once.
    /// </summary>
    /// <remarks>
    /// A null in the list is refused HERE, at the seam where a human is wiring the
    /// application up, rather than survived later. The panel forgives a source
    /// that throws or answers for a unit it has never heard of, because those are
    /// runtime facts about a running world; a missing source is a composition
    /// that was never finished.
    /// </remarks>
    private static IWorldDebugView[] Copy(IReadOnlyList<IWorldDebugView>? sources)
    {
        if (sources is null)
        {
            return [];
        }

        var copy = new IWorldDebugView[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            copy[i] = sources[i] ??
                throw new ArgumentException($"source {Number(i + 1)} of {Number(sources.Count)} is null", nameof(sources));
        }

        return copy;
    }

    private static ViewerSession BuildSession(Grid grid, RecordedScenario? scenario, int squad)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return scenario is null
            ? ViewerSession.FromMap(grid, "(unnamed)", squad)
            : ViewerSession.FromScenario(grid, "(unnamed)", scenario);
    }

    /// <summary>
    /// Rebuilds everything the app derives from the session's content. Runs at
    /// construction and after every version bump, so derived state can never
    /// describe a map that is no longer loaded.
    /// </summary>
    [MemberNotNull(nameof(_grid), nameof(_terrain), nameof(_clock), nameof(_previousCells))]
    private void AdoptContent()
    {
        _sessionVersion = _session.Version;
        _grid = _session.Grid;

        // Fit decides two things and then stops being consulted: the window's
        // size, which the hosts follow and which must not change when somebody
        // zooms, and the smallest useful cell -- there is no reason to zoom out
        // past the whole map already being visible.
        var fit = GridLayout.Fit(_grid, _fitWidth, _fitHeight);
        _viewWidth = fit.PixelWidth;
        _viewHeight = fit.PixelHeight;
        _fitCellSize = fit.CellSize;
        _cellSize = fit.CellSize;
        _focusCell = _grid.Index(_grid.Width / 2, _grid.Height / 2);
        RefreshLayout();

        _terrain = TerrainImage.FromGrid(_grid, RgbaColor.RayWhite, RgbaColor.DarkGray);
        // The pace survives a load: somebody watching at one tick a second who
        // opens another scenario wants to watch that one at the same speed.
        _clock = new FixedTimestep(StepSeconds);
        _previousCells = [.. _session.Agents.Select(a => a.Cell)];
        _blend = 0f;
        _dragging = false;
    }

    /// <summary>Recomputes the window's view of the map from the camera.</summary>
    private void RefreshLayout() =>
        Layout = GridLayout.Viewing(_grid, _cellSize, _viewWidth, _viewHeight, _focusCell);

    /// <summary>
    /// Applies this frame's pan, zoom and reset. Returns whether anything moved,
    /// so the layout is rebuilt on the frames that need it and not on the rest.
    /// </summary>
    /// <remarks>
    /// Panning is a HALF SCREEN per press rather than a smooth glide, and that is
    /// a choice rather than a limit of the input snapshot.
    /// <para>
    /// An instrument is being read, not flown: half a screen is repeatable, lands
    /// in the same place twice, and cannot overshoot what is being looked at.
    /// </para>
    /// <para>
    /// Zoom doubles and halves, so the scale is always a whole number of pixels
    /// per cell and no cell straddles a half-pixel. It floors at whatever fitted
    /// the whole map, because zooming out past that only adds margin.
    /// </para>
    /// </remarks>
    private bool MoveCamera(in InputState input)
    {
        var moved = false;

        if (input.IsPressed(ViewerKeys.ResetView))
        {
            _cellSize = _fitCellSize;
            _focusCell = _grid.Index(_grid.Width / 2, _grid.Height / 2);
            return true;
        }

        if (input.IsPressed(ViewerKeys.ZoomIn) && _cellSize < MaxCellSize)
        {
            _cellSize = Math.Min(MaxCellSize, _cellSize * 2);
            moved = true;
        }

        if (input.IsPressed(ViewerKeys.ZoomOut) && _cellSize > _fitCellSize)
        {
            _cellSize = Math.Max(_fitCellSize, _cellSize / 2);
            moved = true;
        }

        var stepX = Math.Max(1, _viewWidth / _cellSize / 2);
        var stepY = Math.Max(1, _viewHeight / _cellSize / 2);
        var x = _grid.ColumnOf(_focusCell);
        var y = _grid.RowOf(_focusCell);

        if (input.IsPressed(ViewerKeys.PanLeft))
        {
            x -= stepX;
            moved = true;
        }

        if (input.IsPressed(ViewerKeys.PanRight))
        {
            x += stepX;
            moved = true;
        }

        if (input.IsPressed(ViewerKeys.PanUp))
        {
            y -= stepY;
            moved = true;
        }

        if (input.IsPressed(ViewerKeys.PanDown))
        {
            y += stepY;
            moved = true;
        }

        // Clamped to the map, not to what is visible: Viewing does the second
        // clamp, and doing it twice here would make a pan at the edge feel stuck
        // one press before it actually is.
        _focusCell = _grid.Index(
            Math.Clamp(x, 0, _grid.Width - 1),
            Math.Clamp(y, 0, _grid.Height - 1));

        return moved;
    }

    /// <summary>
    /// A colour per unit, so a crowd is legible as individuals.
    /// </summary>
    /// <remarks>
    /// Hue spread by the golden ratio, which separates any number of ids without
    /// a palette to run out of. Stalled units are red and arrived ones are grey,
    /// because those two states are worth seeing at a glance and no amount of hue
    /// tells you them.
    /// <para>
    /// Each side then owns an ARC of the wheel rather than the whole of it. A
    /// full-wheel spread is the best answer to "which unit is that" and the worst
    /// to "whose unit is that", and in a fight the second question is asked far
    /// more often.
    /// </para>
    /// </remarks>
    private static RgbaColor ColourFor(AgentState agent)
    {
        if (!agent.Alive)
        {
            // Out of the world: it keeps its id and its last cell, holds nothing,
            // and no verb accepts it. Darker than the walls, so a body reads as
            // scenery rather than as a unit that has stopped taking orders.
            return RgbaColor.Rgb(55, 55, 60);
        }

        if (agent.Stuck)
        {
            // Blocked-and-waiting dims toward the terrain; blocked-and-probing
            // burns bright. QUEUED MUST NOT LOOK LIKE REFUSED — a unit doing
            // nothing visible reads as ignoring the order unless the display
            // says "in the queue", which is the recorded failure mode this
            // colour split exists to close.
            return agent.Waiting ? RgbaColor.Rgb(190, 120, 120) : RgbaColor.Red;
        }

        if (agent.Arrived)
        {
            return RgbaColor.Rgb(130, 130, 130);
        }

        if (agent.Waiting)
        {
            // Held by a doctrine (a metered gate, a queue) without ever having
            // failed: patient, not broken. Dim, desaturated, unmistakably
            // "standing in line".
            return RgbaColor.Rgb(150, 150, 170);
        }

        // The golden ratio's conjugate one dimension down: ids land as far apart
        // inside the arc as they can, the way the golden angle spread them around
        // the whole wheel before sides existed. Stepping the angle itself and
        // then folding it into a narrower arc does NOT work -- 137.5 degrees
        // modulo a 130-degree arc is a 7-degree step, and consecutive ids come
        // out the same colour.
        var spread = agent.Id * 0.381966f % 1f;
        var hue = ((agent.Side * SideArcSpacing) + (spread * SideArcWidth)) % 360f;
        var sector = hue / 60f;
        var x = 1f - Math.Abs((sector % 2f) - 1f);

        var (r, g, b) = (int)sector switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };

        // Toward white a little, so every unit reads against the dark terrain.
        return RgbaColor.Rgb(
            (byte)((0.35f + (0.65f * r)) * 255),
            (byte)((0.35f + (0.65f * g)) * 255),
            (byte)((0.35f + (0.65f * b)) * 255));
    }

    private RectF DragRect()
    {
        var x = Math.Min(_dragAnchor.X, _dragCurrent.X);
        var y = Math.Min(_dragAnchor.Y, _dragCurrent.Y);
        return new RectF(x, y, Math.Abs(_dragCurrent.X - _dragAnchor.X), Math.Abs(_dragCurrent.Y - _dragAnchor.Y));
    }

    private static bool IsBox(RectF rect) => rect.Width >= ClickSlopPixels || rect.Height >= ClickSlopPixels;

    private void CommitDrag()
    {
        // A tiny drag is a click, and the press already handled it.
        var box = DragRect();
        if (!IsBox(box))
        {
            return;
        }

        // The box replaces the press's nearest-unit guess. Boxing empty ground
        // clears the selection, the way every RTS reads that gesture.
        var inside = new List<int>();
        foreach (var agent in _session.Agents)
        {
            var at = CenterOfCell(agent.Cell);
            if (at.X >= box.X && at.X <= box.Right && at.Y >= box.Y && at.Y <= box.Bottom)
            {
                inside.Add(agent.Id);
            }
        }

        _session.Select(inside);
    }

    private int NearestAgentTo(int cell)
    {
        var x = _grid.ColumnOf(cell);
        var y = _grid.RowOf(cell);

        var best = -1;
        var bestDistance = double.PositiveInfinity;

        foreach (var agent in _session.Agents)
        {
            var distance = Movement.OctileDistance(
                _grid.ColumnOf(agent.Cell), _grid.RowOf(agent.Cell), x, y);

            if (distance < bestDistance)
            {
                best = agent.Id;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// The status line's text, read off the session each time it is called.
    /// </summary>
    /// <remarks>
    /// Marked as well as <see cref="IViewerApp.StatusText"/>, because the
    /// property is an auto-getter and the question is asked HERE: this is what
    /// reaches the tick report, the field cache and every agent.
    /// </remarks>
    [Observes]
    private string BuildStatus()
    {
        if (_loadError is { } refusal)
        {
            return $"load failed: {refusal}";
        }

        var agents = _session.Agents;
        var planning = agents.Count(a => a.Thinking);
        var arrived = agents.Count(a => a.Arrived);
        var stuck = agents.Count(a => a.Stuck);

        // Every counter goes through Fixed, the ROSTER INCLUDED, and Fixed pads
        // to a constant -- see CounterDigits. So the line holds its length while
        // the numbers change and while a live world brings units on. A breathing
        // status line jitters in place -- and in a window sized to content it
        // shook the whole window.
        static string Fixed(int value) =>
            value.ToString(CultureInfo.InvariantCulture).PadLeft(CounterDigits);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_grid.Width}x{_grid.Height}  {Fixed(agents.Count)} units  {Fixed(arrived)} arrived  {Fixed(stuck)} stuck  " +
            $"{Fixed(planning)} planning  fields {_session.LiveFields}/{MovementSystem.FieldCapacity}  " +
            $"{_session.LastTick.NodesSpent,6} nodes/tick  " +
            $"tick {_session.CurrentTick,6}  {(_session.Running ? "[running]" : "[paused]"),-9} " +
            $"{PaceLabel,-8} sel {Fixed(_session.Selection.Count)}  " +
            $"LMB click/drag select  RMB order  {Hints()}");
    }

    /// <summary>
    /// The key hints, read off the keymap rather than written out.
    /// </summary>
    /// <remarks>
    /// A literal here was correct exactly as long as nothing could be rebound,
    /// and the whole point of the keymap is that something can be. A status line
    /// that is confidently wrong about a key is worse than one that omits it.
    /// <para>
    /// Only actions that DO something appear. The two overlay keys are bound and
    /// carried and do nothing yet; hinting them would be the same lie one step
    /// earlier. The viewpoint key is hinted only where a viewer was actually
    /// wired to somebody's knowledge, which is the same rule and not an
    /// exception to it.
    /// </para>
    /// </remarks>
    private string Hints() =>
        $"{Keys.KeycapFor(ViewerKeys.Space)} {Does(ViewerKeys.Space)}  " +
        $"{Keys.KeycapFor(ViewerKeys.Step)} {Does(ViewerKeys.Step)}  " +
        $"{Keys.KeycapFor(ViewerKeys.Pace)} {Does(ViewerKeys.Pace)}  " +
        $"{Keys.KeycapFor(ViewerKeys.R)} {Does(ViewerKeys.R)}" +
        Eyes();

    /// <summary>
    /// Whose eyes the board is currently drawn through, or nothing at all where
    /// there are no eyes to borrow.
    /// </summary>
    /// <remarks>
    /// Padded to a constant width, like every counter in the line: "observer"
    /// and "side 0" are different lengths, and a status line that changes length
    /// shakes a window sized to its content.
    /// </remarks>
    private string Eyes()
    {
        if (_eyes is null)
        {
            return string.Empty;
        }

        var whose = _viewpoint < 0 ? "observer" : $"side {Number(_viewpoint)}";
        return $"  {Keys.KeycapFor(ViewerKeys.Viewpoint)} view {whose,-8}";
    }

    /// <summary>
    /// What one action does, in the words the status line and the controls
    /// folder both use.
    /// </summary>
    /// <remarks>
    /// ONE VOCABULARY, so the hint at the end of the status line and the row in
    /// the panel cannot come to disagree about the same key. They already share
    /// the keycap -- that is what <see cref="Keymap"/> is for -- and this is the
    /// other half of the same sentence.
    /// <para>
    /// <b>An action that does nothing right now says so.</b> Two of the overlays
    /// are bound and carried and not wired to anything, and the viewpoint key is
    /// inert in a viewer nobody lent eyes to. The status line deals with that by
    /// leaving them out; the folder cannot, because a folder that quietly omits
    /// three of the fourteen keys is a list the reader stops trusting. So they
    /// appear, and what they say is what pressing them will actually get you.
    /// </para>
    /// </remarks>
    private string Does(ViewerKeys action) => action switch
    {
        ViewerKeys.Space => "pause",
        ViewerKeys.Step => "step",
        ViewerKeys.Pace => "pace",
        ViewerKeys.R => _session.CanRestart ? "restart" : "regroup",
        ViewerKeys.PanLeft => "pan left",
        ViewerKeys.PanRight => "pan right",
        ViewerKeys.PanUp => "pan up",
        ViewerKeys.PanDown => "pan down",
        ViewerKeys.ZoomIn => "zoom in",
        ViewerKeys.ZoomOut => "zoom out",
        ViewerKeys.ResetView => "whole map again",
        ViewerKeys.Viewpoint => _eyes is null ? "cycle viewpoint (nothing to cycle here)" : "cycle viewpoint",
        ViewerKeys.PathOverlay => "route overlay (not wired yet)",
        ViewerKeys.LosOverlay => "sight overlay (not wired yet)",
        _ => "-",
    };

    /// <summary>
    /// The controls, one row per bound key, read off the keymap.
    /// </summary>
    /// <remarks>
    /// <b>Generated, never written out.</b> The rows come from
    /// <see cref="Keymap.Bindings"/>, which is the same map the hosts translate
    /// through and the status line hints from -- so a rebound key moves in all
    /// three at once and none of them can be left claiming the old one. A
    /// hand-kept list here would be a second source of truth about the one thing
    /// a reader has no way to check except by pressing the key.
    /// </remarks>
    private List<DebugRow> ControlRows()
    {
        var rows = new List<DebugRow>(Keys.Bindings.Count);
        foreach (var (keycap, action) in Keys.Bindings)
        {
            rows.Add(new DebugRow(Controls, keycap, Does(action)));
        }

        return rows;
    }

    /// <summary>
    /// The watched unit spelled out: what the movement layer says about it, what
    /// every other source says about it, the few facts only this class can
    /// answer, and last the controls.
    /// </summary>
    /// <remarks>
    /// <b>Most of this is no longer written here.</b> The bulk of the panel used
    /// to be rows hand-built out of <see cref="AgentState"/> and
    /// <see cref="PlanResult"/>, which is a second vocabulary for facts
    /// <see cref="MovementSystem.DebugFor"/> already reports — and reports
    /// better, because it reaches the slot, the blocked count and the retry
    /// gate's REMAINING ticks, none of which are on the per-tick snapshot at
    /// all. Two shapes for the same unit is the duplication this replaces.
    /// <para>
    /// <b>The viewer's own rows are a group of their own, and the separation is
    /// the point.</b> A row under <c>Viewer</c> is one the movement layer cannot
    /// answer, because it is not a fact about the unit: it is about what got
    /// DRAWN — the wait marks on the route, the no-route cross — or about what
    /// got SELECTED. Mixed in among the rest they would read as simulation
    /// state, and somebody would go looking for the wait count in Nav.Core.
    /// </para>
    /// <para>
    /// Rebuilt whole each time rather than cached: a cache is one more thing
    /// that can go on describing a unit after it moved, and this is a page of
    /// strings for ONE unit, built only while somebody is watching it.
    /// </para>
    /// <para>
    /// <b>The order is movement layer, then each source in the order it was
    /// supplied, then the viewer's own group.</b> The eye tracks a number by where
    /// it sits, so the one thing a panel may never do is reshuffle: the movement
    /// layer stays where it has always been, a source lands after everything
    /// added before it, and adding a second source cannot move the first one's
    /// rows. <see cref="IWorldDebugView"/> promises no grouping, so the position
    /// of a block is the only ordering anyone gets and it is worth being strict
    /// about.
    /// </para>
    /// <para>
    /// Marked as well as <see cref="IViewerApp.Inspector"/>, for the same reason
    /// <see cref="BuildStatus"/> is: the property hands back a field, and this is
    /// where the movement layer is actually asked.
    /// </para>
    /// </remarks>
    [Observes]
    private IReadOnlyList<DebugRow> BuildInspector()
    {
        const string Viewer = "Viewer";

        var selection = _session.Selection;
        if (selection.Count == 0)
        {
            // THE CONTROLS ARE NOT SOMETHING A SELECTION REVEALS. This used to
            // answer with nothing at all, which is defensible while every row is
            // about a unit and indefensible the moment one of them is about the
            // keyboard: the instant a reader most needs to know which key does
            // what is BEFORE they have worked out how to select anything.
            return ControlRows();
        }

        // The lowest id, because ViewerSession keeps the selection in ascending
        // order and a box-select has no other stable first member.
        var watched = selection[0];
        var rows = new List<DebugRow>(_session.DebugFor(watched).Describe());

        // Headings already spoken for. A source is free to call a group anything,
        // and without this a source that picked a name the movement layer already
        // uses would land its rows under that heading and read as the movement
        // layer's own answers. Viewer is reserved from the start for the same
        // reason, one block further down.
        //
        // The names shipped today were pulled apart so no such collision happens
        // -- the movement layer says Agent and the tactics view says Condition --
        // and that changes nothing here: what a source calls its groups is not
        // this panel's to know in advance.
        var taken = new HashSet<string>(StringComparer.Ordinal) { Viewer, Controls };
        foreach (var row in rows)
        {
            taken.Add(row.Group);
        }

        // A source that broke, reported after everything that worked. Collected
        // rather than written in place so a failure cannot push the rows above it
        // around -- which is the one thing an instrument must not do on the frame
        // something goes wrong.
        var broke = new List<DebugRow>();
        for (var i = 0; i < _sources.Length; i++)
        {
            List<DebugRow> supplied;
            try
            {
                supplied = RowsFrom(_sources[i], watched, taken);
            }
            catch (Exception e)
            {
                // EVERY exception, deliberately. A source is somebody else's code
                // reached through an interface, so which ones it can raise is not
                // knowable here, and the panel is an instrument: a source that
                // throws loses its rows and says so, and the unit is still
                // described. Losing the whole block rather than the rows after
                // the throw is also deliberate -- half a source's page, with no
                // way to tell which half is missing, is worse than none of it.
                broke.Add(new DebugRow(
                    Viewer,
                    $"source {Number(i + 1)}",
                    $"threw {e.GetType().Name}",
                    e.Message.ReplaceLineEndings(" ")));
                continue;
            }

            foreach (var row in supplied)
            {
                taken.Add(row.Group);
            }

            rows.AddRange(supplied);
        }

        if (selection.Count > 1)
        {
            // Otherwise the panel silently describes one unit of a boxed group
            // and reads as though the box only caught one.
            rows.Add(new DebugRow(Viewer, "others", $"{Number(selection.Count - 1)} also selected"));
        }

        PlanResult? route = null;
        foreach (var (id, plan) in _session.CurrentPlans())
        {
            if (id == watched)
            {
                route = plan;
                break;
            }
        }

        rows.Add(new DebugRow(Viewer, "waits", route is null ? "-" : Number(WaitCount(route))));

        // The orange cross, as a row. Render decides it from four conditions and
        // the panel is the only place a watcher can read WHY the unit is crossed
        // out, so both go through the one predicate rather than agreeing by hand.
        var agent = _session.Agents[watched];
        rows.Add(HasNoRoute(agent, route is not null)
            ? new DebugRow(Viewer, "no route", "yes", "crossed out on the map")
            : new DebugRow(Viewer, "no route", "no"));

        rows.AddRange(broke);

        // LAST, so that adding it moved nothing. Every row above this line is
        // where it was before there was a controls folder, and a reader who
        // knows the keys never has to look past the unit again -- which is also
        // the argument for folding this one shut, if anyone wants it shut.
        rows.AddRange(ControlRows());

        return rows;
    }

    /// <summary>
    /// One source's page: what it says about the watched unit, then what it says
    /// about itself, with any heading the panel has already used renamed.
    /// </summary>
    /// <remarks>
    /// <b>The unit's rows come first and the source's own rows follow them.</b>
    /// The panel's subject is one unit, and a source's <c>Describe</c> is the
    /// setup it is fighting in -- the rates, the board, what the fog is doing.
    /// That is context worth having on screen beside a unit losing health at some
    /// rate, and it reads the same whoever is watched, so it belongs under the
    /// answers that change rather than over them. Dropping it was the alternative,
    /// and it would have thrown away the half of <see cref="IWorldDebugView"/>
    /// that says what the numbers mean.
    /// <para>
    /// <b>A group name already in the panel is renamed rather than merged or
    /// dropped.</b> Two blocks called <c>Unit</c>, interleaved, would be a panel
    /// quietly claiming that a tactics world's rows are the movement layer's --
    /// the one outcome worth going out of the way to prevent. Dropping the rows
    /// instead would lose whatever the source had to say for the sake of a name
    /// collision it cannot see. So the second one becomes <c>Unit (2)</c>, the
    /// third <c>Unit (3)</c>, every row of that group inside this source gets the
    /// same new name, and a host printing a heading on each change still prints
    /// one heading per block.
    /// </para>
    /// <para>
    /// An id the source has never heard of needs nothing here:
    /// <see cref="IWorldDebugView.DebugFor"/> answers for any int, and a source
    /// with nothing to say about one contributes no rows and therefore no heading.
    /// </para>
    /// </remarks>
    private static List<DebugRow> RowsFrom(IWorldDebugView source, int watched, IReadOnlySet<string> taken)
    {
        var unit = source.DebugFor(watched).Describe();
        var world = source.Describe();

        var rows = new List<DebugRow>(unit.Count + world.Count);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in unit.Concat(world))
        {
            if (!renamed.TryGetValue(row.Group, out var heading))
            {
                heading = row.Group;
                var attempt = 2;
                while (taken.Contains(heading) || renamed.ContainsValue(heading))
                {
                    heading = $"{row.Group} ({Number(attempt)})";
                    attempt++;
                }

                renamed[row.Group] = heading;
            }

            rows.Add(row with { Group = heading });
        }

        return rows;
    }

    /// <summary>
    /// Whether the map crosses this unit out: nothing to walk, and none of the
    /// ordinary reasons to have nothing.
    /// </summary>
    /// <remarks>
    /// The conditions are argued at the call site in <see cref="Render"/>, which
    /// is where the drawing decision is made. It is a method rather than a line
    /// there because the inspector reports the same fact, and a panel that
    /// disagreed with the map about which units are crossed out would be worse
    /// than no panel.
    /// </remarks>
    private static bool HasNoRoute(AgentState agent, bool routed) =>
        !routed && agent.Alive && !agent.Arrived && !agent.Thinking;

    /// <summary>
    /// Ticks the plan spends standing still: a cell repeated from the one before
    /// it.
    /// </summary>
    /// <remarks>
    /// The raw count, every repeat. The map marks only the ones in the plan's
    /// interior, because the run at its head is the planner's latency and a dot
    /// per tick of that lands on top of the unit and says nothing. The panel is
    /// where the unfiltered number belongs, so what was drawn can be read
    /// against what there was to draw.
    /// </remarks>
    private static int WaitCount(PlanResult plan)
    {
        var waits = 0;
        for (var i = 1; i < plan.Cells.Count; i++)
        {
            if (plan.Cells[i - 1] == plan.Cells[i])
            {
                waits++;
            }
        }

        return waits;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
