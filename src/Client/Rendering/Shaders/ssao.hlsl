// Half-resolution Enhanced SSAO: deterministic rotated 8-tap raw evaluation,
// followed by horizontal and vertical surface-aware bilateral filtering.

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
cbuffer SsaoConstants : register(b0, space3)
{
    row_major float4x4 inverseProjection;
    row_major float4x4 projection;
    row_major float4x4 view;
    float4 ssaoOptions; // x operation, y world radius, z world bias, w strength
    float4 texelOptions; // xy surface texel, zw AO texel
};

Texture2D surfaceTexture : register(t0, space2);
SamplerState surfaceSampler : register(s0, space2);
Texture2D occlusionTexture : register(t1, space2);
SamplerState occlusionSampler : register(s1, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

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

float CoordinateRotation(float2 pixel)
{
    return frac(sin(dot(pixel, float2(12.9898f, 78.233f)))
        * 43758.5453f) * 6.28318530718f;
}

float3 SafeNormalize(float3 value, float3 fallback)
{
    float lengthSquared = dot(value, value);
    return lengthSquared > 0.00000001f
        ? value * rsqrt(lengthSquared) : fallback;
}

float3 ReconstructViewPosition(float2 uv, float linearDepth)
{
    float2 ndc = float2(uv.x * 2.0f - 1.0f,
        1.0f - uv.y * 2.0f);
    float4 farView = mul(inverseProjection, float4(ndc, 1.0f, 1.0f));
    float3 viewRay = farView.xyz / farView.w;
    viewRay /= max(-viewRay.z, 0.0001f);
    return viewRay * linearDepth;
}

float2 ProjectViewPosition(float3 position)
{
    float4 clip = mul(projection, float4(position, 1.0f));
    float2 ndc = clip.xy / max(clip.w, 0.0001f);
    return float2(ndc.x * 0.5f + 0.5f,
        0.5f - ndc.y * 0.5f);
}

float3 Perpendicular(float3 normal)
{
    float3 axis = abs(normal.x) < 0.75f
        ? float3(1.0f, 0.0f, 0.0f)
        : float3(0.0f, 1.0f, 0.0f);
    return SafeNormalize(cross(axis, normal), float3(0.0f, 0.0f, 1.0f));
}

float RawOcclusion(float2 uv, float2 pixel)
{
    float4 center = surfaceTexture.SampleLevel(surfaceSampler, uv, 0);
    float centerDepth = center.z;
    if (centerDepth <= 0.0f) return 1.0f;
    float3 centerPosition = ReconstructViewPosition(uv, centerDepth);
    float3 centerNormal = SafeNormalize(mul((float3x3)view,
        DecodeOctNormal(center.xy)), float3(0.0f, 1.0f, 0.0f));
    float angle = CoordinateRotation(pixel);
    float3 randomAxis = float3(cos(angle), sin(angle), 0.0f);
    float3 tangent = SafeNormalize(randomAxis
        - centerNormal * dot(randomAxis, centerNormal),
        Perpendicular(centerNormal));
    float3 bitangent = SafeNormalize(cross(centerNormal, tangent),
        Perpendicular(centerNormal));
    static const float3 kernel[8] = {
        float3(-0.00838357f, 0.00619598f, 0.03270313f),
        float3(0.07861696f, 0.02191250f, 0.03666586f),
        float3(-0.07420653f, 0.08852110f, 0.12068250f),
        float3(-0.14775101f, -0.15629660f, 0.15347359f),
        float3(-0.04577608f, 0.04613560f, 0.18834935f),
        float3(-0.02546187f, 0.06705395f, 0.00960697f),
        float3(0.51128659f, -0.31241221f, 0.12213019f),
        float3(0.36317529f, -0.28364695f, 0.67895861f)
    };
    float occluded = 0.0f;
    [unroll]
    for (int index = 0; index < 8; index++)
    {
        float3 sampleDirection = tangent * kernel[index].x
            + bitangent * kernel[index].y + centerNormal * kernel[index].z;
        float3 samplePosition = centerPosition
            + sampleDirection * ssaoOptions.y;
        if (samplePosition.z >= -0.0001f) continue;
        float2 sampleUv = ProjectViewPosition(samplePosition);
        if (any(sampleUv <= 0.0f) || any(sampleUv >= 1.0f)) continue;
        float4 neighbor = surfaceTexture.SampleLevel(surfaceSampler,
            sampleUv, 0);
        if (neighbor.z <= 0.0f) continue;
        float3 neighborPosition = ReconstructViewPosition(sampleUv, neighbor.z);
        float3 delta = neighborPosition - centerPosition;
        float distanceToNeighbor = length(delta);
        float hemisphereSeparation = dot(delta, centerNormal);
        float sampleDepth = -samplePosition.z;
        float depthBlocked = neighbor.z < sampleDepth - ssaoOptions.z
            ? 1.0f : 0.0f;
        float rangeWeight = 1.0f - smoothstep(ssaoOptions.y * 0.1f,
            ssaoOptions.y, distanceToNeighbor);
        float angleWeight = saturate(hemisphereSeparation
            / max(distanceToNeighbor, 0.0001f));
        occluded += depthBlocked * step(ssaoOptions.z,
            hemisphereSeparation) * rangeWeight * angleWeight;
    }
    return saturate(1.0f - occluded * (ssaoOptions.w / 8.0f));
}

float Bilateral(float2 uv, bool horizontal)
{
    float4 center = surfaceTexture.SampleLevel(surfaceSampler, uv, 0);
    if (center.z <= 0.0f) return 1.0f;
    float3 centerNormal = DecodeOctNormal(center.xy);
    float2 axis = horizontal ? float2(texelOptions.z, 0.0f)
        : float2(0.0f, texelOptions.w);
    float sum = 0.0f;
    float weightSum = 0.0f;
    [unroll]
    for (int tap = -2; tap <= 2; tap++)
    {
        float2 sampleUv = uv + axis * tap;
        float4 surface = surfaceTexture.SampleLevel(surfaceSampler, sampleUv, 0);
        if (surface.z <= 0.0f) continue;
        float spatial = tap == 0 ? 0.40f : abs(tap) == 1 ? 0.24f : 0.06f;
        float depthWeight = exp(-abs(surface.z - center.z)
            / max(center.z * 0.02f, 0.0001f));
        float normalWeight = pow(saturate(dot(centerNormal,
            DecodeOctNormal(surface.xy))), 8.0f);
        float weight = spatial * depthWeight * normalWeight;
        sum += occlusionTexture.SampleLevel(occlusionSampler, sampleUv, 0).r
            * weight;
        weightSum += weight;
    }
    return weightSum > 0.0f ? sum / weightSum : 1.0f;
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    float ao = ssaoOptions.x < 0.5f
        ? RawOcclusion(input.texcoord, input.position.xy)
        : Bilateral(input.texcoord, ssaoOptions.x < 1.5f);
    return float4(ao, ao, ao, 1.0f);
}
#endif
