namespace Nav.Core;

/// <param name="Id">Stable for the life of the system, and the planning order.</param>
/// <param name="Cell">Where the agent is now.</param>
/// <param name="Goal">Where it is trying to get to, or its own cell if it has no order.</param>
/// <param name="Arrived">Standing on its goal.</param>
/// <param name="Stuck">It has an order it cannot make any progress on.</param>
public readonly record struct AgentState(int Id, int Cell, int Goal, bool Arrived, bool Stuck);

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

        public bool Stuck { get; set; }
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
        [.. _agents.Select(a => new AgentState(a.Id, a.Cell, a.Goal, a.Cell == a.Goal, a.Stuck))];

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
            _agents[id].Stuck = false;
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

        if (plan.IsStuck)
        {
            // It is standing still, which is a fact others need. An agent with no
            // plan is invisible to everyone planning after it and to the collision
            // checker, which is how a stationary unit gets walked through.
            agent.Stuck = true;
            agent.Plan = new PlanResult([agent.Cell], CurrentTick, 0.0, plan.Expanded, Found: false);
        }
        else
        {
            agent.Stuck = false;
            agent.Plan = plan;
        }

        _table.Reserve(agent.Plan.Cells, agent.Plan.StartTick, agent.Id);
    }
}
