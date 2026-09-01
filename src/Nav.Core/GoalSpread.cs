namespace Nav.Core;

/// <summary>
/// Turns "everyone go there" into one distinct destination per agent.
/// </summary>
/// <remarks>
/// Twelve units cannot stand on one cell. A group order that gives them all the
/// same goal has exactly one winner and eleven agents that wait forever for a cell
/// somebody is standing on — and because an arrived agent holds its cell for the
/// rest of the window, they wait forever rather than briefly.
/// <para>
/// Spread the goals before planning, not after failing. The failure mode of not
/// doing so looks like a deadlock in the planner, which is the wrong place to go
/// looking.
/// </para>
/// </remarks>
public static class GoalSpread
{
    /// <summary>
    /// The <paramref name="count"/> passable cells nearest <paramref name="target"/>,
    /// nearest first, reachable from it.
    /// </summary>
    /// <remarks>
    /// Breadth-first over the same movement rules the search uses, so a cell on the
    /// far side of a wall is not "near" merely because it is close. Fewer than
    /// <paramref name="count"/> cells come back when the region cannot hold them,
    /// which the caller must handle rather than being told a comfortable lie.
    /// </remarks>
    /// <param name="grid">The map the flood runs over, under the search's own step and corner rules.</param>
    /// <param name="target">
    /// Where to spread from, and itself the first cell returned unless
    /// <paramref name="excluded"/> rules it out. An impassable target yields nothing,
    /// which is what makes this double as the caller's passability check.
    /// </param>
    /// <param name="count">How many cells to hand back. Zero is legal and returns nothing.</param>
    /// <param name="excluded">
    /// Cells that may not be handed out as goals — an arrived unit's cell during
    /// reconciliation, say. Excluded cells are still traversed, so the region
    /// beyond a parked unit is not falsely unreachable.
    /// </param>
    public static IReadOnlyList<int> Nearest(Grid grid, int target, int count, Func<int, bool>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0 || !grid.IsPassable(target))
        {
            return [];
        }

        var found = new List<int>(count);
        var seen = new bool[grid.CellCount];
        var queue = new Queue<int>();

        seen[target] = true;
        queue.Enqueue(target);

        while (queue.Count > 0 && found.Count < count)
        {
            var cell = queue.Dequeue();
            if (excluded is null || !excluded(cell))
            {
                found.Add(cell);
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
                if (seen[next])
                {
                    continue;
                }

                seen[next] = true;
                queue.Enqueue(next);
            }
        }

        return found;
    }

    /// <summary>
    /// Gives each agent its own destination near <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Closest cell to the closest agent, greedily, so the group does not cross
    /// over itself on the way in. Ties break on agent id, which is what keeps a
    /// group order deterministic — the same order issued twice must produce the
    /// same assignment or nothing downstream can be compared.
    /// <para>
    /// O(n²) in the size of the group, which is the right trade: a group order is
    /// occasional and a dozen agents is 144 comparisons.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int Agent, int Goal)> Assign(
        Grid grid,
        int target,
        IReadOnlyList<(int Agent, int Cell)> agents,
        Func<int, bool>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(agents);

        if (agents.Count == 0)
        {
            return [];
        }

        var goals = Nearest(grid, target, agents.Count, excluded);
        if (goals.Count == 0)
        {
            return [];
        }

        var remaining = agents.ToList();
        var assigned = new List<(int Agent, int Goal)>(goals.Count);

        foreach (var goal in goals)
        {
            var goalX = grid.ColumnOf(goal);
            var goalY = grid.RowOf(goal);

            var bestAt = 0;
            var bestDistance = double.PositiveInfinity;

            for (var i = 0; i < remaining.Count; i++)
            {
                var distance = Movement.OctileDistance(
                    grid.ColumnOf(remaining[i].Cell), grid.RowOf(remaining[i].Cell), goalX, goalY);

                if (distance < bestDistance ||
                    (distance == bestDistance && remaining[i].Agent < remaining[bestAt].Agent))
                {
                    bestAt = i;
                    bestDistance = distance;
                }
            }

            assigned.Add((remaining[bestAt].Agent, goal));
            remaining.RemoveAt(bestAt);
        }

        // Deterministic output order regardless of assignment order.
        assigned.Sort((left, right) => left.Agent.CompareTo(right.Agent));
        return assigned;
    }
}
