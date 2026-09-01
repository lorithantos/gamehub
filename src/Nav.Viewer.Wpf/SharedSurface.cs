using Vortice.Direct3D11;
using Vortice.DXGI;

using D9 = Vortice.Direct3D9;

namespace Nav.Viewer.Wpf;

/// <summary>
/// A Direct3D 11 render target that WPF's <c>D3DImage</c> will accept.
/// </summary>
/// <remarks>
/// <c>D3DImage</c> speaks D3D9Ex and nothing else, so a D3D11 texture reaches it
/// by the shared-handle route: create the texture with
/// <c>ResourceOptionFlags.Shared</c>, take the handle off its
/// <c>IDXGIResource</c>, and hand that handle to D3D9Ex's <c>CreateTexture</c>
/// as an <em>existing</em> resource rather than a request for a new one. Surface
/// level 0 of the result is the only thing <c>SetBackBuffer</c> will take.
/// <para>
/// <c>B8G8R8A8_UNorm</c> is not a preference: D3D9Ex accepts a narrow set of
/// shared formats, and this is the one that pairs with <c>A8R8G8B8</c>.
/// </para>
/// <para>
/// The D3D9Ex device exists only to open the handle. It draws nothing, so its
/// presentation parameters are a 1x1 windowed swap chain that never presents.
/// </para>
/// </remarks>
internal sealed class SharedSurface : IDisposable
{
    private readonly D9.IDirect3D9Ex _d3d9;
    private readonly D9.IDirect3DDevice9Ex _device9;
    private readonly D9.IDirect3DTexture9 _texture9;

    public SharedSurface(ID3D11Device device, IntPtr windowHandle, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);

        Width = width;
        Height = height;

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.Shared,
        };

        Texture = device.CreateTexture2D(description);
        RenderTargetView = device.CreateRenderTargetView(Texture);

        using var resource = Texture.QueryInterface<IDXGIResource>();
        var handle = resource.SharedHandle;

        _d3d9 = D9.D3D9.Direct3DCreate9Ex();

        var present = new D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = D9.SwapEffect.Discard,
            DeviceWindowHandle = windowHandle,
            PresentationInterval = D9.PresentInterval.Immediate,
            BackBufferFormat = D9.Format.Unknown,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
        };

        _device9 = _d3d9.CreateDeviceEx(
            0,
            D9.DeviceType.Hardware,
            windowHandle,
            D9.CreateFlags.HardwareVertexProcessing | D9.CreateFlags.Multithreaded | D9.CreateFlags.FpuPreserve,
            present);

        // This is the whole trick: passing an existing handle by reference OPENS
        // the D3D11 texture rather than allocating a new D3D9 one.
        _texture9 = _device9.CreateTexture(
            (uint)width,
            (uint)height,
            1,
            D9.Usage.RenderTarget,
            D9.Format.A8R8G8B8,
            D9.Pool.Default,
            ref handle);

        Surface = _texture9.GetSurfaceLevel(0);
    }

    public int Width { get; }

    public int Height { get; }

    public ID3D11Texture2D Texture { get; }

    public ID3D11RenderTargetView RenderTargetView { get; }

    /// <summary>The pointer <c>D3DImage.SetBackBuffer</c> wants.</summary>
    public D9.IDirect3DSurface9 Surface { get; }

    public void Dispose()
    {
        Surface.Dispose();
        _texture9.Dispose();
        _device9.Dispose();
        _d3d9.Dispose();
        RenderTargetView.Dispose();
        Texture.Dispose();
    }
}
