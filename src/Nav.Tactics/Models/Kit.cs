namespace Nav.Tactics.Models;

/// <summary>What a unit type carries into a fight.</summary>
/// <param name="Name">The unit type, as sectioned in the config.</param>
/// <param name="Weapon">What it fires.</param>
/// <param name="Armour">What it wears; a class from the table.</param>
/// <param name="Range">How far it can shoot, in octile step cost.</param>
/// <param name="Sight">
/// How far it can SEE, in the same measure. Separate from <paramref name="Range"/>
/// on purpose: a unit that sees further than it shoots is a scout, and one that
/// shoots further than it sees needs somebody else to spot for it. Neither is
/// expressible if sight is derived from reach.
/// </param>
/// <param name="ShotsPerSecond">Rate of fire. Zero is a unit that cannot shoot.</param>
/// <param name="HitPoints">How much there is to lose. Health on the seam is a fraction of this.</param>
public sealed record Kit(
    string Name,
    Weapon Weapon,
    string Armour,
    double Range,
    double Sight,
    double ShotsPerSecond,
    double HitPoints);
