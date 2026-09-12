// VE22 Enhanced authored sky. This pass writes only scene color before opaque
// geometry; it never writes depth or selective bloom.

#ifdef VERTEX_STAGE
struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

VertexOutput main_vs(uint vertexId : SV_VertexID)
{
    VertexOutput output;
    float2 position = vertexId == 0 ? float2(-1.0f, -1.0f)
        : vertexId == 1 ? float2(3.0f, -1.0f) : float2(-1.0f, 3.0f);
    output.position = float4(position, 0.0f, 1.0f);
    output.texcoord = float2(position.x * 0.5f + 0.5f,
        0.5f - position.y * 0.5f);
    return output;
}
#else
Texture2D skyTexture0 : register(t0, space2);
SamplerState skySampler0 : register(s0, space2);
Texture2D skyTexture1 : register(t1, space2);
SamplerState skySampler1 : register(s1, space2);
Texture2D skyTexture2 : register(t2, space2);
SamplerState skySampler2 : register(s2, space2);
Texture2D skyTexture3 : register(t3, space2);
SamplerState skySampler3 : register(s3, space2);
Texture2D skyTexture4 : register(t4, space2);
SamplerState skySampler4 : register(s4, space2);
Texture2D skyTexture5 : register(t5, space2);
SamplerState skySampler5 : register(s5, space2);

cbuffer SkyConstants : register(b0, space3)
{
    row_major float4x4 inverseProjection;
    row_major float4x4 inverseViewRotation;
    // x: 0 background, 1 cubemap, 2 overlay; y: time; z: opacity; w: intensity
    float4 skyOptions;
    // x/y scroll, z rotation speed, w twinkle rate
    float4 skyMotion;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float SRGBToLinearComponent(float value)
{
    value = saturate(value);
    return value <= 0.04045f ? value / 12.92f
        : pow((value + 0.055f) / 1.055f, 2.4f);
}

float3 SRGBToLinear(float3 value)
{
    return float3(SRGBToLinearComponent(value.r),
        SRGBToLinearComponent(value.g), SRGBToLinearComponent(value.b));
}

float3 WorldDirection(float2 texcoord)
{
    float2 ndc = float2(texcoord.x * 2.0f - 1.0f,
        1.0f - texcoord.y * 2.0f);
    float4 viewPosition = mul(inverseProjection, float4(ndc, 1.0f, 1.0f));
    float3 viewDirection = normalize(viewPosition.xyz
        / max(abs(viewPosition.w), 0.00001f));
    return normalize(mul(inverseViewRotation,
        float4(viewDirection, 0.0f)).xyz);
}

float3 RotateY(float3 direction, float angle)
{
    float cosine = cos(angle);
    float sine = sin(angle);
    return float3(cosine * direction.x + sine * direction.z,
        direction.y, -sine * direction.x + cosine * direction.z);
}

float2 EquirectangularUv(float3 direction)
{
    return float2(atan2(direction.z, direction.x) / 6.283185307f + 0.5f,
        0.5f - asin(clamp(direction.y, -1.0f, 1.0f)) / 3.141592654f);
}

float4 SampleCubeFaces(float3 direction)
{
    float3 magnitude = abs(direction);
    float2 uv;
    if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
    {
        uv = direction.x >= 0.0f
            ? float2(-direction.z, -direction.y) / magnitude.x
            : float2(direction.z, -direction.y) / magnitude.x;
        uv = uv * 0.5f + 0.5f;
        return direction.x >= 0.0f
            ? skyTexture0.Sample(skySampler0, uv)
            : skyTexture1.Sample(skySampler1, uv);
    }
    if (magnitude.y >= magnitude.z)
    {
        uv = direction.y >= 0.0f
            ? float2(direction.x, direction.z) / magnitude.y
            : float2(direction.x, -direction.z) / magnitude.y;
        uv = uv * 0.5f + 0.5f;
        return direction.y >= 0.0f
            ? skyTexture2.Sample(skySampler2, uv)
            : skyTexture3.Sample(skySampler3, uv);
    }
    uv = direction.z >= 0.0f
        ? float2(direction.x, -direction.y) / magnitude.z
        : float2(-direction.x, -direction.y) / magnitude.z;
    uv = uv * 0.5f + 0.5f;
    return direction.z >= 0.0f
        ? skyTexture4.Sample(skySampler4, uv)
        : skyTexture5.Sample(skySampler5, uv);
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    float time = skyOptions.y;
    float3 direction = RotateY(WorldDirection(input.texcoord),
        skyMotion.z * time);
    float4 sampled;
    if (skyOptions.x > 0.5f && skyOptions.x < 1.5f)
    {
        sampled = SampleCubeFaces(direction);
    }
    else
    {
        float2 uv = EquirectangularUv(direction)
            + skyMotion.xy * time;
        uv = float2(frac(uv.x), saturate(uv.y));
        sampled = skyTexture0.Sample(skySampler0, uv);
    }
    float twinkle = skyMotion.w > 0.0f
        ? 0.75f + 0.25f * sin(time * skyMotion.w * 6.283185307f) : 1.0f;
    sampled.rgb = SRGBToLinear(sampled.rgb)
        * max(skyOptions.w, 0.0f) * twinkle;
    if (skyOptions.x < 1.5f) return float4(sampled.rgb, 1.0f);
    return float4(sampled.rgb, saturate(sampled.a * skyOptions.z));
}
#endif
