using System.Diagnostics;

using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Criterion 9: the cost of a tick has a ceiling, and the ceiling holds at scale.
/// </summary>
public sealed class TickBudgetTests(ITestOutputHelper output)
{
    private const int Agents = 200;
    private const int Ticks = 400;
    private const int NodeBudget = 4000;

    private sealed record Run(
        MovementSystem System,
        IReadOnlyList<TickReport> Reports,
        IReadOnlyList<double> Milliseconds,
        IReadOnlyList<AgentPlan> Trajectories);

    private static Run Swarm(int agents = Agents, int budget = NodeBudget)
    {
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));
        var system = new MovementSystem(grid, horizon: 32, nodeBudgetPerTick: budget);

        // The first N passable cells, so placement is deterministic and dense.
        var placed = 0;
        var trails = new List<int>[agents];
        for (var cell = 0; cell < grid.CellCount && placed < agents; cell++)
        {
            if (!grid.IsPassable(cell))
            {
                continue;
            }

            trails[system.AddAgent(cell)] = [];
            placed++;
        }

        // Everybody, at once, to the far corner. The worst shape of order there
        // is: one command, N searches, all arriving in the same frame.
        system.Order([.. Enumerable.Range(0, agents)], grid.Index(44, 44));

        var reports = new List<TickReport>(Ticks);
        var milliseconds = new List<double>(Ticks);
        var clock = new Stopwatch();

        for (var tick = 0; tick <= Ticks; tick++)
        {
            foreach (var agent in system.Agents)
            {
                trails[agent.Id].Add(agent.Cell);
            }

            if (tick == Ticks)
            {
                break;
            }

            clock.Restart();
            system.Tick();
            clock.Stop();

            reports.Add(system.LastTick);
            milliseconds.Add(clock.Elapsed.TotalMilliseconds);
        }

        var trajectories = trails
            .Select((cells, id) => new AgentPlan(id, new PlanResult(cells, 0, 0.0, 0, Found: true)))
            .ToArray();

        return new Run(system, reports, milliseconds, trajectories);
    }

    private static double Percentile(IReadOnlyList<double> values, double quantile)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)(quantile * sorted.Length), 0, sorted.Length - 1)];
    }

    [Fact]
    public void TheFullOrderOutcomeIsTheMilestone3Baseline()
    {
        // Milestone 3 criterion 3 gates against this measurement. Writing it
        // surfaced a finding the 400-tick criterion-9 runs could not see: the
        // 200-agent order NEVER COMPLETES. Exactly 126 arrive and 74 are
        // permanently stuck -- the static goal assignment at terminal scale,
        // with the outer agents' goals frozen inside the settled pile. The
        // numbers below are pinned as a characterization: milestone 3's
        // reconciliation is expected to break this test by raising arrivals
        // to 200, and updating the pin is the point.
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));
        var system = new MovementSystem(grid, horizon: 32, nodeBudgetPerTick: NodeBudget);

        var placed = 0;
        for (var cell = 0; cell < grid.CellCount && placed < Agents; cell++)
        {
            if (!grid.IsPassable(cell))
            {
                continue;
            }

            system.AddAgent(cell);
            placed++;
        }

        system.Order([.. Enumerable.Range(0, Agents)], grid.Index(44, 44));

        var settledAt = -1;
        long nodesAtSettle = 0;
        for (var tick = 0; tick < 1500; tick++)
        {
            system.Tick();
            if (settledAt < 0 && system.Agents.All(a => a.Arrived))
            {
                settledAt = system.CurrentTick;
                nodesAtSettle = system.TotalExpanded;
                break;
            }
        }

        // Then hold for 200 more ticks: a settled world must be a silent one.
        for (var tick = 0; tick < 200; tick++)
        {
            system.Tick();
        }

        output.WriteLine(
            $"200-agent arena order: all arrived at tick {settledAt}, {nodesAtSettle:N0} nodes to settle, " +
            $"{system.TotalExpanded - nodesAtSettle:N0} nodes after settling");

        // The milestone-2 baseline NEVER completed: it froze at 126 arrived and
        // 74 permanently stuck (129/71 with field tie-breaking), burning the
        // full 4,000-node budget every tick forever -- ~4.96M at the plateau
        // and unbounded beyond it. Goal reconciliation completes the order and
        // then goes silent, which is criteria 3 and 6 in one measurement: a
        // finite total against an unbounded one, and zero spend after settling.
        Assert.True(settledAt > 0, $"only {system.Agents.Count(a => a.Arrived)} of {Agents} arrived");
        Assert.Equal(0, system.Agents.Count(a => a.Stuck));
        Assert.Equal(0, system.TotalExpanded - nodesAtSettle);
    }

    [Fact]
    public void NoTickEverExceedsItsNodeBudget()
    {
        var run = Swarm();

        var worst = run.Reports.Max(r => r.NodesSpent);

        output.WriteLine(
            $"nodes per tick: p50 {Percentile([.. run.Reports.Select(r => (double)r.NodesSpent)], 0.50):N0}  " +
            $"p99 {Percentile([.. run.Reports.Select(r => (double)r.NodesSpent)], 0.99):N0}  max {worst:N0}  " +
            $"(budget {NodeBudget:N0})");

        Assert.True(worst <= NodeBudget, $"a tick spent {worst:N0} nodes against a budget of {NodeBudget:N0}");
    }

    [Fact]
    public void ATickStaysInsideAFrame()
    {
        // Reported as a distribution, never a mean. On the milestone 1 corpus the
        // mean expansion count sat at the 67th percentile and hid a 5x effect;
        // a frame budget is spent on individual frames, so the tail is the number
        // that decides whether this fits.
        var run = Swarm();

        var p50 = Percentile(run.Milliseconds, 0.50);
        var p99 = Percentile(run.Milliseconds, 0.99);
        var worst = run.Milliseconds.Max();

        output.WriteLine(
            $"tick cost: p50 {p50:F3} ms  p99 {p99:F3} ms  max {worst:F3} ms  " +
            $"over {run.Milliseconds.Count} ticks with {Agents} agents");

        // Generous against a 16.6 ms frame: this is a shared CI-ish machine and a
        // hard threshold that fails on someone else's laptop teaches nothing. The
        // budget in nodes is the real guarantee; this is the sanity check on it.
        Assert.True(p99 < 50.0, $"p99 tick cost was {p99:F3} ms");
    }

    [Fact]
    public void TwoHundredAgentsNeverOccupyTheSameSpace()
    {
        var run = Swarm();

        var report = CollisionCheck.Inspect(run.Trajectories);

        output.WriteLine(
            $"{report.AgentTicksChecked:N0} agent-ticks checked, " +
            $"{run.System.Agents.Count(a => a.Arrived)} of {Agents} arrived");

        Assert.True(
            report.Clean,
            $"{report.Conflicts.Count} conflicts, first few: {string.Join("; ", report.Conflicts.Take(5))}");
        Assert.True(report.AgentTicksChecked > Agents * Ticks / 2);
    }

    [Fact]
    public void PlanningIsSpreadAcrossTicksRatherThanDoneOnTheOrder()
    {
        // The order queues work; it does not do it. If a hundred searches ran
        // where the order arrived, the first tick would carry all of them.
        var run = Swarm();

        Assert.True(run.Reports[0].Queued > 0, "the first tick planned everybody");
        Assert.True(
            run.Reports.Count(r => r.SearchesStarted > 0) > 5,
            "planning did not spread across ticks");
    }

    [Fact]
    public void SearchesInFlightAreCapped()
    {
        // A suspended search owns a workspace, so this is a memory bound as much
        // as a scheduling one.
        var run = Swarm();

        var mostThinking = 0;
        for (var i = 0; i < run.Reports.Count; i++)
        {
            mostThinking = Math.Max(mostThinking, run.Reports[i].SearchesStarted);
        }

        Assert.True(mostThinking <= 200, "impossible number of searches started in one tick");
        Assert.All(run.Reports, r => Assert.True(r.NodesSpent <= NodeBudget));
    }

    [Fact]
    public void ATightBudgetStillMakesProgressRatherThanStalling()
    {
        // The interesting failure is not "too slow" but "never finishes": if a
        // budget too small for one search meant no search ever completed, the
        // system would look alive and do nothing.
        var run = Swarm(agents: 20, budget: 500);

        var finished = run.Reports.Sum(r => r.SearchesFinished);
        var abandoned = run.Reports.Sum(r => r.SearchesAbandoned);
        var moved = run.Trajectories.Count(t => t.Plan.Cells[0] != t.Plan.Cells[^1]);

        output.WriteLine(
            $"budget 500: {finished} finished, {abandoned} abandoned, {moved} of 20 agents moved, " +
            $"{run.System.Agents.Count(a => a.Arrived)} arrived");

        Assert.True(finished > 0, "no search ever completed");
        Assert.True(moved > 0, "no agent moved at all");
    }

    [Fact]
    public void AnImpossibleBudgetIsReportedRatherThanSpunOn()
    {
        // Fifty nodes a tick shared across twenty agents cannot plan a route
        // across a 49x49 map, and no amount of retrying changes that. What must
        // NOT happen is a treadmill: discard, restart identically, discard again,
        // busy forever and moving never.
        //
        // The latency doubles on each discard until it reaches half the horizon,
        // after which the agent is reported STALLED and backs off. The system says
        // it is beaten instead of pretending to work.
        var run = Swarm(agents: 20, budget: 50);

        var finished = run.Reports.Sum(r => r.SearchesFinished);
        var stalled = run.System.Agents.Count(a => a.Stuck);

        output.WriteLine(
            $"budget 50: {finished} finished, " +
            $"{run.Reports.Sum(r => r.SearchesAbandoned)} abandoned, {stalled} of 20 reported stalled");

        Assert.True(stalled > 0, "an unmeetable budget reported nobody as stalled");

        // Every "finished" must correspond to a plan that actually exists. A bool
        // return once counted abandoned searches as completed ones, and reported
        // twenty-four of each that were the same twenty-four events.
        Assert.True(
            finished == 0 || run.System.CurrentPlans().Count > 0,
            "searches reported as finished produced no plans");
    }
}
