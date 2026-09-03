using Nav.Core;
using Nav.Core.Models;

namespace Nav.Tactics;

/// <summary>
/// What a shot is worth against what it hits, who gets the credit, and what
/// each kind of unit carries into a fight.
/// </summary>
/// <remarks>
/// A percentage table indexed by weapon and armour, a blast that halves per
/// cell, and a kit per unit type: weapon, armour, reach, rate of fire and hit
/// points.
/// <para>
/// The NUMBERS are ours and live in <c>config/combat.ini</c>. None was copied
/// from anywhere; only the shape is borrowed, and the shape is older than this
/// project by thirty years.
/// </para>
/// <para>
/// <b>Credit goes by last hit.</b> It is a stochastic estimator of damage share
/// — deal sixty percent and you land about sixty percent of the kills — so any
/// one kill is luck and a battle is not.
/// </para>
/// </remarks>
public sealed class Combat
{
    // WHY LAST HIT RATHER THAN EXACT SHARES. Crediting share exactly needs a
    // contributor map per target; crediting whoever landed the fatal blow needs
    // nothing at all, and the two agree in expectation. The residual bias runs
    // the right way: a bigger hit is likelier to be the one that crosses zero,
    // so heavy units collect more killing blows than their damage share alone
    // would give them, which reinforces veterancy rather than distorting it.
    // Measured at 75.5% of kills for 75% of damage -- docs/scale-and-doctrine.md.

    private static readonly string[] WeaponNames = ["rifle", "autocannon", "cannon", "rocket", "flame"];

    private readonly Dictionary<string, Weapon> _weapons;
    private readonly Dictionary<string, Kit> _kits;
    private readonly string[] _armour;

    private Combat(
        string[] armour,
        Dictionary<string, Weapon> weapons,
        Dictionary<string, Kit> kits,
        double falloffPerCell,
        double perDamage,
        double killBonus)
    {
        _armour = armour;
        _weapons = weapons;
        _kits = kits;
        FalloffPerCell = falloffPerCell;
        RankPerDamage = perDamage;
        RankPerKill = killBonus;
    }

    /// <summary>What a cell of distance from the centre multiplies damage by.</summary>
    public double FalloffPerCell { get; }

    /// <summary>Contribution earned per whole unit of health dealt.</summary>
    public double RankPerDamage { get; }

    /// <summary>Contribution earned for landing a fatal blow, before the victim's rank scales it.</summary>
    public double RankPerKill { get; }

    /// <summary>Armour classes, in the order the tables index them.</summary>
    public IReadOnlyList<string> ArmourClasses => _armour;

    /// <summary>Every unit type that can be enlisted, in config order.</summary>
    public IReadOnlyList<string> Units { get; private set; } = [];

    /// <summary>Reads the tables. Every weapon must answer for every armour class, and every unit must be complete.</summary>
    /// <exception cref="ArgumentException">
    /// A weapon's row is the wrong length, or a unit names a weapon or armour
    /// class that does not exist, or lacks a range, a rate or hit points.
    /// </exception>
    public static Combat From(Ini ini)
    {
        ArgumentNullException.ThrowIfNull(ini);

        var armour = Split(ini.Text("armour", "order", "unarmoured"));
        var weapons = new Dictionary<string, Weapon>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in WeaponNames)
        {
            var section = "weapon." + name;
            var row = Split(ini.Text(section, "versus", string.Empty));
            if (row.Length != armour.Length)
            {
                // A short row would read as zero damage against the classes it
                // omits, which is indistinguishable from a balance decision and
                // is the reason this throws rather than padding.
                throw new ArgumentException(
                    $"Weapon '{name}' answers for {row.Length} armour classes; there are {armour.Length}.",
                    nameof(ini));
            }

            weapons[name] = new Weapon(
                name,
                ini.Number(section, "baseDamage", 1.0),
                ini.Int(section, "blastCells", 0),
                [.. row.Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) / 100.0)]);
        }

        var kits = new Dictionary<string, Kit>(StringComparer.OrdinalIgnoreCase);
        var units = Split(ini.Text("units", "names", string.Empty));
        foreach (var unit in units)
        {
            var section = "unit." + unit;
            var weaponName = ini.Text(section, "weapon", string.Empty);
            if (!weapons.TryGetValue(weaponName, out var weapon))
            {
                throw new ArgumentException($"Unit '{unit}' carries weapon '{weaponName}', which is not in the table.", nameof(ini));
            }

            var armourName = ini.Text(section, "armour", string.Empty);
            if (!armour.Contains(armourName, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unit '{unit}' wears armour '{armourName}', which is not a class.", nameof(ini));
            }

            var range = ini.Number(section, "range", 0.0);
            var rate = ini.Number(section, "shotsPerSecond", -1.0);
            var hitPoints = ini.Number(section, "hitPoints", 0.0);
            if (range <= 0.0 || rate < 0.0 || hitPoints <= 0.0)
            {
                // Zero reach or zero health is a unit that cannot fight or
                // cannot die, and a missing key reads exactly like one.
                throw new ArgumentException(
                    $"Unit '{unit}' needs a positive range, a non-negative shotsPerSecond and positive hitPoints.",
                    nameof(ini));
            }

            kits[unit] = new Kit(unit, weapon, armourName, range, rate, hitPoints);
        }

        return new Combat(
            armour,
            weapons,
            kits,
            ini.Number("blast", "falloffPerCell", 0.5),
            ini.Number("rank", "perDamage", 1.0),
            ini.Number("rank", "killBonus", 25.0))
        {
            Units = units,
        };
    }

    /// <summary>The kit a unit type carries.</summary>
    /// <exception cref="ArgumentException">No such unit type.</exception>
    public Kit KitFor(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return _kits.TryGetValue(unit, out var kit)
            ? kit
            : throw new ArgumentException($"No unit type '{unit}'; the config knows {string.Join(", ", Units)}.", nameof(unit));
    }

    /// <summary>
    /// Damage one shot does to one target: the weapon's base, scaled by how it
    /// fares against that armour, then by distance from where it landed.
    /// </summary>
    /// <param name="weapon">Weapon name, as sectioned in the config.</param>
    /// <param name="baseDamage">The weapon's damage before anything modifies it.</param>
    /// <param name="armour">The target's armour class.</param>
    /// <param name="cellsFromCentre">Distance from the blast's centre; zero for a direct hit.</param>
    public double Damage(string weapon, double baseDamage, string armour, double cellsFromCentre)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(armour);
        ArgumentOutOfRangeException.ThrowIfNegative(cellsFromCentre);

        var index = Array.FindIndex(_armour, a => string.Equals(a, armour, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || !_weapons.TryGetValue(weapon, out var row))
        {
            return 0.0;
        }

        return baseDamage * row.Versus[index] * Math.Pow(FalloffPerCell, cellsFromCentre);
    }

    /// <summary>One shot from <paramref name="shooter"/>'s kit against <paramref name="armour"/>, in hit points.</summary>
    public double Damage(Kit shooter, string armour, double cellsFromCentre)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        return Damage(shooter.Weapon.Name, shooter.Weapon.BaseDamage, armour, cellsFromCentre);
    }

    /// <summary>
    /// How fast <paramref name="shooter"/> can hurt something wearing
    /// <paramref name="armour"/>: hit points per second at a direct hit.
    /// </summary>
    /// <remarks>
    /// The measure a unit picks its target by. It asks what the other unit can
    /// do to ME, now — so the same rocket bike is the tank's first target and
    /// the rifleman's last, and neither is wrong.
    /// </remarks>
    public double ThreatPerSecond(Kit shooter, string armour)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        return Damage(shooter, armour, 0.0) * shooter.ShotsPerSecond;
    }

    private static string[] Split(string csv) =>
        csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>A weapon as the table knows it: what it does per shot, how far a hit spreads, and its row.</summary>
/// <param name="Name">The config section's name.</param>
/// <param name="BaseDamage">Hit points per shot against 100% armour at the centre.</param>
/// <param name="BlastCells">Cells from the centre still hit, with falloff. Zero is a single target.</param>
/// <param name="Versus">Fraction of base damage per armour class, in <see cref="Combat.ArmourClasses"/> order.</param>
public sealed record Weapon(string Name, double BaseDamage, int BlastCells, IReadOnlyList<double> Versus);

/// <summary>What a unit type carries into a fight.</summary>
/// <param name="Name">The unit type, as sectioned in the config.</param>
/// <param name="Weapon">What it fires.</param>
/// <param name="Armour">What it wears; a class from the table.</param>
/// <param name="Range">How far it can shoot, in octile step cost.</param>
/// <param name="ShotsPerSecond">Rate of fire. Zero is a unit that cannot shoot.</param>
/// <param name="HitPoints">How much there is to lose. Health on the seam is a fraction of this.</param>
public sealed record Kit(string Name, Weapon Weapon, string Armour, double Range, double ShotsPerSecond, double HitPoints);
