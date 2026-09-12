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
Texture2D lutTexture : register(t1, space2);
SamplerState lutSampler : register(s1, space2);

cbuffer ColorGradeConstants : register(b0, space3)
{
    float4 colorGradeOptions;
};

static const float LutDimension = 16.0f;
static const float2 LutTextureSize = float2(256.0f, 16.0f);

float2 LutUv(float red, float green, float blueSlice)
{
    float pixelX = blueSlice * LutDimension + red * (LutDimension - 1.0f) + 0.5f;
    float pixelY = green * (LutDimension - 1.0f) + 0.5f;
    return float2(pixelX, pixelY) / LutTextureSize;
}

float4 main_ps(float4 position : SV_Position, float2 texcoord : TEXCOORD0) : SV_Target0
{
    float4 source = sourceTexture.Sample(sourceSampler, texcoord);
    float3 displayLinear = saturate(source.rgb);
    float blue = displayLinear.b * (LutDimension - 1.0f);
    float lowerSlice = floor(blue);
    float upperSlice = min(lowerSlice + 1.0f, LutDimension - 1.0f);
    float3 lower = lutTexture.Sample(lutSampler,
        LutUv(displayLinear.r, displayLinear.g, lowerSlice)).rgb;
    float3 upper = lutTexture.Sample(lutSampler,
        LutUv(displayLinear.r, displayLinear.g, upperSlice)).rgb;
    float3 graded = lerp(lower, upper, frac(blue));
    return float4(lerp(displayLinear, graded, saturate(colorGradeOptions.x)), source.a);
}
#endif
