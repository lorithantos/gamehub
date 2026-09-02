namespace Nav.Tactics.Tests;

/// <summary>
/// The written-down world, and the one thing in it that is not written down:
/// rank, which is earned by standing where the enemy is.
/// </summary>
/// <remarks>
/// Health here is set by the caller and asserted trivially; what is worth
/// pinning is exposure, because it is the only rule <see cref="DemoWorld"/>
/// runs on its own that a demo cannot see it running. A unit's rank at the end
/// of a replay is a claim about every tick of that replay, and these are the
/// tests that make the claim mean something.
/// </remarks>
public sealed class DemoWorldTests
{
    private const string Room =
        """
        type octile
        height 11
        width 21
        map
        .....................
        .....................
        .....................
        .....................
        .....................
        .....................
        .....................
        .....................
        .....................
        .....................
        .....................
        """;

    /// <summary>Agents standing still at the given cells, so exposure is the only thing moving.</summary>
    private static (MovementSystem System, Grid Grid) Scene(params (int X, int Y)[] at)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        foreach (var (x, y) in at)
        {
            system.AddAgent(grid.Index(x, y));
        }

        return (system, grid);
    }

    private static void Settle(DemoWorld world, MovementSystem system, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            system.Tick();
            world.Settle(system.Agents);
        }
    }

    [Fact]
    public void OnlyTheUnitInReachOfTheEnemyEarnsAnything()
    {
        // Two units that never move and never fight. One is four cells from a
        // hostile and one is sixteen, and after a hundred quiet ticks they are
        // no longer the same unit.
        var (system, grid) = Scene((4, 5), (20, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [60, 160]);
        world.HostileCells.Add(grid.Index(0, 5));

        Settle(world, system, ticks: 100);

        Assert.Equal(100, world.ExposureTicksOf(0));
        Assert.Equal(0, world.ExposureTicksOf(1));
        Assert.Equal(1, world.RankOf(0));
        Assert.Equal(0, world.RankOf(1));
    }

    [Fact]
    public void RankClimbsThroughTheTableAndStops()
    {
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [10, 25]);
        world.HostileCells.Add(grid.Index(0, 5));

        Settle(world, system, ticks: 9);
        Assert.Equal(0, world.RankOf(0));

        Settle(world, system, ticks: 1);
        Assert.Equal(1, world.RankOf(0));

        Settle(world, system, ticks: 15);
        Assert.Equal(2, world.RankOf(0));

        // Past the last threshold there is nowhere left to climb, and the count
        // keeps rising underneath -- so a later table with a third entry would
        // promote this unit without rewriting its history.
        Settle(world, system, ticks: 200);
        Assert.Equal(2, world.RankOf(0));
        Assert.Equal(225, world.ExposureTicksOf(0));
    }

    [Fact]
    public void WhatIsEarnedIsNotLostByLeaving()
    {
        // The unit is exposed, then the enemy is gone. Rank stays. A veteran at
        // a repair pad is still a veteran, which is exactly the case the rank
        // table has to survive: it is consulted while the unit is away.
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [10]);
        world.HostileCells.Add(grid.Index(0, 5));

        Settle(world, system, ticks: 20);
        Assert.Equal(1, world.RankOf(0));

        world.HostileCells.Clear();
        Settle(world, system, ticks: 200);

        Assert.Equal(1, world.RankOf(0));
        Assert.Equal(20, world.ExposureTicksOf(0));
    }

    [Fact]
    public void ExposureIsMeasuredInStepCostsAndTheDiagonalIsNotFree()
    {
        // Octile, which is the same distance the doctrine's own Distance answers
        // -- the two disagreeing would be a quiet way to make a demo
        // unexplainable. So the radius is a budget in step COSTS, not a count of
        // cells, and a diagonal spends root two of it.
        //
        // Unit 0 sits four across and four down from the hostile: five steps of
        // ground, but 5.66 of cost. Unit 1 sits five cells due east: five of
        // both. A radius of five reaches the one further away in cells and not
        // the one nearer, which is the whole point.
        var (system, grid) = Scene((4, 4), (5, 0));
        var near = new DemoWorld(grid, exposureRadius: 5.0);
        near.HostileCells.Add(grid.Index(0, 0));

        Assert.False(near.IsExposed(grid.Index(4, 4)), "a 4x4 diagonal costs 5.66, not 4");
        Assert.True(near.IsExposed(grid.Index(5, 0)), "five due east costs exactly five");

        Settle(near, system, ticks: 1);
        Assert.Equal(0, near.ExposureTicksOf(0));
        Assert.Equal(1, near.ExposureTicksOf(1));

        // Widen it past 5.66 and the diagonal comes inside.
        var wide = new DemoWorld(grid, exposureRadius: 6.0);
        wide.HostileCells.Add(grid.Index(0, 0));
        Assert.True(wide.IsExposed(grid.Index(4, 4)));
    }

    [Fact]
    public void AWorldWithNoEnemyPromotesNobody()
    {
        var (system, grid) = Scene((4, 5), (6, 5));
        var world = new DemoWorld(grid);

        Settle(world, system, ticks: 500);

        Assert.Equal(0, world.RankOf(0));
        Assert.Equal(0, world.RankOf(1));
    }

    [Fact]
    public void StandingExposedCostsHealthAsWellAsEarningRank()
    {
        // The same standing does both, which is the trade the whole doctrine is
        // built on. Unit 0 is in reach and unit 1 is not.
        var (system, grid) = Scene((4, 5), (20, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [10], damagePerTick: 0.01);
        world.HostileCells.Add(grid.Index(0, 5));

        Settle(world, system, ticks: 20);

        Assert.Equal(0.8, world.HealthOf(0), 6);
        Assert.Equal(1.0, world.HealthOf(1), 6);
        Assert.Equal(1, world.RankOf(0));
    }

    [Fact]
    public void AVeteranMendsItselfWhereverItIsStanding()
    {
        // Not on a pad, not near one. Earn the rank under fire, then walk out of
        // reach and recover on your own -- which is what makes a veteran the
        // unit that needs a pad least.
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [10], damagePerTick: 0.02, selfHealPerTick: 0.01);
        world.HostileCells.Add(grid.Index(0, 5));

        Settle(world, system, ticks: 10);
        Assert.True(world.IsFullRank(0), "ten exposed ticks should have topped the one-entry table");

        // Nine ticks bleeding at 0.02 and mending at 0.01, then the tenth
        // promotes it and the eleventh onward is the net -0.01.
        var hurt = world.HealthOf(0);
        Assert.True(hurt < 1.0, "the unit took no damage at all");

        world.HostileCells.Clear();
        Settle(world, system, ticks: 5);

        Assert.Equal(hurt + 0.05, world.HealthOf(0), 6);
    }

    [Fact]
    public void SelfHealingCanBeOverwhelmed()
    {
        // The case Lori named, and it is not a case anybody wrote: the rates are
        // summed, so being overwhelmed is just the sign of the sum. Incoming
        // 0.03 against mending 0.01 is a veteran losing 0.02 a tick while
        // standing exactly where its doctrine wants it.
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [1], damagePerTick: 0.03, selfHealPerTick: 0.01);
        world.HostileCells.Add(grid.Index(0, 5));

        Settle(world, system, ticks: 1);
        Assert.True(world.IsFullRank(0));

        var after = world.HealthOf(0);
        Settle(world, system, ticks: 10);

        Assert.Equal(after - 0.2, world.HealthOf(0), 6);
        Assert.True(world.HealthOf(0) < 0.85, "a veteran under enough fire must still be losing");
    }

    [Fact]
    public void TheArmoryIsFasterRatherThanExclusive()
    {
        // A veteran on a pad gets both rates. If the pad were the only way to
        // heal, a self-healing unit would have to choose between mending and
        // being anywhere useful.
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [1], selfHealPerTick: 0.01, repairPerTick: 0.05);
        world.HostileCells.Add(grid.Index(0, 5));
        world.RepairCells.Add(grid.Index(4, 5));
        world.SetHealth(0, 0.2);

        Settle(world, system, ticks: 1);
        Assert.True(world.IsFullRank(0));

        var after = world.HealthOf(0);
        Settle(world, system, ticks: 10);

        Assert.Equal(after + 0.6, world.HealthOf(0), 6);
    }

    [Fact]
    public void ARookieDoesNotMendItself()
    {
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, exposureRadius: 6.0, rankAt: [500], selfHealPerTick: 0.01);
        world.HostileCells.Add(grid.Index(0, 5));
        world.SetHealth(0, 0.5);

        Settle(world, system, ticks: 50);

        Assert.Equal(0.5, world.HealthOf(0), 6);
        Assert.False(world.IsFullRank(0));
    }

    [Fact]
    public void AWorldWithNoRanksHasNoVeteransToHeal()
    {
        // The guard the empty table needs. Rank 0 would otherwise BE the top
        // rank, and every unit alive would mend itself -- the opposite of what
        // an empty table says.
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid, rankAt: [], selfHealPerTick: 0.01);
        world.SetHealth(0, 0.5);

        Settle(world, system, ticks: 50);

        Assert.False(world.IsFullRank(0));
        Assert.Equal(0.5, world.HealthOf(0), 6);
    }

    [Fact]
    public void TheRatesAreOffUntilAskedFor()
    {
        // Everything above is opt-in, so every demo and test written before the
        // rates existed still describes what runs.
        var (system, grid) = Scene((4, 5));
        var world = new DemoWorld(grid);
        world.HostileCells.Add(grid.Index(4, 5));
        world.SetHealth(0, 0.5);

        Settle(world, system, ticks: 100);

        Assert.Equal(0.0, world.DamagePerTick);
        Assert.Equal(0.0, world.SelfHealPerTick);
        Assert.Equal(0.5, world.HealthOf(0), 6);
        Assert.Equal(100, world.ExposureTicksOf(0));
    }

    [Fact]
    public void TheRankTableMustClimb()
    {
        var grid = Grid.FromMapText(Room);

        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoWorld(grid, rankAt: [60, 60]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoWorld(grid, rankAt: [160, 60]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoWorld(grid, rankAt: [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoWorld(grid, exposureRadius: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoWorld(grid, damagePerTick: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoWorld(grid, selfHealPerTick: -0.01));
        Assert.Throws<ArgumentNullException>(() => new DemoWorld(null!));

        // An empty table is legal and means nobody is ever promoted, which is
        // what a demo about something other than rank wants.
        Assert.Empty(new DemoWorld(grid, rankAt: []).RankAt);
    }
}
