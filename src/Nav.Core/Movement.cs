namespace Nav.Core;

/// <summary>
/// The octile movement model: what a step costs, which steps are legal, and the
/// heuristic that must stay admissible against both.
/// </summary>
/// <remarks>
/// These three things live together because they cannot be allowed to drift.
/// A heuristic that overestimates the cost model it is paired with stops A* being
/// optimal, and the failure is silent -- paths come back slightly too expensive
/// and nothing throws. Keeping them in one file means changing one puts the other
/// two in front of you.
/// <para>
/// There is no square root anywhere on a hot path here. sqrt(2) is a constant, so
/// the step costs are constants baked into <see cref="Steps"/> and the heuristic
/// is a multiply-add. Nothing is computed per step that could have been computed
/// once.
/// </para>
/// </remarks>
public static class Movement
{
    /// <summary>
    /// One north, east, south or west step, and the unit the rest of the model is
    /// quoted in: <see cref="DiagonalCost"/> is sqrt(2) of it and
    /// <see cref="WaitCost"/> is exactly one of it.
    /// </summary>
    public const double CardinalCost = 1.0;

    /// <summary>
    /// What a tick spent standing still costs.
    /// </summary>
    /// <remarks>
    /// Deliberately not free. A cooperative search needs a wait action so a unit
    /// can yield right of way, and if waiting cost nothing the cheapest plan would
    /// be to wait forever. Priced at a cardinal step so the search prefers making
    /// progress, and so a plan's cost still means "ticks of effort".
    /// <para>
    /// It leaves the octile heuristic admissible: waiting never reduces the
    /// distance remaining, so the heuristic still cannot overestimate.
    /// </para>
    /// </remarks>
    public const double WaitCost = CardinalCost;

    /// <summary>
    /// Computed rather than typed out. The literal is 1.4142135623730951, but a
    /// hand-copied constant is a thing that can be wrong, and the JIT folds this
    /// after first access anyway.
    /// </summary>
    public static readonly double DiagonalCost = Math.Sqrt(2.0);

    /// <summary>One of the eight moves, with its cost already resolved.</summary>
    public readonly record struct Step(int DeltaX, int DeltaY, double Cost);

    /// <summary>
    /// The eight moves, cardinals first, in a fixed order.
    /// </summary>
    /// <remarks>
    /// A table, so the search never branches on "is this one diagonal" to decide
    /// what it costs -- the cost arrives with the offset. The order is fixed
    /// rather than incidental: it is what makes two runs of the same query expand
    /// nodes in the same sequence, which acceptance criterion 7 asks for.
    /// <para>
    /// <c>DiagonalCost</c> is usable here because static field initialisers run in
    /// textual order and it is declared above.
    /// </para>
    /// </remarks>
    private static readonly Step[] StepTable =
    [
        new(0, -1, CardinalCost),    // north
        new(1, 0, CardinalCost),     // east
        new(0, 1, CardinalCost),     // south
        new(-1, 0, CardinalCost),    // west
        new(1, -1, DiagonalCost),    // north-east
        new(1, 1, DiagonalCost),     // south-east
        new(-1, 1, DiagonalCost),    // south-west
        new(-1, -1, DiagonalCost),   // north-west
    ];

    /// <summary>
    /// A span rather than the array, so callers cannot reach in and rewrite the
    /// cost model.
    /// </summary>
    public static ReadOnlySpan<Step> Steps => StepTable;

    /// <summary>
    /// True if a unit standing on <paramref name="x"/>, <paramref name="y"/> may
    /// move by <paramref name="deltaX"/>, <paramref name="deltaY"/>.
    /// </summary>
    /// <remarks>
    /// THE CORNER-CUTTING RULE. A diagonal move is legal only when BOTH cells it
    /// passes between are passable -- blocking on just one of the two is the
    /// common wrong implementation, and it produces costs slightly below the
    /// published optima rather than anything that looks like a bug. On the
    /// section 4 fixture it returns 10.65685 where the answer is 11.82843.
    /// <para>
    /// The origin cell is assumed passable; the search only ever expands from
    /// cells it has already accepted, so re-checking it here would be dead work
    /// on every neighbour of every expanded node.
    /// </para>
    /// </remarks>
    public static bool IsLegalStep(Grid grid, int x, int y, int deltaX, int deltaY)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (!grid.IsPassable(x + deltaX, y + deltaY))
        {
            return false;
        }

        if (deltaX == 0 || deltaY == 0)
        {
            return true;
        }

        return grid.IsPassable(x + deltaX, y) && grid.IsPassable(x, y + deltaY);
    }

    /// <summary>
    /// The octile distance between two cells: the cost of the cheapest path
    /// across empty terrain, and therefore an admissible heuristic.
    /// </summary>
    /// <remarks>
    /// <c>(dx + dy) + (sqrt(2) - 2) * min(dx, dy)</c> -- take min(dx,dy) diagonal
    /// steps and the rest cardinal. It never overestimates, because obstacles and
    /// the corner rule can only make the real path longer.
    /// </remarks>
    public static double OctileDistance(int ax, int ay, int bx, int by)
    {
        var dx = Math.Abs(ax - bx);
        var dy = Math.Abs(ay - by);
        return dx + dy + ((DiagonalCost - 2.0) * Math.Min(dx, dy));
    }

    /// <summary>
    /// The cost of a walk made of <paramref name="cardinalSteps"/> cardinal and
    /// <paramref name="diagonalSteps"/> diagonal moves.
    /// </summary>
    /// <remarks>
    /// Two multiplications instead of one addition per step. The benchmark's
    /// published optimal lengths are computed this way, so reporting a path's cost
    /// through this method compares against them without a summation's worth of
    /// accumulated rounding standing in between. The search still accumulates
    /// step by step -- it has to, to order the frontier -- but the number that
    /// faces the oracle is this one.
    /// </remarks>
    public static double ExactCost(int cardinalSteps, int diagonalSteps) =>
        (cardinalSteps * CardinalCost) + (diagonalSteps * DiagonalCost);
}
