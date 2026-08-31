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

    // --- criterion 6, which is what the format exists for --------------------

    [Theory]
    [InlineData("headon")]
    [InlineData("chokepoint")]
    [InlineData("group")]
    [InlineData("crossing")]
    [InlineData("standing")]
    [InlineData("crosscut")]
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
        Assert.True(
            outcome.MaxStalledTicks > 10,
            $"a permanent deadlock should show a long stall, saw {outcome.MaxStalledTicks}");
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
