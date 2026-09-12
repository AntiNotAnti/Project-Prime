// Enhanced force-field distortion. The vertex stage preserves the scene mesh
// transform ABI; the fragment stage writes a bounded screen-space UV offset.

#ifdef VERTEX_STAGE
cbuffer FrameConstants : register(b0, space1)
{
    row_major float4x4 view;
    row_major float4x4 projection;
    float4 frameOptions;
};

cbuffer DrawConstants : register(b1, space1)
{
    row_major float4x4 transform;
    row_major float4x4 billboard;
    row_major float4x4 matrixStack[31];
    row_major float4x4 texcoordMatrix;
    float4 distortionDrawOptions; // x matrix stack enabled
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

VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    uint matrixIndex = min(input.matrixIndex, 30u);
    float4x4 stackMatrix = distortionDrawOptions.x > 0.5f
        ? matrixStack[matrixIndex] : transform;
    float4 world = mul(mul(stackMatrix, billboard),
        float4(input.position, 1.0f));
    output.position = mul(projection, mul(view, world));
    output.position.z = (output.position.z + output.position.w) * 0.5f;
    output.texcoord = mul(texcoordMatrix,
        float4(input.texcoord, 0.0f, 1.0f)).xy;
    return output;
}
#else
cbuffer DistortionConstants : register(b0, space3)
{
    float4 distortionOptions; // x strength, y falloff, z phase
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float2 main_ps(VertexOutput input) : SV_Target0
{
    float2 coordinate = input.texcoord * 9.0f
        + distortionOptions.z * float2(13.0f, -17.0f);
    float2 wave = float2(sin(coordinate.y + cos(coordinate.x * 0.7f)),
        cos(coordinate.x - sin(coordinate.y * 0.8f)));
    float edge = pow(saturate(length(input.texcoord * 2.0f - 1.0f)),
        max(distortionOptions.y, 1.0f));
    return clamp(wave * distortionOptions.x * (0.35f + 0.65f * edge),
        -distortionOptions.x, distortionOptions.x);
}
#endif
