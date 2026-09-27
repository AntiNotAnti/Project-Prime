namespace MphRead.Mods.Cosmetics
{
    internal static class CosmeticShader
    {
        // GLSL 1.20 / ES 3 compatible source shared verbatim by both backends.
        public const string Source = @"
uniform int cosmetic_skin;
uniform int cosmetic_effect;
uniform float cosmetic_time;
uniform vec3 cosmetic_primary;
uniform vec3 cosmetic_secondary;
uniform float cosmetic_intensity;
uniform float cosmetic_pulse;
uniform float cosmetic_scroll;
uniform float cosmetic_dissolve;
float cosmetic_noise(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
void apply_cosmetics(inout vec4 col) {
    if (cosmetic_skin == 0 && cosmetic_effect == 0 && cosmetic_dissolve <= 0.0) return;
    float lum = dot(col.rgb, vec3(0.2126, 0.7152, 0.0722));
    if (cosmetic_skin == 1) {
        // Preserve texture detail and a little palette identity; never black.
        col.rgb = mix(col.rgb, vec3(0.20, 0.23, 0.28) * (0.65 + lum), 0.88);
    } else if (cosmetic_skin == 2) {
        float etch = smoothstep(0.92, 0.99, sin(texcoord.x * 85.0) * sin(texcoord.y * 85.0));
        col.rgb = mix(col.rgb, vec3(0.62, 0.51, 0.30) * (0.35 + lum), 0.5);
        col.rgb += etch * vec3(0.03, 0.07, 0.065);
    }
    if (cosmetic_effect != 0) {
        float t = cosmetic_time * cosmetic_scroll;
        float rim = pow(1.0 - abs(normalize(surface_normal).z), 2.5);
        float pulse = 0.65 + 0.35 * sin(cosmetic_time * cosmetic_pulse);
        float wave = 0.5 + 0.5 * sin(texcoord.y * 30.0 - t * 2.0);
        float mask = rim;
        if (cosmetic_effect == 1) mask = 0.25 + 0.55 * pulse;
        if (cosmetic_effect == 3) mask = smoothstep(0.78, 0.95, wave) * 0.5 + rim;
        if (cosmetic_effect == 4) mask = cosmetic_noise(floor(texcoord * 24.0) + floor(t * 3.0)) * 0.3 + rim;
        if (cosmetic_effect == 5 || cosmetic_effect == 7) mask = smoothstep(0.86, 1.0, wave) * 0.45 + rim * 0.6;
        if (cosmetic_effect == 6) mask = pow(abs(sin(texcoord.x * 31.0 + sin(texcoord.y * 29.0 + t))), 28.0) + rim * 0.5;
        if (cosmetic_effect == 8) mask = wave * 0.35 + rim * 0.7;
        if (cosmetic_effect == 9) mask = (0.5 + 0.5 * sin(texcoord.x * 40.0 + t)) * rim;
        col.rgb += mix(cosmetic_primary, cosmetic_secondary, wave) * min(mask, 1.0) * cosmetic_intensity * pulse;
    }
    if (cosmetic_dissolve > 0.0) {
        float n = cosmetic_noise(floor(texcoord * 64.0));
        if (n < cosmetic_dissolve) discard;
        col.rgb += cosmetic_primary * (1.0 - smoothstep(cosmetic_dissolve, cosmetic_dissolve + 0.08, n)) * 0.3;
    }
}
";
    }
}
