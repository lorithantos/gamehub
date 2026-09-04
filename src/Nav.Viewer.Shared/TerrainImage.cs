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

    /// <summary>Texels across, which is the grid's width in <em>cells</em> -- one texel per cell, never per pixel.</summary>
    public int Width { get; }

    /// <summary>Texels down, likewise the grid's height in cells.</summary>
    public int Height { get; }

    /// <summary>Bytes per row. RGBA8, so four per texel.</summary>
    public int Stride => Width * 4;

    /// <summary>
    /// The rows top to bottom, <see cref="Stride"/> bytes each: RGBA8, row-major,
    /// and always exactly <c>Width * Height * 4</c> bytes long.
    /// </summary>
    /// <remarks>
    /// A span rather than an array, so a renderer walks it straight into a texture
    /// with no copy and no way to write back.
    /// <para>
    /// It stays valid for the lifetime of this instance, which is what lets a
    /// renderer whose device was reset re-upload from an image it still holds.
    /// </para>
    /// </remarks>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>
    /// Paints every cell once in row-major index order: <paramref name="passable"/>
    /// wherever <see cref="Grid.IsPassable(int)"/> is true and
    /// <paramref name="blocked"/> everywhere else, alpha included.
    /// </summary>
    /// <remarks>
    /// Always a NEW instance, and that matters: both renderers key their
    /// single-slot upload cache on <em>reference</em> identity, so a fresh object
    /// is how the app says "the map changed, re-upload".
    /// <para>
    /// So calling this once per frame rebuilds the texture once per frame. The
    /// app calls it only when a load bumps the session's version.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The same map painted through ONE SIDE's eyes: cells it can see in the
    /// ordinary colours, a pad it can see in <paramref name="pad"/>, and
    /// everything else dimmed toward black by <paramref name="dim"/>.
    /// </summary>
    /// <remarks>
    /// <b>Fog is a second image rather than a sixth renderer verb.</b> One texel
    /// per cell is exactly the granularity fog has, both renderers already cache
    /// a terrain upload by reference identity, and a textured quad drawn after
    /// the map and before the units lands under the units in BOTH hosts -- which
    /// is the only ordering the two agree on, because the D3D11 one batches every
    /// line and circle and flushes them at the end of the frame.
    /// <para>
    /// <b>Fully opaque, and a whole map rather than a mask.</b> Nothing here is
    /// translucent: it repaints every cell, seen ones included, so it covers the
    /// terrain underneath instead of tinting it. A mask with transparent holes
    /// would depend on a blend state neither renderer promises.
    /// </para>
    /// <para>
    /// <b>A pad is drawn only if it is in <paramref name="pads"/>, which is the
    /// point.</b> A side plans its retreat to ground it has actually found, so a
    /// pad it cannot see must not be on the picture drawn through its eyes.
    /// </para>
    /// <para>
    /// Always a NEW instance, like <see cref="FromGrid"/>, so calling it is how
    /// the app says re-upload. The app calls it only when the visible set it was
    /// last built from stops matching the one it is handed.
    /// </para>
    /// </remarks>
    /// <param name="grid">The map being drawn.</param>
    /// <param name="visible">Cells the side can see. Anything outside the grid is ignored.</param>
    /// <param name="pads">Repair cells the side can see, drawn over the rest.</param>
    /// <param name="passable">Open ground the side can see.</param>
    /// <param name="blocked">Wall the side can see.</param>
    /// <param name="pad">A repair cell the side can see.</param>
    /// <param name="dim">What a channel is multiplied by where the side cannot see. 0 is black, 1 is no fog.</param>
    public static TerrainImage Fogged(
        Grid grid,
        IReadOnlyList<int> visible,
        IReadOnlyList<int> pads,
        RgbaColor passable,
        RgbaColor blocked,
        RgbaColor pad,
        float dim)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(pads);

        var dark = Math.Clamp(dim, 0f, 1f);
        var pixels = new byte[grid.CellCount * 4];
        for (var i = 0; i < grid.CellCount; i++)
        {
            Paint(pixels, i, Dimmed(grid.IsPassable(i) ? passable : blocked, dark));
        }

        foreach (var cell in visible)
        {
            if (cell >= 0 && cell < grid.CellCount)
            {
                Paint(pixels, cell, grid.IsPassable(cell) ? passable : blocked);
            }
        }

        foreach (var cell in pads)
        {
            if (cell >= 0 && cell < grid.CellCount)
            {
                Paint(pixels, cell, pad);
            }
        }

        return new TerrainImage(grid.Width, grid.Height, pixels);
    }

    private static void Paint(byte[] pixels, int cell, RgbaColor color)
    {
        var at = cell * 4;
        pixels[at] = color.R;
        pixels[at + 1] = color.G;
        pixels[at + 2] = color.B;
        pixels[at + 3] = color.A;
    }

    /// <summary>The colour with every channel but alpha scaled, so fogged ground stays OPAQUE ground.</summary>
    private static RgbaColor Dimmed(RgbaColor color, float scale) =>
        new((byte)(color.R * scale), (byte)(color.G * scale), (byte)(color.B * scale), color.A);
}
