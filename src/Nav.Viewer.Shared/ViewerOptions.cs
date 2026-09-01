using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Nav.Viewer;

/// <summary>
/// The command line, parsed. Pure, and therefore testable without a window.
/// </summary>
/// <remarks>
/// Refuses rather than repairs, in keeping with <c>Grid.FromMapText</c>: an
/// unknown flag or a second positional argument is an error with usage, not
/// something quietly ignored.
/// <para>
/// The first non-flag argument is still the map path, so the milestone-1
/// contract — <c>Nav.Viewer maps/arena.map</c> — keeps working unchanged.
/// </para>
/// </remarks>
public sealed record ViewerOptions(string? MapPath, int? MaxFrames, bool ShowHelp, string? ScenarioPath = null)
{
    public static string UsageText =>
        """
        usage: <viewer> [map-path] [--scenario FILE] [--frames N] [--help]

          map-path        a Moving AI .map file. Defaults to the embedded fixture,
                          or with --scenario to the map the scenario names.
          --scenario FILE replay a recorded scenario: its placements, its orders,
                          at their recorded ticks. Your own clicks still work.
          --frames N      exit after N frames. For smoke runs that need no human.
          --help          this text.
        """;

    /// <summary>
    /// Where a scenario's map lives: beside the scenario file, or one directory
    /// up — the fixture layout, where scenarios/ sits inside the map folder.
    /// </summary>
    /// <remarks>
    /// When neither exists, the beside-path is returned anyway so the map
    /// loader's refusal names a real path instead of this method guessing.
    /// </remarks>
    public static string ResolveScenarioMap(string scenarioPath, string mapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);

        var directory = Path.GetDirectoryName(Path.GetFullPath(scenarioPath))!;
        var beside = Path.Combine(directory, mapName);
        if (File.Exists(beside))
        {
            return beside;
        }

        if (Path.GetDirectoryName(directory) is { } parent)
        {
            var above = Path.Combine(parent, mapName);
            if (File.Exists(above))
            {
                return above;
            }
        }

        return beside;
    }

    public static bool TryParse(string[] args, [NotNullWhen(true)] out ViewerOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = null;

        string? mapPath = null;
        int? maxFrames = null;
        string? scenarioPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h" or "/?")
            {
                options = new ViewerOptions(null, null, ShowHelp: true);
                return true;
            }

            if (arg.StartsWith("--scenario=", StringComparison.Ordinal))
            {
                scenarioPath = arg["--scenario=".Length..];
                continue;
            }

            if (arg == "--scenario")
            {
                if (i + 1 >= args.Length)
                {
                    error = "--scenario needs a file path.";
                    return false;
                }

                scenarioPath = args[++i];
                continue;
            }

            if (arg.StartsWith("--frames=", StringComparison.Ordinal))
            {
                if (!TryFrames(arg["--frames=".Length..], ref maxFrames, out error))
                {
                    return false;
                }

                continue;
            }

            if (arg == "--frames")
            {
                if (i + 1 >= args.Length)
                {
                    error = "--frames needs a count.";
                    return false;
                }

                if (!TryFrames(args[++i], ref maxFrames, out error))
                {
                    return false;
                }

                continue;
            }

            if (arg.StartsWith('-'))
            {
                error = $"unknown option '{arg}'.";
                return false;
            }

            if (mapPath is not null)
            {
                error = $"unexpected second map path '{arg}'.";
                return false;
            }

            mapPath = arg;
        }

        if (scenarioPath is not null && scenarioPath.Length == 0)
        {
            error = "--scenario needs a file path.";
            return false;
        }

        options = new ViewerOptions(mapPath, maxFrames, ShowHelp: false, scenarioPath);
        return true;
    }

    private static bool TryFrames(string value, ref int? frames, out string? error)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            error = $"--frames needs a positive integer, found '{value}'.";
            return false;
        }

        frames = parsed;
        error = null;
        return true;
    }
}
