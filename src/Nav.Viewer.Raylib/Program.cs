using Nav.Core;

namespace Nav.Viewer.Raylib;

/// <summary>
/// Entry point for the raylib host.
/// </summary>
/// <remarks>
/// An explicit class with an explicit <c>Main</c>, not top-level statements.
/// Two reasons, and the second is the better one: the WPF host will need
/// <c>[STAThread]</c>, which top-level statements cannot carry; and code in
/// top-level statements is invisible to the Roslyn code graph, so every
/// <c>PathFinder.FindPath</c> and <c>Walker.Advance</c> call the old viewer made
/// showed up nowhere. Moving the logic into <c>ViewerApp</c> makes the viewer
/// analysable for the first time.
/// <para>
/// What is left here is only wiring: parse, load, size, construct, run.
/// </para>
/// </remarks>
internal static class Program
{
    private const int MaxMapPixels = 1000;
    private const int StatusHeight = 26;

    private static int Main(string[] args)
    {
        if (!ViewerOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ViewerOptions.UsageText);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ViewerOptions.UsageText);
            return 0;
        }

        Grid grid;
        string mapName;
        RecordedScenario? scenario = null;
        try
        {
            if (options.ScenarioPath is { } scenarioFile)
            {
                scenario = RecordedScenario.FromFile(scenarioFile);
                var mapFile = options.MapPath
                    ?? ViewerOptions.ResolveScenarioMap(scenarioFile, scenario.MapName);
                grid = Grid.FromMapFile(mapFile);
                mapName = $"{Path.GetFileName(scenarioFile)} on {Path.GetFileName(mapFile)}";
            }
            else if (options.MapPath is { } path)
            {
                grid = Grid.FromMapFile(path);
                mapName = Path.GetFileName(path);
            }
            else
            {
                grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
                mapName = "(embedded fixture)";
            }
        }
        catch (Exception ex) when (ex is MapFormatException or IOException or UnauthorizedAccessException)
        {
            // The loader refuses precisely and names the line. Printing that
            // beats a stack trace, and beats opening a window onto nothing.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        // A map of nothing but walls parses perfectly well -- "@@@" is valid --
        // and then ViewerApp has no cell to put the unit on. Found by
        // exception_escapes once the extraction made this code visible to the
        // graph: it reported an unconditional InvalidOperationException reaching
        // Main from EndPassableCell. Refusing here with the map's name beats
        // catching it later, and beats the stack trace it used to produce.
        if (grid.PassableCount == 0)
        {
            Console.Error.WriteLine($"{mapName} has no passable cell; there is nothing to walk on.");
            return 1;
        }

        // Pure, and shared: the WPF host will size itself from this same call,
        // so the two cannot disagree about geometry by accident.
        var layout = GridLayout.Fit(grid, MaxMapPixels, MaxMapPixels - StatusHeight);

        ViewerApp app;
        try
        {
            app = new ViewerApp(grid, layout, scenario: scenario);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // A scenario agent on a wall or off the map. The message names the
            // cell; refusing beats a window of units that were never placed.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using var host = new RaylibHost(layout, StatusHeight, $"Nav.Viewer - {mapName}", options.MaxFrames);
        host.Run(app);

        return 0;
    }
}
