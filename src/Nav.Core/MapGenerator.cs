using System.Text;

namespace Nav.Core;

/// <summary>
/// Builds maps at the scale a real battle needs, out of nobody else's work.
/// </summary>
/// <remarks>
/// The fixtures under <c>maps/fixtures</c> are hand-drawn and tiny — the largest
/// is 49x49, against 384x384 for the smallest map in a published benchmark set.
/// Doctrine tuned at that size produces thresholds that transfer to nothing,
/// because there is no ground between "in the fight" and "across the map" for a
/// rule to have an opinion about.
/// <para>
/// <b>Why generate rather than download.</b> Benchmark corpora exist and this
/// project already uses one to check the pathfinder against published optimal
/// costs. That is a fair use of somebody's map and it stays in the harness. What
/// a generated map buys instead is two things a downloaded one cannot. Nothing
/// we show anybody carries someone else's level design. And the passages are
/// KNOWN, because we cut them — so a chokepoint detector can be scored against
/// ground truth rather than eyeballed against a screenshot.
/// </para>
/// <para>
/// Deterministic from the seed, using its own generator rather than
/// <see cref="Random"/>, whose sequence is not contracted across runtimes. Two
/// runs of the same seed must produce the same map, or a fixture is not a
/// fixture.
/// </para>
/// </remarks>
public static class MapGenerator
{
    /// <summary>Smallest room the splitter will leave, excluding its walls.</summary>
    private const int MinRoom = 6;

    /// <summary>
    /// Cuts a map of rooms joined by passages, and reports what it cut.
    /// </summary>
    /// <param name="width">Map width in cells. At least 32.</param>
    /// <param name="height">Map height in cells. At least 32.</param>
    /// <param name="seed">Anything; the same seed gives the same map.</param>
    /// <param name="loopPercent">
    /// How many extra passages to cut beyond the minimum needed to join every
    /// room, as a percentage of the rooms. Zero gives a tree, where every passage
    /// is a separator and there is exactly one route between any two points — tidy
    /// for testing a detector and unlike any real map. Higher values give
    /// alternatives to flank through, and passages that separate nothing.
    /// </param>
    /// <param name="corridorWidth">How wide to cut a passage. One or two.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or a knob is out of range.</exception>
    public static GeneratedMap Generate(
        int width, int height, int seed, int loopPercent = 25, int corridorWidth = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 32);
        ArgumentOutOfRangeException.ThrowIfNegative(loopPercent);
        ArgumentOutOfRangeException.ThrowIfLessThan(corridorWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(corridorWidth, 2);

        var rng = new Rng((uint)seed);
        var open = new bool[width * height];

        // 1. Split the rectangle until the pieces are room-sized.
        var leaves = new List<Rect>();
        Split(new Rect(1, 1, width - 2, height - 2), ref rng, leaves, 0);

        // 2. Carve a room inside each piece. The room takes roughly half to three
        //    quarters of its partition, NOT all of it: the leftover is the wall,
        //    and a generator that fills its leaves produces an arena rather than
        //    a map. Our own arena fixture is 85% open, and it is open on purpose
        //    because it exists to test throughput; real maps run 35-74%.
        var rooms = new List<Rect>();
        foreach (var leaf in leaves)
        {
            var w = Math.Max(3, (leaf.W * (45 + (int)rng.Next(30))) / 100);
            var h = Math.Max(3, (leaf.H * (45 + (int)rng.Next(30))) / 100);
            var x = leaf.X + 1 + (int)rng.Next(Math.Max(1, leaf.W - w - 1));
            var y = leaf.Y + 1 + (int)rng.Next(Math.Max(1, leaf.H - h - 1));
            var room = new Rect(x, y, w, h);
            rooms.Add(room);
            Fill(open, width, room);
        }

        // 3. Clutter the larger rooms. Blocks are kept clear of the room's edges
        //    and of its centre, so they cannot seal a room off or land on the
        //    cell a corridor is aimed at -- they add cover and local narrowness
        //    without ever threatening connectivity.
        foreach (var room in rooms)
        {
            if (room.W < 9 || room.H < 9)
            {
                continue;
            }

            var blocks = 1 + (int)rng.Next(3);
            for (var i = 0; i < blocks; i++)
            {
                var bw = 2 + (int)rng.Next(Math.Max(1, room.W / 4));
                var bh = 2 + (int)rng.Next(Math.Max(1, room.H / 4));
                var bx = room.X + 2 + (int)rng.Next(Math.Max(1, room.W - bw - 4));
                var by = room.Y + 2 + (int)rng.Next(Math.Max(1, room.H - bh - 4));
                var midX = room.X + (room.W / 2);
                var midY = room.Y + (room.H / 2);

                for (var y = by; y < by + bh; y++)
                {
                    for (var x = bx; x < bx + bw; x++)
                    {
                        if (Math.Abs(x - midX) <= 1 && Math.Abs(y - midY) <= 1)
                        {
                            continue;
                        }

                        open[(y * width) + x] = false;
                    }
                }
            }
        }

        // 4. Join them. A spanning tree first so the map is connected, then extra
        //    passages so there is more than one way round.
        var edges = SpanningTree(rooms, ref rng);
        var extra = rooms.Count * loopPercent / 100;
        AddLoops(edges, rooms, extra, ref rng);

        // 5. Cut each passage and remember where it went.
        var carved = new List<(int A, int B, int Cell)>();
        foreach (var (a, b) in edges)
        {
            var cell = Carve(open, width, height, rooms[a], rooms[b], corridorWidth, ref rng);
            carved.Add((a, b, cell));
        }

        var text = ToText(open, width, height);
        var grid = Grid.FromMapText(text);
        var gates = ScoreGates(grid, open, width, height, rooms, carved, corridorWidth);
        return new GeneratedMap(grid, text, gates);
    }

    /// <summary>
    /// What each passage is worth, as two facts: what filling it strands, and how
    /// much further the traffic would have to go.
    /// </summary>
    /// <remarks>
    /// Both, because they are different properties. A passage with a route around
    /// it strands nobody, and is still a chokepoint if that route is long. The
    /// detour is the number a doctrine cares about; the stranding is the one that
    /// is exact.
    /// <para>
    /// Measured on a REBUILT grid with the passage filled, rather than reasoned
    /// from the room graph. The graph says which edges are bridges; it does not
    /// know that two rooms ended up adjacent enough for their passages to merge,
    /// or that a corridor clipped the corner of a third room. Rebuilding is slow
    /// and cannot be wrong about the map it is actually looking at, and this runs
    /// once at generation rather than in a tick.
    /// </para>
    /// </remarks>
    private static List<KnownGate> ScoreGates(
        Grid grid,
        bool[] open,
        int width,
        int height,
        List<Rect> rooms,
        List<(int A, int B, int Cell)> carved,
        int corridorWidth)
    {
        var gates = new List<KnownGate>();
        var workspace = new SearchWorkspace();

        foreach (var (a, b, cell) in carved)
        {
            // Plug the passage: every cell of it near the corner that is not part
            // of a room, so filling it does not wall up a room as well.
            var plugged = (bool[])open.Clone();
            var cx = grid.ColumnOf(cell);
            var cy = grid.RowOf(cell);
            var plug = 0;
            for (var dy = -corridorWidth; dy <= corridorWidth; dy++)
            {
                for (var dx = -corridorWidth; dx <= corridorWidth; dx++)
                {
                    var x = cx + dx;
                    var y = cy + dy;
                    if (x <= 0 || y <= 0 || x >= width - 1 || y >= height - 1)
                    {
                        continue;
                    }

                    if (plugged[(y * width) + x] && !InAnyRoom(rooms, x, y))
                    {
                        plugged[(y * width) + x] = false;
                        plug++;
                    }
                }
            }

            var without = Grid.FromMapText(ToText(plugged, width, height));
            // ONE FLOOD DECIDES BOTH, and the order matters. The first version
            // worked the two out independently and let a test assert they
            // agreed. They did not, because "I could not find a cell to path
            // from" was being recorded as "there is no way round" -- three
            // passages in open ground came back as cuts that stranded nobody.
            // Whether plugging split the map is a CONNECTIVITY question, so a
            // flood answers it, and the detour follows from that answer instead
            // of racing it.
            var total = 0;
            var anyOpen = -1;
            for (var c = 0; c < without.CellCount; c++)
            {
                if (!without.IsPassable(c))
                {
                    continue;
                }

                total++;
                if (anyOpen < 0)
                {
                    anyOpen = c;
                }
            }

            var reached = anyOpen >= 0 ? Flood(without, anyOpen) : 0;
            var stranded = Math.Min(reached, total - reached);

            double detour;
            if (stranded > 0)
            {
                // Split in two, so there is no way round by definition and no
                // path needs running to discover it.
                detour = double.PositiveInfinity;
            }
            else
            {
                var from = Centre(without, rooms[a]);
                var to = Centre(without, rooms[b]);
                var before = PathFinder.FindPath(
                    grid, Centre(grid, rooms[a]), Centre(grid, rooms[b]), workspace);
                var after = from >= 0 && to >= 0
                    ? PathFinder.FindPath(without, from, to, workspace)
                    : before;

                // Still connected, so a route exists. If the endpoints could not
                // be resolved, the honest answer is that this passage cost the
                // traffic nothing -- not that it was a gate.
                detour = after.Found && before.Found ? Math.Max(0, after.Cost - before.Cost) : 0;
            }

            gates.Add(new KnownGate(cell, corridorWidth, stranded, detour));
        }

        return [.. gates.OrderBy(g => g.Cell)];
    }

    /// <summary>
    /// Somewhere inside the room to path from: its middle if that is open, else
    /// the open cell nearest the middle, else -1 if the whole room is filled.
    /// </summary>
    /// <remarks>
    /// It searches rather than testing one cell, and that is not fussiness. The
    /// obstacle pass and the plug can both cover a room's exact middle, and
    /// treating "the middle cell is blocked" as "this room is unreachable"
    /// recorded a passage as a cut when the room was perfectly well connected
    /// two cells to the left. It made the two halves of the oracle contradict
    /// each other, which the consistency test caught.
    /// </remarks>
    private static int Centre(Grid grid, Rect room)
    {
        var midX = room.X + (room.W / 2);
        var midY = room.Y + (room.H / 2);
        var best = -1;
        var bestDistance = int.MaxValue;

        for (var y = room.Y; y < room.Y + room.H; y++)
        {
            for (var x = room.X; x < room.X + room.W; x++)
            {
                if (!grid.IsPassable(x, y))
                {
                    continue;
                }

                var d = ((x - midX) * (x - midX)) + ((y - midY) * (y - midY));
                if (d < bestDistance || (d == bestDistance && grid.Index(x, y) < best))
                {
                    bestDistance = d;
                    best = grid.Index(x, y);
                }
            }
        }

        return best;
    }

    private static int Flood(Grid grid, int from)
    {
        var seen = new bool[grid.CellCount];
        var stack = new Stack<int>();
        seen[from] = true;
        stack.Push(from);
        var count = 0;
        while (stack.Count > 0)
        {
            var cell = stack.Pop();
            count++;
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
                    if (!seen[n])
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }
        }

        return count;
    }

    private static bool InAnyRoom(List<Rect> rooms, int x, int y) =>
        rooms.Any(r => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H);

    private static void Split(Rect r, ref Rng rng, List<Rect> leaves, int depth)
    {
        var canSplitX = r.W >= (MinRoom * 2) + 1;
        var canSplitY = r.H >= (MinRoom * 2) + 1;

        if (depth > 8 || (!canSplitX && !canSplitY))
        {
            leaves.Add(r);
            return;
        }

        // Split the longer way, so rooms stay roughly square.
        var vertical = canSplitX && (!canSplitY || r.W >= r.H);
        if (vertical)
        {
            var span = r.W - (MinRoom * 2);
            var at = MinRoom + (int)rng.Next(Math.Max(1, span));
            Split(new Rect(r.X, r.Y, at, r.H), ref rng, leaves, depth + 1);
            Split(new Rect(r.X + at, r.Y, r.W - at, r.H), ref rng, leaves, depth + 1);
        }
        else
        {
            var span = r.H - (MinRoom * 2);
            var at = MinRoom + (int)rng.Next(Math.Max(1, span));
            Split(new Rect(r.X, r.Y, r.W, at), ref rng, leaves, depth + 1);
            Split(new Rect(r.X, r.Y + at, r.W, r.H - at), ref rng, leaves, depth + 1);
        }
    }

    /// <summary>Nearest-neighbour spanning tree, so every room is reachable.</summary>
    private static List<(int A, int B)> SpanningTree(List<Rect> rooms, ref Rng rng)
    {
        var edges = new List<(int, int)>();
        var joined = new List<int> { 0 };
        var loose = Enumerable.Range(1, rooms.Count - 1).ToList();

        while (loose.Count > 0)
        {
            var bestIn = 0;
            var bestOut = 0;
            var bestDistance = int.MaxValue;
            foreach (var i in joined)
            {
                foreach (var j in loose)
                {
                    var d = Distance(rooms[i], rooms[j]);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestIn = i;
                        bestOut = j;
                    }
                }
            }

            edges.Add((bestIn, bestOut));
            joined.Add(bestOut);
            loose.Remove(bestOut);
        }

        return edges;
    }

    private static void AddLoops(List<(int A, int B)> edges, List<Rect> rooms, int count, ref Rng rng)
    {
        var have = new HashSet<(int, int)>(edges.Select(e => e.A < e.B ? e : (e.B, e.A)));
        for (var attempt = 0; attempt < count * 8 && have.Count < edges.Count + count; attempt++)
        {
            var a = (int)rng.Next(rooms.Count);
            var b = (int)rng.Next(rooms.Count);
            if (a == b)
            {
                continue;
            }

            var key = a < b ? (a, b) : (b, a);
            if (have.Contains(key) || Distance(rooms[a], rooms[b]) > 60)
            {
                continue;
            }

            have.Add(key);
            edges.Add((a, b));
        }
    }

    private static int Distance(Rect a, Rect b) =>
        Math.Abs((a.X + (a.W / 2)) - (b.X + (b.W / 2))) + Math.Abs((a.Y + (a.H / 2)) - (b.Y + (b.H / 2)));

    /// <summary>Cuts an L-shaped passage between two rooms; returns its corner.</summary>
    private static int Carve(
        bool[] open, int width, int height, Rect a, Rect b, int thickness, ref Rng rng)
    {
        var ax = a.X + (a.W / 2);
        var ay = a.Y + (a.H / 2);
        var bx = b.X + (b.W / 2);
        var by = b.Y + (b.H / 2);

        // Corner first or last, chosen from the seed so passages are not all
        // the same shape.
        var horizontalFirst = rng.Next(2) == 0;
        var cornerX = horizontalFirst ? bx : ax;
        var cornerY = horizontalFirst ? ay : by;

        Line(open, width, height, ax, ay, cornerX, cornerY, thickness);
        Line(open, width, height, cornerX, cornerY, bx, by, thickness);
        return (cornerY * width) + cornerX;
    }

    private static void Line(bool[] open, int width, int height, int x0, int y0, int x1, int y1, int thickness)
    {
        var stepX = Math.Sign(x1 - x0);
        var stepY = Math.Sign(y1 - y0);
        var x = x0;
        var y = y0;
        while (x != x1 || y != y1)
        {
            Dot(open, width, height, x, y, thickness);
            if (x != x1)
            {
                x += stepX;
            }
            else
            {
                y += stepY;
            }
        }

        Dot(open, width, height, x1, y1, thickness);
    }

    private static void Dot(bool[] open, int width, int height, int x, int y, int thickness)
    {
        for (var dy = 0; dy < thickness; dy++)
        {
            for (var dx = 0; dx < thickness; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (px > 0 && py > 0 && px < width - 1 && py < height - 1)
                {
                    open[(py * width) + px] = true;
                }
            }
        }
    }

    private static void Fill(bool[] open, int width, Rect r)
    {
        for (var y = r.Y; y < r.Y + r.H; y++)
        {
            for (var x = r.X; x < r.X + r.W; x++)
            {
                open[(y * width) + x] = true;
            }
        }
    }

    private static string ToText(bool[] open, int width, int height)
    {
        var text = new StringBuilder(((width + 1) * height) + 64);
        text.Append("type octile\n").Append("height ").Append(height).Append('\n')
            .Append("width ").Append(width).Append('\n').Append("map\n");
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                text.Append(open[(y * width) + x] ? '.' : '@');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private readonly record struct Rect(int X, int Y, int W, int H);

    /// <summary>
    /// Xorshift32. Small, and contracted in a way <see cref="Random"/> is not:
    /// the framework's sequence has changed between runtimes before, which would
    /// silently rewrite every seeded fixture in the repository.
    /// </summary>
    private struct Rng(uint seed)
    {
        private uint _state = seed == 0 ? 0x9E3779B9u : seed;

        public uint Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public uint Next(int exclusiveMax) => exclusiveMax <= 1 ? 0 : Next() % (uint)exclusiveMax;
    }
}
