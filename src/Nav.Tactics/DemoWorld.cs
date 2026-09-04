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
/// <b>Rank is earned, not assigned.</b> There is no SetRank, on purpose. Rank
/// climbs a table of contribution points, and contribution comes from one
/// place: damage dealt through <see cref="DamageBy"/>, with a bonus for the
/// killing blow. A viewer can go back through the trace and see every point.
/// </para>
/// <para>
/// Units with a kit shoot each tick at whatever in range can hurt them fastest;
/// see <see cref="Settle"/>. A world without a combat table fires nothing, and
/// rank there is earned only by what a test deals by hand.
/// </para>
/// <para>
/// A unit within <see cref="ExposureRadius"/> of a scripted threat is EXPOSED
/// for that tick: it loses <see cref="DamagePerTick"/>, and the ticks are
/// counted. Exposure never earns rank — it measured presence, which was only
/// ever a stand-in until something could deal damage.
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
/// <b>Sides.</b> Whose unit a thing is belongs to the movement system, given
/// at <see cref="MovementSystem.AddAgent"/>, and this world learns it from the
/// broadcast. <see cref="ViewFor"/> hands each side its own perception: the
/// other sides' living units are its hostiles, alongside any scripted
/// <see cref="HostileCells"/>, which are everybody's enemy. The world itself is
/// side 0's view.
/// </para>
/// </remarks>
public sealed class DemoWorld : IPerception, IPerceptionView
{
    private readonly Dictionary<int, double> _health = [];
    private readonly Dictionary<int, int> _exposure = [];
    private readonly Dictionary<int, double> _contribution = [];
    private readonly Dictionary<int, int> _side = [];
    private readonly SortedDictionary<int, int> _cell = [];
    private readonly Dictionary<int, Kit> _kit = [];
    private readonly List<Casualty> _fallen = [];
    private readonly Dictionary<int, Dictionary<int, Sighting>> _memory = [];
    private readonly Dictionary<int, SortedSet<int>> _visible = [];
    private readonly Dictionary<int, List<int>> _pads = [];
    private readonly Grid _grid;
    private readonly double[] _rankAt;
    private readonly Combat? _combat;
    private readonly ISight _sight;
    private readonly double _secondsPerTick;
    private MovementSystem? _system;
    private bool _stale = true;
    private int _asOf = -1;

    /// <param name="grid">The map the cells are indices into. Needed to measure exposure.</param>
    /// <param name="repairPerTick">How much health one tick on a repair cell restores.</param>
    /// <param name="exposureRadius">Octile distance to a hostile within which a unit counts as exposed.</param>
    /// <param name="rankAt">
    /// Contribution points at which rank rises, ascending. With the shipped
    /// credit rates a solo kill is worth about fifty, so the default asks for
    /// roughly one kill and then three. Empty means rank never rises.
    /// </param>
    /// <param name="damagePerTick">
    /// How much health a unit loses for each tick it stands exposed to a
    /// scripted threat. Zero, the default, leaves damage to fire and to the
    /// caller.
    /// </param>
    /// <param name="selfHealPerTick">
    /// How much health a unit at the TOP of the rank table recovers each tick,
    /// wherever it is standing. Zero, the default, means nobody heals except on
    /// a repair cell.
    /// </param>
    /// <param name="combat">
    /// The damage table and the kits, for a world where units shoot. Null, the
    /// default, is a world where nothing can be enlisted with a kit and nobody
    /// fires.
    /// </param>
    /// <param name="scale">
    /// What a tick is worth in seconds, which turns a kit's rate of fire into
    /// damage per tick. Defaults to <see cref="WorldScale.Default"/>.
    /// </param>
    /// <param name="fog">
    /// Whether a side is limited to what its own units can see. False, the
    /// default, is the omniscient world everything before fog was written
    /// against: every side is told about every unit, and
    /// <see cref="IPerception.Sightings"/> is empty because there is nothing a
    /// side knows that it cannot currently see.
    /// </param>
    /// <param name="sight">
    /// How to decide whether one cell can see another. Defaults to
    /// <see cref="RadiusSight"/> — plain distance, blind to walls. Only consulted
    /// when <paramref name="fog"/> is on.
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
        IReadOnlyList<double>? rankAt = null,
        double damagePerTick = 0.0,
        double selfHealPerTick = 0.0,
        Combat? combat = null,
        WorldScale? scale = null,
        bool fog = false,
        ISight? sight = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repairPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exposureRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(damagePerTick);
        ArgumentOutOfRangeException.ThrowIfNegative(selfHealPerTick);

        _grid = grid;
        _combat = combat;
        _sight = sight ?? new RadiusSight(grid);
        Fog = fog;
        _secondsPerTick = (scale ?? WorldScale.Default).SecondsPerTick;
        _rankAt = [.. rankAt ?? [50.0, 150.0]];
        for (var i = 0; i < _rankAt.Length; i++)
        {
            // Ascending and positive, or RankOf's climb is not a climb: a
            // repeated or falling entry would make two ranks the same rank.
            var floor = i == 0 ? 0.0 : _rankAt[i - 1];
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

    /// <summary>Contribution points at which rank rises, ascending.</summary>
    public IReadOnlyList<double> RankAt => _rankAt;

    /// <summary>
    /// Whether a side is limited to what its own units can see.
    /// </summary>
    /// <remarks>
    /// Off by default, and everything written before fog existed reads the same
    /// with it off — an omniscient world is the special case where the filter
    /// passes everything, not a separate code path.
    /// <para>
    /// With it on, a unit's eyes are its kit's <see cref="Kit.Sight"/>. A unit
    /// nobody enlisted has no kit and so sees NOTHING, which is a defined answer
    /// rather than a default; a fog world refuses to settle with one standing,
    /// because a blind unit is far likelier to be a forgotten
    /// <see cref="Enlist"/> than a deliberate choice.
    /// </para>
    /// </remarks>
    public bool Fog { get; }

    /// <summary>
    /// Cells scripted threats stand on. Mutable: a demo moves them. Hostile to
    /// every side, and never a unit — nothing here has health or a side.
    /// </summary>
    public List<int> HostileCells { get; } = [];

    /// <summary>Cells where a unit is repaired.</summary>
    public List<int> RepairCells { get; } = [];

    /// <summary>
    /// How far a repair pad can see, in octile step cost.
    /// </summary>
    /// <remarks>
    /// <b>A pad is a watcher, not just a destination.</b> Under fog a side that
    /// cannot see a pad cannot plan a retreat to one, so a pad that had no eyes
    /// would be a pad nobody could ever use — and the pad standing on its own
    /// ground is what makes it known. Everything above zero is the disc of map
    /// it lights around itself, which is the ground a hurt unit retreats
    /// THROUGH and the ground an enemy creeping up on the armory is caught on.
    /// <para>
    /// Vision from a pad currently goes to EVERY side, because a pad currently
    /// belongs to nobody: <see cref="RepairCells"/> is a list of ground, and
    /// <see cref="RepairPolicy"/> lets anyone who reaches one use it. When pads
    /// get an owner, this is the number that follows the owner.
    /// </para>
    /// <para>
    /// Small on purpose. A pad is an installation, not a radar, and the point of
    /// the number is to make placing one a decision — a pad in a hollow lights
    /// nothing and a pad on a shoulder watches the approach.
    /// </para>
    /// </remarks>
    public double PadSight { get; init; } = 5.0;

    /// <summary>
    /// Hands a unit a kit. Units nobody enlists have none: they can be shot,
    /// as unarmoured, and never shoot.
    /// </summary>
    /// <param name="agent">Who.</param>
    /// <param name="kit">A unit type from the combat config.</param>
    /// <exception cref="ArgumentOutOfRangeException">A negative id.</exception>
    /// <exception cref="ArgumentException">No such unit type.</exception>
    /// <exception cref="InvalidOperationException">This world has no combat table.</exception>
    public void Enlist(int agent, string kit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(agent);
        ArgumentNullException.ThrowIfNull(kit);

        if (_combat is null)
        {
            throw new InvalidOperationException("This world has no combat table, so nothing can carry a kit.");
        }

        _kit[agent] = _combat.KitFor(kit);
    }

    /// <summary>Which side a unit is on, as the movement system said when it placed it; 0 for one never heard of.</summary>
    public int SideOf(int agent) => _side.GetValueOrDefault(agent);

    /// <summary>What a unit carries, or null for one enlisted without a kit.</summary>
    public Kit? KitOf(int agent) => _kit.GetValueOrDefault(agent);

    /// <summary>
    /// How much there is to lose: the kit's hit points, or one for a unit with
    /// no kit — so for those, damage in hit points IS damage as a fraction, and
    /// everything written before kits existed still reads the same.
    /// </summary>
    public double HitPointsOf(int agent) => KitOf(agent)?.HitPoints ?? 1.0;

    /// <summary>The armour class a shot at this unit is judged against. Unarmoured without a kit.</summary>
    public string ArmourOf(int agent) => KitOf(agent)?.Armour ?? "unarmoured";

    /// <summary>
    /// Who reached zero health since the last <see cref="Settle"/> began, in the
    /// order they fell, with who did it. The layer above decides what to do
    /// about it; typically <see cref="MovementSystem.Remove"/>.
    /// </summary>
    public IReadOnlyList<Casualty> Fallen => _fallen;

    /// <summary>
    /// The world as one side perceives it: health and rank as anybody sees them,
    /// and the OTHER sides' living units as hostiles.
    /// </summary>
    /// <remarks>
    /// Where units stand is what the system has broadcast since
    /// <see cref="Listen"/>. Listen before the first pass, or a doctrine's
    /// first decision is taken blind.
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
    /// <remarks>
    /// What the last edge left, as of <see cref="AsOf"/>. Asking resolves
    /// nothing: the sides looked when the tick ended, and there is no other
    /// moment at which anybody can be looking.
    /// </remarks>
    [Observes]
    public IReadOnlyList<int> HostilesFor(int side) =>
        Fog
            ? _visible.TryGetValue(side, out var seen) ? [.. seen] : []
            : Everything(side);

    /// <summary>
    /// What <paramref name="side"/> knows about enemy units it has seen, by
    /// agent, ascending. Empty without <see cref="Fog"/>, which knows nothing
    /// because it sees everything.
    /// </summary>
    [Observes]
    public IReadOnlyList<Sighting> SightingsFor(int side) =>
        Fog && _memory.TryGetValue(side, out var known) ? ByAgent(known) : [];

    /// <summary>
    /// What a panel or a report holds instead of this world: the perception
    /// questions and the tick they are answered as of, with no verb anywhere on
    /// the type.
    /// </summary>
    /// <remarks>
    /// Itself, the way <c>FieldCache</c> is its own
    /// <see cref="Core.Interfaces.IDistanceFieldView"/>: the narrowing is in the
    /// TYPE the instrument holds, so there is nothing to allocate and nothing
    /// that can drift from what the run is actually using.
    /// </remarks>
    public IPerceptionView View => this;

    /// <inheritdoc/>
    public int AsOf => _asOf;

    /// <inheritdoc/>
    public IReadOnlyList<int> PeekHostiles(int side) => HostilesFor(side);

    /// <inheritdoc/>
    public IReadOnlyList<Sighting> PeekSightings(int side) => SightingsFor(side);

    /// <inheritdoc/>
    /// <remarks>
    /// Copied, where <see cref="RepairPointsFor"/> hands back the list it holds.
    /// An answer that changes after it was given is not a stale answer, it is an
    /// answer to a question nobody asked, and the other two peeks already copy.
    /// </remarks>
    public IReadOnlyList<int> PeekRepairPoints(int side) => [.. RepairPointsFor(side)];

    /// <summary>
    /// Every scripted threat and every other side's living unit, ascending:
    /// what a world without <see cref="Fog"/> tells everybody.
    /// </summary>
    private IReadOnlyList<int> Everything(int side)
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

    /// <summary>One side's memory in agent order, copied out so the caller cannot write to it.</summary>
    private static IReadOnlyList<Sighting> ByAgent(Dictionary<int, Sighting> known)
    {
        var sightings = new List<Sighting>(known.Values);
        sightings.Sort((a, b) => a.Agent.CompareTo(b.Agent));
        return sightings;
    }

    /// <summary>
    /// The tick edge: every side's view of the board brought up to date, and the
    /// whole view stamped with the tick it is now as of.
    /// </summary>
    /// <remarks>
    /// <b>The only state anybody can see is the state at an edge.</b> This runs
    /// at the end of <see cref="Settle"/>, after the shots and the health
    /// arithmetic, because combat and death change what is on the board; and
    /// once from <see cref="Listen"/>, which is the edge the run opens on. A
    /// reader that stops between those has nothing mid-transition to catch,
    /// because there is nothing between them.
    /// <para>
    /// <b>What a side can see changes only when the board changes, and every
    /// board change is broadcast</b> — including the movement of the WATCHER,
    /// which is what discovers a unit that has been standing still all along. So
    /// there is nothing to look at on a tick where nothing happened, and
    /// <see cref="Hear"/> rather than the clock decides whether there is work.
    /// </para>
    /// <para>
    /// The exception is <see cref="HostileCells"/>: a scripted threat is a
    /// position a demo writes directly, with no event behind it, so a world
    /// holding any is re-looked once per <see cref="Settle"/>. A world of real
    /// units alone does no work on a quiet tick.
    /// </para>
    /// <para>
    /// <see cref="AsOf"/> is stamped either way. A tick that moved nothing still
    /// ended, and the last look is still the current answer at that edge —
    /// unchanged is not out of date.
    /// </para>
    /// <para>
    /// A world without <see cref="Fog"/> has no per-side view to bring up to
    /// date: every side is told about every unit, read straight off the board.
    /// So it takes the stamp and nothing else.
    /// </para>
    /// <para>
    /// Each side is computed from the same board and writes only its own
    /// entries, so the loop is a set of independent workers over one snapshot
    /// and nothing about it needs sides to run in order.
    /// </para>
    /// </remarks>
    private void Look()
    {
        if (_system is null)
        {
            return;
        }

        // Before the guards, because the stamp is about the tick ENDING and not
        // about the looking finding anything. A quiet tick and a world with no
        // fog both end, and a view stuck at the last tick something happened
        // would be claiming otherwise.
        _asOf = _system.CurrentTick;
        if (!_stale || !Fog)
        {
            return;
        }

        _stale = false;
        var tick = _asOf;

        var sides = new SortedSet<int>(_side.Values);
        foreach (var side in sides)
        {
            var watchers = new List<(int Cell, double Sight)>();
            foreach (var (agent, cell) in _cell)
            {
                if (SideOf(agent) == side)
                {
                    watchers.Add((cell, KitOf(agent)?.Sight ?? 0.0));
                }
            }

            // The pads watch too, for everybody, because a pad belongs to
            // nobody yet. A side with every unit dead still sees the ground its
            // armory stands on.
            foreach (var pad in RepairCells)
            {
                watchers.Add((pad, PadSight));
            }

            var seen = new SortedSet<int>();
            var known = _memory.TryGetValue(side, out var memory) ? memory : _memory[side] = [];

            // Everybody's enemy, and nobody's memory: a scripted threat has no
            // id to hang a sighting on, so it is here while it is watched and
            // simply gone when it is not.
            foreach (var cell in HostileCells)
            {
                if (Watched(watchers, cell))
                {
                    seen.Add(cell);
                }
            }

            foreach (var (agent, cell) in _cell)
            {
                if (SideOf(agent) != side && Watched(watchers, cell))
                {
                    seen.Add(cell);
                    known[agent] = new Sighting(agent, cell, tick);
                }
            }

            // A sighting nobody refreshed, on ground somebody is looking at, has
            // been refuted: the unit left, or died there and was carried off.
            // Without this every ghost would be permanent, and a side could
            // never learn that it had been baited.
            foreach (var agent in known.Keys.ToList())
            {
                var ghost = known[agent];
                if (ghost.Tick != tick && Watched(watchers, ghost.Cell))
                {
                    known.Remove(agent);
                }
            }

            _visible[side] = seen;

            // A pad is seen if anything watching for this side can see it, its
            // own eyes included — which is why a pad is never lost, and why a
            // pad with no sight at all would be a pad nobody could retreat to.
            var pads = new List<int>();
            foreach (var pad in RepairCells)
            {
                if (Watched(watchers, pad))
                {
                    pads.Add(pad);
                }
            }

            _pads[side] = pads;
        }
    }

    /// <summary>
    /// Refuses a fog world holding a unit that was never enlisted, and so has no
    /// eyes.
    /// </summary>
    /// <remarks>
    /// Sight 0 is a real answer — a unit with no kit sees nothing — but it is
    /// almost never the answer anybody wanted, and a side quietly blinded by a
    /// missing <see cref="Enlist"/> would look exactly like a doctrine that had
    /// stopped working. So it is refused rather than obeyed.
    /// <para>
    /// This is the earliest moment the question can be asked. A unit is PLACED
    /// before it can be enlisted, because enlisting needs the id that placing
    /// hands back, so the check cannot live on the arrival itself.
    /// </para>
    /// </remarks>
    private void RequireEyes()
    {
        foreach (var (agent, _) in _cell)
        {
            if (!_kit.ContainsKey(agent))
            {
                throw new InvalidOperationException(
                    $"Unit {agent} has no kit, so it has no sight, and this world has fog. Enlist it, or turn fog off.");
            }
        }
    }

    /// <summary>Whether any of these watchers can see the cell.</summary>
    private bool Watched(List<(int Cell, double Sight)> watchers, int cell)
    {
        foreach (var (from, range) in watchers)
        {
            if (_sight.CanSee(from, cell, range))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Registers for what the movement system broadcasts -- who was placed
    /// where, who stepped where, who is gone -- and catches up on who is
    /// already standing. Call it once, before the opening pass, so the sides
    /// can see each other at tick 0. Calling it again for the same system
    /// changes nothing.
    /// </summary>
    /// <remarks>
    /// The world's whole knowledge of the board comes through the handler
    /// this registers, and nothing else is read from the system after the
    /// catch-up. That is what makes a limited perception possible later: a
    /// side that is not told an event does not know it.
    /// <para>
    /// The handler writes only this world's own record of where things are.
    /// It never calls back into the system, so it can be told a step
    /// mid-tick without anything being asked of the tick.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Already listening to a different system.</exception>
    public void Listen(MovementSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (ReferenceEquals(_system, system))
        {
            return;
        }

        if (_system is not null)
        {
            throw new InvalidOperationException("This world is already listening to another movement system.");
        }

        _system = system;
        _stale = true;
        system.Happened += Hear;
        foreach (var agent in system.Agents)
        {
            _side[agent.Id] = agent.Side;
            if (agent.Alive)
            {
                _cell[agent.Id] = agent.Cell;
            }
        }

        // THE OPENING EDGE, and the reason this has to be here rather than in
        // the first Settle. Every other look happens at the END of a tick, so
        // the first doctrine pass -- which runs before any tick has ended --
        // would read a view nothing had ever resolved and every side would open
        // the run blind. Catching up on who is standing and looking at them is
        // one act.
        Look();
    }

    private void Hear(MovementEvent e)
    {
        // Anything at all: what a side can see depends as much on where its own
        // units are as on where the enemy's are, so a step by ANYBODY can change
        // somebody's view.
        _stale = true;
        _side[e.Agent] = e.Side;
        switch (e.Kind)
        {
            case MovementEventKind.Added:
            case MovementEventKind.Moved:
                _cell[e.Agent] = e.Cell;
                break;
            case MovementEventKind.Removed:
                _cell.Remove(e.Agent);
                break;
        }
    }

    /// <inheritdoc/>
    public double HealthOf(int agent) => _health.TryGetValue(agent, out var health) ? health : 1.0;

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles => HostilesFor(0);

    /// <inheritdoc/>
    public IReadOnlyList<Sighting> Sightings => SightingsFor(0);

    /// <inheritdoc/>
    public IReadOnlyList<int> RepairPoints => RepairPointsFor(0);

    /// <summary>
    /// Pads <paramref name="side"/> can see, and so can plan to reach. Every pad
    /// without <see cref="Fog"/>.
    /// </summary>
    [Observes]
    public IReadOnlyList<int> RepairPointsFor(int side) =>
        Fog
            ? _pads.TryGetValue(side, out var pads) ? pads : []
            : RepairCells;

    /// <inheritdoc/>
    /// <remarks>
    /// Contribution only climbs, so rank is never lost by leaving the fight: a
    /// veteran at a repair pad is still a veteran, which is the case the rank
    /// table has to survive because it is consulted while the unit is away.
    /// </remarks>
    public int RankOf(int agent)
    {
        var points = ContributionOf(agent);
        var rank = 0;
        while (rank < _rankAt.Length && points >= _rankAt[rank])
        {
            rank++;
        }

        return rank;
    }

    /// <summary>How many ticks this unit has spent exposed to a scripted threat. Never falls.</summary>
    /// <remarks>
    /// A measure of presence, kept because a replay can ask it and because
    /// <see cref="DamagePerTick"/> keys off the same fact. It earns nothing.
    /// </remarks>
    public int ExposureTicksOf(int agent) => _exposure.GetValueOrDefault(agent);

    /// <summary>Whether a unit standing on this cell is within reach of a scripted threat right now.</summary>
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
    /// <param name="amount">
    /// Damage in HIT POINTS, after armour and falloff. Not negative. A unit with
    /// no kit has one hit point, so for it this is a fraction as it always was.
    /// </param>
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

        // Credit is in FRACTIONS of the victim, whatever it weighs: taking a
        // tank from full to dead is worth what taking a rifleman is. Weighting
        // by what the victim cost is the C&C3 rule, and waits on units having a
        // cost.
        var fraction = amount / HitPointsOf(target);

        // Only damage that LANDED counts. Crediting the swing rather than the
        // wound would pay a unit for overkill, and pay every unit still firing
        // at something already dead.
        var dealt = Math.Min(fraction, before);
        SetHealth(target, before - fraction);

        var earned = dealt * RankPerDamage;
        if (HealthOf(target) <= 0.0)
        {
            earned += RankPerKill * (1.0 + (0.3 * RankOf(target)));
            _fallen.Add(new Casualty(target, attacker));
        }

        _contribution[attacker] = ContributionOf(attacker) + earned;
        return earned;
    }

    /// <summary>Contribution a unit has banked from damage and kills: what its rank is read from.</summary>
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
    /// One tick of the world happening to the units: every armed unit's shot
    /// resolved, exposure credited, then every rate that touches health
    /// applied together, for every unit the system has placed and not
    /// removed, and last of all every side looking at what that left. Call it
    /// once per tick, after the system has ticked.
    /// </summary>
    /// <remarks>
    /// <b>This is the tick edge.</b> When it returns, perception has settled:
    /// <see cref="HostilesFor"/>, <see cref="SightingsFor"/> and
    /// <see cref="RepairPointsFor"/> answer for the board as it stands now,
    /// stamped <see cref="AsOf"/>, and none of them resolves anything when
    /// asked. A run that ticks the system without settling has not finished a
    /// tick, and perception stays where the last edge left it.
    /// <para>
    /// <b>Shots are decided before any lands.</b> Every shooter picks its
    /// target from where things stood at the start of the pass, then the shots
    /// resolve in shooter order. Two units that would kill each other both die;
    /// nobody is spared by having a lower id.
    /// </para>
    /// <para>
    /// A shooter takes the highest THREAT in range: the enemy that can hurt it
    /// fastest right now, by the table, not the one with the most to lose.
    /// Ties go to the nearer, then the lower id.
    /// </para>
    /// <para>
    /// A blast hits every unit within the weapon's radius of the target's cell,
    /// friend and foe alike, with falloff; only the shooter is spared its own
    /// shot.
    /// </para>
    /// <para>
    /// Exposure is credited AFTER the move, so a unit is judged on where it
    /// ended the tick rather than where it started -- the cell it chose, not the
    /// cell it was leaving.
    /// </para>
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
    /// <exception cref="InvalidOperationException">Not listening to any system, so there is nothing to settle.</exception>
    public void Settle()
    {
        if (_system is null)
        {
            throw new InvalidOperationException("Listen to a movement system before settling; a world that hears nothing has nothing to settle.");
        }

        if (Fog)
        {
            RequireEyes();

            // A scripted threat moves by a demo writing to the list, with no
            // event behind it, so its position cannot be trusted to be the one
            // that was last looked at. Units alone cost nothing here.
            if (HostileCells.Count > 0)
            {
                _stale = true;
            }
        }

        _fallen.Clear();
        Fire();

        foreach (var (agent, cell) in _cell)
        {
            var exposed = IsExposed(cell);
            if (exposed)
            {
                _exposure[agent] = ExposureTicksOf(agent) + 1;
            }

            // Rank is read AFTER this tick's shots have landed, so the tick
            // that promotes a unit is also the first tick it heals itself.
            var delta = 0.0;
            if (RepairCells.Contains(cell))
            {
                delta += RepairPerTick;
            }

            if (IsFullRank(agent))
            {
                delta += SelfHealPerTick;
            }

            if (exposed)
            {
                delta -= DamagePerTick;
            }

            if (delta != 0.0)
            {
                SetHealth(agent, HealthOf(agent) + delta);
            }
        }

        // Last of all, because the shots and the deaths above changed what is
        // on the board. When this returns the tick is over and every side's
        // view is current, so a reader that stops here is reading an edge
        // rather than provoking one.
        Look();
    }

    /// <summary>Every armed unit shoots once: targets chosen from the start of the pass, shots landed in id order.</summary>
    private void Fire()
    {
        if (_combat is null || _kit.Count == 0)
        {
            return;
        }

        var shots = new List<(int Shooter, int Target)>();
        foreach (var (shooter, cell) in _cell)
        {
            if (KitOf(shooter) is { ShotsPerSecond: > 0.0 } kit && HealthOf(shooter) > 0.0)
            {
                var target = TargetFor(shooter, cell, kit);
                if (target >= 0)
                {
                    shots.Add((shooter, target));
                }
            }
        }

        foreach (var (shooter, target) in shots)
        {
            Land(shooter, target);
        }
    }

    /// <summary>The living enemy in range that can hurt this unit fastest; -1 for none.</summary>
    private int TargetFor(int shooter, int cell, Kit kit)
    {
        var side = SideOf(shooter);
        var x = _grid.ColumnOf(cell);
        var y = _grid.RowOf(cell);

        var best = -1;
        var bestThreat = double.NegativeInfinity;
        var bestDistance = double.PositiveInfinity;

        foreach (var (other, at) in _cell)
        {
            if (SideOf(other) == side || HealthOf(other) <= 0.0)
            {
                continue;
            }

            var distance = Movement.OctileDistance(x, y, _grid.ColumnOf(at), _grid.RowOf(at));
            if (distance > kit.Range)
            {
                continue;
            }

            var threat = KitOf(other) is { } theirs ? _combat!.ThreatPerSecond(theirs, kit.Armour) : 0.0;
            if (threat > bestThreat || (threat == bestThreat && distance < bestDistance))
            {
                best = other;
                bestThreat = threat;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>One tick of fire from a shooter at a target's cell, spread over everyone the blast reaches.</summary>
    private void Land(int shooter, int target)
    {
        var kit = KitOf(shooter)!;
        var centre = _cell[target];
        var cx = _grid.ColumnOf(centre);
        var cy = _grid.RowOf(centre);
        var perShot = kit.ShotsPerSecond * _secondsPerTick;

        foreach (var (victim, at) in _cell)
        {
            if (victim == shooter)
            {
                continue;
            }

            var distance = Movement.OctileDistance(cx, cy, _grid.ColumnOf(at), _grid.RowOf(at));
            if (distance > kit.Weapon.BlastCells)
            {
                continue;
            }

            var amount = _combat!.Damage(kit, ArmourOf(victim), distance) * perShot;
            if (amount > 0.0)
            {
                DamageBy(victim, amount, shooter);
            }
        }
    }

    /// <summary>One side's window on the world. Everything but the enemy list is the world's own answer.</summary>
    private sealed class SideView(DemoWorld world, int side) : IPerception
    {
        public double HealthOf(int agent) => world.HealthOf(agent);

        public int RankOf(int agent) => world.RankOf(agent);

        public IReadOnlyList<int> Hostiles => world.HostilesFor(side);

        public IReadOnlyList<Sighting> Sightings => world.SightingsFor(side);

        public IReadOnlyList<int> RepairPoints => world.RepairPointsFor(side);
    }
}
