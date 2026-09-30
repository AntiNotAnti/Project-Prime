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
    mtx_stack: array<mat4x4<f32>, 32>,
    fog_min: f32,
    _pad_2560_0_: f32,
    _pad_2560_1_: f32,
    _pad_2560_2_: f32,
    fog_max: f32,
    _pad_2576_0_: f32,
    _pad_2576_1_: f32,
    _pad_2576_2_: f32,
    _prime_advanced_materials: i32,
    _pad_2592_0_: f32,
    _pad_2592_1_: f32,
    _pad_2592_2_: f32,
    _prime_use_normal_map: i32,
    _pad_2608_0_: f32,
    _pad_2608_1_: f32,
    _pad_2608_2_: f32,
    _prime_use_specular_map: i32,
    _pad_2624_0_: f32,
    _pad_2624_1_: f32,
    _pad_2624_2_: f32,
    _prime_use_emissive_map: i32,
    _pad_2640_0_: f32,
    _pad_2640_1_: f32,
    _pad_2640_2_: f32,
    _prime_use_override: i32,
    _pad_2656_0_: f32,
    _pad_2656_1_: f32,
    _pad_2656_2_: f32,
    textured_player_skin: i32,
    _pad_2672_0_: f32,
    _pad_2672_1_: f32,
    _pad_2672_2_: f32,
    _prime_player_outline_mask: i32,
    _pad_2688_0_: f32,
    _pad_2688_1_: f32,
    _pad_2688_2_: f32,
    player_outline_color: vec3<f32>,
    _pad_2704_0_: f32,
    override_color: vec4<f32>,
    _prime_use_pal_override: i32,
    _pad_2736_0_: f32,
    _pad_2736_1_: f32,
    _pad_2736_2_: f32,
    pal_override_color: vec4<f32>,
    mat_alpha: f32,
    _pad_2768_0_: f32,
    _pad_2768_1_: f32,
    _pad_2768_2_: f32,
    mat_mode: i32,
    _pad_2784_0_: f32,
    _pad_2784_1_: f32,
    _pad_2784_2_: f32,
    toon_table: array<vec3<f32>, 32>,
    cel_bands: i32,
    _pad_3312_0_: f32,
    _pad_3312_1_: f32,
    _pad_3312_2_: f32,
    _prime_use_flat: i32,
    _pad_3328_0_: f32,
    _pad_3328_1_: f32,
    _pad_3328_2_: f32,
    flat_color: vec3<f32>,
    _pad_3344_0_: f32,
    cosmetic_skin: i32,
    _pad_3360_0_: f32,
    _pad_3360_1_: f32,
    _pad_3360_2_: f32,
    cosmetic_preserve_palette: i32,
    _pad_3376_0_: f32,
    _pad_3376_1_: f32,
    _pad_3376_2_: f32,
    cosmetic_effect: i32,
    _pad_3392_0_: f32,
    _pad_3392_1_: f32,
    _pad_3392_2_: f32,
    cosmetic_time: f32,
    _pad_3408_0_: f32,
    _pad_3408_1_: f32,
    _pad_3408_2_: f32,
    cosmetic_primary: vec3<f32>,
    _pad_3424_0_: f32,
    cosmetic_secondary: vec3<f32>,
    _pad_3440_0_: f32,
    cosmetic_intensity: f32,
    _pad_3456_0_: f32,
    _pad_3456_1_: f32,
    _pad_3456_2_: f32,
    cosmetic_pulse: f32,
    _pad_3472_0_: f32,
    _pad_3472_1_: f32,
    _pad_3472_2_: f32,
    cosmetic_scroll: f32,
    _pad_3488_0_: f32,
    _pad_3488_1_: f32,
    _pad_3488_2_: f32,
    cosmetic_dissolve: f32,
    _pad_3504_0_: f32,
    _pad_3504_1_: f32,
    _pad_3504_2_: f32,
    prime_viewport: vec4<f32>,
    prime_imm_color: vec4<f32>,
    prime_imm_normal: vec4<f32>,
    prime_alpha_func: i32,
    _pad_3568_0_: f32,
    _pad_3568_1_: f32,
    _pad_3568_2_: f32,
    prime_alpha_ref: f32,
    _pad_3584_0_: f32,
    _pad_3584_1_: f32,
    _pad_3584_2_: f32,
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
    let _e318: vec2<f32> = uv_1;
    let _e320: vec2<f32> = uv_1;
    let _e323: vec2<f32> = uv_1;
    let _e328: vec4<f32> = global.prime_texture_flip[0];
    let _e330: vec2<f32> = uv_1;
    let _e333: vec2<f32> = uv_1;
    let _e338: vec4<f32> = global.prime_texture_flip[0];
    let _e342: vec2<f32> = uv_1;
    let _e344: vec2<f32> = uv_1;
    let _e347: vec2<f32> = uv_1;
    let _e352: vec4<f32> = global.prime_texture_flip[0];
    let _e354: vec2<f32> = uv_1;
    let _e357: vec2<f32> = uv_1;
    let _e362: vec4<f32> = global.prime_texture_flip[0];
    let _e366: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e342.x, mix(_e354.y, (1f - _e357.y), _e362.x)));
    return _e366;
}

fn prime_sample_normal_tex(uv_2: vec2<f32>) -> vec4<f32> {
    var uv_3: vec2<f32>;

    uv_3 = uv_2;
    let _e320: vec2<f32> = uv_3;
    let _e322: vec2<f32> = uv_3;
    let _e325: vec2<f32> = uv_3;
    let _e330: vec4<f32> = global.prime_texture_flip[1];
    let _e332: vec2<f32> = uv_3;
    let _e335: vec2<f32> = uv_3;
    let _e340: vec4<f32> = global.prime_texture_flip[1];
    let _e344: vec2<f32> = uv_3;
    let _e346: vec2<f32> = uv_3;
    let _e349: vec2<f32> = uv_3;
    let _e354: vec4<f32> = global.prime_texture_flip[1];
    let _e356: vec2<f32> = uv_3;
    let _e359: vec2<f32> = uv_3;
    let _e364: vec4<f32> = global.prime_texture_flip[1];
    let _e368: vec4<f32> = textureSample(prime_tex_normal_tex, prime_sampler_normal_tex, vec2<f32>(_e344.x, mix(_e356.y, (1f - _e359.y), _e364.x)));
    return _e368;
}

fn prime_sample_specular_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e322: vec2<f32> = uv_5;
    let _e324: vec2<f32> = uv_5;
    let _e327: vec2<f32> = uv_5;
    let _e332: vec4<f32> = global.prime_texture_flip[2];
    let _e334: vec2<f32> = uv_5;
    let _e337: vec2<f32> = uv_5;
    let _e342: vec4<f32> = global.prime_texture_flip[2];
    let _e346: vec2<f32> = uv_5;
    let _e348: vec2<f32> = uv_5;
    let _e351: vec2<f32> = uv_5;
    let _e356: vec4<f32> = global.prime_texture_flip[2];
    let _e358: vec2<f32> = uv_5;
    let _e361: vec2<f32> = uv_5;
    let _e366: vec4<f32> = global.prime_texture_flip[2];
    let _e370: vec4<f32> = textureSample(prime_tex_specular_tex, prime_sampler_specular_tex, vec2<f32>(_e346.x, mix(_e358.y, (1f - _e361.y), _e366.x)));
    return _e370;
}

fn prime_sample_emissive_tex(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e324: vec2<f32> = uv_7;
    let _e326: vec2<f32> = uv_7;
    let _e329: vec2<f32> = uv_7;
    let _e334: vec4<f32> = global.prime_texture_flip[3];
    let _e336: vec2<f32> = uv_7;
    let _e339: vec2<f32> = uv_7;
    let _e344: vec4<f32> = global.prime_texture_flip[3];
    let _e348: vec2<f32> = uv_7;
    let _e350: vec2<f32> = uv_7;
    let _e353: vec2<f32> = uv_7;
    let _e358: vec4<f32> = global.prime_texture_flip[3];
    let _e360: vec2<f32> = uv_7;
    let _e363: vec2<f32> = uv_7;
    let _e368: vec4<f32> = global.prime_texture_flip[3];
    let _e372: vec4<f32> = textureSample(prime_tex_emissive_tex, prime_sampler_emissive_tex, vec2<f32>(_e348.x, mix(_e360.y, (1f - _e363.y), _e368.x)));
    return _e372;
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

    let _e328: vec3<f32> = surface_normal_1;
    n = normalize(_e328);
    let _e331: i32 = global._prime_use_normal_map;
    if !((_e331 != 0i)) {
        let _e335: vec3<f32> = n;
        return _e335;
    }
    let _e337: vec3<f32> = surface_position_1;
    let _e338: vec3<f32> = dpdx(_e337);
    dp1_ = _e338;
    let _e341: vec3<f32> = surface_position_1;
    let _e342: vec3<f32> = dpdy(_e341);
    dp2_ = _e342;
    let _e345: vec2<f32> = texcoord_1;
    let _e346: vec2<f32> = dpdx(_e345);
    duv1_ = _e346;
    let _e349: vec2<f32> = texcoord_1;
    let _e350: vec2<f32> = dpdy(_e349);
    duv2_ = _e350;
    let _e352: vec2<f32> = duv1_;
    let _e354: vec2<f32> = duv2_;
    let _e357: vec2<f32> = duv1_;
    let _e359: vec2<f32> = duv2_;
    det = ((_e352.x * _e354.y) - (_e357.y * _e359.x));
    let _e365: f32 = det;
    if (abs(_e365) < 0.000001f) {
        let _e369: vec3<f32> = n;
        return _e369;
    }
    let _e370: vec3<f32> = dp1_;
    let _e371: vec2<f32> = duv2_;
    let _e374: vec3<f32> = dp2_;
    let _e375: vec2<f32> = duv1_;
    let _e379: f32 = det;
    let _e382: vec3<f32> = dp1_;
    let _e383: vec2<f32> = duv2_;
    let _e386: vec3<f32> = dp2_;
    let _e387: vec2<f32> = duv1_;
    let _e391: f32 = det;
    tangent = normalize((((_e382 * _e383.y) - (_e386 * _e387.y)) / vec3(_e391)));
    let _e396: vec3<f32> = dp1_;
    let _e398: vec2<f32> = duv2_;
    let _e401: vec3<f32> = dp2_;
    let _e402: vec2<f32> = duv1_;
    let _e406: f32 = det;
    let _e409: vec3<f32> = dp1_;
    let _e411: vec2<f32> = duv2_;
    let _e414: vec3<f32> = dp2_;
    let _e415: vec2<f32> = duv1_;
    let _e419: f32 = det;
    bitangent = normalize((((-(_e409) * _e411.x) + (_e414 * _e415.x)) / vec3(_e419)));
    let _e425: vec2<f32> = texcoord_1;
    let _e426: vec4<f32> = prime_sample_normal_tex(_e425);
    mapNormal = ((_e426.xyz * 2f) - vec3(1f));
    let _e434: vec3<f32> = tangent;
    let _e435: vec3<f32> = mapNormal;
    let _e438: vec3<f32> = bitangent;
    let _e439: vec3<f32> = mapNormal;
    let _e443: vec3<f32> = n;
    let _e444: vec3<f32> = mapNormal;
    let _e448: vec3<f32> = tangent;
    let _e449: vec3<f32> = mapNormal;
    let _e452: vec3<f32> = bitangent;
    let _e453: vec3<f32> = mapNormal;
    let _e457: vec3<f32> = n;
    let _e458: vec3<f32> = mapNormal;
    return normalize((((_e448 * _e449.x) + (_e452 * _e453.y)) + (_e457 * _e458.z)));
}

fn cosmetic_noise(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e333: vec2<f32> = p_1;
    let _e342: vec2<f32> = p_1;
    let _e354: vec2<f32> = p_1;
    let _e363: vec2<f32> = p_1;
    return fract((sin(dot(_e363, vec2<f32>(127.1f, 311.7f))) * 43758.547f));
}

fn cosmetic_soft_noise(p_2: vec2<f32>) -> f32 {
    var p_3: vec2<f32>;
    var cell: vec2<f32>;
    var f: vec2<f32>;

    p_3 = p_2;
    let _e330: vec2<f32> = p_3;
    cell = floor(_e330);
    let _e334: vec2<f32> = p_3;
    f = fract(_e334);
    let _e337: vec2<f32> = f;
    let _e338: vec2<f32> = f;
    let _e342: vec2<f32> = f;
    f = ((_e337 * _e338) * (vec2(3f) - (2f * _e342)));
    let _e348: vec2<f32> = cell;
    let _e349: f32 = cosmetic_noise(_e348);
    let _e350: vec2<f32> = cell;
    let _e355: vec2<f32> = cell;
    let _e360: f32 = cosmetic_noise((_e355 + vec2<f32>(1f, 0f)));
    let _e361: vec2<f32> = f;
    let _e364: vec2<f32> = cell;
    let _e365: f32 = cosmetic_noise(_e364);
    let _e366: vec2<f32> = cell;
    let _e371: vec2<f32> = cell;
    let _e376: f32 = cosmetic_noise((_e371 + vec2<f32>(1f, 0f)));
    let _e377: vec2<f32> = f;
    let _e380: vec2<f32> = cell;
    let _e385: vec2<f32> = cell;
    let _e390: f32 = cosmetic_noise((_e385 + vec2<f32>(0f, 1f)));
    let _e391: vec2<f32> = cell;
    let _e395: vec2<f32> = cell;
    let _e399: f32 = cosmetic_noise((_e395 + vec2(1f)));
    let _e400: vec2<f32> = f;
    let _e402: vec2<f32> = cell;
    let _e407: vec2<f32> = cell;
    let _e412: f32 = cosmetic_noise((_e407 + vec2<f32>(0f, 1f)));
    let _e413: vec2<f32> = cell;
    let _e417: vec2<f32> = cell;
    let _e421: f32 = cosmetic_noise((_e417 + vec2(1f)));
    let _e422: vec2<f32> = f;
    let _e425: vec2<f32> = f;
    let _e428: vec2<f32> = cell;
    let _e429: f32 = cosmetic_noise(_e428);
    let _e430: vec2<f32> = cell;
    let _e435: vec2<f32> = cell;
    let _e440: f32 = cosmetic_noise((_e435 + vec2<f32>(1f, 0f)));
    let _e441: vec2<f32> = f;
    let _e444: vec2<f32> = cell;
    let _e445: f32 = cosmetic_noise(_e444);
    let _e446: vec2<f32> = cell;
    let _e451: vec2<f32> = cell;
    let _e456: f32 = cosmetic_noise((_e451 + vec2<f32>(1f, 0f)));
    let _e457: vec2<f32> = f;
    let _e460: vec2<f32> = cell;
    let _e465: vec2<f32> = cell;
    let _e470: f32 = cosmetic_noise((_e465 + vec2<f32>(0f, 1f)));
    let _e471: vec2<f32> = cell;
    let _e475: vec2<f32> = cell;
    let _e479: f32 = cosmetic_noise((_e475 + vec2(1f)));
    let _e480: vec2<f32> = f;
    let _e482: vec2<f32> = cell;
    let _e487: vec2<f32> = cell;
    let _e492: f32 = cosmetic_noise((_e487 + vec2<f32>(0f, 1f)));
    let _e493: vec2<f32> = cell;
    let _e497: vec2<f32> = cell;
    let _e501: f32 = cosmetic_noise((_e497 + vec2(1f)));
    let _e502: vec2<f32> = f;
    let _e505: vec2<f32> = f;
    return mix(mix(_e445, _e456, _e457.x), mix(_e492, _e501, _e502.x), _e505.y);
}

fn cosmetic_finish() -> vec2<f32> {
    let _e327: i32 = global.cosmetic_skin;
    if (_e327 == 1i) {
        return vec2<f32>(0.78f, 0.3f);
    }
    let _e333: i32 = global.cosmetic_skin;
    if (_e333 == 2i) {
        return vec2<f32>(0.85f, 0.38f);
    }
    let _e339: i32 = global.cosmetic_skin;
    if (_e339 == 3i) {
        return vec2<f32>(0.05f, 0.24f);
    }
    let _e345: i32 = global.cosmetic_skin;
    if (_e345 == 4i) {
        return vec2<f32>(0.55f, 0.42f);
    }
    let _e351: i32 = global.cosmetic_skin;
    if (_e351 == 5i) {
        return vec2<f32>(0.12f, 0.68f);
    }
    return vec2<f32>(0.35f, 0.28f);
}

fn cosmetic_circuit() -> f32 {
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var travel: f32;

    let _e327: vec2<f32> = texcoord_1;
    let _e330: vec2<f32> = texcoord_1;
    grid = fract((_e330 * 18f));
    let _e338: vec2<f32> = grid;
    let _e340: vec2<f32> = grid;
    let _e342: vec2<f32> = grid;
    let _e344: vec2<f32> = grid;
    let _e349: vec2<f32> = grid;
    let _e351: vec2<f32> = grid;
    let _e353: vec2<f32> = grid;
    let _e355: vec2<f32> = grid;
    trace = (1f - smoothstep(0.035f, 0.09f, min(_e353.x, _e355.y)));
    let _e364: vec2<f32> = grid;
    let _e368: vec2<f32> = grid;
    let _e375: vec2<f32> = grid;
    let _e379: vec2<f32> = grid;
    node = (1f - smoothstep(0.08f, 0.16f, length((_e379 - vec2(0.15f)))));
    let _e389: vec2<f32> = texcoord_1;
    let _e391: vec2<f32> = texcoord_1;
    let _e396: f32 = global.cosmetic_time;
    let _e400: vec2<f32> = texcoord_1;
    let _e402: vec2<f32> = texcoord_1;
    let _e407: f32 = global.cosmetic_time;
    let _e417: vec2<f32> = texcoord_1;
    let _e419: vec2<f32> = texcoord_1;
    let _e424: f32 = global.cosmetic_time;
    let _e428: vec2<f32> = texcoord_1;
    let _e430: vec2<f32> = texcoord_1;
    let _e435: f32 = global.cosmetic_time;
    travel = pow((0.5f + (0.5f * sin((((_e428.x + _e430.y) * 32f) - (_e435 * 1.8f))))), 8f);
    let _e445: f32 = trace;
    let _e447: f32 = travel;
    let _e452: f32 = node;
    let _e455: f32 = trace;
    let _e457: f32 = travel;
    let _e462: f32 = node;
    return max((_e455 * (0.3f + (_e457 * 0.7f))), (_e462 * 0.75f));
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

    let _e328: i32 = global.cosmetic_skin;
    let _e331: i32 = global.cosmetic_effect;
    let _e335: f32 = global.cosmetic_dissolve;
    if (((_e328 == 0i) && (_e331 == 0i)) && (_e335 <= 0f)) {
        return;
    }
    let _e339: vec4<f32> = (*col);
    nativeColor = _e339.xyz;
    let _e342: vec4<f32> = (*col);
    let _e348: vec4<f32> = (*col);
    lum = dot(_e348.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e356: vec3<f32> = surface_normal_1;
    let _e359: vec3<f32> = surface_normal_1;
    let _e360: vec3<f32> = surface_normal_1;
    let _e365: vec3<f32> = surface_normal_1;
    let _e366: vec3<f32> = surface_normal_1;
    let _e372: vec3<f32> = surface_normal_1;
    let _e373: vec3<f32> = surface_normal_1;
    let _e378: vec3<f32> = surface_normal_1;
    let _e379: vec3<f32> = surface_normal_1;
    n_1 = (_e356 * inverseSqrt(max(dot(_e378, _e379), 0.0001f)));
    let _e386: mat4x4<f32> = global.view_mtx;
    let _e396: vec3<f32> = n_1;
    viewNormal = (mat3x3<f32>(_e386[0].xyz, _e386[1].xyz, _e386[2].xyz) * _e396);
    let _e399: mat4x4<f32> = global.view_mtx;
    let _e400: vec3<f32> = surface_position_1;
    toEye = -((_e399 * vec4<f32>(_e400.x, _e400.y, _e400.z, 1f)).xyz);
    let _e410: vec3<f32> = toEye;
    let _e413: vec3<f32> = toEye;
    let _e414: vec3<f32> = toEye;
    let _e419: vec3<f32> = toEye;
    let _e420: vec3<f32> = toEye;
    let _e426: vec3<f32> = toEye;
    let _e427: vec3<f32> = toEye;
    let _e432: vec3<f32> = toEye;
    let _e433: vec3<f32> = toEye;
    toEye = (_e410 * inverseSqrt(max(dot(_e432, _e433), 0.0001f)));
    let _e442: vec3<f32> = viewNormal;
    let _e443: vec3<f32> = toEye;
    let _e447: vec3<f32> = viewNormal;
    let _e448: vec3<f32> = toEye;
    let _e457: vec3<f32> = viewNormal;
    let _e458: vec3<f32> = toEye;
    let _e462: vec3<f32> = viewNormal;
    let _e463: vec3<f32> = toEye;
    let _e474: vec3<f32> = viewNormal;
    let _e475: vec3<f32> = toEye;
    let _e479: vec3<f32> = viewNormal;
    let _e480: vec3<f32> = toEye;
    let _e489: vec3<f32> = viewNormal;
    let _e490: vec3<f32> = toEye;
    let _e494: vec3<f32> = viewNormal;
    let _e495: vec3<f32> = toEye;
    rim = pow(clamp((1f - abs(dot(_e494, _e495))), 0f, 1f), 2.2f);
    let _e505: i32 = global.cosmetic_skin;
    if (_e505 == 1i) {
        {
            let _e508: vec4<f32> = (*col);
            let _e510: vec4<f32> = (*col);
            let _e517: f32 = lum;
            let _e521: vec4<f32> = (*col);
            let _e528: f32 = lum;
            let _e533: vec3<f32> = mix(_e521.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e528)), vec3(0.88f));
            (*col).x = _e533.x;
            (*col).y = _e533.y;
            (*col).z = _e533.z;
        }
    } else {
        let _e540: i32 = global.cosmetic_skin;
        if (_e540 == 2i) {
            {
                let _e545: vec2<f32> = texcoord_1;
                let _e549: vec2<f32> = texcoord_1;
                let _e554: vec2<f32> = texcoord_1;
                let _e558: vec2<f32> = texcoord_1;
                let _e566: vec2<f32> = texcoord_1;
                let _e570: vec2<f32> = texcoord_1;
                let _e575: vec2<f32> = texcoord_1;
                let _e579: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e570.x * 85f)) * sin((_e579.y * 85f))));
                let _e587: vec4<f32> = (*col);
                let _e589: vec4<f32> = (*col);
                let _e596: f32 = lum;
                let _e600: vec4<f32> = (*col);
                let _e607: f32 = lum;
                let _e612: vec3<f32> = mix(_e600.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e607)), vec3(0.82f));
                (*col).x = _e612.x;
                (*col).y = _e612.y;
                (*col).z = _e612.z;
                let _e619: vec4<f32> = (*col);
                let _e621: vec4<f32> = (*col);
                let _e623: f32 = etch;
                let _e629: vec3<f32> = (_e621.xyz + (_e623 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e629.x;
                (*col).y = _e629.y;
                (*col).z = _e629.z;
            }
        }
    }
    let _e636: i32 = global.cosmetic_skin;
    if (_e636 == 3i) {
        {
            let _e639: vec2<f32> = texcoord_1;
            let _e642: vec2<f32> = texcoord_1;
            panel = fract((_e642 * 12f));
            let _e649: vec2<f32> = panel;
            let _e653: vec2<f32> = panel;
            let _e658: vec2<f32> = panel;
            let _e662: vec2<f32> = panel;
            seam = (smoothstep(0.025f, 0.075f, _e653.x) * smoothstep(0.025f, 0.075f, _e662.y));
            let _e667: vec4<f32> = (*col);
            let _e686: f32 = seam;
            let _e690: f32 = lum;
            let _e694: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e686)) * (0.5f + (_e690 * 0.6f)));
            (*col).x = _e694.x;
            (*col).y = _e694.y;
            (*col).z = _e694.z;
        }
    } else {
        let _e701: i32 = global.cosmetic_skin;
        if (_e701 == 4i) {
            {
                let _e704: vec4<f32> = (*col);
                let _e711: f32 = lum;
                let _e718: f32 = cosmetic_circuit();
                let _e720: vec3<f32> = ((vec3<f32>(0.13f, 0.18f, 0.22f) * (0.6f + _e711)) + (vec3<f32>(0.08f, 0.8f, 0.65f) * _e718));
                (*col).x = _e720.x;
                (*col).y = _e720.y;
                (*col).z = _e720.z;
            }
        } else {
            let _e727: i32 = global.cosmetic_skin;
            if (_e727 == 5i) {
                {
                    let _e732: vec2<f32> = texcoord_1;
                    let _e736: vec2<f32> = texcoord_1;
                    let _e741: vec2<f32> = texcoord_1;
                    let _e745: vec2<f32> = texcoord_1;
                    let _e753: vec2<f32> = texcoord_1;
                    let _e757: vec2<f32> = texcoord_1;
                    let _e762: vec2<f32> = texcoord_1;
                    let _e766: vec2<f32> = texcoord_1;
                    let _e777: vec2<f32> = texcoord_1;
                    let _e781: vec2<f32> = texcoord_1;
                    let _e786: vec2<f32> = texcoord_1;
                    let _e790: vec2<f32> = texcoord_1;
                    let _e798: vec2<f32> = texcoord_1;
                    let _e802: vec2<f32> = texcoord_1;
                    let _e807: vec2<f32> = texcoord_1;
                    let _e811: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e798.x * 65f) + (_e802.y * 38f)) + (sin((_e811.y * 25f)) * 2f))));
                    let _e822: vec4<f32> = (*col);
                    let _e841: f32 = stripe;
                    let _e845: f32 = lum;
                    let _e847: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e841)) * (0.55f + _e845));
                    (*col).x = _e847.x;
                    (*col).y = _e847.y;
                    (*col).z = _e847.z;
                }
            } else {
                let _e854: i32 = global.cosmetic_skin;
                if (_e854 == 6i) {
                    {
                        let _e857: vec2<f32> = texcoord_1;
                        let _e860: f32 = global.cosmetic_time;
                        let _e866: vec2<f32> = texcoord_1;
                        let _e869: f32 = global.cosmetic_time;
                        let _e875: f32 = cosmetic_soft_noise(((_e866 * 9f) + vec2<f32>((_e869 * 0.025f), 0f)));
                        cloud = _e875;
                        let _e878: vec2<f32> = texcoord_1;
                        let _e881: vec2<f32> = texcoord_1;
                        let _e885: vec2<f32> = texcoord_1;
                        let _e888: vec2<f32> = texcoord_1;
                        let _e892: f32 = cosmetic_noise(floor((_e888 * 100f)));
                        let _e894: vec2<f32> = texcoord_1;
                        let _e897: vec2<f32> = texcoord_1;
                        let _e901: vec2<f32> = texcoord_1;
                        let _e904: vec2<f32> = texcoord_1;
                        let _e908: f32 = cosmetic_noise(floor((_e904 * 100f)));
                        star = step(0.985f, _e908);
                        let _e911: vec4<f32> = (*col);
                        let _e930: f32 = cloud;
                        let _e934: f32 = lum;
                        let _e937: f32 = star;
                        let _e941: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e930)) * (0.5f + _e934)) + vec3((_e937 * 0.65f)));
                        (*col).x = _e941.x;
                        (*col).y = _e941.y;
                        (*col).z = _e941.z;
                    }
                }
            }
        }
    }
    let _e948: i32 = global.cosmetic_skin;
    if (_e948 != 0i) {
        {
            let _e951: vec2<f32> = cosmetic_finish();
            finish = _e951;
            let _e965: vec3<f32> = viewNormal;
            let _e992: vec3<f32> = viewNormal;
            let _e1010: vec2<f32> = finish;
            let _e1014: vec2<f32> = finish;
            let _e1029: vec3<f32> = viewNormal;
            let _e1056: vec3<f32> = viewNormal;
            let _e1074: vec2<f32> = finish;
            let _e1078: vec2<f32> = finish;
            sheen = pow(clamp(dot(_e1056, normalize(vec3<f32>(-0.35f, 0.6f, 0.72f))), 0f, 1f), mix(44f, 8f, _e1078.y));
            let _e1083: vec4<f32> = (*col);
            let _e1085: vec4<f32> = (*col);
            let _e1091: f32 = rim;
            let _e1094: vec2<f32> = finish;
            let _e1100: vec4<f32> = (*col);
            let _e1104: vec2<f32> = finish;
            let _e1108: vec4<f32> = (*col);
            let _e1112: vec2<f32> = finish;
            let _e1116: f32 = sheen;
            let _e1119: vec3<f32> = (_e1085.xyz + (((vec3<f32>(0.12f, 0.16f, 0.2f) * _e1091) * (1f - _e1094.y)) + (mix(vec3(0.12f), (_e1108.xyz * 0.28f), vec3(_e1112.x)) * _e1116)));
            (*col).x = _e1119.x;
            (*col).y = _e1119.y;
            (*col).z = _e1119.z;
        }
    }
    let _e1126: i32 = global.cosmetic_preserve_palette;
    let _e1129: i32 = global.cosmetic_skin;
    if ((_e1126 != 0i) && (_e1129 != 0i)) {
        {
            let _e1133: vec3<f32> = nativeColor;
            let _e1135: vec3<f32> = nativeColor;
            let _e1137: vec3<f32> = nativeColor;
            let _e1139: vec3<f32> = nativeColor;
            let _e1141: vec3<f32> = nativeColor;
            let _e1144: vec3<f32> = nativeColor;
            let _e1146: vec3<f32> = nativeColor;
            let _e1148: vec3<f32> = nativeColor;
            let _e1150: vec3<f32> = nativeColor;
            let _e1152: vec3<f32> = nativeColor;
            let _e1156: vec3<f32> = nativeColor;
            let _e1158: vec3<f32> = nativeColor;
            let _e1160: vec3<f32> = nativeColor;
            let _e1162: vec3<f32> = nativeColor;
            let _e1164: vec3<f32> = nativeColor;
            let _e1167: vec3<f32> = nativeColor;
            let _e1169: vec3<f32> = nativeColor;
            let _e1171: vec3<f32> = nativeColor;
            let _e1173: vec3<f32> = nativeColor;
            let _e1175: vec3<f32> = nativeColor;
            chroma = (max(_e1144.x, max(_e1150.y, _e1152.z)) - min(_e1167.x, min(_e1173.y, _e1175.z)));
            let _e1181: vec4<f32> = (*col);
            let _e1183: vec4<f32> = (*col);
            let _e1191: f32 = chroma;
            let _e1195: vec4<f32> = (*col);
            let _e1197: vec3<f32> = nativeColor;
            let _e1203: f32 = chroma;
            let _e1208: vec3<f32> = mix(_e1195.xyz, _e1197, vec3((smoothstep(0.1f, 0.35f, _e1203) * 0.9f)));
            (*col).x = _e1208.x;
            (*col).y = _e1208.y;
            (*col).z = _e1208.z;
        }
    }
    let _e1215: i32 = global.cosmetic_effect;
    if (_e1215 != 0i) {
        {
            let _e1218: f32 = global.cosmetic_time;
            let _e1219: f32 = global.cosmetic_scroll;
            t = (_e1218 * _e1219);
            let _e1224: f32 = global.cosmetic_time;
            let _e1225: f32 = global.cosmetic_pulse;
            let _e1227: f32 = global.cosmetic_time;
            let _e1228: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1227 * _e1228))));
            let _e1236: vec2<f32> = texcoord_1;
            let _e1240: f32 = t;
            let _e1244: vec2<f32> = texcoord_1;
            let _e1248: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1244.y * 30f) - (_e1248 * 2f)))));
            let _e1257: f32 = rim;
            mask = (0.28f + (_e1257 * 0.72f));
            let _e1262: i32 = global.cosmetic_effect;
            if (_e1262 == 1i) {
                let _e1267: f32 = pulse;
                mask = (0.25f + (0.55f * _e1267));
            }
            let _e1270: i32 = global.cosmetic_effect;
            if (_e1270 == 3i) {
                let _e1278: f32 = wave;
                let _e1282: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1278) * 0.5f) + _e1282);
            }
            let _e1284: i32 = global.cosmetic_effect;
            if (_e1284 == 4i) {
                let _e1287: vec2<f32> = texcoord_1;
                let _e1290: f32 = t;
                let _e1293: f32 = t;
                let _e1299: vec2<f32> = texcoord_1;
                let _e1302: f32 = t;
                let _e1305: f32 = t;
                let _e1311: f32 = cosmetic_soft_noise(((_e1299 * 16f) + vec2<f32>((_e1302 * 0.3f), (-(_e1305) * 0.5f))));
                let _e1314: f32 = rim;
                mask = ((_e1311 * 0.45f) + (_e1314 * 0.6f));
            }
            let _e1318: i32 = global.cosmetic_effect;
            let _e1321: i32 = global.cosmetic_effect;
            if ((_e1318 == 5i) || (_e1321 == 7i)) {
                let _e1331: f32 = wave;
                let _e1336: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1331) * 0.65f)) + (_e1336 * 0.45f));
            }
            let _e1340: i32 = global.cosmetic_effect;
            if (_e1340 == 6i) {
                let _e1344: vec2<f32> = texcoord_1;
                let _e1348: vec2<f32> = texcoord_1;
                let _e1352: f32 = t;
                let _e1354: vec2<f32> = texcoord_1;
                let _e1358: f32 = t;
                let _e1362: vec2<f32> = texcoord_1;
                let _e1366: vec2<f32> = texcoord_1;
                let _e1370: f32 = t;
                let _e1372: vec2<f32> = texcoord_1;
                let _e1376: f32 = t;
                let _e1381: vec2<f32> = texcoord_1;
                let _e1385: vec2<f32> = texcoord_1;
                let _e1389: f32 = t;
                let _e1391: vec2<f32> = texcoord_1;
                let _e1395: f32 = t;
                let _e1399: vec2<f32> = texcoord_1;
                let _e1403: vec2<f32> = texcoord_1;
                let _e1407: f32 = t;
                let _e1409: vec2<f32> = texcoord_1;
                let _e1413: f32 = t;
                let _e1420: vec2<f32> = texcoord_1;
                let _e1424: vec2<f32> = texcoord_1;
                let _e1428: f32 = t;
                let _e1430: vec2<f32> = texcoord_1;
                let _e1434: f32 = t;
                let _e1438: vec2<f32> = texcoord_1;
                let _e1442: vec2<f32> = texcoord_1;
                let _e1446: f32 = t;
                let _e1448: vec2<f32> = texcoord_1;
                let _e1452: f32 = t;
                let _e1457: vec2<f32> = texcoord_1;
                let _e1461: vec2<f32> = texcoord_1;
                let _e1465: f32 = t;
                let _e1467: vec2<f32> = texcoord_1;
                let _e1471: f32 = t;
                let _e1475: vec2<f32> = texcoord_1;
                let _e1479: vec2<f32> = texcoord_1;
                let _e1483: f32 = t;
                let _e1485: vec2<f32> = texcoord_1;
                let _e1489: f32 = t;
                let _e1498: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1475.x * 31f) + sin(((_e1485.y * 29f) + _e1489))))), 16f)) + (_e1498 * 0.5f));
            }
            let _e1502: i32 = global.cosmetic_effect;
            if (_e1502 == 8i) {
                let _e1505: f32 = wave;
                let _e1508: f32 = rim;
                mask = ((_e1505 * 0.35f) + (_e1508 * 0.7f));
            }
            let _e1512: i32 = global.cosmetic_effect;
            if (_e1512 == 9i) {
                let _e1517: vec2<f32> = texcoord_1;
                let _e1521: f32 = t;
                let _e1523: vec2<f32> = texcoord_1;
                let _e1527: f32 = t;
                let _e1532: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1523.x * 40f) + _e1527)))) * _e1532);
            }
            let _e1537: vec3<f32> = global.cosmetic_primary;
            let _e1538: vec3<f32> = global.cosmetic_secondary;
            let _e1539: f32 = wave;
            energy = mix(_e1537, _e1538, vec3(_e1539));
            let _e1546: f32 = mask;
            let _e1550: f32 = global.cosmetic_intensity;
            let _e1552: f32 = pulse;
            strength = ((clamp(_e1546, 0f, 1f) * _e1550) * _e1552);
            let _e1555: i32 = global.cosmetic_preserve_palette;
            if (_e1555 != 0i) {
                let _e1558: f32 = strength;
                strength = (_e1558 * 0.65f);
            }
            let _e1561: vec4<f32> = (*col);
            let _e1563: vec4<f32> = (*col);
            let _e1565: vec3<f32> = energy;
            let _e1567: f32 = lum;
            let _e1572: f32 = strength;
            let _e1577: f32 = strength;
            let _e1583: vec4<f32> = (*col);
            let _e1585: vec3<f32> = energy;
            let _e1587: f32 = lum;
            let _e1592: f32 = strength;
            let _e1597: f32 = strength;
            let _e1604: vec3<f32> = mix(_e1583.xyz, (_e1585 * (0.4f + (_e1587 * 0.6f))), vec3(clamp((_e1597 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1604.x;
            (*col).y = _e1604.y;
            (*col).z = _e1604.z;
            let _e1611: vec4<f32> = (*col);
            let _e1613: vec4<f32> = (*col);
            let _e1615: vec3<f32> = energy;
            let _e1616: f32 = strength;
            let _e1620: vec3<f32> = (_e1613.xyz + ((_e1615 * _e1616) * 0.45f));
            (*col).x = _e1620.x;
            (*col).y = _e1620.y;
            (*col).z = _e1620.z;
        }
    }
    let _e1627: f32 = global.cosmetic_dissolve;
    if (_e1627 > 0f) {
        {
            let _e1630: vec2<f32> = texcoord_1;
            let _e1633: vec2<f32> = texcoord_1;
            let _e1637: vec2<f32> = texcoord_1;
            let _e1640: vec2<f32> = texcoord_1;
            let _e1644: f32 = cosmetic_noise(floor((_e1640 * 64f)));
            n_2 = _e1644;
            let _e1646: f32 = n_2;
            let _e1647: f32 = global.cosmetic_dissolve;
            if (_e1646 < _e1647) {
                discard;
            }
            let _e1649: vec4<f32> = (*col);
            let _e1651: vec4<f32> = (*col);
            let _e1653: vec3<f32> = global.cosmetic_primary;
            let _e1656: f32 = global.cosmetic_dissolve;
            let _e1660: f32 = global.cosmetic_dissolve;
            let _e1661: f32 = global.cosmetic_dissolve;
            let _e1664: f32 = n_2;
            let _e1670: vec3<f32> = (_e1651.xyz + ((_e1653 * (1f - smoothstep(_e1660, (_e1661 + 0.08f), _e1664))) * 0.3f));
            (*col).x = _e1670.x;
            (*col).y = _e1670.y;
            (*col).z = _e1670.z;
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

    let _e328: i32 = global._prime_advanced_materials;
    let _e332: i32 = global._prime_use_light;
    if (!((_e328 != 0i)) || !((_e332 != 0i))) {
        return;
    }
    let _e337: vec3<f32> = mapped_normal();
    n_3 = _e337;
    let _e342: vec3<f32> = global.light1vec;
    let _e343: vec3<f32> = n_3;
    let _e349: vec3<f32> = global.light1vec;
    let _e350: vec3<f32> = n_3;
    d1_ = max(0f, -(dot(_e349, _e350)));
    let _e358: vec3<f32> = global.light2vec;
    let _e359: vec3<f32> = n_3;
    let _e365: vec3<f32> = global.light2vec;
    let _e366: vec3<f32> = n_3;
    d2_ = max(0f, -(dot(_e365, _e366)));
    let _e376: vec3<f32> = global.light1col;
    l1_ = dot(_e376, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e388: vec3<f32> = global.light2col;
    l2_ = dot(_e388, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e395: vec4<f32> = (*col_1);
    let _e397: vec4<f32> = (*col_1);
    let _e401: f32 = d1_;
    let _e402: f32 = l1_;
    let _e404: f32 = d2_;
    let _e405: f32 = l2_;
    let _e412: f32 = d1_;
    let _e413: f32 = l1_;
    let _e415: f32 = d2_;
    let _e416: f32 = l2_;
    let _e426: f32 = d1_;
    let _e427: f32 = l1_;
    let _e429: f32 = d2_;
    let _e430: f32 = l2_;
    let _e437: f32 = d1_;
    let _e438: f32 = l1_;
    let _e440: f32 = d2_;
    let _e441: f32 = l2_;
    let _e450: vec3<f32> = (_e397.xyz * mix(0.92f, 1.1f, clamp((((_e437 * _e438) + (_e440 * _e441)) * 0.65f), 0f, 1f)));
    (*col_1).x = _e450.x;
    (*col_1).y = _e450.y;
    (*col_1).z = _e450.z;
    let _e457: i32 = global._prime_use_specular_map;
    if (_e457 != 0i) {
        let _e461: vec2<f32> = texcoord_1;
        let _e462: vec4<f32> = prime_sample_specular_tex(_e461);
        local = _e462;
    } else {
        let _e463: vec3<f32> = global.specular;
        let _e465: vec3<f32> = global.specular;
        let _e467: vec3<f32> = global.specular;
        let _e469: vec3<f32> = global.specular;
        let _e472: vec3<f32> = global.specular;
        let _e474: vec3<f32> = global.specular;
        let _e476: vec3<f32> = global.specular;
        let _e478: vec3<f32> = global.specular;
        let _e480: vec3<f32> = global.specular;
        let _e483: vec3<f32> = global.specular;
        local = vec4<f32>(max(max(_e478.x, _e480.y), _e483.z), 0.55f, 0f, 1f);
    }
    let _e491: vec4<f32> = local;
    sm = _e491;
    let _e493: vec4<f32> = sm;
    let _e497: vec4<f32> = sm;
    roughness = clamp(_e497.y, 0.04f, 1f);
    let _e508: vec3<f32> = global.light1vec;
    let _e510: vec3<f32> = viewDir;
    let _e512: vec3<f32> = global.light1vec;
    let _e514: vec3<f32> = viewDir;
    h1_ = normalize((-(_e512) + _e514));
    let _e518: vec3<f32> = global.light2vec;
    let _e520: vec3<f32> = viewDir;
    let _e522: vec3<f32> = global.light2vec;
    let _e524: vec3<f32> = viewDir;
    h2_ = normalize((-(_e522) + _e524));
    let _e533: f32 = roughness;
    exponent = mix(72f, 4f, _e533);
    let _e538: vec3<f32> = n_3;
    let _e539: vec3<f32> = h1_;
    let _e544: vec3<f32> = n_3;
    let _e545: vec3<f32> = h1_;
    let _e552: vec3<f32> = n_3;
    let _e553: vec3<f32> = h1_;
    let _e558: vec3<f32> = n_3;
    let _e559: vec3<f32> = h1_;
    let _e563: f32 = exponent;
    let _e565: f32 = l1_;
    let _e569: vec3<f32> = n_3;
    let _e570: vec3<f32> = h2_;
    let _e575: vec3<f32> = n_3;
    let _e576: vec3<f32> = h2_;
    let _e583: vec3<f32> = n_3;
    let _e584: vec3<f32> = h2_;
    let _e589: vec3<f32> = n_3;
    let _e590: vec3<f32> = h2_;
    let _e594: f32 = exponent;
    let _e596: f32 = l2_;
    highlight = ((pow(max(dot(_e558, _e559), 0f), _e563) * _e565) + (pow(max(dot(_e589, _e590), 0f), _e594) * _e596));
    let _e600: vec4<f32> = (*col_1);
    let _e602: vec4<f32> = (*col_1);
    let _e604: f32 = highlight;
    let _e605: vec4<f32> = sm;
    let _e609: vec4<f32> = sm;
    let _e618: vec3<f32> = (_e602.xyz + vec3(((_e604 * clamp(_e609.x, 0f, 1f)) * 0.16f)));
    (*col_1).x = _e618.x;
    (*col_1).y = _e618.y;
    (*col_1).z = _e618.z;
    let _e625: i32 = global._prime_use_emissive_map;
    if (_e625 != 0i) {
        let _e628: vec4<f32> = (*col_1);
        let _e630: vec4<f32> = (*col_1);
        let _e633: vec2<f32> = texcoord_1;
        let _e634: vec4<f32> = prime_sample_emissive_tex(_e633);
        let _e638: vec3<f32> = (_e630.xyz + (_e634.xyz * 0.75f));
        (*col_1).x = _e638.x;
        (*col_1).y = _e638.y;
        (*col_1).z = _e638.z;
        return;
    } else {
        return;
    }
}

fn toon_color(vtx_color: vec4<f32>) -> vec4<f32> {
    var vtx_color_1: vec4<f32>;

    vtx_color_1 = vtx_color;
    let _e329: vec4<f32> = vtx_color_1;
    let _e335: vec4<f32> = vtx_color_1;
    let _e344: vec3<f32> = global.toon_table[i32(clamp((_e335.x * 31f), 0f, 31f))];
    let _e345: vec4<f32> = vtx_color_1;
    return vec4<f32>(_e344.x, _e344.y, _e344.z, _e345.w);
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
    let _e329: i32 = global.cel_bands;
    steps = f32(_e329);
    let _e332: vec3<f32> = c_1;
    let _e334: vec3<f32> = c_1;
    let _e336: vec3<f32> = c_1;
    let _e338: vec3<f32> = c_1;
    let _e341: vec3<f32> = c_1;
    let _e343: vec3<f32> = c_1;
    let _e345: vec3<f32> = c_1;
    let _e347: vec3<f32> = c_1;
    let _e349: vec3<f32> = c_1;
    let _e352: vec3<f32> = c_1;
    lum_1 = max(max(_e347.x, _e349.y), _e352.z);
    let _e356: f32 = lum_1;
    if (_e356 <= 0f) {
        {
            let _e359: vec3<f32> = c_1;
            return _e359;
        }
    }
    let _e360: f32 = lum_1;
    let _e361: f32 = steps;
    scaled = ((_e360 * _e361) - 0.5f);
    let _e367: f32 = scaled;
    lower = floor(_e367);
    let _e370: f32 = lower;
    let _e375: f32 = scaled;
    let _e376: f32 = lower;
    let _e380: f32 = scaled;
    let _e381: f32 = lower;
    let _e385: f32 = steps;
    level = (((_e370 + 0.5f) + smoothstep(0.46f, 0.54f, (_e380 - _e381))) / _e385);
    let _e388: vec3<f32> = c_1;
    let _e389: f32 = level;
    let _e390: f32 = lum_1;
    banded = (_e388 * (_e389 / _e390));
    let _e399: vec3<f32> = banded;
    grey = dot(_e399, vec3<f32>(0.299f, 0.587f, 0.114f));
    let _e406: f32 = grey;
    let _e410: f32 = grey;
    let _e412: vec3<f32> = banded;
    let _e418: f32 = grey;
    let _e422: f32 = grey;
    let _e424: vec3<f32> = banded;
    return clamp(mix(vec3(_e422), _e424, vec3(1.35f)), vec3(0f), vec3(1f));
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

    let _e328: i32 = global._prime_use_texture;
    if (_e328 != 0i) {
        {
            let _e331: i32 = global._prime_use_pal_override;
            if (_e331 != 0i) {
                let _e334: vec4<f32> = global.pal_override_color;
                let _e335: vec3<f32> = _e334.xyz;
                let _e337: vec2<f32> = texcoord_1;
                let _e338: vec4<f32> = prime_sample_tex(_e337);
                local_1 = vec4<f32>(_e335.x, _e335.y, _e335.z, _e338.w);
            } else {
                let _e345: vec2<f32> = texcoord_1;
                let _e346: vec4<f32> = prime_sample_tex(_e345);
                local_1 = _e346;
            }
            let _e348: vec4<f32> = local_1;
            texcolor = _e348;
            let _e350: i32 = global._prime_use_flat;
            let _e353: i32 = global._prime_use_pal_override;
            let _e358: i32 = global.textured_player_skin;
            if (((_e350 != 0i) && !((_e353 != 0i))) && (_e358 == 0i)) {
                {
                    let _e362: vec4<f32> = texcolor;
                    let _e364: vec3<f32> = global.flat_color;
                    texcolor.x = _e364.x;
                    texcolor.y = _e364.y;
                    texcolor.z = _e364.z;
                }
            }
            let _e371: i32 = global.mat_mode;
            if (_e371 == 1i) {
                {
                    let _e374: vec4<f32> = texcolor;
                    let _e376: vec4<f32> = texcolor;
                    let _e379: vec4<f32> = color_1;
                    let _e382: vec4<f32> = texcolor;
                    let _e388: vec4<f32> = texcolor;
                    let _e390: vec4<f32> = texcolor;
                    let _e393: vec4<f32> = color_1;
                    let _e396: vec4<f32> = texcolor;
                    let _e402: vec4<f32> = texcolor;
                    let _e404: vec4<f32> = texcolor;
                    let _e407: vec4<f32> = color_1;
                    let _e410: vec4<f32> = texcolor;
                    let _e416: f32 = global.mat_alpha;
                    let _e417: vec4<f32> = color_1;
                    col_2 = vec4<f32>(((_e374.x * _e376.w) + (_e379.x * (1f - _e382.w))), ((_e388.y * _e390.w) + (_e393.y * (1f - _e396.w))), ((_e402.z * _e404.w) + (_e407.z * (1f - _e410.w))), (_e416 * _e417.w));
                }
            } else {
                let _e421: i32 = global.mat_mode;
                if (_e421 == 2i) {
                    {
                        let _e425: vec4<f32> = color_1;
                        let _e426: vec4<f32> = toon_color(_e425);
                        toon = _e426;
                        let _e428: vec4<f32> = texcolor;
                        let _e430: vec4<f32> = color_1;
                        let _e433: vec4<f32> = toon;
                        let _e435: vec3<f32> = ((_e428.xyz * _e430.x) + _e433.xyz);
                        let _e436: f32 = global.mat_alpha;
                        let _e437: vec4<f32> = texcolor;
                        let _e440: vec4<f32> = color_1;
                        col_2 = vec4<f32>(_e435.x, _e435.y, _e435.z, ((_e436 * _e437.w) * _e440.w));
                    }
                } else {
                    {
                        let _e447: vec4<f32> = color_1;
                        let _e448: vec4<f32> = texcolor;
                        let _e449: vec3<f32> = _e448.xyz;
                        let _e450: f32 = global.mat_alpha;
                        let _e451: vec4<f32> = texcolor;
                        col_2 = (_e447 * vec4<f32>(_e449.x, _e449.y, _e449.z, (_e450 * _e451.w)));
                    }
                }
            }
            let _e459: i32 = global._prime_use_override;
            if (_e459 != 0i) {
                {
                    let _e462: i32 = global.textured_player_skin;
                    if (_e462 > 0i) {
                        {
                            let _e465: i32 = global.textured_player_skin;
                            if (_e465 == 2i) {
                                {
                                    let _e470: vec4<f32> = texcolor;
                                    let _e476: vec4<f32> = texcolor;
                                    let _e485: vec4<f32> = texcolor;
                                    let _e491: vec4<f32> = texcolor;
                                    detail = smoothstep(0.05f, 0.85f, dot(_e491.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f)));
                                    let _e500: vec4<f32> = global.override_color;
                                    let _e504: f32 = detail;
                                    tinted = (_e500.xyz * (0.25f + (0.75f * _e504)));
                                    let _e509: vec4<f32> = col_2;
                                    let _e512: vec4<f32> = texcolor;
                                    let _e516: vec4<f32> = texcolor;
                                    let _e522: vec3<f32> = tinted;
                                    let _e523: vec4<f32> = texcolor;
                                    let _e527: vec4<f32> = texcolor;
                                    let _e534: vec3<f32> = mix(_e522, pow(_e527.xyz, vec3(0.7f)), vec3(0.25f));
                                    col_2.x = _e534.x;
                                    col_2.y = _e534.y;
                                    col_2.z = _e534.z;
                                }
                            } else {
                                {
                                    let _e541: vec4<f32> = col_2;
                                    let _e543: vec4<f32> = col_2;
                                    let _e545: vec4<f32> = texcolor;
                                    let _e548: vec4<f32> = col_2;
                                    let _e550: vec4<f32> = texcolor;
                                    let _e559: vec4<f32> = col_2;
                                    let _e561: vec4<f32> = texcolor;
                                    let _e564: vec4<f32> = col_2;
                                    let _e566: vec4<f32> = texcolor;
                                    let _e577: vec3<f32> = clamp((mix(_e564.xyz, _e566.xyz, vec3(0.8f)) * 1.25f), vec3(0f), vec3(1f));
                                    col_2.x = _e577.x;
                                    col_2.y = _e577.y;
                                    col_2.z = _e577.z;
                                }
                            }
                        }
                    } else {
                        {
                            let _e584: vec4<f32> = col_2;
                            let _e586: vec4<f32> = global.override_color;
                            let _e587: vec3<f32> = _e586.xyz;
                            col_2.x = _e587.x;
                            col_2.y = _e587.y;
                            col_2.z = _e587.z;
                        }
                    }
                    let _e595: vec4<f32> = col_2;
                    let _e597: vec4<f32> = global.override_color;
                    col_2.w = (_e595.w * _e597.w);
                }
            }
        }
    } else {
        let _e600: i32 = global._prime_use_override;
        if (_e600 != 0i) {
            {
                let _e603: vec4<f32> = global.override_color;
                col_2 = _e603;
            }
        } else {
            {
                let _e604: i32 = global.mat_mode;
                if (_e604 == 2i) {
                    let _e608: vec4<f32> = color_1;
                    let _e609: vec4<f32> = toon_color(_e608);
                    local_2 = _e609;
                } else {
                    let _e610: vec4<f32> = color_1;
                    local_2 = _e610;
                }
                let _e612: vec4<f32> = local_2;
                col_2 = _e612;
                let _e614: vec4<f32> = col_2;
                let _e616: f32 = global.mat_alpha;
                col_2.w = (_e614.w * _e616);
            }
        }
    }
    apply_material_lighting((&col_2));
    apply_cosmetics((&col_2));
    let _e622: i32 = global._prime_player_outline_mask;
    if (_e622 != 0i) {
        {
            let _e625: vec4<f32> = col_2;
            if (_e625.w <= 0.01f) {
                discard;
            }
            let _e629: vec4<f32> = col_2;
            let _e631: vec3<f32> = global.player_outline_color;
            col_2.x = _e631.x;
            col_2.y = _e631.y;
            col_2.z = _e631.z;
        }
    }
    let _e638: i32 = global.cel_bands;
    if (_e638 > 0i) {
        {
            let _e641: vec4<f32> = col_2;
            let _e643: vec4<f32> = col_2;
            let _e645: vec4<f32> = col_2;
            let _e647: vec3<f32> = cel_shade(_e645.xyz);
            col_2.x = _e647.x;
            col_2.y = _e647.y;
            col_2.z = _e647.z;
        }
    }
    let _e654: i32 = global._prime_fog_enable;
    if (_e654 != 0i) {
        {
            let _e658: vec4<f32> = gl_FragCoord;
            depth = _e658.z;
            let _e663: f32 = depth;
            let _e664: f32 = global.fog_max;
            if (_e663 >= _e664) {
                {
                    density = 1f;
                }
            } else {
                let _e667: f32 = depth;
                let _e668: f32 = global.fog_min;
                if (_e667 > _e668) {
                    {
                        let _e670: f32 = depth;
                        let _e671: f32 = global.fog_min;
                        let _e673: f32 = global.fog_max;
                        let _e674: f32 = global.fog_min;
                        density = ((((_e670 - _e671) / (_e673 - _e674)) * 124f) / 128f);
                    }
                }
            }
            let _e681: vec4<f32> = col_2;
            let _e683: f32 = density;
            let _e686: vec4<f32> = global.fog_color;
            let _e687: f32 = density;
            let _e690: vec3<f32> = ((_e681 * (1f - _e683)) + (_e686 * _e687)).xyz;
            let _e691: vec4<f32> = col_2;
            col_2 = vec4<f32>(_e690.x, _e690.y, _e690.z, _e691.w);
        }
    }
    let _e697: vec4<f32> = col_2;
    prime_output = _e697;
    return;
}

fn main_1() {
    var a: f32;
    var r: f32;
    var keep: bool;

    prime_original_main();
    let _e328: vec4<f32> = prime_output;
    a = _e328.w;
    let _e331: f32 = global.prime_alpha_ref;
    r = _e331;
    let _e333: i32 = global.prime_alpha_func;
    let _e336: i32 = global.prime_alpha_func;
    let _e339: f32 = a;
    let _e340: f32 = r;
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
    keep = (((((((_e333 == 519i) || ((_e336 == 513i) && (_e339 < _e340))) || ((_e344 == 514i) && (_e347 == _e348))) || ((_e352 == 515i) && (_e355 <= _e356))) || ((_e360 == 516i) && (_e363 > _e364))) || ((_e368 == 517i) && (_e371 != _e372))) || ((_e376 == 518i) && (_e379 >= _e380)));
    let _e385: bool = keep;
    if !(_e385) {
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
    let _e351: vec4<f32> = prime_output;
    return FragmentOutput(_e351);
}
