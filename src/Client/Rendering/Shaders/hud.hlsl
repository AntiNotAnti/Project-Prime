// R7 full-resolution HUD overlay source.  HUD quads stay on the drawable,
// after the resolution-scaled scene and cel pass.  Compile this source with
// SDL_shadercross; no shader compiler is needed at runtime.

#ifdef VERTEX_STAGE
struct VertexInput
{
    float3 position : TEXCOORD0;
    float4 color : TEXCOORD1;
    float2 texcoord : TEXCOORD2;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float4 color : TEXCOORD0;
    float2 texcoord : TEXCOORD1;
};

VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    output.position = float4(input.position, 1.0f);
    output.color = input.color;
    output.texcoord = input.texcoord;
    return output;
}
#else
cbuffer HudConstants : register(b0, space3)
{
    float4 hudOptions; // x alpha, y use texture, z use mask, w reserved
    float4 viewport;   // drawable width, drawable height, reserved, reserved
};

Texture2D hudTexture : register(t0, space2);
SamplerState hudSampler : register(s0, space2);
Texture2D maskTexture : register(t1, space2);
SamplerState maskSampler : register(s1, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float4 color : TEXCOORD0;
    float2 texcoord : TEXCOORD1;
};

float2 LegacyMaskTexcoord(float2 pixelPosition)
{
    // SDL exposes fragment position from the top edge; legacy gl_FragCoord.y
    // was measured from the bottom edge. Convert before applying the original
    // square-mask centering expression.
    float legacyY = viewport.y - pixelPosition.y;
    float maskY = legacyY + (viewport.x - viewport.y) / 2.0f;
    return float2(pixelPosition.x / viewport.x, 1.0f - maskY / viewport.x);
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    // RttFragmentShader samples the bound texture when present and otherwise
    // uses the current flat colour for procedural HUD geometry.
    float4 output = hudOptions.y > 0.5f
        ? hudTexture.Sample(hudSampler, input.texcoord)
        : input.color;
    if (hudOptions.z > 0.5f)
    {
        float4 maskColor = maskTexture.Sample(maskSampler,
            LegacyMaskTexcoord(input.position.xy));
        if (maskColor.a > 0.0f)
        {
            output.a = 0.0f;
        }
    }
    output.a *= hudOptions.x;
    return output;
}
#endif
