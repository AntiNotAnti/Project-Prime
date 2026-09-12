// Full-screen Enhanced scene warp. Neutral vectors are an exact source copy.

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
Texture2D sourceTexture : register(t0, space2);
SamplerState sourceSampler : register(s0, space2);
Texture2D distortionTexture : register(t1, space2);
SamplerState distortionSampler : register(s1, space2);
cbuffer FullscreenConstants : register(b0, space3)
{
    float4 operation;
    float4 fadeColor;
    float4 viewport;
    float4 overlayOptions;
};
struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};
float4 main_ps(VertexOutput input) : SV_Target0
{
    float2 offset = distortionTexture.Sample(
        distortionSampler, input.texcoord).rg;
    float maximum = max(operation.x, 0.0f);
    float magnitude = length(offset);
    if (magnitude > maximum && magnitude > 0.0f)
    {
        offset *= maximum / magnitude;
    }
    float2 warpedUv = saturate(input.texcoord + offset);
    return sourceTexture.Sample(sourceSampler, warpedUv);
}
#endif
