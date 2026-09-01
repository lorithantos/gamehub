using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// Milestone 3 criterion 4: chokepoint ground truth. The fixtures were chosen
/// for this back when the design was banked — the gap and the crosscut
/// bottleneck are the committed answers a detector must find.
/// </summary>
public sealed class ChokepointMapTests(ITestOutputHelper output)
{
    [Fact]
    public void TheGapIsTheOnlyChokepointOnGapMap()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("gap.map"));

        var found = ChokepointMap.Find(grid);

        var single = Assert.Single(found);
        Assert.Equal(grid.Index(6, 4), single.Cell);
        Assert.Equal(1, single.Width);
    }

    [Fact]
    public void TheCrosscutBottleneckIsFound()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("crosscut.map"));

        var found = ChokepointMap.Find(grid);

        output.WriteLine("crosscut: " + string.Join(" ", found.Select(c =>
            $"({grid.ColumnOf(c.Cell)},{grid.RowOf(c.Cell)})w{c.Width}")));

        Assert.Contains(found, c => c.Cell == grid.Index(9, 3));
    }

    [Fact]
    public void AnOpenFieldHasNoChokepoints()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("empty-8-8.map"));

        Assert.Empty(ChokepointMap.Find(grid));
    }

    [Fact]
    public void DetectionIsDeterministic()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("crosscut.map"));

        Assert.Equal(ChokepointMap.Find(grid), ChokepointMap.Find(grid));
    }

    [Fact]
    public void TheArenaSetIsStable()
    {
        // Not ground truth -- a snapshot, so drift announces itself instead of
        // sliding. If the detector legitimately changes, update the pin and say
        // why in the commit.
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));

        var found = ChokepointMap.Find(grid);

        output.WriteLine("arena: " + string.Join(" ", found.Select(c =>
            $"({grid.ColumnOf(c.Cell)},{grid.RowOf(c.Cell)})w{c.Width}")));
        output.WriteLine($"arena count: {found.Count}");

        Assert.Equal(ChokepointMap.Find(grid), found);
    }

    [Fact]
    public void OpenGroundCarryingAllTheTrafficIsStillNotAChokepoint()
    {
        // The other discrimination, made explicit: the centre of an empty map
        // carries every sampled path -- maximal betweenness -- and is still not
        // a chokepoint, because nothing FORCES the traffic. Width is the veto.
        var grid = Grid.FromMapFile(Fixtures.Map("empty-8-8.map"));

        var found = ChokepointMap.Find(grid, terminals: 32);

        Assert.Empty(found);
    }
}
