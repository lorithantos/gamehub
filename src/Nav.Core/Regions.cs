namespace Nav.Core;

/// <summary>
/// Cuts a map into regions at its gates, then makes the result usable: slivers
/// merged away, oversized rooms split.
/// </summary>
/// <remarks>
/// Cutting at gates alone gives a decomposition, not a useful one. Measured on a
/// generated 256-square: 90 regions, median size EIGHT, largest holding 46% of
/// the open map. Two different faults wearing one number.
/// <list type="bullet">
/// <item><description><b>Slivers.</b> Two gates near each other carve the strip
/// between them into its own region. It is a region by the letter of the rule
/// and nothing anybody would plan through.</description></item>
/// <item><description><b>The room nothing cuts.</b> A gate is where the map is
/// NARROW, so a large open space contains none by construction and survives
/// whole. A search inside it is most of a flat search, which is the entire
/// saving gone.</description></item>
/// </list>
/// <para>
/// Merging fixes the first and cannot fix the second; splitting fixes the second
/// and would leave the first. So both, in that order, and the split is
/// GEOMETRIC — there is no semantic cut to be found in an empty room, which is
/// the honest reason the gate decomposition alone was never going to balance.
/// </para>
/// </remarks>
public static class Regions
{
    /// <summary>
    /// Builds the abstract graph: gates cut it, then the pieces are made even.
    /// </summary>
    /// <param name="grid">The map.</param>
    /// <param name="gates">
    /// Where the map is narrow. Needs whole passages rather than one cell each,
    /// so this takes the detector rather than <c>IChokepointSource</c> — a
    /// two-cell corridor with one cell removed is still a corridor.
    /// </param>
    /// <param name="smallest">
    /// Regions under this are absorbed into a neighbour. Nothing smaller is worth
    /// a node in a graph whose whole purpose is to have few nodes.
    /// </param>
    /// <param name="largest">
    /// Regions over this are split until they are not. The ceiling is what makes
    /// an intra-region search cheap, which is the point of the abstraction.
    /// </param>
    public static RegionGraph Build(Grid grid, ContourGates gates, int smallest = 48, int largest = 1024)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentOutOfRangeException.ThrowIfNegative(smallest);
        ArgumentOutOfRangeException.ThrowIfLessThan(largest, 1);

        var isGate = new bool[grid.CellCount];
        foreach (var (_, cells) in gates.Slices(grid))
        {
            foreach (var cell in cells)
            {
                isGate[cell] = true;
            }
        }

        var region = CutAtGates(grid, isGate);
        Merge(grid, region, smallest);
        Split(grid, region, largest);

        var sizes = Renumber(grid, region);
        return new RegionGraph(region, sizes, Link(grid, region, isGate));
    }

    /// <summary>Flood what is left once the gate cells are removed.</summary>
    private static int[] CutAtGates(Grid grid, bool[] isGate)
    {
        var region = new int[grid.CellCount];
        Array.Fill(region, -1);

        var next = 0;
        var stack = new Stack<int>();

        for (var start = 0; start < grid.CellCount; start++)
        {
            if (!grid.IsPassable(start) || isGate[start] || region[start] >= 0)
            {
                continue;
            }

            var id = next++;
            region[start] = id;
            stack.Push(start);

            while (stack.Count > 0)
            {
                foreach (var n in Around(grid, stack.Pop()))
                {
                    if (!isGate[n] && region[n] < 0)
                    {
                        region[n] = id;
                        stack.Push(n);
                    }
                }
            }
        }

        return region;
    }

    /// <summary>
    /// Absorbs undersized regions into whichever neighbour they touch most.
    /// </summary>
    /// <remarks>
    /// Smallest first and repeatedly, because absorbing one can push another over
    /// the floor and there is no reason to split what has just been joined. Ties
    /// go to the lower region id so two runs agree, which everything downstream
    /// of a replay depends on.
    /// </remarks>
    private static void Merge(Grid grid, int[] region, int smallest)
    {
        if (smallest <= 0)
        {
            return;
        }

        // Regions with no neighbour to be absorbed into. Local, because a static
        // set here would be state shared between every call on every map, and
        // the whole design of this codebase is ownership rather than sharing.
        var stranded = new HashSet<int>();

        while (true)
        {
            var counts = Counts(region);
            var victim = -1;
            var victimSize = int.MaxValue;

            foreach (var (id, size) in counts)
            {
                if (size < smallest && size < victimSize && !stranded.Contains(id))
                {
                    victim = id;
                    victimSize = size;
                }
            }

            if (victim < 0)
            {
                return;
            }

            // Whichever neighbour it shares the most border with; a sliver
            // belongs to the room it is most attached to, not to whichever one
            // happens to be scanned first.
            var shared = new Dictionary<int, int>();
            for (var cell = 0; cell < region.Length; cell++)
            {
                if (region[cell] != victim)
                {
                    continue;
                }

                foreach (var n in Around(grid, cell))
                {
                    var other = region[n];
                    if (other >= 0 && other != victim)
                    {
                        shared[other] = shared.GetValueOrDefault(other) + 1;
                    }
                }
            }

            if (shared.Count == 0)
            {
                // An island under the floor. Nothing to absorb it, and deleting
                // it would strand its cells, so it keeps its node and stops
                // being a candidate.
                stranded.Add(victim);
                continue;
            }

            var into = shared.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;
            for (var cell = 0; cell < region.Length; cell++)
            {
                if (region[cell] == victim)
                {
                    region[cell] = into;
                }
            }
        }
    }

    /// <summary>
    /// Splits oversized regions along their longest axis, repeatedly.
    /// </summary>
    /// <remarks>
    /// Two poles by double sweep — furthest cell from an arbitrary start, then
    /// furthest from that — which approximates the region's diameter cheaply.
    /// Every cell then goes to the nearer pole BY DISTANCE THROUGH THE REGION
    /// rather than as the crow flies, so a horseshoe-shaped room splits along
    /// its length instead of across the gap in its middle.
    /// <para>
    /// This is geometric and admits it. There is no chokepoint in an open room to
    /// find, so a cut there answers "where can this be halved" rather than "where
    /// does the map pinch" — a different question, asked only because the first
    /// one has no answer here.
    /// </para>
    /// </remarks>
    private static void Split(Grid grid, int[] region, int largest)
    {
        var guard = 0;
        while (guard++ < 10_000)
        {
            var counts = Counts(region);
            var target = -1;
            var targetSize = largest;

            foreach (var (id, size) in counts)
            {
                if (size > targetSize)
                {
                    target = id;
                    targetSize = size;
                }
            }

            if (target < 0)
            {
                return;
            }

            var members = new List<int>();
            for (var cell = 0; cell < region.Length; cell++)
            {
                if (region[cell] == target)
                {
                    members.Add(cell);
                }
            }

            var first = FurthestWithin(grid, region, target, members[0]);
            var second = FurthestWithin(grid, region, target, first);
            if (first == second)
            {
                return;
            }

            var fromFirst = DistancesWithin(grid, region, target, first);
            var fromSecond = DistancesWithin(grid, region, target, second);
            var fresh = region.Max() + 1;

            foreach (var cell in members)
            {
                // Ties to the first pole, so the split is the same on every run.
                if (fromSecond[cell] < fromFirst[cell])
                {
                    region[cell] = fresh;
                }
            }

            // A region that would not divide -- everything nearer one pole --
            // would loop forever; leave it oversized and honest.
            if (!members.Any(c => region[c] == fresh))
            {
                return;
            }
        }
    }

    private static int FurthestWithin(Grid grid, int[] region, int id, int from)
    {
        var distance = DistancesWithin(grid, region, id, from);
        var best = from;
        var bestDistance = -1;

        for (var cell = 0; cell < region.Length; cell++)
        {
            if (region[cell] == id && distance[cell] > bestDistance)
            {
                bestDistance = distance[cell];
                best = cell;
            }
        }

        return best;
    }

    /// <summary>Steps from a seed to every cell of one region, without leaving it.</summary>
    private static int[] DistancesWithin(Grid grid, int[] region, int id, int from)
    {
        var distance = new int[region.Length];
        Array.Fill(distance, -1);

        var queue = new Queue<int>();
        distance[from] = 0;
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var n in Around(grid, cell))
            {
                if (region[n] == id && distance[n] < 0)
                {
                    distance[n] = distance[cell] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return distance;
    }

    /// <summary>Compacts ids to 0..n-1 in first-appearance order and reports sizes.</summary>
    private static List<int> Renumber(Grid grid, int[] region)
    {
        var map = new Dictionary<int, int>();
        var sizes = new List<int>();

        for (var cell = 0; cell < region.Length; cell++)
        {
            if (region[cell] < 0)
            {
                continue;
            }

            if (!map.TryGetValue(region[cell], out var id))
            {
                id = sizes.Count;
                map[region[cell]] = id;
                sizes.Add(0);
            }

            region[cell] = id;
            sizes[id]++;
        }

        return sizes;
    }

    /// <summary>
    /// Every pair of regions that touch, whether through a gate or along a seam
    /// a split created.
    /// </summary>
    /// <remarks>
    /// Derived from adjacency rather than from the gate list, because splitting
    /// makes boundaries no gate knows about — deriving links from gates alone
    /// would leave a split room's halves unconnected in the graph while being
    /// perfectly walkable on the map.
    /// </remarks>
    private static List<RegionLink> Link(Grid grid, int[] region, bool[] isGate)
    {
        var links = new Dictionary<(int, int), int>();

        for (var cell = 0; cell < grid.CellCount; cell++)
        {
            if (!grid.IsPassable(cell))
            {
                continue;
            }

            var here = region[cell];

            foreach (var n in Around(grid, cell))
            {
                var there = region[n];

                if (here >= 0 && there >= 0 && here != there)
                {
                    Record(links, here, there, cell);
                }
                else if (here < 0 && isGate[cell])
                {
                    // A gate cell belongs to no region; it joins whatever it
                    // touches, which is the whole reason it was cut out.
                    foreach (var m in Around(grid, cell))
                    {
                        if (region[m] >= 0 && there >= 0 && region[m] != there)
                        {
                            Record(links, region[m], there, cell);
                        }
                    }
                }
            }
        }

        return [.. links.Select(kv => new RegionLink(kv.Key.Item1, kv.Key.Item2, kv.Value)).OrderBy(l => l.A).ThenBy(l => l.B)];
    }

    private static void Record(Dictionary<(int, int), int> links, int a, int b, int cell)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!links.TryGetValue(key, out var held) || cell < held)
        {
            links[key] = cell;
        }
    }

    private static Dictionary<int, int> Counts(int[] region)
    {
        var counts = new Dictionary<int, int>();
        foreach (var id in region)
        {
            if (id >= 0)
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        return counts;
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
