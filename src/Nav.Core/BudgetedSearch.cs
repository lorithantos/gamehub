namespace Nav.Core;

/// <summary>
/// One space-time search, interruptible and resumable.
/// </summary>
/// <remarks>
/// Milestone 1 measured a single search expanding 594,349 nodes on a
/// 1,048,576-cell map. At any realistic node rate that is more than a 60Hz frame:
/// one unit, one order, one dropped frame, before any multi-agent work is added.
/// So a search has to be something a caller can stop and come back to.
/// <para>
/// <b>This is the only implementation of the search.</b>
/// <see cref="CooperativePlanner.FindPlan"/> is a wrapper that runs it to
/// completion in one call. Criterion 7 -- that a plan built in fifty-node
/// instalments is identical to the same plan built in one go -- is therefore true
/// by construction rather than by testing: there is no second code path to
/// diverge. The tests still assert it, because a claim of "by construction" is
/// worth checking too.
/// </para>
/// <para>
/// A SUSPENDED SEARCH OWNS ITS WORKSPACE. The frontier and the state arrays live
/// there across calls, so handing the same workspace to another search before
/// this one finishes corrupts both. Budget the number of searches in flight
/// accordingly.
/// </para>
/// </remarks>
internal sealed class BudgetedSearch
{
    private const byte Open = 1;
    private const byte Closed = 2;

    /// <summary>See <see cref="PathFinder"/>; the same reasoning applies.</summary>
    private const double Improvement = 1e-9;

    private readonly Grid _grid;
    private readonly IReservationView _reservations;
    private readonly int _agent;
    private readonly int _goal;
    private readonly int _baseTick;
    private readonly int _lastTick;
    private readonly int _cellCount;
    private readonly int _width;
    private readonly int _goalX;
    private readonly int _goalY;
    private readonly int _startState;
    private readonly int _generation;

    private readonly double[] _cost;
    private readonly int[] _parent;
    private readonly byte[] _state;
    private readonly int[] _stamp;
    private readonly BinaryHeap _frontier;
    private readonly DistanceField? _field;
    private readonly double _fieldAtGoal;

    private int _bestState = -1;
    private double _bestH = double.PositiveInfinity;
    private double _bestCost = double.PositiveInfinity;
    private int _expanded;

    /// <param name="grid">
    /// The map. It must be sized for the same cell count
    /// <paramref name="reservations"/> was built for -- the two share flat cell
    /// indices, and a mismatch surfaces as an out-of-range reservation query
    /// rather than a wrong answer.
    /// </param>
    /// <param name="reservations">
    /// What everyone else has claimed, and also the clock: the search may plan
    /// from <c>CurrentTick</c> up to <c>CurrentTick + Horizon - 1</c> and no
    /// further, and anything past that edge simply reads as free.
    /// </param>
    /// <param name="agent">
    /// Who is planning. Not negative. Its own reservations never block it, so a
    /// replan is not obstructed by the plan it is about to replace.
    /// </param>
    /// <param name="start">
    /// Flat index of the cell the agent is standing on. Impassable and the search
    /// is finished stuck before a single node is expanded.
    /// </param>
    /// <param name="goal">
    /// Flat index of the cell it is trying to reach. Impassable finishes it stuck;
    /// equal to <paramref name="start"/> finishes it at once with a one-cell plan.
    /// </param>
    /// <param name="startTick">
    /// The tick at which the agent is on <paramref name="start"/>. Behind the
    /// reservation window it throws; past the window's last tick the search is
    /// finished stuck.
    /// </param>
    /// <param name="workspace">
    /// Scratch, grown here to <c>Horizon * CellCount</c> entries because a state
    /// is a cell AND a tick. It belongs to this search until the search finishes
    /// -- see the remarks on the type.
    /// </param>
    /// <param name="field">
    /// Optional heuristic source: a <see cref="DistanceField"/> for the order's
    /// destination, shared by the whole group. The agent's own goal need not be
    /// the field's destination — the heuristic is shaded by the triangle
    /// inequality, <c>h(c) = max(octile, field(c) − field(goal))</c>, which is
    /// admissible and consistent for any goal and collapses expansions where the
    /// field's destination and the goal agree. Without a field, octile as ever.
    /// A field only sharpens the estimate; it never changes what is reachable,
    /// so every reservation and legality rule binds exactly as before.
    /// </param>
    public BudgetedSearch(
        Grid grid,
        IReservationView reservations,
        int agent,
        int start,
        int goal,
        int startTick,
        SearchWorkspace workspace,
        DistanceField? field = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(reservations);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentOutOfRangeException.ThrowIfNegative(agent);

        if (startTick < reservations.CurrentTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTick), startTick, $"The window begins at tick {reservations.CurrentTick}.");
        }

        _grid = grid;
        _reservations = reservations;
        _agent = agent;
        _goal = goal;
        _cellCount = grid.CellCount;
        _width = grid.Width;
        _baseTick = reservations.CurrentTick;
        _lastTick = _baseTick + reservations.Horizon - 1;
        _goalX = grid.ColumnOf(goal);
        _goalY = grid.RowOf(goal);
        _field = field;
        _fieldAtGoal = field?.CostFrom(goal) ?? double.PositiveInfinity;

        // Everything decidable before the first node is expanded is decided here,
        // so a caller that only ever calls Advance still gets these answers.
        if (!grid.IsPassable(start) || !grid.IsPassable(goal) || startTick > _lastTick)
        {
            Finish(PlanResult.Stuck(startTick, expanded: 0));
        }
        else if (start == goal && reservations.IsHoldable(goal, startTick, agent))
        {
            // Standing on the goal is only arriving if the agent may STAY there,
            // which is the same rule the search loop applies to every other
            // arrival. Without the holdability check this exit declared a one-cell
            // plan found and Commit parked the agent on a cell somebody else held a
            // tick later. It surfaced when a heap swap changed A*'s tie-breaking:
            // in the throng scenario agent 13's old route left it standing on its
            // freshly claimed goal at exactly its anchor tick while agent 16 held
            // that cell for the following tick, and both stood on it at tick 19.
            // When the goal cannot be held, the search runs and finds a plan that
            // steps aside and comes back, or waits somewhere it is allowed to.
            Finish(new PlanResult([start], startTick, 0.0, Expanded: 0, Found: true));
        }

        workspace.EnsureCapacity(reservations.Horizon * _cellCount);
        workspace.NextGeneration();

        // Cached AFTER EnsureCapacity, which may have replaced them.
        _cost = workspace.Cost;
        _parent = workspace.Parent;
        _state = workspace.State;
        _stamp = workspace.Stamp;
        _frontier = workspace.Frontier;
        _generation = workspace.Generation;

        _startState = ((startTick - _baseTick) * _cellCount) + start;

        if (Finished)
        {
            return;
        }

        var startH = Heuristic(start, grid.ColumnOf(start), grid.RowOf(start));
        _stamp[_startState] = _generation;
        _cost[_startState] = 0.0;
        _parent[_startState] = -1;
        _state[_startState] = Open;
        _frontier.Push(_startState, startH, startH);
    }

    /// <summary>True once <see cref="Result"/> is the answer.</summary>
    public bool Finished { get; private set; }

    /// <summary>Meaningful only once <see cref="Finished"/>.</summary>
    public PlanResult Result { get; private set; } = PlanResult.Stuck(0, 0);

    /// <summary>Nodes closed so far, across every <see cref="Advance"/>.</summary>
    public int Expanded => _expanded;

    /// <summary>
    /// Has this search reached <paramref name="cell"/> at any tick in its window
    /// -- opened or closed? If so, its answer may run through that cell on the
    /// strength of a table that has since changed.
    /// </summary>
    /// <remarks>
    /// The question a park has to ask. A search reads the table as it expands,
    /// so anything it has NOT yet reached will see a cell parked on and route
    /// around it; but a state already in its frontier was priced when the cell
    /// was free, and a popped state is not re-checked against the table. A
    /// suspended search that has touched the cell can therefore commit a plan
    /// straight through a unit that stopped there after it looked, and the
    /// table's own assertion is what catches it -- as it did, at arena scale,
    /// the first time a doctrine parked a whole crust at once. Stamps are per
    /// state, so this is one comparison per tick of the window.
    /// </remarks>
    internal bool Touches(int cell)
    {
        if (Finished)
        {
            return Result.Cells.Contains(cell);
        }

        for (var tick = _baseTick; tick <= _lastTick; tick++)
        {
            if (_stamp[((tick - _baseTick) * _cellCount) + cell] == _generation)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Expands at most <paramref name="nodeBudget"/> nodes.
    /// </summary>
    /// <returns>True when the search is over, false when there is more to do.</returns>
    public bool Advance(int nodeBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeBudget);

        if (Finished)
        {
            return true;
        }

        var spent = 0;

        while (_frontier.Count > 0)
        {
            if (spent >= nodeBudget)
            {
                return false;
            }

            var current = _frontier.Pop();

            // Lazy deletion: a stale entry for an already-settled cell is expected
            // and costs nothing but the pop, so it is not charged to the budget.
            if (_state[current] == Closed)
            {
                continue;
            }

            _state[current] = Closed;
            _expanded++;
            spent++;

            var cell = current % _cellCount;
            var tick = _baseTick + (current / _cellCount);
            var costSoFar = _cost[current];

            var x = _grid.ColumnOf(cell);
            var y = _grid.RowOf(cell);
            var h = Heuristic(cell, x, y);

            // Arriving is only arriving if the agent may stay.
            if (cell == _goal && _reservations.IsHoldable(cell, tick, _agent))
            {
                Finish(Reconstruct(current, found: true));
                return true;
            }

            if ((h < _bestH || (h == _bestH && costSoFar < _bestCost)) &&
                _reservations.IsHoldable(cell, tick, _agent))
            {
                _bestState = current;
                _bestH = h;
                _bestCost = costSoFar;
            }

            if (tick >= _lastTick)
            {
                continue;
            }

            if (_reservations.IsFree(cell, tick + 1, _agent))
            {
                Relax(current, cell, tick + 1, costSoFar + Movement.WaitCost, x, y);
            }

            foreach (var step in Movement.Steps)
            {
                if (!Movement.IsLegalStep(_grid, x, y, step.DeltaX, step.DeltaY))
                {
                    continue;
                }

                var nextX = x + step.DeltaX;
                var nextY = y + step.DeltaY;
                var next = (nextY * _width) + nextX;

                if (!_reservations.IsFree(next, tick + 1, _agent) ||
                    _reservations.IsSwap(cell, next, tick, _agent))
                {
                    continue;
                }

                Relax(current, next, tick + 1, costSoFar + step.Cost, nextX, nextY);
            }
        }

        Finish(_bestState < 0
            ? PlanResult.Stuck(_baseTick + (_startState / _cellCount), _expanded)
            : Reconstruct(_bestState, found: false));

        return true;
    }

    /// <summary>Runs to completion in one call.</summary>
    public PlanResult RunToCompletion()
    {
        while (!Advance(int.MaxValue))
        {
            // Advance only returns false on an exhausted budget, and int.MaxValue
            // is not going to exhaust. The loop is here so the contract holds
            // rather than because it is expected to spin.
        }

        return Result;
    }

    private void Finish(PlanResult result)
    {
        Result = result;
        Finished = true;
    }

    private void Relax(int from, int cell, int tick, double cost, int cellX, int cellY)
    {
        var next = ((tick - _baseTick) * _cellCount) + cell;
        var live = _stamp[next] == _generation;

        if (live && _state[next] == Closed)
        {
            return;
        }

        if (live && cost + Improvement >= _cost[next])
        {
            return;
        }

        _stamp[next] = _generation;
        _cost[next] = cost;
        _parent[next] = from;
        _state[next] = Open;

        var h = Heuristic(cell, cellX, cellY);
        _frontier.Push(next, cost + h, h);
    }

    /// <summary>
    /// The remaining-distance estimate: octile alone, or the maximum of octile
    /// and the field's triangle-shaded distance when a field is present. Both
    /// components are admissible and consistent, so their maximum is too — and
    /// dominates each, which is the whole point of carrying the field.
    /// </summary>
    private double Heuristic(int cell, int x, int y)
    {
        var octile = Movement.OctileDistance(x, y, _goalX, _goalY);
        if (_field is null || double.IsPositiveInfinity(_fieldAtGoal))
        {
            return octile;
        }

        var fromCell = _field.CostFrom(cell);
        if (double.IsPositiveInfinity(fromCell))
        {
            // The cell cannot reach the field's destination. If the goal can,
            // the cell cannot reach the goal either -- but that is the search's
            // discovery to make; the heuristic just declines to help.
            return octile;
        }

        return Math.Max(octile, fromCell - _fieldAtGoal);
    }

    private PlanResult Reconstruct(int endState, bool found)
    {
        var states = new List<int>();
        for (var at = endState; at != -1; at = _parent[at])
        {
            states.Add(at);
            if (at == _startState)
            {
                break;
            }
        }

        states.Reverse();

        var cells = new int[states.Count];
        for (var i = 0; i < states.Count; i++)
        {
            cells[i] = states[i] % _cellCount;
        }

        var cardinals = 0;
        var diagonals = 0;
        for (var i = 1; i < cells.Length; i++)
        {
            var deltaX = _grid.ColumnOf(cells[i]) - _grid.ColumnOf(cells[i - 1]);
            var deltaY = _grid.RowOf(cells[i]) - _grid.RowOf(cells[i - 1]);

            // A wait is priced as a cardinal step, so it counts in the same bucket
            // and the cost stays exact rather than summed.
            if (deltaX != 0 && deltaY != 0)
            {
                diagonals++;
            }
            else
            {
                cardinals++;
            }
        }

        var firstTick = _baseTick + (states[0] / _cellCount);
        return new PlanResult(cells, firstTick, Movement.ExactCost(cardinals, diagonals), _expanded, found);
    }
}
