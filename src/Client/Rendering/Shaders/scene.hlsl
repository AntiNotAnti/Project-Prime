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
    float4 frameOptions; // x lighting, y show-colors, z show-textures, w reserved
    float4 visualLightPositionRadius[8];
    float4 visualLightColorIntensity[8];
    float4 visualLightOptions; // x bounded light count, y/z/w reserved
};
#else
cbuffer FrameConstants : register(b0, space3)
{
    float4 fog;          // rgb colour, a enabled
    float4 frameOptions; // x fog enabled, y cel band count, z/w reserved
    float4 fogRange;     // x minimum depth, y maximum depth
};
#endif

#ifdef VERTEX_STAGE
cbuffer DrawConstants : register(b1, space1)
#else
cbuffer DrawConstants : register(b1, space3)
#endif
{
    // A submission with a matrix stack selects matrixStack[vertex.matrixIndex].
    // A submission without one puts its Transform in slot zero and sets
    // drawOptions.w to zero.  This preserves the legacy stack-or-transform
    // rule and prevents applying both transforms accidentally.
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
    float4 drawOptions;     // x texture, y colour override, z palette, w stack
    float4 materialOptions; // x alpha, y polygon mode, z texgen mode, w lighting
    float4 renderOptions;   // x alpha test: 0 off, 1 equal-one, 2 less-than-one
    float4 flatColor;       // rgb alpha-weighted texture average for cel surfaces
};

struct VertexInput
{
    float3 position : TEXCOORD0;
    float4 color : TEXCOORD1;
    float3 normal : TEXCOORD2;
    float2 texcoord : TEXCOORD3;
    uint matrixIndex : TEXCOORD4;
    uint vertexFlags : TEXCOORD5;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 normal : TEXCOORD0;
    float2 texcoord : TEXCOORD1;
    float4 color : TEXCOORD2;
};

#ifdef VERTEX_STAGE
float3 light_calc(float3 lightVector, float3 lightColor, float3 normal,
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
    output.position = mul(projection, mul(view, world));
    // Legacy OpenGL projection matrices produce NDC depth in [-1, 1]. SDL's
    // GPU backends use [0, 1], so remap clip depth without changing x/y/w.
    output.position.z = (output.position.z + output.position.w) * 0.5f;
    output.normal = normalize(mul((float3x3)modelMatrix, input.normal));

    uint explicitColor = input.vertexFlags & 1u;
    float3 vertexColor = frameOptions.y > 0.5f
        ? (explicitColor != 0u ? input.color.rgb : diffuse.rgb)
        : float3(1.0f, 1.0f, 1.0f);
    if (frameOptions.x > 0.5f && materialOptions.w > 0.5f)
    {
        float3 diffuseCurrent = diffuse.rgb;
        float3 ambientCurrent = ambient.rgb;
        // DIF_AMB with bit 15 clear is the legacy explicit-colour sentinel.
        if (explicitColor != 0u && input.color.a == 0.0f)
        {
            diffuseCurrent = vertexColor;
            ambientCurrent = float3(0.0f, 0.0f, 0.0f);
        }
        float3 color1 = light_calc(light1Vector.xyz, light1Color.rgb,
            output.normal, diffuseCurrent, ambientCurrent, specular.rgb);
        float3 color2 = light_calc(light2Vector.xyz, light2Color.rgb,
            output.normal, diffuseCurrent, ambientCurrent, specular.rgb);
        output.color = float4(min(color1 + color2 + emission.rgb,
            float3(1.0f, 1.0f, 1.0f)), 1.0f);
    }
    else
    {
        output.color = float4(vertexColor, 1.0f);
    }

    // Optional render-only point lights are applied after the two legacy DS
    // lights. Their count is zero unless the captured quality snapshot enables
    // DynamicVisualLights, so Original takes no per-light work.
    uint visualLightCount = min((uint)visualLightOptions.x, 8u);
    float3 visualDiffuse = float3(0.0f, 0.0f, 0.0f);
    for (uint lightIndex = 0u; lightIndex < visualLightCount; lightIndex++)
    {
        float3 delta = visualLightPositionRadius[lightIndex].xyz - world.xyz;
        float distanceToLight = length(delta);
        float radius = max(visualLightPositionRadius[lightIndex].w, 0.0001f);
        float attenuation = saturate(1.0f - distanceToLight / radius);
        attenuation *= attenuation;
        float diffuseFactor = distanceToLight > 0.0001f
            ? max(dot(output.normal, delta / distanceToLight), 0.0f) : 1.0f;
        visualDiffuse += diffuse.rgb * visualLightColorIntensity[lightIndex].rgb
            * visualLightColorIntensity[lightIndex].w * diffuseFactor * attenuation;
    }
    output.color.rgb = saturate(output.color.rgb + visualDiffuse);

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
    return saturate(lerp(float3(grey, grey, grey), banded, 1.35f));
}

float4 main_ps(VertexOutput input) : SV_Target0
{
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

    float4 result;
    uint polygonMode = (uint)materialOptions.y;
    if (!useTexture)
    {
        result = drawOptions.y > 0.5f
            ? overrideColor
            : float4(input.color.rgb, materialOptions.x * input.color.a);
    }
    else if (polygonMode == 1u) // decal
    {
        result = float4(lerp(input.color.rgb, textureColor.rgb, textureColor.a),
            materialOptions.x * input.color.a);
    }
    else if (polygonMode == 2u) // toon/highlight mode
    {
        result = float4(textureColor.rgb * input.color.r + toon_color(input.color.r),
            materialOptions.x * textureColor.a * input.color.a);
    }
    else // modulate
    {
        result = float4(input.color.rgb * textureColor.rgb,
            materialOptions.x * textureColor.a * input.color.a);
    }
    if (!useTexture && polygonMode == 2u)
    {
        result.rgb = toon_color(input.color.r);
    }
    if (drawOptions.y > 0.5f && useTexture)
    {
        result.rgb = overrideColor.rgb;
        result.a *= overrideColor.a;
    }

    float fogDensity = 0.0f;
    if (frameOptions.x > 0.5f)
    {
        // Fragment SV_Position.z is already the normalized device depth.
        float depth = saturate(input.position.z);
        float fogMin = fogRange.x;
        float fogMax = fogRange.y;
        fogDensity = depth >= fogMax ? 1.0f
            : (depth > fogMin ? (depth - fogMin) / max(fogMax - fogMin, 0.0001f)
                * (124.0f / 128.0f) : 0.0f);
        fogDensity = saturate(fogDensity);
    }

    // A bloom emission pass reuses this exact material/texture evaluation and
    // is selected only by explicit frozen BloomStrength metadata. It never
    // scans scene brightness or changes the six legacy passes. Fog obscures
    // energy using the same visibility, without bleeding fog colour into it.
    if (renderOptions.z > 0.0f)
    {
        return float4(result.rgb * saturate(renderOptions.z) * result.a
            * (1.0f - fogDensity), 1.0f);
    }

    // Band the finished world surface before fog. HUD scene draws upload a
    // zero band count, matching legacy SetHudLayerUniforms.
    if (frameOptions.y > 0.0f)
    {
        result.rgb = cel_shade(result.rgb);
    }

    if (frameOptions.x > 0.5f)
    {
        result.rgb = lerp(result.rgb, fog.rgb, fogDensity);
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
