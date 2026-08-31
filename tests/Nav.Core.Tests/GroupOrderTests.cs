namespace Nav.Core.Tests;

/// <summary>
/// Milestone 2, step 4: many agents, one order, and criterion 5 — the cost bound
/// borrowed from milestone 1's verified search.
/// </summary>
public sealed class GroupOrderTests
{
    private const int Horizon = 64;

    /// <summary>A 10x10 interior, room enough for a group to spread into.</summary>
    private const string Hall =
        """
        type octile
        height 12
        width 12
        map
        @@@@@@@@@@@@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @..........@
        @@@@@@@@@@@@
        """;

    /// <summary>Two pockets joined by nothing: the left one is three cells.</summary>
    private const string Split =
        """
        type octile
        height 3
        width 7
        map
        @@@@@@@
        @.@...@
        @@@@@@@
        """;

    // --- spreading -----------------------------------------------------------

    [Fact]
    public void TheTargetItselfIsTheFirstDestination()
    {
        var grid = Grid.FromMapText(Hall);
        var target = grid.Index(5, 5);

        var cells = GoalSpread.Nearest(grid, target, 4);

        Assert.Equal(target, cells[0]);
    }

    [Fact]
    public void EveryDestinationIsDistinctAndPassable()
    {
        var grid = Grid.FromMapText(Hall);

        var cells = GoalSpread.Nearest(grid, grid.Index(5, 5), 12);

        Assert.Equal(12, cells.Count);
        Assert.Equal(12, cells.Distinct().Count());
        Assert.All(cells, cell => Assert.True(grid.IsPassable(cell)));
    }

    [Fact]
    public void DestinationsSpreadOutwardsFromTheTarget()
    {
        var grid = Grid.FromMapText(Hall);
        var target = grid.Index(5, 5);

        var cells = GoalSpread.Nearest(grid, target, 9);

        // Breadth-first, so distance from the target never decreases along the list.
        var distances = cells
            .Select(c => Movement.OctileDistance(grid.ColumnOf(c), grid.RowOf(c), 5, 5))
            .ToArray();

        for (var i = 1; i < distances.Length; i++)
        {
            Assert.True(distances[i] >= distances[i - 1] - 1e-9, $"distance fell at index {i}");
        }
    }

    [Fact]
    public void ACellBehindAWallIsNotNearby()
    {
        // Reachability, not proximity. The right-hand pocket is two columns away
        // and unreachable, so it must not be offered as a destination.
        var grid = Grid.FromMapText(Split);

        var cells = GoalSpread.Nearest(grid, grid.Index(1, 1), 5);

        Assert.Single(cells);
        Assert.Equal(grid.Index(1, 1), cells[0]);
    }

    [Fact]
    public void AskingForMoreCellsThanExistReturnsWhatThereIs()
    {
        var grid = Grid.FromMapText(Split);

        var cells = GoalSpread.Nearest(grid, grid.Index(3, 1), 99);

        Assert.Equal(3, cells.Count);
    }

    [Fact]
    public void AnImpassableTargetHasNoDestinations()
    {
        var grid = Grid.FromMapText(Hall);

        Assert.Empty(GoalSpread.Nearest(grid, grid.Index(0, 0), 4));
    }

    // --- assignment ----------------------------------------------------------

    [Fact]
    public void EveryAgentGetsItsOwnDestination()
    {
        var grid = Grid.FromMapText(Hall);
        var agents = Squad(grid, 6);

        var assigned = GoalSpread.Assign(grid, grid.Index(8, 8), agents);

        Assert.Equal(6, assigned.Count);
        Assert.Equal(6, assigned.Select(a => a.Goal).Distinct().Count());
        Assert.Equal(agents.Select(a => a.Agent).Order(), assigned.Select(a => a.Agent));
    }

    [Fact]
    public void TheSameOrderTwiceAssignsTheSameDestinations()
    {
        var grid = Grid.FromMapText(Hall);
        var agents = Squad(grid, 8);

        var first = GoalSpread.Assign(grid, grid.Index(8, 8), agents);
        var second = GoalSpread.Assign(grid, grid.Index(8, 8), agents);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheNearestAgentTakesTheNearestDestination()
    {
        var grid = Grid.FromMapText(Hall);
        var target = grid.Index(9, 9);

        // Agent 1 starts beside the target; agent 0 is across the hall.
        (int Agent, int Cell)[] agents = [(0, grid.Index(1, 1)), (1, grid.Index(8, 9))];

        var assigned = GoalSpread.Assign(grid, target, agents);

        Assert.Equal(target, assigned.Single(a => a.Agent == 1).Goal);
    }

    // --- criterion 5 ---------------------------------------------------------

    [Fact]
    public void AGroupPlanCostsAtLeastTheSumOfIndividualOptima()
    {
        // CRITERION 5. There is no published optimal for a multi-agent plan, so the
        // truth source is milestone 1's verified single-agent search: no
        // collision-free solution can cost LESS than agents moving as if alone.
        // A total below this bound is not a better answer, it is a collision the
        // checker missed.
        var grid = Grid.FromMapText(Hall);
        var (plans, assigned) = PlanSquad(grid, size: 6, target: grid.Index(8, 8));

        Assert.All(plans, p => Assert.True(p.Plan.Found, $"agent {p.Agent} did not arrive"));

        var lowerBound = 0.0;
        var starts = Squad(grid, 6).ToDictionary(a => a.Agent, a => a.Cell);
        foreach (var (agent, goal) in assigned)
        {
            lowerBound += PathFinder.FindPath(grid, starts[agent], goal).Cost;
        }

        var total = plans.Sum(p => p.Plan.Cost);

        Assert.True(
            total >= lowerBound - 1e-6,
            $"sum of costs {total:F5} is below the single-agent lower bound {lowerBound:F5}");
    }

    [Fact]
    public void AGroupPlanIsFreeOfBothCollisionKinds()
    {
        var grid = Grid.FromMapText(Hall);
        var (plans, _) = PlanSquad(grid, size: 8, target: grid.Index(8, 8));

        var report = CollisionCheck.Inspect(plans);

        Assert.True(report.Clean, $"conflicts: {string.Join("; ", report.Conflicts)}");
        Assert.True(report.AgentTicksChecked > 8);
    }

    [Fact]
    public void EveryAgentInAGroupArrivesSomewhereDistinct()
    {
        var grid = Grid.FromMapText(Hall);
        var (plans, _) = PlanSquad(grid, size: 8, target: grid.Index(8, 8));

        var finals = plans.Select(p => p.Plan.Cells[^1]).ToArray();

        Assert.Equal(8, finals.Distinct().Count());
    }

    [Fact]
    public void EveryStepOfEveryGroupPlanIsLegal()
    {
        var grid = Grid.FromMapText(Hall);
        var (plans, _) = PlanSquad(grid, size: 8, target: grid.Index(8, 8));

        foreach (var (agent, plan) in plans)
        {
            for (var i = 1; i < plan.Cells.Count; i++)
            {
                var previous = plan.Cells[i - 1];
                if (previous == plan.Cells[i])
                {
                    continue;
                }

                var x = grid.ColumnOf(previous);
                var y = grid.RowOf(previous);
                Assert.True(
                    Movement.IsLegalStep(
                        grid, x, y,
                        grid.ColumnOf(plan.Cells[i]) - x,
                        grid.RowOf(plan.Cells[i]) - y),
                    $"agent {agent}, tick {i}: illegal move");
            }
        }
    }

    [Fact]
    public void TheSameGroupOrderTwiceProducesTheSamePlans()
    {
        var grid = Grid.FromMapText(Hall);

        var (first, _) = PlanSquad(grid, size: 6, target: grid.Index(8, 8));
        var (second, _) = PlanSquad(grid, size: 6, target: grid.Index(8, 8));

        Assert.Equal(first.Select(p => p.Plan.Cells), second.Select(p => p.Plan.Cells));
        Assert.Equal(first.Sum(p => p.Plan.Expanded), second.Sum(p => p.Plan.Expanded));
    }

    // --- helpers -------------------------------------------------------------

    private static (int Agent, int Cell)[] Squad(Grid grid, int size) =>
        [.. Enumerable.Range(0, size).Select(i => (i, grid.Index(1 + (i % 3), 1 + (i / 3))))];

    private static (List<AgentPlan> Plans, IReadOnlyList<(int Agent, int Goal)> Assigned) PlanSquad(
        Grid grid,
        int size,
        int target)
    {
        var agents = Squad(grid, size);
        var assigned = GoalSpread.Assign(grid, target, agents);

        var table = new ReservationTable(grid.CellCount, Horizon);
        var workspace = new SearchWorkspace();
        var starts = agents.ToDictionary(a => a.Agent, a => a.Cell);

        // Agents are NOT pre-reserved. Reserving an unplanned agent's cell holds
        // it for the whole window, so a squad standing together walls itself in
        // and nobody can move -- measured, not theorised. An agent that has not
        // planned yet is simply absent from the table; planning order is what
        // resolves them, and an agent that cannot get out of the way in time
        // reports stuck.
        var plans = new List<AgentPlan>();
        foreach (var (agent, goal) in assigned)
        {
            // Fixed order, by agent id, so a tick is reproducible.
            var plan = CooperativePlanner.FindPlan(grid, table, agent, starts[agent], goal, 0, workspace);

            // A stuck agent has no plan and would therefore be invisible, both to
            // the agents planning after it and to the collision checker. It is
            // standing still, so that is what it gets: a one-cell plan, reserved
            // like any other.
            var settled = plan.IsStuck
                ? new PlanResult([starts[agent]], 0, 0.0, plan.Expanded, Found: false)
                : plan;

            table.Reserve(settled.Cells, settled.StartTick, agent);
            plans.Add(new AgentPlan(agent, settled));
        }

        return (plans, assigned);
    }
}
