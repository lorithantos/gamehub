using System.Globalization;
using System.Numerics;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The viewer, with no window and no renderer: pick a start, pick a goal, solve,
/// walk it.
/// </summary>
/// <remarks>
/// Everything the old top-level <c>Program.cs</c> did between reading the map and
/// touching raylib now lives here, and this type references nothing but
/// <c>Nav.Core</c> and the seam. That it compiles in a project with no graphics
/// package is the claim the whole exercise is testing.
/// </remarks>
public sealed class ViewerApp : IViewerApp
{
    private const double SpeedCellsPerSecond = 4.0;

    private readonly Grid _grid;
    private readonly TerrainImage _terrain;
    private readonly FixedTimestep _clock = new();

    private int _start;
    private int _goal;
    private PathResult _result;
    private Walker? _walker;
    private bool _running;

    public ViewerApp(Grid grid, GridLayout layout)
    {
        ArgumentNullException.ThrowIfNull(grid);

        _grid = grid;
        Layout = layout;
        _terrain = TerrainImage.FromGrid(grid, RgbaColor.RayWhite, RgbaColor.DarkGray);

        // Something to look at before the first click, on any map.
        _start = EndPassableCell(grid, fromEnd: false);
        _goal = EndPassableCell(grid, fromEnd: true);
        _result = PathFinder.FindPath(grid, _start, _goal);
        _walker = MakeWalker(_result, grid);
        StatusText = BuildStatus();
    }

    public GridLayout Layout { get; }

    public string StatusText { get; private set; }

    public int Start => _start;

    public int Goal => _goal;

    /// <summary>
    /// The current solution. Exposed so a test can assert the <em>same
    /// reference</em> survives an input that should not have recomputed
    /// anything — a cheaper and stricter check than comparing costs.
    /// </summary>
    public PathResult Result => _result;

    public Walker? Walker => _walker;

    public bool Running => _running;

    public void Update(in InputState input, float deltaSeconds)
    {
        if (input.IsPressed(MouseButtons.Left) && Layout.TryPick(input.MousePosition, _grid, out var picked))
        {
            _start = picked;
            Recompute();
        }
        else if (input.IsPressed(MouseButtons.Right) && Layout.TryPick(input.MousePosition, _grid, out picked))
        {
            _goal = picked;
            Recompute();
        }

        if (input.IsPressed(ViewerKeys.Space))
        {
            _running = !_running;
        }

        if (input.IsPressed(ViewerKeys.R))
        {
            _walker?.Reset();
            _running = false;
        }

        // Fixed steps, so the walk is reproducible rather than tied to frame
        // time. Reset while paused, or the accumulator banks the pause and
        // spends it in one burst on resume.
        if (_running && _walker is not null && !_walker.Arrived)
        {
            var steps = _clock.Accumulate(deltaSeconds);
            for (var i = 0; i < steps; i++)
            {
                _walker.Advance(_clock.Step);
            }
        }
        else
        {
            _clock.Reset();
        }

        StatusText = BuildStatus();
    }

    public void Render(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        renderer.BeginFrame(RgbaColor.Black);
        renderer.DrawTerrain(_terrain, new RectF(0, 0, Layout.PixelWidth, Layout.PixelHeight));

        if (_result.Found)
        {
            var thickness = Math.Max(1.0f, Layout.CellSize * 0.15f);
            for (var i = 1; i < _result.Cells.Count; i++)
            {
                renderer.DrawLine(
                    CenterOfCell(_result.Cells[i - 1]),
                    CenterOfCell(_result.Cells[i]),
                    thickness,
                    RgbaColor.SkyBlue);
            }
        }

        var marker = Math.Max(2.0f, Layout.CellSize * 0.3f);
        renderer.DrawCircle(CenterOfCell(_start), marker, RgbaColor.Green);
        renderer.DrawCircle(CenterOfCell(_goal), marker, RgbaColor.Red);

        if (_walker is not null)
        {
            renderer.DrawCircle(
                Layout.CenterOf(_walker.X, _walker.Y),
                Math.Max(2.0f, Layout.CellSize * 0.35f),
                RgbaColor.Orange);
        }

        renderer.EndFrame();
    }

    public Vector2 CenterOfCell(int cell) => Layout.CenterOf(_grid.ColumnOf(cell), _grid.RowOf(cell));

    private void Recompute()
    {
        _result = PathFinder.FindPath(_grid, _start, _goal);
        _walker = MakeWalker(_result, _grid);
        _clock.Reset();
    }

    private string BuildStatus()
    {
        var path = _result.Found
            ? $"cost {_result.Cost.ToString("F5", CultureInfo.InvariantCulture)}  steps {_result.StepCount}  expanded {_result.Expanded}"
            : $"no path  expanded {_result.Expanded}";

        return $"{_grid.Width}x{_grid.Height}  {path}   [{(_running ? "running" : "paused")}]  " +
               "LMB start  RMB goal  SPACE walk  R reset";
    }

    private static Walker? MakeWalker(PathResult path, Grid map) =>
        path.Found ? new Walker(path.Cells, map.Width, SpeedCellsPerSecond) : null;

    private static int EndPassableCell(Grid map, bool fromEnd)
    {
        if (fromEnd)
        {
            for (var i = map.CellCount - 1; i >= 0; i--)
            {
                if (map.IsPassable(i))
                {
                    return i;
                }
            }
        }
        else
        {
            for (var i = 0; i < map.CellCount; i++)
            {
                if (map.IsPassable(i))
                {
                    return i;
                }
            }
        }

        throw new InvalidOperationException("The map has no passable cell.");
    }
}
