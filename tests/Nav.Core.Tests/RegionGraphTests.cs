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
            $"the largest region holds {100.0 * regions.Sizes.Max() / regions.Sizes.Sum():F0}% of the open map");

        // The two faults the merge and the split exist to fix, as assertions
        // rather than as a printed apology. Before them: median size EIGHT and a
        // single region holding 46% of the map.
        Assert.True(
            regions.Sizes.Max() <= 1024,
            $"largest region is {regions.Sizes.Max()} cells; a search inside it is most of a flat search");
        Assert.True(
            Median(regions.Sizes) > 32,
            $"median region is {Median(regions.Sizes)} cells, which is a sliver rather than a place");

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
                + $"{noSharedGate} unroutable over the region graph");
        if (ratios.Count > 0)
        {
            output.WriteLine(
                $"path length through the gates vs flat optimum: "
                    + $"best {ratios[0]:F3}, median {ratios[ratios.Count / 2]:F3}, "
                    + $"worst {ratios[^1]:F3}, mean {ratios.Average():F3}");
        }

        Assert.NotEmpty(ratios);
        Assert.True(ratios[0] >= 0.999, "a route through gates cannot be shorter than the optimum");
        Assert.Equal(0, noSharedGate);

        // The cost of hierarchy, pinned. This is UNREFINED -- the route is forced
        // through one representative cell per gate, exactly. A real planner uses
        // the gate sequence to shape a window and reruns the flat search inside
        // it, which recovers most of the loss; the worst case here (a route two
        // and a half times the optimum) is what refinement is for.
        Assert.True(
            ratios[ratios.Count / 2] < 1.15,
            $"median route is {ratios[ratios.Count / 2]:F3} of optimal; hierarchy is costing too much");
    }

    /// <summary>
    /// A real hierarchical route: search the region graph for a sequence of
    /// gates, then walk it. Returns the walked cost, or zero if no route exists.
    /// </summary>
    /// <remarks>
    /// <b>Dijkstra over LINKS, not over regions.</b> What a region costs to cross
    /// depends on which gate you came in by and which you leave by, so a node
    /// that is only "a region" cannot carry the cost — the natural state is the
    /// gate you are standing at, with an edge to every other gate of a region it
    /// touches. That is the line graph, and getting it wrong is how a
    /// hierarchical planner quietly returns routes far worse than it should.
    /// <para>
    /// The first version of this took the single best gate directly joining the
    /// two regions and gave up otherwise. Fine when regions were few and huge;
    /// once balancing produced 108 of them it could not route 232 of 244 sampled
    /// pairs, and the ratios it did report were measuring ITS limits rather than
    /// the abstraction's.
    /// </para>
    /// </remarks>
    private static double ThroughGates(Grid grid, RegionGraph regions, int from, int to, SearchWorkspace workspace)
    {
        var start = regions.At(from);
        var goal = regions.At(to);
        if (start < 0 || goal < 0)
        {
            return 0;
        }

        // Which links touch each region.
        var byRegion = new Dictionary<int, List<int>>();
        for (var i = 0; i < regions.Links.Count; i++)
        {
            foreach (var side in new[] { regions.Links[i].A, regions.Links[i].B })
            {
                if (!byRegion.TryGetValue(side, out var list))
                {
                    byRegion[side] = list = [];
                }

                list.Add(i);
            }
        }

        double Walk(int a, int b)
        {
            var path = PathFinder.FindPath(grid, a, b, workspace);
            return path.Found ? path.Cost : double.PositiveInfinity;
        }

        // Seed: the cost of reaching each gate out of the starting region.
        var best = new Dictionary<int, double>();
        var queue = new PriorityQueue<int, double>();
        foreach (var i in byRegion.GetValueOrDefault(start, []))
        {
            var cost = Walk(from, regions.Links[i].Cell);
            if (double.IsFinite(cost))
            {
                best[i] = cost;
                queue.Enqueue(i, cost);
            }
        }

        var answer = double.PositiveInfinity;

        // Straight there, if both ends are in the same region.
        if (start == goal)
        {
            answer = Walk(from, to);
        }

        while (queue.Count > 0)
        {
            var link = queue.Dequeue();
            var cost = best[link];
            if (cost > answer)
            {
                break;
            }

            var here = regions.Links[link];
            foreach (var side in new[] { here.A, here.B })
            {
                if (side == goal)
                {
                    answer = Math.Min(answer, cost + Walk(here.Cell, to));
                }

                foreach (var next in byRegion.GetValueOrDefault(side, []))
                {
                    if (next == link)
                    {
                        continue;
                    }

                    var step = cost + Walk(here.Cell, regions.Links[next].Cell);
                    if (double.IsFinite(step) && (!best.TryGetValue(next, out var held) || step < held))
                    {
                        best[next] = step;
                        queue.Enqueue(next, step);
                    }
                }
            }
        }

        return double.IsFinite(answer) ? answer : 0;
    }

    private static int Median(IReadOnlyList<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
