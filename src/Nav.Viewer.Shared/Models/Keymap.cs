namespace Nav.Viewer.Models;

/// <summary>
/// Which keycap does what: <see cref="PhysicalKey"/> in, <see cref="ViewerKeys"/>
/// out.
/// </summary>
/// <remarks>
/// Built when a third rebindable action arrived. Before that each host owned a
/// switch from its own key type straight onto <see cref="ViewerKeys"/> — a
/// binding written where nothing could rebind it, and written twice, once per
/// host, free to drift apart — and the status line's hints were a literal
/// string, so the two hosts could disagree about a key and the hint could not be
/// wrong about one, it could only be wrong about both.
/// <para>
/// Immutable. <see cref="Rebound"/> returns a new map rather than editing this
/// one, so a map handed to an app cannot change underneath the hints generated
/// from it.
/// </para>
/// <para>
/// <b>The extension point is the constructor.</b> Nothing here reads or writes a
/// file: a bindings loader would parse one, fold it over
/// <see cref="Default"/> with <see cref="Rebound"/>, and hand the result to
/// <see cref="ViewerApp"/>. Adding that later touches no other file.
/// </para>
/// </remarks>
public sealed class Keymap
{
    /// <summary>
    /// What each key is called on screen. Separate from the binding, because the
    /// keycap's name does not change when what it does changes.
    /// </summary>
    private static readonly IReadOnlyDictionary<PhysicalKey, string> Keycaps =
        new Dictionary<PhysicalKey, string>
        {
            [PhysicalKey.Space] = "SPACE",
            [PhysicalKey.R] = "R",
            [PhysicalKey.S] = "S",
            [PhysicalKey.T] = "T",
            [PhysicalKey.V] = "V",
            [PhysicalKey.P] = "P",
            [PhysicalKey.L] = "L",
            [PhysicalKey.Left] = "LEFT",
            [PhysicalKey.Right] = "RIGHT",
            [PhysicalKey.Up] = "UP",
            [PhysicalKey.Down] = "DOWN",
            [PhysicalKey.Plus] = "+",
            [PhysicalKey.Minus] = "-",
            [PhysicalKey.Home] = "HOME",
        };

    private readonly IReadOnlyDictionary<PhysicalKey, ViewerKeys> _bindings;

    private Keymap(IReadOnlyDictionary<PhysicalKey, ViewerKeys> bindings) => _bindings = bindings;

    /// <summary>
    /// The keys the viewer shipped with, to the letter. A user who rebinds
    /// nothing must notice nothing, so this is checked against the hard-coded
    /// switches it replaced rather than re-derived from taste.
    /// </summary>
    /// <remarks>
    /// V, P and L are the three that did not exist before, chosen for the word:
    /// viewpoint, path, line of sight. They are bound and carried, and they do
    /// nothing yet.
    /// </remarks>
    public static Keymap Default { get; } = new(
        new Dictionary<PhysicalKey, ViewerKeys>
        {
            [PhysicalKey.Space] = ViewerKeys.Space,
            [PhysicalKey.R] = ViewerKeys.R,
            [PhysicalKey.S] = ViewerKeys.Step,
            [PhysicalKey.T] = ViewerKeys.Pace,
            [PhysicalKey.Left] = ViewerKeys.PanLeft,
            [PhysicalKey.Right] = ViewerKeys.PanRight,
            [PhysicalKey.Up] = ViewerKeys.PanUp,
            [PhysicalKey.Down] = ViewerKeys.PanDown,
            [PhysicalKey.Plus] = ViewerKeys.ZoomIn,
            [PhysicalKey.Minus] = ViewerKeys.ZoomOut,
            [PhysicalKey.Home] = ViewerKeys.ResetView,
            [PhysicalKey.V] = ViewerKeys.Viewpoint,
            [PhysicalKey.P] = ViewerKeys.PathOverlay,
            [PhysicalKey.L] = ViewerKeys.LosOverlay,
        });

    /// <summary>
    /// What <paramref name="key"/> does, or <see cref="ViewerKeys.None"/> for a
    /// key nothing is bound to.
    /// </summary>
    /// <remarks>
    /// None is a usable answer rather than a failure: hosts feed it straight to
    /// <see cref="InputAccumulator.SetKeyState"/>, where an empty mask sets and
    /// clears nothing. That is what lets a host translate every key it sees
    /// without first asking whether the viewer wants it.
    /// </remarks>
    public ViewerKeys Action(PhysicalKey key) =>
        _bindings.TryGetValue(key, out var action) ? action : ViewerKeys.None;

    /// <summary>
    /// This map with <paramref name="key"/> doing <paramref name="action"/>
    /// instead. Binding it to <see cref="ViewerKeys.None"/> unbinds it.
    /// </summary>
    public Keymap Rebound(PhysicalKey key, ViewerKeys action)
    {
        var bindings = new Dictionary<PhysicalKey, ViewerKeys>(_bindings) { [key] = action };
        return new Keymap(bindings);
    }

    /// <summary>
    /// The keycap to print for <paramref name="action"/>, or <c>"-"</c> when
    /// nothing is bound to it -- so a hint generated from this map cannot claim a
    /// key that no longer does the thing.
    /// </summary>
    /// <remarks>
    /// The first binding in <see cref="PhysicalKey"/> order wins, so an action
    /// reachable from two keys names one of them and names the same one every
    /// frame. A hint that alternated would change the status line's length,
    /// which shakes a window sized to its content.
    /// </remarks>
    public string KeycapFor(ViewerKeys action)
    {
        var found = PhysicalKey.None;
        foreach (var (key, bound) in _bindings)
        {
            if (bound == action && (found == PhysicalKey.None || key < found))
            {
                found = key;
            }
        }

        return Keycaps.TryGetValue(found, out var cap) ? cap : "-";
    }
}
