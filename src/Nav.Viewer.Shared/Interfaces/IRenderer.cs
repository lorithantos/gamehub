using System.Numerics;

namespace Nav.Viewer.Interfaces;

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
    /// <summary>
    /// Opens the frame and clears the whole target to <paramref name="clear"/>.
    /// Every other verb here is only meaningful between this call and
    /// <see cref="EndFrame"/>.
    /// </summary>
    /// <remarks>
    /// Clearing and opening are one verb rather than two because a renderer that
    /// batches has to be told a frame started: the D3D11 implementation drops
    /// last frame's accumulated geometry here and binds its entire pipeline
    /// state, and a separate <c>Clear</c> would have let a caller draw before any
    /// of that ran.
    /// </remarks>
    void BeginFrame(RgbaColor clear);

    /// <summary>
    /// Closes the frame. A renderer that batches submits here, so anything drawn
    /// since <see cref="BeginFrame"/> is not guaranteed to have reached the
    /// device until this returns.
    /// </summary>
    /// <remarks>
    /// It is deliberately not a present. The buffer swap belongs to the host --
    /// which is also where the status chrome is drawn, after the app's frame --
    /// so the raylib implementation has nothing at all to do here while the
    /// D3D11 one flushes a whole frame's worth of lines and circles in one draw.
    /// </remarks>
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

    /// <summary>
    /// A segment <paramref name="thickness"/> pixels wide, centred on the line.
    /// </summary>
    /// <remarks>
    /// Both real renderers expand it into a quad and leave joints unmitred, so a
    /// chain of segments shows the same small notch on a turn either way -- which
    /// is what lets a route drawn by one host be compared against the other.
    /// </remarks>
    void DrawLine(Vector2 from, Vector2 to, float thickness, RgbaColor color);

    /// <summary>
    /// A <em>filled</em> disc. There is no stroke, so a ring is drawn as a
    /// smaller disc on top -- which is how the viewer marks a selected unit and
    /// a leader without the interface growing a sixth verb.
    /// </summary>
    void DrawCircle(Vector2 center, float radius, RgbaColor color);
}
