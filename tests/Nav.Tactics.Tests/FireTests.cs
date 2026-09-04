using Nav.Core;

using Xunit.Abstractions;

namespace Nav.Tactics.Tests;

/// <summary>
/// Weapons that fire: who shoots whom, what a blast reaches, and what a hit
/// point is worth on a seam that only ever sees fractions.
/// </summary>
public sealed class FireTests(ITestOutputHelper output)
{
    private static string ConfigDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config");

    private static Combat Shipped() => Combat.From(Ini.FromFile(Path.Combine(ConfigDir, "combat.ini")));

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

    private static readonly WorldScale Scale = WorldScale.Default;

    private static (MovementSystem System, Grid Grid) Scene(params (int X, int Y, int Side)[] at)
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        foreach (var (x, y, side) in at)
        {
            system.AddAgent(grid.Index(x, y), side);
        }

        return (system, grid);
    }

    /// <summary>One tick of fire on a world where nobody moves: expected health after one settle.</summary>
    private static double AfterOneTick(Combat combat, Kit shooter, Kit victim, double cellsFromCentre = 0.0) =>
        1.0 - (combat.Damage(shooter, victim.Armour, cellsFromCentre) * shooter.ShotsPerSecond * Scale.SecondsPerTick / victim.HitPoints);

    [Fact]
    public void AUnitFiresAtAnEnemyInRangeAndNotBeyond()
    {
        var combat = Shipped();
        var rifleman = combat.KitFor("rifleman");
        var (system, grid) = Scene((5, 5, 0), (8, 5, 1), (15, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "rifleman");
        world.Enlist(1, "rifleman");
        world.Enlist(2, "rifleman");

        system.Tick();
        world.Listen(system);
        world.Settle();

        // Three cells is inside a rifle's reach and ten is not. The two in
        // reach shoot each other in the same tick, so both are hurt alike.
        var expected = AfterOneTick(combat, rifleman, rifleman);
        Assert.True(expected < 1.0, "the config gave a rifle no bite at all");
        Assert.Equal(expected, world.HealthOf(0), 9);
        Assert.Equal(expected, world.HealthOf(1), 9);
        Assert.Equal(1.0, world.HealthOf(2), 9);
    }

    [Fact]
    public void ThreatIsJudgedByWhatItCanDoToMe()
    {
        // The same two enemies, a buggy and a rocket bike, and two observers.
        // To a tank the rocket is the danger and the autocannon a nuisance, so
        // the tank shoots the bike even with the buggy nearer. To a rifleman
        // the autocannon is the faster killer, so the rifleman shoots the
        // buggy even with the bike nearer. Neither is wrong.
        //
        // The non-target sits off the axis of everybody else's fire, so the
        // only way it can be hurt is by being chosen.
        var combat = Shipped();
        var buggy = combat.KitFor("buggy");
        var rocketbike = combat.KitFor("rocketbike");

        output.WriteLine(
            $"to a tank: buggy {combat.ThreatPerSecond(buggy, "plated"):F1}/s, "
                + $"rocket bike {combat.ThreatPerSecond(rocketbike, "plated"):F1}/s; "
                + $"to a rifleman: buggy {combat.ThreatPerSecond(buggy, "unarmoured"):F1}/s, "
                + $"rocket bike {combat.ThreatPerSecond(rocketbike, "unarmoured"):F1}/s");

        var (system, grid) = Scene((5, 5, 0), (5, 8, 1), (9, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "tank");
        world.Enlist(1, "buggy");
        world.Enlist(2, "rocketbike");
        system.Tick();
        world.Listen(system);
        world.Settle();

        Assert.True(world.HealthOf(2) < 1.0, "the tank did not shoot the rocket bike");
        Assert.Equal(1.0, world.HealthOf(1), 9);

        var (system2, grid2) = Scene((5, 5, 0), (7, 5, 1), (5, 9, 1));
        var world2 = new DemoWorld(grid2, combat: combat, scale: Scale);
        world2.Enlist(0, "rifleman");
        world2.Enlist(1, "rocketbike");
        world2.Enlist(2, "buggy");
        system2.Tick();
        world2.Listen(system2);
        world2.Settle();

        Assert.True(world2.HealthOf(2) < 1.0, "the rifleman did not shoot the buggy");
        Assert.Equal(1.0, world2.HealthOf(1), 9);
    }

    [Fact]
    public void ABlastHitsEveryoneInRadiusIncludingYourOwnSide()
    {
        // A rocket bike fires at a tank five cells off. A friendly rifleman
        // standing one cell past the tank is inside the rocket's two-cell
        // blast and takes half of what a direct hit would do to it.
        var combat = Shipped();
        var rocketbike = combat.KitFor("rocketbike");
        var rifleman = combat.KitFor("rifleman");
        var (system, grid) = Scene((2, 5, 0), (7, 5, 1), (8, 5, 0));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "rocketbike");
        world.Enlist(1, "tank");
        world.Enlist(2, "rifleman");
        system.Tick();
        world.Listen(system);
        world.Settle();

        // The tank shoots back at the rocket bike, not the rifleman beside it,
        // so the rifleman's only wound is friendly.
        var expected = AfterOneTick(combat, rocketbike, rifleman, cellsFromCentre: 1.0);
        Assert.True(expected < 1.0);
        Assert.Equal(expected, world.HealthOf(2), 9);
        Assert.True(world.HealthOf(1) < 1.0, "the rocket bike did not hit the tank");
        Assert.True(world.HealthOf(0) < 1.0, "the tank did not shoot back");
    }

    [Fact]
    public void DamageIsInHitPointsAndHealthStaysAFraction()
    {
        var combat = Shipped();
        var (_, grid) = Scene((5, 5, 0));
        var world = new DemoWorld(grid, combat: combat) { RankPerDamage = 10.0, RankPerKill = 0.0 };
        world.Enlist(0, "tank");

        var tank = combat.KitFor("tank");
        var earned = world.DamageBy(target: 0, amount: tank.HitPoints / 10.0, attacker: 7);

        Assert.Equal(0.9, world.HealthOf(0), 9);
        Assert.Equal(1.0, earned, 9);
        Assert.Equal(1.0, world.HitPointsOf(9), 9);
        Assert.Equal("unarmoured", world.ArmourOf(9));
    }

    [Fact]
    public void TheFallenAreReportedAndTheDeadStopFiring()
    {
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (6, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "rifleman");
        world.Enlist(1, "rifleman");
        world.SetHealth(1, 0.001);

        system.Tick();
        world.Listen(system);
        world.Settle();

        // Both shots were decided before either landed: the dying man fires.
        Assert.Equal([new Casualty(Victim: 1, Killer: 0)], world.Fallen);
        Assert.Equal(0.0, world.HealthOf(1), 9);
        var wounded = world.HealthOf(0);
        Assert.True(wounded < 1.0, "the falling unit's shot was lost");

        system.Remove(1);
        system.Tick();
        world.Listen(system);
        world.Settle();

        Assert.Empty(world.Fallen);
        Assert.Equal(wounded, world.HealthOf(0), 9);
        Assert.Empty(world.ViewFor(0).Hostiles);
    }

    [Fact]
    public void MutualKillsAreSimultaneous()
    {
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (6, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "rifleman");
        world.Enlist(1, "rifleman");
        world.SetHealth(0, 0.001);
        world.SetHealth(1, 0.001);

        system.Tick();
        world.Listen(system);
        world.Settle();

        // Listed in the order the shots landed: 0's shot first, so 1 falls
        // first, and 1's shot -- already decided -- still lands.
        Assert.Equal([new Casualty(1, 0), new Casualty(0, 1)], world.Fallen);
    }

    [Fact]
    public void TheRetainedTargetIsTheOneTheRuleChose()
    {
        // The same scene as ThreatIsJudgedByWhatItCanDoToMe, asked the other
        // question: not who was hurt, but who the world says was being shot at.
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (5, 8, 1), (9, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "tank");
        world.Enlist(1, "buggy");
        world.Enlist(2, "rocketbike");

        system.Tick();
        world.Listen(system);
        world.Settle();

        // The rule read off the table rather than named: the enemy that can hurt
        // THIS shooter fastest. The buggy is nearer, so a retained value that
        // had drifted to distance or to id would answer 1 here.
        var plated = combat.KitFor("tank").Armour;
        var expected = new[] { 1, 2 }
            .OrderByDescending(enemy => combat.ThreatPerSecond(world.KitOf(enemy)!, plated))
            .First();

        output.WriteLine($"the tank's threat table says {expected}, and it holds {world.TargetOf(0)}");

        Assert.Equal(expected, world.TargetOf(0));
        Assert.Equal(2, expected);

        // Tied to the shot that was actually fired: the named unit is the one
        // that took the damage, and the other is untouched.
        Assert.True(world.HealthOf(expected) < 1.0, "the retained target is not the unit that was hit");
        Assert.Equal(1.0, world.HealthOf(1), 9);

        // And both of theirs, which have only one enemy to choose from.
        Assert.Equal(0, world.TargetOf(1));
        Assert.Equal(0, world.TargetOf(2));
    }

    [Fact]
    public void AUnitThatFiredAtNobodyThisTickHasNoTarget()
    {
        // Two riflemen close enough to shoot and one standing ten cells off.
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (8, 5, 1), (15, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "rifleman");
        world.Enlist(1, "rifleman");
        world.Enlist(2, "rifleman");

        system.Tick();
        world.Listen(system);
        world.Settle();

        Assert.Equal(1, world.TargetOf(0));
        Assert.Equal(0, world.TargetOf(1));
        Assert.Equal(-1, world.TargetOf(2));

        // A unit nobody has ever heard of never fired either.
        Assert.Equal(-1, world.TargetOf(9));

        // The target goes away, and with it the answer. A per-tick fact kept
        // from the last tick something happened would still say 1 here, which
        // is the exact lie a watching panel would draw.
        system.Remove(1);
        system.Tick();
        world.Settle();

        Assert.Equal(-1, world.TargetOf(0));
    }

    [Fact]
    public void ATargetThatFellToTheShotIsNotReportedAsATarget()
    {
        // A tank reaches six cells and a rifleman four, so the shooting is one
        // way: the tank kills, nothing shoots back, and the shooter is plainly
        // alive when the question is asked.
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (10, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "tank");
        world.Enlist(1, "rifleman");
        world.SetHealth(1, 0.001);

        system.Tick();
        world.Listen(system);
        world.Settle();

        Assert.Equal([new Casualty(Victim: 1, Killer: 0)], world.Fallen);
        Assert.Equal(0.0, world.HealthOf(1), 9);
        Assert.Equal(1.0, world.HealthOf(0), 9);

        Assert.Equal(-1, world.TargetOf(0));
    }

    [Fact]
    public void AShooterThatFellThisTickIsNotShootingAtAnything()
    {
        // The shot lands and the shooter dies of something else: a scripted
        // threat one cell away, on a radius tight enough that the rifleman five
        // cells further out is not standing in it. So the target survives, and
        // the only reason the answer changes is that the shooter is a corpse.
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (10, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale, exposureRadius: 1.0, damagePerTick: 0.5);
        world.Enlist(0, "tank");
        world.Enlist(1, "rifleman");
        world.SetHealth(0, 0.1);
        world.HostileCells.Add(grid.Index(4, 5));

        system.Tick();
        world.Listen(system);
        world.Settle();

        // Fired, and the shot landed: the rifleman is wounded by a tank that no
        // longer exists.
        Assert.True(world.HealthOf(1) < 1.0, "the dying tank's shot was lost");
        Assert.Equal(0.0, world.HealthOf(0), 9);

        Assert.Equal(-1, world.TargetOf(0));
    }

    [Fact]
    public void NobodyIsShootingAtAnythingBeforeTheFirstSettle()
    {
        // The same shape as AsOf being -1 until an edge has happened. These two
        // are in range of each other and will both have a target one line later.
        var combat = Shipped();
        var (system, grid) = Scene((5, 5, 0), (8, 5, 1));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, "rifleman");
        world.Enlist(1, "rifleman");

        system.Tick();
        world.Listen(system);

        Assert.Equal(-1, world.TargetOf(0));
        Assert.Equal(-1, world.TargetOf(1));

        world.Settle();

        Assert.Equal(1, world.TargetOf(0));
        Assert.Equal(0, world.TargetOf(1));
    }

    [Fact]
    public void AKitNeedsATableAndAKnownName()
    {
        var combat = Shipped();
        var (_, grid) = Scene((5, 5, 0));

        Assert.Throws<InvalidOperationException>(() => new DemoWorld(grid).Enlist(0, "tank"));
        Assert.Throws<ArgumentException>(() => new DemoWorld(grid, combat: combat).Enlist(0, "trebuchet"));
        Assert.Throws<ArgumentException>(() => combat.KitFor("trebuchet"));
    }

    [Fact]
    public void TheShippedUnitsAreCompleteAndDefaultNothing()
    {
        var ini = Ini.FromFile(Path.Combine(ConfigDir, "combat.ini"));
        var combat = Combat.From(ini);

        Assert.Equal(["rifleman", "buggy", "tank", "rocketbike"], combat.Units);
        Assert.DoesNotContain(ini.Defaulted, key => key.StartsWith("unit.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ini.Defaulted, key => key.StartsWith("weapon.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnIncompleteUnitIsRefused()
    {
        var ini = Ini.Parse(
            "[armour]\norder = a\n[weapon.rifle]\nversus = 100\n[weapon.autocannon]\nversus = 100\n"
                + "[weapon.cannon]\nversus = 100\n[weapon.rocket]\nversus = 100\n[weapon.flame]\nversus = 100\n"
                + "[units]\nnames = ghost\n[unit.ghost]\nweapon = rifle\narmour = a\nrange = 3\nshotsPerSecond = 1\n");

        Assert.Throws<ArgumentException>(() => Combat.From(ini));
    }

    [Fact]
    public void TimeToKillForTheRecord()
    {
        // Not a rule, a table: seconds for each kit to kill each other kit at
        // a direct hit. Printed so the numbers can be argued with; asserted
        // only where the table's shape would otherwise be lost.
        var combat = Shipped();
        var kits = combat.Units.Select(combat.KitFor).ToArray();

        output.WriteLine("seconds to kill, shooter down the side, target across");
        output.WriteLine($"{string.Empty,-12}{string.Join(string.Empty, kits.Select(k => $"{k.Name,12}"))}");
        var seconds = new Dictionary<(string, string), double>();
        foreach (var shooter in kits)
        {
            var row = string.Empty;
            foreach (var target in kits)
            {
                var perSecond = combat.ThreatPerSecond(shooter, target.Armour);
                var s = perSecond > 0 ? target.HitPoints / perSecond : double.PositiveInfinity;
                seconds[(shooter.Name, target.Name)] = s;
                row += $"{s,12:F1}";
            }

            output.WriteLine($"{shooter.Name,-12}{row}");
        }

        Assert.True(seconds[("rifleman", "tank")] > 5 * seconds[("tank", "rifleman")], "a rifle should be much slower against a tank than the reverse");
        Assert.True(seconds[("rocketbike", "tank")] < seconds[("tank", "tank")], "the rocket should beat the cannon against plate");
    }
}
