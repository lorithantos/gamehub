using System.Numerics;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The one place grid coordinates and screen pixels are converted.
/// </summary>
/// <remarks>
/// Drawing goes one way and mouse picking goes the other, and writing the
/// transform twice is how they end up half a cell apart.
/// </remarks>
internal readonly record struct GridLayout(int CellSize, int PixelWidth, int PixelHeight)
{
    /// <summary>
    /// The largest whole number of pixels per cell that fits the budget.
    /// </summary>
    /// <remarks>
    /// Whole pixels, so a cell never lands on a half-pixel boundary and shimmers.
    /// Floored at 1: a 512x512 map renders at one pixel per cell rather than
    /// vanishing.
    /// </remarks>
    public static GridLayout Fit(Grid grid, int maxWidth, int maxHeight)
    {
        var cell = Math.Max(1, Math.Min(maxWidth / grid.Width, maxHeight / grid.Height));
        return new GridLayout(cell, grid.Width * cell, grid.Height * cell);
    }

    /// <summary>Screen position of the centre of a cell, or of a continuous position between cells.</summary>
    public Vector2 CenterOf(float gridX, float gridY) =>
        new((gridX + 0.5f) * CellSize, (gridY + 0.5f) * CellSize);

    /// <summary>The cell under a screen position, or false if that is not over the map.</summary>
    public bool TryPick(Vector2 screen, Grid grid, out int cell)
    {
        cell = -1;

        if (screen.X < 0 || screen.Y < 0 || screen.X >= PixelWidth || screen.Y >= PixelHeight)
        {
            return false;
        }

        var x = (int)(screen.X / CellSize);
        var y = (int)(screen.Y / CellSize);
        if (!grid.InBounds(x, y))
        {
            return false;
        }

        cell = grid.Index(x, y);
        return true;
    }
}
