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

    private readonly Grid _grid;
    private readonly TerrainImage _terrain;
    private readonly MovementSystem _system;
    private readonly FixedTimestep _clock = new(TickSeconds);
    private readonly int[] _previousCells;

    /// <summary>How far through the current tick we are, for drawing between cells.</summary>
    private float _blend;

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
        Selected = _system.Agents.Count > 0 ? 0 : -1;
        StatusText = BuildStatus();
    }

    public GridLayout Layout { get; }

    public string StatusText { get; private set; }

    /// <summary>The unit whose route is drawn, or -1.</summary>
    public int Selected { get; private set; }

    public bool Running => _running;

    public IReadOnlyList<AgentState> Agents => _system.Agents;

    public int CurrentTick => _system.CurrentTick;

    public void Update(in InputState input, float deltaSeconds)
    {
        if (input.IsPressed(MouseButtons.Left) && Layout.TryPick(input.MousePosition, _grid, out var picked))
        {
            Selected = NearestAgentTo(picked);
        }
        else if (input.IsPressed(MouseButtons.Right) &&
                 Layout.TryPick(input.MousePosition, _grid, out var target) &&
                 Selected >= 0)
        {
            _system.Order([Selected], target);
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

        // Only the selected unit's route. Drawing every route at two dozen units
        // is a ball of yarn, and at two hundred it is a solid colour.
        var plans = _system.CurrentPlans();
        foreach (var (agent, plan) in plans)
        {
            if (agent != Selected || plan.Cells.Count < 2)
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

            if (agent.Id == Selected)
            {
                // A ring, drawn as a slightly larger circle underneath would be —
                // but the seam has no stroke, so the selection is a second smaller
                // dot on top. Five verbs is five verbs.
                renderer.DrawCircle(at, radius * 0.35f, RgbaColor.Black);
            }
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

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_grid.Width}x{_grid.Height}  {agents.Count} units  {arrived} arrived  {stuck} stuck  " +
            $"{planning} planning  {_system.LastTick.NodesSpent} nodes/tick  " +
            $"tick {_system.CurrentTick}  [{(_running ? "running" : "paused")}]  " +
            $"sel {Selected}  LMB select  RMB order  SPACE pause  R regroup");
    }
}
