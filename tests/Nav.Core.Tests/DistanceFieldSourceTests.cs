using System.Diagnostics.CodeAnalysis;

namespace Nav.Core.Tests;

/// <summary>
/// A field source that answers exactly as the one it wraps, and remembers what it
/// was asked.
/// </summary>
/// <remarks>
/// A miss is detected by INSTANCE IDENTITY rather than by counting: the cache
/// hands back the same object for a hit, so a different instance for a
/// destination already seen means it was evicted and rebuilt. Comparing
/// <c>Count</c> before and after cannot tell a rebuild from a hit, because an
/// eviction plus a build leaves the count unchanged.
/// </remarks>
internal sealed class CountingFieldSource(IDistanceFieldSource inner) : IDistanceFieldSource, IDistanceFieldView
{
    private readonly Dictionary<int, DistanceField> _last = [];

    /// <summary>Every destination asked for, in order.</summary>
    public List<int> Asked { get; } = [];

    /// <summary>Fields actually computed -- first builds plus rebuilds after eviction.</summary>
    public int Builds { get; private set; }

    public int Count => inner.Count;

    /// <summary>
    /// Itself, so a peek through this wrapper still reaches the cache underneath.
    /// Handing out the inner view instead would be honest too; this way a test
    /// holding the wrapper can ask what is held without naming a second object.
    /// </summary>
    public IDistanceFieldView View => this;

    public DistanceField For(int destination)
    {
        Asked.Add(destination);
        var field = inner.For(destination);

        if (!_last.TryGetValue(destination, out var previous) || !ReferenceEquals(previous, field))
        {
            Builds++;
        }

        _last[destination] = field;
        return field;
    }

    /// <summary>
    /// Forwarded, and deliberately NOT recorded in <see cref="Asked"/>. A peek is
    /// not an ask: counting it here would make this wrapper report the very
    /// disturbance the member exists to avoid.
    /// </summary>
    public bool TryPeek(int destination, [NotNullWhen(true)] out DistanceField? field) =>
        inner.View.TryPeek(destination, out field);
}

/// <summary>
/// The distance-field seam: that a supplied source is really the one used, and
/// that what it is asked is a function of the run and nothing else.
/// </summary>
public sealed class DistanceFieldSourceTests
{
    private static CountingFieldSource Run(Grid grid, int ticks = 40)
    {
        var source = new CountingFieldSource(new FieldCache(grid, MovementSystem.FieldCapacity));
        var system = new MovementSystem(grid, fields: source);

        // Placed and aimed at PASSABLE cells taken from the grid rather than at
        // coordinates that look open on a sketch. The arena's row 1 is wall.
        // A lambda, not a method group: IsPassable has an (x, y) overload too, and
        // the group binds ambiguously against Where's indexed form.
        var open = Enumerable.Range(0, grid.CellCount).Where(cell => grid.IsPassable(cell)).ToArray();
        for (var i = 0; i < 6; i++)
        {
            system.AddAgent(open[i]);
        }

        // Three separate orders, so three destinations are live at once.
        system.Order([0, 1], open[^1]);
        system.Order([2, 3], open[open.Length / 2]);
        system.Order([4, 5], open[open.Length / 3]);

        for (var tick = 0; tick < ticks; tick++)
        {
            system.Tick();
        }

        return source;
    }

    [Fact]
    public void TheSuppliedSourceIsTheOneTheSystemActuallyUses()
    {
        // An interface nothing can be substituted into is decoration. This is the
        // test that the constructor parameter is real.
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));

        var source = Run(grid);

        Assert.NotEmpty(source.Asked);
        Assert.True(source.Builds > 0, "the wrapped cache was never asked to build anything");
    }

    [Fact]
    public void TwoIdenticalRunsAskForTheSameFieldsInTheSameOrder()
    {
        // The interface documents determinism as part of its CONTRACT -- replay
        // must not depend on what happens to be cached -- and until the source was
        // substitutable there was no way to observe whether that held. The ask
        // sequence is now visible, so the claim is testable rather than asserted.
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));

        var first = Run(grid);
        var second = Run(grid);

        Assert.Equal(first.Asked, second.Asked);
        Assert.Equal(first.Builds, second.Builds);
    }

    [Fact]
    public void TheCacheNeverHoldsMoreFieldsThanItsCapacity()
    {
        // A field is one double per cell, so the cap is a memory bound and not a
        // tuning knob. On a 49x49 arena that is 2,401 doubles apiece.
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));

        var source = Run(grid);

        Assert.InRange(source.Count, 1, MovementSystem.FieldCapacity);
    }

    [Fact]
    public void AFieldIsSharedRatherThanRebuiltForEveryAsk()
    {
        // K fields serve N units, and that is the whole economy of the design.
        // Six agents across three orders, forty ticks: builds must be a small
        // fraction of asks, or the cache is not doing its job.
        var grid = Grid.FromMapFile(Fixtures.Map("arena.map"));

        var source = Run(grid);

        Assert.True(
            source.Builds * 4 < source.Asked.Count,
            $"{source.Builds} builds against {source.Asked.Count} asks -- the cache is barely helping");
    }
}
