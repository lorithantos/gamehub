namespace Nav.Core.Tests;

/// <summary>
/// Milestone 2's step 3 gate: two agents cannot pass through each other, and the
/// checker that says so must be able to say otherwise.
/// </summary>
public sealed class CollisionCheckTests
{
    private const int Horizon = 24;

    private const string Corridor =
        """
        type octile
        height 3
        width 7
        map
        @@@@@@@
        @.....@
        @@@@@@@
        """;

    private const string Room =
        """
        type octile
        height 5
        width 7
        map
        @@@@@@@
        @.....@
        @.....@
        @.....@
        @@@@@@@
        """;

    private static PlanResult Handmade(int startTick, params int[] cells) =>
        new(cells, startTick, cells.Length, 0, Found: true);

    // --- the checker must be able to fail ------------------------------------

    [Fact]
    public void AVertexCollisionIsReported()
    {
        // Both agents arrive on cell 10 at tick 2.
        var report = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 8, 9, 10)),
            new AgentPlan(1, Handmade(0, 12, 11, 10)),
        ]);

        Assert.False(report.Clean);
        Assert.Equal(1, report.CountOf(ConflictKind.Vertex));
        Assert.Equal(2, report.Conflicts[0].Tick);
        Assert.Equal(10, report.Conflicts[0].Cell);
    }

    [Fact]
    public void AnEdgeCollisionIsReported()
    {
        // 9 -> 10 against 10 -> 9. Neither shares a cell at either tick; they pass
        // straight through each other. This is the case a vertex-only checker
        // calls clean, which is why it has its own test.
        var report = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 9, 10)),
            new AgentPlan(1, Handmade(0, 10, 9)),
        ]);

        Assert.Equal(0, report.CountOf(ConflictKind.Vertex));
        Assert.Equal(1, report.CountOf(ConflictKind.Edge));
        Assert.Equal(0, report.Conflicts[0].Tick);
    }

    [Fact]
    public void AnArrivedAgentIsStillAnObstacle()
    {
        // Agent 0's plan ends at tick 1; it is standing on 10 thereafter. Agent 1
        // walking onto 10 at tick 3 collides with it. Treating a finished plan as
        // an absence is how stationary units become invisible.
        var report = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 9, 10)),
            new AgentPlan(1, Handmade(0, 8, 8, 9, 10)),
        ]);

        Assert.False(report.Clean);
        Assert.Contains(report.Conflicts, c => c.Kind == ConflictKind.Vertex && c.Tick == 3);
    }

    [Fact]
    public void FollowingNoseToTailIsNotACollision()
    {
        // Agent 1 walks into each cell exactly as agent 0 leaves it.
        var report = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 9, 10, 11)),
            new AgentPlan(1, Handmade(0, 8, 9, 10)),
        ]);

        // Agent 0 stops on 11; agent 1 stops on 10. No overlap at any tick.
        Assert.True(report.Clean);
    }

    [Fact]
    public void TheReportSaysHowMuchItLookedAt()
    {
        // A clean verdict over nothing is not evidence.
        var report = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 9, 10, 11)),
            new AgentPlan(1, Handmade(0, 1, 2, 3)),
        ]);

        Assert.True(report.Clean);
        Assert.Equal(6, report.AgentTicksChecked);
    }

    [Fact]
    public void NoPlansIsCleanButChecksNothing()
    {
        var report = CollisionCheck.Inspect([]);

        Assert.True(report.Clean);
        Assert.Equal(0, report.AgentTicksChecked);
    }

    [Fact]
    public void ThreeAgentsOnOneCellIsThreeCollidingPairs()
    {
        // Pairing every arrival with the FIRST occupant alone would report two
        // conflicts here -- 0 with 1, 0 with 2 -- and never that 1 and 2 are also
        // standing on each other. CountOf would then be a count of reports rather
        // than of collisions, which is not a number worth asserting on.
        var report = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 9, 10)),
            new AgentPlan(1, Handmade(0, 11, 10)),
            new AgentPlan(2, Handmade(0, 17, 10)),
        ]);

        Assert.Equal(3, report.CountOf(ConflictKind.Vertex));
        Assert.All(report.Conflicts, c => Assert.Equal(10, c.Cell));

        var pairs = report.Conflicts.Select(c => (c.AgentA, c.AgentB)).ToHashSet();
        Assert.Equal([(0, 1), (0, 2), (1, 2)], pairs);
    }

    [Fact]
    public void AVertexPairIsOrderedByIdRegardlessOfPlanOrder()
    {
        // The edge check has always ordered its pair with an `agent < mover`
        // guard. The vertex check used to emit whichever agent appeared earlier in
        // the list, so the SAME collision reported (0,1) or (1,0) depending on how
        // the caller happened to sort its plans -- and AgentA meant two different
        // things depending on the kind.
        var ascending = CollisionCheck.Inspect(
        [
            new AgentPlan(0, Handmade(0, 9, 10)),
            new AgentPlan(1, Handmade(0, 11, 10)),
        ]);

        var descending = CollisionCheck.Inspect(
        [
            new AgentPlan(1, Handmade(0, 11, 10)),
            new AgentPlan(0, Handmade(0, 9, 10)),
        ]);

        Assert.Equal(0, ascending.Conflicts[0].AgentA);
        Assert.Equal(1, ascending.Conflicts[0].AgentB);
        Assert.Equal(0, descending.Conflicts[0].AgentA);
        Assert.Equal(1, descending.Conflicts[0].AgentB);
    }

    // --- the gate ------------------------------------------------------------

    [Fact]
    public void TwoAgentsHeadOnInACorridorDoNotPassThroughEachOther()
    {
        // THE GATE. A one-wide corridor gives them nowhere to pass, so the only
        // collision-free outcome is that one of them does not get through. What
        // must never happen is that they swap.
        var grid = Grid.FromMapText(Corridor);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var workspace = new SearchWorkspace();

        var west = grid.Index(1, 1);
        var east = grid.Index(5, 1);

        var first = CooperativePlanner.FindPlan(grid, table, 0, west, east, 0, workspace);
        table.Reserve(first.Cells, first.StartTick, 0);

        var second = CooperativePlanner.FindPlan(grid, table, 1, east, west, 0, workspace);
        table.Reserve(second.Cells, second.StartTick, 1);

        var report = CollisionCheck.Inspect([new AgentPlan(0, first), new AgentPlan(1, second)]);

        Assert.True(
            report.Clean,
            $"expected no conflicts, found {string.Join("; ", report.Conflicts)}");
        Assert.True(report.AgentTicksChecked > 0);

        // The one that planned first gets through.
        Assert.True(first.Found);
        Assert.Equal(east, first.Cells[^1]);

        // The second has nowhere to go and nowhere to wait: it cannot hold its own
        // cell, because agent 0 finishes there, and it cannot step aside, because
        // stepping aside means swapping with agent 0 coming the other way. Saying
        // STUCK is the correct answer under a fixed priority order, and it is a
        // better one than a plan that quietly parks in agent 0's path.
        Assert.True(second.IsStuck);
    }

    [Fact]
    public void ThePlannerWillNotStopSomewhereItMayNotStay()
    {
        // Every step legal, the destination not. Agent 0 passes through cell (3,2)
        // late in the window and finishes beyond it, so an agent that walks there
        // and stops is parked in its way — and because reserving a plan holds its
        // final cell, it would do so by overwriting a reservation already made.
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var workspace = new SearchWorkspace();

        var crossing = grid.Index(3, 2);
        table.Reserve(
            [grid.Index(1, 2), grid.Index(2, 2), crossing, grid.Index(4, 2), grid.Index(5, 2)],
            startTick: 0,
            agent: 0);

        // Agent 1 is asked to go somewhere unreachable, so it will settle for the
        // closest cell it may remain in — which must not be one agent 0 needs.
        var plan = CooperativePlanner.FindPlan(grid, table, 1, grid.Index(1, 1), grid.Index(6, 0), 0, workspace);

        if (!plan.IsStuck)
        {
            var final = plan.Cells[^1];
            Assert.True(
                table.IsHoldable(final, plan.LastTick, agent: 1),
                $"the plan stops on cell {final}, which agent 1 may not remain in");
        }
    }

    [Fact]
    public void ThePlannerRoutesAroundAStandingAgentWhenThereIsRoom()
    {
        // Criterion 8. The same situation as the corridor, but with space, so the
        // right answer is to go around rather than to give up.
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var workspace = new SearchWorkspace();

        var blocker = grid.Index(3, 2);
        var standing = new PlanResult([blocker], 0, 0.0, 0, Found: true);
        table.Reserve(standing.Cells, 0, agent: 0);

        var moving = CooperativePlanner.FindPlan(grid, table, 1, grid.Index(1, 2), grid.Index(5, 2), 0, workspace);

        Assert.True(moving.Found);
        Assert.DoesNotContain(blocker, moving.Cells);

        var report = CollisionCheck.Inspect([new AgentPlan(0, standing), new AgentPlan(1, moving)]);
        Assert.True(report.Clean, $"conflicts: {string.Join("; ", report.Conflicts)}");
    }

    [Fact]
    public void FourAgentsCrossingAnOpenRoomStayApart()
    {
        var grid = Grid.FromMapText(Room);
        var table = new ReservationTable(grid.CellCount, Horizon);
        var workspace = new SearchWorkspace();

        (int Start, int Goal)[] orders =
        [
            (grid.Index(1, 1), grid.Index(5, 3)),
            (grid.Index(1, 3), grid.Index(5, 1)),
            (grid.Index(5, 1), grid.Index(1, 3)),
            (grid.Index(5, 3), grid.Index(1, 1)),
        ];

        var plans = new List<AgentPlan>();
        for (var agent = 0; agent < orders.Length; agent++)
        {
            var plan = CooperativePlanner.FindPlan(
                grid, table, agent, orders[agent].Start, orders[agent].Goal, 0, workspace);
            table.Reserve(plan.Cells, plan.StartTick, agent);
            plans.Add(new AgentPlan(agent, plan));
        }

        var report = CollisionCheck.Inspect(plans);

        Assert.True(report.Clean, $"conflicts: {string.Join("; ", report.Conflicts)}");
        Assert.True(report.AgentTicksChecked >= 4);
    }
}
