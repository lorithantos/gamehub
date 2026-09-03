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

    /// <summary>One tick of fire on a world where nobody moves: expected health after one settle.</summary>
    private static double AfterOneTick(Combat combat, Kit shooter, Kit victim, double cellsFromCentre = 0.0) =>
        1.0 - (combat.Damage(shooter, victim.Armour, cellsFromCentre) * shooter.ShotsPerSecond * Scale.SecondsPerTick / victim.HitPoints);

    [Fact]
    public void AUnitFiresAtAnEnemyInRangeAndNotBeyond()
    {
        var combat = Shipped();
        var rifleman = combat.KitFor("rifleman");
        var (system, grid) = Scene((5, 5), (8, 5), (15, 5));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, side: 0, kit: "rifleman");
        world.Enlist(1, side: 1, kit: "rifleman");
        world.Enlist(2, side: 1, kit: "rifleman");

        system.Tick();
        world.Settle(system);

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

        var (system, grid) = Scene((5, 5), (5, 8), (9, 5));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, side: 0, kit: "tank");
        world.Enlist(1, side: 1, kit: "buggy");
        world.Enlist(2, side: 1, kit: "rocketbike");
        system.Tick();
        world.Settle(system);

        Assert.True(world.HealthOf(2) < 1.0, "the tank did not shoot the rocket bike");
        Assert.Equal(1.0, world.HealthOf(1), 9);

        var (system2, grid2) = Scene((5, 5), (7, 5), (5, 9));
        var world2 = new DemoWorld(grid2, combat: combat, scale: Scale);
        world2.Enlist(0, side: 0, kit: "rifleman");
        world2.Enlist(1, side: 1, kit: "rocketbike");
        world2.Enlist(2, side: 1, kit: "buggy");
        system2.Tick();
        world2.Settle(system2);

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
        var (system, grid) = Scene((2, 5), (7, 5), (8, 5));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, side: 0, kit: "rocketbike");
        world.Enlist(1, side: 1, kit: "tank");
        world.Enlist(2, side: 0, kit: "rifleman");
        system.Tick();
        world.Settle(system);

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
        var (_, grid) = Scene((5, 5));
        var world = new DemoWorld(grid, combat: combat) { RankPerDamage = 10.0, RankPerKill = 0.0 };
        world.Enlist(0, side: 0, kit: "tank");

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
        var (system, grid) = Scene((5, 5), (6, 5));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, side: 0, kit: "rifleman");
        world.Enlist(1, side: 1, kit: "rifleman");
        world.SetHealth(1, 0.001);

        system.Tick();
        world.Settle(system);

        // Both shots were decided before either landed: the dying man fires.
        Assert.Equal([new Casualty(Victim: 1, Killer: 0)], world.Fallen);
        Assert.Equal(0.0, world.HealthOf(1), 9);
        var wounded = world.HealthOf(0);
        Assert.True(wounded < 1.0, "the falling unit's shot was lost");

        system.Remove(1);
        system.Tick();
        world.Settle(system);

        Assert.Empty(world.Fallen);
        Assert.Equal(wounded, world.HealthOf(0), 9);
        Assert.Empty(world.ViewFor(0).Hostiles);
    }

    [Fact]
    public void MutualKillsAreSimultaneous()
    {
        var combat = Shipped();
        var (system, grid) = Scene((5, 5), (6, 5));
        var world = new DemoWorld(grid, combat: combat, scale: Scale);
        world.Enlist(0, side: 0, kit: "rifleman");
        world.Enlist(1, side: 1, kit: "rifleman");
        world.SetHealth(0, 0.001);
        world.SetHealth(1, 0.001);

        system.Tick();
        world.Settle(system);

        // Listed in the order the shots landed: 0's shot first, so 1 falls
        // first, and 1's shot -- already decided -- still lands.
        Assert.Equal([new Casualty(1, 0), new Casualty(0, 1)], world.Fallen);
    }

    [Fact]
    public void AKitNeedsATableAndAKnownName()
    {
        var combat = Shipped();
        var (_, grid) = Scene((5, 5));

        Assert.Throws<InvalidOperationException>(() => new DemoWorld(grid).Enlist(0, 0, "tank"));
        Assert.Throws<ArgumentException>(() => new DemoWorld(grid, combat: combat).Enlist(0, 0, "trebuchet"));
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
