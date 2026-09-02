using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Every number the corpus binds settling behaviour against, in one table.
/// </summary>
/// <remarks>
/// A REPORT, not a gate. The assertions live in the tests these figures mirror;
/// this exists so a change to how a group picks and fills its parking ring can be
/// judged BEFORE it is committed, instead of by reading which tests it broke.
/// <para>
/// It was written mid-spike, after two plausible changes to the claim pass were
/// each reverted: the first showed up as units retreating from a settled blob,
/// the second as a throng packing loosely, and in both cases the failing test
/// named a symptom rather than the trade. Seeing arena settle time, packing
/// tightness, ten route ratios and the benchmark boundary move together turns
/// that into a decision.
/// </para>
/// <para>
/// Run it alone with
/// <c>dotnet test tests/Nav.Core.Tests --filter FullyQualifiedName~SettlingReport</c>,
/// once before a change and once after. It takes a few seconds and passes
/// whatever it prints, except where a figure crosses a ceiling the corpus pins,
/// which it marks BROKEN so the table can be skimmed.
/// </para>
/// <para>
/// <b>As of gating the good-enough rule on being held up</b>, on this machine:
/// arena-200 settles at 579 ticks for 5.30M nodes; the throng packs to 3.41,
/// which is the ideal exactly; route ratios run headon 1.000, group 1.303,
/// crosscut 1.101, chokepoint 1.286, crossing 1.297, standing 1.261,
/// staggered 1.063, throng 1.264, countermand 2.519, reconcile 1.336; blob
/// retreats are 0, 0 and 0; and the benchmark lands 126 of 128.
/// Treat these as the last known good reading, not as a target: they are a
/// machine and a moment, and the ceilings are what actually bind.
/// </para>
/// </remarks>
[Trait("kind", "report")]
public sealed class SettlingReport(ITestOutputHelper output)
{
    private const string PatrolMap =
        """
        type octile
        height 15
        width 29
        map
        .............................
        .............................
        ....@@@@@@.......@@@@@@@@....
        ....@@@@@@.......@@@@@@@@....
        .............................
        .............................
        .............................
        .............................
        .............................
        .............................
        ....@@@@@@@@@........@@@@....
        ....@@@@@@@@@........@@@@....
        .............................
        .............................
        .............................
        """;

    [Fact]
    public void Report()
    {
        output.WriteLine("=== settling report ===");
        Arena();
        ThrongPacking();
        MovementRatios();
        BlobRetreats();
        Boundary();
        PatrolApproach();
    }

    /// <summary>200 agents, one order: when does it settle and what did it cost?</summary>
    private void Arena()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));
        var system = new MovementSystem(grid, horizon: 32, nodeBudgetPerTick: 10_000);

        var placed = 0;
        for (var cell = 0; cell < grid.CellCount && placed < 200; cell++)
        {
            if (grid.IsPassable(cell))
            {
                system.AddAgent(cell);
                placed++;
            }
        }

        system.Order([.. Enumerable.Range(0, 200)], grid.Index(44, 44));

        var settledAt = -1;
        long nodes = 0;
        for (var tick = 0; tick < 2000 && settledAt < 0; tick++)
        {
            system.Tick();
            if (system.Agents.All(a => a.Arrived))
            {
                settledAt = system.CurrentTick;
                nodes = system.TotalExpanded;
            }
        }

        output.WriteLine($"arena-200          settle {settledAt,5}   nodes {nodes,12:N0}   (lower is better)");
    }

    /// <summary>How fat the throng's blob settles against the ideal pack.</summary>
    private void ThrongPacking()
    {
        var (scenario, grid) = Fixtures.Load("throng");
        var outcome = ScenarioPlayback.Play(scenario, grid);

        var field = DistanceField.Build(grid, grid.Index(9, 4));
        var doorway = new HashSet<int>();
        foreach (var choke in ChokepointMap.Find(grid))
        {
            doorway.Add(choke.Cell);
            var x = grid.ColumnOf(choke.Cell);
            var y = grid.RowOf(choke.Cell);
            foreach (var step in Movement.Steps)
            {
                if (grid.IsPassable(x + step.DeltaX, y + step.DeltaY))
                {
                    doorway.Add(grid.Index(x + step.DeltaX, y + step.DeltaY));
                }
            }
        }

        var worst = outcome.FinalCells.Max(field.CostFrom);
        var ideal = GoalSpread.Nearest(grid, grid.Index(9, 4), 24, doorway.Contains).Max(field.CostFrom);
        var ceiling = ideal + (2.0 * Movement.DiagonalCost);

        output.WriteLine(
            $"throng-pack        worst {worst,5:F2}   ideal {ideal,5:F2}   ceiling {ceiling,5:F2}   " +
            (worst <= ceiling ? "ok" : "BROKEN"));
    }

    /// <summary>Distance walked against the single-agent optimum, per scenario.</summary>
    private void MovementRatios()
    {
        var ceilings = new Dictionary<string, double>
        {
            ["headon"] = 1.05, ["group"] = 1.80, ["crosscut"] = 1.30, ["chokepoint"] = 1.50,
            ["crossing"] = 1.50, ["standing"] = 1.50, ["staggered"] = 1.20, ["throng"] = 1.60,
            ["countermand"] = 2.55, ["reconcile"] = 2.35,
        };

        foreach (var (name, ceiling) in ceilings)
        {
            var (scenario, grid) = Fixtures.Load(name);
            var outcome = ScenarioPlayback.Play(scenario, grid);

            var movement = 0.0;
            var lowerBound = 0.0;

            foreach (var plan in outcome.Trajectories)
            {
                var cells = plan.Plan.Cells;
                var first = 0;
                while (first + 1 < cells.Count && cells[first] == cells[first + 1])
                {
                    first++;
                }

                var lastAt = cells.Count - 1;
                while (lastAt > 0 && cells[lastAt] == cells[lastAt - 1])
                {
                    lastAt--;
                }

                for (var i = first + 1; i <= lastAt; i++)
                {
                    if (cells[i] == cells[i - 1])
                    {
                        continue;
                    }

                    var diagonal = grid.ColumnOf(cells[i]) != grid.ColumnOf(cells[i - 1]) &&
                                   grid.RowOf(cells[i]) != grid.RowOf(cells[i - 1]);
                    movement += diagonal ? Movement.ExactCost(0, 1) : Movement.ExactCost(1, 0);
                }

                lowerBound += PathFinder.FindPath(grid, cells[0], cells[^1]).Cost;
            }

            var ratio = lowerBound > 0 ? movement / lowerBound : 0.0;
            output.WriteLine(
                $"ratio-{name,-12} {ratio,5:F3}   ceiling {ceiling,5:F2}   " +
                (ratio <= ceiling ? "ok" : "BROKEN"));
        }
    }

    /// <summary>Late outward steps once a blob has formed, worst per agent.</summary>
    private void BlobRetreats()
    {
        foreach (var (destX, destY) in new[] { (18, 5), (18, 3), (19, 5) })
        {
            var grid = Grid.FromMapFile(Fixtures.Map("crosscut.map"));
            var system = new MovementSystem(grid);
            var placed = 0;
            for (var cell = 0; cell < grid.CellCount && placed < 24; cell++)
            {
                if (grid.IsPassable(cell))
                {
                    system.AddAgent(cell);
                    placed++;
                }
            }

            system.Order([.. Enumerable.Range(0, placed)], grid.Index(destX, destY));
            var field = DistanceField.Build(grid, grid.Index(destX, destY));

            var previous = system.Agents.Select(a => a.Cell).ToArray();
            var perAgent = new int[24];
            for (var tick = 1; tick <= 400; tick++)
            {
                system.Tick();
                var settled = system.Agents.Count(a => a.Arrived);
                foreach (var agent in system.Agents)
                {
                    if (agent.Cell == previous[agent.Id])
                    {
                        continue;
                    }

                    if (settled >= 20 && field.CostFrom(agent.Cell) > field.CostFrom(previous[agent.Id]) + 0.5)
                    {
                        perAgent[agent.Id]++;
                    }

                    previous[agent.Id] = agent.Cell;
                }
            }

            var arrived = system.Agents.Count(a => a.Arrived);
            var worst = perAgent.Max();
            output.WriteLine(
                $"blob-({destX},{destY})        arrived {arrived,3}/24   worst retreat {worst}   " +
                (arrived == 24 && worst <= 1 ? "ok" : "BROKEN"));
        }
    }

    /// <summary>The published-benchmark boundary.</summary>
    private void Boundary()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("empty-16-16.map"));
        var records = ScenarioFile.FromFile(Fixtures.Map("empty-16-16-even-1.scen"));

        var system = new MovementSystem(grid, horizon: 32, nodeBudgetPerTick: 10_000);
        foreach (var record in records)
        {
            system.AddAgent(grid.Index(record.StartX, record.StartY));
        }

        for (var agent = 0; agent < records.Count; agent++)
        {
            system.Order([agent], grid.Index(records[agent].GoalX, records[agent].GoalY));
        }

        var arrived = 0;
        var lastGrowth = 0;
        for (var tick = 0; tick < 2000 && system.CurrentTick - lastGrowth < 300; tick++)
        {
            system.Tick();
            var now = system.Agents.Count(a => a.Arrived);
            if (now > arrived)
            {
                arrived = now;
                lastGrowth = system.CurrentTick;
            }
        }

        output.WriteLine($"mapf-128           arrived {arrived,3}/128   floor 126   " + (arrived >= 126 ? "ok" : "BROKEN"));
    }

    /// <summary>
    /// THE CASE THE SPIKE IS FOR. Three units walk in from the south to a post
    /// and settle around it: how long until the last one stops, how far did they
    /// walk between them, and did anyone cross the post to reach its cell?
    /// </summary>
    private void PatrolApproach()
    {
        var grid = Grid.FromMapText(PatrolMap);
        var system = new MovementSystem(grid);
        int[] starts = [grid.Index(1, 12), grid.Index(2, 13), grid.Index(1, 14)];
        foreach (var cell in starts)
        {
            system.AddAgent(cell);
        }

        var post = grid.Index(3, 7);
        system.Order([0, 1, 2], post);

        var previous = system.Agents.Select(a => a.Cell).ToArray();
        var steps = 0;
        var crossings = 0;
        var settledAt = -1;

        for (var tick = 1; tick <= 120 && settledAt < 0; tick++)
        {
            system.Tick();
            foreach (var agent in system.Agents)
            {
                if (agent.Cell == previous[agent.Id])
                {
                    continue;
                }

                steps++;

                // Stepping ONTO the post while it is somebody else's goal means
                // walking through the middle of the formation to get around it.
                if (agent.Cell == post && system.Agents.Any(o => o.Id != agent.Id && o.Goal == post))
                {
                    crossings++;
                }

                previous[agent.Id] = agent.Cell;
            }

            if (system.Agents.All(a => a.Arrived))
            {
                settledAt = system.CurrentTick;
            }
        }

        var cells = string.Join(" ", system.Agents.Select(a => $"{a.Id}:({grid.ColumnOf(a.Cell)},{grid.RowOf(a.Cell)})"));
        output.WriteLine($"patrol-approach    settle {settledAt,5}   steps {steps,4}   crossings {crossings}   {cells}");
    }
}
