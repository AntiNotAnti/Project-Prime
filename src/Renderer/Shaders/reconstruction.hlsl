// Enhanced-only spatial reconstruction. The scene is tone-mapped and graded
// at its native render resolution, then reconstructed once into the full-size
// display-linear target. HUD, visor, overlays, and capture composition remain
// outside this pass and therefore stay pixel-sharp.

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

cbuffer ReconstructionConstants : register(b0, space3)
{
    float4 sourceAndDestination; // xy source size, zw destination size
    float4 reconstructionOptions; // x sharpness, y enabled
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float Lanczos2(float value)
{
    value = abs(value);
    if (value < 0.0001f) return 1.0f;
    if (value >= 2.0f) return 0.0f;
    float x = 3.14159265359f * value;
    return sin(x) * sin(x * 0.5f) / (0.5f * x * x);
}

float3 Reconstruct(float2 uv)
{
    float2 sourceSize = max(sourceAndDestination.xy, float2(1.0f, 1.0f));
    float2 sourcePosition = uv * sourceSize - 0.5f;
    float2 basePosition = floor(sourcePosition);
    float2 fraction = sourcePosition - basePosition;
    float3 sum = float3(0.0f, 0.0f, 0.0f);
    float weightSum = 0.0f;
    [unroll]
    for (int y = -1; y <= 2; y++)
    {
        [unroll]
        for (int x = -1; x <= 2; x++)
        {
            float weight = Lanczos2((float)x - fraction.x)
                * Lanczos2((float)y - fraction.y);
            float2 samplePosition = clamp(basePosition + float2(x, y),
                float2(0.0f, 0.0f), sourceSize - 1.0f);
            float2 sampleUv = (samplePosition + 0.5f) / sourceSize;
            sum += sourceTexture.SampleLevel(sourceSampler, sampleUv, 0.0f).rgb
                * weight;
            weightSum += weight;
        }
    }
    return sum / max(weightSum, 0.0001f);
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    if (reconstructionOptions.y <= 0.5f)
        return sourceTexture.SampleLevel(sourceSampler, input.texcoord, 0.0f);

    float2 destinationSize = max(sourceAndDestination.zw,
        float2(1.0f, 1.0f));
    float2 destinationTexel = 1.0f / destinationSize;
    float3 center = Reconstruct(input.texcoord);
    float3 north = Reconstruct(input.texcoord + float2(0.0f, -destinationTexel.y));
    float3 south = Reconstruct(input.texcoord + float2(0.0f, destinationTexel.y));
    float3 west = Reconstruct(input.texcoord + float2(-destinationTexel.x, 0.0f));
    float3 east = Reconstruct(input.texcoord + float2(destinationTexel.x, 0.0f));
    float3 neighborhoodMin = min(center, min(min(north, south), min(west, east)));
    float3 neighborhoodMax = max(center, max(max(north, south), max(west, east)));
    float3 average = (north + south + west + east) * 0.25f;
    float3 contrast = neighborhoodMax - neighborhoodMin;
    float contrastWeight = saturate(max(max(contrast.r, contrast.g), contrast.b)
        * 4.0f);
    float3 sharpened = center + (center - average)
        * saturate(reconstructionOptions.x) * contrastWeight;
    return float4(clamp(sharpened, neighborhoodMin, neighborhoodMax), 1.0f);
}
#endif
