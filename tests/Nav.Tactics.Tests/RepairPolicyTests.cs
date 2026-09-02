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
}
