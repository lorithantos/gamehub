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
/// The other half of the control-inversion test. The raylib host drives the app;
/// here <c>Application.Run</c> owns the loop and per-frame work hangs off the
/// compositor. The app cannot tell the difference, which is the whole claim.
/// <para>
/// <c>CompositionTarget.Rendering</c> is a <b>static</b> event, so the
/// subscription is paired with an unsubscribe on window close.
/// </para>
/// <para>
/// An un-unsubscribed handler keeps ticking against a disposed device — a
/// use-after-free with a managed-looking cause and a native crash.
/// </para>
/// </remarks>
internal sealed class WpfHost(GridLayout layout, int? maxFrames) : IViewerHost
{
    private readonly InputAccumulator _input = new();
    private readonly FrameClock _clock = new();
    private readonly D3DImage _image = new();

    private GridLayout _layout = layout;
    private MainWindow? _window;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private SharedSurface? _surface;
    private D3D11Renderer? _renderer;
    private IViewerApp? _app;
    private InspectorView? _inspector;
    private double _dpiScale = 1.0;
    private int _frames;
    private bool _disposed;

    public bool DebugLayerActive { get; private set; }

    public void Run(IViewerApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;

        _window = new MainWindow { Title = app.WindowTitle };
        _window.Surface.Source = _image;
        _inspector = new InspectorView(_window.Inspector);

        _window.SourceInitialized += OnSourceInitialized;
        _window.KeyDown += (_, e) => SetKey(e.Key, down: true);
        _window.KeyUp += (_, e) => SetKey(e.Key, down: false);
        _window.MouseDown += OnMouseButton;
        _window.MouseUp += OnMouseButton;
        _window.MouseMove += OnMouseMove;
        _window.Closed += OnClosed;

        // Loading is host chrome, WPF's way: a file dropped on the window, or
        // Ctrl+O for the stock dialog. What the file means is the app's business.
        _window.AllowDrop = true;
        _window.Drop += OnDrop;

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
        SizeChrome();

        CreateDevice();
        _surface = new SharedSurface(_device!, handle, _layout.PixelWidth, _layout.PixelHeight);
        _renderer = new D3D11Renderer(_device!, _context!, _surface);

        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface.Surface.NativePointer);
        _image.Unlock();

        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>
    /// The surface, not the text, decides the window's size.
    /// </summary>
    /// <remarks>
    /// Otherwise the status line is the widest element on small maps, and
    /// SizeToContent re-measures the WHOLE WINDOW every time a counter changes
    /// digit count — the window visibly shakes while an agent replans.
    /// <para>The text wraps instead -- see MainWindow.xaml.</para>
    /// <para>
    /// The inspector column joined that arithmetic and had to be pinned at both
    /// ends to stay out of it: a fixed width in the XAML, and its height taken
    /// from the surface. Left to size itself it would have driven the window
    /// from whichever unit happened to be selected.
    /// </para>
    /// <para>
    /// The arithmetic itself is <see cref="MainWindow.SizeTo"/>, so a test can
    /// ask for it without standing up a Direct3D device.
    /// </para>
    /// </remarks>
    private void SizeChrome() => _window!.SizeTo(_layout, _dpiScale);

    /// <summary>
    /// The map changed size, so everything sized from it follows: the D3D
    /// surface and renderer are rebuilt at the new dimensions, the back buffer
    /// is re-pointed, and the window auto-sizes exactly once more before
    /// locking again. The same detach-rebuild-reattach order device loss would
    /// use.
    /// </summary>
    private void RebuildForLayout(GridLayout layout)
    {
        _layout = layout;
        SizeChrome();

        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
        _image.Unlock();

        _renderer!.Dispose();
        _surface!.Dispose();

        var handle = new WindowInteropHelper(_window!).Handle;
        _surface = new SharedSurface(_device!, handle, _layout.PixelWidth, _layout.PixelHeight);
        _renderer = new D3D11Renderer(_device!, _context!, _surface);

        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface.Surface.NativePointer);
        _image.Unlock();

        _window!.SizeToContent = SizeToContent.WidthAndHeight;
        _window.UpdateLayout();
        _window.SizeToContent = SizeToContent.Manual;
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

        _app.Update(_input.Drain(), deltaSeconds);

        if (_app.Layout != _layout)
        {
            RebuildForLayout(_app.Layout);
        }

        if (!string.Equals(_window.Title, _app.WindowTitle, StringComparison.Ordinal))
        {
            _window.Title = _app.WindowTitle;
        }

        // The app opens and closes the frame, not the host -- see the contract on
        // IViewerHost. This used to bracket the call again, which cost a second
        // full-target clear and, because EndFrame flushes the batch without
        // emptying it, submitted every line and circle twice.
        _app.Render(_renderer);

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

        // The panel decides for itself how little to do. It rebuilds its
        // elements only when the set of groups and keys changes and otherwise
        // writes text into the ones it already has, each write guarded the same
        // way the status line's is -- so the per-frame cost is a comparison per
        // row and, on most frames, nothing else. See InspectorView.
        _inspector!.Update(_app.Inspector);

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
        //
        // Escape and Ctrl+O are host chrome and stop here: they close the window
        // and open a file, neither of which the app has an opinion about, and
        // neither of which anyone should be able to rebind away.
        switch (key)
        {
            case Key.Escape when down:
                _window?.Close();
                return;
            case Key.O when down && (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                PromptForFile();
                return;
            default:
                break;
        }

        // WPF's Key becomes the shared physical identity ONCE, and the app's
        // keymap decides what it does -- see Keymap for why the map sits
        // between them.
        var physical = key switch
        {
            Key.Space => PhysicalKey.Space,
            Key.R => PhysicalKey.R,
            Key.S => PhysicalKey.S,
            Key.T => PhysicalKey.T,
            Key.V => PhysicalKey.V,
            Key.P => PhysicalKey.P,
            Key.L => PhysicalKey.L,
            Key.Left => PhysicalKey.Left,
            Key.Right => PhysicalKey.Right,
            Key.Up => PhysicalKey.Up,
            Key.Down => PhysicalKey.Down,

            Key.OemPlus or Key.Add => PhysicalKey.Plus,
            Key.OemMinus or Key.Subtract => PhysicalKey.Minus,
            Key.Home => PhysicalKey.Home,
            _ => PhysicalKey.None,
        };

        // An unbound key answers None, and setting an empty mask sets and clears
        // nothing -- so there is no need to ask first whether the viewer wants
        // this key.
        _input.SetKeyState(_app?.Keys.Action(physical) ?? ViewerKeys.None, down);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _app?.LoadFile(files[0]);
        }
    }

    private void PromptForFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "maps and scenarios|*.map;*.scenario|all files|*.*",
        };

        if (dialog.ShowDialog(_window) == true)
        {
            _app?.LoadFile(dialog.FileName);
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
