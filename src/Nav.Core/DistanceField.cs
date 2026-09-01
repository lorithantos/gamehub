namespace Nav.Core;

/// <summary>
/// The exact cost from every cell to one destination: D* Lite's <c>g</c> surface
/// and a flow field at once, keyed by the destination rather than by any unit.
/// </summary>
/// <remarks>
/// The milestone-2 §12 decision, now built: route knowledge belongs to the
/// destination. In an RTS the number of live destinations K is small while the
/// unit count N is not, so K of these — shared by every unit heading there —
/// replace N searches rediscovering the same ground.
/// <para>
/// <b>This is a heuristic, not a motion model.</b> Units still move by the
/// space-time search against the reservation table; a field only replaces the
/// octile estimate with the true remaining distance, which collapses expansions
/// toward the path length while leaving every collision guarantee untouched.
/// </para>
/// <para>
/// Built by one backward Dijkstra over the exact movement rules the search uses
/// — same <see cref="Movement.Steps"/>, same corner rule, same costs. Those
/// rules are symmetric, so distance-from-destination equals
/// distance-to-destination, and milestone 1's verified A* is the oracle: for
/// every passable cell, <see cref="CostFrom"/> must equal <c>PathFinder</c>'s
/// cost from that cell, which the test suite checks cell by cell. Terrain is
/// static, so a field never goes stale; when terrain change arrives, D* Lite
/// repair lands as a change inside this type rather than a redesign around it.
/// </para>
/// </remarks>
public sealed class DistanceField
{
    private readonly double[] _cost;

    private DistanceField(int destination, double[] cost)
    {
        Destination = destination;
        _cost = cost;
    }

    /// <summary>The cell every cost here is measured to, and the key this field is cached under.</summary>
    public int Destination { get; }

    /// <summary>The exact remaining cost, or positive infinity if the cell cannot reach the destination.</summary>
    public double CostFrom(int cell) => _cost[cell];

    /// <summary>
    /// Can this cell reach the destination at all? Terrain is static, so a false
    /// here is a permanent verdict about the map -- never a temporary one about traffic.
    /// </summary>
    public bool Reaches(int cell) => !double.IsPositiveInfinity(_cost[cell]);

    /// <summary>
    /// Sweeps the whole map once -- a single backward Dijkstra, O(cells log cells)
    /// -- after which every query is an array read.
    /// </summary>
    /// <param name="grid">The map to sweep. Every cell gets an entry, passable or not.</param>
    /// <param name="destination">The cell the field is keyed by; must be on the map and passable.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="destination"/> is off the map or impassable. An impassable goal
    /// has no field, and refusing says so where a field of infinities would not.
    /// </exception>
    public static DistanceField Build(Grid grid, int destination)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegative(destination);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destination, grid.CellCount);
        if (!grid.IsPassable(destination))
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination), destination, "the destination is not passable; no field exists.");
        }

        var cost = new double[grid.CellCount];
        Array.Fill(cost, double.PositiveInfinity);

        // Plain Dijkstra with lazy deletion, mirroring the search's own frontier
        // discipline. The scratch is local; only the settled costs live on.
        var frontier = new PriorityQueue<int, double>();
        cost[destination] = 0.0;
        frontier.Enqueue(destination, 0.0);

        while (frontier.TryDequeue(out var cell, out var reached))
        {
            if (reached > cost[cell])
            {
                continue;   // a stale entry; the cell was settled cheaper
            }

            var x = grid.ColumnOf(cell);
            var y = grid.RowOf(cell);

            foreach (var step in Movement.Steps)
            {
                if (!Movement.IsLegalStep(grid, x, y, step.DeltaX, step.DeltaY))
                {
                    continue;
                }

                var next = grid.Index(x + step.DeltaX, y + step.DeltaY);
                var through = reached + step.Cost;
                if (through < cost[next])
                {
                    cost[next] = through;
                    frontier.Enqueue(next, through);
                }
            }
        }

        return new DistanceField(destination, cost);
    }
}

/// <summary>
/// K live fields, least-recently-used beyond capacity.
/// </summary>
/// <remarks>
/// The cap exists because a field is O(cells) of memory and a game's set of live
/// destinations is small but unbounded over a match. Eviction is deterministic
/// (strict LRU on <see cref="For"/> calls), so two identical runs hold identical
/// caches — replay determinism must not depend on what happens to be cached.
/// </remarks>
public sealed class FieldCache : IDistanceFieldSource
{
    private readonly Grid _grid;
    private readonly int _capacity;
    private readonly Dictionary<int, DistanceField> _fields = [];
    private readonly List<int> _recency = [];   // least recent first

    /// <summary>An empty cache. Fields are built on first request, never up front.</summary>
    /// <param name="grid">The map every field in this cache is built over.</param>
    /// <param name="capacity">
    /// How many fields to keep live; asking for one more evicts the least recently
    /// requested. At least one.
    /// </param>
    public FieldCache(Grid grid, int capacity)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _grid = grid;
        _capacity = capacity;
    }

    /// <inheritdoc/>
    public int Count => _fields.Count;

    /// <inheritdoc/>
    /// <remarks>
    /// Built on the first ask and then handed to every later caller as the SAME
    /// instance. Each call also marks the field most recently used, so the
    /// sequence of calls is what decides the eviction order -- which is what
    /// makes eviction deterministic and replay safe.
    /// </remarks>
    public DistanceField For(int destination)
    {
        if (_fields.TryGetValue(destination, out var cached))
        {
            _recency.Remove(destination);
            _recency.Add(destination);
            return cached;
        }

        if (_fields.Count == _capacity)
        {
            var evict = _recency[0];
            _recency.RemoveAt(0);
            _fields.Remove(evict);
        }

        var field = DistanceField.Build(_grid, destination);
        _fields[destination] = field;
        _recency.Add(destination);
        return field;
    }
}
