using System.Globalization;

using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Acceptance criterion 5 -- the important one.
/// </summary>
/// <remarks>
/// Every other test in this suite checks the implementation against itself or
/// against a number written into the brief. This one checks it against costs
/// published by someone else, computed by a different implementation, for a real
/// map. It is the only test here that can tell a self-consistently wrong
/// pathfinder from a right one.
/// </remarks>
public sealed class BenchmarkOracleTests(ITestOutputHelper output)
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void EveryArenaRecordMatchesItsPublishedOptimalCost()
    {
        var grid = Grid.FromMapFile(Fixtures.ArenaMap);
        var records = ScenarioFile.FromFile(Fixtures.ArenaScenario);

        Assert.NotEmpty(records);

        var passed = 0;
        var undershoots = 0;
        var overshoots = 0;
        var unreachable = 0;
        var failures = new List<string>();

        foreach (var record in records)
        {
            record.EnsureMatches(grid, Fixtures.ArenaScenario);

            var result = PathFinder.FindPath(grid, record.StartIndex(grid), record.GoalIndex(grid));

            if (!result.Found)
            {
                unreachable++;
                failures.Add($"line {record.LineNumber}: no path found, expected {record.OptimalLength:F8}");
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

            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"line {record.LineNumber}: ({record.StartX},{record.StartY})->({record.GoalX},{record.GoalY}) expected {record.OptimalLength:F8}, got {result.Cost:F8}, delta {delta:+0.00000000;-0.00000000}"));
        }

        output.WriteLine($"criterion 5: {passed}/{records.Count} records matched their published optimal cost");

        if (failures.Count == 0)
        {
            return;
        }

        // The brief's own diagnosis, applied automatically: a systematic
        // undershoot means the corner rule lets paths through walls, a systematic
        // overshoot means the diagonal cost or the heuristic is wrong. Saying so
        // here beats leaving 130 raw deltas for a reader to characterise.
        var diagnosis = (undershoots, overshoots, unreachable) switch
        {
            (> 0, 0, 0) => "systematic UNDERSHOOT -- Movement.IsLegalStep is permitting corner cuts",
            (0, > 0, 0) => "systematic OVERSHOOT -- the diagonal cost or the octile heuristic is wrong",
            (0, 0, > 0) => "paths not found -- connectivity or terrain passability is wrong",
            _ => $"mixed: {undershoots} under, {overshoots} over, {unreachable} unreachable",
        };

        Assert.Fail(
            $"{passed}/{records.Count} records matched. {diagnosis}." +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures.Take(20)) +
            (failures.Count > 20 ? $"{Environment.NewLine}... and {failures.Count - 20} more" : string.Empty));
    }

    [Fact]
    public void EveryArenaPathIsActuallyWalkable()
    {
        // A cost matching the oracle is necessary but not sufficient: it would
        // still match if the returned cells were nonsense and only the counting
        // were right. This walks each path and checks every move.
        var grid = Grid.FromMapFile(Fixtures.ArenaMap);
        var records = ScenarioFile.FromFile(Fixtures.ArenaScenario);

        foreach (var record in records)
        {
            var start = record.StartIndex(grid);
            var goal = record.GoalIndex(grid);
            var result = PathFinder.FindPath(grid, start, goal);

            Assert.True(result.Found, $"line {record.LineNumber}: no path");
            Assert.Equal(start, result.Cells[0]);
            Assert.Equal(goal, result.Cells[^1]);

            for (var i = 1; i < result.Cells.Count; i++)
            {
                var previous = result.Cells[i - 1];
                var x = grid.ColumnOf(previous);
                var y = grid.RowOf(previous);
                var deltaX = grid.ColumnOf(result.Cells[i]) - x;
                var deltaY = grid.RowOf(result.Cells[i]) - y;

                Assert.True(
                    Movement.IsLegalStep(grid, x, y, deltaX, deltaY),
                    $"line {record.LineNumber}, step {i}: ({x},{y}) by ({deltaX},{deltaY}) is not legal");
            }
        }
    }

    [Fact]
    public void TheArenaFixtureIsTheMapTheScenarioFileExpects()
    {
        var grid = Grid.FromMapFile(Fixtures.ArenaMap);

        Assert.Equal(49, grid.Width);
        Assert.Equal(49, grid.Height);

        var records = ScenarioFile.FromFile(Fixtures.ArenaScenario);
        Assert.All(records, record => Assert.Equal("arena.map", record.MapName));

        output.WriteLine(
            $"arena.map: {grid.Width}x{grid.Height}, {grid.PassableCount} passable of {grid.CellCount}; " +
            $"{records.Count} scenario records");
    }

    [Fact]
    public void RunningTheWholeSetTwiceGivesIdenticalAnswers()
    {
        var grid = Grid.FromMapFile(Fixtures.ArenaMap);
        var records = ScenarioFile.FromFile(Fixtures.ArenaScenario);

        foreach (var record in records)
        {
            var start = record.StartIndex(grid);
            var goal = record.GoalIndex(grid);

            var first = PathFinder.FindPath(grid, start, goal);
            var second = PathFinder.FindPath(grid, start, goal);

            Assert.Equal(first.Cells, second.Cells);
            Assert.Equal(first.Cost, second.Cost);
            Assert.Equal(first.Expanded, second.Expanded);
        }
    }
}
