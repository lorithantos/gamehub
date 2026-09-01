namespace Nav.Viewer.Models;

/// <summary>
/// The only keyboard commands the viewer has an opinion about, named for what
/// they do rather than for the keycap.
/// </summary>
/// <remarks>
/// Flags, because one snapshot has to be able to carry several at once -- see
/// <see cref="InputState.KeysPressed"/>. Which physical key produces which
/// member is each host's business and appears nowhere in the shared project:
/// <see cref="Step"/> happens to be S in both hosts, and moving it would not
/// touch a line of this file. <see cref="Space"/> and <see cref="R"/> are named
/// after their key only because no better verb existed; treat the summary as the
/// meaning.
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
}
