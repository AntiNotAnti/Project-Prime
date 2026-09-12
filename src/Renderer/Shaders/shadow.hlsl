// Enhanced directional shadow caster. This pass replays opaque triangles only
// and matches scene alpha coverage without introducing another material ABI.

#ifdef VERTEX_STAGE
cbuffer ShadowFrameConstants : register(b0, space1)
{
    row_major float4x4 shadowViewProjection;
    row_major float4x4 view;
    float4 frameOptions; // x lighting
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
    float2 texcoord : TEXCOORD0;
};

#ifdef VERTEX_STAGE
VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    uint matrixIndex = min(input.matrixIndex, 30u);
    float4x4 stackMatrix = drawOptions.w > 0.5f
        ? matrixStack[matrixIndex] : transform;
    float4 world = mul(mul(stackMatrix, billboard), float4(input.position, 1.0f));
    output.position = mul(shadowViewProjection, world);
    output.position.z = (output.position.z + output.position.w) * 0.5f;
    uint texgen = (uint)materialOptions.z;
    if (drawOptions.x > 0.5f)
    {
        if (texgen == 2u)
        {
            // Keep alpha-tested shadow coverage identical to the scene's
            // normal-generated coordinate path.
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
            // Keep alpha-tested shadow coverage identical to the scene's
            // position-generated coordinate path.
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
#else
Texture2D albedoTexture : register(t0, space2);
SamplerState albedoSampler : register(s0, space2);

float main_ps(VertexOutput input) : SV_Depth
{
    bool useTexture = drawOptions.x > 0.5f;
    float textureAlpha = useTexture
        ? albedoTexture.Sample(albedoSampler, input.texcoord).a : 1.0f;
    uint polygonMode = (uint)materialOptions.y;
    float alpha = !useTexture || polygonMode == 1u
        ? materialOptions.x : materialOptions.x * textureAlpha;
    if (!useTexture && drawOptions.y > 0.5f) alpha = overrideColor.a;
    if (useTexture && drawOptions.y > 0.5f) alpha *= overrideColor.a;
    clip(alpha == 1.0f ? 1.0f : -1.0f);
    return input.position.z;
}
#endif
