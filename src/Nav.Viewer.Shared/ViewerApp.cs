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

    // All derived from the session's content, and all rebuilt when its Version
    // moves: a load replaces the map, and everything below follows the map.
    private Grid _grid;
    private TerrainImage _terrain;
    private FixedTimestep _clock;
    private int[] _previousCells;
    private int _sessionVersion;

    /// <summary>How far through the current tick we are, for drawing between cells.</summary>
    private float _blend;

    private bool _dragging;
    private Vector2 _dragAnchor;
    private Vector2 _dragCurrent;

    /// <summary>Why the last load was refused, shown until the next input.</summary>
    private string? _loadError;

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

    public GridLayout Layout { get; private set; }

    public string StatusText { get; private set; }

    public string WindowTitle => $"Nav.Viewer - {_session.MapName}";

    public ViewerSession Session => _session;

    /// <summary>The units orders go to, in id order.</summary>
    public IReadOnlyList<int> Selection => _session.Selection;

    public bool Running => _session.Running;

    public IReadOnlyList<AgentState> Agents => _session.Agents;

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

    public void Render(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        renderer.BeginFrame(RgbaColor.Black);
        renderer.DrawTerrain(_terrain, new RectF(0, 0, Layout.PixelWidth, Layout.PixelHeight));

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
        foreach (var agent in _session.Agents)
        {
            var from = CenterOfCell(_previousCells[agent.Id]);
            var to = CenterOfCell(agent.Cell);
            var at = Vector2.Lerp(from, to, _blend);

            renderer.DrawCircle(at, radius, ColourFor(agent));

            if (selection.Contains(agent.Id))
            {
                // A ring, drawn as a slightly larger circle underneath would be —
                // but the seam has no stroke, so the selection is a second smaller
                // dot on top. Five verbs is five verbs.
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
        Layout = GridLayout.Fit(_grid, _fitWidth, _fitHeight);
        _terrain = TerrainImage.FromGrid(_grid, RgbaColor.RayWhite, RgbaColor.DarkGray);
        _clock = new FixedTimestep(_session.TickSeconds);
        _previousCells = [.. _session.Agents.Select(a => a.Cell)];
        _blend = 0f;
        _dragging = false;
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
            return RgbaColor.Red;
        }

        if (agent.Arrived)
        {
            return RgbaColor.Rgb(130, 130, 130);
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
            $"{Fixed(planning)} planning  {_session.LastTick.NodesSpent,6} nodes/tick  " +
            $"tick {_session.CurrentTick,6}  {(_session.Running ? "[running]" : "[paused]"),-9} " +
            $"sel {Fixed(_session.Selection.Count)}  LMB click/drag select  RMB order  SPACE pause  S step  " +
            $"{(_session.IsReplay ? "R restart" : "R regroup")}");
    }
}
