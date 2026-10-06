namespace MphRead.Mods.Render
{
    /// <summary>
    /// Opt-in character ORM lighting. The two native directional lights use an
    /// energy-conserving GGX / Smith / Schlick BRDF in linear color. Ambient is
    /// the native room/material approximation; this does not provide IBL.
    /// Shared verbatim by desktop GLSL and ES and translated to modern WGSL.
    /// </summary>
    internal static class PhysicalMaterialShader
    {
        internal const string Source = @"
uniform vec3 diffuse;
uniform vec3 ambient;
uniform vec3 emission;

vec3 physical_to_linear(vec3 c) {
    vec3 low = c / 12.92;
    vec3 high = pow(max((c + 0.055) / 1.055, vec3(0.0)), vec3(2.4));
    return mix(low, high, step(vec3(0.04045), c));
}
vec3 physical_to_srgb(vec3 c) {
    c = max(c, vec3(0.0));
    vec3 low = c * 12.92;
    vec3 high = 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055;
    return mix(low, high, step(vec3(0.0031308), c));
}
vec3 physical_direct(vec3 albedo, vec3 n, vec3 v, vec3 l, vec3 radiance,
    float metallic, float roughness) {
    float nl = max(dot(n, l), 0.0), nv = max(dot(n, v), 0.0);
    if (nl <= 0.0 || nv <= 0.0) return vec3(0.0);
    vec3 halfSum = v + l;
    vec3 h = halfSum * inversesqrt(max(dot(halfSum, halfSum), 0.000001));
    float nh = clamp(dot(n, h), 0.0, 1.0), vh = clamp(dot(v, h), 0.0, 1.0);
    float a = roughness * roughness, a2 = a * a;
    float denominator = nh * nh * (a2 - 1.0) + 1.0;
    float distribution = a2 / max(3.14159265 * denominator * denominator, 0.000001);
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float geometry = (nv / max(nv * (1.0 - k) + k, 0.000001))
        * (nl / max(nl * (1.0 - k) + k, 0.000001));
    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    vec3 fresnel = f0 + (vec3(1.0) - f0) * pow(1.0 - vh, 5.0);
    vec3 spec = distribution * geometry * fresnel / max(4.0 * nv * nl, 0.0001);
    vec3 kd = (vec3(1.0) - fresnel) * (1.0 - metallic);
    return (kd * albedo / 3.14159265 + spec) * radiance * nl;
}
vec3 physical_material(vec3 base, vec3 n, vec4 orm) {
    float ao = clamp(orm.r, 0.0, 1.0), roughness = clamp(orm.g, 0.08, 1.0);
    float metallic = clamp(orm.b, 0.0, 1.0);
    vec3 eye = -(view_mtx * vec4(surface_position, 1.0)).xyz;
    eye *= inversesqrt(max(dot(eye, eye), 0.000001));
    vec3 v = transpose(mat3(view_mtx)) * eye;
    vec3 albedo = physical_to_linear(clamp(base * diffuse, 0.0, 1.0));
    vec3 ambientRadiance = max(ambient * (light1col + light2col), vec3(0.0));
    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    // AO attenuates the native ambient approximation, never direct light.
    vec3 result = ((1.0 - metallic) * albedo + f0 * 0.35) * ambientRadiance * ao;
    vec3 l1 = -light1vec * inversesqrt(max(dot(light1vec, light1vec), 0.000001));
    vec3 l2 = -light2vec * inversesqrt(max(dot(light2vec, light2vec), 0.000001));
    // Native light colors were authored for Lambert lighting without / pi.
    // Calibrate them into BRDF radiance once, retaining the room's intensity.
    result += physical_direct(albedo, n, v, l1, max(light1col, vec3(0.0)) * 3.14159265, metallic, roughness);
    result += physical_direct(albedo, n, v, l2, max(light2col, vec3(0.0)) * 3.14159265, metallic, roughness);
    result += physical_to_linear(max(emission, vec3(0.0)));
    return physical_to_srgb(result);
}
";
    }
}
