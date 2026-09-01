namespace Nav.Core;

/// <param name="Id">Stable for the life of the system, and the planning order.</param>
/// <param name="Cell">Where the agent is now.</param>
/// <param name="Goal">Where it is trying to get to, or its own cell if it has no order.</param>
/// <param name="Arrived">Standing on its goal.</param>
/// <param name="StalledTicks">Consecutive replans that got it no closer to its goal.</param>
/// <param name="Thinking">A search is in flight for this agent; it holds position until it lands.</param>
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
    bool Thinking)
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
/// Agents plan in id order. An arbitrary but FIXED order is what makes a tick
/// reproducible, which every acceptance criterion downstream depends on.
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

    private sealed class Agent(int id, int cell)
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

        public bool WantsPlan { get; set; }

        /// <summary>
        /// The order's destination, which keys the shared distance field. The
        /// assigned <see cref="Goal"/> is per-agent; the field is per-order, so
        /// a whole group shares one. -1 until the first order.
        /// </summary>
        public int FieldKey { get; set; } = -1;

        /// <summary>The order this agent last belonged to, or null.</summary>
        public Group? Group { get; set; }
    }

    /// <summary>
    /// One order's members, for reconciliation and the leader. Assignment is a
    /// snapshot but settling is a process; the group is what reconciles the two.
    /// </summary>
    private sealed class Group
    {
        public required int Destination { get; init; }

        public required List<Agent> Members { get; init; }

        // Far enough in the past to always fire, small enough that the
        // subtraction in the hysteresis check cannot overflow.
        public int LastReconcileTick { get; set; } = -1_000_000;

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
        _fields = new FieldCache(grid, capacity: 8);
    }

    public int CurrentTick { get; private set; }

    /// <summary>Nodes expanded across every plan ever made. A cost measure.</summary>
    public long TotalExpanded { get; private set; }

    /// <summary>What the last <see cref="Tick"/> cost.</summary>
    public TickReport LastTick { get; private set; }

    public IReadOnlyList<AgentState> Agents =>
        [.. _agents.Select(a => new AgentState(
            a.Id, a.Cell, a.Goal, a.Cell == a.Goal, a.StalledTicks, a.Search is not null))];

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
    public void Order(IReadOnlyList<int> agents, int goalCell)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var squad = agents.Select(id => (Agent: id, Cell: _agents[id].Cell)).ToArray();
        var assigned = GoalSpread.Assign(_grid, goalCell, squad);
        if (assigned.Count == 0)
        {
            return;
        }

        var group = new Group { Destination = goalCell, Members = [] };
        foreach (var (id, goal) in assigned)
        {
            var agent = _agents[id];
            agent.Goal = goal;

            // The FIELD is keyed by the order's destination, so the whole group
            // shares one; the assigned goal stays per-agent. An impassable
            // destination (a click on a wall) falls back to the assigned goal,
            // which GoalSpread guarantees passable.
            agent.FieldKey = _grid.IsPassable(goalCell) ? goalCell : goal;

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
        Reconcile();

        LastTick = SpendPlanningBudget();

        CurrentTick++;
        _table.Advance();

        var anyArrived = false;
        foreach (var agent in _agents)
        {
            var at = agent.Plan?.CellAt(CurrentTick) ?? -1;
            if (at >= 0)
            {
                var wasArrived = agent.Cell == agent.Goal;
                agent.Cell = at;
                anyArrived |= !wasArrived && agent.Cell == agent.Goal;
            }
        }

        if (anyArrived)
        {
            foreach (var agent in _agents)
            {
                if (agent.StalledTicks > 0 && agent.Cell != agent.Goal)
                {
                    agent.RetryAfterTick = CurrentTick;
                }
            }
        }
    }

    /// <summary>
    /// The milestone-2 defect, fixed where it lives: goal assignment is a
    /// snapshot but a group's settling is a process. When a member stalls, the
    /// not-yet-arrived members are re-assigned from their CURRENT positions,
    /// with every settled unit's cell off the table — so a stuck unit either
    /// gets a goal it can actually reach, or gets its own cell and arrives in
    /// place, and either way stops burning budget on a goal frozen inside the
    /// pile.
    /// </summary>
    /// <remarks>
    /// Hysteresis (once per <see cref="StallBackstopTicks"/>/8 per group) keeps
    /// reassignment from becoming churn; fixed group order and id-ordered
    /// members keep it deterministic. A goal change is a wake: exactly the
    /// members whose goals moved are replanned, nobody else.
    /// </remarks>
    private void Reconcile()
    {
        foreach (var group in _groups)
        {
            // Groups of one are exempt on principle: an individually addressed
            // order means THIS unit, THERE, and reassigning it falsifies the
            // player's intent. Reconciliation is a group-order semantic.
            if (group.Members.Count < 2 ||
                CurrentTick - group.LastReconcileTick < StallBackstopTicks / 8)
            {
                continue;
            }

            // Only HARD-stalled members are re-goaled: two failed replans, not
            // one. A single no-progress replan is usually traffic, and a first
            // version that reassigned on any stall re-goaled transiently blocked
            // units constantly -- arrivals froze while goals played musical
            // chairs on an 8-tick beat. Stalled members are also STANDING
            // members, which is what makes giving one its own cell an arrival
            // rather than a contradiction.
            var stalled = group.Members
                .Where(m => m.StalledTicks >= 2 && m.Cell != m.Goal)
                .OrderBy(m => m.Id)
                .ToArray();
            if (stalled.Length == 0)
            {
                continue;
            }

            group.LastReconcileTick = CurrentTick;
            ReconcileGroup(group, stalled);
            ElectLeader(group);
        }
    }

    /// <summary>
    /// Re-goals a group's hard-stalled members onto spots they can ACTUALLY
    /// WALK TO. One multi-source breadth-first sweep from all stalled members
    /// at once (settled units' cells are walls), then closest member takes
    /// closest spot by field distance. A member with no reachable empty spot
    /// takes its own cell — a hopeless jam becomes an honest arrival in place,
    /// which is what a real crowd does when the destination is full: it stops
    /// where it meets the mass.
    /// </summary>
    /// <remarks>
    /// One O(cells) sweep per firing, deliberately: the first version ran a
    /// breadth-first search per member and put p99 tick cost at 51 ms — the
    /// frame ceiling criterion exists precisely to catch that. Assigning by
    /// raw distance without the reachability sweep is worse than slow: the
    /// 200-agent run froze at 129 arrivals with 71 units expensively probing
    /// goals the arrived crust had sealed shut.
    /// </remarks>
    private void ReconcileGroup(Group group, Agent[] stalled)
    {
        var key = group.Members[0].FieldKey >= 0 ? group.Members[0].FieldKey : group.Destination;
        var field = _fields.For(key);

        // Flat maps, not sets: the sweep touches every cell a few times.
        var claimed = new bool[_grid.CellCount];
        var settled = new bool[_grid.CellCount];
        var occupied = new bool[_grid.CellCount];
        foreach (var agent in _agents)
        {
            claimed[agent.Goal] = true;
            occupied[agent.Cell] = true;
            if (agent.Cell == agent.Goal)
            {
                settled[agent.Cell] = true;
            }
        }

        // Multi-source BFS over what the stalled crowd can jointly reach.
        var seen = new bool[_grid.CellCount];
        var queue = new Queue<int>();
        foreach (var member in stalled)
        {
            seen[member.Cell] = true;
            queue.Enqueue(member.Cell);
        }

        var spots = new List<int>();
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();

            // A spot must be EMPTY and unclaimed: handing out a cell somebody
            // stands on re-creates the frozen-goal wait.
            if (!claimed[cell] && !occupied[cell] && field.Reaches(cell))
            {
                spots.Add(cell);
            }

            var x = _grid.ColumnOf(cell);
            var y = _grid.RowOf(cell);
            foreach (var step in Movement.Steps)
            {
                if (!Movement.IsLegalStep(_grid, x, y, step.DeltaX, step.DeltaY))
                {
                    continue;
                }

                var next = ((y + step.DeltaY) * _grid.Width) + x + step.DeltaX;
                if (seen[next] || settled[next])
                {
                    continue;
                }

                seen[next] = true;
                queue.Enqueue(next);
            }
        }

        // Closest member takes closest spot, both by field distance, ties on
        // cell/id — deterministic, and it fills the crust from the inside out.
        spots.Sort((a, b) =>
        {
            var byCost = field.CostFrom(a).CompareTo(field.CostFrom(b));
            return byCost != 0 ? byCost : a.CompareTo(b);
        });

        var members = stalled
            .OrderBy(m => field.CostFrom(m.Cell))
            .ThenBy(m => m.Id)
            .ToArray();

        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            var pick = i < spots.Count ? spots[i] : (claimed[member.Cell] ? -1 : member.Cell);
            if (pick < 0 || pick == member.Goal)
            {
                continue;
            }

            // Never move a member's goal FARTHER from the destination than its
            // own position: beyond the crust there is nothing to gain, and a
            // member whose best spot is worse than standing arrives in place.
            if (pick != member.Cell &&
                field.CostFrom(pick) > field.CostFrom(member.Cell) &&
                !claimed[member.Cell])
            {
                pick = member.Cell;
                if (pick == member.Goal)
                {
                    continue;
                }
            }

            claimed[member.Goal] = false;
            claimed[pick] = true;
            member.Goal = pick;
            member.StalledTicks = 0;
            member.RetryAfterTick = CurrentTick;
            member.WantsPlan = true;
            Abandon(member);
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
            // No progress. Wait for an event -- an arrival, an order, a
            // reconciliation -- rather than re-asking an unchanged world on a
            // short timer; the backstop turns a missed wake into slow, not
            // frozen.
            agent.StalledTicks++;
            agent.RetryAfterTick = CurrentTick + StallBackstopTicks;
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
}
