namespace Nav.Viewer;

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
    public static RgbaColor Rgb(byte r, byte g, byte b) => new(r, g, b, 255);

    public static readonly RgbaColor Black = Rgb(0, 0, 0);
    public static readonly RgbaColor White = Rgb(255, 255, 255);
    public static readonly RgbaColor RayWhite = Rgb(245, 245, 245);
    public static readonly RgbaColor DarkGray = Rgb(80, 80, 80);
    public static readonly RgbaColor SkyBlue = Rgb(102, 191, 255);
    public static readonly RgbaColor Green = Rgb(0, 228, 48);
    public static readonly RgbaColor Red = Rgb(230, 41, 55);
    public static readonly RgbaColor Orange = Rgb(255, 161, 0);
}
