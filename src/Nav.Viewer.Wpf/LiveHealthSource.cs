using Nav.Core;

namespace Nav.Viewer.Wpf;

/// <summary>
/// A health view that asks for its view again every time it is read, so the bars
/// on screen belong to the world being stepped rather than to the one that was
/// being stepped when the application was composed.
/// </summary>
/// <remarks>
/// <b>Exactly <see cref="LiveVisibilitySource"/>'s problem, one instrument
/// over.</b> A world cannot be rewound, so a restart builds another one; a view
/// wrapped once around the first survives that perfectly well and goes on
/// answering with the dead world's damage. Every unit in the new fight would
/// then wear the bar of whichever unit happened to hold its id in the old one --
/// and nothing would look broken, which is the whole danger.
/// <para>
/// So the fix is the same one: a closure over the same handle the session's
/// factory assigns, read at draw time. The host keeps the handle, because the
/// world half of it is a tactics type the viewer cannot name.
/// </para>
/// <para>
/// It observes and nothing else, and the closure must not either.
/// </para>
/// </remarks>
/// <param name="current">
/// The view for the world as it stands now. Called on every read, so whatever it
/// closes over may be replaced between two of them.
/// </param>
internal sealed class LiveHealthSource(Func<IHealthView> current) : IHealthView
{
    private readonly Func<IHealthView> _current =
        current ?? throw new ArgumentNullException(nameof(current));

    /// <inheritdoc/>
    [Observes]
    public float HealthOf(int agent) => _current().HealthOf(agent);
}
