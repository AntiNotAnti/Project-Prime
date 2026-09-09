// R12 selective bloom post shader. The source texture contains only geometry
// selected by explicit frozen BloomStrength metadata; there is deliberately no
// whole-scene brightness threshold in this shader.

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
cbuffer BloomConstants : register(b0, space3)
{
    float4 bloomOptions; // xy blur direction, z restrained composite strength
    float4 texelSize;    // xy inverse source dimensions
};

Texture2D sourceTexture : register(t0, space2);
SamplerState sourceSampler : register(s0, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float3 BlurSample(float2 uv, float2 step)
{
    // Compact normalized five-tap kernel, used horizontally then vertically
    // on bounded quarter-resolution targets.
    float3 result = sourceTexture.Sample(sourceSampler, uv).rgb * 0.375f;
    result += sourceTexture.Sample(sourceSampler, uv + step).rgb * 0.25f;
    result += sourceTexture.Sample(sourceSampler, uv - step).rgb * 0.25f;
    result += sourceTexture.Sample(sourceSampler, uv + step * 2.0f).rgb * 0.0625f;
    result += sourceTexture.Sample(sourceSampler, uv - step * 2.0f).rgb * 0.0625f;
    return result;
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    float2 direction = bloomOptions.xy;
    if (dot(direction, direction) < 0.5f)
    {
        return float4(sourceTexture.Sample(sourceSampler, input.texcoord).rgb
            * bloomOptions.z, 0.0f);
    }
    return float4(BlurSample(input.texcoord, direction * texelSize.xy), 0.0f);
}
#endif
