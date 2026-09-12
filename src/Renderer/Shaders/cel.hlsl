// R7 depth-based cel outline source.  The scene target and its depth texture
// are sampled after HUD models but before the full-resolution HUD/helmet and
// fade, so those later stages are never outlined.

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
cbuffer CelConstants : register(b0, space3)
{
    float4 celOptions;  // x outline, y near, z far, w depth quantum
    float4 texelOptions; // x texel width, y texel height, z packed surface data, w reserved
};

Texture2D sceneTexture : register(t0, space2);
SamplerState sceneSampler : register(s0, space2);
Texture2D depthTexture : register(t1, space2);
SamplerState depthSampler : register(s1, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float2 PixelUv(float2 pixelPosition)
{
    return pixelPosition * texelOptions.xy;
}

float RawDepth(float2 pixelPosition, float dx, float dy)
{
    return depthTexture.SampleLevel(depthSampler,
        PixelUv(pixelPosition + float2(dx, dy)), 0).x;
}

float KinkAbs(float2 pixelPosition, float d, float reach)
{
    float horizontal = (RawDepth(pixelPosition, -reach, 0) - d)
        + (RawDepth(pixelPosition, reach, 0) - d);
    float vertical = (RawDepth(pixelPosition, 0, -reach) - d)
        + (RawDepth(pixelPosition, 0, reach) - d);
    return max(abs(horizontal), abs(vertical)) / reach;
}

float KinkRel(float2 pixelPosition, float d, float reach, float unit)
{
    return KinkAbs(pixelPosition, d, reach) / unit;
}

float EdgeAt(float2 pixelPosition, float d, float reach, float unit)
{
    float quantised = celOptions.w * 4.0f / reach / unit;
    float lo = max(1.1f, quantised * 1.5f);
    float hi = max(3.5f, quantised * 4.0f);
    return smoothstep(lo, hi, KinkRel(pixelPosition, d, reach, unit));
}

float4 PackedSurface(float2 pixelPosition, float dx, float dy)
{
    return depthTexture.SampleLevel(depthSampler,
        PixelUv(pixelPosition + float2(dx, dy)), 0);
}

float3 DecodeOctNormal(float2 encoded)
{
    float2 folded = encoded * 2.0f - 1.0f;
    float3 normal = float3(folded,
        1.0f - abs(folded.x) - abs(folded.y));
    float correction = saturate(-normal.z);
    normal.xy += float2(normal.x >= 0.0f ? -correction : correction,
        normal.y >= 0.0f ? -correction : correction);
    return normalize(normal);
}

float SurfaceEdgeAt(float2 pixelPosition, float reach)
{
    float4 center = PackedSurface(pixelPosition, 0, 0);
    if (center.z <= 0.0f) return 0.0f;
    float3 centerNormal = DecodeOctNormal(center.xy);
    static const float2 directions[4] = {
        float2(-1.0f, 0.0f), float2(1.0f, 0.0f),
        float2(0.0f, -1.0f), float2(0.0f, 1.0f)
    };
    float depthDifference = 0.0f;
    float normalDifference = 0.0f;
    [unroll]
    for (int index = 0; index < 4; index++)
    {
        float2 offset = directions[index] * reach;
        float4 neighbor = PackedSurface(pixelPosition, offset.x, offset.y);
        if (neighbor.z <= 0.0f)
        {
            depthDifference = 1.0f;
            continue;
        }
        depthDifference = max(depthDifference,
            abs(neighbor.z - center.z) / max(center.z, 0.0001f));
        normalDifference = max(normalDifference,
            1.0f - saturate(dot(centerNormal,
                DecodeOctNormal(neighbor.xy))));
    }
    float depthEdge = smoothstep(0.006f, 0.025f, depthDifference);
    float normalEdge = smoothstep(0.08f, 0.30f, normalDifference);
    return max(depthEdge, normalEdge);
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    float2 pixelPosition = input.position.xy;
    float3 base = sceneTexture.Sample(sceneSampler, PixelUv(pixelPosition)).rgb;
    float ink = 0.0f;

    if (texelOptions.z > 0.5f)
    {
        ink = max(SurfaceEdgeAt(pixelPosition, 1.0f),
            SurfaceEdgeAt(pixelPosition, 2.0f)) * celOptions.x;
        return float4(base * (1.0f - ink), 1.0f);
    }

    float d = RawDepth(pixelPosition, 0, 0);

    if (d < 0.9999995f)
    {
        float scale = celOptions.z / (celOptions.z - celOptions.y) - d;
        float unit = max(scale, 1e-9f) * texelOptions.x;
        ink = max(EdgeAt(pixelPosition, d, 2.0f, unit),
            EdgeAt(pixelPosition, d, 3.0f, unit)) * celOptions.x;
    }
    return float4(base * (1.0f - ink), 1.0f);
}
#endif
