namespace Nav.Core;

/// <summary>
/// A unit walking a path at a constant speed in cells per second.
/// </summary>
/// <remarks>
/// Two decisions carry this type.
/// <para>
/// FIRST: the position is derived from total elapsed time, not accumulated into.
/// <see cref="Advance"/> adds to a running <em>time</em> and then computes where
/// that time puts the unit. Adding a displacement to a position each call would
/// make the answer depend on how the caller happened to slice the interval,
/// which is exactly what acceptance criterion 9 rejects.
/// </para>
/// <para>
/// SECOND: the arc length of every prefix of the path is computed once, in the
/// constructor, into a cumulative array. Locating the unit is then a binary
/// search rather than a walk, a delta spanning fifty segments costs the same as
/// one spanning half of one, and no square root is evaluated at any point -- a
/// segment is either 1 or sqrt(2) long, so its length is a two-entry lookup.
/// Space for time, and the space is one double per path cell.
/// </para>
/// <para>
/// The detail this is all guarding: a diagonal segment is sqrt(2) long, not 1.
/// A walker that gives each segment equal wall-clock time visibly accelerates on
/// diagonals.
/// </para>
/// </remarks>
internal sealed class Walker
{
    /// <summary>Indexed by "is this segment diagonal", so nothing branches to find a length.</summary>
    private static readonly double[] SegmentLength = [Movement.CardinalCost, Movement.DiagonalCost];

    /// <summary>
    /// Distance below which a position counts as the end of the path. Well under
    /// the 1e-4 the acceptance criteria compare at, and far above the rounding a
    /// summed arc length can carry.
    /// </summary>
    private const double ArrivalEpsilon = 1e-9;

    private readonly float[] _pointX;
    private readonly float[] _pointY;
    private readonly double[] _cumulative;
    private readonly double _speed;

    private double _elapsed;

    /// <param name="path">Flat cell indices, start first and goal last.</param>
    /// <param name="gridWidth">Width of the grid the indices refer to.</param>
    /// <param name="speed">Cells per second, along the path rather than per segment.</param>
    public Walker(IReadOnlyList<int> path, int gridWidth, double speed)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(gridWidth, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speed);

        if (path.Count == 0)
        {
            throw new ArgumentException("A walker needs at least one cell to stand on.", nameof(path));
        }

        _speed = speed;
        _pointX = new float[path.Count];
        _pointY = new float[path.Count];
        _cumulative = new double[path.Count];

        var previousX = 0;
        var previousY = 0;

        for (var i = 0; i < path.Count; i++)
        {
            var cell = path[i];
            ArgumentOutOfRangeException.ThrowIfNegative(cell, nameof(path));

            var x = cell % gridWidth;
            var y = cell / gridWidth;
            _pointX[i] = x;
            _pointY[i] = y;

            if (i > 0)
            {
                var deltaX = Math.Abs(x - previousX);
                var deltaY = Math.Abs(y - previousY);

                // A path with a gap in it would silently produce an arc length
                // that is too short and a unit that teleports along it.
                if (deltaX > 1 || deltaY > 1 || (deltaX == 0 && deltaY == 0))
                {
                    throw new ArgumentException(
                        $"path cells {i - 1} and {i} are not adjacent: ({previousX},{previousY}) to ({x},{y})",
                        nameof(path));
                }

                _cumulative[i] = _cumulative[i - 1] + SegmentLength[deltaX > 0 && deltaY > 0 ? 1 : 0];
            }

            previousX = x;
            previousY = y;
        }

        UpdatePosition();
    }

    /// <summary>
    /// Column in CELLS, fractional between two path nodes -- a drawing position, not
    /// a cell index, and rounding it back to one loses the whole point. Recomputed
    /// from <see cref="Elapsed"/> rather than accumulated into.
    /// </summary>
    public float X { get; private set; }

    /// <summary>Row, in the same fractional cell units as <see cref="X"/>.</summary>
    public float Y { get; private set; }

    /// <summary>
    /// True once the walk has reached the final cell, where the position clamps. A
    /// long frame therefore ends the walk rather than carrying the unit past its goal.
    /// </summary>
    public bool Arrived { get; private set; }

    /// <summary>Total arc length of the path, in cells.</summary>
    public double PathLength => _cumulative[^1];

    /// <summary>Seconds walked so far. The single piece of mutable state.</summary>
    public double Elapsed => _elapsed;

    /// <summary>
    /// Advances the walk by <paramref name="deltaSeconds"/>, however many
    /// segments that spans.
    /// </summary>
    public void Advance(double deltaSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);

        _elapsed += deltaSeconds;
        UpdatePosition();
    }

    /// <summary>Puts the unit back at the start of the path.</summary>
    public void Reset()
    {
        _elapsed = 0.0;
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        var distance = _elapsed * _speed;
        var total = PathLength;

        // Clamp rather than overshoot. A unit that runs past the goal because a
        // frame was long is a bug the renderer shows and the model denies.
        if (distance >= total - ArrivalEpsilon)
        {
            X = _pointX[^1];
            Y = _pointY[^1];
            Arrived = true;
            return;
        }

        Arrived = false;

        var index = Array.BinarySearch(_cumulative, distance);
        if (index < 0)
        {
            // ~index is where it would be inserted, so the segment it falls in
            // starts one before that.
            index = ~index - 1;
        }

        // The arrival branch above rules out the final vertex, so index + 1 is
        // always a real cell.
        var segmentStart = _cumulative[index];
        var segmentLength = _cumulative[index + 1] - segmentStart;
        var t = (distance - segmentStart) / segmentLength;

        X = (float)(_pointX[index] + ((_pointX[index + 1] - _pointX[index]) * t));
        Y = (float)(_pointY[index] + ((_pointY[index + 1] - _pointY[index]) * t));
    }
}
