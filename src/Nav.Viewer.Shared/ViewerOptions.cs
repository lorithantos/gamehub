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
public sealed record ViewerOptions(string? MapPath, int? MaxFrames, bool ShowHelp)
{
    public static string UsageText =>
        """
        usage: <viewer> [map-path] [--frames N] [--help]

          map-path   a Moving AI .map file. Defaults to the embedded fixture.
          --frames N exit after N frames. For smoke runs that need no human.
          --help     this text.
        """;

    public static bool TryParse(string[] args, [NotNullWhen(true)] out ViewerOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = null;

        string? mapPath = null;
        int? maxFrames = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h" or "/?")
            {
                options = new ViewerOptions(null, null, ShowHelp: true);
                return true;
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

        options = new ViewerOptions(mapPath, maxFrames, ShowHelp: false);
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
