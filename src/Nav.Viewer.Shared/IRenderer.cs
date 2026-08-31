using System.Numerics;

namespace Nav.Viewer;

/// <summary>
/// Everything the viewer knows how to draw.
/// </summary>
/// <remarks>
/// Five verbs, and the list is deliberately shorter than it looks like it should
/// be.
/// <para>
/// <b>There is no text.</b> The app owns the status <em>string</em>
/// (<see cref="IViewerApp.StatusText"/>); each host owns its presentation. WPF
/// binds it to a TextBlock, raylib draws it with its own bitmap font. That is
/// not a convenience -- raw Direct3D 11 cannot draw a glyph at all, and putting
/// text here would have forced either DirectWrite interop or a glyph atlas into
/// a milestone that needs neither.
/// </para>
/// <para>
/// There is no <c>Resize</c>, no <c>CreateTexture</c> and no <c>Dispose</c>
/// either. Those are device and window concerns; on this interface they would
/// leak the host back into the app it is supposed to be separated from.
/// </para>
/// </remarks>
public interface IRenderer
{
    void BeginFrame(RgbaColor clear);

    void EndFrame();

    /// <summary>
    /// Draws the map, scaled to <paramref name="destination"/> with
    /// nearest-neighbour sampling so a magnified cell stays a hard square.
    /// </summary>
    /// <remarks>
    /// Takes the CPU-side image every frame rather than a handle the app holds.
    /// The renderer decides when to upload and keeps its own cache, which is
    /// what keeps device loss from ever becoming an app-level concept.
    /// </remarks>
    void DrawTerrain(TerrainImage image, RectF destination);

    void DrawLine(Vector2 from, Vector2 to, float thickness, RgbaColor color);

    void DrawCircle(Vector2 center, float radius, RgbaColor color);
}
