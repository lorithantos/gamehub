namespace Nav.Core.Tests;

/// <summary>
/// Every committed scenario, replayed under orderings the production heap never
/// picks: collision-freedom must hold for all of them.
/// </summary>
/// <remarks>
/// A* is indifferent between frontier entries that tie exactly on <c>(f, h)</c>,
/// so which one pops first is an accident of heap layout, and the collision core
/// must be correct whichever accident occurs. The PriorityQueue spike changed
/// that accident once and found a real hole; this changes it sixteen times per
/// scenario, on purpose, with each ordering fixed by its seed.
/// <para>
/// Reproducibility is the property that makes a failure here worth anything, and
/// it is asserted rather than assumed: the same seed replayed twice must produce
/// identical trajectories. If that ever fails, the fuzz heap has stopped being
/// deterministic and nothing else in this file can be trusted.
/// </para>
/// </remarks>
public sealed class TieBreakFuzzTests
{
    private const int Seeds = 16;

    public static IEnumerable<object[]> Scenarios =>
        new[] { "chokepoint", "countermand", "crosscut", "crossing", "group", "headon", "reconcile", "staggered", "standing", "throng" }
            .Select(name => new object[] { name });

    private static (RecordedScenario Scenario, Grid Grid) Load(string name)
    {
        var scenario = RecordedScenario.FromFile(Fixtures.Scenario(name));
        return (scenario, Grid.FromMapFile(Fixtures.Map(scenario.MapName)));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void NoTieBreakProducesACollision(string name)
    {
        var (scenario, grid) = Load(name);
        var failures = new List<string>();

        for (var seed = 0; seed < Seeds; seed++)
        {
            var outcome = ScenarioPlayback.Play(scenario, grid, tieBreakSeed: seed);
            if (!outcome.Conflicts.Clean)
            {
                failures.Add($"seed {seed}: {outcome.Conflicts.Conflicts[0]}");
            }
        }

        // Every failing seed is reported, not just the first, because two seeds
        // failing at the same cell and tick and one seed failing somewhere else
        // are different findings.
        Assert.True(failures.Count == 0, $"{name}: {failures.Count} of {Seeds} seeds collided:\n  " + string.Join("\n  ", failures));
    }

    public static IEnumerable<object[]> ScenariosUnderTightBudgets =>
        Scenarios.SelectMany(s => new[] { 50, 200 }.Select(budget => new[] { s[0], budget }));

    [Theory]
    [MemberData(nameof(ScenariosUnderTightBudgets))]
    public void NoTieBreakProducesACollisionWhenSearchesSuspend(string name, int nodeBudgetPerTick)
    {
        // The default-budget fuzz above says little about one class of defect,
        // because at 4,000 nodes a tick almost no search is ever suspended. A
        // search that IS suspended holds a frontier validated against the table
        // as it stood, and commits later against a table that may have moved --
        // and the reservation ring's Mark writes unconditionally. If that hole is
        // reachable, this is the regime that reaches it: fifty nodes a tick is
        // the setting the design notes cite as the one where nothing completes
        // inside its slack.
        var (scenario, grid) = Load(name);
        var failures = new List<string>();

        for (var seed = 0; seed < 8; seed++)
        {
            var outcome = ScenarioPlayback.Play(scenario, grid, tieBreakSeed: seed, nodeBudgetPerTick: nodeBudgetPerTick);
            if (!outcome.Conflicts.Clean)
            {
                failures.Add($"seed {seed}: {outcome.Conflicts.Conflicts[0]}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{name} at budget {nodeBudgetPerTick}: {failures.Count} of 8 seeds collided:\n  " + string.Join("\n  ", failures));
    }

    public static IEnumerable<object[]> ScenariosAtOtherHorizons =>
        Scenarios.SelectMany(s => new[] { 8, 16, 64 }.Select(horizon => new[] { s[0], horizon }));

    [Theory]
    [MemberData(nameof(ScenariosAtOtherHorizons))]
    public void NoTieBreakProducesACollisionAtOtherHorizons(string name, int horizon)
    {
        // Every run above used the default window of 32. A short window is the
        // regime where plans reach the window edge constantly and the parking
        // rule, not the ring, is what keeps an arrived unit visible; a long one
        // is where the ring carries the most state across the most ticks. The
        // collision verdict has to hold in both, under orderings the production
        // heap never picks.
        var (scenario, grid) = Load(name);
        var failures = new List<string>();

        for (var seed = 0; seed < 8; seed++)
        {
            var outcome = ScenarioPlayback.Play(scenario, grid, horizon: horizon, tieBreakSeed: seed);
            if (!outcome.Conflicts.Clean)
            {
                failures.Add($"seed {seed}: {outcome.Conflicts.Conflicts[0]}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{name} at horizon {horizon}: {failures.Count} of 8 seeds collided:\n  " + string.Join("\n  ", failures));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ASeedReplaysToTheSameTrajectories(string name)
    {
        // The self-check. Seed 7 is arbitrary; what matters is that it is the
        // same seed both times.
        var (scenario, grid) = Load(name);

        var first = ScenarioPlayback.Play(scenario, grid, tieBreakSeed: 7);
        var second = ScenarioPlayback.Play(scenario, grid, tieBreakSeed: 7);

        Assert.Equal(first.FinalCells, second.FinalCells);
        Assert.Equal(first.Conflicts.Conflicts.Count, second.Conflicts.Conflicts.Count);
        Assert.Equal(first.TotalExpanded, second.TotalExpanded);
        for (var i = 0; i < first.Trajectories.Count; i++)
        {
            Assert.Equal(first.Trajectories[i].Plan.Cells, second.Trajectories[i].Plan.Cells);
        }
    }

    [Fact]
    public void ANullSeedIsTheProductionOrdering()
    {
        // The seam must be invisible when unused: an unseeded run is the same run
        // as before the third key existed, expansion for expansion.
        var (scenario, grid) = Load("throng");

        var unseeded = ScenarioPlayback.Play(scenario, grid);
        var explicitNull = ScenarioPlayback.Play(scenario, grid, tieBreakSeed: null);

        Assert.Equal(unseeded.TotalExpanded, explicitNull.TotalExpanded);
        Assert.Equal(unseeded.FinalCells, explicitNull.FinalCells);
    }

    [Fact]
    public void DifferentSeedsActuallyProduceDifferentRuns()
    {
        // If every seed searched identically the fuzz would be exploring nothing.
        // Pinned on a search rather than a scenario: a group follows its field
        // and searches only when blocked, so no committed scenario is guaranteed
        // to contain a search with ties in it any more. A block in the middle of
        // a room, with start and goal on its axis of symmetry, is guaranteed to:
        // the way round above and the way round below tie exactly on (f, h) at
        // every step, and only the third key decides. Sixteen seeds must not all
        // walk the same way round.
        var grid = Grid.FromMapText(
            """
            type octile
            height 13
            width 13
            map
            .............
            .............
            .............
            .............
            .............
            .....@@@.....
            .....@@@.....
            .....@@@.....
            .............
            .............
            .............
            .............
            .............
            """);
        var table = new ReservationTable(grid.CellCount, 32);

        var paths = Enumerable.Range(0, Seeds)
            .Select(seed => string.Join(",", new BudgetedSearch(
                grid, table, agent: 0, grid.Index(1, 6), grid.Index(11, 6), 0,
                new SearchWorkspace(tieBreakSeed: seed)).RunToCompletion().Cells))
            .Distinct()
            .Count();

        Assert.True(paths > 1, "every seed produced an identical path; the tie-break key is not being consulted");
    }
}
