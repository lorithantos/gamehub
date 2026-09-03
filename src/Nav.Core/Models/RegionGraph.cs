namespace Nav.Core.Models;

/// <summary>
/// One gate as an edge of the abstract graph: the two regions it joins, and
/// where on the map it is.
/// </summary>
/// <param name="A">A region index.</param>
/// <param name="B">The other, always different from <paramref name="A"/>.</param>
/// <param name="Cell">A cell in the passage, for a path that has to walk through it.</param>
public sealed record RegionLink(int A, int B, int Cell);

/// <summary>
/// A map cut into regions at its gates, so a route can be planned over tens of
/// nodes instead of a quarter of a million cells.
/// </summary>
/// <remarks>
/// This is the pay-off of finding the gates. A 512x512 map is 262,144 cells and
/// a search over it is the thing <c>BudgetedSearch</c> exists to ration; the
/// same map is a couple of hundred regions, and a search over THOSE is free. The
/// grid stays where the contention is -- units still reserve cells and still
/// push past each other locally -- and stops being where the "which way round"
/// question is answered.
/// <para>
/// <b>It is an annotation, like the gates it is built from.</b> Nothing here is
/// authoritative about whether a step is legal, and a route over regions is a
/// hint about which way to go rather than a path anybody walks. That matters
/// because the abstraction is NOT guaranteed optimal: going through the gates a
/// region route names can be longer than the flat search's answer, and the
/// repository validates flat search against published optimal costs. Those are
/// different claims and must not be allowed to become one.
/// </para>
/// </remarks>
/// <param name="RegionOf">Region index per cell; -1 for a wall or a gate cell.</param>
/// <param name="Sizes">Cells in each region, indexed by region.</param>
/// <param name="Links">Every gate, as an edge between the two regions it joins.</param>
public sealed record RegionGraph(
    IReadOnlyList<int> RegionOf, IReadOnlyList<int> Sizes, IReadOnlyList<RegionLink> Links)
{
    /// <summary>How many regions the map fell into.</summary>
    public int Count => Sizes.Count;

    /// <summary>Region containing a cell, or -1 for a wall or a gate.</summary>
    public int At(int cell) => cell >= 0 && cell < RegionOf.Count ? RegionOf[cell] : -1;
}
