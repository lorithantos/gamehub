using System.Numerics;

using Nav.Core;

namespace Nav.Viewer.Models;

/// <summary>
/// The one place grid coordinates and screen pixels are converted.
/// </summary>
/// <remarks>
/// Drawing goes one way and mouse picking goes the other, and writing the
/// transform twice is how they end up half a cell apart.
/// <para>
/// It was already renderer-free before the seam existed, which is why it moved
/// here unchanged. Both hosts and both renderers go through it, so their
/// geometry cannot disagree.
/// </para>
/// <para>
/// <b>The window and the map are two different rectangles</b>, and they were the
/// same one until a map arrived that did not fit. <see cref="PixelWidth"/> and
/// <see cref="PixelHeight"/> are the WINDOW — both hosts size themselves from
/// them and rebuild when they change — while the map's own extent is
/// <see cref="CellSize"/> times the grid, which may be far larger.
/// <see cref="OriginX"/> and <see cref="OriginY"/> say where the map's corner
/// sits inside the window, and go negative once the map is bigger than what is
/// looking at it.
/// </para>
/// </remarks>
/// <param name="CellSize">Pixels per cell.</param>
/// <param name="PixelWidth">Window width. Not the map's width unless they happen to match.</param>
/// <param name="PixelHeight">Window height.</param>
/// <param name="OriginX">Window x of the map's left edge; negative when scrolled past it.</param>
/// <param name="OriginY">Window y of the map's top edge.</param>
public readonly record struct GridLayout(
    int CellSize, int PixelWidth, int PixelHeight, int OriginX = 0, int OriginY = 0)
{
    /// <summary>
    /// The whole map at the largest whole number of pixels per cell that fits the
    /// budget, with the window sized to match it exactly.
    /// </summary>
    /// <remarks>
    /// Whole pixels, so a cell never lands on a half-pixel boundary and
    /// shimmers. Floored at 1, and that floor is the reason
    /// <see cref="Viewing"/> exists: a 512x512 map does render here rather than
    /// vanishing, at one pixel per cell — and every mark the viewer draws is
    /// sized from <see cref="CellSize"/>, so a unit, its id, its health bar and
    /// its route all collapse into that one pixel. The map is visible and the
    /// SIMULATION is not, which is worse than failing.
    /// </remarks>
    public static GridLayout Fit(Grid grid, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var cell = Math.Max(1, Math.Min(maxWidth / grid.Width, maxHeight / grid.Height));
        return new GridLayout(cell, grid.Width * cell, grid.Height * cell);
    }

    /// <summary>
    /// A window of fixed size looking at part of a map, at a chosen scale, centred
    /// on a cell.
    /// </summary>
    /// <remarks>
    /// The map is placed so <paramref name="focusCell"/> sits in the middle of the
    /// window, then clamped: never scrolled so far that empty space shows beyond an
    /// edge, and simply centred on whichever axis the map is smaller than the
    /// window. So a caller may ask to focus anything, including a cell near a
    /// corner, without having to know how the clamping works.
    /// <para>
    /// Asking for the fitted cell size and the fitted window reproduces
    /// <see cref="Fit"/> exactly — the clamp resolves to an origin of zero —
    /// which is why the viewer can compute its layout this way always and have
    /// its unzoomed behaviour be unchanged.
    /// </para>
    /// </remarks>
    /// <param name="grid">The map being looked at.</param>
    /// <param name="cellSize">Pixels per cell. At least one.</param>
    /// <param name="viewWidth">Window width in pixels.</param>
    /// <param name="viewHeight">Window height in pixels.</param>
    /// <param name="focusCell">The cell to put in the middle, clamped as described.</param>
    public static GridLayout Viewing(Grid grid, int cellSize, int viewWidth, int viewHeight, int focusCell)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewHeight, 1);

        var extentX = grid.Width * cellSize;
        var extentY = grid.Height * cellSize;

        var focusX = focusCell >= 0 && focusCell < grid.CellCount ? grid.ColumnOf(focusCell) : grid.Width / 2;
        var focusY = focusCell >= 0 && focusCell < grid.CellCount ? grid.RowOf(focusCell) : grid.Height / 2;

        return new GridLayout(
            cellSize,
            viewWidth,
            viewHeight,
            Anchor(extentX, viewWidth, (int)((focusX + 0.5f) * cellSize)),
            Anchor(extentY, viewHeight, (int)((focusY + 0.5f) * cellSize)));
    }

    /// <summary>
    /// Where one axis of the map starts inside the window: centred when the map is
    /// the smaller, otherwise scrolled to put <paramref name="focus"/> in the
    /// middle without letting the far edge come inside the window.
    /// </summary>
    private static int Anchor(int extent, int view, int focus)
    {
        if (extent <= view)
        {
            return (view - extent) / 2;
        }

        return Math.Clamp((view / 2) - focus, view - extent, 0);
    }

    /// <summary>The map's full width in pixels at this scale, which may exceed the window.</summary>
    public int MapWidth(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid.Width * CellSize;
    }

    /// <summary>The map's full height in pixels at this scale.</summary>
    public int MapHeight(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid.Height * CellSize;
    }

    /// <summary>Screen position of a cell's centre, or of a continuous position between cells.</summary>
    public Vector2 CenterOf(float gridX, float gridY) =>
        new(((gridX + 0.5f) * CellSize) + OriginX, ((gridY + 0.5f) * CellSize) + OriginY);

    /// <summary>The cell under a screen position, or false if that is not over the map.</summary>
    /// <remarks>
    /// Two rejections, not one, and they are different questions. A point outside
    /// the WINDOW is not being pointed at; a point inside the window but off the
    /// MAP is the margin around a map smaller than its window, or the void beyond
    /// an edge. Both answer false, and conflating them would let a click on the
    /// margin pick the nearest cell as though the map extended there.
    /// </remarks>
    public bool TryPick(Vector2 screen, Grid grid, out int cell)
    {
        ArgumentNullException.ThrowIfNull(grid);

        cell = -1;

        if (screen.X < 0 || screen.Y < 0 || screen.X >= PixelWidth || screen.Y >= PixelHeight)
        {
            return false;
        }

        var mapX = screen.X - OriginX;
        var mapY = screen.Y - OriginY;
        if (mapX < 0 || mapY < 0)
        {
            return false;
        }

        var x = (int)(mapX / CellSize);
        var y = (int)(mapY / CellSize);
        if (!grid.InBounds(x, y))
        {
            return false;
        }

        cell = grid.Index(x, y);
        return true;
    }
}
