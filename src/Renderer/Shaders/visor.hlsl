// Viewer-local Enhanced combat visor. Input and output are display-linear.

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

cbuffer VisorConstants : register(b0, space3)
{
    // center clear radius, edge vignette, chromatic separation, distortion
    float4 visorCombat;
    // screen direction x/y (positive y is up), edge opacity, distortion
    float4 visorDamage;
    // linear RGB impulse, scanline interference
    float4 visorDamageColor;
    // low-health edge, low-health interference, center radius, helmet reflection
    float4 visorLowHealth;
    // distortion phase, interference phase, damage center radius, reserved
    float4 visorPhases;
};

static const float MaximumEdgeOpacity = 0.45f;
static const float MaximumChromaticSeparation = 0.006f;
static const float MaximumDistortion = 0.02f;
static const float MaximumInterference = 0.3f;

float EdgeMask(float2 centered, float clearRadius)
{
    return smoothstep(saturate(clearRadius), 1.0f, length(centered));
}

float2 SafeDirection(float2 value)
{
    float magnitude = length(value);
    return magnitude > 0.00001f ? value / magnitude : float2(0.0f, 0.0f);
}

float4 main_ps(float4 position : SV_Position, float2 texcoord : TEXCOORD0) : SV_Target0
{
    float2 centered = texcoord * 2.0f - 1.0f;
    float2 radial = SafeDirection(centered);
    float combatMask = EdgeMask(centered, visorCombat.x);
    float damageMask = EdgeMask(centered, visorPhases.z);
    float lowHealthMask = EdgeMask(centered, visorLowHealth.z);
    float2 damageDirection = SafeDirection(float2(visorDamage.x, -visorDamage.y));
    float directional = dot(damageDirection, damageDirection) > 0.0f
        ? pow(saturate(dot(radial, damageDirection)), 2.0f) : 1.0f;

    float wave = sin((centered.y * 11.0f + centered.x * 7.0f
        + visorPhases.x * 6.2831853f));
    float combatDistortion = min(max(visorCombat.w, 0.0f), MaximumDistortion);
    float damageDistortion = min(max(visorDamage.w, 0.0f), MaximumDistortion);
    float2 offset = radial * wave * combatDistortion * combatMask;
    offset += damageDirection * damageDistortion * damageMask * directional;
    float offsetLength = length(offset);
    if (offsetLength > MaximumDistortion)
        offset *= MaximumDistortion / offsetLength;

    float2 warpedUv = saturate(texcoord + offset);
    float chromatic = min(max(visorCombat.z, 0.0f),
        MaximumChromaticSeparation) * combatMask;
    float2 chromaticOffset = radial * chromatic;
    float4 centerSample = sourceTexture.Sample(sourceSampler, warpedUv);
    float red = sourceTexture.Sample(sourceSampler,
        saturate(warpedUv + chromaticOffset)).r;
    float blue = sourceTexture.Sample(sourceSampler,
        saturate(warpedUv - chromaticOffset)).b;
    float3 color = float3(red, centerSample.g, blue);

    float combatEdge = min(max(visorCombat.y, 0.0f), MaximumEdgeOpacity)
        * combatMask;
    float damageEdge = min(max(visorDamage.z, 0.0f), MaximumEdgeOpacity)
        * damageMask * directional;
    float lowHealthEdge = min(max(visorLowHealth.x, 0.0f), MaximumEdgeOpacity)
        * lowHealthMask;
    float totalEdge = min(combatEdge + damageEdge + lowHealthEdge,
        MaximumEdgeOpacity);
    color *= 1.0f - totalEdge;

    float scanline = 0.5f + 0.5f * sin(texcoord.y * 1600.0f
        + visorPhases.y * 6.2831853f);
    float damageInterference = min(max(visorDamageColor.w, 0.0f),
        MaximumInterference) * damageMask * directional;
    float lowHealthInterference = min(max(visorLowHealth.y, 0.0f),
        MaximumInterference) * lowHealthMask;
    color *= 1.0f - (damageInterference + lowHealthInterference)
        * (0.25f + 0.25f * scanline);
    color += saturate(visorDamageColor.rgb) * damageMask * directional;

    float helmetReflection = saturate(visorLowHealth.w) * combatMask;
    float reflectionBand = pow(saturate(1.0f - abs(radial.y + 0.25f)), 8.0f);
    color += float3(0.035f, 0.05f, 0.06f)
        * helmetReflection * reflectionBand;
    return float4(max(color, 0.0f), centerSample.a);
}
#endif
