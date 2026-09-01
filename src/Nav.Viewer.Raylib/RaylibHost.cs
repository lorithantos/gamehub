using Raylib_cs;

using RlColor = Raylib_cs.Color;

namespace Nav.Viewer.Raylib;

/// <summary>
/// Window and loop, raylib's way: the application owns the loop.
/// </summary>
/// <remarks>
/// This is one half of the control-inversion test. Here the host runs
/// <c>while (!WindowShouldClose())</c> and calls the app; the WPF host will be
/// called <em>by</em> its framework instead. The app cannot tell the difference,
/// which is the point.
/// <para>
/// Input is polled with the <c>IsKeyDown</c> family rather than
/// <c>IsKeyPressed</c>, because <see cref="InputAccumulator"/> derives the edges.
/// Feeding it held state keeps edge detection in one shared, tested place
/// instead of one copy per host.
/// </para>
/// </remarks>
internal sealed class RaylibHost(GridLayout layout, int statusHeight, int? maxFrames) : IViewerHost
{
    private readonly InputAccumulator _input = new();
    private readonly RaylibRenderer _renderer = new();

    public void Run(IViewerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        global::Raylib_cs.Raylib.SetConfigFlags(ConfigFlags.VSyncHint);
        global::Raylib_cs.Raylib.InitWindow(layout.PixelWidth, layout.PixelHeight + statusHeight, app.WindowTitle);
        global::Raylib_cs.Raylib.SetTargetFPS(60);

        var frames = 0;
        var current = app.Layout;
        var currentTitle = app.WindowTitle;

        while (!global::Raylib_cs.Raylib.WindowShouldClose())
        {
            // Loading is host chrome, raylib's way: a file dropped on the
            // window. What the file means is the app's business.
            if (global::Raylib_cs.Raylib.IsFileDropped())
            {
                var dropped = global::Raylib_cs.Raylib.GetDroppedFiles();
                if (dropped.Length > 0)
                {
                    app.LoadFile(dropped[0]);
                }
            }

            // The map decides the geometry, so the window follows the layout
            // rather than the layout being frozen at construction.
            if (app.Layout != current)
            {
                current = app.Layout;
                global::Raylib_cs.Raylib.SetWindowSize(current.PixelWidth, current.PixelHeight + statusHeight);
            }

            if (!string.Equals(app.WindowTitle, currentTitle, StringComparison.Ordinal))
            {
                currentTitle = app.WindowTitle;
                global::Raylib_cs.Raylib.SetWindowTitle(currentTitle);
            }

            _input.SetMousePosition(global::Raylib_cs.Raylib.GetMousePosition());
            _input.SetMouseButtonState(MouseButtons.Left, global::Raylib_cs.Raylib.IsMouseButtonDown(MouseButton.Left));
            _input.SetMouseButtonState(MouseButtons.Right, global::Raylib_cs.Raylib.IsMouseButtonDown(MouseButton.Right));
            _input.SetKeyState(ViewerKeys.Space, global::Raylib_cs.Raylib.IsKeyDown(KeyboardKey.Space));
            _input.SetKeyState(ViewerKeys.R, global::Raylib_cs.Raylib.IsKeyDown(KeyboardKey.R));
            _input.SetKeyState(ViewerKeys.Step, global::Raylib_cs.Raylib.IsKeyDown(KeyboardKey.S));
            _input.SetKeyState(ViewerKeys.Pace, global::Raylib_cs.Raylib.IsKeyDown(KeyboardKey.T));

            // Raw frame time, unclamped: FixedTimestep.MaxStepsPerFrame is
            // already the circuit breaker, and a second one here would make the
            // two hosts disagree about how much time passed.
            app.Update(_input.Snapshot(), global::Raylib_cs.Raylib.GetFrameTime());

            global::Raylib_cs.Raylib.BeginDrawing();
            app.Render(_renderer);

            // Status chrome belongs to the host. The WPF host will use a
            // TextBlock; raylib has its own text, so nothing about drawing
            // strings needs to exist on IRenderer.
            global::Raylib_cs.Raylib.DrawRectangle(0, current.PixelHeight, current.PixelWidth, statusHeight, RlColor.Black);
            global::Raylib_cs.Raylib.DrawText(app.StatusText, 6, current.PixelHeight + 6, 14, RlColor.RayWhite);

            global::Raylib_cs.Raylib.EndDrawing();

            if (maxFrames is { } limit && ++frames >= limit)
            {
                break;
            }
        }

        global::Raylib_cs.Raylib.CloseWindow();
    }

    public void Dispose() => _renderer.Dispose();
}
