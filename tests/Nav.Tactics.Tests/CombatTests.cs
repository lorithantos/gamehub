using Nav.Core;

using Xunit.Abstractions;

namespace Nav.Tactics.Tests;

/// <summary>
/// The damage table, and the attribution rule underneath rank.
/// </summary>
public sealed class CombatTests(ITestOutputHelper output)
{
    private static string ConfigDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config");

    private static Combat Shipped() => Combat.From(Ini.FromFile(Path.Combine(ConfigDir, "combat.ini")));

    private static Grid Room() => Grid.FromMapText(
        "type octile\nheight 11\nwidth 11\nmap\n" + string.Join('\n', Enumerable.Repeat(new string('.', 11), 11)));

    [Fact]
    public void TheTableIsAHardCounterRatherThanAStatTotal()
    {
        // The point of a weapon-versus-armour matrix: composition beats
        // arithmetic. If every weapon ranked the armour classes the same way,
        // the table would be a damage multiplier wearing a costume.
        var combat = Shipped();

        var rifleVsInfantry = combat.Damage("rifle", 1.0, "unarmoured", 0);
        var rifleVsPlate = combat.Damage("rifle", 1.0, "plated", 0);
        var cannonVsInfantry = combat.Damage("cannon", 1.0, "unarmoured", 0);
        var cannonVsPlate = combat.Damage("cannon", 1.0, "plated", 0);

        output.WriteLine(
            $"rifle: {rifleVsInfantry:F2} vs infantry, {rifleVsPlate:F2} vs plate; "
                + $"cannon: {cannonVsInfantry:F2} vs infantry, {cannonVsPlate:F2} vs plate");

        Assert.True(rifleVsInfantry > rifleVsPlate, "a rifle should be worse against armour");
        Assert.True(cannonVsPlate > cannonVsInfantry, "a cannon should be worse against infantry");
    }

    [Fact]
    public void BlastFallsOffAndIsWorthHalfACellOut()
    {
        var combat = Shipped();

        var centre = combat.Damage("rocket", 4.0, "plated", 0);
        var oneOut = combat.Damage("rocket", 4.0, "plated", 1);
        var twoOut = combat.Damage("rocket", 4.0, "plated", 2);

        Assert.Equal(centre * 0.5, oneOut, 9);
        Assert.Equal(centre * 0.25, twoOut, 9);
    }

    [Fact]
    public void DamageAndKillsAreComparableRatherThanOneDrowningTheOther()
    {
        // Health is a 0..1 fraction, so killing a unit outright is ONE point of
        // damage. If the kill bonus dwarfs that, "rank measures contribution"
        // silently means "rank measures kills" -- which is the model this
        // replaced. It did, at first: perDamage 1 against killBonus 25.
        var combat = Shipped();
        var soloKill = (1.0 * combat.RankPerDamage) + combat.RankPerKill;
        var fromDamage = 1.0 * combat.RankPerDamage / soloKill;

        output.WriteLine(
            $"a solo kill earns {soloKill:F0}, of which {fromDamage * 100:F0}% is the damage itself");

        Assert.InRange(fromDamage, 0.3, 0.7);
    }

    [Fact]
    public void AnUnknownWeaponOrArmourDoesNothingRatherThanGuessing()
    {
        var combat = Shipped();

        Assert.Equal(0.0, combat.Damage("trebuchet", 10.0, "plated", 0));
        Assert.Equal(0.0, combat.Damage("rifle", 10.0, "aetherial", 0));
    }

    [Fact]
    public void AShortRowIsRefusedRatherThanPaddedWithZeroes()
    {
        // Padding would read as "this weapon does nothing to structures", which
        // is indistinguishable from a balance decision.
        var ini = Ini.Parse(
            "[armour]\norder = a, b, c\n[weapon.rifle]\nversus = 100, 50\n");

        Assert.Throws<ArgumentException>(() => Combat.From(ini));
    }

    [Fact]
    public void OnlyDamageThatLandedEarnsAnything()
    {
        // Crediting the swing rather than the wound pays for overkill, and pays
        // everybody still shooting at something already dead.
        var world = new DemoWorld(Room()) { RankPerDamage = 1.0, RankPerKill = 0.0 };
        world.SetHealth(1, 0.30);

        var earned = world.DamageBy(target: 1, amount: 5.0, attacker: 7);

        Assert.Equal(0.30, earned, 9);
        Assert.Equal(0.0, world.HealthOf(1), 9);
        Assert.Equal(0.0, world.DamageBy(target: 1, amount: 5.0, attacker: 8), 9);
    }

    [Fact]
    public void TheKillBonusGoesToWhoeverWasResolvingDamageAtDeath()
    {
        var world = new DemoWorld(Room()) { RankPerDamage = 1.0, RankPerKill = 25.0 };
        world.SetHealth(1, 1.0);

        world.DamageBy(target: 1, amount: 0.9, attacker: 7);
        world.DamageBy(target: 1, amount: 0.1, attacker: 8);

        output.WriteLine(
            $"unit 7 did 90% of the damage and earned {world.ContributionOf(7):F2}; "
                + $"unit 8 landed the blow and earned {world.ContributionOf(8):F2}");

        Assert.Equal(0.9, world.ContributionOf(7), 9);
        Assert.True(world.ContributionOf(8) > 25.0, "the killer did not get the bonus");
    }

    [Fact]
    public void AVeteranIsWorthMoreToKillThanARookie()
    {
        // Rank has a value to the ENEMY as well as to its owner, which is a
        // second-order incentive costing one multiply: a veteran is a better
        // target, so protecting one is a real decision rather than sentiment.
        var grid = Room();
        var world = new DemoWorld(grid, rankAt: [5])
        {
            RankPerKill = 10.0,
            RankPerDamage = 0.0,
        };

        // Kill a rookie first, before it has done anything.
        world.SetHealth(0, 0.5);
        world.DamageBy(0, 0.5, attacker: 7);
        var forARookie = world.ContributionOf(7);

        // Now let the same unit earn rank with a kill of its own, and kill it again.
        world.SetHealth(5, 0.5);
        world.DamageBy(5, 0.5, attacker: 0);

        Assert.True(world.RankOf(0) > 0, "the target never ranked up, so the test proves nothing");

        world.SetHealth(0, 0.5);
        world.DamageBy(0, 0.5, attacker: 8);
        var forAVeteran = world.ContributionOf(8);

        output.WriteLine(
            $"killing a rank-0 unit is worth {forARookie:F1}; "
                + $"killing a rank-{world.RankOf(0)} unit is worth {forAVeteran:F1}");

        Assert.True(forAVeteran > forARookie, "rank made no difference to what the victim was worth");
    }

    [Fact]
    public void LastHitIsAnUnbiasedEstimatorOfDamageShare()
    {
        // The claim the whole rule rests on, measured rather than asserted. Two
        // units alternate equal-sized hits into a stream of targets; over many
        // kills the killing blows should split in proportion to damage dealt,
        // even though any single kill is luck.
        //
        // This is what makes last-hit cheap AND fair: exact share accounting
        // needs a contributor map per target and agrees with this in
        // expectation anyway.
        var world = new DemoWorld(Room()) { RankPerDamage = 0.0, RankPerKill = 1.0 };

        const int Targets = 400;
        var seed = 12345u;
        uint Next()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return seed;
        }

        for (var t = 0; t < Targets; t++)
        {
            var id = 1000 + t;
            world.SetHealth(id, 1.0);

            // Unit 7 deals three quarters of the damage, unit 8 a quarter,
            // in randomly ordered small bites.
            while (world.HealthOf(id) > 0)
            {
                var attacker = Next() % 4 == 0 ? 8 : 7;
                world.DamageBy(id, 0.1, attacker);
            }
        }

        var sevens = world.ContributionOf(7);
        var eights = world.ContributionOf(8);
        var share = sevens / (sevens + eights);

        output.WriteLine(
            $"unit 7 dealt ~75% of damage and took {share * 100:F1}% of {Targets} kills");

        Assert.InRange(share, 0.65, 0.85);
    }
}
