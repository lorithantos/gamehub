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

    /// <summary>How long a stalled agent waits before trying again.</summary>
    /// <remarks>
    /// Two agents deadlocked in a corridor replanned every tick for sixty ticks and
    /// spent 14,266 search nodes against 126 for a scenario that actually
    /// completed. Retrying a hopeless search every tick is not persistence, it is
    /// the whole budget spent on the one agent that cannot use it.
    /// </remarks>
    private const int StallBackoffTicks = 8;

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
    }

    private readonly Grid _grid;
    private readonly ReservationTable _table;
    private readonly List<Agent> _agents = [];
    private readonly Stack<SearchWorkspace> _workspacePool = new();
    private readonly int _nodeBudgetPerTick;
    private readonly int _maxSearchesInFlight;

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
        foreach (var (id, goal) in GoalSpread.Assign(_grid, goalCell, squad))
        {
            var agent = _agents[id];
            agent.Goal = goal;
            agent.StalledTicks = 0;
            agent.RetryAfterTick = 0;
            agent.WantsPlan = true;
            Abandon(agent);
        }
    }

    /// <summary>Advances one tick: spend the planning budget, then move everybody.</summary>
    public void Tick()
    {
        LastTick = SpendPlanningBudget();

        CurrentTick++;
        _table.Advance();

        foreach (var agent in _agents)
        {
            var at = agent.Plan?.CellAt(CurrentTick) ?? -1;
            if (at >= 0)
            {
                agent.Cell = at;
            }
        }
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

        // It holds its cell while it thinks. A unit deciding where to go is still
        // standing somewhere, and everyone planning meanwhile has to see that.
        _table.Reserve([agent.Cell], CurrentTick, agent.Id);

        agent.Search = new BudgetedSearch(
            _grid, _table, agent.Id, agent.Cell, agent.Goal, agent.AnchorTick, agent.Workspace);
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
                // stall and a backoff rather than another attempt next tick.
                agent.StalledTicks++;
                agent.RetryAfterTick = CurrentTick + StallBackoffTicks;
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
        // The agent stands where it is until the anchor, then follows the plan.
        // Splicing the two here means CellAt answers for every tick in between,
        // and one Reserve call covers the whole future rather than two that would
        // each replace the other.
        var pad = agent.AnchorTick - CurrentTick;
        var cells = new List<int>(pad + plan.Cells.Count);
        for (var i = 0; i < pad; i++)
        {
            cells.Add(agent.Cell);
        }

        cells.AddRange(plan.IsStuck ? [agent.Cell] : plan.Cells);

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
            agent.StalledTicks++;
            agent.RetryAfterTick = CurrentTick + StallBackoffTicks;
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
