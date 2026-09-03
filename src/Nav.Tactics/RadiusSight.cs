namespace Nav.Tactics;

/// <summary>
/// Sight as a plain radius: within octile distance is seen, and walls do not
/// exist.
/// </summary>
/// <remarks>
/// The measure the rest of the tactics layer already uses — the same octile
/// distance as reach, exposure and the leash — so a sight range and a weapon
/// range are numbers that can be compared without a conversion.
/// <para>
/// WHAT THIS GETS WRONG, on purpose: an enemy on the far side of a wall is
/// seen. So a corner is not yet cover, and a chokepoint is a bottleneck rather
/// than an ambush. It is the cheap half of the seam, taken first because it
/// costs one distance per candidate and because being wrong at a corner is
/// obvious in a replay, where a subtly wrong ray is not.
/// </para>
/// </remarks>
public sealed class RadiusSight(Grid grid) : ISight
{
    private readonly Grid _grid = grid ?? throw new ArgumentNullException(nameof(grid));

    /// <inheritdoc/>
    public bool CanSee(int from, int to, double range)
    {
        if (from == to)
        {
            return true;
        }

        var distance = Movement.OctileDistance(
            _grid.ColumnOf(from), _grid.RowOf(from), _grid.ColumnOf(to), _grid.RowOf(to));
        return distance <= range;
    }
}
