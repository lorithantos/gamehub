using System.Globalization;
using System.Numerics;

using Nav.Core;
using Nav.Viewer;

using Raylib_cs;

// A thin host. Everything it knows how to do beyond drawing lives in Nav.Core,
// which is what keeps swapping this file for a WinForms one an hour's work.

const int MaxMapPixels = 1000;
const int StatusHeight = 26;
const double SpeedCellsPerSecond = 4.0;

Grid grid;
string mapName;

try
{
    if (args.Length > 0)
    {
        grid = Grid.FromMapFile(args[0]);
        mapName = Path.GetFileName(args[0]);
    }
    else
    {
        grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
        mapName = "(embedded fixture)";
    }
}
catch (Exception ex) when (ex is MapFormatException or IOException or UnauthorizedAccessException)
{
    // The loader refuses precisely and says which line. Printing that beats a
    // stack trace, and beats opening a window onto an empty map.
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var layout = GridLayout.Fit(grid, MaxMapPixels, MaxMapPixels - StatusHeight);

var start = EndPassableCell(grid, fromEnd: false);
var goal = EndPassableCell(grid, fromEnd: true);
var result = PathFinder.FindPath(grid, start, goal);
var walker = MakeWalker(result, grid);
var clock = new FixedTimestep();
var running = false;

Raylib.SetConfigFlags(ConfigFlags.VSyncHint);
Raylib.InitWindow(layout.PixelWidth, layout.PixelHeight + StatusHeight, $"Nav.Viewer - {mapName}");
Raylib.SetTargetFPS(60);

using var terrain = new TerrainLayer(grid);

while (!Raylib.WindowShouldClose())
{
    // --- input ------------------------------------------------------------
    var mouse = Raylib.GetMousePosition();

    // Nested rather than combined with &&: these predicates return raylib's
    // CBool, which converts to bool implicitly but is not a bool operand.
    if (Raylib.IsMouseButtonPressed(MouseButton.Left))
    {
        if (layout.TryPick(mouse, grid, out var picked))
        {
            start = picked;
            Recompute();
        }
    }
    else if (Raylib.IsMouseButtonPressed(MouseButton.Right))
    {
        if (layout.TryPick(mouse, grid, out var picked))
        {
            goal = picked;
            Recompute();
        }
    }

    if (Raylib.IsKeyPressed(KeyboardKey.Space))
    {
        running = !running;
    }

    if (Raylib.IsKeyPressed(KeyboardKey.R))
    {
        walker?.Reset();
        running = false;
    }

    // --- simulation -------------------------------------------------------
    // Fixed steps, so the walk is reproducible rather than tied to frame time.
    // The accumulator is reset while paused; otherwise it banks the pause and
    // spends it in one burst on resume.
    if (running && walker is not null && !walker.Arrived)
    {
        var steps = clock.Accumulate(Raylib.GetFrameTime());
        for (var i = 0; i < steps; i++)
        {
            walker.Advance(clock.Step);
        }
    }
    else
    {
        clock.Reset();
    }

    // --- draw -------------------------------------------------------------
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);

    terrain.Draw(layout);

    if (result.Found)
    {
        var thickness = Math.Max(1.0f, layout.CellSize * 0.15f);
        for (var i = 1; i < result.Cells.Count; i++)
        {
            Raylib.DrawLineEx(
                CenterOfCell(result.Cells[i - 1]),
                CenterOfCell(result.Cells[i]),
                thickness,
                Color.SkyBlue);
        }
    }

    var marker = Math.Max(2.0f, layout.CellSize * 0.3f);
    Raylib.DrawCircleV(CenterOfCell(start), marker, Color.Green);
    Raylib.DrawCircleV(CenterOfCell(goal), marker, Color.Red);

    if (walker is not null)
    {
        Raylib.DrawCircleV(
            layout.CenterOf(walker.X, walker.Y),
            Math.Max(2.0f, layout.CellSize * 0.35f),
            Color.Orange);
    }

    Raylib.DrawRectangle(0, layout.PixelHeight, layout.PixelWidth, StatusHeight, Color.Black);
    Raylib.DrawText(StatusLine(), 6, layout.PixelHeight + 6, 14, Color.RayWhite);

    Raylib.EndDrawing();
}

Raylib.CloseWindow();
return 0;

Vector2 CenterOfCell(int cell) => layout.CenterOf(grid.ColumnOf(cell), grid.RowOf(cell));

void Recompute()
{
    result = PathFinder.FindPath(grid, start, goal);
    walker = MakeWalker(result, grid);
    clock.Reset();
}

string StatusLine()
{
    // Formatted in pieces rather than through string.Create: that overload takes
    // its interpolation handler by ref, so it binds only to a single
    // interpolated string literal and not to a concatenation of them.
    var path = result.Found
        ? $"cost {result.Cost.ToString("F5", CultureInfo.InvariantCulture)}  steps {result.StepCount}  expanded {result.Expanded}"
        : $"no path  expanded {result.Expanded}";

    return $"{grid.Width}x{grid.Height}  {path}   [{(running ? "running" : "paused")}]  " +
           "LMB start  RMB goal  SPACE walk  R reset";
}

static Walker? MakeWalker(PathResult path, Grid map) =>
    path.Found ? new Walker(path.Cells, map.Width, SpeedCellsPerSecond) : null;

// A start and a goal that exist on any map, so there is something to look at
// before the first click.
static int EndPassableCell(Grid map, bool fromEnd)
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
