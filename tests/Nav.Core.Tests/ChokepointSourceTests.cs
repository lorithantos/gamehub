namespace Nav.Core.Tests;

/// <summary>
/// The chokepoint seam: that gates are supplied rather than discovered, and that
/// supplying none turns metering off without touching a doctrine.
/// </summary>
public sealed class ChokepointSourceTests
{
    /// <summary>A wall across the middle with one gap: the smallest real gate.</summary>
    private const string Gate =
        """
        type octile
        height 7
        width 9
        map
        .........
        .........
        ....@....
        ....@....
        ....@....
        .........
        .........
        """;

    private static Grid Map() => Grid.FromMapText(Gate);

    /// <summary>Records what it was asked and answers with a fixed list.</summary>
    private sealed class Authored(params Chokepoint[] gates) : IChokepointSource
    {
        public int Asks { get; private set; }

        public IReadOnlyList<Chokepoint> For(Grid grid)
        {
            Asks++;
            return gates;
        }
    }

    [Fact]
    public void TheDefaultScanAgreesWithTheStaticFinderItWraps()
    {
        // ChokepointScan is a thin object over ChokepointMap.Find, and its only
        // addition is making the terminal count a value a caller holds. If those
        // two ever disagree the wrapper has grown behaviour it should not have.
        var grid = Map();

        Assert.Equal(ChokepointMap.Find(grid), new ChokepointScan().For(grid));
        Assert.Equal(ChokepointMap.Find(grid, 32), new ChokepointScan(32).For(grid));
    }

    [Fact]
    public void TheSourceIsAskedOnceAndThenHeld()
    {
        // Cached by the system rather than by the source, so an expensive source
        // stays a pure answer and is never asked twice.
        var grid = Map();
        var source = new Authored(new Chokepoint(grid.Index(4, 1), 1));
        var system = new MovementSystem(grid, chokepoints: source);

        system.AddAgent(grid.Index(1, 1));
        system.AddAgent(grid.Index(2, 1));
        system.Order([0, 1], grid.Index(7, 5));

        for (var tick = 0; tick < 12; tick++)
        {
            system.Tick();
        }

        Assert.Equal(1, source.Asks);
    }

    [Fact]
    public void MeteringFindsNoGateWhenTheSourceOffersNone()
    {
        // Structural, not a flag: MeteredGatherDoctrine looks for the gate between
        // its members and the destination, finds nothing, and returns before it can
        // hold anybody. Same doctrine, same map, no pacing.
        var grid = Map();
        var ops = new FakeGroupOps { Destination = 0, Slots = [0], Chokepoints = [] }
            .Cost(0, 0.0);

        for (var id = 0; id < 8; id++)
        {
            ops.With(id, cell: 100 + id, goal: 0).Cost(100 + id, 7.0 + (0.5 * id));
        }

        new MeteredGatherDoctrine().Advance(ops);

        Assert.Empty(ops.Holds);
    }

    [Fact]
    public void ASuppliedGateIsTheOneMeteringPacesAgainst()
    {
        // The same fixture with a gate present DOES hold -- so the previous test is
        // measuring the absence of gates rather than a fixture that could never
        // meter in the first place.
        var ops = new FakeGroupOps
        {
            Destination = 0,
            Slots = [0],
            Chokepoints = [new Chokepoint(Cell: 50, Width: 1)],
        }
            .Cost(50, 5.0)
            .Cost(0, 0.0);

        for (var id = 0; id < 8; id++)
        {
            ops.With(id, cell: 100 + id, goal: 0).Cost(100 + id, 7.0 + (0.5 * id));
        }

        new MeteredGatherDoctrine().Advance(ops);

        Assert.NotEmpty(ops.Holds);
    }
}
