// Specialized world depth/stencil shader. It preserves the scene transform,
// billboard, matrix-palette, texgen, and alpha predicates while omitting all
// lighting, normal mapping, reflection, fog, and color work.

#ifdef VERTEX_STAGE
cbuffer FrameConstants : register(b0, space1)
{
    row_major float4x4 view;
    row_major float4x4 projection;
    float4 frameOptions; // x lighting
};

cbuffer VertexDrawConstants : register(b1, space1)
{
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
    float4 drawOptions;
    float4 materialOptions;
};

cbuffer MatrixPaletteConstants : register(b2, space1)
{
    row_major float4x4 matrixStack[31];
};
#else
cbuffer AlphaConstants : register(b0, space3)
{
    float4 alphaOptions; // x textured, y alpha-test mode, z material alpha
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
    float2 texcoord : TEXCOORD0;
};

#ifdef VERTEX_STAGE
VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    uint matrixIndex = min(input.matrixIndex, 30u);
    float4x4 stackMatrix = drawOptions.w > 0.5f
        ? matrixStack[matrixIndex] : transform;
    float4x4 modelMatrix = mul(stackMatrix, billboard);
    float4 world = mul(modelMatrix, float4(input.position, 1.0f));
    output.position = mul(projection, mul(view, world));
    output.position.z = (output.position.z + output.position.w) * 0.5f;

    uint texgen = (uint)materialOptions.z;
    if (drawOptions.x <= 0.5f)
    {
        output.texcoord = float2(0.0f, 0.0f);
    }
    else if (texgen == 2u)
    {
        float4x4 lightView = (frameOptions.x > 0.5f
            && materialOptions.w > 0.5f) ? view
            : float4x4(1.0f, 0.0f, 0.0f, 0.0f,
                       0.0f, 1.0f, 0.0f, 0.0f,
                       0.0f, 0.0f, 1.0f, 0.0f,
                       0.0f, 0.0f, 0.0f, 1.0f);
        float4x4 texgenMatrix = mul(texcoordMatrix,
            mul(lightView, stackMatrix));
        output.texcoord = float2(
            dot(input.normal, float3(texgenMatrix[0][0], texgenMatrix[1][0],
                texgenMatrix[2][0])) + input.texcoord.x,
            dot(input.normal, float3(texgenMatrix[0][1], texgenMatrix[1][1],
                texgenMatrix[2][1])) + input.texcoord.y);
    }
    else if (texgen == 3u)
    {
        output.texcoord = float2(
            dot(input.position, float3(texcoordMatrix[0][0],
                texcoordMatrix[1][0], texcoordMatrix[2][0])) + input.texcoord.x,
            dot(input.position, float3(texcoordMatrix[0][1],
                texcoordMatrix[1][1], texcoordMatrix[2][1])) + input.texcoord.y);
    }
    else
    {
        output.texcoord = mul(texcoordMatrix,
            float4(input.texcoord, 0.0f, 1.0f)).xy;
    }
    return output;
}
#else
Texture2D albedoTexture : register(t0, space2);
SamplerState albedoSampler : register(s0, space2);

float4 main_ps(VertexOutput input) : SV_Target0
{
    float alpha = alphaOptions.x > 0.5f
        ? albedoTexture.Sample(albedoSampler, input.texcoord).a
            * alphaOptions.z
        : alphaOptions.z;
    if (alphaOptions.y < 1.5f)
    {
        if (alpha != 1.0f) discard;
    }
    else if (alpha >= 1.0f)
    {
        discard;
    }
    return float4(0.0f, 0.0f, 0.0f, alpha);
}
#endif
