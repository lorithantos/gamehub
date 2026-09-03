using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// The generator is meant to be an ORACLE, so its own claims are what these
/// pin. A detector scored against a lying oracle is worse off than one nobody
/// scored at all.
/// </summary>
public sealed class MapGeneratorTests(ITestOutputHelper output)
{
    [Fact]
    public void TheSameSeedGivesTheSameMap()
    {
        // A fixture that is not reproducible is not a fixture, and this is the
        // reason the generator carries its own xorshift instead of Random --
        // the framework's sequence has changed between runtimes before.
        var first = MapGenerator.Generate(128, 128, seed: 4242);
        var second = MapGenerator.Generate(128, 128, seed: 4242);

        Assert.Equal(first.MapText, second.MapText);
        Assert.Equal(first.Gates.Count, second.Gates.Count);
        Assert.Equal(
            first.Gates.Select(g => (g.Cell, g.SmallerSide, g.Detour)),
            second.Gates.Select(g => (g.Cell, g.SmallerSide, g.Detour)));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentMaps()
    {
        Assert.NotEqual(
            MapGenerator.Generate(128, 128, seed: 1).MapText,
            MapGenerator.Generate(128, 128, seed: 2).MapText);
    }

    [Fact]
    public void EveryOpenCellIsReachableFromEveryOther()
    {
        // The whole point of the spanning tree. A map with a stranded pocket
        // would make a scenario fail as "the guard never arrived" rather than
        // as an error, which is the worst way for a fixture to be wrong.
        var map = MapGenerator.Generate(192, 192, seed: 7);
        var grid = map.Grid;

        var start = -1;
        var open = 0;
        for (var c = 0; c < grid.CellCount; c++)
        {
            if (grid.IsPassable(c))
            {
                open++;
                if (start < 0)
                {
                    start = c;
                }
            }
        }

        Assert.True(open > 0, "the generator produced no open cells at all");

        var field = DistanceField.Build(grid, start);
        var reached = 0;
        for (var c = 0; c < grid.CellCount; c++)
        {
            if (grid.IsPassable(c) && double.IsFinite(field.CostFrom(c)))
            {
                reached++;
            }
        }

        Assert.Equal(open, reached);
    }

    [Fact]
    public void ItProducesBothKindsOfPassage()
    {
        // The distinction the whole record exists for. With loops asked for, the
        // map must contain passages that separate something AND passages that
        // separate nothing -- otherwise a detector can score full marks by
        // answering the same way every time.
        var map = MapGenerator.Generate(256, 256, seed: 11, loopPercent: 40);

        var cuts = map.Gates.Count(g => double.IsInfinity(g.Detour));
        var loops = map.Gates.Count(g => !double.IsInfinity(g.Detour));

        output.WriteLine($"{map.Gates.Count} passages: {cuts} cut the map, {loops} have a way round");
        foreach (var gate in map.Gates.Where(g => !double.IsInfinity(g.Detour))
            .OrderByDescending(g => g.Detour).Take(5))
        {
            output.WriteLine(
                $"  panama at ({map.Grid.ColumnOf(gate.Cell)},{map.Grid.RowOf(gate.Cell)}): "
                    + $"strands {gate.SmallerSide}, detour {gate.Detour:F1}");
        }

        Assert.True(cuts > 0, "no passage separated anything; loops swallowed the whole tree");
        Assert.True(loops > 0, "no passage had a way round; loopPercent did nothing");
    }

    [Fact]
    public void ACutStrandsSomethingAndAnythingElseStrandsNothing()
    {
        // The two fields must agree with each other. Infinite detour and zero
        // stranding at once would mean the oracle contradicts itself.
        var map = MapGenerator.Generate(192, 192, seed: 3, loopPercent: 30);

        foreach (var gate in map.Gates)
        {
            if (double.IsInfinity(gate.Detour))
            {
                Assert.True(
                    gate.SmallerSide > 0,
                    $"gate at {gate.Cell} has no way round yet strands nobody");
            }
            else
            {
                Assert.True(
                    gate.SmallerSide == 0,
                    $"gate at {gate.Cell} strands {gate.SmallerSide} yet has a way round");
                Assert.True(gate.Detour >= 0, $"gate at {gate.Cell} claims filling it makes routes shorter");
            }
        }
    }

    [Fact]
    public void ItReachesRealMapScale()
    {
        // The reason it exists: the largest committed fixture is 49x49, and the
        // smallest map in a published benchmark set is 384x384.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var map = MapGenerator.Generate(512, 512, seed: 99);
        clock.Stop();

        var open = 0;
        for (var c = 0; c < map.Grid.CellCount; c++)
        {
            if (map.Grid.IsPassable(c))
            {
                open++;
            }
        }

        output.WriteLine(
            $"512x512 in {clock.ElapsedMilliseconds} ms: {open} open cells "
                + $"({100.0 * open / map.Grid.CellCount:F1}%), {map.Gates.Count} passages");

        Assert.Equal(512, map.Grid.Width);
        Assert.True(open > 20_000, $"only {open} open cells; the map is nearly solid");
        Assert.True(map.Gates.Count > 10, $"only {map.Gates.Count} passages on a 512x512 map");
    }
}
