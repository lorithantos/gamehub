namespace Nav.Core;

/// <param name="Id">Stable for the life of the system, and the planning order.</param>
/// <param name="Cell">Where the agent is now.</param>
/// <param name="Goal">Where it is trying to get to, or its own cell if it has no order.</param>
/// <param name="Arrived">Standing on its goal.</param>
/// <param name="StalledTicks">Consecutive replans that got it no closer to its goal.</param>
/// <param name="Thinking">A search is in flight for this agent; it holds position until it lands.</param>
/// <param name="Waiting">
/// Gated from planning until an event or its backstop: queued by a doctrine,
/// backing off after a failed replan, or held short of a gate. WAITING IS NOT
/// FAILING — a unit doing nothing visible reads as "I've refused the order"
/// unless the display can say "I'm in the queue", which is what this flag is for.
/// </param>
/// <remarks>
/// <see cref="Stuck"/> means NO PROGRESS, not "no plan". The distinction was worth
/// a bug: an agent that can stand still always has a plan — the one-cell plan of
/// staying put — so a check for "did the planner return anything" reports two
/// agents deadlocked nose to nose in a corridor as perfectly healthy. They have
/// plans. The plans go nowhere.
/// </remarks>
public readonly record struct AgentState(
    int Id,
    int Cell,
    int Goal,
    bool Arrived,
    int StalledTicks,
    bool Thinking,
    bool Waiting)
{
    /// <summary>Has an order it is making no progress on.</summary>
    public bool Stuck => !Arrived && StalledTicks > 0;
}

/// <param name="NodesSpent">Search nodes expanded during the tick.</param>
/// <param name="SearchesStarted">Searches begun during the tick.</param>
/// <param name="SearchesFinished">Searches that produced a plan during the tick.</param>
/// <param name="SearchesAbandoned">Searches discarded because their anchor went stale.</param>
/// <param name="Queued">Agents still waiting for a planning slot at the end of the tick.</param>
public readonly record struct TickReport(
    int NodesSpent,
    int SearchesStarted,
    int SearchesFinished,
    int SearchesAbandoned,
    int Queued);

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
    /// wake into slow instead of frozen. The headon trace showed the old
    /// 8-tick timer re-asking an unchanged question at 196 nodes a probe.
    /// </summary>
    private const int StallBackstopTicks = 64;

    internal sealed class Agent(int id, int cell)
    {
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

        /// <summary>The parking ring: nearest cells to the destination, innermost first.</summary>
        public required IReadOnlyList<int> Slots { get; init; }

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
    private readonly FieldCache _fields;
    private readonly List<Group> _groups = [];

    // Per-tick occupancy caches, rebuilt at the top of Tick and kept current by
    // GroupOps mutations, so doctrines read O(1) answers instead of scanning.
    private readonly HashSet<int> _claimedGoals = [];
    private readonly HashSet<int> _settledCells = [];
    private readonly HashSet<int> _occupiedCells = [];

    private IReadOnlyList<Chokepoint>? _chokepoints;

    /// <summary>The map's chokepoints, detected once and cached for the system's life.</summary>
    internal IReadOnlyList<Chokepoint> MapChokepoints => _chokepoints ??= ChokepointMap.Find(_grid);

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
    public MovementSystem(
        Grid grid,
        int horizon = 32,
        int nodeBudgetPerTick = 4000,
        int maxSearchesInFlight = 8)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeBudgetPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSearchesInFlight);

        _grid = grid;
        _table = new ReservationTable(grid.CellCount, horizon);
        _nodeBudgetPerTick = nodeBudgetPerTick;
        _maxSearchesInFlight = maxSearchesInFlight;
        _fields = new FieldCache(grid, FieldCapacity);
    }

    /// <summary>
    /// Ticks elapsed, starting at zero and advanced once per <see cref="Tick"/> --
    /// the clock plans, reservations and every retry gate are indexed against, so
    /// nothing in this system means anything except relative to it.
    /// </summary>
    public int CurrentTick { get; private set; }

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
            Waiting: a.Cell != a.Goal && a.Search is null && a.RetryAfterTick > CurrentTick))];

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

        // The parking ring doubles as the passability check: an impassable
        // destination yields no ring and the order is refused as before.
        //
        // A GROUP's ring keeps doorways clear: no slot on or beside a
        // chokepoint. The gap fixture taught this the hard way -- the ring
        // included the gap's inner mouth, an early claimer parked in the
        // doorway, and the chamber sealed with the rest of the group outside.
        // A single unit is exempt: ordering one unit ONTO a doorway is intent.
        Func<int, bool>? keepDoorwaysClear = null;
        if (agents.Count > 1 && MapChokepoints.Count > 0)
        {
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

            keepDoorwaysClear = doorways.Contains;
        }

        var slots = GoalSpread.Nearest(_grid, goalCell, agents.Count, keepDoorwaysClear);
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
        foreach (var id in agents.OrderBy(id => id))
        {
            var agent = _agents[id];

            // A group member is NOT handed a parking slot here. It walks toward
            // the shared destination and claims the innermost open slot once it
            // gets NEAR -- the way a real team fills in on arrival rather than
            // pre-booking spots from across the map. A single-unit order is its
            // own claim: this unit, that cell, immediately.
            agent.Goal = slots[0];
            agent.HasSlot = agents.Count == 1;
            agent.FieldKey = slots[0];

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
        ElectLeader(group);
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
            if (agent.HasSlot)
            {
                _claimedGoals.Add(agent.Goal);
            }

            _occupiedCells.Add(agent.Cell);
            if (agent.Cell == agent.Goal)
            {
                _settledCells.Add(agent.Cell);
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
    private void ElectLeader(Group group)
    {
        var key = group.Members[0].FieldKey >= 0 ? group.Members[0].FieldKey : group.Members[0].Goal;
        var field = _fields.For(key);

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

    private bool ShouldStart(Agent agent) =>
        agent.Search is null &&
        agent.Cell != agent.Goal &&
        CurrentTick >= agent.RetryAfterTick &&
        (agent.WantsPlan || agent.Plan is null || agent.Plan.LastTick <= CurrentTick);

    private void Begin(Agent agent)
    {
        agent.WantsPlan = false;
        agent.LastPlanAttemptTick = CurrentTick;
        agent.AnchorTick = CurrentTick + agent.Latency;
        agent.Workspace = _workspacePool.Count > 0 ? _workspacePool.Pop() : new SearchWorkspace();

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

        Commit(agent, search.Result);
        Release(agent);
        return SearchOutcome.Committed;
    }

    private void Commit(Agent agent, PlanResult plan)
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

        agent.Plan = new PlanResult(cells, CurrentTick, plan.Cost, plan.Expanded, plan.Found);
        _table.Reserve(cells, CurrentTick, agent.Id);

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
    }

    private void Abandon(Agent agent)
    {
        if (agent.Search is null)
        {
            return;
        }

        Release(agent);
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
    /// What a <see cref="GroupDoctrine"/> may see and do — the whole seam.
    /// </summary>
    /// <remarks>
    /// Queries are O(1) against the per-tick caches; the mutations are the safe
    /// verbs and nothing else. A doctrine cannot touch plans, reservations, or
    /// the search, so no doctrine can break collision-freedom — the same
    /// argument the renderer seam makes about windowing, applied to movement
    /// policy.
    /// </remarks>
    public sealed class GroupOps
    {
        private readonly MovementSystem _system;
        private readonly Group _group;
        private readonly DistanceField _field;

        internal GroupOps(MovementSystem system, Group group)
        {
            _system = system;
            _group = group;

            var key = group.Members[0].FieldKey >= 0 ? group.Members[0].FieldKey : group.Destination;
            _field = system._fields.For(key);
            Members = [.. group.Members.Select(m => m.Id).OrderBy(id => id)];
        }

        /// <summary>
        /// The tick this pass is running for -- identical across every pass and every
        /// group in the tick, which is what makes a doctrine's own cooldowns
        /// comparable from one call to the next.
        /// </summary>
        public int CurrentTick => _system.CurrentTick;

        /// <summary>
        /// The cell the order was aimed at, after any snap off impassable ground: the
        /// centre the parking ring surrounds, and not necessarily any member's goal.
        /// <see cref="FieldCost"/> is measured to the ring's innermost slot, which is
        /// this cell except where the ring was pushed clear of a doorway.
        /// </summary>
        public int Destination => _group.Destination;

        /// <summary>The parking ring, innermost first.</summary>
        public IReadOnlyList<int> Slots => _group.Slots;

        /// <summary>Member ids, ascending.</summary>
        public IReadOnlyList<int> Members { get; }

        /// <summary>
        /// Every chokepoint on the MAP, not merely those between this group and its
        /// destination -- detected once for the system's life and shared by every
        /// group. A doctrine that wants the gate in its way filters these by
        /// <see cref="FieldCost"/>.
        /// </summary>
        public IReadOnlyList<Chokepoint> Chokepoints => _system.MapChokepoints;

        /// <summary>Exact distance from a cell to the destination, or infinity.</summary>
        public double FieldCost(int cell) => _field.CostFrom(cell);


        /// <summary>
        /// Where the member is standing. An O(1) indexed read, so a doctrine may call
        /// it inside a loop without thinking, and FIXED for the whole pass -- agents
        /// move after planning, never during a doctrine.
        /// </summary>
        public int CellOf(int id) => _system._agents[id].Cell;

        /// <summary>
        /// The cell the member is currently aimed at: its parking slot if it holds
        /// one, otherwise the ring's innermost slot it is walking toward. O(1), and
        /// LIVE -- a <see cref="ClaimSlot"/> earlier in this same pass shows up here.
        /// </summary>
        public int GoalOf(int id) => _system._agents[id].Goal;

        /// <summary>
        /// Consecutive replans that ended no nearer the goal -- failed attempts, not
        /// ticks waited, so a member sitting out a long backstop still reads 1. Reset
        /// to zero whenever a claim moves its goal.
        /// </summary>
        public int StalledReplans(int id) => _system._agents[id].StalledTicks;

        /// <summary>
        /// False while the member is still QUEUED: walking toward the ring with no
        /// cell of its own, because group members claim on approach rather than at
        /// order time. It is the flag the fill-like-water claiming turns on.
        /// </summary>
        public bool HasSlot(int id) => _system._agents[id].HasSlot;

        /// <summary>Is this cell some slot-holder's goal?</summary>
        public bool IsClaimed(int cell) => _system._claimedGoals.Contains(cell);

        /// <summary>
        /// The group member holding this cell as its slot, or -1. Ties cannot
        /// happen: a claim is exclusive.
        /// </summary>
        public int ClaimantOf(int cell)
        {
            foreach (var member in _group.Members)
            {
                if (member.HasSlot && member.Goal == cell)
                {
                    return member.Id;
                }
            }

            return -1;
        }

        /// <summary>
        /// Releases a member's claim and sends it back to the queue — it will
        /// claim again on approach, or reconcile in its turn.
        /// </summary>
        public void ReleaseSlot(int id)
        {
            var agent = _system._agents[id];
            if (!agent.HasSlot)
            {
                return;
            }

            agent.HasSlot = false;
            _system._claimedGoals.Remove(agent.Goal);
            agent.StalledTicks = 0;
            agent.RetryAfterTick = _system.CurrentTick;
            agent.WantsPlan = true;
            _system.Abandon(agent);
        }

        /// <summary>Is a unit parked on this cell (standing on its goal)?</summary>
        public bool IsSettled(int cell) => _system._settledCells.Contains(cell);

        /// <summary>
        /// Is anybody standing here -- this group's members and every other agent
        /// alike? An O(1) hit against the tick's occupancy snapshot, which no doctrine
        /// pass can change, since nothing moves until planning is done.
        /// </summary>
        public bool IsOccupied(int cell) => _system._occupiedCells.Contains(cell);

        /// <summary>
        /// Gives the member this cell as its parking slot: goal, claim, wake.
        /// Idempotent when the goal already matches.
        /// </summary>
        public void ClaimSlot(int id, int cell)
        {
            var agent = _system._agents[id];
            agent.HasSlot = true;

            if (agent.Goal == cell)
            {
                _system._claimedGoals.Add(cell);
                return;
            }

            _system._claimedGoals.Remove(agent.Goal);
            _system._claimedGoals.Add(cell);
            agent.Goal = cell;
            agent.StalledTicks = 0;
            agent.RetryAfterTick = _system.CurrentTick;
            agent.WantsPlan = true;
            _system.Abandon(agent);
        }

        /// <summary>Lets a gated member plan again now.</summary>
        public void Wake(int id)
        {
            var agent = _system._agents[id];
            agent.RetryAfterTick = Math.Min(agent.RetryAfterTick, _system.CurrentTick);
        }

        /// <summary>
        /// Keeps the member standing, quietly: no goal change, no search, no
        /// stall — just no replanning for a few ticks. Refresh it each tick to
        /// hold longer; a lapsed hold degrades to planning, never to frozen.
        /// </summary>
        public void Hold(int id, int ticks)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
            var agent = _system._agents[id];
            agent.RetryAfterTick = Math.Max(agent.RetryAfterTick, _system.CurrentTick + ticks);
        }

        /// <summary>
        /// Empty, unclaimed cells the given members can jointly WALK TO —
        /// settled units are walls — ordered by field distance then cell. One
        /// O(cells) sweep; call once per pass, not per member.
        /// </summary>
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
