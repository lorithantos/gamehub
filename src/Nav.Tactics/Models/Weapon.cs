namespace Nav.Tactics.Models;

/// <summary>A weapon as the table knows it: what it does per shot, how far a hit spreads, and its row.</summary>
/// <param name="Name">The config section's name.</param>
/// <param name="BaseDamage">Hit points per shot against 100% armour at the centre.</param>
/// <param name="BlastCells">Cells from the centre still hit, with falloff. Zero is a single target.</param>
/// <param name="Versus">Fraction of base damage per armour class, in <see cref="Combat.ArmourClasses"/> order.</param>
public sealed record Weapon(string Name, double BaseDamage, int BlastCells, IReadOnlyList<double> Versus);
