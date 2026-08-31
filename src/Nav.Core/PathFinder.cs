namespace Nav.Core;

/// <summary>
/// A* over an octile grid, producing optimal paths.
/// </summary>
public static class PathFinder
{
    private const byte Unvisited = 0;
    private const byte Open = 1;
    private const byte Closed = 2;

    /// <summary>
    /// How much better a route has to be before it is treated as an improvement.
    /// </summary>
    /// <remarks>
    /// Two routes of mathematically identical cost can differ in the last bit
    /// because their steps were summed in a different order. Without a floor,
    /// that noise reopens settled cells and inflates the expansion count for no
    /// gain. Octile costs on a grid are separated by at least sqrt(2)-1, so a
    /// floor eleven orders of magnitude below that discards nothing real.
    /// </remarks>
    private const double Improvement = 1e-9;

    /// <summary>
    /// Finds a cheapest path from <paramref name="start"/> to
    /// <paramref name="goal"/>, both flat cell indices.
    /// </summary>
    /// <remarks>
    /// An unreachable goal returns <see cref="PathResult.Found"/> false rather
    /// than throwing: "there is no path" is a routine answer in a game, not an
    /// exceptional one, and callers that have to catch it will eventually catch
    /// it in the wrong place.
    /// <para>
    /// State is three dense arrays sized to the grid -- <c>g</c>, <c>parent</c>
    /// and a per-cell state byte -- keyed by the flat index. No node objects, so
    /// the inner loop allocates nothing at all.
    /// </para>
    /// </remarks>
    public static PathResult FindPath(Grid grid, int start, int goal)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (!grid.IsPassable(start) || !grid.IsPassable(goal))
        {
            return PathResult.NotFound(expanded: 0);
        }

        if (start == goal)
        {
            return new PathResult([start], 0.0, Expanded: 0, Found: true);
        }

        var width = grid.Width;
        var cellCount = grid.CellCount;

        var g = new double[cellCount];
        Array.Fill(g, double.PositiveInfinity);
        var parent = new int[cellCount];
        Array.Fill(parent, -1);
        var state = new byte[cellCount];

        var goalX = grid.ColumnOf(goal);
        var goalY = grid.RowOf(goal);

        var frontier = new BinaryHeap();
        var startH = Movement.OctileDistance(grid.ColumnOf(start), grid.RowOf(start), goalX, goalY);
        g[start] = 0.0;
        state[start] = Open;
        frontier.Push(start, startH, startH);

        var expanded = 0;

        while (frontier.Count > 0)
        {
            var current = frontier.Pop();

            // Lazy deletion: improving a cell pushes a second entry rather than
            // repositioning the first, so a stale entry for an already-settled
            // cell is expected and is simply dropped here.
            if (state[current] == Closed)
            {
                continue;
            }

            state[current] = Closed;
            expanded++;

            if (current == goal)
            {
                return Reconstruct(grid, parent, goal, start, expanded);
            }

            var x = grid.ColumnOf(current);
            var y = grid.RowOf(current);
            var costSoFar = g[current];

            foreach (var step in Movement.Steps)
            {
                if (!Movement.IsLegalStep(grid, x, y, step.DeltaX, step.DeltaY))
                {
                    continue;
                }

                var next = ((y + step.DeltaY) * width) + x + step.DeltaX;

                // The octile heuristic is consistent for this cost model, so a
                // closed cell already holds its optimal g and cannot be improved.
                if (state[next] == Closed)
                {
                    continue;
                }

                var tentative = costSoFar + step.Cost;
                if (tentative + Improvement >= g[next])
                {
                    continue;
                }

                g[next] = tentative;
                parent[next] = current;
                state[next] = Open;

                var h = Movement.OctileDistance(x + step.DeltaX, y + step.DeltaY, goalX, goalY);
                frontier.Push(next, tentative + h, h);
            }
        }

        return PathResult.NotFound(expanded);
    }

    /// <summary>
    /// Walks the parent chain back from the goal and reports the cost from the
    /// step counts rather than from the accumulated <c>g</c>.
    /// </summary>
    /// <remarks>
    /// The search has to accumulate as it goes -- that is what orders the
    /// frontier -- but the number that faces the benchmark oracle is better
    /// computed the way the benchmark computes it: cardinals plus diagonals times
    /// sqrt(2), with no summation's worth of rounding in between.
    /// </remarks>
    private static PathResult Reconstruct(Grid grid, int[] parent, int goal, int start, int expanded)
    {
        var cells = new List<int>();
        for (var cell = goal; cell != -1; cell = parent[cell])
        {
            cells.Add(cell);
            if (cell == start)
            {
                break;
            }
        }

        cells.Reverse();

        var cardinals = 0;
        var diagonals = 0;
        for (var i = 1; i < cells.Count; i++)
        {
            var deltaX = grid.ColumnOf(cells[i]) - grid.ColumnOf(cells[i - 1]);
            var deltaY = grid.RowOf(cells[i]) - grid.RowOf(cells[i - 1]);
            if (deltaX != 0 && deltaY != 0)
            {
                diagonals++;
            }
            else
            {
                cardinals++;
            }
        }

        return new PathResult(cells, Movement.ExactCost(cardinals, diagonals), expanded, Found: true);
    }
}
