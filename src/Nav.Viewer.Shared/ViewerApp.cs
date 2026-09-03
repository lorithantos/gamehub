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
/// Since the <see cref="ViewerSession"/> extraction this class owns only
/// presentation: the layout, the terrain image, wall-clock accumulation, frame
/// blending, and the drag rectangle. Everything with a decision in it — what is
/// loaded, who is selected, whether time runs — lives in the session, and this
/// class translates input into session commands and session state into the five
/// renderer verbs.
/// </para>
/// <para>
/// <b><see cref="IRenderer"/> did not have to grow.</b> Many units are
/// <c>DrawCircle</c> in a loop; the selected unit's route is the same
/// <c>DrawLine</c> the single-agent viewer used; the drag band is four lines.
/// If a milestone-2 display need had required a sixth verb, that would have been
/// the seam's first genuine leak and worth recording — it did not.
/// </para>
/// </remarks>
public sealed class ViewerApp : IViewerApp
{
    /// <summary>A drag smaller than this in both axes is a click.</summary>
    private const float ClickSlopPixels = 4.0f;

    private readonly ViewerSession _session;
    private readonly int _fitWidth;
    private readonly int _fitHeight;

    /// <summary>Never zoom past a cell this big; beyond it a screen holds nothing.</summary>
    private const int MaxCellSize = 48;

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
    /// retreat or a patrol turn is over in a second or two of wall clock, which
    /// is fine for a soak test and useless for seeing what a doctrine did; two
    /// ticks a second is about reading speed, and one is for watching a single
    /// decision land.
    /// </remarks>
    private static readonly double?[] Paces = [null, 0.5, 1.0];

    private int _pace;

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
    /// </remarks>
    public ViewerApp(ViewerSession session, int maxPixelWidth, int maxPixelHeight)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxPixelHeight, 0);

        _session = session;
        _fitWidth = maxPixelWidth;
        _fitHeight = maxPixelHeight;
        AdoptContent();
        StatusText = BuildStatus();
    }

    /// <summary>
    /// Convenience for tests and callers that have content rather than a
    /// session: wraps it in one. The layout's own pixel box is handed back as
    /// the fit budget, which reproduces the identical layout because
    /// <see cref="GridLayout.Fit"/> is a fixed point over its own output.
    /// </summary>
    public ViewerApp(Grid grid, GridLayout layout, int squad = ViewerSession.DefaultSquad, RecordedScenario? scenario = null)
        : this(BuildSession(grid, scenario, squad), layout.PixelWidth, layout.PixelHeight)
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
    /// The app owns the <em>string</em> because <see cref="IRenderer"/> has no
    /// text verb by design; each host owns how it is shown. Its counters are
    /// padded to a width they cannot outgrow, so the line never changes length
    /// while the numbers do -- a breathing line shook a window sized to content.
    /// </summary>
    public string StatusText { get; private set; }

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
            for (var id = 0; id < _previousCells.Length; id++)
            {
                _previousCells[id] = _session.Agents[id].Cell;
            }

            _session.Tick();

            // Draw the completed tick, not a blend into it: a step should land
            // exactly on the state it produced.
            for (var id = 0; id < _previousCells.Length; id++)
            {
                _previousCells[id] = _session.Agents[id].Cell;
            }

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

        if (input.IsPressed(ViewerKeys.R))
        {
            if (_session.IsReplay)
            {
                // Reload the recording: tick zero, clock stopped, ready to
                // watch again.
                _session.Restart();
                for (var id = 0; id < _previousCells.Length; id++)
                {
                    _previousCells[id] = _session.Agents[id].Cell;
                }

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
                var before = _session.Agents;
                for (var id = 0; id < _previousCells.Length; id++)
                {
                    _previousCells[id] = before[id].Cell;
                }

                _session.Tick();
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

        StatusText = BuildStatus();
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
    /// </remarks>
    public void Render(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        renderer.BeginFrame(RgbaColor.Black);
        // The map's own extent, placed at the camera's origin -- NOT the window,
        // which they stopped being the same rectangle when zooming arrived. The
        // renderer already took a destination rect, so scrolling and scaling cost
        // it nothing: it is one textured quad either way.
        renderer.DrawTerrain(
            _terrain,
            new RectF(Layout.OriginX, Layout.OriginY, Layout.MapWidth(_grid), Layout.MapHeight(_grid)));

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

            var thickness = Math.Max(1.0f, Layout.CellSize * 0.12f);
            for (var i = 1; i < plan.Cells.Count; i++)
            {
                if (plan.Cells[i - 1] == plan.Cells[i])
                {
                    continue;   // a wait draws nothing
                }

                renderer.DrawLine(
                    CenterOfCell(plan.Cells[i - 1]),
                    CenterOfCell(plan.Cells[i]),
                    thickness,
                    RgbaColor.SkyBlue);
            }
        }

        var radius = Math.Max(2.0f, Layout.CellSize * 0.34f);
        var leaders = _session.Leaders;
        foreach (var agent in _session.Agents)
        {
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
    /// a choice rather than a limitation of the input snapshot. An instrument is
    /// being read, not flown: half a screen is repeatable, lands in the same place
    /// twice, and cannot overshoot the thing being looked at. It also asks nothing
    /// new of the hosts, which report key presses as edges.
    /// <para>
    /// Zoom doubles and halves, so the scale is always a whole number of pixels
    /// per cell and a cell never straddles a half-pixel and shimmers. It floors at
    /// whatever fitted the whole map, because zooming out past that only adds
    /// margin.
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
    /// Hue stepped by the golden angle, which spreads any number of ids apart
    /// without a palette to run out of. Stalled units are red and arrived ones are
    /// grey, because those two states are worth seeing at a glance and no amount
    /// of hue tells you them.
    /// </remarks>
    private static RgbaColor ColourFor(AgentState agent)
    {
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

        var hue = agent.Id * 137.508f % 360f;
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

        // Every counter is padded to a width it cannot outgrow, so the line
        // never changes length while the numbers change. A breathing status
        // line jitters in place -- and in a window sized to content it shook
        // the whole window.
        var pad = agents.Count.ToString(CultureInfo.InvariantCulture).Length;
        string Fixed(int value) => value.ToString(CultureInfo.InvariantCulture).PadLeft(pad);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_grid.Width}x{_grid.Height}  {agents.Count} units  {Fixed(arrived)} arrived  {Fixed(stuck)} stuck  " +
            $"{Fixed(planning)} planning  fields {_session.LiveFields}/{MovementSystem.FieldCapacity}  " +
            $"{_session.LastTick.NodesSpent,6} nodes/tick  " +
            $"tick {_session.CurrentTick,6}  {(_session.Running ? "[running]" : "[paused]"),-9} " +
            $"{PaceLabel,-8} sel {Fixed(_session.Selection.Count)}  " +
            $"LMB click/drag select  RMB order  SPACE pause  S step  T pace  " +
            $"{(_session.IsReplay ? "R restart" : "R regroup")}");
    }
}
