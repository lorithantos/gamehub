using System.Numerics;

using Raylib_cs;

using RlColor = Raylib_cs.Color;
using RlRectangle = Raylib_cs.Rectangle;

namespace Nav.Viewer.Raylib;

/// <summary>
/// <see cref="IRenderer"/> over raylib.
/// </summary>
/// <remarks>
/// Does not open or close raylib's drawing bracket. <c>BeginDrawing</c> and
/// <c>EndDrawing</c> are tied to the window and its buffer swap, which makes
/// them the host's business — and it leaves the host somewhere to draw its own
/// status chrome after the app's frame. The D3D11 renderer will divide
/// responsibility the same way, with present belonging to the host there too.
/// </remarks>
internal sealed class RaylibRenderer : IRenderer, IDisposable
{
    private Texture2D _texture;
    private TerrainImage? _uploaded;
    private bool _hasTexture;

    public void BeginFrame(RgbaColor clear) => global::Raylib_cs.Raylib.ClearBackground(ToRaylib(clear));

    public void EndFrame()
    {
        // Nothing: the swap belongs to the host.
    }

    public void DrawTerrain(TerrainImage image, RectF destination)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Single-slot cache keyed on the instance. The app never learns that a
        // texture exists, so it never has to know when one is lost either.
        if (!ReferenceEquals(_uploaded, image))
        {
            Upload(image);
        }

        global::Raylib_cs.Raylib.DrawTexturePro(
            _texture,
            new RlRectangle(0, 0, _texture.Width, _texture.Height),
            new RlRectangle(destination.X, destination.Y, destination.Width, destination.Height),
            Vector2.Zero,
            0.0f,
            RlColor.White);
    }

    public void DrawLine(Vector2 from, Vector2 to, float thickness, RgbaColor color) =>
        global::Raylib_cs.Raylib.DrawLineEx(from, to, thickness, ToRaylib(color));

    public void DrawCircle(Vector2 center, float radius, RgbaColor color) =>
        global::Raylib_cs.Raylib.DrawCircleV(center, radius, ToRaylib(color));

    public void Dispose()
    {
        if (_hasTexture)
        {
            global::Raylib_cs.Raylib.UnloadTexture(_texture);
            _hasTexture = false;
        }
    }

    /// <summary>
    /// The conversion lives here rather than beside <see cref="RgbaColor"/>,
    /// because putting it there would drag raylib into the shared project and
    /// undo the whole arrangement.
    /// </summary>
    private static RlColor ToRaylib(RgbaColor color) =>
        new() { R = color.R, G = color.G, B = color.B, A = color.A };

    private void Upload(TerrainImage image)
    {
        Dispose();

        var pixels = image.Pixels;
        var raw = global::Raylib_cs.Raylib.GenImageColor(image.Width, image.Height, RlColor.Black);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var at = ((y * image.Width) + x) * 4;
                var color = new RlColor { R = pixels[at], G = pixels[at + 1], B = pixels[at + 2], A = pixels[at + 3] };
                global::Raylib_cs.Raylib.ImageDrawPixel(ref raw, x, y, color);
            }
        }

        _texture = global::Raylib_cs.Raylib.LoadTextureFromImage(raw);
        global::Raylib_cs.Raylib.UnloadImage(raw);

        // Point filtering: a magnified cell must stay a hard square.
        global::Raylib_cs.Raylib.SetTextureFilter(_texture, TextureFilter.Point);

        _hasTexture = true;
        _uploaded = image;
    }
}
