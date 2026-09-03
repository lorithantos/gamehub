namespace Nav.Core;

/// <summary>
/// Cuts a map at its gates and reports what is left: the regions, and which
/// gate joins which.
/// </summary>
/// <remarks>
/// Removing the gate cells and flooding what remains is the whole method. It
/// works because a gate is by definition where the map is narrow, so deleting a
/// handful of cells separates two pieces that are each far larger — which is the
/// property <see cref="ContourGates"/> measures and the old traffic-share
/// criterion could not.
/// <para>
/// A gate that separates nothing still becomes an edge, and should: the Panama
/// case, where filling it strands nobody because there is a way round, is still
/// a passage the abstract route may want to take. Both ends simply land in the
/// same region and the edge is dropped, which is the correct answer for a gate
/// that gates nothing.
/// </para>
/// </remarks>
public static class Regions
{
    /// <summary>
    /// Builds the abstract graph for a map, using <paramref name="gates"/> to
    /// decide where to cut.
    /// </summary>
    /// <param name="grid">The map.</param>
    /// <param name="gates">
    /// Where the map is narrow. Needs the whole of each passage rather than one
    /// cell of it, so this takes the concrete detector rather than
    /// <c>IChokepointSource</c> — a two-cell corridor with one cell removed is
    /// still a corridor, and a caller cutting with the interface's single cell
    /// would find exactly one region and no edges.
    /// </param>
    public static RegionGraph Build(Grid grid, ContourGates gates)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(gates);

        var slices = gates.Slices(grid);
        var isGate = new bool[grid.CellCount];
        foreach (var (_, cells) in slices)
        {
            foreach (var cell in cells)
            {
                isGate[cell] = true;
            }
        }

        var regionOf = new int[grid.CellCount];
        Array.Fill(regionOf, -1);

        var sizes = new List<int>();
        var stack = new Stack<int>();

        for (var start = 0; start < grid.CellCount; start++)
        {
            if (!grid.IsPassable(start) || isGate[start] || regionOf[start] >= 0)
            {
                continue;
            }

            var region = sizes.Count;
            var size = 0;
            regionOf[start] = region;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                size++;
                var x = grid.ColumnOf(cell);
                var y = grid.RowOf(cell);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || !grid.IsPassable(x + dx, y + dy))
                        {
                            continue;
                        }

                        var n = grid.Index(x + dx, y + dy);
                        if (!isGate[n] && regionOf[n] < 0)
                        {
                            regionOf[n] = region;
                            stack.Push(n);
                        }
                    }
                }
            }

            sizes.Add(size);
        }

        var links = new List<RegionLink>();
        var seen = new HashSet<(int, int)>();

        foreach (var (cell, cells) in slices)
        {
            // Which regions does this passage touch? Its own cells are unassigned,
            // so look at what sits around them.
            var touching = new HashSet<int>();
            foreach (var member in cells)
            {
                var x = grid.ColumnOf(member);
                var y = grid.RowOf(member);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (!grid.IsPassable(x + dx, y + dy))
                        {
                            continue;
                        }

                        var r = regionOf[grid.Index(x + dx, y + dy)];
                        if (r >= 0)
                        {
                            touching.Add(r);
                        }
                    }
                }
            }

            // Touching one region means the cut did not cut: a passage with a way
            // round, whose sides were already joined elsewhere. Not an edge.
            var ends = touching.OrderBy(r => r).ToArray();
            for (var i = 0; i < ends.Length; i++)
            {
                for (var j = i + 1; j < ends.Length; j++)
                {
                    if (seen.Add((ends[i], ends[j])))
                    {
                        links.Add(new RegionLink(ends[i], ends[j], cell));
                    }
                }
            }
        }

        return new RegionGraph(regionOf, sizes, links);
    }
}
