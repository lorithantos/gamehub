namespace Nav.Core;

/// <summary>
/// Agents, their orders, and a tick whose cost has a ceiling.
/// </summary>
/// <remarks>
/// Planning is queued and budgeted, never done inline on an order. A hundred units
/// told to move is a hundred searches, and doing them where the order arrives puts
/// all of that in one frame.
/// <para>
/// New searches start longest-waiting first, tie-broken by id; searches already in
/// flight are continued in id order. What matters is that both orders are total and
/// FIXED -- that is what makes a tick reproducible, which every acceptance criterion
/// downstream depends on. Plain id order was tried and starves the tail: under a
/// budget too small to finish anything, the first agents take every slot forever.
/// </para>
/// <para>
/// <b>An agent whose search is still running holds position and stays reserved.</b>
/// It is thinking, and a thinking unit is still standing somewhere. Its search is
/// anchored a few ticks ahead so that the plan, when it lands, starts in the
/// future rather than in a past the reservation window has already dropped.
/// </para>
/// </remarks>
public sealed class MovementSystem
{
    /// <summary>
    /// How far ahead a search is anchored to begin with, in ticks.
    /// </summary>
    /// <remarks>
    /// A search that spans ticks finishes after the window has moved, and a plan
    /// anchored at the tick the search STARTED would then begin in the past —
    /// which the reservation table rejects outright. Anchoring ahead buys the
    /// search this many ticks to finish in; overrunning it costs a discard, which
    /// is counted rather than hidden.
    /// <para>
    /// It DOUBLES on each discard, and that is not a refinement. A fixed latency
    /// livelocks on a tight budget: the search cannot finish in four ticks, so it
    /// is abandoned, restarted, and abandoned again forever — a system that looks
    /// busy and never moves. Measured on a fifty-node budget, where nothing
    /// completed at all until the latency could grow.
    /// </para>
    /// </remarks>
    private const int InitialPlanningLatency = 4;

    /// <summary>
    /// How long a stalled agent waits before a timer-driven retry. Long,
    /// deliberately: retries are EVENT-driven now — an arrival, an order, a
    /// reconciliation — and the timer is only the backstop that turns a missed
    /// wake into slow instead of frozen. What a short timer cost:
    /// <c>docs/search-and-movement.md</c>.
    /// </summary>
    private const int StallBackstopTicks = 64;

    /// <summary>
    /// Ticks a SLOTTED group member may stand blocked, descending toward its
    /// slot, before it is allowed a search instead. Greedy descent cannot get
    /// round a parked fellow; the space-time search can.
    /// </summary>
    /// <remarks>
    /// The search is what unsticks a big crust, and every tick of waiting for it
    /// is paid by two hundred units at once; too eager and the detours it plans
    /// round a packed rim read as retreats. Twelve is where both hold. The swept
    /// table is in <c>docs/search-and-movement.md</c>; re-run the settling report
    /// before moving it.
    /// </remarks>
    private const int FollowBlockedTicks = 12;

    internal sealed class Agent(int id, int cell)
    {
        /// <summary>
        /// Consecutive ticks this member has stood still because no step down
        /// its field was free. Reset by any step, and by a committed search.
        /// </summary>
        public int BlockedTicks { get; set; }

        public int Id { get; } = id;

        public int Cell { get; set; } = cell;

        public int Goal { get; set; } = cell;

        public PlanResult? Plan { get; set; }

        public BudgetedSearch? Search { get; set; }

        public SearchWorkspace? Workspace { get; set; }

        /// <summary>The tick the in-flight search's plan will start at.</summary>
        public int AnchorTick { get; set; }

        /// <summary>Ticks of slack this agent's next search gets. Grows on a discard.</summary>
        public int Latency { get; set; } = InitialPlanningLatency;

        public int StalledTicks { get; set; }

        /// <summary>Do not start another search before this tick.</summary>
        public int RetryAfterTick { get; set; }

        /// <summary>When this agent last got a planning slot; -1 if never.</summary>
        public int LastPlanAttemptTick { get; set; } = -1;

        /// <summary>When a nearby vacancy last woke this agent.</summary>
        public int LastVacancyWakeTick { get; set; } = -1_000_000;

        public bool WantsPlan { get; set; }

        /// <summary>
        /// The order's destination, which keys the shared distance field. The
        /// assigned <see cref="Goal"/> is per-agent; the field is per-order, so
        /// a whole group shares one. -1 until the first order.
        /// </summary>
        public int FieldKey { get; set; } = -1;

        /// <summary>The order this agent last belonged to, or null.</summary>
        public Group? Group { get; set; }

        /// <summary>
        /// Where this agent is going on its own, or -1 when it is with its group.
        /// An errand overrides <see cref="Goal"/> and <see cref="FieldKey"/> and
        /// nothing else: the agent stays in <see cref="Group"/>, and the seam
        /// stops listing it as on station until it is recalled or re-ordered.
        /// </summary>
        public int Errand { get; set; } = -1;

        /// <summary>
        /// True once this agent holds a concrete parking slot. A group member
        /// starts WITHOUT one — it walks toward the shared destination and
        /// claims a slot on approach, the way a person walks toward a gathering
        /// and takes the nearest open spot once they are near.
        /// </summary>
        public bool HasSlot { get; set; } = true;
    }

    /// <summary>
    /// One order's members, for reconciliation and the leader. Assignment is a
    /// snapshot but settling is a process; the group is what reconciles the two.
    /// </summary>
    internal sealed class Group
    {
        public required int Destination { get; init; }

        public required List<Agent> Members { get; init; }

        /// <summary>
        /// The parking ring: nearest cells to the destination, innermost first.
        /// Regrown when a unit joins, so it is always sized to the membership.
        /// </summary>
        public required IReadOnlyList<int> Slots { get; set; }

        /// <summary>How this group moves. Holds its own state; deterministic.</summary>
        public required GroupDoctrine Doctrine { get; init; }

        public int Leader { get; set; } = -1;
    }

    private readonly Grid _grid;
    private readonly ReservationTable _table;
    private readonly List<Agent> _agents = [];
    private readonly Stack<SearchWorkspace> _workspacePool = new();
    private readonly int _nodeBudgetPerTick;
    private readonly int _maxSearchesInFlight;
    private readonly IDistanceFieldSource _fields;
    private readonly List<Group> _groups = [];

    // Per-tick occupancy caches, rebuilt at the top of Tick and kept current by
    // GroupOps mutations, so doctrines read O(1) answers instead of scanning.
    // Claimed goals map cell to holder, so "is it claimed" and "by whom" are one
    // lookup over one set rather than two answers over different scopes.
    private readonly Dictionary<int, int> _claimedGoals = [];
    private readonly HashSet<int> _settledCells = [];
    private readonly HashSet<int> _occupiedCells = [];

    private readonly IChokepointSource _chokepointSource;

    // Null in production. When set, every workspace this system creates gets a
    // frontier whose exact-tie ordering is drawn from a seed derived from this
    // one and the workspace's creation index -- derived with a fixed arithmetic
    // mix on purpose, because HashCode.Combine is randomised per process and
    // would make the "same seed" a different ordering on every run.
    private readonly int? _tieBreakSeed;
    private int _workspacesCreated;
    private IReadOnlyList<Chokepoint>? _chokepoints;

    /// <summary>
    /// The map's chokepoints, asked for once and held for the system's life.
    /// </summary>
    /// <remarks>
    /// Cached HERE rather than in the source, so a source stays a pure answer and
    /// an expensive one is never asked twice. The grid is taken to be static, which
    /// is the same assumption the distance fields rest on.
    /// </remarks>
    internal IReadOnlyList<Chokepoint> MapChokepoints => _chokepoints ??= _chokepointSource.For(_grid);

    /// <param name="grid">
    /// The map every agent moves over. Held, not copied, and taken to be static --
    /// the distance fields cached behind it are never invalidated.
    /// </param>
    /// <param name="horizon">
    /// Ticks of future the reservation table holds. It also bounds planning
    /// latency: a search may be anchored at most half a horizon ahead, because an
    /// anchor at the far edge leaves no room for a plan behind it.
    /// </param>
    /// <param name="nodeBudgetPerTick">
    /// Search nodes a tick may spend across every agent. The ceiling criterion 9
    /// measures.
    /// </param>
    /// <param name="maxSearchesInFlight">
    /// How many searches may be suspended at once. Capped because a suspended
    /// search OWNS its workspace — the frontier and state arrays live there
    /// between calls — so this is a memory bound as much as a scheduling one.
    /// </param>
    /// <param name="fields">
    /// Where distance fields come from. Defaults to a <see cref="FieldCache"/> of
    /// <see cref="FieldCapacity"/> built over <paramref name="grid"/>, which is
    /// what a match wants. Supply one to share a source across systems, to hand in
    /// fields precomputed at load, or to wrap the default in something that counts
    /// -- the capacity is a guess until somebody measures it.
    /// <para>
    /// Whatever is passed must be deterministic in the sense
    /// <see cref="IDistanceFieldSource"/> describes, or replay stops being a test.
    /// </para>
    /// </param>
    /// <param name="chokepoints">
    /// Where the map's gates come from. Defaults to <see cref="ChokepointScan"/>,
    /// which finds them by sampling. Supply <see cref="NoChokepoints"/> to switch
    /// metering off structurally, or a precomputed list for a shipped map whose
    /// gates never change.
    /// </param>
    /// <param name="tieBreakSeed">
    /// Makes every search pop a different but fixed one of its equally good
    /// frontier entries, so a run can be checked against orderings the
    /// production heap never chooses. Null, the default, is the production
    /// ordering. A collision that appears under one seed and not another is a
    /// real defect, because every path is still optimal and collision-freedom
    /// must hold for every valid tie-break.
    /// </param>
    public MovementSystem(
        Grid grid,
        int horizon = 32,
        int nodeBudgetPerTick = 4000,
        int maxSearchesInFlight = 8,
        IDistanceFieldSource? fields = null,
        IChokepointSource? chokepoints = null,
        int? tieBreakSeed = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeBudgetPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSearchesInFlight);

        _grid = grid;
        _table = new ReservationTable(grid.CellCount, horizon);
        _nodeBudgetPerTick = nodeBudgetPerTick;
        _maxSearchesInFlight = maxSearchesInFlight;
        _fields = fields ?? new FieldCache(grid, FieldCapacity);
        _chokepointSource = chokepoints ?? new ChokepointScan();
        _tieBreakSeed = tieBreakSeed;
    }

    /// <summary>
    /// A workspace for the pool. Each one gets its own derived seed so two
    /// workspaces do not draw identical tie-break sequences, and the derivation
    /// is plain arithmetic so it is the same on every run.
    /// </summary>
    private SearchWorkspace NewWorkspace()
    {
        var index = _workspacesCreated++;
        return new SearchWorkspace(
            tieBreakSeed: _tieBreakSeed is { } seed ? unchecked((seed * 397) ^ (index * 7919)) : null);
    }

    /// <summary>
    /// Ticks elapsed, starting at zero and advanced once per <see cref="Tick"/> --
    /// the clock plans, reservations and every retry gate are indexed against, so
    /// nothing in this system means anything except relative to it.
    /// </summary>
    public int CurrentTick { get; private set; }

    /// <summary>
    /// The map this system moves over: the one it was built with, held and never
    /// copied. Exposed so a layer above can measure distances on the same ground
    /// without being handed the grid twice.
    /// </summary>
    public Grid Grid => _grid;

    /// <summary>Nodes expanded across every plan ever made. A cost measure.</summary>
    public long TotalExpanded { get; private set; }

    /// <summary>What the last <see cref="Tick"/> cost.</summary>
    public TickReport LastTick { get; private set; }

    /// <summary>
    /// A fresh snapshot of every agent in id order -- values copied out, not handles
    /// in, so a caller may hold it across a <see cref="Tick"/> and still be reading
    /// the tick it asked about.
    /// </summary>
    public IReadOnlyList<AgentState> Agents =>
        [.. _agents.Select(a => new AgentState(
            a.Id, a.Cell, a.Goal, a.Cell == a.Goal, a.StalledTicks, a.Search is not null,
            Waiting: a.Cell != a.Goal && a.Search is null && a.RetryAfterTick > CurrentTick,
            Errand: a.Errand))];

    /// <summary>Each live group's leader, for display and diagnostics.</summary>
    public IReadOnlyList<int> Leaders =>
        [.. _groups.Where(g => g.Leader >= 0).Select(g => g.Leader)];

    /// <summary>Distance fields currently cached, of <see cref="FieldCapacity"/>.</summary>
    public int LiveFields => _fields.Count;

    /// <summary>
    /// How many destinations may hold a live distance field before the coldest is
    /// dropped. Scaled to the handful of orders a match runs at once, never to the
    /// unit count -- that independence is the reason fields are keyed by destination.
    /// </summary>
    public const int FieldCapacity = 8;

    /// <summary>
    /// Places an agent and returns its id: consecutive from zero, stable for life,
    /// and the deterministic tiebreak everything else in this system falls back on.
    /// The cell is reserved from <see cref="CurrentTick"/> immediately, so a search
    /// that runs before this agent has ever moved already routes around it.
    /// </summary>
    /// <param name="cell">Where it stands. Must be passable.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cell"/> is impassable.</exception>
    public int AddAgent(int cell)
    {
        if (!_grid.IsPassable(cell))
        {
            throw new ArgumentOutOfRangeException(nameof(cell), cell, "An agent cannot stand on an impassable cell.");
        }

        var agent = new Agent(_agents.Count, cell);
        _agents.Add(agent);

        // It is standing here, and everyone planning after this must see that.
        _table.Reserve([cell], CurrentTick, agent.Id);
        return agent.Id;
    }

    /// <summary>
    /// Sends a group to one place, spreading the destination so each agent has its
    /// own cell to stand on.
    /// </summary>
    /// <remarks>
    /// Queues the planning; it does not do it. That is the difference between a
    /// hundred-unit order costing one frame and costing however long a hundred
    /// searches take.
    /// </remarks>
    public void Order(IReadOnlyList<int> agents, int goalCell) => Order(agents, goalCell, doctrine: null);

    private HashSet<int>? _doorways;

    /// <summary>
    /// Every chokepoint cell and every passable cell beside one: the cells a
    /// group keeps clear of, for parking and for settling alike. Built once.
    /// </summary>
    private HashSet<int> Doorways
    {
        get
        {
            if (_doorways is not null)
            {
                return _doorways;
            }

            var doorways = new HashSet<int>();
            foreach (var choke in MapChokepoints)
            {
                doorways.Add(choke.Cell);
                var x = _grid.ColumnOf(choke.Cell);
                var y = _grid.RowOf(choke.Cell);
                foreach (var step in Movement.Steps)
                {
                    if (_grid.IsPassable(x + step.DeltaX, y + step.DeltaY))
                    {
                        doorways.Add(((y + step.DeltaY) * _grid.Width) + x + step.DeltaX);
                    }
                }
            }

            return _doorways = doorways;
        }
    }

    /// <summary>
    /// The parking ring for <paramref name="count"/> units at <paramref name="destination"/>,
    /// ordered so the rim fills before the middle. Empty if the
    /// destination is impassable. Shared by an order and by a unit joining a
    /// formation later, so a ring is always sized to the membership it serves.
    /// </summary>
    /// <remarks>
    /// A GROUP's ring keeps doorways clear: no slot on or beside a chokepoint.
    /// The gap fixture taught this the hard way -- the ring included the gap's
    /// inner mouth, an early claimer parked in the doorway, and the chamber
    /// sealed with the rest of the group outside. A single unit is exempt:
    /// ordering one unit ONTO a doorway is intent.
    /// <para>
    /// <b>It fills across, from the far rim to the near one.</b> Handing out the
    /// middle first plugs the cell everything has to pass through: the leader
    /// parks in it and every unit behind walks around the outside. The patrol
    /// showed it -- a unit one step from its post spent five ticks going the
    /// long way round whoever took the centre. Starting at the rim FURTHEST
    /// from the arriving group and sweeping across means each unit walks into
    /// open ground, and the last cell filled is the one nearest the units still
    /// coming, so nobody crosses the formation to reach a place in it.
    /// </para>
    /// <para>
    /// Filling outward would march a squad to a distant edge for no reason, and
    /// the ring's size is what stops it: exactly one cell per member, so the rim
    /// is one shell out for three units and widens only as the group does.
    /// </para>
    /// </remarks>
    /// <param name="destination">The cell the order was aimed at.</param>
    /// <param name="count">How many units the ring must seat.</param>
    /// <param name="fromCells">
    /// Where the members stand, which gives the sweep its direction. Empty, or a
    /// group already standing on the destination, leaves no axis to sweep along
    /// and the ring falls back to the rim outward-in.
    /// </param>
    private IReadOnlyList<int> RingFor(int destination, int count, IReadOnlyList<int> fromCells)
    {
        Func<int, bool>? keepDoorwaysClear = null;
        if (count > 1 && Doorways.Count > 0)
        {
            keepDoorwaysClear = Doorways.Contains;
        }

        var candidates = GoalSpread.Nearest(_grid, destination, count, keepDoorwaysClear);

        // THE DOORWAY RULE YIELDS TO THE ORDER. Two cases, and both refused the
        // order silently before this: a destination that IS a doorway -- a
        // squad sent to hold an entryway, which is the whole point of the
        // order -- and a corridor, which is doorways end to end, where keeping
        // clear of every one leaves nothing to seat anyone on. Either way the
        // player clicked there and meant it; and the exclusion, left to run,
        // would seat the group in the nearest room instead, turning "go here"
        // into "go somewhere near here". A single unit was already exempt on
        // the same reasoning.
        if (keepDoorwaysClear is not null &&
            (keepDoorwaysClear(destination) || candidates.Count < count))
        {
            candidates = GoalSpread.Nearest(_grid, destination, count);
        }

        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var destinationX = _grid.ColumnOf(destination);
        var destinationY = _grid.RowOf(destination);

        double Radius(int cell) => Movement.OctileDistance(
            _grid.ColumnOf(cell), _grid.RowOf(cell), destinationX, destinationY);

        // The axis the group is arriving along, pointing the way they are
        // walking: from where they stand toward the destination.
        var axisX = 0.0;
        var axisY = 0.0;
        if (fromCells.Count > 0)
        {
            axisX = destinationX - fromCells.Average(c => (double)_grid.ColumnOf(c));
            axisY = destinationY - fromCells.Average(c => (double)_grid.RowOf(c));
        }

        var length = Math.Sqrt((axisX * axisX) + (axisY * axisY));
        if (length < 1e-9)
        {
            // Already on top of the destination: no direction to sweep along,
            // so fall back to the rim and work inward.
            return [.. candidates.OrderByDescending(Radius).ThenBy(cell => cell)];
        }

        axisX /= length;
        axisY /= length;

        // How far along that axis a cell lies. The far rim scores highest, the
        // near rim lowest, and the middle falls between -- so taking them in
        // this order sweeps across the circle rather than out from its centre.
        double Along(int cell) =>
            ((_grid.ColumnOf(cell) - destinationX) * axisX) +
            ((_grid.RowOf(cell) - destinationY) * axisY);

        return [.. candidates.OrderByDescending(Along).ThenByDescending(Radius).ThenBy(cell => cell)];
    }

    /// <param name="agents">
    /// Who to send. The sequence's own order does not matter -- members are taken in
    /// ascending id, so the same order issued twice assigns the same slots.
    /// </param>
    /// <param name="goalCell">
    /// Where to go. An impassable cell SNAPS to the nearest passable one rather than
    /// being refused: a click on a wall means the ground beside it.
    /// </param>
    /// <param name="doctrine">
    /// How this group should move. Defaults to <see cref="GatherDoctrine"/> -- the
    /// scrum -- by measurement rather than by taste: on the gap fixture the pacing
    /// brake cost four times the arrival time and more nodes than free contention.
    /// Pass <see cref="MeteredGatherDoctrine"/> to buy a visibly ordered column and
    /// pay for it.
    /// </param>
    public void Order(IReadOnlyList<int> agents, int goalCell, GroupDoctrine? doctrine)
    {
        ArgumentNullException.ThrowIfNull(agents);

        // A click on a wall MEANS the ground beside it. Refuse-don't-repair is
        // the right rule for file formats and the wrong one for player input:
        // an order onto impassable terrain used to be silently swallowed, and
        // fourteen selected units stood at spawn for nine hundred sixty ticks
        // looking healthy while the player wondered what was wrong. Snap to
        // the nearest passable cell, exactly as every RTS does.
        if (!_grid.IsPassable(goalCell))
        {
            var clickX = _grid.ColumnOf(goalCell);
            var clickY = _grid.RowOf(goalCell);
            var best = -1;
            var bestDistance = double.PositiveInfinity;
            for (var cell = 0; cell < _grid.CellCount; cell++)
            {
                if (!_grid.IsPassable(cell))
                {
                    continue;
                }

                var distance = Movement.OctileDistance(
                    clickX, clickY, _grid.ColumnOf(cell), _grid.RowOf(cell));
                if (distance < bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
            }

            if (best < 0)
            {
                return;   // a map of nothing but walls; there is nowhere to go
            }

            goalCell = best;
        }

        // EVERY ID IS RESOLVED BEFORE ANYTHING IS TOUCHED, and that ordering is the
        // whole of this guard. Resolving inside the loop meant a bad id threw
        // halfway: the agents before it had already been re-goaled and moved onto
        // the new group, the new group was never added to _groups, and an emptied
        // old group was never pruned. ElectLeader then ran over that empty group on
        // the next tick and every tick after -- so one unknown id made a caught
        // exception into a MovementSystem that could not tick again.
        var members = new Agent[agents.Count];
        var at = 0;
        foreach (var id in agents.OrderBy(id => id))
        {
            if (id < 0 || id >= _agents.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(agents), id, $"no such agent; this system has {_agents.Count}.");
            }

            members[at++] = _agents[id];
        }

        // The parking ring doubles as the passability check: an impassable
        // destination yields no ring and the order is refused as before.
        var slots = RingFor(goalCell, agents.Count, [.. members.Select(m => m.Cell)]);
        if (slots.Count == 0)
        {
            return;
        }

        // The default is the SCRUM, by measurement, not the meter: on the gap
        // fixture the pacing brake cost 4x the arrival time and more nodes
        // than free contention -- reservation contention through a doorway,
        // with event-driven stalls and fill-like-water claiming, already IS a
        // well-behaved queue. MeteredGatherDoctrine remains available for
        // callers that want a visibly ordered column and will pay for it.
        var group = new Group
        {
            Destination = goalCell,
            Members = [],
            Slots = slots,
            Doctrine = doctrine ?? new GatherDoctrine(),
        };
        foreach (var agent in members)
        {
            // A group member is NOT handed a parking slot here. It walks toward
            // the shared destination and claims the innermost open slot once it
            // gets NEAR -- the way a real team fills in on arrival rather than
            // pre-booking spots from across the map. A single-unit order is its
            // own claim: this unit, that cell, immediately.
            // THE DESTINATION, not the ring's first cell. They were the same
            // thing while the ring began at its centre, and stopped being when
            // it began at its rim -- at which point a whole group set off
            // toward a cell on the edge of its own formation, and keyed its
            // shared distance field there too.
            agent.Goal = goalCell;
            agent.HasSlot = agents.Count == 1;
            agent.FieldKey = goalCell;
            agent.Errand = -1;   // an order ends an errand like any other goal

            agent.StalledTicks = 0;
            agent.RetryAfterTick = 0;
            agent.WantsPlan = true;
            Abandon(agent);

            agent.Group?.Members.Remove(agent);
            agent.Group = group;
            group.Members.Add(agent);
        }

        _groups.RemoveAll(g => g.Members.Count == 0);
        _groups.Add(group);

        // A member of a group of two or more follows the group's field from
        // the next tick; whatever it was walking is dropped here so it does
        // not finish an old route first. After the membership is complete,
        // because that is what decides who follows.
        foreach (var agent in members)
        {
            if (IsFollower(agent))
            {
                Stand(agent);
            }
        }

        ElectLeader(group);
    }

    /// <summary>
    /// Sends one agent away on an errand of its own, to <paramref name="destination"/>
    /// with its own field, while its place in the formation it was ordered with is
    /// kept for it. The formation lists it as away rather than on station until
    /// <see cref="Recall(int)"/> or a new order.
    /// </summary>
    /// <remarks>
    /// Not a movement doctrine's verb, on purpose. Whether a unit should leave its
    /// formation -- to retreat for repair, to scout -- is a decision about
    /// membership, made above this layer, and the formation only reports it. The
    /// errand itself is exactly what a single-unit order is, a goal and a field
    /// key; the difference is that nothing here forgets where the unit belongs.
    /// </remarks>
    /// <param name="agent">
    /// Who. Must have been ordered at least once: an errand is a departure from
    /// somewhere, and an agent that was never ordered has nowhere to be recalled to.
    /// </param>
    /// <param name="destination">Where to. Must be passable; it is not snapped.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No such agent, or <paramref name="destination"/> is off the map or impassable.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="agent"/> has never been ordered and so has no formation.
    /// </exception>
    public void Dispatch(int agent, int destination)
    {
        var unit = Resolve(agent);
        if (unit.Group is null)
        {
            throw new InvalidOperationException(
                $"agent {agent} has never been ordered and has no formation to be recalled to.");
        }

        if (!_grid.IsPassable(destination))
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination), destination, "An errand must end on a passable cell on the map.");
        }

        // The slot is dropped here; the claim cache is rebuilt at the head of the
        // next tick and will simply not list it, so the ring sees the space at once.
        unit.HasSlot = false;
        unit.Errand = destination;
        unit.Goal = destination;
        unit.FieldKey = destination;
        Redirect(unit);
    }

    /// <summary>
    /// Ends an agent's errand: it is aimed back at its formation's ring, holds no
    /// slot, and claims one on approach exactly as a freshly ordered member does.
    /// A no-op on an agent that is not away.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">No such agent.</exception>
    public void Recall(int agent)
    {
        var unit = Resolve(agent);
        if (unit.Errand < 0 || unit.Group is null)
        {
            return;
        }

        // Back to exactly the state Order leaves a group member in.
        unit.Errand = -1;
        unit.Goal = unit.Group.Destination;
        unit.FieldKey = unit.Group.Destination;
        unit.HasSlot = false;
        Redirect(unit);
    }

    /// <summary>
    /// Ends an agent's errand into the formation <paramref name="alongside"/> is
    /// in, rather than the one it left. What a squad needs after a sortie: the
    /// fellows it left at the station have moved on, and coming back means
    /// joining them where they are now.
    /// </summary>
    /// <remarks>
    /// The formation's ring is regrown to its new member count, so the joiner has
    /// a slot to claim on approach like anyone else. Without that it waits beside
    /// a full ring indefinitely: a member with no slot that makes no progress
    /// waits four backstops for its next attempt, on the premise that a claim will
    /// wake it sooner -- and with every slot held, no claim ever comes. A no-op on
    /// an agent that is not away. See <c>docs/search-and-movement.md</c>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">No such agent, either of them.</exception>
    /// <exception cref="ArgumentException">The two ids are the same agent.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="alongside"/> has never been ordered.</exception>
    public void Recall(int agent, int alongside)
    {
        var unit = Resolve(agent);
        var host = Resolve(alongside);
        if (agent == alongside)
        {
            throw new ArgumentException("An agent cannot rejoin alongside itself.", nameof(alongside));
        }

        if (host.Group is null)
        {
            throw new InvalidOperationException(
                $"agent {alongside} has never been ordered and is in no formation to join.");
        }

        if (unit.Errand < 0)
        {
            return;
        }

        if (!ReferenceEquals(unit.Group, host.Group))
        {
            unit.Group?.Members.Remove(unit);
            _groups.RemoveAll(g => g.Members.Count == 0);
            host.Group.Members.Add(unit);
            unit.Group = host.Group;
            host.Group.Slots = RingFor(
                host.Group.Destination,
                host.Group.Members.Count,
                [.. host.Group.Members.Select(m => m.Cell)]);
        }

        unit.Errand = -1;
        unit.Goal = host.Group.Destination;
        unit.FieldKey = host.Group.Destination;
        unit.HasSlot = false;
        Redirect(unit);
    }

    private Agent Resolve(int agent)
    {
        if (agent < 0 || agent >= _agents.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(agent), agent, $"no such agent; this system has {_agents.Count}.");
        }

        return _agents[agent];
    }

    /// <summary>A goal changed under this agent: plan again, now, from scratch.</summary>
    private void Redirect(Agent unit)
    {
        unit.StalledTicks = 0;
        unit.RetryAfterTick = CurrentTick;
        unit.WantsPlan = true;
        Abandon(unit);

        if (IsFollower(unit))
        {
            Stand(unit);
        }
    }

    /// <summary>
    /// Drops whatever plan the agent was walking and holds the cell it stands on,
    /// from now. What a follower does when its goal changes -- there is no
    /// anchor to walk out to, it simply descends toward the new goal from the
    /// next tick -- and what any agent does when the step it was about to take
    /// is no longer its to take.
    /// </summary>
    private void Stand(Agent agent)
    {
        agent.Plan = null;
        agent.BlockedTicks = 0;
        _table.Reserve([agent.Cell], CurrentTick, agent.Id);
    }

    /// <summary>
    /// Advances one tick: reconcile settling groups, spend the planning budget,
    /// then move everybody — and wake the stalled if anyone arrived, because an
    /// arrival is exactly the event that can change a stalled agent's answer.
    /// </summary>
    public void Tick()
    {
        // Per-tick occupancy caches the doctrines read through GroupOps.
        _claimedGoals.Clear();
        _settledCells.Clear();
        _occupiedCells.Clear();
        foreach (var agent in _agents)
        {
            _occupiedCells.Add(agent.Cell);
            if (agent.Cell == agent.Goal)
            {
                _settledCells.Add(agent.Cell);
            }
        }

        // Claimed goals are rebuilt group by group, so that the one thing the
        // doctrine does guarantee can be ASSERTED: within a group, no two
        // slot-holders share a cell. Two groups may legitimately hold one cell,
        // because two single-unit orders aimed at the same spot are two claims
        // on it by design (one arrives, the other stalls and reconciles to a
        // neighbour), and the cache then records the later group's holder.
        // Within a group the same state was the endgame defect: two members
        // standing on one cell for good, visible only as an arrival count. The
        // doctrine displaces the absent claimant instead, and if that ever
        // stops holding, the tick that first sees it names both agents rather
        // than letting the last writer quietly win the cache.
        foreach (var group in _groups)
        {
            foreach (var agent in group.Members)
            {
                if (!agent.HasSlot)
                {
                    continue;
                }

                if (_claimedGoals.TryGetValue(agent.Goal, out var holder))
                {
                    foreach (var other in group.Members)
                    {
                        if (other.Id == holder)
                        {
                            throw new InvalidOperationException(
                                $"agents {holder} and {agent.Id} of the group ordered to cell {group.Destination} " +
                                $"both hold cell {agent.Goal} as their slot.");
                        }
                    }
                }

                _claimedGoals[agent.Goal] = agent.Id;
            }
        }

        foreach (var group in _groups)
        {
            // Groups of one are exempt on principle: an individually addressed
            // order means THIS unit, THERE, and no doctrine may falsify that.
            if (group.Members.Count >= 2)
            {
                group.Doctrine.Advance(new GroupOps(this, group));
            }

            ElectLeader(group);
        }

        LastTick = SpendPlanningBudget();

        CurrentTick++;
        _table.Advance();

        var anyArrived = false;
        var vacated = new HashSet<int>();
        var entered = new HashSet<int>();
        // THE GUARANTEE, at the one place it can be kept: a unit takes a step
        // only if it holds the cell at that tick. A plan committed against a
        // table that has since changed -- a follower stopped on a cell it was
        // going to cross -- is stale; its owner stands where it is, holds that,
        // and asks again (followers by following, everybody else by
        // searching). Standing can make a fellow's step stale in turn, when
        // that fellow was stepping into the cell being held, so this runs to a
        // fixed point BEFORE anybody moves: no unit may step into a cell
        // another unit has just decided to stay on.
        // Two tests, and both are needed. The table's: the mover holds the cell
        // at this tick. And the plain one: nobody is standing still on it --
        // because a unit that has just decided to stand parks on its cell from
        // THIS tick, and a mover whose plan parks it on the same cell from the
        // same tick ties with it in the table, while in the world one of them
        // is already there. Standing wins; the mover stands too, and the loop
        // goes round again because that can stop the unit behind it.
        var moving = new bool[_agents.Count];
        var target = new int[_agents.Count];
        for (var i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            var step = agent.Plan?.CellAt(CurrentTick) ?? -1;
            target[i] = step;
            moving[i] = step >= 0 && step != agent.Cell && _table.HolderOf(step, CurrentTick) == agent.Id;
        }

        bool anyStood;
        do
        {
            anyStood = false;
            var still = new HashSet<int>();
            for (var i = 0; i < _agents.Count; i++)
            {
                if (!moving[i])
                {
                    still.Add(_agents[i].Cell);
                }
            }

            for (var i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                var stale = (target[i] >= 0 && target[i] != agent.Cell && !moving[i] && agent.Plan is not null) ||
                            (moving[i] && still.Contains(target[i]));
                if (stale)
                {
                    Stand(agent);
                    agent.WantsPlan = true;
                    moving[i] = false;
                    anyStood = true;
                }
            }
        }
        while (anyStood);

        foreach (var agent in _agents)
        {
            var at = agent.Plan?.CellAt(CurrentTick) ?? -1;
            if (at >= 0)
            {
                if (at != agent.Cell)
                {
                    vacated.Add(agent.Cell);
                    entered.Add(at);
                }

                var wasArrived = agent.Cell == agent.Goal;
                agent.Cell = at;
                anyArrived |= !wasArrived && agent.Cell == agent.Goal;
            }
        }


        // THE SPACE-OPENED WAKE — the release event the reservation-index note
        // always promised, at last implemented precisely. A cell somebody moved
        // off and nobody moved onto is genuinely free, and a stalled unit
        // standing beside it should replan NOW, not when its backstop lapses.
        // Without this the rear of a group failed its first replans against its
        // own front, napped for sixty-four ticks, and departed as a visibly
        // SECOND EXPEDITION after the first had crossed and assembled -- an
        // outcome nobody wants from one order. Vacated-and-reentered cells
        // (lane traffic streaming past) wake nobody, so a unit beside a busy
        // lane does not burn a search per passing car.
        vacated.ExceptWith(entered);

        foreach (var agent in _agents)
        {
            if (agent.StalledTicks == 0 || agent.Cell == agent.Goal ||
                agent.RetryAfterTick <= CurrentTick)
            {
                continue;
            }

            // An arrival is broadcast news (a slot freed somewhere, claims are
            // about to move) and measurably earns its keep alongside the local
            // wake: removing it doubled the throng's node spend, because
            // distant queued units need the slot news even with no vacancy
            // beside them.
            if (anyArrived)
            {
                agent.RetryAfterTick = CurrentTick;
                continue;
            }

            // Per-agent cooldown on vacancy wakes. Unthrottled, a 200-unit
            // crowd's trailing edge vacates cells every tick, every stalled
            // unit re-probes every tick, and the budget drowns in churn --
            // measured as ONE arrival in a thousand ticks. Throttled, a rank
            // still follows the rank ahead within eight ticks: one movement,
            // bounded spend.
            if (CurrentTick - agent.LastVacancyWakeTick < StallBackstopTicks / 8)
            {
                continue;
            }

            var x = _grid.ColumnOf(agent.Cell);
            var y = _grid.RowOf(agent.Cell);
            var here = Movement.OctileDistance(x, y, _grid.ColumnOf(agent.Goal), _grid.RowOf(agent.Goal));
            foreach (var step in Movement.Steps)
            {
                var nextX = x + step.DeltaX;
                var nextY = y + step.DeltaY;
                if (!vacated.Contains((nextY * _grid.Width) + nextX))
                {
                    continue;
                }

                // Only an opening AHEAD is an opening. A marching crowd frees a
                // rank of cells behind itself every tick, and waking interior
                // units on rear vacancies measured as budget saturation and one
                // arrival in a thousand ticks -- their blockage was in front all
                // along. A vacancy no closer to the goal than the unit is just
                // the crowd leaving.
                if (Movement.OctileDistance(
                        nextX, nextY, _grid.ColumnOf(agent.Goal), _grid.RowOf(agent.Goal)) < here)
                {
                    agent.RetryAfterTick = CurrentTick;
                    agent.LastVacancyWakeTick = CurrentTick;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The member best placed to head the group: minimal field distance to the
    /// destination, ties on id. Presentational and diagnostic this milestone —
    /// the hook later steering hangs off, not a special planner.
    /// </summary>
    /// <remarks>
    /// An empty group has no leader and is not an error. It cannot arise from a
    /// completed order, which prunes emptied groups, but a group with no members is
    /// a coherent state and dereferencing member zero to discover that is not. This
    /// guard and the id resolution in
    /// <see cref="Order(IReadOnlyList{int}, int, GroupDoctrine?)"/> close the same
    /// hole from both ends.
    /// </remarks>
    private void ElectLeader(Group group)
    {
        if (group.Members.Count == 0)
        {
            group.Leader = -1;
            return;
        }

        // Keyed by the destination, not by a member: a member away on an errand
        // carries the errand's field key, and the leader is measured against the
        // group's destination whoever happens to be first in the list.
        var field = _fields.For(group.Destination);

        var best = -1;
        var bestCost = double.PositiveInfinity;
        foreach (var member in group.Members.OrderBy(m => m.Id))
        {
            var cost = field.CostFrom(member.Cell);
            if (cost < bestCost)
            {
                best = member.Id;
                bestCost = cost;
            }
        }

        group.Leader = best;
    }

    /// <summary>Every agent's plan as it currently stands, for collision checking.</summary>
    public IReadOnlyList<AgentPlan> CurrentPlans() =>
        [.. _agents.Where(a => a.Plan is not null).Select(a => new AgentPlan(a.Id, a.Plan!))];

    private TickReport SpendPlanningBudget()
    {
        var remaining = _nodeBudgetPerTick;
        var started = 0;
        var finished = 0;
        var abandoned = 0;

        // A GROUP MOVEMENT, NOT A GROUP OF MOVEMENTS. Every member of a group
        // shares one distance field for its destination, and that field is an
        // exact flow field: descending it one step at a time IS the shortest
        // route over the terrain. So members follow it, each tick, in id
        // order, before any search runs -- and searches then see where the
        // followers will be next tick. No node is spent on a follower.
        //
        // Before this, every member was planned to the destination cell by its
        // own space-time search, and only one of them could ever hold that
        // cell; each of the others exhausted the whole window before returning
        // "walk to the crust and stop". On the arena that was 199 units doing
        // it, repeatedly.
        //
        // FRONT FIRST. Followers step in order of distance to their goal, nearest
        // first, ties on id -- the order a column actually moves in. In id order
        // a unit behind another asked before the one ahead had stepped, found
        // the cell ahead still parked on, and waited a tick; the throng's
        // departure spread went from a column to a concertina.
        var followers = _agents
            .Where(agent => IsFollower(agent) && FollowIsDue(agent))
            .OrderBy(agent => _fields.For(agent.FieldKey >= 0 ? agent.FieldKey : agent.Goal).CostFrom(agent.Cell))
            .ThenBy(agent => agent.Id)
            .ToArray();

        foreach (var agent in followers)
        {
            Follow(agent);
        }

        // Searches already in flight first. They are holding workspaces, and
        // finishing one frees a slot for somebody waiting.
        foreach (var agent in _agents)
        {
            if (agent.Search is null || remaining <= 0)
            {
                continue;
            }

            Tally(Progress(agent, ref remaining), ref finished, ref abandoned);
        }

        // LONGEST-WAITING FIRST, not lowest id. Iterating agents in id order
        // starves the tail: an agent whose search is abandoned wants another slot
        // immediately, and being early in the list it takes one -- so under a
        // budget too small to finish anything, the first two agents cycle forever
        // and nobody else is ever tried. Measured at 50 nodes a tick: 26 searches
        // started across 400 ticks, all of them belonging to agents 0 and 1, with
        // 18 agents never planned at all and therefore reporting no trouble.
        //
        // Ties break on id, so this stays deterministic: the same tick with the
        // same state always picks the same agent.
        //
        // The trade is real and was measured rather than assumed. Fair scheduling
        // took 200-agent arrivals from 123 down to 80, because agents mid-journey
        // now queue behind ones that just abandoned. It is still the right call:
        // 123 arrivals came with agents that were NEVER PLANNED, and a unit that
        // ignores an order reads as broken in a way that a slow unit does not.
        //
        // Prioritising plan-less agents ahead of plan-refreshing ones was tried as
        // a way to recover the arrivals. It changed nothing measurable -- after
        // the first round every agent holds some plan, so the discriminator is
        // uniform -- and was removed rather than kept as plausible-looking
        // decoration.
        var waiting = _agents
            .Where(ShouldStart)
            .OrderBy(a => a.LastPlanAttemptTick)
            .ThenBy(a => a.Id);

        foreach (var agent in waiting)
        {
            if (remaining <= 0 || InFlight() >= _maxSearchesInFlight)
            {
                break;
            }

            Begin(agent);
            started++;

            Tally(Progress(agent, ref remaining), ref finished, ref abandoned);
        }

        return new TickReport(
            NodesSpent: _nodeBudgetPerTick - Math.Max(remaining, 0),
            SearchesStarted: started,
            SearchesFinished: finished,
            SearchesAbandoned: abandoned,
            Queued: _agents.Count(ShouldStart));
    }

    private int InFlight() => _agents.Count(a => a.Search is not null);

    /// <summary>
    /// Whether this agent wants a planning slot now.
    /// </summary>
    /// <remarks>
    /// A PLAN IS ASKED FOR BEFORE THE OLD ONE RUNS OUT, by exactly the run-up the
    /// next search will be given. Waiting for the plan to expire and only then
    /// starting a search means the agent stands for the whole anchor while a
    /// finished plan waits to be allowed to begin (<c>docs/search-and-movement.md</c>
    /// has what that cost the patrol). Asking a latency early means the new plan is
    /// anchored to start where the old one ends, and the walk is continuous.
    /// <para>
    /// It does not make an agent replan more often in steady state -- a plan is
    /// still replaced once per plan -- only earlier, and <see cref="Commit"/>
    /// already splices the old plan's remaining cells ahead of the new one, which
    /// is what makes an anchor inside a live plan safe.
    /// </para>
    /// </remarks>
    private bool ShouldStart(Agent agent) =>
        agent.Search is null &&
        agent.Cell != agent.Goal &&
        CurrentTick >= agent.RetryAfterTick &&
        !(IsFollower(agent) && KeepsFollowing(agent)) &&
        (agent.WantsPlan || agent.Plan is null || agent.Plan.LastTick <= CurrentTick + agent.Latency);

    /// <summary>
    /// A follower keeps following -- never searches -- while it is not blocked
    /// long, and ALWAYS while it holds no slot. An unslotted member's goal is
    /// the destination, which somebody already holds; a search for that is the
    /// window-exhausting search followers exist to avoid, and its "no progress"
    /// then read as a stall and had the reconcile pass park a whole throng in
    /// the wrong room. A slotted member's goal is a cell it can hold, so a
    /// search for it is cheap and is how it gets round a parked fellow.
    /// </summary>
    private static bool KeepsFollowing(Agent agent) =>
        !agent.HasSlot || agent.BlockedTicks < FollowBlockedTicks;

    /// <summary>
    /// A member of a group of two or more, on station: it moves by descending
    /// the group's field rather than by searching. A single-unit order, and a
    /// unit away on an errand, still plan -- their goal is one cell they can
    /// hold, and that search is cheap.
    /// </summary>
    private static bool IsFollower(Agent agent) =>
        agent.Group is { Members.Count: >= 2 } && agent.Errand < 0;

    /// <summary>
    /// A follower takes a step whenever it has no live plan to walk: its own
    /// two-cell plan ran out last tick, or it never had one. A committed search
    /// plan is walked to its end first, and a search in flight is left to land.
    /// </summary>
    private bool FollowIsDue(Agent agent) =>
        agent.Search is null &&
        agent.Cell != agent.Goal &&
        KeepsFollowing(agent) &&
        (agent.Plan is null || agent.Plan.LastTick <= CurrentTick);

    /// <summary>
    /// One step down the field: of the legal, free, non-swapping neighbours,
    /// the one with the lowest cost -- if it is lower than here. Otherwise
    /// stand, and count the tick as blocked.
    /// </summary>
    /// <remarks>
    /// The step is a two-cell plan reserved like any other, so the tick that
    /// moves everybody, the seam's <c>IsMoving</c>, the collision checker and
    /// the replay never learn that no search produced it. A member holding a
    /// claimed slot descends the octile distance to that slot instead of the
    /// field, which is keyed to the destination; a slot is a step or two from
    /// it and this is the first thing to try before spending a search there.
    /// </remarks>
    private void Follow(Agent agent)
    {
        var here = agent.Cell;
        var x = _grid.ColumnOf(here);
        var y = _grid.RowOf(here);

        var toSlot = agent.HasSlot && agent.Group is { } group && agent.Goal != group.Destination;
        var field = toSlot ? null : _fields.For(agent.FieldKey >= 0 ? agent.FieldKey : agent.Goal);
        var goalX = _grid.ColumnOf(agent.Goal);
        var goalY = _grid.RowOf(agent.Goal);

        double Cost(int cell) => toSlot
            ? Movement.OctileDistance(_grid.ColumnOf(cell), _grid.RowOf(cell), goalX, goalY)
            : field!.CostFrom(cell);

        var hereCost = Cost(here);
        var best = -1;
        var bestCost = hereCost;

        foreach (var step in Movement.Steps)
        {
            if (!Movement.IsLegalStep(_grid, x, y, step.DeltaX, step.DeltaY))
            {
                continue;
            }

            var next = ((y + step.DeltaY) * _grid.Width) + x + step.DeltaX;
            if (!_table.IsFree(next, CurrentTick + 1, agent.Id) ||
                _table.IsSwap(here, next, CurrentTick, agent.Id) ||
                _table.WillBeParkedOn(next, agent.Id))
            {
                continue;
            }

            var cost = Cost(next);
            if (cost < bestCost - 1e-9)
            {
                best = next;
                bestCost = cost;
            }
        }

        // Nowhere better: stand. Standing beats planning, so a plan that had
        // booked this cell for next tick is the one that goes stale, at the
        // move. The first version stepped OUT of the way instead, to the
        // cheapest free neighbour, and that is a unit walking away from its
        // destination -- the blob measured it as a retreat of two.
        if (best < 0)
        {
            agent.BlockedTicks++;
            agent.Plan = new PlanResult([here, here], CurrentTick, Movement.WaitCost, Expanded: 0, Found: false);
            _table.Reserve(agent.Plan.Cells, CurrentTick, agent.Id);
            return;
        }

        agent.BlockedTicks = 0;
        agent.Plan = new PlanResult([here, best], CurrentTick, bestCost, Expanded: 0, Found: best == agent.Goal);
        _table.Reserve(agent.Plan.Cells, CurrentTick, agent.Id);
    }

    private void Begin(Agent agent)
    {
        agent.WantsPlan = false;
        agent.LastPlanAttemptTick = CurrentTick;
        agent.AnchorTick = CurrentTick + agent.Latency;
        agent.Workspace = _workspacePool.Count > 0 ? _workspacePool.Pop() : NewWorkspace();

        // THE TABLE MUST DESCRIBE THE MOVEMENT THAT WILL ACTUALLY HAPPEN while
        // the search runs. A standing thinker holds its cell. A MOVING one keeps
        // walking its old plan until the new one lands -- so its old route stays
        // reserved, sliced from now. Reserving only the current cell here
        // (Reserve is release-then-mark) silently dropped a moving agent's route
        // reservations while the route was still being walked, and everyone else
        // then planned straight through its future -- three scenarios collided
        // the first time reconciliation started mid-route searches.
        if (agent.Plan is { } live && live.LastTick > CurrentTick)
        {
            var remaining = new List<int>(live.LastTick - CurrentTick + 1);
            for (var t = CurrentTick; t <= live.LastTick; t++)
            {
                remaining.Add(live.CellAt(t));
            }

            _table.Reserve(remaining, CurrentTick, agent.Id);
        }
        else
        {
            _table.Reserve([agent.Cell], CurrentTick, agent.Id);
        }

        // The field build is amortised: once per destination, cached across the
        // whole system, and O(cells log cells) -- not charged to the node budget
        // because it is not search work, the same way Advance's ring clear is not.
        var field = _fields.For(agent.FieldKey >= 0 ? agent.FieldKey : agent.Goal);

        // The search starts from where the agent WILL BE at the anchor -- along
        // its old plan if one is still walking, its own cell if it stands. A
        // moving agent planned from its Begin-time cell commits a teleport.
        var start = agent.Plan?.CellAt(agent.AnchorTick) ?? agent.Cell;
        if (start < 0)
        {
            start = agent.Cell;
        }

        agent.Search = new BudgetedSearch(
            _grid, _table, agent.Id, start, agent.Goal, agent.AnchorTick, agent.Workspace, field);
    }

    /// <summary>What became of a search this call.</summary>
    /// <remarks>
    /// Three outcomes, not two. A bool "did it finish" counts an abandoned search
    /// as a completed one, which is exactly the report that hid this: twenty-four
    /// searches were said to have finished and twenty-four abandoned, and they
    /// were the same twenty-four events. Nothing had ever produced a plan.
    /// </remarks>
    private enum SearchOutcome
    {
        Running,
        Committed,
        Abandoned,
    }

    private static void Tally(SearchOutcome outcome, ref int finished, ref int abandoned)
    {
        switch (outcome)
        {
            case SearchOutcome.Committed:
                finished++;
                break;
            case SearchOutcome.Abandoned:
                abandoned++;
                break;
            case SearchOutcome.Running:
            default:
                break;
        }
    }

    private SearchOutcome Progress(Agent agent, ref int remaining)
    {
        // CHECKED BEFORE ADVANCING, NOT AFTER. A suspended search holds the tick
        // the window began at when it was created, and every state in its frontier
        // sits at or after its anchor. Once the window has moved past that anchor,
        // the very first pop queries a tick the table has already dropped and it
        // throws -- so a search whose anchor has gone stale must never be advanced
        // again, not merely discarded once it finishes.
        if (agent.AnchorTick < CurrentTick)
        {
            // HALF the horizon, not all of it. Anchoring at the window's last tick
            // leaves the search exactly one tick to plan in, so it comes back with
            // a single cell and the agent stands still — a plan in form and a
            // stall in fact. The anchor has to leave room for a plan behind it.
            var ceiling = Math.Max(_table.Horizon / 2, InitialPlanningLatency);

            if (agent.Latency >= ceiling)
            {
                // Already given every tick the window has and still not finished.
                // Retrying identically is a treadmill, so this becomes a reported
                // stall that waits for an event, with the timer as the backstop.
                agent.StalledTicks++;
                agent.RetryAfterTick = CurrentTick + StallBackstopTicks;
            }
            else
            {
                // It needed longer than it was given. Give it longer next time, or
                // it will be discarded at the same point forever.
                agent.Latency = Math.Min(agent.Latency * 2, ceiling);
            }

            Abandon(agent);
            agent.WantsPlan = true;
            return SearchOutcome.Abandoned;
        }

        var search = agent.Search!;
        var before = search.Expanded;
        var done = search.Advance(Math.Max(remaining, 1));

        var spent = search.Expanded - before;
        remaining -= spent;
        TotalExpanded += spent;

        if (!done)
        {
            return SearchOutcome.Running;
        }

        if (!Commit(agent, search.Result))
        {
            // The table moved under a suspended search -- followers step every
            // tick -- and the plan it produced now crosses somebody. Ask again
            // from where the unit stands rather than let Mark refuse it.
            Release(agent);
            agent.WantsPlan = true;
            return SearchOutcome.Abandoned;
        }

        Release(agent);
        return SearchOutcome.Committed;
    }

    /// <returns>
    /// False if the plan no longer fits the table and was not committed; the
    /// caller re-asks. True otherwise.
    /// </returns>
    private bool Commit(Agent agent, PlanResult plan)
    {
        // The agent follows its OLD plan until the anchor -- standing still if it
        // has none -- then follows the new one. Splicing the two here means
        // CellAt answers for every tick in between, and one Reserve call covers
        // the whole future rather than two that would each replace the other.
        var pad = agent.AnchorTick - CurrentTick;
        var cells = new List<int>(pad + plan.Cells.Count);
        for (var t = CurrentTick; t < agent.AnchorTick; t++)
        {
            var at = agent.Plan?.CellAt(t) ?? -1;
            cells.Add(at >= 0 ? at : agent.Cell);
        }

        cells.AddRange(plan.IsStuck ? [cells.Count > 0 ? cells[^1] : agent.Cell] : plan.Cells);

        // VALIDATED AGAINST THE TABLE AS IT IS NOW, not as it was when the
        // search looked. A search suspended across ticks priced its states
        // against a table that followers have since changed every tick; a
        // plan through a cell somebody now holds is stale, not wrong, and is
        // discarded here rather than refused by Mark's assertion, which stays
        // as the last line of defence for the genuinely unsound.
        var last = CurrentTick + _table.Horizon - 1;
        for (var i = 0; i < cells.Count; i++)
        {
            var tick = CurrentTick + i;
            if (tick > last)
            {
                break;
            }

            if (!_table.IsFree(cells[i], tick, agent.Id) ||
                (i > 0 && _table.IsSwap(cells[i - 1], cells[i], tick - 1, agent.Id)))
            {
                return false;
            }
        }

        if (!_table.IsHoldable(cells[^1], CurrentTick + cells.Count - 1, agent.Id))
        {
            return false;
        }

        agent.Plan = new PlanResult(cells, CurrentTick, plan.Cost, plan.Expanded, plan.Found);
        _table.Reserve(cells, CurrentTick, agent.Id);
        agent.BlockedTicks = 0;

        // It finished inside its slack, so the next search starts optimistic again
        // rather than carrying a latency it no longer needs.
        agent.Latency = InitialPlanningLatency;

        // Progress is measured against the GOAL, not against whether a plan came
        // back. Two agents deadlocked nose to nose both have plans and would
        // otherwise report as healthy forever.
        var before = DistanceToGoal(agent, agent.Cell);
        var after = DistanceToGoal(agent, cells[^1]);
        if (after < before - 1e-9)
        {
            agent.StalledTicks = 0;
        }
        else
        {
            // No progress. Wait for an event -- an arrival, an order, a claim,
            // a reconciliation -- rather than re-asking an unchanged world on
            // a short timer; the backstop turns a missed wake into slow, not
            // frozen. A QUEUED member (no slot yet) waits four times longer:
            // its progress arrives as a claim, which wakes it by name, and its
            // timer probes against the crowd measured as roughly half the
            // whole order's node spend.
            agent.StalledTicks++;
            agent.RetryAfterTick = CurrentTick + (agent.HasSlot ? StallBackstopTicks : 4 * StallBackstopTicks);
        }

        return true;
    }

    private void Abandon(Agent agent)
    {
        if (agent.Search is null)
        {
            return;
        }

        Release(agent);
    }

    /// <summary>
    /// A cell was just parked on outside any search. Every other search still in
    /// flight that has reached that cell is discarded and asked for again, because
    /// what it has priced so far assumed the cell was free.
    /// </summary>
    /// <remarks>
    /// The table only ever changes at a commit, and a commit reserves a path --
    /// one cell per tick -- so a suspended search that had already reached one
    /// of those exact states was rare enough that the tie-break fuzz never
    /// produced it. A park reserves one cell at EVERY tick of the window, and a
    /// settling crust parks many at once, so the same race became the first
    /// thing the arena did: "agent 58 reserved cell 1804 at tick 234, which
    /// agent 196 already holds". Searches that have not reached the cell need
    /// nothing; they read the table as they go and will route around.
    /// </remarks>
    private void ForgetSearchesThrough(int cell, Agent parked)
    {
        foreach (var other in _agents)
        {
            if (ReferenceEquals(other, parked) || other.Search is null || !other.Search.Touches(cell))
            {
                continue;
            }

            Abandon(other);
            other.WantsPlan = true;
        }
    }

    private void Release(Agent agent)
    {
        if (agent.Workspace is not null)
        {
            _workspacePool.Push(agent.Workspace);
            agent.Workspace = null;
        }

        agent.Search = null;
    }

    private double DistanceToGoal(Agent agent, int from) =>
        Movement.OctileDistance(
            _grid.ColumnOf(from), _grid.RowOf(from),
            _grid.ColumnOf(agent.Goal), _grid.RowOf(agent.Goal));

    /// <summary>
    /// The one implementation of <see cref="IGroupOps"/>, and the reason the
    /// contracts can be public while nothing concrete is.
    /// </summary>
    /// <remarks>
    /// Nested, so it reaches <see cref="MovementSystem"/>'s private state without
    /// widening anything; internal, so no consumer can name it. A doctrine sees
    /// three interfaces and no class. Nobody outside constructs one either --
    /// <see cref="Tick"/> builds it and hands it to
    /// <see cref="GroupDoctrine.Advance"/>, which is what lets the type stay
    /// hidden with no factory to compensate.
    /// <para>
    /// Documentation lives on the interfaces, where a caller reads it. What is
    /// here is implementation.
    /// </para>
    /// </remarks>
    internal sealed class GroupOps : IGroupOps
    {
        private readonly MovementSystem _system;
        private readonly Group _group;
        private readonly DistanceField _field;
        private readonly HashSet<int> _members;

        internal GroupOps(MovementSystem system, Group group)
        {
            _system = system;
            _group = group;

            // The group's field is keyed by its destination, which is what Order
            // gives every member as its field key. Read from the group rather
            // than from a member, because a member away on an errand carries the
            // errand's key, and "whichever member happens to be first" is not a
            // fact the group's field should depend on.
            _field = system._fields.For(group.Destination);

            // Two lists from one membership: on station, and away. Every pass
            // iterates Members; the mutators accept both, because confinement is
            // about the group and an errand does not leave it.
            Members = [.. group.Members.Where(m => m.Errand < 0).Select(m => m.Id).OrderBy(id => id)];
            Dispatched = [.. group.Members.Where(m => m.Errand >= 0).Select(m => m.Id).OrderBy(id => id)];
            _members = [.. group.Members.Select(m => m.Id)];
        }

        /// <inheritdoc/>
        public IReadOnlyList<int> Dispatched { get; }

        /// <inheritdoc/>
        public int ErrandOf(int id) => Member(id).Errand;

        /// <summary>
        /// The agent behind a member id, refusing an id outside this group.
        /// </summary>
        /// <remarks>
        /// Every mutator resolves its target here, so a doctrine handed one
        /// group's seam cannot reach another group's agent: the confinement is
        /// structural rather than a convention the doctrine is trusted to keep.
        /// The check comes before any write, so a refused call changes nothing.
        /// </remarks>
        private Agent Member(int id)
        {
            if (!_members.Contains(id))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id), id, $"Agent {id} is not a member of the group ordered to cell {_group.Destination}.");
            }

            return _system._agents[id];
        }

        /// <summary>
        /// Drops a claim, but only the caller's own. The same shape as the
        /// reservation ring's release guard: a record naming a cell somebody else
        /// now holds must not take it from them.
        /// </summary>
        private void Unclaim(int cell, int id)
        {
            if (_system._claimedGoals.TryGetValue(cell, out var holder) && holder == id)
            {
                _system._claimedGoals.Remove(cell);
            }
        }

        /// <inheritdoc/>
        public int CurrentTick => _system.CurrentTick;

        /// <inheritdoc/>
        public int Destination => _group.Destination;

        /// <inheritdoc/>
        public IReadOnlyList<int> Slots => _group.Slots;

        /// <inheritdoc/>
        public IReadOnlyList<int> Members { get; }

        /// <inheritdoc/>
        public IReadOnlyList<Chokepoint> Chokepoints => _system.MapChokepoints;

        /// <inheritdoc/>
        public double FieldCost(int cell) => _field.CostFrom(cell);

        /// <inheritdoc/>
        public int CellOf(int id) => _system._agents[id].Cell;

        /// <inheritdoc/>
        public int GoalOf(int id) => _system._agents[id].Goal;

        /// <inheritdoc/>
        public int StalledReplans(int id) => _system._agents[id].StalledTicks;

        /// <inheritdoc/>
        public bool HasSlot(int id) => _system._agents[id].HasSlot;

        /// <inheritdoc/>
        /// <remarks>
        /// Read from the plan the tick is about to walk. Doctrines run before the
        /// budget is spent and before the clock advances, so the cell this agent
        /// moves onto is <c>CellAt(CurrentTick + 1)</c> -- the very expression
        /// <see cref="Tick"/> uses a few lines later to move it.
        /// </remarks>
        public bool IsMoving(int id)
        {
            var agent = _system._agents[id];
            if (agent.Plan is not { } plan)
            {
                return false;
            }

            var next = plan.CellAt(_system.CurrentTick + 1);
            return next >= 0 && next != agent.Cell;
        }

        /// <inheritdoc/>
        public bool IsDoorway(int cell) => _system.Doorways.Contains(cell);

        /// <inheritdoc/>
        public bool IsClaimed(int cell) => _system._claimedGoals.ContainsKey(cell);

        /// <inheritdoc/>
        public int ClaimantOf(int cell) => _system._claimedGoals.TryGetValue(cell, out var holder) ? holder : -1;

        /// <inheritdoc/>
        public void ReleaseSlot(int id)
        {
            var agent = Member(id);
            if (!agent.HasSlot)
            {
                return;
            }

            agent.HasSlot = false;
            Unclaim(agent.Goal, id);
            agent.StalledTicks = 0;
            agent.RetryAfterTick = _system.CurrentTick;
            agent.WantsPlan = true;
            _system.Abandon(agent);
        }

        /// <inheritdoc/>
        public bool IsSettled(int cell) => _system._settledCells.Contains(cell);

        /// <inheritdoc/>
        public bool IsOccupied(int cell) => _system._occupiedCells.Contains(cell);

        /// <inheritdoc/>
        public void ClaimSlot(int id, int cell)
        {
            var agent = Member(id);

            // WHETHER IT HELD ONE IS READ BEFORE IT IS SET, and that is the fix.
            // Setting HasSlot first and then removing agent.Goal from the claimed
            // set retracts a claim this agent may never have made: an un-slotted
            // member's Goal is the shared WALKING TARGET every member was given at
            // order time, so one member claiming its own cell deleted a different
            // member's legitimate claim on that target. A third member then saw the
            // cell as unclaimed and took it, and two members held one cell
            // permanently -- reachable at default settings whenever three are near
            // the ring, which is every gather endgame.
            var held = agent.HasSlot;
            agent.HasSlot = true;

            if (agent.Goal == cell)
            {
                _system._claimedGoals[cell] = id;
                return;
            }

            if (held)
            {
                Unclaim(agent.Goal, id);
            }

            _system._claimedGoals[cell] = id;
            agent.Goal = cell;
            agent.StalledTicks = 0;
            agent.RetryAfterTick = _system.CurrentTick;
            agent.WantsPlan = true;
            _system.Abandon(agent);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The table is asked BEFORE anything changes, so a refusal leaves the
        /// member exactly as it was -- plan, claim, search and all. On success
        /// the order is: table (the old route is released and the cell held),
        /// claim (goal and cache, which may retract a slot elsewhere), then the
        /// search in flight is dropped -- unconditionally, because ClaimSlot
        /// leaves a search alone when the goal already matches, and a search
        /// that later committed would re-route a parked unit -- and finally the
        /// plan becomes the one cell it stands on, so <see cref="Tick"/> keeps it
        /// there and <see cref="IsMoving"/> reads false from the next pass.
        /// </remarks>
        public bool Park(int id)
        {
            var agent = Member(id);
            var here = agent.Cell;

            if (!_system._table.TryPark(here, id))
            {
                return false;
            }

            _system.ForgetSearchesThrough(here, agent);
            ClaimSlot(id, here);
            _system.Abandon(agent);

            agent.Plan = new PlanResult([here], _system.CurrentTick, 0.0, 0, Found: true);
            agent.WantsPlan = false;
            agent.StalledTicks = 0;
            return true;
        }

        /// <inheritdoc/>
        public void Wake(int id)
        {
            var agent = Member(id);
            agent.RetryAfterTick = Math.Min(agent.RetryAfterTick, _system.CurrentTick);
        }

        /// <inheritdoc/>
        public void Hold(int id, int ticks)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
            var agent = Member(id);
            agent.RetryAfterTick = Math.Max(agent.RetryAfterTick, _system.CurrentTick + ticks);
        }

        /// <inheritdoc/>
        public bool CanWalkTo(int id, int cell)
        {
            var grid = _system._grid;
            var start = CellOf(id);
            if (start == cell)
            {
                return true;
            }

            var seen = new bool[grid.CellCount];
            var queue = new Queue<int>();
            seen[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var here = queue.Dequeue();
                var x = grid.ColumnOf(here);
                var y = grid.RowOf(here);
                foreach (var step in Movement.Steps)
                {
                    if (!Movement.IsLegalStep(grid, x, y, step.DeltaX, step.DeltaY))
                    {
                        continue;
                    }

                    var next = ((y + step.DeltaY) * grid.Width) + x + step.DeltaX;
                    if (next == cell)
                    {
                        return true;
                    }

                    if (seen[next] || IsSettled(next))
                    {
                        continue;
                    }

                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public IReadOnlyList<int> Neighbours(int cell)
        {
            var grid = _system._grid;
            var x = grid.ColumnOf(cell);
            var y = grid.RowOf(cell);
            var result = new List<int>(8);
            foreach (var step in Movement.Steps)
            {
                if (Movement.IsLegalStep(grid, x, y, step.DeltaX, step.DeltaY))
                {
                    result.Add(((y + step.DeltaY) * grid.Width) + x + step.DeltaX);
                }
            }

            return result;
        }

        /// <inheritdoc/>
        public IReadOnlyList<int> ReachableSpots(IReadOnlyList<int> fromMembers)
        {
            ArgumentNullException.ThrowIfNull(fromMembers);

            var grid = _system._grid;
            var seen = new bool[grid.CellCount];
            var queue = new Queue<int>();
            foreach (var id in fromMembers)
            {
                var cell = CellOf(id);
                if (!seen[cell])
                {
                    seen[cell] = true;
                    queue.Enqueue(cell);
                }
            }

            var spots = new List<int>();
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();

                if (!IsClaimed(cell) && !IsOccupied(cell) && _field.Reaches(cell))
                {
                    spots.Add(cell);
                }

                var x = grid.ColumnOf(cell);
                var y = grid.RowOf(cell);
                foreach (var step in Movement.Steps)
                {
                    if (!Movement.IsLegalStep(grid, x, y, step.DeltaX, step.DeltaY))
                    {
                        continue;
                    }

                    var next = ((y + step.DeltaY) * grid.Width) + x + step.DeltaX;
                    if (seen[next] || IsSettled(next))
                    {
                        continue;
                    }

                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }

            spots.Sort((a, b) =>
            {
                var byCost = _field.CostFrom(a).CompareTo(_field.CostFrom(b));
                return byCost != 0 ? byCost : a.CompareTo(b);
            });

            return spots;
        }
    }
}
