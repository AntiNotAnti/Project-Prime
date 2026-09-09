// R5/R6 proof shader. HLSL is the canonical source; build tools generate
// backend artifacts offline. The shader deliberately has no runtime compiler
// or scene-specific uniforms so it can prove the SDL GPU device path with a
// single indexed mesh while R7 scene parity remains out of scope.
struct VertexInput
{
    // SDL GPU's D3D12 path uses TEXCOORD as its default semantic name for
    // vertex attributes. Keep the HLSL semantics aligned with pipeline
    // locations so the DXIL artifact binds identically to SPIR-V/MSL.
    float3 position : TEXCOORD0;
    float4 color : TEXCOORD1;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float4 color : TEXCOORD0;
};

VertexOutput main_vs(VertexInput input)
{
    VertexOutput output;
    output.position = float4(input.position, 1.0f);
    output.color = input.color;
    return output;
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    return input.color;
}
