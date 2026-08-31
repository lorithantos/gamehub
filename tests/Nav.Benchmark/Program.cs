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
        string? Error)
    {
        /// <summary>Cells in the grid. Every search clears state arrays sized to this.</summary>
        public long Cells { get; init; }

        /// <summary>Nodes actually popped and closed, summed over every search.</summary>
        public long TotalExpanded { get; init; }

        public int MaxExpanded { get; init; }

        /// <summary>Searches that expanded under one percent of the grid.</summary>
        public int NarrowSearches { get; init; }

        /// <summary>Records per difficulty bucket, indexed by the scenario's own bucket number.</summary>
        public long[] BucketRecords { get; init; } = new long[BucketCount];

        /// <summary>Nodes expanded per difficulty bucket.</summary>
        public long[] BucketExpanded { get; init; } = new long[BucketCount];
    }

    /// <summary>
    /// Moving AI buckets each problem by path difficulty. Anything beyond this is
    /// folded into the last band rather than dropped.
    /// </summary>
    private const int BucketCount = 32;

    private static int Main(string[] args)
    {
        var root = args.FirstOrDefault(a => !a.StartsWith('-')) ?? Path.Combine("maps", "dao");
        var filter = ValueOf(args, "--map");

        // A benchmark corpus is spread across difficulties on purpose; a game is
        // not. Capping the bucket approximates a workload of short local moves,
        // which is what most pathing in an RTS actually is.
        var maxBucket = int.TryParse(ValueOf(args, "--max-bucket"), out var cap) ? cap : int.MaxValue;

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
                var outcome = RunOne(scenarioPath, mapDirectory, maxBucket);
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

    private static MapOutcome RunOne(string scenarioPath, string mapDirectory, int maxBucket)
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
            var records = ScenarioFile.FromFile(scenarioPath)
                .Where(r => r.Bucket <= maxBucket)
                .ToList();

            // One workspace per map, reused across that map's records. This
            // method runs on many threads at once and each has its own, which is
            // the whole reason the workspace is a parameter rather than shared
            // state: Grid is immutable, nothing here is static, nothing locks.
            var workspace = new SearchWorkspace(grid.CellCount);

            var passed = 0;
            var undershoots = 0;
            var overshoots = 0;
            var unreachable = 0;
            var worstDelta = 0.0;
            string? worstDetail = null;
            var totalExpanded = 0L;
            var maxExpanded = 0;
            var narrow = 0;
            var bucketRecords = new long[BucketCount];
            var bucketExpanded = new long[BucketCount];

            foreach (var record in records)
            {
                record.EnsureMatches(grid, scenarioPath);

                var result = PathFinder.FindPath(grid, record.StartIndex(grid), record.GoalIndex(grid), workspace);

                // Measured regardless of the verdict: the question is how much of
                // the grid a search actually touches, against how much of it gets
                // cleared before the search starts.
                totalExpanded += result.Expanded;
                maxExpanded = Math.Max(maxExpanded, result.Expanded);

                var bucket = Math.Clamp(record.Bucket, 0, BucketCount - 1);
                bucketRecords[bucket]++;
                bucketExpanded[bucket] += result.Expanded;
                if (result.Expanded * 100L < grid.CellCount)
                {
                    narrow++;
                }

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

            return new MapOutcome(mapName, records.Count, passed, undershoots, overshoots, unreachable, worstDelta, worstDetail, null)
            {
                Cells = grid.CellCount,
                TotalExpanded = totalExpanded,
                MaxExpanded = maxExpanded,
                NarrowSearches = narrow,
                BucketRecords = bucketRecords,
                BucketExpanded = bucketExpanded,
            };
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

        ReportSearchCost(outcomes, records);

        var clean = passed == records && broken.Length == 0;
        Console.WriteLine();
        Console.WriteLine(clean
            ? $"ALL {records:N0} RECORDS MATCHED THEIR PUBLISHED OPTIMAL COST."
            : $"{records - passed:N0} of {records:N0} records did not match.");

        return clean ? 0 : 1;
    }

    /// <summary>
    /// How much of the grid each search actually touches, against how much of it
    /// is cleared before the search begins.
    /// </summary>
    /// <remarks>
    /// <c>PathFinder.FindPath</c> allocates and initialises three arrays sized to
    /// the whole grid on every call -- 13 bytes per cell -- before expanding its
    /// first node. This measures whether that is proportionate or whether it
    /// dominates, and it is arithmetic over measured counts rather than an
    /// estimate.
    /// </remarks>
    private static void ReportSearchCost(MapOutcome[] outcomes, int records)
    {
        const int BytesPerCell = 8 + 4 + 1;   // double g, int parent, byte state

        var solved = outcomes.Where(o => o.Error is null && o.Records > 0).ToArray();
        if (solved.Length == 0)
        {
            return;
        }

        var expanded = solved.Sum(o => o.TotalExpanded);
        var cellsCleared = solved.Sum(o => o.Cells * o.Records);
        var narrow = solved.Sum(o => (long)o.NarrowSearches);

        Console.WriteLine();
        Console.WriteLine("SEARCH COST");
        Console.WriteLine($"  nodes expanded, total      : {expanded:N0}");
        Console.WriteLine($"  cells cleared, total       : {cellsCleared:N0}");
        Console.WriteLine($"  cells cleared per node     : {(double)cellsCleared / Math.Max(expanded, 1):N1}");
        Console.WriteLine($"  memory initialised         : {cellsCleared * BytesPerCell / (double)(1L << 30):N1} GiB");
        Console.WriteLine($"  mean expanded per search   : {(double)expanded / Math.Max(records, 1):N0}");
        Console.WriteLine($"  searches touching <1% grid : {narrow:N0} of {records:N0} ({100.0 * narrow / Math.Max(records, 1):F1}%)");

        var worst = solved
            .Where(o => o.TotalExpanded > 0)
            .OrderByDescending(o => (double)(o.Cells * o.Records) / o.TotalExpanded)
            .Take(5);

        Console.WriteLine("  least proportionate maps:");
        foreach (var outcome in worst)
        {
            var ratio = (double)(outcome.Cells * outcome.Records) / outcome.TotalExpanded;
            var mean = (double)outcome.TotalExpanded / outcome.Records;
            Console.WriteLine(
                $"    {outcome.Map,-18} {outcome.Cells,9:N0} cells, mean {mean,8:N0} expanded, {ratio,7:N0}x cleared per node");
        }

        // By difficulty bucket, because the aggregate answers a question nobody
        // asked. A benchmark corpus is deliberately spread across difficulties;
        // a game is not. Short local moves are a different workload, and the
        // last column is what a constant-time reset is worth on each of them.
        Console.WriteLine();
        Console.WriteLine("  bucket   records     mean expanded   cells cleared per node");
        for (var bucket = 0; bucket < BucketCount; bucket++)
        {
            var count = solved.Sum(o => o.BucketRecords[bucket]);
            if (count == 0)
            {
                continue;
            }

            var bucketExpanded = solved.Sum(o => o.BucketExpanded[bucket]);
            var bucketCells = solved.Sum(o => o.BucketRecords[bucket] * o.Cells);
            var mean = (double)bucketExpanded / count;
            var ratio = (double)bucketCells / Math.Max(bucketExpanded, 1);

            Console.WriteLine($"  {bucket,6}  {count,9:N0}   {mean,13:N0}   {ratio,20:N0}x");
        }
    }

    private static string? ValueOf(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
