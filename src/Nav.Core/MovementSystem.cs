namespace Nav.Core;

/// <param name="Id">Stable for the life of the system, and the planning order.</param>
/// <param name="Cell">Where the agent is now.</param>
/// <param name="Goal">Where it is trying to get to, or its own cell if it has no order.</param>
/// <param name="Arrived">Standing on its goal.</param>
/// <param name="StalledTicks">
/// Consecutive replans that got it no closer to its goal.
/// </param>
/// <remarks>
/// <see cref="Stuck"/> means NO PROGRESS, not "no plan". The distinction was
/// worth a bug: an agent that can stand still always has a plan — the one-cell
/// plan of staying put — so a check for "did the planner return anything" reports
/// two agents deadlocked nose to nose in a corridor as perfectly healthy. They
/// have plans. The plans go nowhere.
/// </remarks>
public readonly record struct AgentState(int Id, int Cell, int Goal, bool Arrived, int StalledTicks)
{
    /// <summary>Has an order it is making no progress on.</summary>
    public bool Stuck => !Arrived && StalledTicks > 0;
}

/// <summary>
/// Agents, their orders, and the tick that moves them.
/// </summary>
/// <remarks>
/// Planning is windowed, so an agent does not plan once and follow it forever: it
/// plans as far as the reservation horizon reaches and replans when it gets there.
/// That is what keeps the cost of a tick bounded, and it is why a partial plan is
/// a normal outcome rather than a failure.
/// <para>
/// Agents plan in id order. An arbitrary but FIXED order is what makes a tick
/// reproducible, which every acceptance criterion downstream depends on. Priority
/// schemes that change the order are a later milestone's problem.
/// </para>
/// </remarks>
public sealed class MovementSystem
{
    private sealed class Agent(int id, int cell)
    {
        public int Id { get; } = id;

        public int Cell { get; set; } = cell;

        public int Goal { get; set; } = cell;

        public PlanResult? Plan { get; set; }

        public int StalledTicks { get; set; }
    }

    private readonly Grid _grid;
    private readonly ReservationTable _table;
    private readonly SearchWorkspace _workspace = new();
    private readonly List<Agent> _agents = [];

    public MovementSystem(Grid grid, int horizon = 32)
    {
        ArgumentNullException.ThrowIfNull(grid);

        _grid = grid;
        _table = new ReservationTable(grid.CellCount, horizon);
    }

    /// <summary>The tick the system is on. Agents are where their plans say at this tick.</summary>
    public int CurrentTick { get; private set; }

    /// <summary>Nodes expanded across every plan ever made. A cost measure.</summary>
    public long TotalExpanded { get; private set; }

    public IReadOnlyList<AgentState> Agents =>
        [.. _agents.Select(a => new AgentState(a.Id, a.Cell, a.Goal, a.Cell == a.Goal, a.StalledTicks))];

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
    public void Order(IReadOnlyList<int> agents, int goalCell)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var squad = agents.Select(id => (Agent: id, Cell: _agents[id].Cell)).ToArray();
        foreach (var (id, goal) in GoalSpread.Assign(_grid, goalCell, squad))
        {
            _agents[id].Goal = goal;
            _agents[id].Plan = null;
            _agents[id].StalledTicks = 0;
        }
    }

    /// <summary>Advances one tick: replan whoever needs it, then move everybody.</summary>
    public void Tick()
    {
        foreach (var agent in _agents)
        {
            if (NeedsPlan(agent))
            {
                Replan(agent);
            }
        }

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

    private bool NeedsPlan(Agent agent)
    {
        if (agent.Cell == agent.Goal)
        {
            return false;
        }

        // No plan, or one that runs out at or before the tick about to happen.
        return agent.Plan is null || agent.Plan.LastTick <= CurrentTick;
    }

    private void Replan(Agent agent)
    {
        // Released first so the plan being replaced does not constrain its own
        // replacement. Nothing else is planning at this instant, so no other
        // agent can slip into the gap.
        _table.Release(agent.Id);

        var plan = CooperativePlanner.FindPlan(
            _grid, _table, agent.Id, agent.Cell, agent.Goal, CurrentTick, _workspace);

        TotalExpanded += plan.Expanded;

        // A plan that cannot even stand still is standing still anyway, and that
        // is a fact others need: an agent with no plan is invisible to everyone
        // planning after it and to the collision checker, which is how a
        // stationary unit gets walked through.
        agent.Plan = plan.IsStuck
            ? new PlanResult([agent.Cell], CurrentTick, 0.0, plan.Expanded, Found: false)
            : plan;

        // Progress is measured against the GOAL, not against whether a plan came
        // back. Two agents deadlocked nose to nose both have plans -- the one-cell
        // plan of staying put -- and would otherwise report as healthy forever.
        var before = DistanceToGoal(agent, agent.Cell);
        var after = DistanceToGoal(agent, agent.Plan.Cells[^1]);
        agent.StalledTicks = after < before - 1e-9 ? 0 : agent.StalledTicks + 1;

        _table.Reserve(agent.Plan.Cells, agent.Plan.StartTick, agent.Id);
    }

    private double DistanceToGoal(Agent agent, int from) =>
        Movement.OctileDistance(
            _grid.ColumnOf(from), _grid.RowOf(from),
            _grid.ColumnOf(agent.Goal), _grid.RowOf(agent.Goal));
}
