namespace Nav.Core;

/// <summary>
/// Pre-processing: where does this map force paths together?
/// </summary>
/// <remarks>
/// Two signals, and both are required because each alone false-positives:
/// <list type="bullet">
/// <item><description><b>Betweenness</b> — sample start/goal pairs across the
/// map, run milestone-1 A* over each, count per-cell traversals. High traffic
/// finds where paths <em>want</em> to go — but open-field desire lines score
/// high with no narrowness.</description></item>
/// <item><description><b>Width</b> — the smaller of the contiguous passable
/// runs through the cell along its two axes. Narrowness proves the map
/// <em>forces</em> the traffic — but a dead-end alcove is narrow and
/// unvisited.</description></item>
/// </list>
/// <para>
/// Deterministic throughout: terminals are grid-strided (never random), pairs
/// are enumerated in a fixed order, and the result is sorted by cell — two runs
/// on one map must agree exactly, because the group layer's metering decisions
/// hang off this and replay determinism hangs off those.
/// </para>
/// <para>
/// Chokepoints are <b>annotations, not structure</b>: nothing routes over a
/// region graph, and no hierarchy exists. The group layer reads them for
/// metering; the search never sees them.
/// </para>
/// </remarks>
internal static class ChokepointMap
{
    /// <summary>A passage this wide or narrower can be forced.</summary>
    private const int NarrowWidth = 2;

    /// <summary>
    /// The share of sampled paths that must cross a narrow cell before it
    /// counts as forced rather than merely visited.
    /// </summary>
    private const double TrafficShare = 0.1;

    /// <summary>
    /// Every cell that carries a real share of the map's traffic AND is narrow
    /// enough to force it, in ascending cell order. Empty is the honest answer for
    /// open ground, and the common one.
    /// </summary>
    /// <remarks>
    /// Nothing is memoised here -- one call is one full sweep, so callers that want
    /// this per map hold the result themselves.
    /// </remarks>
    /// <param name="grid">The map to analyse.</param>
    /// <param name="terminals">
    /// How many sampling points to stride evenly through the passable cells. A* runs
    /// over every PAIR of them, so the cost of detection grows with the square of
    /// this. Two at minimum.
    /// </param>
    public static IReadOnlyList<Chokepoint> Find(Grid grid, int terminals = 24)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(terminals, 2);

        var passable = new List<int>();
        for (var cell = 0; cell < grid.CellCount; cell++)
        {
            if (grid.IsPassable(cell))
            {
                passable.Add(cell);
            }
        }

        if (passable.Count < 2)
        {
            return [];
        }

        // Grid-strided terminals: evenly spaced through the passable cells in
        // index order, which spreads them across the map without randomness.
        var stride = Math.Max(1, passable.Count / terminals);
        var sample = new List<int>();
        for (var i = 0; i < passable.Count; i += stride)
        {
            sample.Add(passable[i]);
        }

        var traversals = new int[grid.CellCount];
        var paths = 0;
        var workspace = new SearchWorkspace();

        for (var i = 0; i < sample.Count; i++)
        {
            for (var j = i + 1; j < sample.Count; j++)
            {
                var path = PathFinder.FindPath(grid, sample[i], sample[j], workspace);
                if (!path.Found)
                {
                    continue;
                }

                paths++;

                // Interior cells only: the terminals themselves accumulate
                // counts by being endpoints, which says nothing about the map.
                for (var k = 1; k < path.Cells.Count - 1; k++)
                {
                    traversals[path.Cells[k]]++;
                }
            }
        }

        if (paths == 0)
        {
            return [];
        }

        var threshold = Math.Max(1, (int)(paths * TrafficShare));
        var found = new List<Chokepoint>();

        foreach (var cell in passable)
        {
            if (traversals[cell] < threshold)
            {
                continue;
            }

            var width = WidthAt(grid, cell);
            if (width <= NarrowWidth)
            {
                found.Add(new Chokepoint(cell, width));
            }
        }

        return found;
    }

    /// <summary>
    /// The passage width at a cell: the smaller of its two axis-aligned
    /// contiguous passable runs. A corridor cell has one long run and one short
    /// one, and the short one is the passage.
    /// </summary>
    private static int WidthAt(Grid grid, int cell)
    {
        var x = grid.ColumnOf(cell);
        var y = grid.RowOf(cell);

        return Math.Min(
            RunLength(grid, x, y, 1, 0),
            RunLength(grid, x, y, 0, 1));
    }

    private static int RunLength(Grid grid, int x, int y, int deltaX, int deltaY)
    {
        var run = 1;
        for (var i = 1; grid.IsPassable(x + (i * deltaX), y + (i * deltaY)); i++)
        {
            run++;
        }

        for (var i = 1; grid.IsPassable(x - (i * deltaX), y - (i * deltaY)); i++)
        {
            run++;
        }

        return run;
    }
}
