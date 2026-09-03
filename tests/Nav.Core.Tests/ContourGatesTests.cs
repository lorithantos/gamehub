using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// The detector scored against a map whose gates are known, rather than eyeballed
/// against one whose gates are anybody's guess.
/// </summary>
/// <remarks>
/// This is the whole reason <see cref="MapGenerator"/> exists. Run against a
/// downloaded map, "it found sixteen gates" is not a result, because nobody knows
/// whether that map has sixteen. Run against a generated one, every passage was
/// cut on purpose and its worth was measured by plugging it, so a miss is a miss.
/// </remarks>
public sealed class ContourGatesTests(ITestOutputHelper output)
{
    /// <summary>How far off a known passage a detection may land and still count.</summary>
    private const int Slack = 3;

    [Fact]
    public void ItFindsTheCutsAndSaysWhereTheyAre()
    {
        var map = MapGenerator.Generate(256, 256, seed: 11, loopPercent: 40);
        var grid = map.Grid;
        var found = new ContourGates().For(grid);

        var open = 0;
        for (var c = 0; c < grid.CellCount; c++)
        {
            if (grid.IsPassable(c))
            {
                open++;
            }
        }

        // The passages worth finding: the ones that actually separate a
        // meaningful piece of the map.
        var wanted = map.Gates
            .Where(g => g.SmallerSide >= open / 100)
            .ToList();

        var hits = wanted.Count(g => found.Any(f => Near(grid, f.Cell, g)));
        var spurious = found.Count(f => !map.Gates.Any(g => Near(grid, f.Cell, g)));

        output.WriteLine(
            $"{open} open cells, {map.Gates.Count} passages cut, {wanted.Count} of them separate >=1%");
        output.WriteLine(
            $"detector reported {found.Count}: {hits}/{wanted.Count} real cuts found, "
                + $"{spurious} nowhere near any passage");

        foreach (var miss in wanted.Where(g => !found.Any(f => Near(grid, f.Cell, g))).Take(5))
        {
            output.WriteLine(
                $"  MISSED ({grid.ColumnOf(miss.Cell)},{grid.RowOf(miss.Cell)}) "
                    + $"separating {miss.SmallerSide} cells");
        }

        Assert.True(
            hits >= wanted.Count * 3 / 4,
            $"found only {hits} of {wanted.Count} passages that separate at least 1% of the map");
    }

    [Fact]
    public void ItFindsMoreOfTheRealCutsThanTheShippedScan()
    {
        // COUNT IS NOT THE COMPARISON, and an earlier version of this test made
        // that mistake -- it asserted the new detector reported MORE, right after
        // work went in to make it report less. Neither detector collapses to the
        // same number of answers and neither should be judged on how many it
        // gives. What matters is how much of the ground truth each one finds.
        var map = MapGenerator.Generate(256, 256, seed: 5, loopPercent: 30);
        var grid = map.Grid;

        var open = 0;
        for (var c = 0; c < grid.CellCount; c++)
        {
            if (grid.IsPassable(c))
            {
                open++;
            }
        }

        var wanted = map.Gates.Where(g => g.SmallerSide >= open / 100).ToList();
        var contour = new ContourGates().For(grid);
        var shipped = new ChokepointScan().For(grid);

        var contourHits = wanted.Count(g => contour.Any(f => Near(grid, f.Cell, g)));
        var shippedHits = wanted.Count(g => shipped.Any(f => Near(grid, f.Cell, g)));

        output.WriteLine($"{wanted.Count} passages separate at least 1% of the map");
        output.WriteLine($"  contour     {contourHits}/{wanted.Count} found, {contour.Count} reported");
        output.WriteLine($"  shipped scan {shippedHits}/{wanted.Count} found, {shipped.Count} reported");

        Assert.True(
            contourHits > shippedHits,
            $"contour found {contourHits} real cuts and the shipped scan found {shippedHits}");
    }

    [Fact]
    public void OpenGroundHasNoGatesAndItSaysSo()
    {
        // Empty is the honest answer for open ground and the old code's own doc
        // says so. A detector that invents gates in a field is worse than one
        // that finds none.
        var grid = Grid.FromMapFile(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "maps", "fixtures", "arena.map"));

        var found = new ContourGates().For(grid);
        output.WriteLine($"arena (85% open, deliberately): {found.Count} gates");

        Assert.Empty(found);
    }

    [Fact]
    public void TwoRunsAgree()
    {
        // Required before any cache can exist: a cached answer has to equal a
        // recomputed one, or the map behaves differently depending on whether
        // somebody cleared a directory.
        var map = MapGenerator.Generate(128, 128, seed: 21);
        var first = new ContourGates().For(map.Grid);
        var second = new ContourGates().For(map.Grid);

        Assert.Equal(first.Select(c => (c.Cell, c.Width)), second.Select(c => (c.Cell, c.Width)));
    }

    private static bool Near(Grid grid, int cell, KnownGate gate)
    {
        var x = grid.ColumnOf(cell);
        var y = grid.RowOf(cell);
        foreach (var g in gate.Cells)
        {
            if (Math.Abs(grid.ColumnOf(g) - x) <= Slack && Math.Abs(grid.RowOf(g) - y) <= Slack)
            {
                return true;
            }
        }

        return false;
    }
}
