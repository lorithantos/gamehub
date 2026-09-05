namespace Nav.Viewer.Tactics.Tests;

/// <summary>
/// The tactics half of the health seam: a world's damage as one fraction per
/// agent, with nothing from Nav.Tactics on the way out.
/// </summary>
/// <remarks>
/// What the viewer's own tests cannot reach, exactly as
/// <see cref="DemoWorldVisibilityTests"/> is. They drive the app against a
/// scripted fraction, because the viewer project is compiled with no sight of a
/// world at all -- so "is that the number the world actually holds" can only be
/// asked here, where both halves of the seam are visible at once.
/// </remarks>
public sealed class DemoWorldHealthTests
{
    private const string Room =
        """
        type octile
        height 5
        width 5
        map
        .....
        .....
        .....
        .....
        .....
        """;

    /// <summary>
    /// One unit standing in an empty room. NO KIT AND NO COMBAT TABLE: health is a
    /// fact the world keeps about an agent whether or not it can shoot, so a
    /// scene that needed a weapon to ask after it would be measuring the wrong
    /// thing.
    /// </summary>
    private static (DemoWorld World, int Agent) Scene()
    {
        var grid = Grid.FromMapText(Room);
        var system = new MovementSystem(grid);
        var world = new DemoWorld(grid);

        var agent = system.AddAgent(grid.Index(2, 2), 0);
        world.Listen(system);

        return (world, agent);
    }

    [Fact]
    public void TheFractionIsTheWorldsOwnAndFallsWithTheDamageTheWorldTook()
    {
        var (world, agent) = Scene();
        var health = new DemoWorldHealth(world);

        Assert.Equal(1.0f, health.HealthOf(agent));

        // Not a rounding, not a rescaling and not a maximum looked up anywhere:
        // whatever the world says is left is what crosses, so a bar is drawn
        // from the same number a doctrine retreats on.
        world.SetHealth(agent, 0.4);
        Assert.Equal(0.4f, health.HealthOf(agent), 5);

        world.Damage(agent, 0.25);
        Assert.Equal(0.15f, health.HealthOf(agent), 5);
    }

    [Fact]
    public void AnIdTheWorldNeverHeardOfIsWhole()
    {
        var (world, _) = Scene();
        var health = new DemoWorldHealth(world);

        // THE SAME ANSWER THE LAYER BELOW GIVES A STRANGER -- see
        // IPerception.HealthOf -- rather than a second convention for the same
        // question. A view that answered 0 would put an empty bar over every
        // unit the world has not started tracking yet, which reads as a fight
        // going badly rather than as a unit that has taken nothing.
        Assert.Equal(1.0f, health.HealthOf(9999));
    }
}
