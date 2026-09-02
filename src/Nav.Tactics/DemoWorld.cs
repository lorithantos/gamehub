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
/// It enforces two rules on its own, and both are the kind a movement system
/// cannot guess. A unit standing on a repair cell heals a little each time
/// <see cref="Settle"/> is called. And a unit standing within
/// <see cref="ExposureRadius"/> of a hostile is EXPOSED for that tick, which is
/// counted; <see cref="RankOf"/> is those counts read against
/// <see cref="RankAt"/>. Damage stays the caller's to apply, because what hurts
/// a unit is the part nothing here models.
/// </para>
/// <para>
/// <b>Rank is earned, not assigned.</b> There is no SetRank, on purpose. A demo
/// that could hand out veterans would be showing an arrangement rather than an
/// outcome; here the unit that outranks the others is the one that stood on the
/// hot side of the line and lived, and a viewer can go back through the trace
/// and see it happen. Exposure is proximity only -- no line of sight, no
/// facing, no fire -- because the demo's hostiles do not shoot either.
/// </para>
/// </remarks>
public sealed class DemoWorld : IPerception
{
    private readonly Dictionary<int, double> _health = [];
    private readonly Dictionary<int, int> _exposure = [];
    private readonly Grid _grid;
    private readonly int[] _rankAt;

    /// <param name="grid">The map the cells are indices into. Needed to measure exposure.</param>
    /// <param name="repairPerTick">How much health one tick on a repair cell restores.</param>
    /// <param name="exposureRadius">Octile distance to a hostile within which a unit counts as exposed.</param>
    /// <param name="rankAt">
    /// Exposed-tick counts at which rank rises, ascending. The default costs a
    /// unit a sustained spell in contact per rank. Empty means rank never rises.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A rate or radius is not positive, or <paramref name="rankAt"/> is not positive and ascending.
    /// </exception>
    public DemoWorld(
        Grid grid,
        double repairPerTick = 0.05,
        double exposureRadius = 6.0,
        IReadOnlyList<int>? rankAt = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repairPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exposureRadius);

        _grid = grid;
        _rankAt = [.. rankAt ?? [60, 160]];
        for (var i = 0; i < _rankAt.Length; i++)
        {
            // Ascending and positive, or RankOf's climb is not a climb: a
            // repeated or falling entry would make two ranks the same rank.
            var floor = i == 0 ? 0 : _rankAt[i - 1];
            if (_rankAt[i] <= floor)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rankAt), _rankAt[i], "Rank thresholds must be positive and strictly ascending.");
            }
        }

        RepairPerTick = repairPerTick;
        ExposureRadius = exposureRadius;
    }

    /// <summary>How much health one tick on a repair cell restores.</summary>
    public double RepairPerTick { get; }

    /// <summary>Octile distance to a hostile within which a unit counts as exposed.</summary>
    public double ExposureRadius { get; }

    /// <summary>Exposed-tick counts at which rank rises, ascending.</summary>
    public IReadOnlyList<int> RankAt => _rankAt;

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

    /// <inheritdoc/>
    public int RankOf(int agent)
    {
        var ticks = ExposureTicksOf(agent);
        var rank = 0;
        while (rank < _rankAt.Length && ticks >= _rankAt[rank])
        {
            rank++;
        }

        return rank;
    }

    /// <summary>How many ticks this unit has spent exposed. Never falls.</summary>
    /// <remarks>
    /// Rank is not lost by walking away from the fight, so this only climbs. A
    /// unit at a repair pad simply stops earning, which it does on its own --
    /// the pads are nowhere near the hostiles.
    /// </remarks>
    public int ExposureTicksOf(int agent) => _exposure.GetValueOrDefault(agent);

    /// <summary>Whether a unit standing on this cell is within reach of a hostile right now.</summary>
    public bool IsExposed(int cell)
    {
        var column = _grid.ColumnOf(cell);
        var row = _grid.RowOf(cell);
        foreach (var hostile in HostileCells)
        {
            var distance = Movement.OctileDistance(
                column, row, _grid.ColumnOf(hostile), _grid.RowOf(hostile));
            if (distance <= ExposureRadius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sets a unit's health, clamped to 0..1.</summary>
    public void SetHealth(int agent, double health) => _health[agent] = Math.Clamp(health, 0.0, 1.0);

    /// <summary>Takes health off a unit, never below zero.</summary>
    public void Damage(int agent, double amount) => SetHealth(agent, HealthOf(agent) - amount);

    /// <summary>
    /// Heals every unit standing on a repair cell by <see cref="RepairPerTick"/>,
    /// and credits an exposed tick to every unit standing within reach of a
    /// hostile. Call it once per tick, after the world has moved.
    /// </summary>
    /// <remarks>
    /// Exposure is credited AFTER the move, so a unit is judged on where it
    /// ended the tick rather than where it started -- the cell it chose, not the
    /// cell it was leaving. A unit can be healing and exposed at once if a demo
    /// puts a pad in a bad place; nothing here stops that, and it would be a
    /// true thing about that map.
    /// </remarks>
    public void Settle(IReadOnlyList<AgentState> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            if (RepairCells.Contains(agent.Cell))
            {
                SetHealth(agent.Id, HealthOf(agent.Id) + RepairPerTick);
            }

            if (IsExposed(agent.Cell))
            {
                _exposure[agent.Id] = ExposureTicksOf(agent.Id) + 1;
            }
        }
    }
}
