using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Nav.Core;

namespace Nav.Benchmark;

/// <summary>
/// Runs A* against every scenario record in a downloaded Moving AI benchmark set
/// and checks each one against its published optimal cost.
/// </summary>
/// <remarks>
/// A TOOL, NOT A TEST, and deliberately so. The benchmark corpus is gitignored --
/// only the arena fixture pair is committed, so the unit suite runs offline -- and
/// a test that quietly skipped when the corpus was absent would report PASSED on
/// every machine that never downloaded it. That is the failure the golden-test
/// note in this toolchain records: green results standing for no verification.
/// A separate executable cannot masquerade as a passing gate, because it is not
/// in the suite at all.
/// <para>
/// The unit suite still owns the arena pair, at 130 records. This widens the
/// same check to whatever is on disk.
/// </para>
/// </remarks>
internal static class Program
{
    private const double Tolerance = 1e-6;

    private sealed record MapOutcome(
        string Map,
        int Records,
        int Passed,
        int Undershoots,
        int Overshoots,
        int Unreachable,
        double WorstDelta,
        string? WorstDetail,
        string? Error);

    private static int Main(string[] args)
    {
        var root = args.FirstOrDefault(a => !a.StartsWith('-')) ?? Path.Combine("maps", "dao");
        var filter = ValueOf(args, "--map");

        var scenarioDirectory = Path.Combine(root, "scen");
        var mapDirectory = Path.Combine(root, "maps");

        if (!Directory.Exists(scenarioDirectory) || !Directory.Exists(mapDirectory))
        {
            Console.Error.WriteLine($"No benchmark corpus at '{root}'. Expected {root}/maps and {root}/scen.");
            Console.Error.WriteLine("Download from https://movingai.com/benchmarks/grids.html; it is deliberately not committed.");
            return 2;
        }

        var scenarios = Directory.GetFiles(scenarioDirectory, "*.scen")
            .Where(f => filter is null || Path.GetFileName(f).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        if (scenarios.Length == 0)
        {
            Console.Error.WriteLine($"No .scen files matched under '{scenarioDirectory}'.");
            return 2;
        }

        Console.WriteLine($"{scenarios.Length} scenario files under {root}, {Environment.ProcessorCount} cores");
        Console.WriteLine();

        var outcomes = new ConcurrentBag<MapOutcome>();
        var done = 0;
        var clock = Stopwatch.StartNew();

        Parallel.ForEach(
            scenarios,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            scenarioPath =>
            {
                var outcome = RunOne(scenarioPath, mapDirectory);
                outcomes.Add(outcome);

                var finished = Interlocked.Increment(ref done);
                var failed = outcome.Records - outcome.Passed;
                if (failed > 0 || outcome.Error is not null)
                {
                    Console.WriteLine(
                        $"  [{finished,3}/{scenarios.Length}] FAIL {outcome.Map}: " +
                        (outcome.Error ?? $"{failed} of {outcome.Records} mismatched  {outcome.WorstDetail}"));
                }
                else if (finished % 20 == 0)
                {
                    Console.WriteLine($"  [{finished,3}/{scenarios.Length}] ok so far, {clock.Elapsed.TotalSeconds:F0}s elapsed");
                }
            });

        clock.Stop();
        return Report([.. outcomes], clock.Elapsed);
    }

    private static MapOutcome RunOne(string scenarioPath, string mapDirectory)
    {
        // "arena.map.scen" names "arena.map".
        var mapName = Path.GetFileNameWithoutExtension(scenarioPath);

        try
        {
            var mapPath = Path.Combine(mapDirectory, mapName);
            if (!File.Exists(mapPath))
            {
                return new MapOutcome(mapName, 0, 0, 0, 0, 0, 0.0, null, "map file missing");
            }

            var grid = Grid.FromMapFile(mapPath);
            var records = ScenarioFile.FromFile(scenarioPath);

            var passed = 0;
            var undershoots = 0;
            var overshoots = 0;
            var unreachable = 0;
            var worstDelta = 0.0;
            string? worstDetail = null;

            foreach (var record in records)
            {
                record.EnsureMatches(grid, scenarioPath);

                var result = PathFinder.FindPath(grid, record.StartIndex(grid), record.GoalIndex(grid));
                if (!result.Found)
                {
                    unreachable++;
                    continue;
                }

                var delta = result.Cost - record.OptimalLength;
                if (Math.Abs(delta) <= Tolerance)
                {
                    passed++;
                    continue;
                }

                if (delta < 0)
                {
                    undershoots++;
                }
                else
                {
                    overshoots++;
                }

                if (Math.Abs(delta) > Math.Abs(worstDelta))
                {
                    worstDelta = delta;
                    worstDetail = string.Create(
                        CultureInfo.InvariantCulture,
                        $"line {record.LineNumber}: ({record.StartX},{record.StartY})->({record.GoalX},{record.GoalY}) expected {record.OptimalLength:F8}, got {result.Cost:F8}");
                }
            }

            return new MapOutcome(mapName, records.Count, passed, undershoots, overshoots, unreachable, worstDelta, worstDetail, null);
        }
        catch (Exception ex) when (ex is MapFormatException or IOException)
        {
            return new MapOutcome(mapName, 0, 0, 0, 0, 0, 0.0, null, ex.Message);
        }
    }

    private static int Report(MapOutcome[] outcomes, TimeSpan elapsed)
    {
        var records = outcomes.Sum(o => o.Records);
        var passed = outcomes.Sum(o => o.Passed);
        var undershoots = outcomes.Sum(o => o.Undershoots);
        var overshoots = outcomes.Sum(o => o.Overshoots);
        var unreachable = outcomes.Sum(o => o.Unreachable);
        var broken = outcomes.Where(o => o.Error is not null).ToArray();

        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        Console.WriteLine($"maps        : {outcomes.Length}");
        Console.WriteLine($"records     : {records:N0}");
        Console.WriteLine($"matched     : {passed:N0}");
        Console.WriteLine($"undershoot  : {undershoots:N0}   (corner-cutting would show up here)");
        Console.WriteLine($"overshoot   : {overshoots:N0}   (diagonal cost or heuristic would show up here)");
        Console.WriteLine($"unreachable : {unreachable:N0}");
        Console.WriteLine($"load errors : {broken.Length}");
        Console.WriteLine($"elapsed     : {elapsed.TotalSeconds:F1}s  ({records / Math.Max(elapsed.TotalSeconds, 0.001):N0} searches/sec)");
        Console.WriteLine(new string('=', 70));

        foreach (var outcome in broken)
        {
            Console.WriteLine($"  ERROR {outcome.Map}: {outcome.Error}");
        }

        var mismatched = outcomes
            .Where(o => o.Error is null && o.Passed != o.Records)
            .OrderByDescending(o => Math.Abs(o.WorstDelta))
            .Take(10)
            .ToArray();

        foreach (var outcome in mismatched)
        {
            Console.WriteLine($"  {outcome.Map}: {outcome.Records - outcome.Passed} of {outcome.Records}");
            if (outcome.WorstDetail is not null)
            {
                Console.WriteLine($"    worst {outcome.WorstDelta:+0.00000000;-0.00000000}  {outcome.WorstDetail}");
            }
        }

        var clean = passed == records && broken.Length == 0;
        Console.WriteLine();
        Console.WriteLine(clean
            ? $"ALL {records:N0} RECORDS MATCHED THEIR PUBLISHED OPTIMAL COST."
            : $"{records - passed:N0} of {records:N0} records did not match.");

        return clean ? 0 : 1;
    }

    private static string? ValueOf(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
