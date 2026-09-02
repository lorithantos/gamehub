namespace Nav.Tactics;

/// <summary>
/// Walks a route, engages what comes near it, and will not be led away.
/// </summary>
/// <remarks>
/// Two rules, and each closes a way patrols die in the games this was written
/// after.
/// <para>
/// <b>The leash.</b> A hostile is worth going after only while it is within
/// <see cref="Leash"/> of the ROUTE -- the straight line through the waypoints,
/// closed back to the first. That is one number doing two jobs: it decides what
/// is close enough to engage, and it decides when a fight has gone too far. Bait
/// that withdraws past the leash simply stops being a target, so the patrol
/// turns round and resumes its route instead of being walked into whatever is
/// waiting.
/// </para>
/// <para>
/// Measured to the route rather than to the waypoint being walked toward, and
/// the difference is not academic: on a leg longer than twice the leash, a
/// waypoint-measured leash makes a patrol ignore a hostile standing in the
/// middle of its own route -- which is exactly what the first draft of the
/// demo did. Measured to the line, a patrol covers the ground it walks.
/// </para>
/// <para>
/// <b>Nobody chases alone.</b> This doctrine issues one kind of move --
/// <see cref="ISquadMovement.Sortie"/>, which takes every member on station --
/// and there is no verb here for sending a single unit after something. So the
/// bait pulls the whole patrol or it pulls nobody, and the rule is structural
/// rather than a check somebody has to remember. A member away on an errand is
/// left on it: a unit at the repair pad is not dragged into a fight.
/// </para>
/// </remarks>
public sealed class PatrolDoctrine : SquadDoctrine
{
    private readonly int[] _waypoints;

    private int _at;
    private int _ordered = -1;
    private int _target = -1;

    /// <param name="waypoints">
    /// The route, walked in order and then from the top. Two or more cells; one
    /// cell is a guard, and <see cref="GuardDoctrine"/> is the doctrine for that.
    /// </param>
    /// <param name="leash">
    /// How far off its route the patrol will go after something, in cells.
    /// Beyond it a hostile is somebody else's problem.
    /// </param>
    /// <param name="repair">
    /// How the damaged are sent away and brought back. Null for the default
    /// policy. A patrol had none at all before this and fought to the death;
    /// it is the same rule the guard uses, which is why it is a component.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="waypoints"/> has fewer than two cells.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leash"/> is not positive.</exception>
    public PatrolDoctrine(IReadOnlyList<int> waypoints, double leash = 8.0, RepairPolicy? repair = null)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leash);
        if (waypoints.Count < 2)
        {
            throw new ArgumentException("A patrol needs at least two waypoints; one is a guard.", nameof(waypoints));
        }

        _waypoints = [.. waypoints];
        Leash = leash;
        Repair = repair ?? new RepairPolicy();
    }

    /// <summary>How far off its route the patrol will be drawn, in cells.</summary>
    public double Leash { get; }

    /// <summary>How the damaged are sent away and brought back.</summary>
    public RepairPolicy Repair { get; }

    /// <summary>The route, in the order it is walked.</summary>
    public IReadOnlyList<int> Waypoints => _waypoints;

    /// <summary>The waypoint currently being worked toward.</summary>
    public int CurrentWaypoint => _waypoints[_at];

    /// <summary>The hostile cell being engaged, or -1 when the patrol is just walking.</summary>
    public int Target => _target;

    /// <inheritdoc/>
    public override void Advance(ISquadOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        if (ops.Anchor < 0)
        {
            // Take up the route. This is the one group move the doctrine makes:
            // it sets the squad's station, and after it every move is a sortie.
            ops.MoveAll(_waypoints[0]);
            _ordered = _waypoints[0];
            return;
        }

        // Repair first, so a unit sent off this pass is not in the sortie the
        // pass may issue below, and a unit brought back walks with it.
        Repair.Advance(ops);

        var onStation = ops.Members.Where(id => !ops.Away.Contains(id)).ToArray();
        if (onStation.Length == 0)
        {
            return;
        }

        // Measured against the ROUTE, not against where the units happen to be
        // standing: a patrol drawn to the edge of its leash must not then find
        // the next hostile "near" and walk the leash again, one length at a time.
        _target = NearestHostileWithin(ops);

        var destination = _target;
        if (destination < 0)
        {
            // Nothing to do but walk. A waypoint is reached when everyone still
            // on station is standing on their piece of it.
            if (onStation.All(ops.HasArrived))
            {
                _at = (_at + 1) % _waypoints.Length;
            }

            destination = _waypoints[_at];
        }

        // Only when it CHANGES. Re-ordering the same destination every tick
        // rebuilds the formation under the units and they never settle.
        if (destination != _ordered)
        {
            ops.Sortie(destination);
            _ordered = destination;
        }
    }

    /// <summary>
    /// The hostile closest to the route and inside the leash, or -1. Ties go to
    /// the lower cell so the same world always produces the same choice.
    /// </summary>
    private int NearestHostileWithin(ISquadView ops)
    {
        var best = -1;
        var bestDistance = double.PositiveInfinity;

        foreach (var hostile in ops.Hostiles)
        {
            var distance = OffRoute(ops, hostile);
            if (distance > Leash)
            {
                continue;
            }

            if (distance < bestDistance || (distance == bestDistance && hostile < best))
            {
                best = hostile;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// How far a cell lies off the route: the shortest straight-line distance to
    /// any leg, the closing leg from the last waypoint back to the first
    /// included, because the patrol walks that one too.
    /// </summary>
    private double OffRoute(ISquadView ops, int cell)
    {
        var x = ops.ColumnOf(cell);
        var y = ops.RowOf(cell);

        var best = double.PositiveInfinity;
        for (var i = 0; i < _waypoints.Length; i++)
        {
            var a = _waypoints[i];
            var b = _waypoints[(i + 1) % _waypoints.Length];
            best = Math.Min(
                best,
                ToSegment(x, y, ops.ColumnOf(a), ops.RowOf(a), ops.ColumnOf(b), ops.RowOf(b)));
        }

        return best;
    }

    /// <summary>Distance from a point to a line segment, clamped to the segment's ends.</summary>
    private static double ToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = (dx * dx) + (dy * dy);

        // A degenerate leg (two waypoints on one cell) is just a point.
        var along = lengthSquared <= 0
            ? 0.0
            : Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lengthSquared, 0.0, 1.0);

        var nearestX = ax + (along * dx);
        var nearestY = ay + (along * dy);
        return Math.Sqrt(((px - nearestX) * (px - nearestX)) + ((py - nearestY) * (py - nearestY)));
    }
}
