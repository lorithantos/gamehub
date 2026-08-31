using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The map as pixels, one texel per cell, on the CPU.
/// </summary>
/// <remarks>
/// RGBA8, row-major, immutable. It is the renderer's job to upload this and to
/// decide when — and that division is what keeps device loss out of the app: a
/// renderer whose device was reset re-uploads from an image it still holds,
/// and nothing above it hears about the reset.
/// <para>
/// One texel per cell rather than per pixel is the whole performance story. A
/// 512x512 map is 262,144 cells; magnifying a 512x512 texture costs the same as
/// magnifying a 4x4 one, while a draw call per cell does not survive 60Hz.
/// </para>
/// </remarks>
public sealed class TerrainImage
{
    private readonly byte[] _pixels;

    private TerrainImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row. RGBA8, so four per texel.</summary>
    public int Stride => Width * 4;

    public ReadOnlySpan<byte> Pixels => _pixels;

    public static TerrainImage FromGrid(Grid grid, RgbaColor passable, RgbaColor blocked)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var pixels = new byte[grid.CellCount * 4];
        for (var i = 0; i < grid.CellCount; i++)
        {
            var color = grid.IsPassable(i) ? passable : blocked;
            var at = i * 4;
            pixels[at] = color.R;
            pixels[at + 1] = color.G;
            pixels[at + 2] = color.B;
            pixels[at + 3] = color.A;
        }

        return new TerrainImage(grid.Width, grid.Height, pixels);
    }
}
