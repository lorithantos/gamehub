// System.IO is NOT among the implicit usings a WPF project gets.
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Nav.Viewer.Wpf;

/// <summary>
/// <see cref="IRenderer"/> over raw Direct3D 11.
/// </summary>
/// <remarks>
/// D3D11 has no lines, no circles and no text — only triangles — so every verb
/// here is a triangulation. A line becomes a quad expanded perpendicular to
/// itself, a circle becomes a fan, and text is the host's.
/// <para>
/// Two draw calls per frame: the terrain quad, then everything else batched into
/// one dynamic vertex buffer.
/// </para>
/// <para>
/// There is no swapchain. The target is a shared texture WPF composites through
/// <c>D3DImage</c>, so presentation belongs to the host — the same division the
/// raylib renderer uses.
/// </para>
/// <para>
/// The terrain texture is cached against the <see cref="TerrainImage"/> instance
/// and re-uploaded only when that changes, which is what keeps device lifetime
/// from ever reaching the app.
/// </para>
/// </remarks>
internal sealed unsafe class D3D11Renderer : IRenderer, IDisposable
{
    /// <summary>Matches raylib's <c>DrawCircleV</c> closely enough to compare by eye.</summary>
    private const int CircleSegments = 32;

    private const int InitialVertexCapacity = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex(Vector2 position, Vector2 uv, Vector4 color)
    {
        public Vector2 Position = position;
        public Vector2 Uv = uv;
        public Vector4 Color = color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Vector2 InvViewport;
        public float UseTexture;
        public float Padding;
    }

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly SharedSurface _surface;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11InputLayout _inputLayout;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11RasterizerState _rasterizer;
    private readonly ID3D11Buffer _constants;

    private readonly List<Vertex> _solid = new(InitialVertexCapacity);

    private ID3D11Buffer _vertices;
    private int _vertexCapacity = InitialVertexCapacity;

    private ID3D11Texture2D? _terrainTexture;
    private ID3D11ShaderResourceView? _terrainView;
    private TerrainImage? _uploaded;

    private bool _disposed;

    public D3D11Renderer(ID3D11Device device, ID3D11DeviceContext context, SharedSurface surface)
    {
        _device = device;
        _context = context;
        _surface = surface;

        var hlsl = ReadShaderSource();
        var vertexBytes = Compiler.Compile(hlsl, "VSMain", "Viewer.hlsl", "vs_5_0", ShaderFlags.None, EffectFlags.None);
        var pixelBytes = Compiler.Compile(hlsl, "PSMain", "Viewer.hlsl", "ps_5_0", ShaderFlags.None, EffectFlags.None);

        _vertexShader = _device.CreateVertexShader(vertexBytes.Span);
        _pixelShader = _device.CreatePixelShader(pixelBytes.Span);
        _inputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 16, 0),
            ],
            vertexBytes.Span);

        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            // The exact analogue of raylib's TextureFilter.Point, and the reason
            // a magnified cell stays a hard square rather than a blur.
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MaxAnisotropy = 1,
            MinLOD = 0f,
            MaxLOD = float.MaxValue,
        });

        // Explicit CullMode.None. Winding-order culling silently eating every
        // triangle is the classic first-D3D11 bug, and it presents as "nothing
        // renders, no error" rather than as anything diagnosable.
        _rasterizer = _device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            DepthClipEnable = true,
        });

        var blendDescription = new BlendDescription();
        blendDescription.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _blend = _device.CreateBlendState(blendDescription);

        _constants = _device.CreateBuffer(
            (uint)sizeof(FrameConstants),
            BindFlags.ConstantBuffer,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            ResourceOptionFlags.None,
            0);

        _vertices = CreateVertexBuffer(_vertexCapacity);
    }

    public void BeginFrame(RgbaColor clear)
    {
        _solid.Clear();

        _context.OMSetRenderTargets(_surface.RenderTargetView);
        _context.RSSetViewport(0, 0, _surface.Width, _surface.Height);
        _context.ClearRenderTargetView(_surface.RenderTargetView, ToColor4(clear));

        _context.IASetInputLayout(_inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetSampler(0, _sampler);
        _context.RSSetState(_rasterizer);
        _context.OMSetBlendState(_blend);
    }

    public void DrawTerrain(TerrainImage image, RectF destination)
    {
        ArgumentNullException.ThrowIfNull(image);

        // The cache returns the view rather than setting a field the compiler
        // then cannot prove is non-null. Same effect, no suppression.
        var view = ReferenceEquals(_uploaded, image) && _terrainView is not null
            ? _terrainView
            : UploadTerrain(image);

        Span<Vertex> quad = stackalloc Vertex[6];
        WriteQuad(
            quad,
            new Vector2(destination.X, destination.Y),
            new Vector2(destination.Right, destination.Y),
            new Vector2(destination.X, destination.Bottom),
            new Vector2(destination.Right, destination.Bottom),
            withUvs: true,
            Vector4.One);

        _context.PSSetShaderResource(0, view);
        SetConstants(useTexture: 1f);
        UploadAndDraw(quad);
    }

    public void DrawLine(Vector2 from, Vector2 to, float thickness, RgbaColor color)
    {
        var along = to - from;
        var length = along.Length();
        if (length <= float.Epsilon)
        {
            return;
        }

        // A quad expanded perpendicular to the segment. Joints are left
        // unmitred, which leaves a small notch on a turn -- raylib's DrawLineEx
        // is also quads and leaves the same notch, so the two renderers agree.
        var normal = new Vector2(-along.Y, along.X) / length * (thickness * 0.5f);
        var tint = ToVector4(color);

        Span<Vertex> quad = stackalloc Vertex[6];
        WriteQuad(quad, from - normal, from + normal, to - normal, to + normal, withUvs: false, tint);
        _solid.AddRange(quad);
    }

    public void DrawCircle(Vector2 center, float radius, RgbaColor color)
    {
        var tint = ToVector4(color);
        var previous = center + new Vector2(radius, 0f);

        for (var i = 1; i <= CircleSegments; i++)
        {
            var angle = MathF.Tau * i / CircleSegments;
            var next = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);

            _solid.Add(new Vertex(center, Vector2.Zero, tint));
            _solid.Add(new Vertex(previous, Vector2.Zero, tint));
            _solid.Add(new Vertex(next, Vector2.Zero, tint));

            previous = next;
        }
    }

    public void EndFrame()
    {
        if (_solid.Count > 0)
        {
            SetConstants(useTexture: 0f);
            UploadAndDraw(CollectionsMarshal.AsSpan(_solid));

            // Emptied here as well as in BeginFrame, so submitting is idempotent.
            // Flushing without clearing meant a second EndFrame redrew the whole
            // batch, which is invisible while every colour is opaque and stops
            // being invisible the moment anything is not.
            _solid.Clear();
        }

        // Mandatory before WPF composites the shared surface. Without it the
        // compositor can pick up a frame the GPU has not finished writing, which
        // shows as intermittent tearing rather than as an error.
        _context.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _terrainView?.Dispose();
        _terrainTexture?.Dispose();
        _vertices.Dispose();
        _constants.Dispose();
        _blend.Dispose();
        _rasterizer.Dispose();
        _sampler.Dispose();
        _inputLayout.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _disposed = true;
    }

    private static string ReadShaderSource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string name = "Nav.Viewer.Wpf.Shaders.Viewer.hlsl";
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded shader '{name}' is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Vector4 ToVector4(RgbaColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static Vortice.Mathematics.Color4 ToColor4(RgbaColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static void WriteQuad(
        Span<Vertex> target,
        Vector2 topLeft,
        Vector2 topRight,
        Vector2 bottomLeft,
        Vector2 bottomRight,
        bool withUvs,
        Vector4 tint)
    {
        var uvTopLeft = withUvs ? new Vector2(0, 0) : Vector2.Zero;
        var uvTopRight = withUvs ? new Vector2(1, 0) : Vector2.Zero;
        var uvBottomLeft = withUvs ? new Vector2(0, 1) : Vector2.Zero;
        var uvBottomRight = withUvs ? new Vector2(1, 1) : Vector2.Zero;

        target[0] = new Vertex(topLeft, uvTopLeft, tint);
        target[1] = new Vertex(topRight, uvTopRight, tint);
        target[2] = new Vertex(bottomLeft, uvBottomLeft, tint);
        target[3] = new Vertex(bottomLeft, uvBottomLeft, tint);
        target[4] = new Vertex(topRight, uvTopRight, tint);
        target[5] = new Vertex(bottomRight, uvBottomRight, tint);
    }

    private ID3D11Buffer CreateVertexBuffer(int capacity) =>
        _device.CreateBuffer(
            (uint)(capacity * sizeof(Vertex)),
            BindFlags.VertexBuffer,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            ResourceOptionFlags.None,
            0);

    private void SetConstants(float useTexture)
    {
        var value = new FrameConstants
        {
            InvViewport = new Vector2(1f / _surface.Width, 1f / _surface.Height),
            UseTexture = useTexture,
            Padding = 0f,
        };

        var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            *(FrameConstants*)mapped.DataPointer = value;
        }
        finally
        {
            _context.Unmap(_constants, 0);
        }

        _context.VSSetConstantBuffer(0, _constants);
        _context.PSSetConstantBuffer(0, _constants);
    }

    private void UploadAndDraw(ReadOnlySpan<Vertex> batch)
    {
        if (batch.Length > _vertexCapacity)
        {
            _vertices.Dispose();
            _vertexCapacity = Math.Max(batch.Length, _vertexCapacity * 2);
            _vertices = CreateVertexBuffer(_vertexCapacity);
        }

        var mapped = _context.Map(_vertices, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var destination = new Span<Vertex>((void*)mapped.DataPointer, batch.Length);
            batch.CopyTo(destination);
        }
        finally
        {
            _context.Unmap(_vertices, 0);
        }

        _context.IASetVertexBuffers(
            0,
            [_vertices],
            stackalloc uint[] { (uint)sizeof(Vertex) },
            stackalloc uint[] { 0 });

        _context.Draw((uint)batch.Length, 0);
    }

    private ID3D11ShaderResourceView UploadTerrain(TerrainImage image)
    {
        _terrainView?.Dispose();
        _terrainTexture?.Dispose();

        // R8G8B8A8 matches TerrainImage's own layout, so nothing is swizzled.
        // Only the SHARED render target has to be BGRA, and that is a D3D9Ex
        // constraint rather than a preference.
        _terrainTexture = _device.CreateTexture2D(
            image.Pixels,
            Format.R8G8B8A8_UNorm,
            (uint)image.Width,
            (uint)image.Height,
            1,
            1,
            BindFlags.ShaderResource,
            ResourceOptionFlags.None,
            ResourceUsage.Immutable,
            CpuAccessFlags.None);

        var view = _device.CreateShaderResourceView(_terrainTexture);
        _terrainView = view;
        _uploaded = image;
        return view;
    }
}
