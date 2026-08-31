using System.IO;

using Nav.Core;

namespace Nav.Viewer.Wpf;

/// <summary>
/// Entry point for the WPF / Direct3D 11 host.
/// </summary>
/// <remarks>
/// Deliberately a near-copy of the raylib host's Main. The two share
/// <c>ViewerOptions</c>, <c>Grid</c> and <c>GridLayout</c> -- everything with a
/// decision in it -- and duplicate only the wiring that reads them. Hoisting the
/// remaining forty lines into the shared project would have been a change to
/// <c>Nav.Viewer.Shared</c>, and the point of this phase is to find out whether
/// building a second host needs one. Measure first; tidy afterwards.
/// </remarks>
internal static class Program
{
    private const int MaxMapPixels = 1000;
    private const int StatusHeight = 26;

    [STAThread]
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
        try
        {
            if (options.MapPath is { } path)
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
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (grid.PassableCount == 0)
        {
            Console.Error.WriteLine($"{mapName} has no passable cell; there is nothing to walk on.");
            return 1;
        }

        // The same pure call the raylib host makes, so the two windows are the
        // same size for the same map by construction rather than by agreement.
        var layout = GridLayout.Fit(grid, MaxMapPixels, MaxMapPixels - StatusHeight);

        var app = new ViewerApp(grid, layout);
        using var host = new WpfHost(layout, $"Nav.Viewer - {mapName}", options.MaxFrames);
        host.Run(app);

        return 0;
    }
}
