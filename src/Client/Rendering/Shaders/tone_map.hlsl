// VE2 color transform. The first use applies fixed-exposure tone mapping into
// a display-linear composition target. The final/capture use skips tone
// mapping and optionally performs the linear-to-sRGB transfer for UNORM output;
// an sRGB target performs that last transfer in hardware.

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
cbuffer ToneMapConstants : register(b0, space3)
{
    float4 toneMapOptions; // x exposure, y shader gamma conversion, z tone map enabled
};

Texture2D sceneTexture : register(t0, space2);
SamplerState sceneSampler : register(s0, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float3 ToneMapAces(float3 linearHdr)
{
    float3 x = max(linearHdr * toneMapOptions.x, 0.0f);
    return saturate((x * (2.51f * x + 0.03f))
        / (x * (2.43f * x + 0.59f) + 0.14f));
}

float LinearToSRGBComponent(float value)
{
    value = saturate(value);
    return value <= 0.0031308f ? value * 12.92f
        : 1.055f * pow(value, 1.0f / 2.4f) - 0.055f;
}

float3 LinearToSRGB(float3 value)
{
    return float3(LinearToSRGBComponent(value.r),
        LinearToSRGBComponent(value.g), LinearToSRGBComponent(value.b));
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    float4 source = sceneTexture.Sample(sceneSampler, input.texcoord);
    float3 displayLinear = toneMapOptions.z > 0.5f
        ? ToneMapAces(source.rgb) : saturate(source.rgb);
    float3 output = toneMapOptions.y > 0.5f
        ? LinearToSRGB(displayLinear) : displayLinear;
    return float4(output, saturate(source.a));
}
#endif
