namespace Nav.Core;

/// <summary>
/// One problem from a <c>.scen</c> file: a start, a goal, and the cost of the
/// optimal path between them.
/// </summary>
/// <param name="LineNumber">1-based line this came from, for error messages and failure reports.</param>
/// <param name="Bucket">Difficulty cluster. Carried through; nothing in this milestone reads it.</param>
/// <param name="MapName">Filename of the map this problem belongs to.</param>
/// <param name="MapWidth">Width the record expects the map to have.</param>
/// <param name="MapHeight">Height the record expects the map to have.</param>
/// <param name="StartX">Start column.</param>
/// <param name="StartY">Start row.</param>
/// <param name="GoalX">Goal column.</param>
/// <param name="GoalY">Goal row.</param>
/// <param name="OptimalLength">
/// The published optimal cost, with diagonals at sqrt(2) and no corner cutting.
/// This is the oracle: an implementation that disagrees with it is wrong, and the
/// direction of the disagreement says which way.
/// </param>
public sealed record ScenarioRecord(
    int LineNumber,
    int Bucket,
    string MapName,
    int MapWidth,
    int MapHeight,
    int StartX,
    int StartY,
    int GoalX,
    int GoalY,
    double OptimalLength)
{
    public int StartIndex(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid.Index(StartX, StartY);
    }

    public int GoalIndex(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid.Index(GoalX, GoalY);
    }

    /// <summary>
    /// Throws unless <paramref name="grid"/> is the map this record describes.
    /// </summary>
    /// <remarks>
    /// Scaling coordinates to fit a differently sized map would turn a wrong
    /// pairing of files into a set of plausible, silently incorrect problems --
    /// every cost slightly off, nothing obviously broken. Refusing is the only
    /// useful behaviour.
    /// </remarks>
    public void EnsureMatches(Grid grid, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (grid.Width != MapWidth || grid.Height != MapHeight)
        {
            throw new MapFormatException(
                source,
                LineNumber,
                $"record expects a {MapWidth}x{MapHeight} map but '{MapName}' loaded as {grid.Width}x{grid.Height}");
        }

        if (!grid.InBounds(StartX, StartY))
        {
            throw new MapFormatException(source, LineNumber, $"start ({StartX},{StartY}) is off the map");
        }

        if (!grid.InBounds(GoalX, GoalY))
        {
            throw new MapFormatException(source, LineNumber, $"goal ({GoalX},{GoalY}) is off the map");
        }
    }
}
