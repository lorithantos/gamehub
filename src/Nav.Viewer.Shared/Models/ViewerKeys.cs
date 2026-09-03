namespace Nav.Viewer.Models;

/// <summary>
/// The only keyboard commands the viewer has an opinion about, named for what
/// they do rather than for the keycap.
/// </summary>
/// <remarks>
/// Flags, because one snapshot has to carry several at once.
/// <para>
/// Which physical key produces which member is each host's business and appears
/// nowhere in the shared project — moving one would not touch a line of this
/// file.
/// </para>
/// <para>
/// <see cref="Space"/> and <see cref="R"/> are named after their key only
/// because no better verb existed. Treat the summary as the meaning.
/// </para>
/// </remarks>
[Flags]
public enum ViewerKeys
{
    /// <summary>
    /// The zero value a <see cref="FlagsAttribute"/> enum needs, and what
    /// <see cref="InputState.KeysPressed"/> holds on a frame where no key went
    /// down -- which is nearly every frame.
    /// </summary>
    None = 0,

    /// <summary>
    /// Toggle between running and paused. The key that makes edge detection
    /// worth having: held down it must toggle once, not once per frame.
    /// </summary>
    Space = 1,

    /// <summary>
    /// Restart or regroup, depending on what is loaded: a replay goes back to
    /// tick zero with its clock stopped; a live map instead orders every unit,
    /// selected or not, to unit 0's cell.
    /// </summary>
    R = 2,

    /// <summary>
    /// Advance exactly one tick and stay paused. Pressing it while running
    /// pauses first, so a burst can be caught mid-flight and then walked
    /// forward a tick at a time.
    /// </summary>
    Step = 4,

    /// <summary>
    /// Cycle the tick rate: full speed, then two ticks a second, then one.
    /// At sixty ticks a second a group's behaviour is over before it can be
    /// read; slowed down, the same run is legible without anyone having to
    /// hold the step key.
    /// </summary>
    Pace = 8,

    /// <summary>Scroll the window left over the map.</summary>
    /// <remarks>
    /// The four pans and the two zooms exist for the same reason
    /// <see cref="Pace"/> does, one axis over. Pace answers "this is too fast to
    /// read"; these answer "this is too big to read".
    /// <para>
    /// A 512-square fitted to a window is one pixel per cell, which draws every
    /// unit, id, health bar and route into a single dot.
    /// </para>
    /// </remarks>
    PanLeft = 16,

    /// <summary>Scroll the window right over the map.</summary>
    PanRight = 32,

    /// <summary>Scroll the window up over the map.</summary>
    PanUp = 64,

    /// <summary>Scroll the window down over the map.</summary>
    PanDown = 128,

    /// <summary>Larger cells: less map, more legible.</summary>
    ZoomIn = 256,

    /// <summary>Smaller cells, down to the whole map at once.</summary>
    ZoomOut = 512,

    /// <summary>
    /// Back to the whole map in the window, whatever the panning and zooming got
    /// up to. The way out of being lost, which a viewer that scrolls needs and one
    /// that cannot scroll does not.
    /// </summary>
    ResetView = 1024,
}
