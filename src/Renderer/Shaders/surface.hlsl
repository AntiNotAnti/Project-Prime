// Enhanced single-sample surface-data pass. Runtime pairs this fragment stage
// with scene.vert so opaque geometry uses the exact world-pass transforms.

#ifdef VERTEX_STAGE
cbuffer FrameConstants : register(b0, space1)
{
    row_major float4x4 view;
    row_major float4x4 projection;
    float4 frameOptions;
};

cbuffer DrawConstants : register(b1, space1)
#else
cbuffer DrawConstants : register(b0, space3)
#endif
{
    row_major float4x4 transform;
    row_major float4x4 billboard;
    row_major float4x4 matrixStack[31];
    row_major float4x4 texcoordMatrix;
    float4 diffuse;
    float4 ambient;
    float4 specular;
    float4 emission;
    float4 overrideColor;
    float4 paletteOverride;
    float4 light1Vector;
    float4 light1Color;
    float4 light2Vector;
    float4 light2Color;
    float4 drawOptions;
    float4 materialOptions;
    float4 renderOptions;
    float4 flatColor;
    float4 enhancedEmission;
    float4 enhancedOptions;
    float4 reflectionOptions;
    float4 softParticleOptions;
};

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

float3 SafeNormalize(float3 value, float3 fallback)
{
    float lengthSquared = dot(value, value);
    return lengthSquared > 0.00000001f ? value * rsqrt(lengthSquared) : fallback;
}

float3 Perpendicular(float3 normal)
{
    float3 axis = abs(normal.x) <= abs(normal.y) && abs(normal.x) <= abs(normal.z)
        ? float3(1.0f, 0.0f, 0.0f)
        : abs(normal.y) <= abs(normal.z)
            ? float3(0.0f, 1.0f, 0.0f) : float3(0.0f, 0.0f, 1.0f);
    return SafeNormalize(cross(axis, normal), float3(1.0f, 0.0f, 0.0f));
}

#ifdef VERTEX_STAGE
float3 TransformNormal(float3 normal, float4x4 modelMatrix)
{
    float3x3 model = (float3x3)modelMatrix;
    float3x3 normalMatrix = float3x3(
        cross(model[1], model[2]), cross(model[2], model[0]),
        cross(model[0], model[1]));
    float3 transformed = mul(normalMatrix, normal);
    float determinant = dot(model[0], cross(model[1], model[2]));
    transformed *= determinant < 0.0f ? -1.0f : 1.0f;
    return SafeNormalize(transformed,
        SafeNormalize(mul(model, normal), float3(0.0f, 1.0f, 0.0f)));
}

VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    uint matrixIndex = min(input.matrixIndex, 30u);
    float4x4 stackMatrix = drawOptions.w > 0.5f
        ? matrixStack[matrixIndex] : transform;
    float4x4 modelMatrix = mul(stackMatrix, billboard);
    float4 world = mul(modelMatrix, float4(input.position, 1.0f));
    float4 viewPosition = mul(view, world);
    output.worldPosition = world.xyz;
    output.position = mul(projection, viewPosition);
    output.position.z = (output.position.z + output.position.w) * 0.5f;
    output.viewDepth = max(-viewPosition.z, 0.0f);
    output.worldNormal = TransformNormal(input.normal, modelMatrix);
    float3 tangent = mul((float3x3)modelMatrix, input.tangent.xyz);
    tangent -= output.worldNormal * dot(output.worldNormal, tangent);
    output.worldTangent = float4(SafeNormalize(tangent,
        Perpendicular(output.worldNormal)), input.tangent.w < 0.0f ? -1.0f : 1.0f);
    output.texcoord = drawOptions.x > 0.5f
        ? mul(texcoordMatrix, float4(input.texcoord, 0.0f, 1.0f)).xy
        : float2(0.0f, 0.0f);
    output.vertexColor = input.color;
    output.lightingDiffuse = diffuse.rgb;
    output.lightingAmbient = ambient.rgb;
    return output;
}
#else
Texture2D albedoTexture : register(t0, space2);
SamplerState albedoSampler : register(s0, space2);
Texture2D normalTexture : register(t1, space2);
SamplerState normalSampler : register(s1, space2);

float3 ResolveNormalMap(VertexOutput input, float3 geometryNormal)
{
    float3 tangent = input.worldTangent.xyz
        - geometryNormal * dot(geometryNormal, input.worldTangent.xyz);
    tangent = SafeNormalize(tangent, Perpendicular(geometryNormal));
    float handedness = input.worldTangent.w < 0.0f ? -1.0f : 1.0f;
    float3 bitangent = SafeNormalize(cross(geometryNormal, tangent) * handedness,
        cross(geometryNormal, tangent));
    float a = texcoordMatrix[0][0];
    float b = texcoordMatrix[0][1];
    float c = texcoordMatrix[1][0];
    float d = texcoordMatrix[1][1];
    float determinant = a * d - b * c;
    if (abs(determinant) > 0.00000001f)
    {
        float reciprocal = 1.0f / determinant;
        float3 sourceTangent = tangent;
        tangent = SafeNormalize((d * sourceTangent - c * bitangent) * reciprocal,
            sourceTangent);
        bitangent = SafeNormalize((-b * sourceTangent + a * bitangent) * reciprocal,
            bitangent);
    }
    float3 mapped = normalTexture.Sample(normalSampler, input.texcoord).xyz
        * 2.0f - 1.0f;
    mapped = SafeNormalize(mapped, float3(0.0f, 0.0f, 1.0f));
    return SafeNormalize(tangent * mapped.x + bitangent * mapped.y
        + geometryNormal * mapped.z, geometryNormal);
}

float ResolveAlpha(VertexOutput input)
{
    bool useTexture = drawOptions.x > 0.5f;
    float textureAlpha = useTexture
        ? albedoTexture.Sample(albedoSampler, input.texcoord).a : 1.0f;
    uint polygonMode = (uint)materialOptions.y;
    float alpha = !useTexture || polygonMode == 1u
        ? materialOptions.x : materialOptions.x * textureAlpha;
    if (!useTexture && drawOptions.y > 0.5f) alpha = overrideColor.a;
    if (useTexture && drawOptions.y > 0.5f) alpha *= overrideColor.a;
    return alpha;
}

float2 EncodeOctNormal(float3 normal)
{
    normal = SafeNormalize(normal, float3(0.0f, 1.0f, 0.0f));
    normal /= abs(normal.x) + abs(normal.y) + abs(normal.z);
    float2 encoded = normal.xy;
    if (normal.z < 0.0f)
    {
        encoded = (1.0f - abs(encoded.yx))
            * float2(encoded.x >= 0.0f ? 1.0f : -1.0f,
                encoded.y >= 0.0f ? 1.0f : -1.0f);
    }
    return encoded * 0.5f + 0.5f;
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    // Opaque coverage exactly matches the main opaque pass.
    clip(ResolveAlpha(input) == 1.0f ? 1.0f : -1.0f);
    float3 normal = SafeNormalize(input.worldNormal, float3(0.0f, 1.0f, 0.0f));
    if (renderOptions.w > 0.5f) normal = ResolveNormalMap(input, normal);
    return float4(EncodeOctNormal(normal), max(input.viewDepth, 0.0f),
        saturate(specular.a));
}
#endif
