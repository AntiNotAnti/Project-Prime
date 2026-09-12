// R7 HUD disruption and whiteout source.  This is the HLSL form of the
// legacy ShiftFragmentShader; shift and whiteout tables are frame constants,
// copied into the sealed RenderDisruptionState before encoding.

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
cbuffer DisruptionConstants : register(b0, space3)
{
    float4 disruptionOptions; // x shift factor, y shift index, z lerp, w whiteout factor
    float4 shiftTable[16];    // 64 values, packed as four floats per row
    float4 whiteoutTable[48]; // 192 values, packed as four floats per row
};

Texture2D sourceTexture : register(t0, space2);
SamplerState sourceSampler : register(s0, space2);

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texcoord : TEXCOORD0;
};

float ShiftValue(uint index)
{
    uint row = index >> 2u;
    uint component = index & 3u;
    return shiftTable[row][component];
}

float WhiteoutValue(uint index)
{
    uint row = index >> 2u;
    uint component = index & 3u;
    return whiteoutTable[row][component];
}

float4 main_ps(VertexOutput input) : SV_Target0
{
    // The rasterizer never produces the exact lower edge as a fragment centre,
    // but clamping retains the GLSL table boundary without undefined indexing
    // on APIs whose pixel-centre convention differs.
    uint band = min((uint)max((1.0f - input.texcoord.y) * 192.0f, 0.0f), 191u);
    int indexSigned = (int)band + (int)disruptionOptions.y
        + (int)(band & 1u) * 32;
    int wrapped = indexSigned % 64;
    if (wrapped < 0) wrapped += 64;
    uint index = (uint)wrapped;
    float value1 = ShiftValue(index);
    float value2 = ShiftValue((index + 1u) & 63u);
    float value = lerp(value1, value2, disruptionOptions.z) * disruptionOptions.x;
    float2 shifted = float2(input.texcoord.x + value, input.texcoord.y);

    float4 output;
    if (shifted.x < 0.0f || shifted.x > 1.0f)
    {
        output = float4(0, 0, 0, 1);
    }
    else
    {
        output = sourceTexture.Sample(sourceSampler, shifted);
    }

    if (disruptionOptions.w != 0.0f)
    {
        float factor = WhiteoutValue(band);
        if (disruptionOptions.w < 0.0f)
        {
            // A negative whiteout factor means the table is already the
            // greyscale result, exactly as the legacy shader defines it.
            output = float4(factor, factor, factor, 1);
        }
        else
        {
            factor *= disruptionOptions.w;
            if (factor >= 0.0f)
            {
                output.rgb += (1.0f - output.rgb) * factor;
            }
            else
            {
                factor = -factor;
                output.rgb -= output.rgb * factor;
            }
            output.a = 1.0f;
        }
    }
    return output;
}
#endif
