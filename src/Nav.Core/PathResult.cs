namespace Nav.Core;

/// <summary>
/// The outcome of one search.
/// </summary>
/// <param name="Cells">Flat cell indices, start first and goal last. Empty when nothing was found.</param>
/// <param name="Cost">Sum of the step costs along <paramref name="Cells"/>. Zero when nothing was found.</param>
/// <param name="Expanded">Nodes popped from the frontier and closed. A cost measure, not a result.</param>
/// <param name="Found">Whether a path exists. An unreachable goal is an answer, not an error.</param>
public sealed record PathResult(
    IReadOnlyList<int> Cells,
    double Cost,
    int Expanded,
    bool Found)
{
    /// <summary>Moves along the path -- one fewer than the number of cells.</summary>
    public int StepCount => Cells.Count > 0 ? Cells.Count - 1 : 0;

    public static PathResult NotFound(int expanded) => new([], 0.0, expanded, false);
}
