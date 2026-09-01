using System.Globalization;
using System.Numerics;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The viewer, with no window and no renderer: a squad of units, orders, and a
/// clock.
/// </summary>
/// <remarks>
/// References nothing but <c>Nav.Core</c> and the seam, and compiles in a project
/// with no graphics package. That is the claim the two-renderer experiment was
/// built to test, and it still holds now that the app drives a whole
/// <see cref="MovementSystem"/> rather than one walker.
/// <para>
/// <b><see cref="IRenderer"/> did not have to grow.</b> Many units are
/// <c>DrawCircle</c> in a loop; the selected unit's route is the same
/// <c>DrawLine</c> the single-agent viewer used. If a milestone-2 display need had
/// required a sixth verb, that would have been the seam's first genuine leak and
/// worth recording — it did not.
/// </para>
/// </remarks>
public sealed class ViewerApp : IViewerApp
{
    private const int Squad = 24;

    /// <summary>Seconds of simulation per tick, matching the recorded-scenario default.</summary>
    private const double TickSeconds = 1.0 / 60.0;

    /// <summary>A drag smaller than this in both axes is a click.</summary>
    private const float ClickSlopPixels = 4.0f;

    private readonly Grid _grid;
    private readonly TerrainImage _terrain;
    private readonly MovementSystem _system;
    private readonly FixedTimestep _clock = new(TickSeconds);
    private readonly int[] _previousCells;
    private readonly List<int> _selection = [];

    /// <summary>How far through the current tick we are, for drawing between cells.</summary>
    private float _blend;

    private bool _dragging;
    private Vector2 _dragAnchor;
    private Vector2 _dragCurrent;

    private bool _running = true;

    public ViewerApp(Grid grid, GridLayout layout, int squad = Squad)
    {
        ArgumentNullException.ThrowIfNull(grid);

        _grid = grid;
        Layout = layout;
        _terrain = TerrainImage.FromGrid(grid, RgbaColor.RayWhite, RgbaColor.DarkGray);
        _system = new MovementSystem(grid);

        // A squad to look at before the first click, on any map.
        var placed = 0;
        for (var cell = 0; cell < grid.CellCount && placed < squad; cell++)
        {
            if (!grid.IsPassable(cell))
            {
                continue;
            }

            _system.AddAgent(cell);
            placed++;
        }

        _previousCells = [.. _system.Agents.Select(a => a.Cell)];
        if (_system.Agents.Count > 0)
        {
            _selection.Add(0);
        }

        StatusText = BuildStatus();
    }

    public GridLayout Layout { get; }

    public string StatusText { get; private set; }

    /// <summary>The units orders go to, in id order.</summary>
    public IReadOnlyList<int> Selection => _selection;

    public bool Running => _running;

    public IReadOnlyList<AgentState> Agents => _system.Agents;

    public int CurrentTick => _system.CurrentTick;

    public void Update(in InputState input, float deltaSeconds)
    {
        if (input.IsPressed(MouseButtons.Left) && Layout.TryPick(input.MousePosition, _grid, out var picked))
        {
            // A press selects the nearest unit immediately, the way a click
            // always did. If it turns out to be a drag, the box replaces this
            // on release.
            _selection.Clear();
            var nearest = NearestAgentTo(picked);
            if (nearest >= 0)
            {
                _selection.Add(nearest);
            }

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
            Layout.TryPick(input.MousePosition, _grid, out var target) &&
            _selection.Count > 0)
        {
            _system.Order([.. _selection], target);
        }

        if (input.IsPressed(ViewerKeys.Space))
        {
            _running = !_running;
        }

        if (input.IsPressed(ViewerKeys.R))
        {
            // Everybody home. The nearest thing to a reset that means anything
            // once units have scattered.
            _system.Order([.. Enumerable.Range(0, _system.Agents.Count)], _previousCells[0]);
        }

        if (_running)
        {
            var steps = _clock.Accumulate(deltaSeconds);
            for (var i = 0; i < steps; i++)
            {
                var before = _system.Agents;
                for (var id = 0; id < _previousCells.Length; id++)
                {
                    _previousCells[id] = before[id].Cell;
                }

                _system.Tick();
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
        var soleSelection = _selection.Count == 1 ? _selection[0] : -1;
        var plans = _system.CurrentPlans();
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
        foreach (var agent in _system.Agents)
        {
            var from = CenterOfCell(_previousCells[agent.Id]);
            var to = CenterOfCell(agent.Cell);
            var at = Vector2.Lerp(from, to, _blend);

            renderer.DrawCircle(at, radius, ColourFor(agent));

            if (_selection.Contains(agent.Id))
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
        _selection.Clear();
        foreach (var agent in _system.Agents)
        {
            var at = CenterOfCell(agent.Cell);
            if (at.X >= box.X && at.X <= box.Right && at.Y >= box.Y && at.Y <= box.Bottom)
            {
                _selection.Add(agent.Id);
            }
        }
    }

    private int NearestAgentTo(int cell)
    {
        var x = _grid.ColumnOf(cell);
        var y = _grid.RowOf(cell);

        var best = -1;
        var bestDistance = double.PositiveInfinity;

        foreach (var agent in _system.Agents)
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
        var agents = _system.Agents;
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
            $"{Fixed(planning)} planning  {_system.LastTick.NodesSpent,6} nodes/tick  " +
            $"tick {_system.CurrentTick,6}  {(_running ? "[running]" : "[paused]"),-9} " +
            $"sel {Fixed(_selection.Count)}  LMB click/drag select  RMB order  SPACE pause  R regroup");
    }
}
