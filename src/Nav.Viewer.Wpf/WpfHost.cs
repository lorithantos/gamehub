using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Nav.Viewer.Wpf;

/// <summary>
/// Window and loop, WPF's way: the framework owns the loop and calls us.
/// </summary>
/// <remarks>
/// The other half of the control-inversion test. The raylib host runs
/// <c>while (!WindowShouldClose())</c> and drives the app; here
/// <c>Application.Run</c> owns the loop and per-frame work hangs off the
/// compositor. The app cannot tell the difference, which is the whole claim.
/// <para>
/// <c>CompositionTarget.Rendering</c> is a <b>static</b> event, so the
/// subscription is paired with an unsubscribe on window close. An
/// un-unsubscribed handler keeps ticking against a disposed device -- a
/// use-after-free with a managed-looking cause and a native crash, which is
/// exactly the shape the catalog's WPF notes warn about.
/// </para>
/// </remarks>
internal sealed class WpfHost(GridLayout layout, string title, int? maxFrames) : IViewerHost
{
    private readonly InputAccumulator _input = new();
    private readonly FrameClock _clock = new();
    private readonly D3DImage _image = new();

    private MainWindow? _window;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private SharedSurface? _surface;
    private D3D11Renderer? _renderer;
    private IViewerApp? _app;
    private double _dpiScale = 1.0;
    private int _frames;
    private bool _disposed;

    public bool DebugLayerActive { get; private set; }

    public void Run(IViewerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;

        _window = new MainWindow { Title = title };
        _window.Surface.Source = _image;

        _window.SourceInitialized += OnSourceInitialized;
        _window.KeyDown += (_, e) => SetKey(e.Key, down: true);
        _window.KeyUp += (_, e) => SetKey(e.Key, down: false);
        _window.MouseDown += OnMouseButton;
        _window.MouseUp += OnMouseButton;
        _window.MouseMove += OnMouseMove;
        _window.Closed += OnClosed;

        // Auto-size exactly once, then lock: after the first layout, nothing --
        // not chrome, not text -- may move the window again.
        _window.ContentRendered += (_, _) => _window.SizeToContent = SizeToContent.Manual;

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Run(_window);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _renderer?.Dispose();
        _surface?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _disposed = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(_window!).Handle;

        // WPF lays out in DIPs while the D3D surface and the mouse coordinates
        // that pick cells are physical pixels. Everything below stays in
        // PHYSICAL pixels, and the only conversion is sizing the WPF element.
        _dpiScale = VisualTreeHelper.GetDpi(_window!).DpiScaleX;
        _window!.Surface.Width = layout.PixelWidth / _dpiScale;
        _window.Surface.Height = layout.PixelHeight / _dpiScale;

        // The surface, not the status text, decides the window's width. Without
        // this the squad status line was the widest element on small maps, and
        // SizeToContent re-measured the WHOLE WINDOW every time a counter
        // changed digit count -- the window visibly shook while a stalled agent
        // replanned. The text trims instead.
        _window.StatusBar.Width = layout.PixelWidth / _dpiScale;

        CreateDevice();
        _surface = new SharedSurface(_device!, handle, layout.PixelWidth, layout.PixelHeight);
        _renderer = new D3D11Renderer(_device!, _context!, _surface);

        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface.Surface.NativePointer);
        _image.Unlock();

        CompositionTarget.Rendering += OnRendering;
    }

    private void CreateDevice()
    {
        // The debug layer lives in the Graphics Tools Feature on Demand, not in
        // the OS and not necessarily in the Windows SDK. Asking for it where it
        // is absent fails device creation OUTRIGHT with
        // DXGI_ERROR_SDK_COMPONENT_MISSING, so it is attempted and abandoned
        // rather than assumed. Which branch ran is recorded, because a silently
        // absent validation layer is worse than none -- you trust it.
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };

        try
        {
            D3D11.D3D11CreateDevice(
                null, DriverType.Hardware, DeviceCreationFlags.Debug,
                levels, out _device, out _context).CheckError();
            DebugLayerActive = true;
            return;
        }
        catch (Exception)
        {
            DebugLayerActive = false;
        }

        D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.None,
            levels, out _device, out _context).CheckError();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_renderer is null || _app is null || _window is null)
        {
            return;
        }

        // RenderingTime is a running timestamp, not a delta, and it can fire
        // twice with the same value for one frame. FrameClock owns that.
        var timestamp = ((RenderingEventArgs)e).RenderingTime;
        if (!_clock.TryAdvance(timestamp, out var deltaSeconds))
        {
            return;
        }

        _app.Update(_input.Snapshot(), deltaSeconds);

        _renderer.BeginFrame(RgbaColor.Black);
        _app.Render(_renderer);
        _renderer.EndFrame();

        _image.Lock();
        _image.AddDirtyRect(new Int32Rect(0, 0, _surface!.Width, _surface.Height));
        _image.Unlock();

        if (!string.Equals(_window.Status.Text, _app.StatusText, StringComparison.Ordinal))
        {
            // Guarded: an unconditional assignment triggers a measure and
            // arrange pass sixty times a second for a string that changes on a
            // click.
            _window.Status.Text = _app.StatusText;
        }

        if (maxFrames is { } limit && ++_frames >= limit)
        {
            _window.Close();
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void SetKey(Key key, bool down)
    {
        // Held state, never edges. InputAccumulator derives the edge, which is
        // what makes WPF's key auto-repeat -- roughly thirty events a second --
        // harmless without a single WPF-specific line.
        switch (key)
        {
            case Key.Space:
                _input.SetKeyState(ViewerKeys.Space, down);
                break;
            case Key.R:
                _input.SetKeyState(ViewerKeys.R, down);
                break;
            case Key.Escape when down:
                _window?.Close();
                break;
            default:
                break;
        }
    }

    private void OnMouseButton(object sender, MouseButtonEventArgs e)
    {
        _input.SetMousePosition(ToSurfacePixels(e));
        _input.SetMouseButtonState(MouseButtons.Left, e.LeftButton == MouseButtonState.Pressed);
        _input.SetMouseButtonState(MouseButtons.Right, e.RightButton == MouseButtonState.Pressed);
    }

    private void OnMouseMove(object sender, MouseEventArgs e) => _input.SetMousePosition(ToSurfacePixels(e));

    private Vector2 ToSurfacePixels(MouseEventArgs e)
    {
        var point = e.GetPosition(_window!.Surface);
        return new Vector2((float)(point.X * _dpiScale), (float)(point.Y * _dpiScale));
    }
}
