using Nav.Core;
using Nav.Core.Models;

namespace Nav.Tactics;

/// <summary>
/// What a shot is worth against what it hits, and who gets the credit.
/// </summary>
/// <remarks>
/// The shape is the one this genre has used since 1992 — a percentage table
/// indexed by weapon and armour, and a blast that halves per cell — because it
/// is a good shape and there is no prize for inventing a worse one. The NUMBERS
/// are ours and live in <c>config/combat.ini</c>; none was copied from anywhere.
/// <para>
/// <b>Credit is by last hit, and that is a considered choice rather than the
/// lazy one.</b> Crediting damage share exactly needs a contributor map per
/// target; crediting whoever landed the fatal blow needs nothing at all, and is
/// a stochastic estimator of the same quantity — deal sixty percent of the
/// damage and you land about sixty percent of the kills. The two converge over
/// a battle. What per-kill noise buys is a real saving, and the residual bias
/// runs the right way: a bigger hit is likelier to be the one that crosses zero,
/// so heavy units collect more killing blows than their damage share alone
/// would give them, which reinforces veterancy rather than distorting it.
/// </para>
/// </remarks>
public sealed class Combat
{
    private readonly Dictionary<string, double[]> _versus;
    private readonly string[] _armour;

    private Combat(string[] armour, Dictionary<string, double[]> versus, double falloffPerCell, double perDamage, double killBonus)
    {
        _armour = armour;
        _versus = versus;
        FalloffPerCell = falloffPerCell;
        RankPerDamage = perDamage;
        RankPerKill = killBonus;
    }

    /// <summary>What a cell of distance from the centre multiplies damage by.</summary>
    public double FalloffPerCell { get; }

    /// <summary>Contribution earned per point of damage dealt.</summary>
    public double RankPerDamage { get; }

    /// <summary>Contribution earned for landing a fatal blow, before the victim's rank scales it.</summary>
    public double RankPerKill { get; }

    /// <summary>Armour classes, in the order the tables index them.</summary>
    public IReadOnlyList<string> ArmourClasses => _armour;

    /// <summary>Reads the tables. Every weapon must answer for every armour class.</summary>
    /// <exception cref="ArgumentException">A weapon's row is the wrong length.</exception>
    public static Combat From(Ini ini)
    {
        ArgumentNullException.ThrowIfNull(ini);

        var armour = Split(ini.Text("armour", "order", "unarmoured"));
        var versus = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var weapon in new[] { "rifle", "autocannon", "cannon", "rocket", "flame" })
        {
            var row = Split(ini.Text("weapon." + weapon, "versus", string.Empty));
            if (row.Length != armour.Length)
            {
                // A short row would read as zero damage against the classes it
                // omits, which is indistinguishable from a balance decision and
                // is the reason this throws rather than padding.
                throw new ArgumentException(
                    $"Weapon '{weapon}' answers for {row.Length} armour classes; there are {armour.Length}.",
                    nameof(ini));
            }

            versus[weapon] = [.. row.Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) / 100.0)];
        }

        return new Combat(
            armour,
            versus,
            ini.Number("blast", "falloffPerCell", 0.5),
            ini.Number("rank", "perDamage", 1.0),
            ini.Number("rank", "killBonus", 25.0));
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
        if (index < 0 || !_versus.TryGetValue(weapon, out var row))
        {
            return 0.0;
        }

        return baseDamage * row[index] * Math.Pow(FalloffPerCell, cellsFromCentre);
    }

    private static string[] Split(string csv) =>
        csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
