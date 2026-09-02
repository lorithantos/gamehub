namespace Nav.Tactics.Tests;

/// <summary>
/// The repair policy as a component: the same retreat-and-return the guard
/// always had, now carried by the patrol too.
/// </summary>
/// <remarks>
/// The guard's own retreat is still pinned in <see cref="GuardDoctrineTests"/>,
/// through the doctrine. What is pinned here is the part that only exists
/// because the rule became a component: a patrol unit is sent to repair while
/// the patrol walks on without it, and comes back to wherever the patrol is by
/// then -- and a casualty detached in the same pass as a sortie is not dragged
/// along by it.
/// </remarks>
public sealed class RepairPolicyTests
{
    private const string Room =
        """
        type octile
        height 11
        width 11
        map
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        ...........
        """;

    private static (MovementSystem System, Grid Grid) Scene(int agents)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        for (var i = 0; i < agents; i++)
        {
            system.AddAgent(grid.Index(i, 0));
        }

        return (system, grid);
    }

    private static void Run(Squad squad, MovementSystem system, ScriptedWorld world, int ticks, Action<int>? between = null)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            between?.Invoke(system.CurrentTick);
            squad.Advance(system, world);
            system.Tick();
        }
    }

    [Fact]
    public void APatrolMemberRetreatsToRepairWhileThePatrolWalksOn()
    {
        var (system, grid) = Scene(agents: 3);
        var pad = grid.Index(5, 10);
        var world = new ScriptedWorld { RepairCells = { pad } };
        var doctrine = new PatrolDoctrine([grid.Index(2, 5), grid.Index(8, 5)], leash: 3.0);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);

        var leftAt = -1;
        var backAt = -1;
        var waypointsWhileAway = 0;
        var lastWaypoint = -1;

        Run(squad, system, world, ticks: 400, between: tick =>
        {
            var unit = system.Agents[1];

            if (tick == 40)
            {
                world.Health[1] = 0.2;
            }

            if (leftAt < 0 && unit.Away)
            {
                leftAt = tick;
                Assert.Equal(pad, unit.Errand);
                lastWaypoint = doctrine.CurrentWaypoint;
            }

            if (unit.Away && doctrine.CurrentWaypoint != lastWaypoint)
            {
                waypointsWhileAway++;
                lastWaypoint = doctrine.CurrentWaypoint;
            }

            if (unit.Cell == pad)
            {
                world.Health[1] = Math.Min(1.0, world.HealthOf(1) + 0.05);
            }

            if (leftAt >= 0 && backAt < 0 && !unit.Away)
            {
                backAt = tick;
            }
        });

        Assert.True(leftAt > 40, "the damaged patroller never left");
        Assert.True(waypointsWhileAway >= 1, "the patrol stopped walking while one member was away");
        Assert.True(backAt > leftAt, "the repaired patroller was never brought back");
        Assert.False(system.Agents[1].Away);
        Assert.True(world.HealthOf(1) >= doctrine.Repair.ReturnAbove, "brought back before it was repaired enough");
    }

    [Fact]
    public void WithoutARepairPointADamagedPatrollerStaysWithThePatrol()
    {
        var (system, grid) = Scene(agents: 3);
        var world = new ScriptedWorld { Health = { [1] = 0.1 } };
        var squad = new Squad("patrol", [0, 1, 2], new PatrolDoctrine([grid.Index(2, 5), grid.Index(8, 5)]));
        var everAway = false;

        Run(squad, system, world, ticks: 150, between: _ => everAway |= system.Agents.Any(a => a.Away));

        Assert.False(everAway);
    }

    [Fact]
    public void ACasualtyDetachedInTheSamePassAsASortieIsNotDraggedAlong()
    {
        // The pass in which the patrol reaches a waypoint issues a sortie to
        // the next one. Damage a member in exactly that pass: the policy
        // detaches it first, and the sortie must not re-order it -- an order
        // ends an errand, so a sortie built from the pass's opening snapshot
        // would have cancelled the retreat it had just begun.
        var (system, grid) = Scene(agents: 3);
        var pad = grid.Index(5, 10);
        var world = new ScriptedWorld { RepairCells = { pad } };
        var doctrine = new PatrolDoctrine([grid.Index(2, 5), grid.Index(8, 5)], leash: 3.0);
        var squad = new Squad("patrol", [0, 1, 2], doctrine);

        var damagedAt = -1;
        Run(squad, system, world, ticks: 200, between: tick =>
        {
            if (damagedAt < 0 && tick > 5 && system.Agents.All(a => a.Arrived))
            {
                damagedAt = tick;
                world.Health[1] = 0.2;
            }
        });

        Assert.True(damagedAt > 0, "the patrol never stood arrived on a waypoint");
        Assert.True(system.Agents[1].Away, "the casualty was dragged into the sortie");
        Assert.Equal(pad, system.Agents[1].Errand);
    }

    [Fact]
    public void TheThresholdsMustNotFlap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepairPolicy(retreatBelow: 0.5, returnAbove: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepairPolicy(retreatBelow: 0.6, returnAbove: 0.4));
    }

    /// <summary>Retreat thresholds by rank: rookie, regular, veteran.</summary>
    private static readonly double[] ByRank = [0.4, 0.55, 0.7];

    private static Squad HoldingSquad(RepairPolicy repair, Grid grid, params int[] members) =>
        new("guard", members, new GuardDoctrine(grid.Index(5, 5), repair));

    [Fact]
    public void AVeteranIsPulledWhileARookieAtTheSameHealthHolds()
    {
        // The doctrine in one assertion, and it is the one that looks wrong on
        // screen: two units, identically hurt, and the BETTER one leaves. Half
        // health is under the veteran's 0.7 and over the rookie's 0.4, so
        // nothing but rank separates them.
        var (system, grid) = Scene(agents: 2);
        var world = new ScriptedWorld
        {
            RepairCells = { grid.Index(0, 10), grid.Index(10, 10) },
            Health = { [0] = 0.5, [1] = 0.5 },
            Rank = { [0] = 0, [1] = 2 },
        };

        var squad = HoldingSquad(new RepairPolicy(ByRank, returnAbove: 0.9), grid, 0, 1);
        Run(squad, system, world, ticks: 60);

        Assert.False(system.Agents[0].Away, "the rookie left the line at a health its rank tolerates");
        Assert.True(system.Agents[1].Away, "the veteran held the line at a health its rank does not");
    }

    [Fact]
    public void TheReserveKeepsThatManyStanding()
    {
        // Everyone is hurt past their threshold, so without a reserve the whole
        // line walks off and the position is abandoned -- which is the failure
        // this project exists over, arrived at from the other direction.
        var (system, grid) = Scene(agents: 4);
        var world = new ScriptedWorld
        {
            RepairCells = { grid.Index(0, 10), grid.Index(10, 10) },
            Health = { [0] = 0.2, [1] = 0.2, [2] = 0.2, [3] = 0.2 },
        };

        var squad = HoldingSquad(new RepairPolicy(ByRank, returnAbove: 0.9, reserve: 2), grid, 0, 1, 2, 3);
        Run(squad, system, world, ticks: 60);

        Assert.Equal(2, system.Agents.Count(a => a.Away));
    }

    [Fact]
    public void AScarcePlaceGoesToTheRookieAndTheVeteranStandsHurt()
    {
        // The other way round from the thresholds, and on purpose. Four equally
        // hurt units and room for one to go: the VETERAN stays, because a
        // veteran's place is the line. It earns faster where the enemy is, at
        // full rank it is meant to heal itself, and its standing there is what
        // makes the position survivable for the rookies beside it. Unit 0 is
        // the rookie that leaves; unit 3 is the veteran holding the line at 0.3,
        // well under its own 0.7 -- which is the point: a veteran can be worn
        // past its threshold like anybody else, and the ordering is about who
        // gets a scarce place, not about who is exempt from needing one.
        var (system, grid) = Scene(agents: 4);
        var world = new ScriptedWorld
        {
            RepairCells = { grid.Index(0, 10), grid.Index(10, 10) },
            Health = { [0] = 0.3, [1] = 0.3, [2] = 0.3, [3] = 0.3 },
            Rank = { [3] = 2 },
        };

        var squad = HoldingSquad(new RepairPolicy(ByRank, returnAbove: 0.9, reserve: 3), grid, 0, 1, 2, 3);
        Run(squad, system, world, ticks: 60);

        var away = system.Agents.Where(a => a.Away).Select(a => a.Id).ToArray();
        Assert.Single(away);
        Assert.Equal(0, away[0]);
        Assert.False(system.Agents[3].Away, "the veteran took a place a rookie still had use for");
    }

    [Fact]
    public void WithRoomTheVeteranIsStillPulledFirst()
    {
        // The pair to the test above, so the two rules cannot be confused for a
        // contradiction. Same squad, no reserve: now everybody under their own
        // threshold goes, and the veteran is under a threshold the rookies are
        // not. Rank raises the bar for LEAVING; it lowers the priority for a
        // scarce PLACE. Different questions, opposite answers, both intended.
        var (system, grid) = Scene(agents: 4);
        var world = new ScriptedWorld
        {
            RepairCells = { grid.Index(0, 10), grid.Index(10, 10) },
            Health = { [0] = 0.5, [1] = 0.5, [2] = 0.5, [3] = 0.5 },
            Rank = { [3] = 2 },
        };

        var squad = HoldingSquad(new RepairPolicy(ByRank, returnAbove: 0.9), grid, 0, 1, 2, 3);
        Run(squad, system, world, ticks: 60);

        var away = system.Agents.Where(a => a.Away).Select(a => a.Id).ToArray();
        Assert.Single(away);
        Assert.Equal(3, away[0]);
    }

    [Fact]
    public void AUnitHealedOnTheWayTurnsRoundWithoutReachingThePad()
    {
        // The seam self-healing will need, pinned before it exists. Healing is
        // meant to be FASTER at the armory rather than exclusive to it, so a
        // veteran keeps mending on the walk and can cross its return threshold
        // before it ever arrives -- at which point the right thing is to turn
        // round and go back to the line, not to finish the errand out of
        // politeness.
        //
        // The rejoin pass already does this: it tests health and the errand
        // being a repair point, and says nothing about having arrived. So the
        // doctrine is ready and only the healing model is missing. Here the
        // healing is faked from outside, which is exactly what a self-heal rate
        // in DemoWorld.Settle would do from inside.
        var (system, grid) = Scene(agents: 1);
        var pad = grid.Index(5, 10);
        var world = new ScriptedWorld { RepairCells = { pad }, Health = { [0] = 0.2 } };
        var squad = HoldingSquad(new RepairPolicy(retreatBelow: 0.4, returnAbove: 0.9), grid, 0);

        var leftAt = -1;
        var everArrivedAway = false;
        var backAt = -1;

        Run(squad, system, world, ticks: 120, between: tick =>
        {
            var unit = system.Agents[0];
            if (unit.Away)
            {
                if (leftAt < 0)
                {
                    leftAt = tick;
                }

                everArrivedAway |= unit.Arrived;

                // Mend it on the road, a few ticks after it sets off and well
                // before it could have walked ten rows to the pad.
                if (tick == leftAt + 4)
                {
                    world.Health[0] = 0.95;
                }
            }
            else if (leftAt >= 0 && backAt < 0)
            {
                backAt = tick;
            }
        });

        Assert.True(leftAt >= 0, "the casualty never set off");
        Assert.True(backAt > leftAt, "the unit never rejoined");
        Assert.False(everArrivedAway, "the unit walked all the way to the pad before turning round");
        Assert.NotEqual(pad, system.Agents[0].Cell);
    }

    [Fact]
    public void ARankAboveTheTableUsesTheLastEntry()
    {
        // So a two-entry table is a complete answer, and a world that invents a
        // rank nobody wrote a row for cannot make the policy throw mid-tick.
        var policy = new RepairPolicy(ByRank, returnAbove: 0.9);

        Assert.Equal(0.4, policy.RetreatBelowFor(0));
        Assert.Equal(0.7, policy.RetreatBelowFor(2));
        Assert.Equal(0.7, policy.RetreatBelowFor(97));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.RetreatBelowFor(-1));
    }

    [Fact]
    public void NoRankMayFlap()
    {
        // The check has to cover the whole table. A return of 0.65 is fine for
        // the rookie and the regular and flaps the veteran, and a policy that
        // only looked at the first entry would ship that.
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepairPolicy(ByRank, returnAbove: 0.65));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepairPolicy([], returnAbove: 0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepairPolicy(ByRank, returnAbove: 0.9, reserve: -1));
    }

    [Fact]
    public void TheScalarPolicyIsTheOneRowTable()
    {
        // The old constructor is not a separate code path, so every test written
        // against it still describes what runs.
        var policy = new RepairPolicy(retreatBelow: 0.4, returnAbove: 0.9);

        Assert.Single(policy.RetreatByRank);
        Assert.Equal(0.4, policy.RetreatByRank[0]);
        Assert.Equal(0.4, policy.RetreatBelowFor(5));
        Assert.Equal(0, policy.Reserve);
    }
}
