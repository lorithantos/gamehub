using Nav.Core;
using Nav.Core.Interfaces;

namespace Nav.Viewer.Wpf;

/// <summary>
/// A debug source that asks for its view again every time it is read, so it
/// describes the world being stepped rather than the one that was being stepped
/// when the application was composed.
/// </summary>
/// <remarks>
/// <b>This exists because a world cannot be rewound and a restart therefore
/// builds another one.</b> The session takes a factory for exactly that reason;
/// what it hands back after R is a NEW world, with a new board, a new roster and
/// new health on everybody. A view wrapped once around the first world survives
/// that perfectly well and goes on answering -- with the last numbers the dead
/// world ever had. The panel would sit there reading a fight nobody is playing
/// while the map beside it draws the fight that is actually running, and nothing
/// would look broken.
/// <para>
/// So the indirection is the fix: one closure over the same handle the factory
/// assigns, read at describe time. The host keeps the handle, because the world
/// half of it is a tactics type the viewer cannot name -- see
/// <see cref="IWorldDebugView"/>.
/// </para>
/// <para>
/// It observes and nothing else. Neither member decides anything, and the
/// closure must not either: a source that built a world on the way past would be
/// the panel starting the fight it is supposed to be watching.
/// </para>
/// </remarks>
/// <param name="current">
/// The view for the world as it stands now. Called on every read, so whatever it
/// closes over may be replaced between two of them.
/// </param>
internal sealed class LiveWorldSource(Func<IWorldDebugView> current) : IWorldDebugView
{
    private readonly Func<IWorldDebugView> _current =
        current ?? throw new ArgumentNullException(nameof(current));

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<DebugRow> Describe() => _current().Describe();

    /// <inheritdoc/>
    [Observes]
    public IDebugView DebugFor(int agent) => _current().DebugFor(agent);
}
