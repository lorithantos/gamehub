using System.Numerics;

using Nav.Core;

using Raylib_cs;

namespace Nav.Viewer;

/// <summary>
/// The map as a single texture, one texel per cell, uploaded once.
/// </summary>
/// <remarks>
/// A 512x512 map is 262,144 cells. A draw call each is not viable at 60 Hz, and
/// the terrain does not change between frames anyway, so it is built once and
/// blitted. Point filtering keeps a cell a crisp square when it is scaled up
/// rather than a blur.
/// </remarks>
internal sealed class TerrainLayer : IDisposable
{
    private static readonly Color Passable = Color.RayWhite;
    private static readonly Color Blocked = Color.DarkGray;

    private readonly Texture2D _texture;
    private bool _disposed;

    public TerrainLayer(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var image = Raylib.GenImageColor(grid.Width, grid.Height, Blocked);
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (grid.IsPassable(x, y))
                {
                    Raylib.ImageDrawPixel(ref image, x, y, Passable);
                }
            }
        }

        _texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        Raylib.SetTextureFilter(_texture, TextureFilter.Point);
    }

    public void Draw(GridLayout layout) =>
        Raylib.DrawTexturePro(
            _texture,
            new Rectangle(0, 0, _texture.Width, _texture.Height),
            new Rectangle(0, 0, layout.PixelWidth, layout.PixelHeight),
            Vector2.Zero,
            0.0f,
            Color.White);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Raylib.UnloadTexture(_texture);
        _disposed = true;
    }
}
