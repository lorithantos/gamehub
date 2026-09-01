namespace Nav.Tactics;

/// <summary>
/// A world written down rather than simulated: health per unit, hostile cells,
/// repair cells, all of it settable from outside.
/// </summary>
/// <remarks>
/// What a demo or a test hands a squad in place of a game. It ships in the
/// library rather than in the test project because the demos need it too, and
/// two copies of "the world, faked" would drift.
/// <para>
/// Repair is the one rule it enforces on its own: a unit standing on a repair
/// cell heals a little each time <see cref="Settle"/> is called. Damage is the
/// caller's to apply, because what hurts a unit is the part no movement system
/// can guess.
/// </para>
/// </remarks>
public sealed class DemoWorld : IPerception
{
    private readonly Dictionary<int, double> _health = [];

    /// <param name="repairPerTick">How much health one tick on a repair cell restores.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repairPerTick"/> is not positive.</exception>
    public DemoWorld(double repairPerTick = 0.05)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repairPerTick);
        RepairPerTick = repairPerTick;
    }

    /// <summary>How much health one tick on a repair cell restores.</summary>
    public double RepairPerTick { get; }

    /// <summary>Cells hostile units stand on. Mutable: a demo moves them.</summary>
    public List<int> HostileCells { get; } = [];

    /// <summary>Cells where a unit is repaired.</summary>
    public List<int> RepairCells { get; } = [];

    /// <inheritdoc/>
    public double HealthOf(int agent) => _health.TryGetValue(agent, out var health) ? health : 1.0;

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles => HostileCells;

    /// <inheritdoc/>
    public IReadOnlyList<int> RepairPoints => RepairCells;

    /// <summary>Sets a unit's health, clamped to 0..1.</summary>
    public void SetHealth(int agent, double health) => _health[agent] = Math.Clamp(health, 0.0, 1.0);

    /// <summary>Takes health off a unit, never below zero.</summary>
    public void Damage(int agent, double amount) => SetHealth(agent, HealthOf(agent) - amount);

    /// <summary>
    /// Heals every unit standing on a repair cell by <see cref="RepairPerTick"/>.
    /// Call it once per tick, after the world has moved.
    /// </summary>
    public void Settle(IReadOnlyList<AgentState> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            if (RepairCells.Contains(agent.Cell))
            {
                SetHealth(agent.Id, HealthOf(agent.Id) + RepairPerTick);
            }
        }
    }
}
