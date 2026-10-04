struct PrimeUniforms {
    _prime_use_light: i32,
    _pad_16_0_: f32,
    _pad_16_1_: f32,
    _pad_16_2_: f32,
    _prime_use_texture: i32,
    _pad_32_0_: f32,
    _pad_32_1_: f32,
    _pad_32_2_: f32,
    _prime_show_colors: i32,
    _pad_48_0_: f32,
    _pad_48_1_: f32,
    _pad_48_2_: f32,
    _prime_fog_enable: i32,
    _pad_64_0_: f32,
    _pad_64_1_: f32,
    _pad_64_2_: f32,
    light1vec: vec3<f32>,
    _pad_80_0_: f32,
    light1col: vec3<f32>,
    _pad_96_0_: f32,
    light2vec: vec3<f32>,
    _pad_112_0_: f32,
    light2col: vec3<f32>,
    _pad_128_0_: f32,
    diffuse: vec3<f32>,
    _pad_144_0_: f32,
    ambient: vec3<f32>,
    _pad_160_0_: f32,
    specular: vec3<f32>,
    _pad_176_0_: f32,
    emission: vec3<f32>,
    _pad_192_0_: f32,
    fog_color: vec4<f32>,
    far_plane: f32,
    _pad_224_0_: f32,
    _pad_224_1_: f32,
    _pad_224_2_: f32,
    proj_mtx: mat4x4<f32>,
    view_mtx: mat4x4<f32>,
    view_inv_mtx: mat4x4<f32>,
    tex_mtx: mat4x4<f32>,
    texgen_mode: i32,
    _pad_496_0_: f32,
    _pad_496_1_: f32,
    _pad_496_2_: f32,
    _prime_weighted_skinning: i32,
    _pad_512_0_: f32,
    _pad_512_1_: f32,
    _pad_512_2_: f32,
    mtx_stack: array<mat4x4<f32>, 32>,
    fog_min: f32,
    _pad_2576_0_: f32,
    _pad_2576_1_: f32,
    _pad_2576_2_: f32,
    fog_max: f32,
    _pad_2592_0_: f32,
    _pad_2592_1_: f32,
    _pad_2592_2_: f32,
    _prime_advanced_materials: i32,
    _pad_2608_0_: f32,
    _pad_2608_1_: f32,
    _pad_2608_2_: f32,
    _prime_use_normal_map: i32,
    _pad_2624_0_: f32,
    _pad_2624_1_: f32,
    _pad_2624_2_: f32,
    _prime_use_specular_map: i32,
    _pad_2640_0_: f32,
    _pad_2640_1_: f32,
    _pad_2640_2_: f32,
    _prime_use_emissive_map: i32,
    _pad_2656_0_: f32,
    _pad_2656_1_: f32,
    _pad_2656_2_: f32,
    _prime_use_override: i32,
    _pad_2672_0_: f32,
    _pad_2672_1_: f32,
    _pad_2672_2_: f32,
    textured_player_skin: i32,
    _pad_2688_0_: f32,
    _pad_2688_1_: f32,
    _pad_2688_2_: f32,
    _prime_player_outline_mask: i32,
    _pad_2704_0_: f32,
    _pad_2704_1_: f32,
    _pad_2704_2_: f32,
    player_outline_color: vec3<f32>,
    _pad_2720_0_: f32,
    override_color: vec4<f32>,
    _prime_use_pal_override: i32,
    _pad_2752_0_: f32,
    _pad_2752_1_: f32,
    _pad_2752_2_: f32,
    pal_override_color: vec4<f32>,
    mat_alpha: f32,
    _pad_2784_0_: f32,
    _pad_2784_1_: f32,
    _pad_2784_2_: f32,
    mat_mode: i32,
    _pad_2800_0_: f32,
    _pad_2800_1_: f32,
    _pad_2800_2_: f32,
    toon_table: array<vec3<f32>, 32>,
    cel_bands: i32,
    _pad_3328_0_: f32,
    _pad_3328_1_: f32,
    _pad_3328_2_: f32,
    _prime_use_flat: i32,
    _pad_3344_0_: f32,
    _pad_3344_1_: f32,
    _pad_3344_2_: f32,
    flat_color: vec3<f32>,
    _pad_3360_0_: f32,
    cosmetic_skin: i32,
    _pad_3376_0_: f32,
    _pad_3376_1_: f32,
    _pad_3376_2_: f32,
    cosmetic_preserve_palette: i32,
    _pad_3392_0_: f32,
    _pad_3392_1_: f32,
    _pad_3392_2_: f32,
    cosmetic_effect: i32,
    _pad_3408_0_: f32,
    _pad_3408_1_: f32,
    _pad_3408_2_: f32,
    cosmetic_time: f32,
    _pad_3424_0_: f32,
    _pad_3424_1_: f32,
    _pad_3424_2_: f32,
    cosmetic_primary: vec3<f32>,
    _pad_3440_0_: f32,
    cosmetic_secondary: vec3<f32>,
    _pad_3456_0_: f32,
    cosmetic_intensity: f32,
    _pad_3472_0_: f32,
    _pad_3472_1_: f32,
    _pad_3472_2_: f32,
    cosmetic_pulse: f32,
    _pad_3488_0_: f32,
    _pad_3488_1_: f32,
    _pad_3488_2_: f32,
    cosmetic_scroll: f32,
    _pad_3504_0_: f32,
    _pad_3504_1_: f32,
    _pad_3504_2_: f32,
    cosmetic_dissolve: f32,
    _pad_3520_0_: f32,
    _pad_3520_1_: f32,
    _pad_3520_2_: f32,
    prime_viewport: vec4<f32>,
    prime_imm_color: vec4<f32>,
    prime_imm_normal: vec4<f32>,
    prime_alpha_func: i32,
    _pad_3584_0_: f32,
    _pad_3584_1_: f32,
    _pad_3584_2_: f32,
    prime_alpha_ref: f32,
    _pad_3600_0_: f32,
    _pad_3600_1_: f32,
    _pad_3600_2_: f32,
    prime_texture_flip: array<vec4<f32>, 4>,
}

struct FragmentOutput {
    @location(0) prime_output: vec4<f32>,
}

@group(0) @binding(0)
var<uniform> global: PrimeUniforms;
@group(0) @binding(1)
var prime_tex_tex: texture_2d<f32>;
@group(0) @binding(2)
var prime_sampler_tex: sampler;
@group(0) @binding(3)
var prime_tex_normal_tex: texture_2d<f32>;
@group(0) @binding(4)
var prime_sampler_normal_tex: sampler;
@group(0) @binding(5)
var prime_tex_specular_tex: texture_2d<f32>;
@group(0) @binding(6)
var prime_sampler_specular_tex: sampler;
@group(0) @binding(7)
var prime_tex_emissive_tex: texture_2d<f32>;
@group(0) @binding(8)
var prime_sampler_emissive_tex: sampler;
var<private> prime_output: vec4<f32>;
var<private> texcoord_1: vec2<f32>;
var<private> color_1: vec4<f32>;
var<private> surface_normal_1: vec3<f32>;
var<private> surface_position_1: vec3<f32>;
var<private> gl_FragCoord: vec4<f32>;

fn prime_sample_tex(uv: vec2<f32>) -> vec4<f32> {
    var uv_1: vec2<f32>;

    uv_1 = uv;
    let _e326: vec2<f32> = uv_1;
    let _e328: vec2<f32> = uv_1;
    let _e331: vec2<f32> = uv_1;
    let _e336: vec4<f32> = global.prime_texture_flip[0];
    let _e338: vec2<f32> = uv_1;
    let _e341: vec2<f32> = uv_1;
    let _e346: vec4<f32> = global.prime_texture_flip[0];
    let _e350: vec2<f32> = uv_1;
    let _e352: vec2<f32> = uv_1;
    let _e355: vec2<f32> = uv_1;
    let _e360: vec4<f32> = global.prime_texture_flip[0];
    let _e362: vec2<f32> = uv_1;
    let _e365: vec2<f32> = uv_1;
    let _e370: vec4<f32> = global.prime_texture_flip[0];
    let _e374: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e350.x, mix(_e362.y, (1f - _e365.y), _e370.x)));
    return _e374;
}

fn prime_sample_normal_tex(uv_2: vec2<f32>) -> vec4<f32> {
    var uv_3: vec2<f32>;

    uv_3 = uv_2;
    let _e328: vec2<f32> = uv_3;
    let _e330: vec2<f32> = uv_3;
    let _e333: vec2<f32> = uv_3;
    let _e338: vec4<f32> = global.prime_texture_flip[1];
    let _e340: vec2<f32> = uv_3;
    let _e343: vec2<f32> = uv_3;
    let _e348: vec4<f32> = global.prime_texture_flip[1];
    let _e352: vec2<f32> = uv_3;
    let _e354: vec2<f32> = uv_3;
    let _e357: vec2<f32> = uv_3;
    let _e362: vec4<f32> = global.prime_texture_flip[1];
    let _e364: vec2<f32> = uv_3;
    let _e367: vec2<f32> = uv_3;
    let _e372: vec4<f32> = global.prime_texture_flip[1];
    let _e376: vec4<f32> = textureSample(prime_tex_normal_tex, prime_sampler_normal_tex, vec2<f32>(_e352.x, mix(_e364.y, (1f - _e367.y), _e372.x)));
    return _e376;
}

fn prime_sample_specular_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e330: vec2<f32> = uv_5;
    let _e332: vec2<f32> = uv_5;
    let _e335: vec2<f32> = uv_5;
    let _e340: vec4<f32> = global.prime_texture_flip[2];
    let _e342: vec2<f32> = uv_5;
    let _e345: vec2<f32> = uv_5;
    let _e350: vec4<f32> = global.prime_texture_flip[2];
    let _e354: vec2<f32> = uv_5;
    let _e356: vec2<f32> = uv_5;
    let _e359: vec2<f32> = uv_5;
    let _e364: vec4<f32> = global.prime_texture_flip[2];
    let _e366: vec2<f32> = uv_5;
    let _e369: vec2<f32> = uv_5;
    let _e374: vec4<f32> = global.prime_texture_flip[2];
    let _e378: vec4<f32> = textureSample(prime_tex_specular_tex, prime_sampler_specular_tex, vec2<f32>(_e354.x, mix(_e366.y, (1f - _e369.y), _e374.x)));
    return _e378;
}

fn prime_sample_emissive_tex(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e332: vec2<f32> = uv_7;
    let _e334: vec2<f32> = uv_7;
    let _e337: vec2<f32> = uv_7;
    let _e342: vec4<f32> = global.prime_texture_flip[3];
    let _e344: vec2<f32> = uv_7;
    let _e347: vec2<f32> = uv_7;
    let _e352: vec4<f32> = global.prime_texture_flip[3];
    let _e356: vec2<f32> = uv_7;
    let _e358: vec2<f32> = uv_7;
    let _e361: vec2<f32> = uv_7;
    let _e366: vec4<f32> = global.prime_texture_flip[3];
    let _e368: vec2<f32> = uv_7;
    let _e371: vec2<f32> = uv_7;
    let _e376: vec4<f32> = global.prime_texture_flip[3];
    let _e380: vec4<f32> = textureSample(prime_tex_emissive_tex, prime_sampler_emissive_tex, vec2<f32>(_e356.x, mix(_e368.y, (1f - _e371.y), _e376.x)));
    return _e380;
}

fn mapped_normal() -> vec3<f32> {
    var n: vec3<f32>;
    var dp1_: vec3<f32>;
    var dp2_: vec3<f32>;
    var duv1_: vec2<f32>;
    var duv2_: vec2<f32>;
    var det: f32;
    var tangent: vec3<f32>;
    var bitangent: vec3<f32>;
    var mapNormal: vec3<f32>;

    let _e336: vec3<f32> = surface_normal_1;
    n = normalize(_e336);
    let _e339: i32 = global._prime_use_normal_map;
    if !((_e339 != 0i)) {
        let _e343: vec3<f32> = n;
        return _e343;
    }
    let _e345: vec3<f32> = surface_position_1;
    let _e346: vec3<f32> = dpdx(_e345);
    dp1_ = _e346;
    let _e349: vec3<f32> = surface_position_1;
    let _e350: vec3<f32> = dpdy(_e349);
    dp2_ = _e350;
    let _e353: vec2<f32> = texcoord_1;
    let _e354: vec2<f32> = dpdx(_e353);
    duv1_ = _e354;
    let _e357: vec2<f32> = texcoord_1;
    let _e358: vec2<f32> = dpdy(_e357);
    duv2_ = _e358;
    let _e360: vec2<f32> = duv1_;
    let _e362: vec2<f32> = duv2_;
    let _e365: vec2<f32> = duv1_;
    let _e367: vec2<f32> = duv2_;
    det = ((_e360.x * _e362.y) - (_e365.y * _e367.x));
    let _e373: f32 = det;
    if (abs(_e373) < 0.000001f) {
        let _e377: vec3<f32> = n;
        return _e377;
    }
    let _e378: vec3<f32> = dp1_;
    let _e379: vec2<f32> = duv2_;
    let _e382: vec3<f32> = dp2_;
    let _e383: vec2<f32> = duv1_;
    let _e387: f32 = det;
    let _e390: vec3<f32> = dp1_;
    let _e391: vec2<f32> = duv2_;
    let _e394: vec3<f32> = dp2_;
    let _e395: vec2<f32> = duv1_;
    let _e399: f32 = det;
    tangent = normalize((((_e390 * _e391.y) - (_e394 * _e395.y)) / vec3(_e399)));
    let _e404: vec3<f32> = dp1_;
    let _e406: vec2<f32> = duv2_;
    let _e409: vec3<f32> = dp2_;
    let _e410: vec2<f32> = duv1_;
    let _e414: f32 = det;
    let _e417: vec3<f32> = dp1_;
    let _e419: vec2<f32> = duv2_;
    let _e422: vec3<f32> = dp2_;
    let _e423: vec2<f32> = duv1_;
    let _e427: f32 = det;
    bitangent = normalize((((-(_e417) * _e419.x) + (_e422 * _e423.x)) / vec3(_e427)));
    let _e433: vec2<f32> = texcoord_1;
    let _e434: vec4<f32> = prime_sample_normal_tex(_e433);
    mapNormal = ((_e434.xyz * 2f) - vec3(1f));
    let _e442: vec3<f32> = tangent;
    let _e443: vec3<f32> = mapNormal;
    let _e446: vec3<f32> = bitangent;
    let _e447: vec3<f32> = mapNormal;
    let _e451: vec3<f32> = n;
    let _e452: vec3<f32> = mapNormal;
    let _e456: vec3<f32> = tangent;
    let _e457: vec3<f32> = mapNormal;
    let _e460: vec3<f32> = bitangent;
    let _e461: vec3<f32> = mapNormal;
    let _e465: vec3<f32> = n;
    let _e466: vec3<f32> = mapNormal;
    return normalize((((_e456 * _e457.x) + (_e460 * _e461.y)) + (_e465 * _e466.z)));
}

fn cosmetic_noise(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e341: vec2<f32> = p_1;
    let _e350: vec2<f32> = p_1;
    let _e362: vec2<f32> = p_1;
    let _e371: vec2<f32> = p_1;
    return fract((sin(dot(_e371, vec2<f32>(127.1f, 311.7f))) * 43758.547f));
}

fn cosmetic_soft_noise(p_2: vec2<f32>) -> f32 {
    var p_3: vec2<f32>;
    var cell: vec2<f32>;
    var f: vec2<f32>;

    p_3 = p_2;
    let _e338: vec2<f32> = p_3;
    cell = floor(_e338);
    let _e342: vec2<f32> = p_3;
    f = fract(_e342);
    let _e345: vec2<f32> = f;
    let _e346: vec2<f32> = f;
    let _e350: vec2<f32> = f;
    f = ((_e345 * _e346) * (vec2(3f) - (2f * _e350)));
    let _e356: vec2<f32> = cell;
    let _e357: f32 = cosmetic_noise(_e356);
    let _e358: vec2<f32> = cell;
    let _e363: vec2<f32> = cell;
    let _e368: f32 = cosmetic_noise((_e363 + vec2<f32>(1f, 0f)));
    let _e369: vec2<f32> = f;
    let _e372: vec2<f32> = cell;
    let _e373: f32 = cosmetic_noise(_e372);
    let _e374: vec2<f32> = cell;
    let _e379: vec2<f32> = cell;
    let _e384: f32 = cosmetic_noise((_e379 + vec2<f32>(1f, 0f)));
    let _e385: vec2<f32> = f;
    let _e388: vec2<f32> = cell;
    let _e393: vec2<f32> = cell;
    let _e398: f32 = cosmetic_noise((_e393 + vec2<f32>(0f, 1f)));
    let _e399: vec2<f32> = cell;
    let _e403: vec2<f32> = cell;
    let _e407: f32 = cosmetic_noise((_e403 + vec2(1f)));
    let _e408: vec2<f32> = f;
    let _e410: vec2<f32> = cell;
    let _e415: vec2<f32> = cell;
    let _e420: f32 = cosmetic_noise((_e415 + vec2<f32>(0f, 1f)));
    let _e421: vec2<f32> = cell;
    let _e425: vec2<f32> = cell;
    let _e429: f32 = cosmetic_noise((_e425 + vec2(1f)));
    let _e430: vec2<f32> = f;
    let _e433: vec2<f32> = f;
    let _e436: vec2<f32> = cell;
    let _e437: f32 = cosmetic_noise(_e436);
    let _e438: vec2<f32> = cell;
    let _e443: vec2<f32> = cell;
    let _e448: f32 = cosmetic_noise((_e443 + vec2<f32>(1f, 0f)));
    let _e449: vec2<f32> = f;
    let _e452: vec2<f32> = cell;
    let _e453: f32 = cosmetic_noise(_e452);
    let _e454: vec2<f32> = cell;
    let _e459: vec2<f32> = cell;
    let _e464: f32 = cosmetic_noise((_e459 + vec2<f32>(1f, 0f)));
    let _e465: vec2<f32> = f;
    let _e468: vec2<f32> = cell;
    let _e473: vec2<f32> = cell;
    let _e478: f32 = cosmetic_noise((_e473 + vec2<f32>(0f, 1f)));
    let _e479: vec2<f32> = cell;
    let _e483: vec2<f32> = cell;
    let _e487: f32 = cosmetic_noise((_e483 + vec2(1f)));
    let _e488: vec2<f32> = f;
    let _e490: vec2<f32> = cell;
    let _e495: vec2<f32> = cell;
    let _e500: f32 = cosmetic_noise((_e495 + vec2<f32>(0f, 1f)));
    let _e501: vec2<f32> = cell;
    let _e505: vec2<f32> = cell;
    let _e509: f32 = cosmetic_noise((_e505 + vec2(1f)));
    let _e510: vec2<f32> = f;
    let _e513: vec2<f32> = f;
    return mix(mix(_e453, _e464, _e465.x), mix(_e500, _e509, _e510.x), _e513.y);
}

fn cosmetic_finish() -> vec2<f32> {
    let _e335: i32 = global.cosmetic_skin;
    if (_e335 == 1i) {
        return vec2<f32>(0.78f, 0.3f);
    }
    let _e341: i32 = global.cosmetic_skin;
    if (_e341 == 2i) {
        return vec2<f32>(0.85f, 0.38f);
    }
    let _e347: i32 = global.cosmetic_skin;
    if (_e347 == 3i) {
        return vec2<f32>(0.05f, 0.24f);
    }
    let _e353: i32 = global.cosmetic_skin;
    if (_e353 == 4i) {
        return vec2<f32>(0.55f, 0.42f);
    }
    let _e359: i32 = global.cosmetic_skin;
    if (_e359 == 5i) {
        return vec2<f32>(0.12f, 0.68f);
    }
    return vec2<f32>(0.35f, 0.28f);
}

fn cosmetic_circuit() -> f32 {
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var travel: f32;

    let _e335: vec2<f32> = texcoord_1;
    let _e338: vec2<f32> = texcoord_1;
    grid = fract((_e338 * 18f));
    let _e346: vec2<f32> = grid;
    let _e348: vec2<f32> = grid;
    let _e350: vec2<f32> = grid;
    let _e352: vec2<f32> = grid;
    let _e357: vec2<f32> = grid;
    let _e359: vec2<f32> = grid;
    let _e361: vec2<f32> = grid;
    let _e363: vec2<f32> = grid;
    trace = (1f - smoothstep(0.035f, 0.09f, min(_e361.x, _e363.y)));
    let _e372: vec2<f32> = grid;
    let _e376: vec2<f32> = grid;
    let _e383: vec2<f32> = grid;
    let _e387: vec2<f32> = grid;
    node = (1f - smoothstep(0.08f, 0.16f, length((_e387 - vec2(0.15f)))));
    let _e397: vec2<f32> = texcoord_1;
    let _e399: vec2<f32> = texcoord_1;
    let _e404: f32 = global.cosmetic_time;
    let _e408: vec2<f32> = texcoord_1;
    let _e410: vec2<f32> = texcoord_1;
    let _e415: f32 = global.cosmetic_time;
    let _e425: vec2<f32> = texcoord_1;
    let _e427: vec2<f32> = texcoord_1;
    let _e432: f32 = global.cosmetic_time;
    let _e436: vec2<f32> = texcoord_1;
    let _e438: vec2<f32> = texcoord_1;
    let _e443: f32 = global.cosmetic_time;
    travel = pow((0.5f + (0.5f * sin((((_e436.x + _e438.y) * 32f) - (_e443 * 1.8f))))), 8f);
    let _e453: f32 = trace;
    let _e455: f32 = travel;
    let _e460: f32 = node;
    let _e463: f32 = trace;
    let _e465: f32 = travel;
    let _e470: f32 = node;
    return max((_e463 * (0.3f + (_e465 * 0.7f))), (_e470 * 0.75f));
}

fn apply_cosmetics(col: ptr<function, vec4<f32>>) {
    var nativeColor: vec3<f32>;
    var lum: f32;
    var n_1: vec3<f32>;
    var viewNormal: vec3<f32>;
    var toEye: vec3<f32>;
    var rim: f32;
    var etch: f32;
    var panel: vec2<f32>;
    var seam: f32;
    var stripe: f32;
    var cloud: f32;
    var star: f32;
    var finish: vec2<f32>;
    var sheen: f32;
    var chroma: f32;
    var t: f32;
    var pulse: f32;
    var wave: f32;
    var mask: f32;
    var energy: vec3<f32>;
    var strength: f32;
    var n_2: f32;

    let _e336: i32 = global.cosmetic_skin;
    let _e339: i32 = global.cosmetic_effect;
    let _e343: f32 = global.cosmetic_dissolve;
    if (((_e336 == 0i) && (_e339 == 0i)) && (_e343 <= 0f)) {
        return;
    }
    let _e347: vec4<f32> = (*col);
    nativeColor = _e347.xyz;
    let _e350: vec4<f32> = (*col);
    let _e356: vec4<f32> = (*col);
    lum = dot(_e356.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e364: vec3<f32> = surface_normal_1;
    let _e367: vec3<f32> = surface_normal_1;
    let _e368: vec3<f32> = surface_normal_1;
    let _e373: vec3<f32> = surface_normal_1;
    let _e374: vec3<f32> = surface_normal_1;
    let _e380: vec3<f32> = surface_normal_1;
    let _e381: vec3<f32> = surface_normal_1;
    let _e386: vec3<f32> = surface_normal_1;
    let _e387: vec3<f32> = surface_normal_1;
    n_1 = (_e364 * inverseSqrt(max(dot(_e386, _e387), 0.0001f)));
    let _e394: mat4x4<f32> = global.view_mtx;
    let _e404: vec3<f32> = n_1;
    viewNormal = (mat3x3<f32>(_e394[0].xyz, _e394[1].xyz, _e394[2].xyz) * _e404);
    let _e407: mat4x4<f32> = global.view_mtx;
    let _e408: vec3<f32> = surface_position_1;
    toEye = -((_e407 * vec4<f32>(_e408.x, _e408.y, _e408.z, 1f)).xyz);
    let _e418: vec3<f32> = toEye;
    let _e421: vec3<f32> = toEye;
    let _e422: vec3<f32> = toEye;
    let _e427: vec3<f32> = toEye;
    let _e428: vec3<f32> = toEye;
    let _e434: vec3<f32> = toEye;
    let _e435: vec3<f32> = toEye;
    let _e440: vec3<f32> = toEye;
    let _e441: vec3<f32> = toEye;
    toEye = (_e418 * inverseSqrt(max(dot(_e440, _e441), 0.0001f)));
    let _e450: vec3<f32> = viewNormal;
    let _e451: vec3<f32> = toEye;
    let _e455: vec3<f32> = viewNormal;
    let _e456: vec3<f32> = toEye;
    let _e465: vec3<f32> = viewNormal;
    let _e466: vec3<f32> = toEye;
    let _e470: vec3<f32> = viewNormal;
    let _e471: vec3<f32> = toEye;
    let _e482: vec3<f32> = viewNormal;
    let _e483: vec3<f32> = toEye;
    let _e487: vec3<f32> = viewNormal;
    let _e488: vec3<f32> = toEye;
    let _e497: vec3<f32> = viewNormal;
    let _e498: vec3<f32> = toEye;
    let _e502: vec3<f32> = viewNormal;
    let _e503: vec3<f32> = toEye;
    rim = pow(clamp((1f - abs(dot(_e502, _e503))), 0f, 1f), 2.2f);
    let _e513: i32 = global.cosmetic_skin;
    if (_e513 == 1i) {
        {
            let _e516: vec4<f32> = (*col);
            let _e518: vec4<f32> = (*col);
            let _e525: f32 = lum;
            let _e529: vec4<f32> = (*col);
            let _e536: f32 = lum;
            let _e541: vec3<f32> = mix(_e529.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e536)), vec3(0.88f));
            (*col).x = _e541.x;
            (*col).y = _e541.y;
            (*col).z = _e541.z;
        }
    } else {
        let _e548: i32 = global.cosmetic_skin;
        if (_e548 == 2i) {
            {
                let _e553: vec2<f32> = texcoord_1;
                let _e557: vec2<f32> = texcoord_1;
                let _e562: vec2<f32> = texcoord_1;
                let _e566: vec2<f32> = texcoord_1;
                let _e574: vec2<f32> = texcoord_1;
                let _e578: vec2<f32> = texcoord_1;
                let _e583: vec2<f32> = texcoord_1;
                let _e587: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e578.x * 85f)) * sin((_e587.y * 85f))));
                let _e595: vec4<f32> = (*col);
                let _e597: vec4<f32> = (*col);
                let _e604: f32 = lum;
                let _e608: vec4<f32> = (*col);
                let _e615: f32 = lum;
                let _e620: vec3<f32> = mix(_e608.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e615)), vec3(0.82f));
                (*col).x = _e620.x;
                (*col).y = _e620.y;
                (*col).z = _e620.z;
                let _e627: vec4<f32> = (*col);
                let _e629: vec4<f32> = (*col);
                let _e631: f32 = etch;
                let _e637: vec3<f32> = (_e629.xyz + (_e631 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e637.x;
                (*col).y = _e637.y;
                (*col).z = _e637.z;
            }
        }
    }
    let _e644: i32 = global.cosmetic_skin;
    if (_e644 == 3i) {
        {
            let _e647: vec2<f32> = texcoord_1;
            let _e650: vec2<f32> = texcoord_1;
            panel = fract((_e650 * 12f));
            let _e657: vec2<f32> = panel;
            let _e661: vec2<f32> = panel;
            let _e666: vec2<f32> = panel;
            let _e670: vec2<f32> = panel;
            seam = (smoothstep(0.025f, 0.075f, _e661.x) * smoothstep(0.025f, 0.075f, _e670.y));
            let _e675: vec4<f32> = (*col);
            let _e694: f32 = seam;
            let _e698: f32 = lum;
            let _e702: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e694)) * (0.5f + (_e698 * 0.6f)));
            (*col).x = _e702.x;
            (*col).y = _e702.y;
            (*col).z = _e702.z;
        }
    } else {
        let _e709: i32 = global.cosmetic_skin;
        if (_e709 == 4i) {
            {
                let _e712: vec4<f32> = (*col);
                let _e719: f32 = lum;
                let _e726: f32 = cosmetic_circuit();
                let _e728: vec3<f32> = ((vec3<f32>(0.13f, 0.18f, 0.22f) * (0.6f + _e719)) + (vec3<f32>(0.08f, 0.8f, 0.65f) * _e726));
                (*col).x = _e728.x;
                (*col).y = _e728.y;
                (*col).z = _e728.z;
            }
        } else {
            let _e735: i32 = global.cosmetic_skin;
            if (_e735 == 5i) {
                {
                    let _e740: vec2<f32> = texcoord_1;
                    let _e744: vec2<f32> = texcoord_1;
                    let _e749: vec2<f32> = texcoord_1;
                    let _e753: vec2<f32> = texcoord_1;
                    let _e761: vec2<f32> = texcoord_1;
                    let _e765: vec2<f32> = texcoord_1;
                    let _e770: vec2<f32> = texcoord_1;
                    let _e774: vec2<f32> = texcoord_1;
                    let _e785: vec2<f32> = texcoord_1;
                    let _e789: vec2<f32> = texcoord_1;
                    let _e794: vec2<f32> = texcoord_1;
                    let _e798: vec2<f32> = texcoord_1;
                    let _e806: vec2<f32> = texcoord_1;
                    let _e810: vec2<f32> = texcoord_1;
                    let _e815: vec2<f32> = texcoord_1;
                    let _e819: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e806.x * 65f) + (_e810.y * 38f)) + (sin((_e819.y * 25f)) * 2f))));
                    let _e830: vec4<f32> = (*col);
                    let _e849: f32 = stripe;
                    let _e853: f32 = lum;
                    let _e855: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e849)) * (0.55f + _e853));
                    (*col).x = _e855.x;
                    (*col).y = _e855.y;
                    (*col).z = _e855.z;
                }
            } else {
                let _e862: i32 = global.cosmetic_skin;
                if (_e862 == 6i) {
                    {
                        let _e865: vec2<f32> = texcoord_1;
                        let _e868: f32 = global.cosmetic_time;
                        let _e874: vec2<f32> = texcoord_1;
                        let _e877: f32 = global.cosmetic_time;
                        let _e883: f32 = cosmetic_soft_noise(((_e874 * 9f) + vec2<f32>((_e877 * 0.025f), 0f)));
                        cloud = _e883;
                        let _e886: vec2<f32> = texcoord_1;
                        let _e889: vec2<f32> = texcoord_1;
                        let _e893: vec2<f32> = texcoord_1;
                        let _e896: vec2<f32> = texcoord_1;
                        let _e900: f32 = cosmetic_noise(floor((_e896 * 100f)));
                        let _e902: vec2<f32> = texcoord_1;
                        let _e905: vec2<f32> = texcoord_1;
                        let _e909: vec2<f32> = texcoord_1;
                        let _e912: vec2<f32> = texcoord_1;
                        let _e916: f32 = cosmetic_noise(floor((_e912 * 100f)));
                        star = step(0.985f, _e916);
                        let _e919: vec4<f32> = (*col);
                        let _e938: f32 = cloud;
                        let _e942: f32 = lum;
                        let _e945: f32 = star;
                        let _e949: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e938)) * (0.5f + _e942)) + vec3((_e945 * 0.65f)));
                        (*col).x = _e949.x;
                        (*col).y = _e949.y;
                        (*col).z = _e949.z;
                    }
                }
            }
        }
    }
    let _e956: i32 = global.cosmetic_skin;
    if (_e956 != 0i) {
        {
            let _e959: vec2<f32> = cosmetic_finish();
            finish = _e959;
            let _e973: vec3<f32> = viewNormal;
            let _e1000: vec3<f32> = viewNormal;
            let _e1018: vec2<f32> = finish;
            let _e1022: vec2<f32> = finish;
            let _e1037: vec3<f32> = viewNormal;
            let _e1064: vec3<f32> = viewNormal;
            let _e1082: vec2<f32> = finish;
            let _e1086: vec2<f32> = finish;
            sheen = pow(clamp(dot(_e1064, normalize(vec3<f32>(-0.35f, 0.6f, 0.72f))), 0f, 1f), mix(44f, 8f, _e1086.y));
            let _e1091: vec4<f32> = (*col);
            let _e1093: vec4<f32> = (*col);
            let _e1099: f32 = rim;
            let _e1102: vec2<f32> = finish;
            let _e1108: vec4<f32> = (*col);
            let _e1112: vec2<f32> = finish;
            let _e1116: vec4<f32> = (*col);
            let _e1120: vec2<f32> = finish;
            let _e1124: f32 = sheen;
            let _e1127: vec3<f32> = (_e1093.xyz + (((vec3<f32>(0.12f, 0.16f, 0.2f) * _e1099) * (1f - _e1102.y)) + (mix(vec3(0.12f), (_e1116.xyz * 0.28f), vec3(_e1120.x)) * _e1124)));
            (*col).x = _e1127.x;
            (*col).y = _e1127.y;
            (*col).z = _e1127.z;
        }
    }
    let _e1134: i32 = global.cosmetic_preserve_palette;
    let _e1137: i32 = global.cosmetic_skin;
    if ((_e1134 != 0i) && (_e1137 != 0i)) {
        {
            let _e1141: vec3<f32> = nativeColor;
            let _e1143: vec3<f32> = nativeColor;
            let _e1145: vec3<f32> = nativeColor;
            let _e1147: vec3<f32> = nativeColor;
            let _e1149: vec3<f32> = nativeColor;
            let _e1152: vec3<f32> = nativeColor;
            let _e1154: vec3<f32> = nativeColor;
            let _e1156: vec3<f32> = nativeColor;
            let _e1158: vec3<f32> = nativeColor;
            let _e1160: vec3<f32> = nativeColor;
            let _e1164: vec3<f32> = nativeColor;
            let _e1166: vec3<f32> = nativeColor;
            let _e1168: vec3<f32> = nativeColor;
            let _e1170: vec3<f32> = nativeColor;
            let _e1172: vec3<f32> = nativeColor;
            let _e1175: vec3<f32> = nativeColor;
            let _e1177: vec3<f32> = nativeColor;
            let _e1179: vec3<f32> = nativeColor;
            let _e1181: vec3<f32> = nativeColor;
            let _e1183: vec3<f32> = nativeColor;
            chroma = (max(_e1152.x, max(_e1158.y, _e1160.z)) - min(_e1175.x, min(_e1181.y, _e1183.z)));
            let _e1189: vec4<f32> = (*col);
            let _e1191: vec4<f32> = (*col);
            let _e1199: f32 = chroma;
            let _e1203: vec4<f32> = (*col);
            let _e1205: vec3<f32> = nativeColor;
            let _e1211: f32 = chroma;
            let _e1216: vec3<f32> = mix(_e1203.xyz, _e1205, vec3((smoothstep(0.1f, 0.35f, _e1211) * 0.9f)));
            (*col).x = _e1216.x;
            (*col).y = _e1216.y;
            (*col).z = _e1216.z;
        }
    }
    let _e1223: i32 = global.cosmetic_effect;
    if (_e1223 != 0i) {
        {
            let _e1226: f32 = global.cosmetic_time;
            let _e1227: f32 = global.cosmetic_scroll;
            t = (_e1226 * _e1227);
            let _e1232: f32 = global.cosmetic_time;
            let _e1233: f32 = global.cosmetic_pulse;
            let _e1235: f32 = global.cosmetic_time;
            let _e1236: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1235 * _e1236))));
            let _e1244: vec2<f32> = texcoord_1;
            let _e1248: f32 = t;
            let _e1252: vec2<f32> = texcoord_1;
            let _e1256: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1252.y * 30f) - (_e1256 * 2f)))));
            let _e1265: f32 = rim;
            mask = (0.28f + (_e1265 * 0.72f));
            let _e1270: i32 = global.cosmetic_effect;
            if (_e1270 == 1i) {
                let _e1275: f32 = pulse;
                mask = (0.25f + (0.55f * _e1275));
            }
            let _e1278: i32 = global.cosmetic_effect;
            if (_e1278 == 3i) {
                let _e1286: f32 = wave;
                let _e1290: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1286) * 0.5f) + _e1290);
            }
            let _e1292: i32 = global.cosmetic_effect;
            if (_e1292 == 4i) {
                let _e1295: vec2<f32> = texcoord_1;
                let _e1298: f32 = t;
                let _e1301: f32 = t;
                let _e1307: vec2<f32> = texcoord_1;
                let _e1310: f32 = t;
                let _e1313: f32 = t;
                let _e1319: f32 = cosmetic_soft_noise(((_e1307 * 16f) + vec2<f32>((_e1310 * 0.3f), (-(_e1313) * 0.5f))));
                let _e1322: f32 = rim;
                mask = ((_e1319 * 0.45f) + (_e1322 * 0.6f));
            }
            let _e1326: i32 = global.cosmetic_effect;
            let _e1329: i32 = global.cosmetic_effect;
            if ((_e1326 == 5i) || (_e1329 == 7i)) {
                let _e1339: f32 = wave;
                let _e1344: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1339) * 0.65f)) + (_e1344 * 0.45f));
            }
            let _e1348: i32 = global.cosmetic_effect;
            if (_e1348 == 6i) {
                let _e1352: vec2<f32> = texcoord_1;
                let _e1356: vec2<f32> = texcoord_1;
                let _e1360: f32 = t;
                let _e1362: vec2<f32> = texcoord_1;
                let _e1366: f32 = t;
                let _e1370: vec2<f32> = texcoord_1;
                let _e1374: vec2<f32> = texcoord_1;
                let _e1378: f32 = t;
                let _e1380: vec2<f32> = texcoord_1;
                let _e1384: f32 = t;
                let _e1389: vec2<f32> = texcoord_1;
                let _e1393: vec2<f32> = texcoord_1;
                let _e1397: f32 = t;
                let _e1399: vec2<f32> = texcoord_1;
                let _e1403: f32 = t;
                let _e1407: vec2<f32> = texcoord_1;
                let _e1411: vec2<f32> = texcoord_1;
                let _e1415: f32 = t;
                let _e1417: vec2<f32> = texcoord_1;
                let _e1421: f32 = t;
                let _e1428: vec2<f32> = texcoord_1;
                let _e1432: vec2<f32> = texcoord_1;
                let _e1436: f32 = t;
                let _e1438: vec2<f32> = texcoord_1;
                let _e1442: f32 = t;
                let _e1446: vec2<f32> = texcoord_1;
                let _e1450: vec2<f32> = texcoord_1;
                let _e1454: f32 = t;
                let _e1456: vec2<f32> = texcoord_1;
                let _e1460: f32 = t;
                let _e1465: vec2<f32> = texcoord_1;
                let _e1469: vec2<f32> = texcoord_1;
                let _e1473: f32 = t;
                let _e1475: vec2<f32> = texcoord_1;
                let _e1479: f32 = t;
                let _e1483: vec2<f32> = texcoord_1;
                let _e1487: vec2<f32> = texcoord_1;
                let _e1491: f32 = t;
                let _e1493: vec2<f32> = texcoord_1;
                let _e1497: f32 = t;
                let _e1506: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1483.x * 31f) + sin(((_e1493.y * 29f) + _e1497))))), 16f)) + (_e1506 * 0.5f));
            }
            let _e1510: i32 = global.cosmetic_effect;
            if (_e1510 == 8i) {
                let _e1513: f32 = wave;
                let _e1516: f32 = rim;
                mask = ((_e1513 * 0.35f) + (_e1516 * 0.7f));
            }
            let _e1520: i32 = global.cosmetic_effect;
            if (_e1520 == 9i) {
                let _e1525: vec2<f32> = texcoord_1;
                let _e1529: f32 = t;
                let _e1531: vec2<f32> = texcoord_1;
                let _e1535: f32 = t;
                let _e1540: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1531.x * 40f) + _e1535)))) * _e1540);
            }
            let _e1545: vec3<f32> = global.cosmetic_primary;
            let _e1546: vec3<f32> = global.cosmetic_secondary;
            let _e1547: f32 = wave;
            energy = mix(_e1545, _e1546, vec3(_e1547));
            let _e1554: f32 = mask;
            let _e1558: f32 = global.cosmetic_intensity;
            let _e1560: f32 = pulse;
            strength = ((clamp(_e1554, 0f, 1f) * _e1558) * _e1560);
            let _e1563: i32 = global.cosmetic_preserve_palette;
            if (_e1563 != 0i) {
                let _e1566: f32 = strength;
                strength = (_e1566 * 0.65f);
            }
            let _e1569: vec4<f32> = (*col);
            let _e1571: vec4<f32> = (*col);
            let _e1573: vec3<f32> = energy;
            let _e1575: f32 = lum;
            let _e1580: f32 = strength;
            let _e1585: f32 = strength;
            let _e1591: vec4<f32> = (*col);
            let _e1593: vec3<f32> = energy;
            let _e1595: f32 = lum;
            let _e1600: f32 = strength;
            let _e1605: f32 = strength;
            let _e1612: vec3<f32> = mix(_e1591.xyz, (_e1593 * (0.4f + (_e1595 * 0.6f))), vec3(clamp((_e1605 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1612.x;
            (*col).y = _e1612.y;
            (*col).z = _e1612.z;
            let _e1619: vec4<f32> = (*col);
            let _e1621: vec4<f32> = (*col);
            let _e1623: vec3<f32> = energy;
            let _e1624: f32 = strength;
            let _e1628: vec3<f32> = (_e1621.xyz + ((_e1623 * _e1624) * 0.45f));
            (*col).x = _e1628.x;
            (*col).y = _e1628.y;
            (*col).z = _e1628.z;
        }
    }
    let _e1635: f32 = global.cosmetic_dissolve;
    if (_e1635 > 0f) {
        {
            let _e1638: vec2<f32> = texcoord_1;
            let _e1641: vec2<f32> = texcoord_1;
            let _e1645: vec2<f32> = texcoord_1;
            let _e1648: vec2<f32> = texcoord_1;
            let _e1652: f32 = cosmetic_noise(floor((_e1648 * 64f)));
            n_2 = _e1652;
            let _e1654: f32 = n_2;
            let _e1655: f32 = global.cosmetic_dissolve;
            if (_e1654 < _e1655) {
                discard;
            }
            let _e1657: vec4<f32> = (*col);
            let _e1659: vec4<f32> = (*col);
            let _e1661: vec3<f32> = global.cosmetic_primary;
            let _e1664: f32 = global.cosmetic_dissolve;
            let _e1668: f32 = global.cosmetic_dissolve;
            let _e1669: f32 = global.cosmetic_dissolve;
            let _e1672: f32 = n_2;
            let _e1678: vec3<f32> = (_e1659.xyz + ((_e1661 * (1f - smoothstep(_e1668, (_e1669 + 0.08f), _e1672))) * 0.3f));
            (*col).x = _e1678.x;
            (*col).y = _e1678.y;
            (*col).z = _e1678.z;
            return;
        }
    } else {
        return;
    }
}

fn apply_material_lighting(col_1: ptr<function, vec4<f32>>) {
    var n_3: vec3<f32>;
    var d1_: f32;
    var d2_: f32;
    var l1_: f32;
    var l2_: f32;
    var local: vec4<f32>;
    var sm: vec4<f32>;
    var roughness: f32;
    var viewDir: vec3<f32> = vec3<f32>(0f, 0f, 1f);
    var h1_: vec3<f32>;
    var h2_: vec3<f32>;
    var exponent: f32;
    var highlight: f32;

    let _e336: i32 = global._prime_advanced_materials;
    let _e340: i32 = global._prime_use_light;
    if (!((_e336 != 0i)) || !((_e340 != 0i))) {
        return;
    }
    let _e345: vec3<f32> = mapped_normal();
    n_3 = _e345;
    let _e350: vec3<f32> = global.light1vec;
    let _e351: vec3<f32> = n_3;
    let _e357: vec3<f32> = global.light1vec;
    let _e358: vec3<f32> = n_3;
    d1_ = max(0f, -(dot(_e357, _e358)));
    let _e366: vec3<f32> = global.light2vec;
    let _e367: vec3<f32> = n_3;
    let _e373: vec3<f32> = global.light2vec;
    let _e374: vec3<f32> = n_3;
    d2_ = max(0f, -(dot(_e373, _e374)));
    let _e384: vec3<f32> = global.light1col;
    l1_ = dot(_e384, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e396: vec3<f32> = global.light2col;
    l2_ = dot(_e396, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e403: vec4<f32> = (*col_1);
    let _e405: vec4<f32> = (*col_1);
    let _e409: f32 = d1_;
    let _e410: f32 = l1_;
    let _e412: f32 = d2_;
    let _e413: f32 = l2_;
    let _e420: f32 = d1_;
    let _e421: f32 = l1_;
    let _e423: f32 = d2_;
    let _e424: f32 = l2_;
    let _e434: f32 = d1_;
    let _e435: f32 = l1_;
    let _e437: f32 = d2_;
    let _e438: f32 = l2_;
    let _e445: f32 = d1_;
    let _e446: f32 = l1_;
    let _e448: f32 = d2_;
    let _e449: f32 = l2_;
    let _e458: vec3<f32> = (_e405.xyz * mix(0.92f, 1.1f, clamp((((_e445 * _e446) + (_e448 * _e449)) * 0.65f), 0f, 1f)));
    (*col_1).x = _e458.x;
    (*col_1).y = _e458.y;
    (*col_1).z = _e458.z;
    let _e465: i32 = global._prime_use_specular_map;
    if (_e465 != 0i) {
        let _e469: vec2<f32> = texcoord_1;
        let _e470: vec4<f32> = prime_sample_specular_tex(_e469);
        local = _e470;
    } else {
        let _e471: vec3<f32> = global.specular;
        let _e473: vec3<f32> = global.specular;
        let _e475: vec3<f32> = global.specular;
        let _e477: vec3<f32> = global.specular;
        let _e480: vec3<f32> = global.specular;
        let _e482: vec3<f32> = global.specular;
        let _e484: vec3<f32> = global.specular;
        let _e486: vec3<f32> = global.specular;
        let _e488: vec3<f32> = global.specular;
        let _e491: vec3<f32> = global.specular;
        local = vec4<f32>(max(max(_e486.x, _e488.y), _e491.z), 0.55f, 0f, 1f);
    }
    let _e499: vec4<f32> = local;
    sm = _e499;
    let _e501: vec4<f32> = sm;
    let _e505: vec4<f32> = sm;
    roughness = clamp(_e505.y, 0.04f, 1f);
    let _e516: vec3<f32> = global.light1vec;
    let _e518: vec3<f32> = viewDir;
    let _e520: vec3<f32> = global.light1vec;
    let _e522: vec3<f32> = viewDir;
    h1_ = normalize((-(_e520) + _e522));
    let _e526: vec3<f32> = global.light2vec;
    let _e528: vec3<f32> = viewDir;
    let _e530: vec3<f32> = global.light2vec;
    let _e532: vec3<f32> = viewDir;
    h2_ = normalize((-(_e530) + _e532));
    let _e541: f32 = roughness;
    exponent = mix(72f, 4f, _e541);
    let _e546: vec3<f32> = n_3;
    let _e547: vec3<f32> = h1_;
    let _e552: vec3<f32> = n_3;
    let _e553: vec3<f32> = h1_;
    let _e560: vec3<f32> = n_3;
    let _e561: vec3<f32> = h1_;
    let _e566: vec3<f32> = n_3;
    let _e567: vec3<f32> = h1_;
    let _e571: f32 = exponent;
    let _e573: f32 = l1_;
    let _e577: vec3<f32> = n_3;
    let _e578: vec3<f32> = h2_;
    let _e583: vec3<f32> = n_3;
    let _e584: vec3<f32> = h2_;
    let _e591: vec3<f32> = n_3;
    let _e592: vec3<f32> = h2_;
    let _e597: vec3<f32> = n_3;
    let _e598: vec3<f32> = h2_;
    let _e602: f32 = exponent;
    let _e604: f32 = l2_;
    highlight = ((pow(max(dot(_e566, _e567), 0f), _e571) * _e573) + (pow(max(dot(_e597, _e598), 0f), _e602) * _e604));
    let _e608: vec4<f32> = (*col_1);
    let _e610: vec4<f32> = (*col_1);
    let _e612: f32 = highlight;
    let _e613: vec4<f32> = sm;
    let _e617: vec4<f32> = sm;
    let _e626: vec3<f32> = (_e610.xyz + vec3(((_e612 * clamp(_e617.x, 0f, 1f)) * 0.16f)));
    (*col_1).x = _e626.x;
    (*col_1).y = _e626.y;
    (*col_1).z = _e626.z;
    let _e633: i32 = global._prime_use_emissive_map;
    if (_e633 != 0i) {
        let _e636: vec4<f32> = (*col_1);
        let _e638: vec4<f32> = (*col_1);
        let _e641: vec2<f32> = texcoord_1;
        let _e642: vec4<f32> = prime_sample_emissive_tex(_e641);
        let _e646: vec3<f32> = (_e638.xyz + (_e642.xyz * 0.75f));
        (*col_1).x = _e646.x;
        (*col_1).y = _e646.y;
        (*col_1).z = _e646.z;
        return;
    } else {
        return;
    }
}

fn toon_color(vtx_color: vec4<f32>) -> vec4<f32> {
    var vtx_color_1: vec4<f32>;

    vtx_color_1 = vtx_color;
    let _e337: vec4<f32> = vtx_color_1;
    let _e343: vec4<f32> = vtx_color_1;
    let _e352: vec3<f32> = global.toon_table[i32(clamp((_e343.x * 31f), 0f, 31f))];
    let _e353: vec4<f32> = vtx_color_1;
    return vec4<f32>(_e352.x, _e352.y, _e352.z, _e353.w);
}

fn cel_shade(c: vec3<f32>) -> vec3<f32> {
    var c_1: vec3<f32>;
    var steps: f32;
    var lum_1: f32;
    var scaled: f32;
    var lower: f32;
    var level: f32;
    var banded: vec3<f32>;
    var grey: f32;

    c_1 = c;
    let _e337: i32 = global.cel_bands;
    steps = f32(_e337);
    let _e340: vec3<f32> = c_1;
    let _e342: vec3<f32> = c_1;
    let _e344: vec3<f32> = c_1;
    let _e346: vec3<f32> = c_1;
    let _e349: vec3<f32> = c_1;
    let _e351: vec3<f32> = c_1;
    let _e353: vec3<f32> = c_1;
    let _e355: vec3<f32> = c_1;
    let _e357: vec3<f32> = c_1;
    let _e360: vec3<f32> = c_1;
    lum_1 = max(max(_e355.x, _e357.y), _e360.z);
    let _e364: f32 = lum_1;
    if (_e364 <= 0f) {
        {
            let _e367: vec3<f32> = c_1;
            return _e367;
        }
    }
    let _e368: f32 = lum_1;
    let _e369: f32 = steps;
    scaled = ((_e368 * _e369) - 0.5f);
    let _e375: f32 = scaled;
    lower = floor(_e375);
    let _e378: f32 = lower;
    let _e383: f32 = scaled;
    let _e384: f32 = lower;
    let _e388: f32 = scaled;
    let _e389: f32 = lower;
    let _e393: f32 = steps;
    level = (((_e378 + 0.5f) + smoothstep(0.46f, 0.54f, (_e388 - _e389))) / _e393);
    let _e396: vec3<f32> = c_1;
    let _e397: f32 = level;
    let _e398: f32 = lum_1;
    banded = (_e396 * (_e397 / _e398));
    let _e407: vec3<f32> = banded;
    grey = dot(_e407, vec3<f32>(0.299f, 0.587f, 0.114f));
    let _e414: f32 = grey;
    let _e418: f32 = grey;
    let _e420: vec3<f32> = banded;
    let _e426: f32 = grey;
    let _e430: f32 = grey;
    let _e432: vec3<f32> = banded;
    return clamp(mix(vec3(_e430), _e432, vec3(1.35f)), vec3(0f), vec3(1f));
}

fn prime_original_main() {
    var col_2: vec4<f32>;
    var local_1: vec4<f32>;
    var texcolor: vec4<f32>;
    var toon: vec4<f32>;
    var detail: f32;
    var tinted: vec3<f32>;
    var local_2: vec4<f32>;
    var depth: f32;
    var density: f32 = 0f;

    let _e336: i32 = global._prime_use_texture;
    if (_e336 != 0i) {
        {
            let _e339: i32 = global._prime_use_pal_override;
            if (_e339 != 0i) {
                let _e342: vec4<f32> = global.pal_override_color;
                let _e343: vec3<f32> = _e342.xyz;
                let _e345: vec2<f32> = texcoord_1;
                let _e346: vec4<f32> = prime_sample_tex(_e345);
                local_1 = vec4<f32>(_e343.x, _e343.y, _e343.z, _e346.w);
            } else {
                let _e353: vec2<f32> = texcoord_1;
                let _e354: vec4<f32> = prime_sample_tex(_e353);
                local_1 = _e354;
            }
            let _e356: vec4<f32> = local_1;
            texcolor = _e356;
            let _e358: i32 = global._prime_use_flat;
            let _e361: i32 = global._prime_use_pal_override;
            let _e366: i32 = global.textured_player_skin;
            if (((_e358 != 0i) && !((_e361 != 0i))) && (_e366 == 0i)) {
                {
                    let _e370: vec4<f32> = texcolor;
                    let _e372: vec3<f32> = global.flat_color;
                    texcolor.x = _e372.x;
                    texcolor.y = _e372.y;
                    texcolor.z = _e372.z;
                }
            }
            let _e379: i32 = global.mat_mode;
            if (_e379 == 1i) {
                {
                    let _e382: vec4<f32> = texcolor;
                    let _e384: vec4<f32> = texcolor;
                    let _e387: vec4<f32> = color_1;
                    let _e390: vec4<f32> = texcolor;
                    let _e396: vec4<f32> = texcolor;
                    let _e398: vec4<f32> = texcolor;
                    let _e401: vec4<f32> = color_1;
                    let _e404: vec4<f32> = texcolor;
                    let _e410: vec4<f32> = texcolor;
                    let _e412: vec4<f32> = texcolor;
                    let _e415: vec4<f32> = color_1;
                    let _e418: vec4<f32> = texcolor;
                    let _e424: f32 = global.mat_alpha;
                    let _e425: vec4<f32> = color_1;
                    col_2 = vec4<f32>(((_e382.x * _e384.w) + (_e387.x * (1f - _e390.w))), ((_e396.y * _e398.w) + (_e401.y * (1f - _e404.w))), ((_e410.z * _e412.w) + (_e415.z * (1f - _e418.w))), (_e424 * _e425.w));
                }
            } else {
                let _e429: i32 = global.mat_mode;
                if (_e429 == 2i) {
                    {
                        let _e433: vec4<f32> = color_1;
                        let _e434: vec4<f32> = toon_color(_e433);
                        toon = _e434;
                        let _e436: vec4<f32> = texcolor;
                        let _e438: vec4<f32> = color_1;
                        let _e441: vec4<f32> = toon;
                        let _e443: vec3<f32> = ((_e436.xyz * _e438.x) + _e441.xyz);
                        let _e444: f32 = global.mat_alpha;
                        let _e445: vec4<f32> = texcolor;
                        let _e448: vec4<f32> = color_1;
                        col_2 = vec4<f32>(_e443.x, _e443.y, _e443.z, ((_e444 * _e445.w) * _e448.w));
                    }
                } else {
                    {
                        let _e455: vec4<f32> = color_1;
                        let _e456: vec4<f32> = texcolor;
                        let _e457: vec3<f32> = _e456.xyz;
                        let _e458: f32 = global.mat_alpha;
                        let _e459: vec4<f32> = texcolor;
                        col_2 = (_e455 * vec4<f32>(_e457.x, _e457.y, _e457.z, (_e458 * _e459.w)));
                    }
                }
            }
            let _e467: i32 = global._prime_use_override;
            if (_e467 != 0i) {
                {
                    let _e470: i32 = global.textured_player_skin;
                    if (_e470 > 0i) {
                        {
                            let _e473: i32 = global.textured_player_skin;
                            if (_e473 == 2i) {
                                {
                                    let _e478: vec4<f32> = texcolor;
                                    let _e484: vec4<f32> = texcolor;
                                    let _e493: vec4<f32> = texcolor;
                                    let _e499: vec4<f32> = texcolor;
                                    detail = smoothstep(0.05f, 0.85f, dot(_e499.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f)));
                                    let _e508: vec4<f32> = global.override_color;
                                    let _e512: f32 = detail;
                                    tinted = (_e508.xyz * (0.25f + (0.75f * _e512)));
                                    let _e517: vec4<f32> = col_2;
                                    let _e520: vec4<f32> = texcolor;
                                    let _e524: vec4<f32> = texcolor;
                                    let _e530: vec3<f32> = tinted;
                                    let _e531: vec4<f32> = texcolor;
                                    let _e535: vec4<f32> = texcolor;
                                    let _e542: vec3<f32> = mix(_e530, pow(_e535.xyz, vec3(0.7f)), vec3(0.25f));
                                    col_2.x = _e542.x;
                                    col_2.y = _e542.y;
                                    col_2.z = _e542.z;
                                }
                            } else {
                                {
                                    let _e549: vec4<f32> = col_2;
                                    let _e551: vec4<f32> = col_2;
                                    let _e553: vec4<f32> = texcolor;
                                    let _e556: vec4<f32> = col_2;
                                    let _e558: vec4<f32> = texcolor;
                                    let _e567: vec4<f32> = col_2;
                                    let _e569: vec4<f32> = texcolor;
                                    let _e572: vec4<f32> = col_2;
                                    let _e574: vec4<f32> = texcolor;
                                    let _e585: vec3<f32> = clamp((mix(_e572.xyz, _e574.xyz, vec3(0.8f)) * 1.25f), vec3(0f), vec3(1f));
                                    col_2.x = _e585.x;
                                    col_2.y = _e585.y;
                                    col_2.z = _e585.z;
                                }
                            }
                        }
                    } else {
                        {
                            let _e592: vec4<f32> = col_2;
                            let _e594: vec4<f32> = global.override_color;
                            let _e595: vec3<f32> = _e594.xyz;
                            col_2.x = _e595.x;
                            col_2.y = _e595.y;
                            col_2.z = _e595.z;
                        }
                    }
                    let _e603: vec4<f32> = col_2;
                    let _e605: vec4<f32> = global.override_color;
                    col_2.w = (_e603.w * _e605.w);
                }
            }
        }
    } else {
        let _e608: i32 = global._prime_use_override;
        if (_e608 != 0i) {
            {
                let _e611: vec4<f32> = global.override_color;
                col_2 = _e611;
            }
        } else {
            {
                let _e612: i32 = global.mat_mode;
                if (_e612 == 2i) {
                    let _e616: vec4<f32> = color_1;
                    let _e617: vec4<f32> = toon_color(_e616);
                    local_2 = _e617;
                } else {
                    let _e618: vec4<f32> = color_1;
                    local_2 = _e618;
                }
                let _e620: vec4<f32> = local_2;
                col_2 = _e620;
                let _e622: vec4<f32> = col_2;
                let _e624: f32 = global.mat_alpha;
                col_2.w = (_e622.w * _e624);
            }
        }
    }
    apply_material_lighting((&col_2));
    apply_cosmetics((&col_2));
    let _e630: i32 = global._prime_player_outline_mask;
    if (_e630 != 0i) {
        {
            let _e633: vec4<f32> = col_2;
            if (_e633.w <= 0.01f) {
                discard;
            }
            let _e637: vec4<f32> = col_2;
            let _e639: vec3<f32> = global.player_outline_color;
            col_2.x = _e639.x;
            col_2.y = _e639.y;
            col_2.z = _e639.z;
        }
    }
    let _e646: i32 = global.cel_bands;
    if (_e646 > 0i) {
        {
            let _e649: vec4<f32> = col_2;
            let _e651: vec4<f32> = col_2;
            let _e653: vec4<f32> = col_2;
            let _e655: vec3<f32> = cel_shade(_e653.xyz);
            col_2.x = _e655.x;
            col_2.y = _e655.y;
            col_2.z = _e655.z;
        }
    }
    let _e662: i32 = global._prime_fog_enable;
    if (_e662 != 0i) {
        {
            let _e666: vec4<f32> = gl_FragCoord;
            depth = _e666.z;
            let _e671: f32 = depth;
            let _e672: f32 = global.fog_max;
            if (_e671 >= _e672) {
                {
                    density = 1f;
                }
            } else {
                let _e675: f32 = depth;
                let _e676: f32 = global.fog_min;
                if (_e675 > _e676) {
                    {
                        let _e678: f32 = depth;
                        let _e679: f32 = global.fog_min;
                        let _e681: f32 = global.fog_max;
                        let _e682: f32 = global.fog_min;
                        density = ((((_e678 - _e679) / (_e681 - _e682)) * 124f) / 128f);
                    }
                }
            }
            let _e689: vec4<f32> = col_2;
            let _e691: f32 = density;
            let _e694: vec4<f32> = global.fog_color;
            let _e695: f32 = density;
            let _e698: vec3<f32> = ((_e689 * (1f - _e691)) + (_e694 * _e695)).xyz;
            let _e699: vec4<f32> = col_2;
            col_2 = vec4<f32>(_e698.x, _e698.y, _e698.z, _e699.w);
        }
    }
    let _e705: vec4<f32> = col_2;
    prime_output = _e705;
    return;
}

fn main_1() {
    var a: f32;
    var r: f32;
    var keep: bool;

    prime_original_main();
    let _e336: vec4<f32> = prime_output;
    a = _e336.w;
    let _e339: f32 = global.prime_alpha_ref;
    r = _e339;
    let _e341: i32 = global.prime_alpha_func;
    let _e344: i32 = global.prime_alpha_func;
    let _e347: f32 = a;
    let _e348: f32 = r;
    let _e352: i32 = global.prime_alpha_func;
    let _e355: f32 = a;
    let _e356: f32 = r;
    let _e360: i32 = global.prime_alpha_func;
    let _e363: f32 = a;
    let _e364: f32 = r;
    let _e368: i32 = global.prime_alpha_func;
    let _e371: f32 = a;
    let _e372: f32 = r;
    let _e376: i32 = global.prime_alpha_func;
    let _e379: f32 = a;
    let _e380: f32 = r;
    let _e384: i32 = global.prime_alpha_func;
    let _e387: f32 = a;
    let _e388: f32 = r;
    keep = (((((((_e341 == 519i) || ((_e344 == 513i) && (_e347 < _e348))) || ((_e352 == 514i) && (_e355 == _e356))) || ((_e360 == 515i) && (_e363 <= _e364))) || ((_e368 == 516i) && (_e371 > _e372))) || ((_e376 == 517i) && (_e379 != _e380))) || ((_e384 == 518i) && (_e387 >= _e388)));
    let _e393: bool = keep;
    if !(_e393) {
        discard;
    } else {
        return;
    }
}

@fragment
fn main(@location(0) texcoord: vec2<f32>, @location(1) color: vec4<f32>, @location(2) surface_normal: vec3<f32>, @location(3) surface_position: vec3<f32>, @builtin(position) param: vec4<f32>) -> FragmentOutput {
    texcoord_1 = texcoord;
    color_1 = color;
    surface_normal_1 = surface_normal;
    surface_position_1 = surface_position;
    gl_FragCoord = param;
    main_1();
    let _e359: vec4<f32> = prime_output;
    return FragmentOutput(_e359);
}
