// VE12 selective HDR bloom shader. The source texture contains only geometry
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
    // w operation: 0 blur, 1 downsample, 2 weighted upsample/combine,
    // 3 HDR scene composite. xy are direction or input weights.
    float4 bloomOptions;
    float4 texelSize;    // xy inverse source dimensions
};

Texture2D sourceTexture : register(t0, space2);
Texture2D secondaryTexture : register(t1, space2);
SamplerState sourceSampler : register(s0, space2);
SamplerState secondarySampler : register(s1, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float3 BlurSample(float2 uv, float2 step)
{
    // Compact normalized five-tap kernel at every bounded pyramid level.
    float3 result = sourceTexture.Sample(sourceSampler, uv).rgb * 0.375f;
    result += sourceTexture.Sample(sourceSampler, uv + step).rgb * 0.25f;
    result += sourceTexture.Sample(sourceSampler, uv - step).rgb * 0.25f;
    result += sourceTexture.Sample(sourceSampler, uv + step * 2.0f).rgb * 0.0625f;
    result += sourceTexture.Sample(sourceSampler, uv - step * 2.0f).rgb * 0.0625f;
    return result;
}

float3 Downsample(float2 uv)
{
    // The first pass is 4:1 and must cover the full 4x4 source footprint;
    // later pyramid passes are 2:1. Deriving the source-pixel ratio from UV
    // derivatives keeps the existing constants ABI: 4 * 0.25 = +/-1 source
    // texel, while 2 * 0.25 = +/-0.5. Odd target sizes scale proportionally.
    float2 outputUvStep = float2(abs(ddx(uv.x)), abs(ddy(uv.y)));
    float2 sourcePixelsPerOutput = outputUvStep / texelSize.xy;
    float2 footprintScale = sourcePixelsPerOutput * 0.25f;
    float2 offset = texelSize.xy * footprintScale;
    float3 result = sourceTexture.Sample(sourceSampler, uv + float2(-offset.x, -offset.y)).rgb;
    result += sourceTexture.Sample(sourceSampler, uv + float2(offset.x, -offset.y)).rgb;
    result += sourceTexture.Sample(sourceSampler, uv + float2(-offset.x, offset.y)).rgb;
    result += sourceTexture.Sample(sourceSampler, uv + offset).rgb;
    return result * 0.25f;
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    if (bloomOptions.w > 2.5f)
    {
        float3 scene = sourceTexture.Sample(sourceSampler, input.texcoord).rgb;
        float3 bloom = secondaryTexture.Sample(secondarySampler, input.texcoord).rgb;
        return float4(scene * bloomOptions.x + bloom * bloomOptions.y, 1.0f);
    }
    if (bloomOptions.w > 1.5f)
    {
        float3 first = sourceTexture.Sample(sourceSampler, input.texcoord).rgb;
        float3 second = secondaryTexture.Sample(secondarySampler, input.texcoord).rgb;
        return float4(first * bloomOptions.x + second * bloomOptions.y, 0.0f);
    }
    if (bloomOptions.w > 0.5f)
    {
        return float4(Downsample(input.texcoord), 0.0f);
    }
    float2 direction = bloomOptions.xy;
    return float4(BlurSample(input.texcoord, direction * texelSize.xy), 0.0f);
}
#endif
