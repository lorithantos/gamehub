using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Criterion 4 on the committed Moving AI MAPF instance: completeness on a
/// solvable instance, at the fixture's full agent count, within four times the
/// longest individual optimal path.
/// </summary>
/// <remarks>
/// The fixture is <c>empty-8-8</c> with its full even-1 scenario: 32 agents on
/// 64 open cells — fifty percent density, which is a genuinely hard regime for
/// prioritized planning. The same measurement on <c>empty-16-16</c> at ITS full
/// count (128 agents, also 50%) left 2 of 128 permanently stuck: prioritized
/// planning is not complete, and that is where its limit actually is on this
/// implementation. The committed fixture is the largest of the two that the
/// planner genuinely solves; the 16-16 result is recorded here so the boundary
/// is knowledge rather than a surprise in milestone 3.
/// </remarks>
public sealed class MapfFixtureTests(ITestOutputHelper output)
{
    private const int NodeBudget = 10_000;

    private static (Grid Grid, IReadOnlyList<ScenarioRecord> Records) Load()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("empty-8-8.map"));
        var records = ScenarioFile.FromFile(Fixtures.Map("empty-8-8-even-1.scen"));
        return (grid, records);
    }

    [Fact]
    public void TheFixtureIsFullDensityAndInternallyConsistent()
    {
        var (grid, records) = Load();

        Assert.Equal(32, records.Count);
        Assert.Equal(64, grid.PassableCount);
        Assert.Equal(records.Count, records.Select(r => grid.Index(r.StartX, r.StartY)).Distinct().Count());
        Assert.Equal(records.Count, records.Select(r => grid.Index(r.GoalX, r.GoalY)).Distinct().Count());
    }

    [Fact]
    public void EveryAgentArrivesWithinFourTimesTheLongestOptimal()
    {
        var (grid, records) = Load();

        // The bound comes from milestone 1's verified single-agent search.
        var longest = 0;
        foreach (var record in records)
        {
            var path = PathFinder.FindPath(
                grid, grid.Index(record.StartX, record.StartY), grid.Index(record.GoalX, record.GoalY));
            Assert.True(path.Found, $"({record.StartX},{record.StartY}) cannot reach ({record.GoalX},{record.GoalY})");
            longest = Math.Max(longest, path.Cells.Count - 1);
        }

        var limit = 4 * longest;
        var system = new MovementSystem(grid, horizon: 32, nodeBudgetPerTick: NodeBudget);
        foreach (var record in records)
        {
            system.AddAgent(grid.Index(record.StartX, record.StartY));
        }

        for (var agent = 0; agent < records.Count; agent++)
        {
            system.Order([agent], grid.Index(records[agent].GoalX, records[agent].GoalY));
        }

        var arrivedAt = -1;
        for (var tick = 0; tick < limit; tick++)
        {
            system.Tick();
            if (system.Agents.All(a => a.Arrived))
            {
                arrivedAt = system.CurrentTick;
                break;
            }
        }

        output.WriteLine(
            $"{records.Count} agents on {grid.Width}x{grid.Height}: longest individual optimal {longest} ticks, " +
            $"limit {limit}, all arrived at tick {(arrivedAt < 0 ? "never" : arrivedAt)}, " +
            $"{system.TotalExpanded:N0} nodes");

        Assert.True(
            arrivedAt >= 0,
            $"only {system.Agents.Count(a => a.Arrived)} of {records.Count} arrived within {limit} ticks; " +
            $"{system.Agents.Count(a => a.Stuck)} stuck");
    }
}
