using System;

namespace MphRead.Mods.Render;

// Optional program source: canonical Original shaders remain independently usable
// if this shader cannot compile/link on a compatibility driver.
internal static class ShadowReceiverShader
{
    internal static string Build(string source, bool gles)
    {
        const string early = "if (!advanced_materials) return;";
        const string emissive = "    if (use_emissive_map)";
        if (!source.Contains(early, StringComparison.Ordinal) || !source.Contains(emissive, StringComparison.Ordinal))
            throw new InvalidOperationException("World material shadow insertion boundary changed.");
        string body = Body.Replace("SHADOW_SAMPLE", gles ? "texture" : "texture2D");
        if (gles) body = body.Replace("uniform sampler2D world_shadow_tex;", "uniform highp sampler2D world_shadow_tex;");
        source = source.Replace("vec3 mapped_normal()", body
            + "\nvec3 mapped_normal()", StringComparison.Ordinal);
        return source.Replace(early, "if (!advanced_materials) { col.rgb *= world_shadow_visibility(); return; }", StringComparison.Ordinal)
            .Replace(emissive, "    col.rgb *= world_shadow_visibility();\n" + emissive, StringComparison.Ordinal);
    }

    private const string Body = @"
uniform bool receive_world_shadow;
uniform sampler2D world_shadow_tex;
uniform mat4 world_shadow_matrix;
uniform vec2 world_shadow_texel;
uniform vec3 world_shadow_light;

float world_shadow_visibility() {
    if (!receive_world_shadow) return 1.0;
    vec4 clip = world_shadow_matrix * vec4(surface_position, 1.0);
    if (clip.w <= 0.0) return 1.0;
    vec3 projected = clip.xyz / clip.w * 0.5 + 0.5;
    float footprint = max(abs(dFdx(projected.z)), abs(dFdy(projected.z)));
    vec2 edge = min(projected.xy, vec2(1.0) - projected.xy);
    float border = min(edge.x, edge.y);
    if (border <= 0.0 || projected.z <= 0.0 || projected.z >= 1.0) return 1.0;
    // World-space geometry normals avoid screen-depth discontinuities. Bias is
    // bounded in light-depth space, with a receiver-footprint contribution.
    vec3 n = surface_normal;
    float nlength = dot(n,n);
    n = nlength > 0.000001 ? n * inversesqrt(nlength) : vec3(0.0,0.0,1.0);
    float alignment = abs(dot(n, world_shadow_light));
    float bias = min(0.004, mix(0.0032, 0.0007, alignment) + min(footprint, 0.0008));
    float lit = 0.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            float stored = SHADOW_SAMPLE(world_shadow_tex,
                clamp(projected.xy + vec2(float(x),float(y)) * world_shadow_texel,
                    vec2(0.0), vec2(1.0))).r;
            lit += projected.z - bias <= stored ? 1.0 : 0.0;
        }
    }
    // Fade through the final four texels instead of a hard light-volume edge.
    float fade = smoothstep(0.0, max(world_shadow_texel.x,world_shadow_texel.y) * 4.0, border);
    fade *= smoothstep(0.0,0.02,projected.z) * smoothstep(0.0,0.02,1.0-projected.z);
    return mix(1.0, mix(0.58,1.0,lit / 9.0), fade);
}
";
}
