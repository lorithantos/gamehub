using System.Numerics;

namespace Nav.Viewer.Tests;

/// <summary>One call to <see cref="IRenderer"/>, kept.</summary>
public abstract record DrawCommand
{
    public sealed record BeginFrame(RgbaColor Clear) : DrawCommand;

    public sealed record EndFrame : DrawCommand;

    public sealed record Terrain(TerrainImage Image, RectF Destination) : DrawCommand;

    public sealed record Line(Vector2 From, Vector2 To, float Thickness, RgbaColor Color) : DrawCommand;

    public sealed record Circle(Vector2 Center, float Radius, RgbaColor Color) : DrawCommand;
}

/// <summary>
/// An <see cref="IRenderer"/> that draws nothing and remembers everything.
/// </summary>
/// <remarks>
/// The cheapest possible second implementation, and it makes the app's output
/// assertable: what the viewer decided to draw becomes a list of values instead
/// of pixels on a device nobody can query.
/// <para>
/// It is deliberately not a mock with expectations. Asserting that
/// <c>DrawCircle</c> "was called" tests the double; asserting that the circle
/// landed at the centre <see cref="GridLayout"/> predicts tests the viewer.
/// </para>
/// </remarks>
public sealed class RecordingRenderer : IRenderer
{
    private readonly List<DrawCommand> _commands = [];

    public IReadOnlyList<DrawCommand> Commands => _commands;

    public int FrameCount { get; private set; }

    /// <summary>Commands recorded since the last <see cref="IRenderer.BeginFrame"/>.</summary>
    public IReadOnlyList<DrawCommand> LastFrame
    {
        get
        {
            var start = _commands.FindLastIndex(c => c is DrawCommand.BeginFrame);
            return start < 0 ? [] : _commands.Skip(start).ToList();
        }
    }

    public IEnumerable<T> OfKind<T>()
        where T : DrawCommand => _commands.OfType<T>();

    public IEnumerable<T> LastFrameOfKind<T>()
        where T : DrawCommand => LastFrame.OfType<T>();

    public void Clear()
    {
        _commands.Clear();
        FrameCount = 0;
    }

    public void BeginFrame(RgbaColor clear) => _commands.Add(new DrawCommand.BeginFrame(clear));

    public void EndFrame()
    {
        _commands.Add(new DrawCommand.EndFrame());
        FrameCount++;
    }

    public void DrawTerrain(TerrainImage image, RectF destination) =>
        _commands.Add(new DrawCommand.Terrain(image, destination));

    public void DrawLine(Vector2 from, Vector2 to, float thickness, RgbaColor color) =>
        _commands.Add(new DrawCommand.Line(from, to, thickness, color));

    public void DrawCircle(Vector2 center, float radius, RgbaColor color) =>
        _commands.Add(new DrawCommand.Circle(center, radius, color));
}
