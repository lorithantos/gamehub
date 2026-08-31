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
public sealed class BudgetedSearch
{
    private const byte Open = 1;
    private const byte Closed = 2;

    /// <summary>See <see cref="PathFinder"/>; the same reasoning applies.</summary>
    private const double Improvement = 1e-9;

    private readonly Grid _grid;
    private readonly ReservationTable _reservations;
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

    private int _bestState = -1;
    private double _bestH = double.PositiveInfinity;
    private double _bestCost = double.PositiveInfinity;
    private int _expanded;

    public BudgetedSearch(
        Grid grid,
        ReservationTable reservations,
        int agent,
        int start,
        int goal,
        int startTick,
        SearchWorkspace workspace)
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

        // Everything decidable before the first node is expanded is decided here,
        // so a caller that only ever calls Advance still gets these answers.
        if (!grid.IsPassable(start) || !grid.IsPassable(goal) || startTick > _lastTick)
        {
            Finish(PlanResult.Stuck(startTick, expanded: 0));
        }
        else if (start == goal)
        {
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

        var startH = Movement.OctileDistance(grid.ColumnOf(start), grid.RowOf(start), _goalX, _goalY);
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
            var h = Movement.OctileDistance(x, y, _goalX, _goalY);

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

        var h = Movement.OctileDistance(cellX, cellY, _goalX, _goalY);
        _frontier.Push(next, cost + h, h);
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
