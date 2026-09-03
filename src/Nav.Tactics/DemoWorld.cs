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
/// Everything it enforces on its own turns on one idea: a unit standing within
/// <see cref="ExposureRadius"/> of a hostile is EXPOSED for that tick. Exposure
/// is counted, and <see cref="RankOf"/> is those counts read against
/// <see cref="RankAt"/>; exposure also costs <see cref="DamagePerTick"/>. So
/// the same standing that teaches a unit is the standing that hurts it, and a
/// demo cannot arrange for one without the other. Against that, two rates give
/// health back: <see cref="RepairPerTick"/> on a repair cell, and
/// <see cref="SelfHealPerTick"/> anywhere at all for a unit at the top of the
/// rank table.
/// </para>
/// <para>
/// The rates are summed and applied once per tick, so <em>overwhelmed</em> is
/// not a case anybody wrote: it is the sign of the sum. A veteran under fire
/// faster than it mends still falls under its doctrine's threshold and still
/// goes to a pad. Damage can still be applied by hand as well -- a demo that
/// wants a single scripted casualty at a chosen tick says so with
/// <see cref="Damage"/>, and with the rates left at zero this class behaves
/// exactly as it did before they existed.
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
    private readonly Dictionary<int, double> _contribution = [];
    private readonly Grid _grid;
    private readonly int[] _rankAt;

    /// <param name="grid">The map the cells are indices into. Needed to measure exposure.</param>
    /// <param name="repairPerTick">How much health one tick on a repair cell restores.</param>
    /// <param name="exposureRadius">Octile distance to a hostile within which a unit counts as exposed.</param>
    /// <param name="rankAt">
    /// Exposed-tick counts at which rank rises, ascending. The default costs a
    /// unit a sustained spell in contact per rank. Empty means rank never rises.
    /// </param>
    /// <param name="damagePerTick">
    /// How much health a unit loses for each tick it stands exposed. Zero, the
    /// default, leaves damage entirely to the caller as it always was.
    /// </param>
    /// <param name="selfHealPerTick">
    /// How much health a unit at the TOP of the rank table recovers each tick,
    /// wherever it is standing. Zero, the default, means nobody heals except on
    /// a repair cell.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A rate or radius is negative, the repair rate or radius is not positive,
    /// or <paramref name="rankAt"/> is not positive and ascending.
    /// </exception>
    public DemoWorld(
        Grid grid,
        double repairPerTick = 0.05,
        double exposureRadius = 6.0,
        IReadOnlyList<int>? rankAt = null,
        double damagePerTick = 0.0,
        double selfHealPerTick = 0.0)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repairPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exposureRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(damagePerTick);
        ArgumentOutOfRangeException.ThrowIfNegative(selfHealPerTick);

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
        DamagePerTick = damagePerTick;
        SelfHealPerTick = selfHealPerTick;
    }

    /// <summary>How much health one tick on a repair cell restores.</summary>
    public double RepairPerTick { get; }

    /// <summary>Octile distance to a hostile within which a unit counts as exposed.</summary>
    /// <remarks>
    /// One radius, two consequences, and that is the point of it: standing here
    /// is what earns rank AND what costs health. A demo cannot arrange for a
    /// unit to learn without risking anything.
    /// </remarks>
    public double ExposureRadius { get; }

    /// <summary>How much health a unit loses for each tick it stands exposed.</summary>
    public double DamagePerTick { get; }

    /// <summary>
    /// How much health a unit at the top of the rank table recovers each tick,
    /// wherever it is standing.
    /// </summary>
    /// <remarks>
    /// A RATE, not an exemption. A veteran under fire faster than this loses
    /// health like anybody else and goes to a pad like anybody else -- being
    /// self-healing makes a unit the one that needs a pad LEAST, never one that
    /// never needs one. It also keeps working on the walk, so a veteran can
    /// cross its doctrine's return threshold before it arrives and turn round;
    /// the rejoin pass has never required arrival and
    /// <c>RepairPolicyTests.AUnitHealedOnTheWayTurnsRoundWithoutReachingThePad</c>
    /// pins that.
    /// </remarks>
    public double SelfHealPerTick { get; }

    /// <summary>Whether this unit is at the top of the rank table and so heals itself.</summary>
    /// <remarks>
    /// A world with no rank thresholds has no veterans, so nobody qualifies --
    /// without that guard an empty table would make rank 0 the top rank and
    /// every unit self-healing, which is the opposite of what an empty table
    /// means.
    /// </remarks>
    public bool IsFullRank(int agent) => _rankAt.Length > 0 && RankOf(agent) >= _rankAt.Length;

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

    /// <summary>Takes health off a unit, never below zero. Nobody gets the credit.</summary>
    /// <remarks>
    /// For a demo staging an injury rather than modelling one. Use
    /// <see cref="DamageBy"/> when an attacker exists; contribution earned this
    /// way goes nowhere, which is right for a scripted wound and wrong for a
    /// fight.
    /// </remarks>
    public void Damage(int agent, double amount) => SetHealth(agent, HealthOf(agent) - amount);

    /// <summary>
    /// Damage with an attacker, which is what makes it count for something.
    /// </summary>
    /// <remarks>
    /// <b>Credit is by last hit.</b> The attacker banks contribution in
    /// proportion to the damage it actually did, and whoever was resolving
    /// damage when the target reached zero takes the kill bonus as well, scaled
    /// by what the victim was worth.
    /// <para>
    /// Exact damage-share credit would need a contributor map per target and
    /// would agree with this in expectation anyway: deal sixty percent of the
    /// damage and land about sixty percent of the killing blows. The per-kill
    /// noise is the price of needing no bookkeeping, and the bias it leaves runs
    /// usefully — a bigger hit is likelier to be the one that crosses zero, so
    /// heavy units earn more than their damage share, which is the veterancy
    /// loop reinforcing itself rather than a leak.
    /// </para>
    /// </remarks>
    /// <param name="target">Who is hit.</param>
    /// <param name="amount">Damage after armour and falloff. Not negative.</param>
    /// <param name="attacker">Who dealt it.</param>
    /// <returns>Contribution the attacker earned, damage and any kill bonus together.</returns>
    public double DamageBy(int target, double amount, int attacker)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var before = HealthOf(target);
        if (before <= 0.0 || amount == 0.0)
        {
            return 0.0;
        }

        // Only damage that LANDED counts. Crediting the swing rather than the
        // wound would pay a unit for overkill, and pay every unit still firing
        // at something already dead.
        var dealt = Math.Min(amount, before);
        SetHealth(target, before - amount);

        var earned = dealt * RankPerDamage;
        if (HealthOf(target) <= 0.0)
        {
            earned += RankPerKill * (1.0 + (0.3 * RankOf(target)));
        }

        _contribution[attacker] = ContributionOf(attacker) + earned;
        return earned;
    }

    /// <summary>Contribution a unit has banked from damage and kills.</summary>
    /// <remarks>
    /// Kept beside exposure rather than replacing it while both rules exist.
    /// Exposure measures PRESENCE, which was only ever defensible because
    /// nothing could deal damage; this measures contribution, which is what rank
    /// is supposed to be for.
    /// </remarks>
    public double ContributionOf(int agent) => _contribution.GetValueOrDefault(agent);

    /// <summary>Contribution earned per point of damage dealt.</summary>
    public double RankPerDamage { get; init; } = 1.0;

    /// <summary>
    /// Contribution for a killing blow, before the victim's rank scales it.
    /// </summary>
    /// <remarks>
    /// Scaled by what was killed, so a veteran is worth more to destroy than a
    /// rookie. Rank then has a value to the enemy as well as to its owner, which
    /// is a second-order incentive that costs one multiply.
    /// </remarks>
    public double RankPerKill { get; init; } = 25.0;

    /// <summary>
    /// One tick of the world happening to the units: exposure credited, then
    /// every rate that touches health applied together. Call it once per tick,
    /// after the world has moved.
    /// </summary>
    /// <remarks>
    /// Exposure is credited AFTER the move, so a unit is judged on where it
    /// ended the tick rather than where it started -- the cell it chose, not the
    /// cell it was leaving.
    /// <para>
    /// The three rates are SUMMED and applied once, which is the whole design.
    /// A veteran standing in the open loses <see cref="DamagePerTick"/> and
    /// regains <see cref="SelfHealPerTick"/>, and whichever is larger decides
    /// what happens to it -- so "overwhelmed" is not a special case anybody had
    /// to write, it is just the sign of the sum. The armory is faster rather
    /// than exclusive: a unit on a pad adds <see cref="RepairPerTick"/> on top
    /// of whatever else it is doing, and a unit healing on the road is the same
    /// arithmetic with one term missing.
    /// </para>
    /// <para>
    /// Nothing stops a demo putting a repair cell inside an enemy's reach. The
    /// unit would then heal, bleed and earn rank all at once, and every one of
    /// those would be a true thing about that map.
    /// </para>
    /// </remarks>
    public void Settle(IReadOnlyList<AgentState> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            var exposed = IsExposed(agent.Cell);
            if (exposed)
            {
                _exposure[agent.Id] = ExposureTicksOf(agent.Id) + 1;
            }

            // Rank is read AFTER this tick's exposure is credited, so the tick
            // that promotes a unit is also the first tick it heals itself.
            var delta = 0.0;
            if (RepairCells.Contains(agent.Cell))
            {
                delta += RepairPerTick;
            }

            if (IsFullRank(agent.Id))
            {
                delta += SelfHealPerTick;
            }

            if (exposed)
            {
                delta -= DamagePerTick;
            }

            if (delta != 0.0)
            {
                SetHealth(agent.Id, HealthOf(agent.Id) + delta);
            }
        }
    }
}
