using Nav.Core;

namespace Nav.Viewer.Wpf;

/// <summary>
/// A visibility view that asks for its view again every time it is read, so the
/// fog on screen belongs to the world being stepped rather than to the one that
/// was being stepped when the application was composed.
/// </summary>
/// <remarks>
/// <b>Exactly <see cref="LiveWorldSource"/>'s problem, one instrument over.</b> A
/// world cannot be rewound, so a restart builds another one; a view wrapped once
/// around the first survives that perfectly well and goes on answering with the
/// dead world's sightings. The map would then be drawn through the eyes of a
/// side in a fight nobody is playing, and nothing would look broken -- which is
/// worse here than in the panel, because fog decides what is on screen at all.
/// <para>
/// So the fix is the same one: a closure over the same handle the session's
/// factory assigns, read at draw time. The host keeps the handle, because the
/// world half of it is a tactics type the viewer cannot name.
/// </para>
/// <para>
/// It observes and nothing else, and the closure must not either. A viewer that
/// built a world on the way past would be the instrument starting the fight it
/// is supposed to be watching.
/// </para>
/// </remarks>
/// <param name="current">
/// The view for the world as it stands now. Called on every read, so whatever it
/// closes over may be replaced between two of them.
/// </param>
internal sealed class LiveVisibilitySource(Func<IVisibilityView> current) : IVisibilityView
{
    private readonly Func<IVisibilityView> _current =
        current ?? throw new ArgumentNullException(nameof(current));

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<int> Sides => _current().Sides;

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<int> VisibleCells(int side) => _current().VisibleCells(side);

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<int> RepairPoints(int side) => _current().RepairPoints(side);

    /// <inheritdoc/>
    [Observes]
    public IReadOnlyList<RememberedUnit> Remembered(int side) => _current().Remembered(side);
}
