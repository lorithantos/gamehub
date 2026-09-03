namespace Nav.Core;

/// <summary>
/// Finds the gates by flooding the map and watching where the contour collapses.
/// </summary>
/// <remarks>
/// <b>The measure.</b> Flood from a point with <see cref="DistanceField"/>. In open
/// ground the cells at distance <i>k</i> form a long arc; in a passage they are one
/// or two, and they stay that way for a run of consecutive <i>k</i>. Narrowness is a
/// property of the passage rather than of the map around it.
/// <para>
/// <b>The ranking.</b> Steepest descent over the field gives a forest rooted at the
/// flood's origin, so a cell's subtree is everything whose route home passes
/// through it. A separator cuts the reached map into S and R-S, so its smaller
/// side is <c>min(S, R-S)</c> — capped at half by construction, and about nothing
/// for a cell beside the root, which is the truth about it.
/// </para>
/// <para>
/// <b>Why several origins.</b> One flood gives one tree, and a passage awkwardly
/// placed with respect to that tree is invisible from it. Each origin is snapped to
/// the nearest open cell, which handles a nominal point landing in a wall — and
/// does NOT handle it landing on an island, which is a different failure needing a
/// different answer. Hence both the extra origins and the check that an origin
/// reached the main body before its opinion counts.
/// </para>
/// <para>
/// Supersedes <see cref="ChokepointScan"/>, which is still what
/// <see cref="MovementSystem"/> meters with. What that one got wrong, and how the
/// two scored against known passages, is in <c>docs/gates-and-regions.md</c>.
/// </para>
/// </remarks>
/// <param name="minimumCut">
/// The share of the map a passage must separate before it is reported, as a
/// fraction. Zero reports every narrow contour slice, which on a real map is
/// thousands of coastal nooks.
/// </param>
/// <param name="maximumWidth">A contour slice this wide or narrower is a passage.</param>
public sealed class ContourGates(double minimumCut = 0.01, int maximumWidth = 2) : IChokepointSource
{
    /// <summary>
    /// Where to start the floods, as fractions of the map. Nine on a even grid,
    /// plus four between them: a bad origin costs one flood and a good one is all
    /// that is needed, so more of them is cheap insurance against every kind of
    /// bad luck at once.
    /// </summary>
    private static readonly (double X, double Y)[] Origins =
    [
        (0.17, 0.17), (0.50, 0.17), (0.83, 0.17),
        (0.17, 0.50), (0.50, 0.50), (0.83, 0.50),
        (0.17, 0.83), (0.50, 0.83), (0.83, 0.83),
        (0.33, 0.33), (0.67, 0.33), (0.33, 0.67), (0.67, 0.67),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<Chokepoint> For(Grid grid) => [.. Slices(grid).Select(s => new Chokepoint(s.Cell, s.Cells.Count))];

    /// <summary>
    /// The same gates, with every cell of each rather than one representative.
    /// </summary>
    /// <remarks>
    /// <see cref="IChokepointSource"/> answers with a <see cref="Chokepoint"/>,
    /// which names a single cell — enough for metering, which asks "is traffic
    /// squeezing here". It is not enough to CUT with: a passage two cells wide
    /// still connects when one of them is removed, so a caller deriving regions
    /// by deleting gates would delete nothing and find one region. Hence both
    /// shapes, from one computation.
    /// </remarks>
    public IReadOnlyList<(int Cell, IReadOnlyList<int> Cells)> Slices(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var origins = new List<int>();
        foreach (var (fx, fy) in Origins)
        {
            var cell = NearestOpen(grid, (int)(grid.Width * fx), (int)(grid.Height * fy));
            if (cell >= 0 && !origins.Contains(cell))
            {
                origins.Add(cell);
            }
        }

        if (origins.Count == 0)
        {
            return [];
        }

        var runs = origins.Select(o => Score(grid, o)).ToList();
        var mainBody = runs.Max(r => r.Reached);

        var best = new Dictionary<int, (double Cut, IReadOnlyList<int> Cells)>();
        foreach (var run in runs.Where(r => r.Reached >= mainBody / 2))
        {
            foreach (var (cell, cells, cut) in run.Gates)
            {
                if (!best.TryGetValue(cell, out var held) || cut > held.Cut)
                {
                    best[cell] = (cut, cells);
                }
            }
        }

        // One passage, one answer. A corridor collapses the contour for a run of
        // consecutive distances, so it arrives here as a dozen adjacent cells all
        // separating the same region -- the monotonic range, reported as if it
        // were a dozen gates. Take the best of each neighbourhood and suppress
        // the rest, strongest first so the winner is the throat rather than
        // whichever cell happened to sort lowest.
        var ranked = best
            .Where(kv => kv.Value.Cut >= minimumCut)
            .OrderByDescending(kv => kv.Value.Cut)
            .ThenBy(kv => kv.Key)
            .ToList();

        var kept = new List<(int Cell, IReadOnlyList<int> Cells)>();
        foreach (var (cell, (_, cells)) in ranked)
        {
            var x = grid.ColumnOf(cell);
            var y = grid.RowOf(cell);
            var crowded = kept.Any(k =>
                Math.Abs(grid.ColumnOf(k.Cell) - x) <= Merge &&
                Math.Abs(grid.RowOf(k.Cell) - y) <= Merge);

            if (!crowded)
            {
                kept.Add((cell, cells));
            }
        }

        return [.. kept.OrderBy(k => k.Cell)];
    }

    /// <summary>Cells within this of a stronger gate are the same passage as it.</summary>
    private const int Merge = 4;

    private (List<(int Cell, IReadOnlyList<int> Cells, double Cut)> Gates, int Reached) Score(Grid grid, int origin)
    {
        var field = DistanceField.Build(grid, origin);

        var band = new int[grid.CellCount];
        var reached = 0;
        for (var c = 0; c < grid.CellCount; c++)
        {
            var cost = field.CostFrom(c);
            band[c] = grid.IsPassable(c) && double.IsFinite(cost) ? (int)cost : -1;
            if (band[c] >= 0)
            {
                reached++;
            }
        }

        // Subtree sizes by steepest descent, furthest first so a cell's children
        // are counted before it. Ties go to the lower cell, because a cached or
        // replayed answer has to equal a recomputed one exactly.
        var order = Enumerable.Range(0, grid.CellCount)
            .Where(c => band[c] >= 0)
            .OrderByDescending(c => field.CostFrom(c))
            .ThenBy(c => c)
            .ToArray();

        var subtree = new int[grid.CellCount];
        foreach (var cell in order)
        {
            subtree[cell]++;
            var parent = -1;
            var parentCost = field.CostFrom(cell);
            foreach (var n in Around(grid, cell))
            {
                var cost = field.CostFrom(n);
                if (band[n] >= 0 && (cost < parentCost || (cost == parentCost && parent >= 0 && n < parent)))
                {
                    parent = n;
                    parentCost = cost;
                }
            }

            if (parent >= 0)
            {
                subtree[parent] += subtree[cell];
            }
        }

        var seen = new bool[grid.CellCount];
        var stack = new Stack<int>();
        var gates = new List<(int, IReadOnlyList<int>, double)>();

        for (var start = 0; start < grid.CellCount; start++)
        {
            if (band[start] < 0 || seen[start])
            {
                continue;
            }

            var slice = band[start];
            var members = new List<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                members.Add(cell);
                foreach (var n in Around(grid, cell))
                {
                    if (!seen[n] && band[n] == slice)
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }

            if (members.Count > maximumWidth || members.Contains(origin))
            {
                continue;
            }

            var held = members.Sum(m => subtree[m]);
            var cut = (double)Math.Min(held, reached - held) / Math.Max(1, reached);
            members.Sort();
            gates.Add((members[0], members, cut));
        }

        return (gates, reached);
    }

    private static int NearestOpen(Grid grid, int x, int y)
    {
        if (grid.IsPassable(x, y))
        {
            return grid.Index(x, y);
        }

        for (var r = 1; r < Math.Max(grid.Width, grid.Height); r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r)
                    {
                        continue;
                    }

                    if (grid.IsPassable(x + dx, y + dy))
                    {
                        return grid.Index(x + dx, y + dy);
                    }
                }
            }
        }

        return -1;
    }

    private static IEnumerable<int> Around(Grid grid, int cell)
    {
        var x = grid.ColumnOf(cell);
        var y = grid.RowOf(cell);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && grid.IsPassable(x + dx, y + dy))
                {
                    yield return grid.Index(x + dx, y + dy);
                }
            }
        }
    }
}
