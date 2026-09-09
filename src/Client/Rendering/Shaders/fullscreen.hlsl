// R7 fullscreen source.  SDL_shadercross compiles this file offline for the
// three formats accepted by SDL GPU.  The same fullscreen vertex layout is
// used for scene composition, full-resolution HUD quads, masks, and fades.
//
// Operation values:
//   0 - copy/composite the scene target to the drawable
//   1 - draw a fade colour (the alpha is the legacy fade coverage)
//   2 - draw a textured full-resolution overlay, optionally masked

#ifdef VERTEX_STAGE
struct VertexInput
{
    float3 position : TEXCOORD0;
    float2 texcoord : TEXCOORD1;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    output.position = float4(input.position, 1.0f);
    output.texcoord = input.texcoord;
    return output;
}
#else
cbuffer FullscreenConstants : register(b0, space3)
{
    float4 operation;       // x: 0 composite, 1 fade, 2 overlay
    float4 fadeColor;       // RGB and legacy fade coverage in A
    float4 viewport;        // width, height, source width, source height
    float4 overlayOptions;  // x alpha, y use mask, z/w reserved
};

Texture2D sourceTexture : register(t0, space2);
SamplerState sourceSampler : register(s0, space2);
Texture2D maskTexture : register(t1, space2);
SamplerState maskSampler : register(s1, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float2 LegacyMaskTexcoord(float2 pixelPosition)
{
    // This is the RttFragmentShader expression verbatim.  The mask is in the
    // square 256x256 HUD space while the drawable may be 256x192 or widescreen.
    float legacyY = viewport.y - pixelPosition.y;
    float maskY = legacyY + (viewport.x - viewport.y) / 2.0f;
    return float2(pixelPosition.x / viewport.x, 1.0f - maskY / viewport.x);
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    if (operation.x > 0.5f && operation.x < 1.5f)
    {
        // Legacy fade_color is an outright colour source.  The blend state
        // applies the coverage carried in its alpha channel.
        return fadeColor;
    }

    float4 output = sourceTexture.Sample(sourceSampler, input.texcoord);
    if (operation.x > 1.5f && overlayOptions.y > 0.5f)
    {
        float4 maskColor = maskTexture.Sample(maskSampler,
            LegacyMaskTexcoord(input.position.xy));
        if (maskColor.a > 0.0f)
        {
            output.a = 0.0f;
        }
    }
    if (operation.x > 1.5f)
    {
        output.a *= overlayOptions.x;
    }
    return output;
}
#endif
