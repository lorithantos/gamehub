namespace Nav.Tactics.Tests;

/// <summary>
/// A world a test writes down and rewrites as it goes: health and rank per unit,
/// hostile cells, repair cells. What the game will feed a squad, without a game.
/// </summary>
/// <remarks>
/// Rank IS settable here, where <see cref="DemoWorld"/> makes it earned. The
/// difference is deliberate: a demo showing a veteran has to show where the
/// veteran came from, while a test of the rank table wants to state the rank and
/// assert on the decision, not play out two hundred ticks of standing near an
/// enemy first.
/// </remarks>
public sealed class ScriptedWorld : IPerception
{
    /// <summary>Health per agent; anyone absent is at full health.</summary>
    public Dictionary<int, double> Health { get; } = [];

    /// <summary>Rank per agent; anyone absent is rank 0.</summary>
    public Dictionary<int, int> Rank { get; } = [];

    /// <summary>Cells hostile units stand on.</summary>
    public List<int> HostileCells { get; } = [];

    /// <summary>Cells where a unit is repaired.</summary>
    public List<int> RepairCells { get; } = [];

    /// <inheritdoc/>
    public double HealthOf(int agent) => Health.TryGetValue(agent, out var h) ? h : 1.0;

    /// <inheritdoc/>
    public int RankOf(int agent) => Rank.GetValueOrDefault(agent);

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles => HostileCells;

    /// <inheritdoc/>
    public IReadOnlyList<int> RepairPoints => RepairCells;
}
