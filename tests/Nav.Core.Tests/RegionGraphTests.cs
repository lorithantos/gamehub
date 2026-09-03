using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// What the abstract graph is, and what routing over it costs against the flat
/// optimum.
/// </summary>
/// <remarks>
/// The second question is the one that decides the design. Hierarchical planning
/// is fast because it searches tens of nodes instead of a quarter of a million,
/// and it is near-optimal rather than optimal — going through the gates a region
/// route names can be longer than the shortest path. This repository validates
/// flat search against PUBLISHED OPTIMAL COSTS, so if the two claims are ever
/// allowed to merge, the first hierarchical path will read as a regression in a
/// test that has been green since milestone 1. Measuring the gap now is how that
/// stays a deliberate trade rather than a surprise.
/// </remarks>
public sealed class RegionGraphTests(ITestOutputHelper output)
{
    [Fact]
    public void AMapBecomesTensOfNodesInsteadOfHundredsOfThousands()
    {
        var map = MapGenerator.Generate(256, 256, seed: 11, loopPercent: 40);
        var grid = map.Grid;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var regions = Regions.Build(grid, new ContourGates());
        clock.Stop();

        var open = 0;
        for (var c = 0; c < grid.CellCount; c++)
        {
            if (grid.IsPassable(c))
            {
                open++;
            }
        }

        var degree = regions.Count == 0 ? 0 : 2.0 * regions.Links.Count / regions.Count;
        output.WriteLine(
            $"{open} open cells -> {regions.Count} regions and {regions.Links.Count} links "
                + $"in {clock.ElapsedMilliseconds} ms; mean degree {degree:F1}");
        output.WriteLine(
            $"region sizes: smallest {regions.Sizes.Min()}, median {Median(regions.Sizes)}, largest {regions.Sizes.Max()}");
        output.WriteLine(
            $"the largest region holds {100.0 * regions.Sizes.Max() / regions.Sizes.Sum():F0}% of the open map -- "
                + "a search inside it is still most of a flat search, which is the part this does not yet fix");

        Assert.True(regions.Count > 1, "the map did not divide at all");
        Assert.True(
            regions.Count < open / 50,
            $"{regions.Count} regions for {open} cells is not an abstraction worth having");
    }

    [Fact]
    public void EveryOpenCellIsInAtMostOneRegionAndGatesAreInNone()
    {
        var map = MapGenerator.Generate(192, 192, seed: 3);
        var grid = map.Grid;
        var gates = new ContourGates();
        var regions = Regions.Build(grid, gates);

        var gateCells = gates.Slices(grid).SelectMany(s => s.Cells).ToHashSet();

        for (var c = 0; c < grid.CellCount; c++)
        {
            if (!grid.IsPassable(c))
            {
                Assert.Equal(-1, regions.At(c));
            }
            else if (gateCells.Contains(c))
            {
                Assert.Equal(-1, regions.At(c));
            }
            else
            {
                Assert.InRange(regions.At(c), 0, regions.Count - 1);
            }
        }
    }

    [Fact]
    public void RoutingOverRegionsCostsSomethingAndTheCostIsMeasured()
    {
        // The number the design turns on. For each sampled pair: the flat
        // optimum, and the length of a path forced through the gates an abstract
        // route names. The ratio is what hierarchy costs in path quality.
        var map = MapGenerator.Generate(256, 256, seed: 11, loopPercent: 40);
        var grid = map.Grid;
        var regions = Regions.Build(grid, new ContourGates());
        var workspace = new SearchWorkspace();

        var open = new List<int>();
        for (var c = 0; c < grid.CellCount; c++)
        {
            if (grid.IsPassable(c) && regions.At(c) >= 0)
            {
                open.Add(c);
            }
        }

        // Deterministic spread of pairs rather than random ones: every sampled
        // cell paired with several others at different offsets around the list,
        // so the sample is not all short hops or all long ones.
        var ratios = new List<double>();
        var sameRegion = 0;
        var noSharedGate = 0;
        var samples = 60;
        var stride = Math.Max(1, open.Count / samples);

        for (var i = 0; i < open.Count; i += stride)
        {
            foreach (var offset in new[] { 0.17, 0.38, 0.61, 0.83 })
            {
                var from = open[i];
                var to = open[(i + (int)(open.Count * offset)) % open.Count];
                if (regions.At(from) == regions.At(to))
                {
                    sameRegion++;
                    continue;
                }

                var flat = PathFinder.FindPath(grid, from, to, workspace);
                if (!flat.Found)
                {
                    continue;
                }

                var viaGates = ThroughGates(grid, regions, from, to, workspace);
                if (viaGates > 0)
                {
                    ratios.Add(viaGates / flat.Cost);
                }
                else
                {
                    noSharedGate++;
                }
            }
        }

        ratios.Sort();
        output.WriteLine(
            $"{ratios.Count} cross-region pairs measured; {sameRegion} skipped as same-region, "
                + $"{noSharedGate} as not sharing a gate (the stand-in does not search the graph)");
        if (ratios.Count > 0)
        {
            output.WriteLine(
                $"path length through the gates vs flat optimum: "
                    + $"best {ratios[0]:F3}, median {ratios[ratios.Count / 2]:F3}, "
                    + $"worst {ratios[^1]:F3}, mean {ratios.Average():F3}");
        }

        Assert.NotEmpty(ratios);
        Assert.True(ratios[0] >= 0.999, "a route through gates cannot be shorter than the optimum");
    }

    /// <summary>
    /// The cost of walking to the gate an abstract route would use, then on. A
    /// deliberately naive stand-in for a hierarchical planner: it takes the
    /// single best gate between the two regions rather than searching the region
    /// graph, which is enough to measure what forcing a route through a gate
    /// costs. Zero when the two regions share no gate.
    /// </summary>
    private static double ThroughGates(Grid grid, RegionGraph regions, int from, int to, SearchWorkspace workspace)
    {
        var a = regions.At(from);
        var b = regions.At(to);
        var best = 0.0;

        foreach (var link in regions.Links)
        {
            if ((link.A != a || link.B != b) && (link.A != b || link.B != a))
            {
                continue;
            }

            var first = PathFinder.FindPath(grid, from, link.Cell, workspace);
            if (!first.Found)
            {
                continue;
            }

            var second = PathFinder.FindPath(grid, link.Cell, to, workspace);
            if (!second.Found)
            {
                continue;
            }

            var total = first.Cost + second.Cost;
            if (best == 0.0 || total < best)
            {
                best = total;
            }
        }

        return best;
    }

    private static int Median(IReadOnlyList<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
