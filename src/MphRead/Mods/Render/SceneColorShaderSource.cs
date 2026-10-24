using System;

namespace MphRead.Mods.Render
{
    // IEC 61966-2-1 transfer is RGB-only. Alpha and packed joint weights never
    // enter this helper. A false uniform preserves the native arithmetic.
    internal static class SceneColorShaderSource
    {
        internal const string Input = @"
uniform bool scene_linear_color;
uniform bool authored_linear_tint;
vec3 scene_srgb_decode(vec3 value) {
    vec3 c = max(value, vec3(0.0));
    return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}
vec3 scene_srgb_encode(vec3 value) {
    // Match the scene output's deliberate LDR encode, including highlight clipping.
    vec3 c = clamp(value, 0.0, 1.0);
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - vec3(0.055),
        step(vec3(0.0031308), c));
}
vec3 scene_native_fog_blend(vec3 linearSurface, vec3 encodedFog, float density) {
    if (density <= 0.0) return linearSurface;
    vec3 encodedSurface = scene_srgb_encode(linearSurface);
    vec3 encodedFogged = encodedSurface * (1.0 - density) + encodedFog * density;
    return scene_srgb_decode(encodedFogged);
}
vec3 scene_input_color(vec3 value) {
    return scene_linear_color ? scene_srgb_decode(value) : value;
}
vec3 scene_material_color(vec3 value) {
    return scene_linear_color && authored_linear_tint ? value : scene_input_color(value);
}
";
        internal const string Normal = @"
vec3 scene_normalize(vec3 n, vec3 fallback) {
    float length2 = dot(n,n);
    return length2 > 0.000000000001 && length2 < 1.0e30 ? n * inversesqrt(length2) : fallback;
}
vec3 scene_transform_normal(mat3 transform, vec3 value) {
    // Cofactor inverse transpose works on GLSL 1.20, including reflected and
    // blended palettes. The sign of determinant must survive normalization.
    vec3 c0 = cross(transform[1], transform[2]);
    vec3 c1 = cross(transform[2], transform[0]);
    vec3 c2 = cross(transform[0], transform[1]);
    float determinant = dot(transform[0], c0);
    vec3 fallback = scene_normalize(transform * value, vec3(0.0,0.0,1.0));
    if (!(abs(determinant) > 0.000000000001 && abs(determinant) < 1.0e30)) return fallback;
    vec3 n = mat3(c0,c1,c2) * value * (determinant < 0.0 ? -1.0 : 1.0);
    return scene_normalize(n, fallback);
}
";
        // Procedural palettes are authored colors too. Scalars used for masks,
        // frequencies and material coefficients keep their existing meaning.
        internal static readonly string Cosmetics = BuildCosmetics();
        private static string BuildCosmetics()
        {
            string source = MphRead.Mods.Cosmetics.CosmeticShader.Source;
            string[] palettes = { "0.20, 0.23, 0.28", "0.76, 0.58, 0.28", "0.08, 0.32, 0.28",
                "0.10, 0.14, 0.19", "0.88, 0.90, 0.85", "0.13, 0.18, 0.22", "0.08, 0.8, 0.65",
                "0.85, 0.40, 0.07", "0.08, 0.07, 0.09", "0.10, 0.12, 0.32", "0.48, 0.16, 0.56",
                "0.12, 0.16, 0.20" };
            foreach (string palette in palettes)
                source = source.Replace("vec3(" + palette + ")", "scene_input_color(vec3(" + palette + "))", StringComparison.Ordinal);
            return source.Replace("mix(cosmetic_primary, cosmetic_secondary, wave)",
                    "mix(scene_input_color(cosmetic_primary), scene_input_color(cosmetic_secondary), wave)", StringComparison.Ordinal)
                .Replace("col.rgb += cosmetic_primary *", "col.rgb += scene_input_color(cosmetic_primary) *", StringComparison.Ordinal);
        }
    }

}
