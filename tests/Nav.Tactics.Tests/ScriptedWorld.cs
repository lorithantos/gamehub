namespace Nav.Tactics.Tests;

/// <summary>
/// A world a test writes down and rewrites as it goes: health per unit, hostile
/// cells, repair cells. What the game will feed a squad, without a game.
/// </summary>
public sealed class ScriptedWorld : IPerception
{
    /// <summary>Health per agent; anyone absent is at full health.</summary>
    public Dictionary<int, double> Health { get; } = [];

    /// <summary>Cells hostile units stand on.</summary>
    public List<int> HostileCells { get; } = [];

    /// <summary>Cells where a unit is repaired.</summary>
    public List<int> RepairCells { get; } = [];

    /// <inheritdoc/>
    public double HealthOf(int agent) => Health.TryGetValue(agent, out var h) ? h : 1.0;

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles => HostileCells;

    /// <inheritdoc/>
    public IReadOnlyList<int> RepairPoints => RepairCells;
}
