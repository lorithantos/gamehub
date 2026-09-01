namespace Nav.Viewer.Models;

/// <summary>
/// A colour, in the viewer's own vocabulary.
/// </summary>
/// <remarks>
/// Exists so neither renderer's colour type reaches the app. Conversions to
/// <c>Raylib_cs.Color</c> and to a D3D11 <c>Color4</c> live in each renderer's
/// own file, never here.
/// <para>
/// The named values are raylib's palette byte for byte, read out of
/// <c>Raylib_cs.Color</c> rather than eyeballed. Matching exactly is what makes
/// comparing the two renderers side by side a test rather than a colour-picking
/// exercise -- any visible difference is then a real difference.
/// </para>
/// </remarks>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    /// <summary>
    /// Fully opaque: alpha is 255. The primary constructor is the only way to ask
    /// for anything else, so a translucent colour is always deliberate.
    /// </summary>
    public static RgbaColor Rgb(byte r, byte g, byte b) => new(r, g, b, 255);

    /// <summary>
    /// 0,0,0 -- what the frame is cleared to, and the ink for the dots drawn over
    /// a unit to mark it selected or a leader.
    /// </summary>
    public static readonly RgbaColor Black = Rgb(0, 0, 0);

    /// <summary>
    /// 255,255,255. Distinct from <see cref="RayWhite"/> by ten counts per
    /// channel; raylib carries both, so this palette does too. Nothing in the
    /// viewer currently draws with it.
    /// </summary>
    public static readonly RgbaColor White = Rgb(255, 255, 255);

    /// <summary>
    /// 245,245,245 -- raylib's off-white, and the viewer's passable terrain.
    /// </summary>
    public static readonly RgbaColor RayWhite = Rgb(245, 245, 245);

    /// <summary>
    /// 80,80,80 -- blocked terrain. Dark enough that the per-unit hues, which are
    /// all lifted a third of the way toward white, read against it.
    /// </summary>
    public static readonly RgbaColor DarkGray = Rgb(80, 80, 80);

    /// <summary>
    /// 102,191,255 -- the sole selected unit's route, and the four lines of the
    /// drag band. The only colour the viewer uses for something that is not a
    /// unit or the ground.
    /// </summary>
    public static readonly RgbaColor SkyBlue = Rgb(102, 191, 255);

    /// <summary>
    /// 0,228,48 -- raylib's green, far more saturated than most stock palettes'.
    /// Nothing in the viewer currently draws with it.
    /// </summary>
    public static readonly RgbaColor Green = Rgb(0, 228, 48);

    /// <summary>
    /// 230,41,55 -- a unit that is blocked and actively replanning. A unit that
    /// is blocked but merely queued gets a dimmer, desaturated red instead, so
    /// "waiting" can never be mistaken for "refused".
    /// </summary>
    public static readonly RgbaColor Red = Rgb(230, 41, 55);

    /// <summary>
    /// 255,161,0 -- raylib's orange. Nothing in the viewer currently draws with
    /// it.
    /// </summary>
    public static readonly RgbaColor Orange = Rgb(255, 161, 0);
}
