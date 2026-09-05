namespace Nav.Viewer.Models;

/// <summary>
/// How a side's belief is DEPICTED: how dark ground it cannot see goes, how a
/// sighting fades as it ages, and what a pad it has found is painted with.
/// </summary>
/// <remarks>
/// <b>THE VIEWER'S FOG, NOT THE WORLD'S.</b> Two different things in this
/// project answer to the word. The world's fog decides what a doctrine KNOWS,
/// and moving it moves every measured number. This one decides only how that
/// knowledge is drawn: no simulation value depends on any member here, so a host
/// is free to hand over a different one and read the same fight a different way.
/// <para>
/// Which is the reason it is one type rather than five constants. The
/// alternatives -- fade by age, or do not fade and say when the sighting was
/// taken, or drop a ghost past a threshold, or dim nothing and mark the seen
/// cells instead -- stop being features to build and become values to pass. None
/// of them exists yet. <see cref="Default"/> is the only row, and it draws what
/// the viewer has always drawn.
/// </para>
/// <para>
/// A host that wants one member changed says
/// <c>FogStyle.Default with { Dim = 1.0f }</c> and inherits the reasoning behind
/// the other four.
/// </para>
/// </remarks>
public sealed record FogStyle
{
    /// <summary>
    /// What the viewer draws when nobody said otherwise, which is every host
    /// today.
    /// </summary>
    /// <remarks>
    /// Each member carries its own value and the argument for it, so this is the
    /// whole row and not a second place a number is written down.
    /// </remarks>
    public static FogStyle Default { get; } = new();

    /// <summary>
    /// How dark ground a side cannot see is drawn, as a multiplier on every
    /// colour channel.
    /// </summary>
    /// <remarks>
    /// Dark enough that the seen ring reads as the shape it is at a glance, and
    /// light enough that the walls are still walls -- a watcher has to be able to
    /// see the map a side is moving over, or the fog view is only useful for
    /// counting units.
    /// </remarks>
    public float Dim { get; init; } = 0.28f;

    /// <summary>
    /// How many ticks it takes a sighting to fade all the way into the fog.
    /// </summary>
    /// <remarks>
    /// A DISPLAY value and emphatically not a doctrine's forgetting time.
    /// Nothing here decides when a side stops believing a ghost -- that is the
    /// doctrine's call, and a patrol and a guard have every reason to answer
    /// differently. This only says how long a ghost stays legible on screen, so
    /// that "seen a moment ago" and "seen a minute ago" do not look alike.
    /// </remarks>
    public int GhostFade { get; init; } = 120;

    /// <summary>A sighting taken this tick: one end of the ramp.</summary>
    public RgbaColor GhostFresh { get; init; } = RgbaColor.Rgb(190, 120, 200);

    /// <summary>
    /// A sighting <see cref="GhostFade"/> ticks old or older: all but gone, and
    /// the other end of the ramp.
    /// </summary>
    public RgbaColor GhostStale { get; init; } = RgbaColor.Rgb(60, 45, 65);

    /// <summary>A pad a side can see, painted into the fog image.</summary>
    public RgbaColor Pad { get; init; } = RgbaColor.Rgb(60, 150, 90);
}
