namespace Nav.Viewer.Models;

/// <summary>
/// The two mouse buttons the viewer reads, in the RTS convention: left selects,
/// right orders.
/// </summary>
/// <remarks>
/// Flags, like <see cref="ViewerKeys"/>, and reported by <see cref="InputState"/>
/// twice over -- once as an edge in <see cref="InputState.ButtonsPressed"/> and
/// once as held state in <see cref="InputState.ButtonsDown"/> -- because a drag
/// needs to know both that it began and that it is still going.
/// </remarks>
[Flags]
public enum MouseButtons
{
    /// <summary>
    /// The zero value a <see cref="FlagsAttribute"/> enum needs. In
    /// <see cref="InputState.ButtonsPressed"/> it means nothing was clicked this
    /// frame; in <see cref="InputState.ButtonsDown"/> it means nothing is held,
    /// which is how a drag learns it has been released.
    /// </summary>
    None = 0,

    /// <summary>
    /// Select. The press picks the nearest unit at once; if the pointer then
    /// moves more than a few pixels while the button stays down, the release
    /// replaces that with everything inside the drag box -- and boxing empty
    /// ground clears the selection.
    /// </summary>
    Left = 1,

    /// <summary>
    /// Order: send the current selection to the cell under the pointer. Ignored
    /// off the map, and a no-op with nothing selected.
    /// </summary>
    Right = 2,
}
