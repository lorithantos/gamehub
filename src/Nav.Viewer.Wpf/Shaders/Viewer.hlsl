// One shader pair for both draws. UseTexture selects between the sampled
// terrain and a flat vertex colour, which avoids a second pipeline and a second
// input layout for what is ultimately the same textured quad with the texture
// turned off.
//
// The projection is a float2, not a matrix: the viewer works entirely in pixel
// space, so mapping to normalised device coordinates is a multiply and an add.

cbuffer FrameConstants : register(b0)
{
    float2 InvViewport;   // 1 / surface size, in pixels
    float  UseTexture;    // 1 for the terrain draw, 0 for solid geometry
    float  Padding;
};

struct VertexIn
{
    float2 position : POSITION;
    float2 uv       : TEXCOORD0;
    float4 color    : COLOR0;
};

struct VertexOut
{
    float4 position : SV_POSITION;
    float2 uv       : TEXCOORD0;
    float4 color    : COLOR0;
};

VertexOut VSMain(VertexIn input)
{
    VertexOut output;

    // Pixel space (origin top-left, y down) to NDC (origin centre, y up).
    output.position = float4(
        (input.position.x * InvViewport.x * 2.0) - 1.0,
        1.0 - (input.position.y * InvViewport.y * 2.0),
        0.0,
        1.0);

    output.uv = input.uv;
    output.color = input.color;
    return output;
}

Texture2D Terrain : register(t0);
SamplerState PointClamp : register(s0);

float4 PSMain(VertexOut input) : SV_TARGET
{
    float4 sampled = Terrain.Sample(PointClamp, input.uv);
    return lerp(input.color, sampled, UseTexture);
}
