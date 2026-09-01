using System.Globalization;

using Nav.Core;

namespace Nav.Benchmark;

/// <summary>
/// The trace verbs: <c>trace</c> writes a scenario run as JSONL, positions
/// included; <c>summarize</c> reduces a trace of any size to a bounded digest.
/// </summary>
/// <remarks>
/// The digest is the intended way to read a big trace: it names the ticks worth
/// looking at, and those lines can then be pulled individually with any JSONL
/// tool, instead of paging a whole file through eyes or a context window.
/// </remarks>
internal static class TraceCommands
{
    public static int Run(string[] args) => args[0] switch
    {
        "trace" => Trace(args[1..]),
        "summarize" => Summarize(args[1..]),
        _ => Usage(),
    };

    private static int Usage()
    {
        Console.Error.WriteLine(
            """
            usage: Nav.Benchmark trace <scenario-file> [--map FILE] [--out FILE] [--horizon N]
                   Nav.Benchmark summarize <trace.jsonl>

              trace      play the scenario and write one JSON line per tick, with
                         every agent's position, goal, stall and search spend.
                         Byte-identical for identical runs, so two traces diff.
              summarize  a bounded digest of a trace: aggregates, the worst
                         agents, and the ticks worth looking at.
            """);
        return 2;
    }

    private static int Trace(string[] args)
    {
        string? scenarioPath = null;
        string? mapPath = null;
        string? outPath = null;
        var horizon = 32;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--map" when i + 1 < args.Length:
                    mapPath = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--horizon" when i + 1 < args.Length &&
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out horizon):
                    i++;
                    break;
                default:
                    if (args[i].StartsWith('-') || scenarioPath is not null)
                    {
                        return Usage();
                    }

                    scenarioPath = args[i];
                    break;
            }
        }

        if (scenarioPath is null)
        {
            return Usage();
        }

        try
        {
            var scenario = RecordedScenario.FromFile(scenarioPath);
            mapPath ??= ResolveMap(scenarioPath, scenario.MapName);
            var grid = Grid.FromMapFile(mapPath);

            using TextWriter writer = outPath is null ? Console.Out : new StreamWriter(outPath);
            var outcome = ScenarioTrace.Write(scenario, grid, writer, Path.GetFileName(scenarioPath), horizon);

            if (outPath is not null)
            {
                Console.WriteLine(
                    $"{outPath}: {outcome.Ticks} ticks, {outcome.FinalCells.Count} agents, " +
                    $"{outcome.Arrived} arrived, {outcome.Stuck} stuck, " +
                    $"{outcome.Conflicts.Conflicts.Count} conflicts, {outcome.TotalExpanded:N0} nodes");
            }

            return 0;
        }
        catch (Exception ex) when (ex is MapFormatException or IOException or
            UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Summarize(string[] args)
    {
        if (args.Length != 1 || args[0].StartsWith('-'))
        {
            return Usage();
        }

        try
        {
            using var reader = new StreamReader(args[0]);
            Console.Write(ScenarioTrace.Summarize(reader));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>Beside the scenario file, then one directory up — the fixture layout.</summary>
    private static string ResolveMap(string scenarioPath, string mapName)
    {
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
}
