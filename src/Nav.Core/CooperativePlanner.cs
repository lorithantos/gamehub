namespace Nav.Core;

/// <summary>
/// A* over <c>(cell, tick)</c> instead of <c>cell</c>, respecting what other
/// agents have already reserved.
/// </summary>
/// <remarks>
/// This is <see cref="PathFinder"/> with one dimension added, and deliberately
/// little else. Three things differ:
/// <list type="number">
/// <item><description>A state is a cell <em>at a tick</em>, so the same cell at two
/// times is two states.</description></item>
/// <item><description>Waiting is a ninth action. Without it an agent cannot yield
/// right of way, and instances that are trivially solvable report as
/// unsolvable.</description></item>
/// <item><description>A neighbour must be unreserved as well as passable, and the
/// move must not swap through an agent coming the other way.</description></item>
/// </list>
/// <para>
/// The octile heuristic survives unchanged and stays admissible: it ignores
/// reservations and waiting, and both can only make a plan longer.
/// </para>
/// <para>
/// Search is bounded by the reservation window. Beyond it nothing is reserved, so
/// searching further would be ordinary A* with the cooperation switched off --
/// which is the planner silently answering a different question. A goal beyond the
/// window yields a partial plan instead, and the agent replans once the window has
/// moved.
/// </para>
/// </remarks>
public static class CooperativePlanner
{
    private const byte Unvisited = 0;
    private const byte Open = 1;
    private const byte Closed = 2;

    /// <summary>See <see cref="PathFinder"/>; the same reasoning applies.</summary>
    private const double Improvement = 1e-9;

    public static PlanResult FindPlan(
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

        if (!grid.IsPassable(start) || !grid.IsPassable(goal))
        {
            return PlanResult.Stuck(startTick, expanded: 0);
        }

        var baseTick = reservations.CurrentTick;
        var lastTick = baseTick + reservations.Horizon - 1;

        if (startTick > lastTick)
        {
            return PlanResult.Stuck(startTick, expanded: 0);
        }

        var width = grid.Width;
        var cellCount = grid.CellCount;

        workspace.EnsureCapacity(reservations.Horizon * cellCount);
        workspace.NextGeneration();

        var g = workspace.Cost;
        var parent = workspace.Parent;
        var state = workspace.State;
        var stamp = workspace.Stamp;
        var generation = workspace.Generation;
        var frontier = workspace.Frontier;

        var goalX = grid.ColumnOf(goal);
        var goalY = grid.RowOf(goal);

        var startState = ((startTick - baseTick) * cellCount) + start;
        var startH = Movement.OctileDistance(grid.ColumnOf(start), grid.RowOf(start), goalX, goalY);

        stamp[startState] = generation;
        g[startState] = 0.0;
        parent[startState] = -1;
        state[startState] = Open;
        frontier.Push(startState, startH, startH);

        // The closest the search got TO A CELL IT MAY REMAIN IN, kept so a goal
        // beyond the window still produces forward progress rather than nothing.
        // Starts unset: an agent standing where somebody else has already
        // reserved has nowhere valid to stop, including where it is.
        var bestState = -1;
        var bestH = double.PositiveInfinity;
        var bestCost = double.PositiveInfinity;

        var expanded = 0;

        while (frontier.Count > 0)
        {
            var current = frontier.Pop();
            if (state[current] == Closed)
            {
                continue;
            }

            state[current] = Closed;
            expanded++;

            var cell = current % cellCount;
            var tick = baseTick + (current / cellCount);
            var costSoFar = g[current];

            var x = grid.ColumnOf(cell);
            var y = grid.RowOf(cell);
            var h = Movement.OctileDistance(x, y, goalX, goalY);

            // Arriving is only arriving if the agent may stay. Reaching the goal
            // on a tick when somebody else needs it later is passing through, not
            // finishing, so the search carries on looking for a later arrival.
            if (cell == goal && reservations.IsHoldable(cell, tick, agent))
            {
                return Reconstruct(grid, parent, cellCount, current, startState, baseTick, expanded, found: true);
            }

            if ((h < bestH || (h == bestH && costSoFar < bestCost)) &&
                reservations.IsHoldable(cell, tick, agent))
            {
                bestState = current;
                bestH = h;
                bestCost = costSoFar;
            }

            if (tick >= lastTick)
            {
                // The window ends here. Anything further is unreserved and would
                // not be cooperative planning.
                continue;
            }

            // Waiting. No terrain check -- the agent is already standing here --
            // and no swap check, because staying put exchanges nothing.
            if (reservations.IsFree(cell, tick + 1, agent))
            {
                Relax(current, cell, tick + 1, costSoFar + Movement.WaitCost, x, y);
            }

            foreach (var step in Movement.Steps)
            {
                if (!Movement.IsLegalStep(grid, x, y, step.DeltaX, step.DeltaY))
                {
                    continue;
                }

                var nextX = x + step.DeltaX;
                var nextY = y + step.DeltaY;
                var next = (nextY * width) + nextX;

                if (!reservations.IsFree(next, tick + 1, agent) ||
                    reservations.IsSwap(cell, next, tick, agent))
                {
                    continue;
                }

                Relax(current, next, tick + 1, costSoFar + step.Cost, nextX, nextY);
            }
        }

        // Never reached the goal. Walk to whichever reachable cell got closest AND
        // may be stayed in; that is progress, and the agent replans when the
        // window moves. If there was no such cell, the agent is genuinely stuck
        // and saying so is better than producing a plan that parks in somebody
        // else's path.
        return bestState < 0
            ? PlanResult.Stuck(startTick, expanded)
            : Reconstruct(grid, parent, cellCount, bestState, startState, baseTick, expanded, found: false);

        void Relax(int from, int cell, int tick, double cost, int cellX, int cellY)
        {
            var next = ((tick - baseTick) * cellCount) + cell;
            var live = stamp[next] == generation;

            if (live && state[next] == Closed)
            {
                return;
            }

            if (live && cost + Improvement >= g[next])
            {
                return;
            }

            stamp[next] = generation;
            g[next] = cost;
            parent[next] = from;
            state[next] = Open;

            var h = Movement.OctileDistance(cellX, cellY, goalX, goalY);
            frontier.Push(next, cost + h, h);
        }
    }

    private static PlanResult Reconstruct(
        Grid grid,
        int[] parent,
        int cellCount,
        int endState,
        int startState,
        int baseTick,
        int expanded,
        bool found)
    {
        var states = new List<int>();
        for (var at = endState; at != -1; at = parent[at])
        {
            states.Add(at);
            if (at == startState)
            {
                break;
            }
        }

        states.Reverse();

        var cells = new int[states.Count];
        for (var i = 0; i < states.Count; i++)
        {
            cells[i] = states[i] % cellCount;
        }

        var cardinals = 0;
        var diagonals = 0;
        for (var i = 1; i < cells.Length; i++)
        {
            var deltaX = grid.ColumnOf(cells[i]) - grid.ColumnOf(cells[i - 1]);
            var deltaY = grid.RowOf(cells[i]) - grid.RowOf(cells[i - 1]);

            // A wait is priced as a cardinal step, so it counts in the same
            // bucket and the cost stays exact rather than summed.
            if (deltaX != 0 && deltaY != 0)
            {
                diagonals++;
            }
            else
            {
                cardinals++;
            }
        }

        var firstTick = baseTick + (states[0] / cellCount);
        return new PlanResult(cells, firstTick, Movement.ExactCost(cardinals, diagonals), expanded, found);
    }
}
