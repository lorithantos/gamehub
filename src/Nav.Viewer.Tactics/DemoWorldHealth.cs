using Nav.Core;

namespace Nav.Viewer.Tactics;

/// <summary>
/// A tactics world's damage as the viewer can hold it: one fraction per agent,
/// with no kit, no armour and no world on the way out.
/// </summary>
/// <remarks>
/// <b>The third crossing of the same seam <see cref="DemoWorldDebugView"/> and
/// <see cref="DemoWorldVisibility"/> cross</b>, for the one number a bar needs. A
/// world goes in and <see cref="IHealthView"/> comes out; the viewer above never
/// learns what took the health off, because Nav.Viewer.Shared has no reference it
/// could learn through. This project is the only one that can hold both, which is
/// why the translation happens here.
/// <para>
/// <b>Named for what it wraps, where its interface is named for what it
/// promises.</b> Same as the other two, and the same reason: a caller picking
/// this out of a composition is the only thing that has to know a
/// <see cref="DemoWorld"/> is behind it.
/// </para>
/// <para>
/// <b>The world already answers in a fraction</b> --
/// <see cref="DemoWorld.HealthOf"/> is 1 for undamaged, 0 for destroyed and 1
/// for an id it has never heard of. So this narrows a double to a float and does
/// nothing else: no maximum is looked up and no arithmetic is done, which is
/// what keeps a display out of the business of deciding how hurt anything is.
/// </para>
/// <para>
/// <b>It observes.</b> Reading a health cannot move a world, and the walk holds
/// this to it through <see cref="IHealthView"/>'s own marking.
/// </para>
/// </remarks>
public sealed class DemoWorldHealth : IHealthView
{
    private readonly DemoWorld _world;

    /// <param name="world">The world to read. Never written to.</param>
    public DemoWorldHealth(DemoWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <inheritdoc/>
    [Observes]
    public float HealthOf(int agent) => (float)_world.HealthOf(agent);
}
