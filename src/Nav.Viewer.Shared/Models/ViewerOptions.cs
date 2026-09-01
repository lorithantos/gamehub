using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Nav.Viewer.Models;

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
    /// <summary>
    /// What <c>--help</c> prints, on stdout, with exit code 0. Both hosts also
    /// print it to <b>stderr</b> beneath the message when <see cref="TryParse"/>
    /// refuses, and exit 2 -- a refusal is a usage error, not a run that produced
    /// nothing.
    /// </summary>
    public static string UsageText =>
        """
        usage: <viewer> [map-path] [--scenario FILE] [--frames N] [--help]

          map-path        a Moving AI .map file. Defaults to the embedded fixture,
                          or with --scenario to the map the scenario names.
          --scenario FILE replay a recorded scenario: its placements, its orders,
                          at their recorded ticks. Loads paused at tick zero;
                          SPACE runs it, R reloads it. Your own clicks still work.
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

    /// <summary>
    /// The whole command line, or the first reason it cannot be one. Never
    /// throws for bad input and never repairs it -- refusing is the point.
    /// </summary>
    /// <param name="args">
    /// The arguments, without the executable name. <c>--help</c>, <c>-h</c> or
    /// <c>/?</c> wins the moment it is seen and the rest of the line is not
    /// examined, so help is available even beside arguments that would refuse.
    /// Both <c>--flag value</c> and <c>--flag=value</c> spellings are accepted.
    /// </param>
    /// <param name="options">
    /// The parsed line on success. A help line comes back with
    /// <see cref="ShowHelp"/> set and nothing else, which the caller is expected
    /// to check before treating it as a run.
    /// </param>
    /// <param name="error">
    /// On refusal, one sentence naming the offending argument -- an unknown flag,
    /// a second map path, a <c>--frames</c> that is not a positive integer, a
    /// <c>--scenario</c> with no file. Null on success.
    /// </param>
    /// <returns>
    /// True when the line parsed. <b>False leaves <paramref name="options"/>
    /// null</b> and puts the reason in <paramref name="error"/>; both hosts then
    /// print that reason followed by <see cref="UsageText"/> and exit 2.
    /// </returns>
    public static bool TryParse(
        string[] args,
        [NotNullWhen(true)] out ViewerOptions? options,
        [NotNullWhen(false)] out string? error)
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

    private static bool TryFrames(string value, ref int? frames, [NotNullWhen(false)] out string? error)
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
