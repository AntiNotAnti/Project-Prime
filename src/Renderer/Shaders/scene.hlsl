// R7 base scene shader.  This is deliberately a single canonical HLSL source
// compiled offline with SDL_shadercross for SPIR-V, MSL, and DXIL.  SDL never
// invokes a compiler at runtime.
//
// SDL's default D3D12 semantic map is TEXCOORD based.  Keep every vertex input
// on TEXCOORDn even though POSITION/COLOR would be conventional HLSL names.

#ifdef VERTEX_STAGE
cbuffer FrameConstants : register(b0, space1)
{
    row_major float4x4 view;
    row_major float4x4 projection;
    float4 frameOptions; // x lighting, y show-colors, z show-textures, w enhanced per-pixel lighting
};
#else
cbuffer FrameConstants : register(b0, space3)
{
    float4 fog;          // rgb colour, a enabled
    float4 frameOptions; // x fog, y cel bands, z enhanced per-pixel lighting, w lighting
    float4 fogRange;     // x minimum depth, y maximum depth
    float4 cameraWorldPosition;
    float4 visualLightPositionRadius[32];
    float4 visualLightColorIntensity[32];
    float4 visualLightOptions; // x light count, y authored HUD color to display-linear, zw viewport
    uint4 visualLightTileMasks[36]; // 16x9 screen tiles, four masks per vector
    row_major float4x4 shadowViewProjection;
    float4 shadowOptions; // x enabled, y selected light, z inverse map size, w depth bias
    float4 enhancedFogColorDensity; // rgb linearized below, a density
    float4 enhancedFogHeightFalloff; // x height, y falloff, z enabled
};
#endif

#ifdef VERTEX_STAGE
cbuffer VertexDrawConstants : register(b1, space1)
{
    // A submission with a matrix stack selects matrixStack[vertex.matrixIndex]
    // from the separate vertex-only palette. A submission without one sets
    // drawOptions.w to zero and receives an identity palette from the CPU.
    row_major float4x4 transform;
    row_major float4x4 billboard;
    row_major float4x4 texcoordMatrix;
    float4 diffuse;
    float4 ambient;
    float4 specular;
    float4 emission;
    float4 light1Vector;
    float4 light1Color;
    float4 light2Vector;
    float4 light2Color;
    float4 drawOptions;     // x texture, y colour override, z palette, w stack
    float4 materialOptions; // x alpha, y polygon mode, z texgen mode, w lighting
};

cbuffer MatrixPaletteConstants : register(b2, space1)
{
    row_major float4x4 matrixStack[31];
};
#else
cbuffer FragmentMaterialConstants : register(b1, space3)
{
    row_major float4x4 texcoordMatrix;
    float4 diffuse;
    float4 specular;
    float4 emission;
    float4 overrideColor;
    float4 paletteOverride;
    float4 light1Vector;
    float4 light1Color;
    float4 light2Vector;
    float4 light2Color;
    float4 drawOptions;     // x texture, y colour override, z palette, w stack
    float4 materialOptions; // x alpha, y polygon mode, z texgen mode, w lighting
    float4 renderOptions;   // x alpha test, y cel-flat, z bloom, w normal map
    float4 flatColor;       // rgb alpha-weighted texture average for cel surfaces
    float4 enhancedEmission; // rgb linear tint, a strength
    float4 enhancedOptions;  // x mapped emissive texture
    float4 reflectionOptions; // x enabled, y strength, z smoothness, w mip level
    float4 softParticleOptions; // x enabled, y fade distance
    float4 beamCore;       // rgb HDR core color, a relative width
    float4 beamGlow;       // rgb HDR glow color, a relative width
    float4 beamOptions;    // x noise strength, y scale, z phase, w pulse
    float4 forceFieldEmission; // rgb color, a HDR strength
    float4 forceFieldOptions;  // x noise scale, y strength, z phase, w Fresnel power
    float4 forceFieldFlow;     // xy sampled UV flow, z Fresnel strength, w intersection
};
#endif

struct VertexInput
{
    float3 position : TEXCOORD0;
    float4 color : TEXCOORD1;
    float3 normal : TEXCOORD2;
    float2 texcoord : TEXCOORD3;
    uint matrixIndex : TEXCOORD4;
    uint vertexFlags : TEXCOORD5;
    float4 tangent : TEXCOORD6;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 worldPosition : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
    float2 texcoord : TEXCOORD2;
    float4 vertexColor : TEXCOORD3;
    float3 lightingDiffuse : TEXCOORD4;
    float3 lightingAmbient : TEXCOORD5;
    float4 worldTangent : TEXCOORD6;
    float viewDepth : TEXCOORD7;
};

#ifdef VERTEX_STAGE
float3 SafeNormalize(float3 value, float3 fallback)
{
    float lengthSquared = dot(value, value);
    return lengthSquared > 0.00000001f ? value * rsqrt(lengthSquared) : fallback;
}

float3 TransformNormal(float3 normal, float4x4 modelMatrix)
{
    // The cofactor matrix is the inverse-transpose without its determinant.
    // Normalization removes that common scale and keeps non-uniform model
    // transforms from skewing the lighting normal.
    float3x3 model = (float3x3)modelMatrix;
    float3x3 normalMatrix = float3x3(
        cross(model[1], model[2]),
        cross(model[2], model[0]),
        cross(model[0], model[1]));
    float3 transformed = mul(normalMatrix, normal);
    float determinant = dot(model[0], cross(model[1], model[2]));
    transformed *= determinant < 0.0f ? -1.0f : 1.0f;
    float3 legacyFallback = mul(model, normal);
    return SafeNormalize(transformed, SafeNormalize(legacyFallback, float3(0.0f, 1.0f, 0.0f)));
}

float3 Perpendicular(float3 normal)
{
    float3 axis = abs(normal.x) <= abs(normal.y) && abs(normal.x) <= abs(normal.z)
        ? float3(1.0f, 0.0f, 0.0f)
        : abs(normal.y) <= abs(normal.z)
            ? float3(0.0f, 1.0f, 0.0f) : float3(0.0f, 0.0f, 1.0f);
    return SafeNormalize(cross(axis, normal), float3(1.0f, 0.0f, 0.0f));
}

float3 LegacyLight(float3 lightVector, float3 lightColor, float3 normal,
    float3 diffuseColor, float3 ambientColor, float3 specularColor)
{
    // The DS light vectors point from the surface.  The legacy shader uses
    // this sign and a fixed -Z sight vector; keep both details for parity.
    float3 sight = float3(0.0f, 0.0f, -1.0f);
    float diffuseFactor = max(0.0f, -dot(lightVector, normal));
    float3 halfVector = (lightVector + sight) * 0.5f;
    float specularFactor = max(0.0f, dot(-halfVector, normal));
    specularFactor *= specularFactor;
    return specularColor * lightColor * specularFactor
        + diffuseColor * lightColor * diffuseFactor
        + ambientColor * lightColor;
}

VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    uint matrixIndex = min(input.matrixIndex, 30u);
    float4x4 stackMatrix = drawOptions.w > 0.5f ? matrixStack[matrixIndex] : transform;
    // Legacy order is stack * billboard * vertex.  A billboard only replaces
    // the view inverse rotation; it never adds another model transform.
    float4x4 modelMatrix = mul(stackMatrix, billboard);
    float4 world = mul(modelMatrix, float4(input.position, 1.0f));
    output.worldPosition = world.xyz;
    float4 viewPosition = mul(view, world);
    output.position = mul(projection, viewPosition);
    // Legacy OpenGL projection matrices produce NDC depth in [-1, 1]. SDL's
    // GPU backends use [0, 1], so remap clip depth without changing x/y/w.
    output.position.z = (output.position.z + output.position.w) * 0.5f;
    output.viewDepth = max(-viewPosition.z, 0.0f);
    bool enhancedPerPixel = frameOptions.w > 0.5f;
    // Original keeps the exact legacy mat3(model) normal transform. Only
    // Enhanced opts into inverse-transpose-equivalent normal handling.
    output.worldNormal = enhancedPerPixel
        ? TransformNormal(input.normal, modelMatrix)
        : normalize(mul((float3x3)modelMatrix, input.normal));
    float3 worldTangent = mul((float3x3)modelMatrix, input.tangent.xyz);
    worldTangent -= output.worldNormal * dot(output.worldNormal, worldTangent);
    worldTangent = SafeNormalize(worldTangent, Perpendicular(output.worldNormal));
    float3x3 model = (float3x3)modelMatrix;
    float determinant = dot(model[0], cross(model[1], model[2]));
    float sourceHandedness = input.tangent.w < 0.0f ? -1.0f : 1.0f;
    output.worldTangent = float4(worldTangent,
        sourceHandedness * (determinant < 0.0f ? -1.0f : 1.0f));

    uint explicitColor = input.vertexFlags & 1u;
    float3 vertexColor = frameOptions.y > 0.5f
        ? (explicitColor != 0u ? input.color.rgb : diffuse.rgb)
        : float3(1.0f, 1.0f, 1.0f);
    float3 resolvedDiffuse = diffuse.rgb;
    float3 resolvedAmbient = ambient.rgb;
    // DIF_AMB state may change between vertices in one primitive. Resolve its
    // contribution per vertex and interpolate the actual values; testing an
    // interpolated alpha sentinel in the fragment stage is not well-defined.
    if (explicitColor != 0u && input.color.a == 0.0f)
    {
        resolvedDiffuse = vertexColor;
        resolvedAmbient = float3(0.0f, 0.0f, 0.0f);
    }
    output.lightingDiffuse = resolvedDiffuse;
    output.lightingAmbient = resolvedAmbient;
    if (!enhancedPerPixel && frameOptions.x > 0.5f && materialOptions.w > 0.5f)
    {
        float3 color1 = LegacyLight(light1Vector.xyz, light1Color.rgb,
            output.worldNormal, resolvedDiffuse, resolvedAmbient, specular.rgb);
        float3 color2 = LegacyLight(light2Vector.xyz, light2Color.rgb,
            output.worldNormal, resolvedDiffuse, resolvedAmbient, specular.rgb);
        output.vertexColor = float4(min(color1 + color2 + emission.rgb,
            float3(1.0f, 1.0f, 1.0f)), 1.0f);
    }
    else
    {
        output.vertexColor = float4(vertexColor, 1.0f);
    }

    uint texgen = (uint)materialOptions.z;
    if (drawOptions.x > 0.5f)
    {
        if (texgen == 2u)
        {
            // Match the legacy DS texgen construction exactly: it uses the
            // node stack (never billboard space), includes view orientation
            // only when lighting is active, and keeps the source UV as the
            // affine offset in the two generated rows.
            float4x4 lightView = (frameOptions.x > 0.5f && materialOptions.w > 0.5f)
                ? view : float4x4(1.0f, 0.0f, 0.0f, 0.0f,
                                  0.0f, 1.0f, 0.0f, 0.0f,
                                  0.0f, 0.0f, 1.0f, 0.0f,
                                  0.0f, 0.0f, 0.0f, 1.0f);
            float4x4 texgenMatrix = mul(texcoordMatrix, mul(lightView, stackMatrix));
            output.texcoord = float2(
                dot(input.normal, float3(texgenMatrix[0][0], texgenMatrix[1][0], texgenMatrix[2][0])) + input.texcoord.x,
                dot(input.normal, float3(texgenMatrix[0][1], texgenMatrix[1][1], texgenMatrix[2][1])) + input.texcoord.y);
        }
        else if (texgen == 3u)
        {
            output.texcoord = float2(
                dot(input.position, float3(texcoordMatrix[0][0], texcoordMatrix[1][0], texcoordMatrix[2][0])) + input.texcoord.x,
                dot(input.position, float3(texcoordMatrix[0][1], texcoordMatrix[1][1], texcoordMatrix[2][1])) + input.texcoord.y);
        }
        else
        {
            output.texcoord = mul(texcoordMatrix,
                float4(input.texcoord, 0.0f, 1.0f)).xy;
        }
    }
    else
    {
        output.texcoord = float2(0.0f, 0.0f);
    }
    return output;
}
#endif

#ifndef VERTEX_STAGE
Texture2D sceneTexture : register(t0, space2);
SamplerState sceneSampler : register(s0, space2);
Texture2D normalTexture : register(t1, space2);
SamplerState normalSampler : register(s1, space2);
Texture2D emissiveTexture : register(t2, space2);
SamplerState emissiveSampler : register(s2, space2);
TextureCube reflectionTexture : register(t3, space2);
SamplerState reflectionSampler : register(s3, space2);
Texture2D ambientOcclusionTexture : register(t4, space2);
SamplerState ambientOcclusionSampler : register(s4, space2);
Texture2D shadowTexture : register(t5, space2);
SamplerState shadowSampler : register(s5, space2);
Texture2D surfaceDataTexture : register(t6, space2);
SamplerState surfaceDataSampler : register(s6, space2);

float3 SafeNormalize(float3 value, float3 fallback)
{
    float lengthSquared = dot(value, value);
    return lengthSquared > 0.00000001f ? value * rsqrt(lengthSquared) : fallback;
}

float3 FragmentPerpendicular(float3 normal)
{
    float3 axis = abs(normal.x) <= abs(normal.y) && abs(normal.x) <= abs(normal.z)
        ? float3(1.0f, 0.0f, 0.0f)
        : abs(normal.y) <= abs(normal.z)
            ? float3(0.0f, 1.0f, 0.0f) : float3(0.0f, 0.0f, 1.0f);
    return SafeNormalize(cross(axis, normal), float3(1.0f, 0.0f, 0.0f));
}

float SRGBToLinearComponent(float value)
{
    value = max(value, 0.0f);
    return value <= 0.04045f ? value / 12.92f
        : pow((value + 0.055f) / 1.055f, 2.4f);
}

float3 SRGBToLinear(float3 value)
{
    return float3(SRGBToLinearComponent(value.r),
        SRGBToLinearComponent(value.g), SRGBToLinearComponent(value.b));
}

float PresentationNoise(float2 coordinate)
{
    float value = sin(dot(coordinate, float2(12.9898f, 78.233f)))
        * 43758.5453f;
    return frac(value) * 2.0f - 1.0f;
}

float3 ResolveNormalMap(VertexOutput input, float3 geometryNormal)
{
    float3 tangent = input.worldTangent.xyz
        - geometryNormal * dot(geometryNormal, input.worldTangent.xyz);
    tangent = SafeNormalize(tangent, FragmentPerpendicular(geometryNormal));
    float handedness = input.worldTangent.w < 0.0f ? -1.0f : 1.0f;
    float3 bitangent = SafeNormalize(cross(geometryNormal, tangent) * handedness,
        cross(geometryNormal, tangent));

    // The authored tangent frame describes the source UVs. Apply the inverse
    // of the draw's 2x2 texture transform so rotated, scaled, and mirrored UVs
    // continue to address the normal map in the transformed coordinate space.
    float a = texcoordMatrix[0][0];
    float b = texcoordMatrix[0][1];
    float c = texcoordMatrix[1][0];
    float d = texcoordMatrix[1][1];
    float determinant = a * d - b * c;
    float3 adjustedTangent = tangent;
    float3 adjustedBitangent = bitangent;
    if (abs(determinant) > 0.00000001f)
    {
        float reciprocalDeterminant = 1.0f / determinant;
        adjustedTangent = SafeNormalize(
            (d * tangent - c * bitangent) * reciprocalDeterminant, tangent);
        adjustedBitangent = SafeNormalize(
            (-b * tangent + a * bitangent) * reciprocalDeterminant, bitangent);
    }

    float3 tangentNormal = normalTexture.Sample(normalSampler, input.texcoord).xyz
        * 2.0f - 1.0f;
    tangentNormal = SafeNormalize(tangentNormal, float3(0.0f, 0.0f, 1.0f));
    return SafeNormalize(adjustedTangent * tangentNormal.x
        + adjustedBitangent * tangentNormal.y
        + geometryNormal * tangentNormal.z, geometryNormal);
}

float DistributionGGX(float normalDotHalf, float roughness)
{
    float alpha = max(roughness * roughness, 0.002025f);
    float alphaSquared = alpha * alpha;
    float denominator = normalDotHalf * normalDotHalf
        * (alphaSquared - 1.0f) + 1.0f;
    return alphaSquared / max(3.14159265359f * denominator * denominator,
        0.000001f);
}

float GeometrySchlickGGX(float normalDotDirection, float roughness)
{
    float shifted = roughness + 1.0f;
    float k = shifted * shifted * 0.125f;
    return normalDotDirection / max(normalDotDirection * (1.0f - k) + k,
        0.000001f);
}

float3 FresnelSchlick(float cosine, float3 reflectance)
{
    return reflectance + (1.0f - reflectance)
        * pow(1.0f - saturate(cosine), 5.0f);
}

float3 EvaluateGgxBrdf(float3 normal, float3 lightDirection,
    float3 viewDirection, float3 diffuseColor, float3 specularColor,
    float smoothness)
{
    float normalDotLight = saturate(dot(normal, lightDirection));
    float normalDotView = saturate(dot(normal, viewDirection));
    if (normalDotLight <= 0.0f || normalDotView <= 0.0f)
        return float3(0.0f, 0.0f, 0.0f);

    float3 halfVector = SafeNormalize(lightDirection + viewDirection, normal);
    float normalDotHalf = saturate(dot(normal, halfVector));
    float viewDotHalf = saturate(dot(viewDirection, halfVector));
    float roughness = max(1.0f - saturate(smoothness), 0.045f);
    float3 reflectance = saturate(specularColor);
    float3 fresnel = FresnelSchlick(viewDotHalf, reflectance);
    float distribution = DistributionGGX(normalDotHalf, roughness);
    float geometry = GeometrySchlickGGX(normalDotView, roughness)
        * GeometrySchlickGGX(normalDotLight, roughness);
    float3 specularTerm = distribution * geometry * fresnel
        / max(4.0f * normalDotView * normalDotLight, 0.0001f);
    // Specular-gloss keeps authored specular RGB as F0 and conserves the
    // reflected portion of the diffuse lobe without changing material data.
    float3 diffuseTerm = (1.0f - fresnel) * diffuseColor;
    return (diffuseTerm + specularTerm) * normalDotLight;
}

float EvaluateDirectionalShadow(float3 worldPosition, float3 normal,
    float3 lightVector)
{
    if (shadowOptions.x <= 0.5f) return 1.0f;
    float4 shadowClip = mul(shadowViewProjection,
        float4(worldPosition, 1.0f));
    if (shadowClip.w <= 0.00001f) return 1.0f;
    float3 shadowNdc = shadowClip.xyz / shadowClip.w;
    float2 shadowUv = shadowNdc.xy * 0.5f + 0.5f;
    shadowUv.y = 1.0f - shadowUv.y;
    float shadowDepth = shadowNdc.z * 0.5f + 0.5f;
    if (any(shadowUv < 0.0f) || any(shadowUv > 1.0f)
        || shadowDepth <= 0.0f || shadowDepth >= 1.0f)
        return 1.0f;

    float3 lightDirection = SafeNormalize(-lightVector, normal);
    float slopeBias = shadowOptions.w
        * (1.0f + 2.0f * (1.0f - saturate(dot(normal, lightDirection))));
    float visibility = 0.0f;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float storedDepth = shadowTexture.SampleLevel(shadowSampler,
                shadowUv + float2(x, y) * shadowOptions.z, 0.0f).r;
            visibility += shadowDepth - slopeBias <= storedDepth ? 1.0f : 0.0f;
        }
    }
    return visibility / 9.0f;
}

float3 EvaluateDirectionalLight(float3 lightVector, float3 lightColor,
    float3 normal, float3 viewDirection, float3 diffuseColor,
    float3 specularColor, float smoothness, float visibility)
{
    float3 lightDirection = SafeNormalize(-lightVector, normal);
    return EvaluateGgxBrdf(normal, lightDirection, viewDirection,
        diffuseColor, specularColor, smoothness) * lightColor * visibility;
}

float3 EvaluateRoomLighting(float3 worldPosition, float3 normal,
    float3 diffuseColor, float3 ambientColor, float3 specularColor,
    float smoothness, float3 roomLight1Color, float3 roomLight2Color,
    float ambientOcclusion)
{
    float3 viewDirection = SafeNormalize(
        cameraWorldPosition.xyz - worldPosition, -light1Vector.xyz);
    float3 ambientLight = roomLight1Color + roomLight2Color;
    float shadow1 = shadowOptions.y < 0.5f
        ? EvaluateDirectionalShadow(worldPosition, normal, light1Vector.xyz) : 1.0f;
    float shadow2 = shadowOptions.y >= 0.5f
        ? EvaluateDirectionalShadow(worldPosition, normal, light2Vector.xyz) : 1.0f;
    // SSAO is intentionally confined to the room ambient term. Directional
    // and point lights, reflections, and emission remain unoccluded.
    return ambientColor * ambientLight * ambientOcclusion
        + EvaluateDirectionalLight(light1Vector.xyz, roomLight1Color,
            normal, viewDirection, diffuseColor, specularColor, smoothness, shadow1)
        + EvaluateDirectionalLight(light2Vector.xyz, roomLight2Color,
            normal, viewDirection, diffuseColor, specularColor, smoothness, shadow2);
}

float3 EvaluatePointLight(uint lightIndex, float3 worldPosition, float3 normal,
    float3 diffuseColor, float3 specularColor, float smoothness,
    bool includeSpecular, bool linearLighting)
{
    float3 delta = visualLightPositionRadius[lightIndex].xyz - worldPosition;
    float distanceToLight = length(delta);
    float radius = max(visualLightPositionRadius[lightIndex].w, 0.0001f);
    float attenuation = saturate(1.0f - distanceToLight / radius);
    attenuation *= attenuation;
    float3 lightDirection = distanceToLight > 0.0001f
        ? delta / distanceToLight : normal;
    float3 viewDirection = SafeNormalize(
        cameraWorldPosition.xyz - worldPosition, normal);
    float3 lightColor = visualLightColorIntensity[lightIndex].rgb;
    if (linearLighting) lightColor = SRGBToLinear(lightColor);
    float3 radiance = lightColor
        * visualLightColorIntensity[lightIndex].w * attenuation;
    return radiance * (includeSpecular
        ? EvaluateGgxBrdf(normal, lightDirection, viewDirection,
            diffuseColor, specularColor, smoothness)
        : diffuseColor * saturate(dot(normal, lightDirection)));
}

uint VisualLightMask(float2 pixelPosition)
{
    float2 viewport = max(visualLightOptions.zw, float2(1.0f, 1.0f));
    uint column = min((uint)(saturate(pixelPosition.x / viewport.x) * 16.0f),
        15u);
    uint row = min((uint)(saturate(pixelPosition.y / viewport.y) * 9.0f),
        8u);
    uint tileIndex = row * 16u + column;
    return visualLightTileMasks[tileIndex >> 2u][tileIndex & 3u];
}

float3 EvaluateReflection(float3 worldPosition, float3 normal,
    float3 specularColor)
{
    float3 viewDirection = SafeNormalize(
        cameraWorldPosition.xyz - worldPosition, normal);
    float3 reflectionVector = reflect(-viewDirection, normal);
    float3 environment = SRGBToLinear(reflectionTexture.SampleLevel(
        reflectionSampler, reflectionVector, max(reflectionOptions.w, 0.0f)).rgb);
    float smoothness = saturate(reflectionOptions.z);
    float3 fresnel = FresnelSchlick(saturate(dot(normal, viewDirection)),
        saturate(specularColor));
    float response = max(reflectionOptions.y, 0.0f)
        * lerp(0.35f, 1.0f, smoothness);
    return environment * fresnel * response;
}

// MPH's fixed 32-entry Octolith toon table. It is immutable content data, so
// keeping the packed RGB5 values in the canonical shader avoids reaching out
// of the sealed render frame for global Metadata state.
static const uint toonPacked[32] = {
    0x2000u, 0x2000u, 0x2020u, 0x2021u, 0x2021u, 0x2041u, 0x2441u, 0x2461u,
    0x2461u, 0x2462u, 0x2482u, 0x2482u, 0x28C3u, 0x2CE4u, 0x3105u, 0x3546u,
    0x3967u, 0x3D88u, 0x41C9u, 0x45EAu, 0x4A0Bu, 0x4E4Bu, 0x526Cu, 0x568Du,
    0x5ACEu, 0x5EEFu, 0x6310u, 0x6751u, 0x6B72u, 0x6F93u, 0x73D4u, 0x77F5u
};

float3 toon_color(float red)
{
    uint packed = toonPacked[min((uint)(saturate(red) * 31.0f), 31u)];
    return float3((packed & 31u) / 31.0f,
        ((packed >> 5u) & 31u) / 31.0f,
        ((packed >> 10u) & 31u) / 31.0f);
}

float3 cel_shade(float3 color)
{
    float steps = frameOptions.y;
    float luminance = max(max(color.r, color.g), color.b);
    if (luminance <= 0.0f)
    {
        return color;
    }
    float scaled = luminance * steps - 0.5f;
    float lower = floor(scaled);
    float level = (lower + 0.5f + smoothstep(0.46f, 0.54f, scaled - lower)) / steps;
    float3 banded = color * (level / luminance);
    float grey = dot(banded, float3(0.299f, 0.587f, 0.114f));
    float3 result = lerp(float3(grey, grey, grey), banded, 1.35f);
    return frameOptions.z > 0.5f ? max(result, 0.0f) : saturate(result);
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    float3 normal = SafeNormalize(input.worldNormal, float3(0.0f, 1.0f, 0.0f));
    bool enhancedPerPixel = frameOptions.z > 0.5f;
    bool lightingEnabled = frameOptions.w > 0.5f && materialOptions.w > 0.5f;
    bool hasMappedEmission = enhancedPerPixel && enhancedOptions.x > 0.5f;
    float3 materialEmission = float3(0.0f, 0.0f, 0.0f);
    if (enhancedPerPixel)
    {
        materialEmission = hasMappedEmission
            ? SRGBToLinear(emissiveTexture.Sample(
                emissiveSampler, input.texcoord).rgb)
                * enhancedEmission.rgb * max(enhancedEmission.a, 0.0f)
            : SRGBToLinear(emission.rgb);
    }
    if (enhancedPerPixel && lightingEnabled && renderOptions.w > 0.5f)
    {
        normal = ResolveNormalMap(input, normal);
    }
    float3 surfaceColor = enhancedPerPixel
        ? SRGBToLinear(input.vertexColor.rgb) : input.vertexColor.rgb;
    float3 pointDiffuse = enhancedPerPixel
        ? SRGBToLinear(input.lightingDiffuse) : diffuse.rgb;
    float2 viewport = max(visualLightOptions.zw, float2(1.0f, 1.0f));
    float ambientOcclusion = enhancedPerPixel
        ? ambientOcclusionTexture.Sample(ambientOcclusionSampler,
            input.position.xy / viewport).r : 1.0f;
    if (enhancedPerPixel && lightingEnabled)
    {
        float3 diffuseCurrent = SRGBToLinear(input.lightingDiffuse);
        float3 ambientCurrent = SRGBToLinear(input.lightingAmbient);
        pointDiffuse = diffuseCurrent;
        surfaceColor = EvaluateRoomLighting(input.worldPosition, normal,
            diffuseCurrent, ambientCurrent, SRGBToLinear(specular.rgb), specular.a,
            SRGBToLinear(light1Color.rgb), SRGBToLinear(light2Color.rgb),
            ambientOcclusion);
    }

    uint visualLightCount = min((uint)visualLightOptions.x, 32u);
    uint visualLightMask = VisualLightMask(input.position.xy);
    float3 visualLighting = float3(0.0f, 0.0f, 0.0f);
    for (uint lightIndex = 0u; lightIndex < visualLightCount; lightIndex++)
    {
        if ((visualLightMask & (1u << lightIndex)) == 0u) continue;
        visualLighting += EvaluatePointLight(lightIndex, input.worldPosition, normal,
            pointDiffuse, enhancedPerPixel ? SRGBToLinear(specular.rgb) : specular.rgb,
            specular.a, enhancedPerPixel && lightingEnabled, enhancedPerPixel);
    }
    surfaceColor = enhancedPerPixel
        ? max(surfaceColor + visualLighting, 0.0f)
        : saturate(surfaceColor + visualLighting);

    bool useTexture = drawOptions.x > 0.5f;
    float4 textureColor = useTexture
        ? sceneTexture.Sample(sceneSampler, input.texcoord)
        : float4(1.0f, 1.0f, 1.0f, 1.0f);
    if (drawOptions.z > 0.5f)
    {
        // Palette overrides replace RGB but preserve indexed texture alpha.
        textureColor = float4(paletteOverride.rgb, textureColor.a);
    }
    else if (renderOptions.y > 0.5f)
    {
        // Cel surfaces keep source alpha/cutouts but replace photographic
        // texel RGB with the texture's alpha-weighted average.
        textureColor.rgb = flatColor.rgb;
    }
    if (enhancedPerPixel)
    {
        textureColor.rgb = SRGBToLinear(textureColor.rgb);
    }

    float4 result;
    uint polygonMode = (uint)materialOptions.y;
    if (!useTexture)
    {
        result = drawOptions.y > 0.5f
            ? float4(enhancedPerPixel ? SRGBToLinear(overrideColor.rgb)
                : overrideColor.rgb, overrideColor.a)
            : float4(surfaceColor, materialOptions.x);
    }
    else if (polygonMode == 1u) // decal
    {
        result = float4(lerp(surfaceColor, textureColor.rgb, textureColor.a),
            materialOptions.x);
    }
    else if (polygonMode == 2u) // toon/highlight mode
    {
        float3 toon = toon_color(surfaceColor.r);
        if (enhancedPerPixel) toon = SRGBToLinear(toon);
        result = float4(textureColor.rgb * surfaceColor.r + toon,
            materialOptions.x * textureColor.a);
    }
    else // modulate
    {
        result = float4(surfaceColor * textureColor.rgb,
            materialOptions.x * textureColor.a);
    }
    if (!useTexture && polygonMode == 2u)
    {
        result.rgb = toon_color(surfaceColor.r);
        if (enhancedPerPixel) result.rgb = SRGBToLinear(result.rgb);
    }
    if (drawOptions.y > 0.5f && useTexture)
    {
        result.rgb = enhancedPerPixel
            ? SRGBToLinear(overrideColor.rgb) : overrideColor.rgb;
        result.a *= overrideColor.a;
    }

    if (enhancedPerPixel && enhancedOptions.y > 0.5f)
    {
        float across = abs(frac(input.texcoord.y) * 2.0f - 1.0f);
        float noise = PresentationNoise(float2(
            input.texcoord.x * beamOptions.y + beamOptions.z * 17.0f,
            input.texcoord.y * beamOptions.y - beamOptions.z * 11.0f));
        across = saturate(across + noise * beamOptions.x * 0.12f);
        float core = 1.0f - smoothstep(
            saturate(beamCore.a * 0.45f), saturate(beamCore.a), across);
        float glow = 1.0f - smoothstep(
            saturate(beamGlow.a * 0.35f), saturate(beamGlow.a), across);
        glow *= saturate(1.0f + noise * beamOptions.x);
        float sourceAlpha = result.a;
        result.rgb = (beamGlow.rgb * glow + beamCore.rgb * core)
            * max(beamOptions.w, 0.0f);
        result.a = sourceAlpha * saturate(max(core, glow));
    }

    if (enhancedPerPixel && enhancedOptions.z > 0.5f)
    {
        float2 flowedUv = input.texcoord + forceFieldFlow.xy;
        float noise = PresentationNoise(flowedUv * forceFieldOptions.x
            + forceFieldOptions.z);
        float noiseMask = saturate(1.0f + noise * forceFieldOptions.y);
        float3 viewDirection = SafeNormalize(
            cameraWorldPosition.xyz - input.worldPosition, normal);
        float fresnel = pow(1.0f - saturate(dot(normal, viewDirection)),
            max(forceFieldOptions.w, 1.0f)) * forceFieldFlow.z;
        float intersection = 0.0f;
        if (enhancedOptions.w > 0.5f)
        {
            float surfaceDepth = surfaceDataTexture.SampleLevel(
                surfaceDataSampler, input.position.xy / viewport, 0.0f).z;
            if (surfaceDepth > 0.0f)
            {
                intersection = (1.0f - saturate(abs(surfaceDepth
                    - input.viewDepth) / 0.18f)) * forceFieldFlow.w;
            }
        }
        float3 fieldEmission = SRGBToLinear(forceFieldEmission.rgb)
            * forceFieldEmission.a * (noiseMask + fresnel + intersection);
        result.rgb += fieldEmission;
    }

    if (enhancedPerPixel && softParticleOptions.x > 0.5f)
    {
        float2 surfaceUv = input.position.xy / viewport;
        float surfaceDepth = surfaceDataTexture.SampleLevel(
            surfaceDataSampler, surfaceUv, 0.0f).z;
        float depthFade = surfaceDepth <= 0.0f ? 1.0f
            : saturate((surfaceDepth - input.viewDepth)
                / max(softParticleOptions.y, 0.0001f));
        result.a *= depthFade;
    }

    float fogDensity = 0.0f;
    if (frameOptions.x > 0.5f)
    {
        if (enhancedPerPixel)
        {
            float distanceToCamera = length(
                cameraWorldPosition.xyz - input.worldPosition);
            float fogHeight = enhancedFogHeightFalloff.x;
            float falloff = enhancedFogHeightFalloff.y;
            float cameraDensity = exp(-max(0.0f,
                cameraWorldPosition.y - fogHeight) * falloff);
            float surfaceDensity = exp(-max(0.0f,
                input.worldPosition.y - fogHeight) * falloff);
            float averageDensity = (cameraDensity + surfaceDensity) * 0.5f;
            float enabledDensity = enhancedFogHeightFalloff.z > 0.5f
                ? enhancedFogColorDensity.a : 0.0f;
            fogDensity = saturate(1.0f - exp(-distanceToCamera
                * enabledDensity * averageDensity));
        }
        else
        {
            // Preserve the exact Original/Performance normalized-depth fog.
            float depth = saturate(input.position.z);
            float fogMin = fogRange.x;
            float fogMax = fogRange.y;
            fogDensity = depth >= fogMax ? 1.0f
                : (depth > fogMin ? (depth - fogMin)
                    / max(fogMax - fogMin, 0.0001f)
                    * (124.0f / 128.0f) : 0.0f);
            fogDensity = saturate(fogDensity);
        }
    }

    // Mapped emission is the primary selective bloom source. Existing frozen
    // BloomStrength metadata remains the fallback for materials without one.
    // Neither path scans scene brightness. Cutouts and fog visibility apply
    // without bleeding fog colour into emitted energy.
    if (renderOptions.z > 0.0f)
    {
        float3 bloomSource = hasMappedEmission
            ? materialEmission
            : (result.rgb + materialEmission) * saturate(renderOptions.z);
        return float4(bloomSource * result.a
            * (1.0f - fogDensity), 1.0f);
    }

    // Band the finished world surface before fog. HUD scene draws upload a
    // zero band count, matching legacy SetHudLayerUniforms.
    if (frameOptions.y > 0.0f)
    {
        result.rgb = cel_shade(result.rgb);
    }

    if (enhancedPerPixel && reflectionOptions.x > 0.5f)
    {
        result.rgb += EvaluateReflection(input.worldPosition, normal,
            SRGBToLinear(specular.rgb));
    }

    if (enhancedPerPixel)
    {
        // Keep emission after lit-surface processing. Future ambient occlusion
        // must modulate the lit surface before this additive term.
        result.rgb += materialEmission;
    }

    if (frameOptions.x > 0.5f)
    {
        float3 fogColor = enhancedPerPixel
            ? SRGBToLinear(enhancedFogColorDensity.rgb) : fog.rgb;
        result.rgb = lerp(result.rgb, fogColor, fogDensity);
    }

    if (visualLightOptions.y > 0.5f)
    {
        // Enhanced HUD scene draws occur after tone mapping. Their authored
        // display-referred colors are linearized before composition.
        result.rgb = SRGBToLinear(result.rgb);
    }

    if (renderOptions.x == 1.0f)
    {
        clip(result.a == 1.0f ? 1.0f : -1.0f);
    }
    else if (renderOptions.x == 2.0f)
    {
        clip(result.a < 1.0f ? 1.0f : -1.0f);
    }
    return result;
}
#endif
