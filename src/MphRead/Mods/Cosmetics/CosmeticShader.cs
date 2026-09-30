namespace MphRead.Mods.Cosmetics
{
    internal static class CosmeticShader
    {
        // GLSL 1.20 / ES 3 compatible source shared verbatim by both backends.
        public const string Source = @"
uniform mat4 view_mtx;
uniform int cosmetic_skin;
uniform int cosmetic_preserve_palette;
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
// Continuous value noise avoids whole tiles popping between unrelated values.
float cosmetic_soft_noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(cosmetic_noise(cell), cosmetic_noise(cell + vec2(1.0, 0.0)), f.x),
        mix(cosmetic_noise(cell + vec2(0.0, 1.0)), cosmetic_noise(cell + vec2(1.0)), f.x), f.y);
}
vec2 cosmetic_finish() {
    // Metalness / roughness: shared with the deferred material pass.
    if (cosmetic_skin == 1) return vec2(0.78, 0.30);
    if (cosmetic_skin == 2) return vec2(0.85, 0.38);
    if (cosmetic_skin == 3) return vec2(0.05, 0.24);
    if (cosmetic_skin == 4) return vec2(0.55, 0.42);
    if (cosmetic_skin == 5) return vec2(0.12, 0.68);
    return vec2(0.35, 0.28);
}
float cosmetic_circuit() {
    vec2 grid = fract(texcoord * 18.0);
    float trace = 1.0 - smoothstep(0.035, 0.09, min(grid.x, grid.y));
    float node = 1.0 - smoothstep(0.08, 0.16, length(grid - vec2(0.15)));
    float travel = pow(0.5 + 0.5 * sin((texcoord.x + texcoord.y) * 32.0 - cosmetic_time * 1.8), 8.0);
    return max(trace * (0.3 + travel * 0.7), node * 0.75);
}
void apply_cosmetics(inout vec4 col) {
    if (cosmetic_skin == 0 && cosmetic_effect == 0 && cosmetic_dissolve <= 0.0) return;
    vec3 nativeColor = col.rgb;
    float lum = dot(col.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 n = surface_normal * inversesqrt(max(dot(surface_normal, surface_normal), 0.0001));
    vec3 viewNormal = mat3(view_mtx) * n;
    vec3 toEye = -(view_mtx * vec4(surface_position, 1.0)).xyz;
    toEye *= inversesqrt(max(dot(toEye, toEye), 0.0001));
    float rim = pow(clamp(1.0 - abs(dot(viewNormal, toEye)), 0.0, 1.0), 2.2);
    if (cosmetic_skin == 1) {
        // Preserve texture detail and a little palette identity; never black.
        col.rgb = mix(col.rgb, vec3(0.20, 0.23, 0.28) * (0.65 + lum), 0.88);
    } else if (cosmetic_skin == 2) {
        float etch = smoothstep(0.92, 0.99, sin(texcoord.x * 85.0) * sin(texcoord.y * 85.0));
        col.rgb = mix(col.rgb, vec3(0.76, 0.58, 0.28) * (0.45 + lum), 0.82);
        col.rgb += etch * vec3(0.08, 0.32, 0.28);
    }
    if (cosmetic_skin == 3) {
        vec2 panel = fract(texcoord * 12.0);
        float seam = smoothstep(0.025, 0.075, panel.x) * smoothstep(0.025, 0.075, panel.y);
        col.rgb = mix(vec3(0.10, 0.14, 0.19), vec3(0.88, 0.90, 0.85), seam) * (0.5 + lum * 0.6);
    } else if (cosmetic_skin == 4) {
        col.rgb = vec3(0.13, 0.18, 0.22) * (0.6 + lum) + vec3(0.08, 0.8, 0.65) * cosmetic_circuit();
    } else if (cosmetic_skin == 5) {
        float stripe = smoothstep(0.35, 0.45, sin(texcoord.x * 65.0 + texcoord.y * 38.0 + sin(texcoord.y * 25.0) * 2.0));
        col.rgb = mix(vec3(0.85, 0.40, 0.07), vec3(0.08, 0.07, 0.09), stripe) * (0.55 + lum);
    } else if (cosmetic_skin == 6) {
        float cloud = cosmetic_soft_noise(texcoord * 9.0 + vec2(cosmetic_time * 0.025, 0.0));
        float star = step(0.985, cosmetic_noise(floor(texcoord * 100.0)));
        col.rgb = mix(vec3(0.10, 0.12, 0.32), vec3(0.48, 0.16, 0.56), cloud) * (0.5 + lum) + star * 0.65;
    }
    if (cosmetic_skin != 0) {
        vec2 finish = cosmetic_finish();
        // A restrained view-dependent sheen also works in forward/preview rendering.
        float sheen = pow(clamp(dot(viewNormal, normalize(vec3(-0.35, 0.6, 0.72))), 0.0, 1.0), mix(44.0, 8.0, finish.y));
        col.rgb += vec3(0.12, 0.16, 0.20) * rim * (1.0 - finish.y)
            + mix(vec3(0.12), col.rgb * 0.28, finish.x) * sheen;
    }
    if (cosmetic_preserve_palette != 0 && cosmetic_skin != 0) {
        float chroma = max(nativeColor.r, max(nativeColor.g, nativeColor.b)) - min(nativeColor.r, min(nativeColor.g, nativeColor.b));
        // Keep saturated native team/suit panels; neutral armor carries the skin.
        col.rgb = mix(col.rgb, nativeColor, smoothstep(0.10, 0.35, chroma) * 0.90);
    }
    if (cosmetic_effect != 0) {
        float t = cosmetic_time * cosmetic_scroll;
        float pulse = 0.82 + 0.18 * sin(cosmetic_time * cosmetic_pulse);
        float wave = 0.5 + 0.5 * sin(texcoord.y * 30.0 - t * 2.0);
        float mask = 0.28 + rim * 0.72;
        if (cosmetic_effect == 1) mask = 0.25 + 0.55 * pulse;
        if (cosmetic_effect == 3) mask = smoothstep(0.78, 0.95, wave) * 0.5 + rim;
        if (cosmetic_effect == 4) mask = cosmetic_soft_noise(texcoord * 16.0 + vec2(t * 0.3, -t * 0.5)) * 0.45 + rim * 0.6;
        if (cosmetic_effect == 5 || cosmetic_effect == 7) mask = 0.12 + smoothstep(0.65, 0.95, wave) * 0.65 + rim * 0.45;
        if (cosmetic_effect == 6) mask = 0.12 + pow(abs(sin(texcoord.x * 31.0 + sin(texcoord.y * 29.0 + t))), 16.0) + rim * 0.5;
        if (cosmetic_effect == 8) mask = wave * 0.35 + rim * 0.7;
        if (cosmetic_effect == 9) mask = (0.5 + 0.5 * sin(texcoord.x * 40.0 + t)) * rim;
        vec3 energy = mix(cosmetic_primary, cosmetic_secondary, wave);
        float strength = clamp(mask, 0.0, 1.0) * cosmetic_intensity * pulse;
        if (cosmetic_preserve_palette != 0) strength *= 0.65;
        // A colored surface layer remains legible on saturated cartridge textures,
        // where purely additive emission used to clip to white/yellow.
        col.rgb = mix(col.rgb, energy * (0.4 + lum * 0.6), clamp(strength * 0.55, 0.0, 0.65));
        col.rgb += energy * strength * 0.45;
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
