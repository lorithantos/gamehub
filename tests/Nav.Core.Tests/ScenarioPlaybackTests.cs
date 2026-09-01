using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Milestone 2, step 5: the recorded scenarios, and playing them back.
/// </summary>
/// <remarks>
/// A scenario is an input log, not a recording of positions, so replaying it
/// re-runs the simulation. That makes playback the determinism test — criterion 6
/// otherwise has no mechanism behind it.
/// </remarks>
public sealed class ScenarioPlaybackTests(ITestOutputHelper output)
{
    // --- the format ----------------------------------------------------------

    [Fact]
    public void AScenarioRoundTripsThroughItsOwnTextForm()
    {
        var (scenario, _) = Fixtures.Load("group");

        var again = RecordedScenario.FromText(scenario.ToText());

        Assert.Equal(scenario.MapName, again.MapName);
        Assert.Equal(scenario.EndTick, again.EndTick);
        Assert.Equal(scenario.Agents, again.Agents);
        Assert.Equal(scenario.Orders.Count, again.Orders.Count);
    }

    [Fact]
    public void CommentsAndBlankLinesAreIgnored()
    {
        var scenario = RecordedScenario.FromText(
            """
            # a comment

            version 1
            map hall.map

            # agents
            agent 0 1 1
            order 0 0 5 5
            end 10
            """);

        Assert.Single(scenario.Agents);
        Assert.Equal(10, scenario.EndTick);
    }

    [Theory]
    [InlineData("version 2\n", "version 1")]
    [InlineData("version 1\nmap m.map\nagent 1 1 1\nend 5\n", "consecutively")]
    [InlineData("version 1\nmap m.map\nagent 0 1 1\norder 0 3 5 5\nend 5\n", "unknown agent")]
    [InlineData("version 1\nmap m.map\nagent 0 1 1\norder 5 0 5 5\norder 1 0 6 6\nend 9\n", "tick sequence")]
    [InlineData("version 1\nmap m.map\nagent 0 1 1\nsprint 3\nend 5\n", "unrecognised")]
    [InlineData("version 1\nmap m.map\nagent 0 1 1\n", "'end'")]
    [InlineData("version 1\nagent 0 1 1\nend 5\n", "'map'")]
    public void AMalformedScenarioIsRefusedWithAReason(string text, string expected)
    {
        var error = Assert.Throws<MapFormatException>(() => RecordedScenario.FromText(text));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAgentPlacedOnAWallIsRefused()
    {
        var scenario = RecordedScenario.FromText(
            "version 1\nmap hall.map\nagent 0 0 0\nend 5\n");
        var grid = Grid.FromMapFile(Fixtures.Map("hall.map"));

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => ScenarioPlayback.Play(scenario, grid));

        Assert.Contains("not passable", error.Message, StringComparison.Ordinal);
    }

    // --- criteria 1 and 2, over every committed scenario ---------------------

    [Theory]
    [InlineData("headon")]
    [InlineData("chokepoint")]
    [InlineData("group")]
    [InlineData("crossing")]
    [InlineData("standing")]
    [InlineData("crosscut")]
    [InlineData("throng")]
    [InlineData("staggered")]
    [InlineData("countermand")]
    [InlineData("reconcile")]
    public void NoAgentsEverOccupyTheSameSpace(string name)
    {
        var (scenario, grid) = Fixtures.Load(name);

        var outcome = ScenarioPlayback.Play(scenario, grid);

        output.WriteLine(
            $"{name}: {outcome.Ticks} ticks, {outcome.FinalCells.Count} agents, " +
            $"{outcome.Arrived} arrived, {outcome.Stuck} stuck (max stall {outcome.MaxStalledTicks}), " +
            $"{outcome.Conflicts.AgentTicksChecked:N0} agent-ticks checked, " +
            $"{outcome.TotalExpanded:N0} nodes expanded");

        Assert.True(
            outcome.Conflicts.Clean,
            $"{name}: {outcome.Conflicts.Conflicts.Count} conflicts, first few: " +
            string.Join("; ", outcome.Conflicts.Conflicts.Take(5)));

        // A clean verdict over nothing is not evidence.
        Assert.True(
            outcome.Conflicts.AgentTicksChecked >= scenario.Agents.Count * scenario.EndTick,
            $"only {outcome.Conflicts.AgentTicksChecked} agent-ticks were checked");
    }

    [Theory]
    [InlineData("headon")]
    [InlineData("chokepoint")]
    [InlineData("group")]
    [InlineData("crossing")]
    [InlineData("standing")]
    [InlineData("crosscut")]
    [InlineData("throng")]
    [InlineData("staggered")]
    [InlineData("countermand")]
    [InlineData("reconcile")]
    public void EveryMoveInEveryTrajectoryIsLegal(string name)
    {
        // Multi-agent planning must not quietly acquire the ability to cut
        // corners or teleport.
        var (scenario, grid) = Fixtures.Load(name);
        var outcome = ScenarioPlayback.Play(scenario, grid);

        foreach (var (agent, trail) in outcome.Trajectories)
        {
            for (var i = 1; i < trail.Cells.Count; i++)
            {
                var previous = trail.Cells[i - 1];
                if (previous == trail.Cells[i])
                {
                    continue;
                }

                var x = grid.ColumnOf(previous);
                var y = grid.RowOf(previous);
                Assert.True(
                    Movement.IsLegalStep(
                        grid, x, y,
                        grid.ColumnOf(trail.Cells[i]) - x,
                        grid.RowOf(trail.Cells[i]) - y),
                    $"{name}: agent {agent} made an illegal move at tick {i}");
            }
        }
    }

    // --- criterion 5: the cost bound, reported as the ratio ------------------

    [Theory]
    [InlineData("headon")]
    [InlineData("chokepoint")]
    [InlineData("group")]
    [InlineData("crossing")]
    [InlineData("standing")]
    [InlineData("crosscut")]
    [InlineData("throng")]
    [InlineData("staggered")]
    [InlineData("countermand")]
    [InlineData("reconcile")]
    public void TheCostRatioAgainstTheSingleAgentOptimumIsAtLeastOne(string name)
    {
        // A multi-agent solution cheaper than the sum of individual optima is
        // not a better solution; it is a collision the checker missed. The
        // ratio over the lower bound is the quality number.
        var (scenario, grid) = Fixtures.Load(name);
        var outcome = ScenarioPlayback.Play(scenario, grid);

        var actual = 0.0;
        var lowerBound = 0.0;
        foreach (var (agent, trail) in outcome.Trajectories)
        {
            var cells = trail.Cells;
            if (cells[0] == cells[^1] && cells.All(c => c == cells[0]))
            {
                continue;   // never moved and ended at home: contributes zero to both sums
            }

            // Interior waits are paid moves (yielding costs time); the
            // stationary spans before the first move and after the last are
            // not part of the journey.
            var first = 0;
            while (cells[first] == cells[first + 1])
            {
                first++;
            }

            var last = cells.Count - 1;
            while (cells[last] == cells[last - 1])
            {
                last--;
            }

            for (var i = first + 1; i <= last; i++)
            {
                if (cells[i] == cells[i - 1])
                {
                    actual += Movement.WaitCost;
                    continue;
                }

                var diagonal = grid.ColumnOf(cells[i]) != grid.ColumnOf(cells[i - 1]) &&
                               grid.RowOf(cells[i]) != grid.RowOf(cells[i - 1]);
                actual += diagonal ? Movement.ExactCost(0, 1) : Movement.ExactCost(1, 0);
            }

            var optimal = PathFinder.FindPath(grid, cells[0], cells[^1]);
            Assert.True(optimal.Found, $"{name}: agent {agent}'s final cell is unreachable from its start");
            lowerBound += optimal.Cost;
        }

        output.WriteLine(lowerBound > 0
            ? $"{name}: cost {actual:F5} against lower bound {lowerBound:F5}  ratio {actual / lowerBound:F4}"
            : $"{name}: nobody moved; ratio not applicable");

        Assert.True(
            actual >= lowerBound - 1e-6,
            $"{name}: cost {actual:F5} is below the single-agent lower bound {lowerBound:F5}");
    }

    // --- criterion 6, which is what the format exists for --------------------

    [Theory]
    [InlineData("headon")]
    [InlineData("chokepoint")]
    [InlineData("group")]
    [InlineData("crossing")]
    [InlineData("standing")]
    [InlineData("crosscut")]
    [InlineData("throng")]
    [InlineData("staggered")]
    [InlineData("countermand")]
    [InlineData("reconcile")]
    public void ReplayingSaysExactlyTheSameThing(string name)
    {
        // The whole per-tick sequence, not the final state. A run that ends in the
        // right place by a different route has still broken determinism.
        var (scenario, grid) = Fixtures.Load(name);

        var first = ScenarioPlayback.Play(scenario, grid);
        var second = ScenarioPlayback.Play(scenario, grid);

        Assert.Equal(first.TotalExpanded, second.TotalExpanded);
        Assert.Equal(first.FinalCells, second.FinalCells);

        for (var agent = 0; agent < first.Trajectories.Count; agent++)
        {
            Assert.Equal(
                first.Trajectories[agent].Plan.Cells,
                second.Trajectories[agent].Plan.Cells);
        }
    }

    // --- what each scenario is actually for ----------------------------------

    [Fact]
    public void HeadOnInACorridorResolvesWithoutPassingThrough()
    {
        var (scenario, grid) = Fixtures.Load("headon");

        var outcome = ScenarioPlayback.Play(scenario, grid);

        Assert.True(outcome.Conflicts.Clean);

        // The corridor admits one crossing, so they cannot both arrive.
        Assert.True(outcome.Arrived < 2, "both agents arrived through a one-wide corridor");
    }

    [Fact]
    public void APermanentDeadlockIsReportedAsStuck()
    {
        // A unit at each end of a one-wide corridor, each ordered to the other's
        // cell. Neither can move and neither ever will.
        //
        // Both nonetheless have PLANS -- the one-cell plan of staying put -- so
        // asking "did the planner return something" calls this healthy. Nothing
        // collides, nothing errors, and nothing happens for sixty ticks. Progress
        // toward the goal is the thing worth measuring.
        var (scenario, grid) = Fixtures.Load("headon");

        var outcome = ScenarioPlayback.Play(scenario, grid);

        Assert.Equal(0, outcome.Arrived);
        Assert.Equal(2, outcome.Stuck);

        // StalledTicks counts failed REPLANS, not ticks. Under the milestone-2
        // timer this was >= 3 probes in sixty ticks; milestone 3 made retries
        // event-driven, and in a deadlock no event ever fires -- so ONE failed
        // replan is the honest count, and MORE would mean the backstop is
        // spinning. The report (both stuck) is the criterion; the probe count
        // is the economy.
        Assert.True(
            outcome.MaxStalledTicks >= 1,
            $"a permanent deadlock should show at least one failed replan, saw {outcome.MaxStalledTicks}");

        // And the backoff is what stops a hopeless retry eating the budget.
        // Before it existed this scenario spent 14,266 nodes replanning two
        // deadlocked agents every tick for sixty ticks, against 126 for a
        // scenario that actually completed.
        Assert.True(
            outcome.TotalExpanded < 5000,
            $"a deadlock cost {outcome.TotalExpanded:N0} nodes; the backoff is not holding");
    }

    [Fact]
    public void AScenarioThatCompletesReportsNobodyStalledAtTheEnd()
    {
        var (scenario, grid) = Fixtures.Load("group");

        var outcome = ScenarioPlayback.Play(scenario, grid);

        Assert.Equal(0, outcome.Stuck);
        Assert.True(outcome.AllArrived);
    }

    [Fact]
    public void AGroupOrderLandsEveryUnitOnItsOwnCell()
    {
        var (scenario, grid) = Fixtures.Load("group");

        var outcome = ScenarioPlayback.Play(scenario, grid);

        Assert.Equal(12, outcome.FinalCells.Count);
        Assert.Equal(12, outcome.FinalCells.Distinct().Count());
        Assert.True(outcome.AllArrived, $"only {outcome.Arrived} of 12 arrived");
    }

    [Fact]
    public void UnitsWithNoOrderNeverMove()
    {
        // Agents 0, 1 and 2 are given no order at all. They must still be
        // obstacles, and they must still be exactly where they started.
        var (scenario, grid) = Fixtures.Load("standing");

        var outcome = ScenarioPlayback.Play(scenario, grid);

        foreach (var idle in new[] { 0, 1, 2 })
        {
            var trail = outcome.Trajectories[idle].Plan.Cells;
            Assert.True(trail.Distinct().Count() == 1, $"agent {idle} moved without being ordered to");
            Assert.Equal(grid.Index(scenario.Agents[idle].X, scenario.Agents[idle].Y), outcome.FinalCells[idle]);
        }
    }

    [Fact]
    public void EveryoneGetsThroughTheChokepointEventually()
    {
        var (scenario, grid) = Fixtures.Load("chokepoint");

        var outcome = ScenarioPlayback.Play(scenario, grid);

        Assert.True(outcome.Conflicts.Clean);
        Assert.True(
            outcome.Arrived == 8,
            $"only {outcome.Arrived} of 8 got through; {outcome.Stuck} were stuck");
    }
}
