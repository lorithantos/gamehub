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
/// Everything it enforces turns on one idea: a unit within
/// <see cref="ExposureRadius"/> of a hostile is EXPOSED for that tick.
/// </para>
/// <para>
/// Exposure both earns rank and costs <see cref="DamagePerTick"/>, so the
/// standing that teaches a unit is the standing that hurts it — a demo cannot
/// arrange one without the other.
/// </para>
/// <para>
/// Two rates give health back: <see cref="RepairPerTick"/> on a repair cell, and
/// <see cref="SelfHealPerTick"/> anywhere, for a unit at the top of the table.
/// </para>
/// <para>
/// The rates are summed and applied once per tick, so <em>overwhelmed</em> is
/// not a case anybody wrote — it is the sign of the sum.
/// </para>
/// <para>
/// <b>Rank is earned, not assigned.</b> There is no SetRank, on purpose: the
/// unit that outranks the others is the one that stood on the hot side of the
/// line and lived, and a viewer can go back through the trace and see it happen.
/// </para>
/// <para>
/// Exposure is proximity only — no line of sight, no facing, no fire — because
/// the demo's hostiles do not shoot either.
/// </para>
/// <para>
/// <b>Sides.</b> Every unit is on a side, 0 unless <see cref="Enlist"/> says
/// otherwise, and <see cref="ViewFor"/> hands each side its own perception: the
/// other sides' living units are its hostiles, alongside any scripted
/// <see cref="HostileCells"/>, which are everybody's enemy. The world itself is
/// side 0's view.
/// </para>
/// </remarks>
public sealed class DemoWorld : IPerception
{
    private readonly Dictionary<int, double> _health = [];
    private readonly Dictionary<int, int> _exposure = [];
    private readonly Dictionary<int, double> _contribution = [];
    private readonly Dictionary<int, int> _side = [];
    private readonly Dictionary<int, int> _cell = [];
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
    /// A RATE, not an exemption. Self-healing makes a unit the one that needs a
    /// pad LEAST, never one that never needs one: under fire faster than this it
    /// loses health and goes to a pad like anybody else.
    /// <para>
    /// It keeps working on the walk, so a veteran can cross its return threshold
    /// before arriving and turn round; the rejoin pass has never required arrival
    /// and
    /// <c>RepairPolicyTests.AUnitHealedOnTheWayTurnsRoundWithoutReachingThePad</c>
    /// pins that.
    /// </para>
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

    /// <summary>
    /// Cells scripted threats stand on. Mutable: a demo moves them. Hostile to
    /// every side, and never a unit — nothing here has health or a side.
    /// </summary>
    public List<int> HostileCells { get; } = [];

    /// <summary>Cells where a unit is repaired.</summary>
    public List<int> RepairCells { get; } = [];

    /// <summary>Puts a unit on a side. Units nobody enlists are on side 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A negative id or side.</exception>
    public void Enlist(int agent, int side)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(agent);
        ArgumentOutOfRangeException.ThrowIfNegative(side);
        _side[agent] = side;
    }

    /// <summary>Which side a unit is on; 0 for one never enlisted.</summary>
    public int SideOf(int agent) => _side.GetValueOrDefault(agent);

    /// <summary>
    /// The world as one side perceives it: health and rank as anybody sees them,
    /// and the OTHER sides' living units as hostiles.
    /// </summary>
    /// <remarks>
    /// Where units stand is what the last <see cref="Settle"/> or
    /// <see cref="Observe"/> recorded. Prime it with <see cref="Observe"/> before
    /// the first pass, or a doctrine's first decision is taken blind.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A negative side.</exception>
    public IPerception ViewFor(int side)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(side);
        return side == 0 ? this : new SideView(this, side);
    }

    /// <summary>
    /// Cells hostile to <paramref name="side"/>: every scripted threat, and every
    /// living unit on another side. Ascending, without repeats.
    /// </summary>
    public IReadOnlyList<int> HostilesFor(int side)
    {
        var cells = new SortedSet<int>(HostileCells);
        foreach (var (agent, cell) in _cell)
        {
            if (SideOf(agent) != side)
            {
                cells.Add(cell);
            }
        }

        return [.. cells];
    }

    /// <summary>
    /// Records where the living stand, and that the dead no longer do, without
    /// touching health or rank. <see cref="Settle"/> does this first; call it
    /// alone before the opening pass so the sides can see each other at tick 0.
    /// </summary>
    public void Observe(IReadOnlyList<AgentState> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            if (agent.Alive)
            {
                _cell[agent.Id] = agent.Cell;
            }
            else
            {
                _cell.Remove(agent.Id);
            }
        }
    }

    /// <inheritdoc/>
    public double HealthOf(int agent) => _health.TryGetValue(agent, out var health) ? health : 1.0;

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles => HostilesFor(0);

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
    /// Exact damage-share credit needs a contributor map per target and agrees
    /// with this in expectation anyway: deal sixty percent of the damage, land
    /// about sixty percent of the killing blows.
    /// </para>
    /// <para>
    /// The bias it leaves runs usefully. A bigger hit is likelier to be the one
    /// that crosses zero, so heavy units earn more than their damage share —
    /// veterancy reinforcing itself rather than a leak.
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
    /// The three rates are SUMMED and applied once, which is the whole design. A
    /// veteran in the open loses <see cref="DamagePerTick"/> and regains
    /// <see cref="SelfHealPerTick"/>; whichever is larger decides.
    /// </para>
    /// <para>
    /// So "overwhelmed" is not a special case anybody wrote — it is the sign of
    /// the sum.
    /// </para>
    /// <para>
    /// The armory is faster rather than exclusive: a unit on a pad adds
    /// <see cref="RepairPerTick"/> on top, and one healing on the road is the
    /// same arithmetic with a term missing.
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

        Observe(agents);

        foreach (var agent in agents)
        {
            if (!agent.Alive)
            {
                continue;
            }

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

    /// <summary>One side's window on the world. Everything but the enemy list is the world's own answer.</summary>
    private sealed class SideView(DemoWorld world, int side) : IPerception
    {
        public double HealthOf(int agent) => world.HealthOf(agent);

        public int RankOf(int agent) => world.RankOf(agent);

        public IReadOnlyList<int> Hostiles => world.HostilesFor(side);

        public IReadOnlyList<int> RepairPoints => world.RepairPoints;
    }
}
