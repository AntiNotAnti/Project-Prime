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
    emissive_intensity: f32,
    _pad_2672_0_: f32,
    _pad_2672_1_: f32,
    _pad_2672_2_: f32,
    _prime_use_override: i32,
    _pad_2688_0_: f32,
    _pad_2688_1_: f32,
    _pad_2688_2_: f32,
    textured_player_skin: i32,
    _pad_2704_0_: f32,
    _pad_2704_1_: f32,
    _pad_2704_2_: f32,
    _prime_player_outline_mask: i32,
    _pad_2720_0_: f32,
    _pad_2720_1_: f32,
    _pad_2720_2_: f32,
    player_outline_color: vec3<f32>,
    _pad_2736_0_: f32,
    override_color: vec4<f32>,
    _prime_use_pal_override: i32,
    _pad_2768_0_: f32,
    _pad_2768_1_: f32,
    _pad_2768_2_: f32,
    pal_override_color: vec4<f32>,
    mat_alpha: f32,
    _pad_2800_0_: f32,
    _pad_2800_1_: f32,
    _pad_2800_2_: f32,
    mat_mode: i32,
    _pad_2816_0_: f32,
    _pad_2816_1_: f32,
    _pad_2816_2_: f32,
    toon_table: array<vec3<f32>, 32>,
    cel_bands: i32,
    _pad_3344_0_: f32,
    _pad_3344_1_: f32,
    _pad_3344_2_: f32,
    _prime_use_flat: i32,
    _pad_3360_0_: f32,
    _pad_3360_1_: f32,
    _pad_3360_2_: f32,
    flat_color: vec3<f32>,
    _pad_3376_0_: f32,
    cosmetic_skin: i32,
    _pad_3392_0_: f32,
    _pad_3392_1_: f32,
    _pad_3392_2_: f32,
    cosmetic_preserve_palette: i32,
    _pad_3408_0_: f32,
    _pad_3408_1_: f32,
    _pad_3408_2_: f32,
    cosmetic_effect: i32,
    _pad_3424_0_: f32,
    _pad_3424_1_: f32,
    _pad_3424_2_: f32,
    cosmetic_time: f32,
    _pad_3440_0_: f32,
    _pad_3440_1_: f32,
    _pad_3440_2_: f32,
    cosmetic_primary: vec3<f32>,
    _pad_3456_0_: f32,
    cosmetic_secondary: vec3<f32>,
    _pad_3472_0_: f32,
    cosmetic_intensity: f32,
    _pad_3488_0_: f32,
    _pad_3488_1_: f32,
    _pad_3488_2_: f32,
    cosmetic_pulse: f32,
    _pad_3504_0_: f32,
    _pad_3504_1_: f32,
    _pad_3504_2_: f32,
    cosmetic_scroll: f32,
    _pad_3520_0_: f32,
    _pad_3520_1_: f32,
    _pad_3520_2_: f32,
    cosmetic_dissolve: f32,
    _pad_3536_0_: f32,
    _pad_3536_1_: f32,
    _pad_3536_2_: f32,
    prime_viewport: vec4<f32>,
    prime_imm_color: vec4<f32>,
    prime_imm_normal: vec4<f32>,
    prime_alpha_func: i32,
    _pad_3600_0_: f32,
    _pad_3600_1_: f32,
    _pad_3600_2_: f32,
    prime_alpha_ref: f32,
    _pad_3616_0_: f32,
    _pad_3616_1_: f32,
    _pad_3616_2_: f32,
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
    let _e334: vec2<f32> = uv_1;
    let _e336: vec2<f32> = uv_1;
    let _e339: vec2<f32> = uv_1;
    let _e344: vec4<f32> = global.prime_texture_flip[0];
    let _e346: vec2<f32> = uv_1;
    let _e349: vec2<f32> = uv_1;
    let _e354: vec4<f32> = global.prime_texture_flip[0];
    let _e358: vec2<f32> = uv_1;
    let _e360: vec2<f32> = uv_1;
    let _e363: vec2<f32> = uv_1;
    let _e368: vec4<f32> = global.prime_texture_flip[0];
    let _e370: vec2<f32> = uv_1;
    let _e373: vec2<f32> = uv_1;
    let _e378: vec4<f32> = global.prime_texture_flip[0];
    let _e382: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e358.x, mix(_e370.y, (1f - _e373.y), _e378.x)));
    return _e382;
}

fn prime_sample_normal_tex(uv_2: vec2<f32>) -> vec4<f32> {
    var uv_3: vec2<f32>;

    uv_3 = uv_2;
    let _e336: vec2<f32> = uv_3;
    let _e338: vec2<f32> = uv_3;
    let _e341: vec2<f32> = uv_3;
    let _e346: vec4<f32> = global.prime_texture_flip[1];
    let _e348: vec2<f32> = uv_3;
    let _e351: vec2<f32> = uv_3;
    let _e356: vec4<f32> = global.prime_texture_flip[1];
    let _e360: vec2<f32> = uv_3;
    let _e362: vec2<f32> = uv_3;
    let _e365: vec2<f32> = uv_3;
    let _e370: vec4<f32> = global.prime_texture_flip[1];
    let _e372: vec2<f32> = uv_3;
    let _e375: vec2<f32> = uv_3;
    let _e380: vec4<f32> = global.prime_texture_flip[1];
    let _e384: vec4<f32> = textureSample(prime_tex_normal_tex, prime_sampler_normal_tex, vec2<f32>(_e360.x, mix(_e372.y, (1f - _e375.y), _e380.x)));
    return _e384;
}

fn prime_sample_specular_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e338: vec2<f32> = uv_5;
    let _e340: vec2<f32> = uv_5;
    let _e343: vec2<f32> = uv_5;
    let _e348: vec4<f32> = global.prime_texture_flip[2];
    let _e350: vec2<f32> = uv_5;
    let _e353: vec2<f32> = uv_5;
    let _e358: vec4<f32> = global.prime_texture_flip[2];
    let _e362: vec2<f32> = uv_5;
    let _e364: vec2<f32> = uv_5;
    let _e367: vec2<f32> = uv_5;
    let _e372: vec4<f32> = global.prime_texture_flip[2];
    let _e374: vec2<f32> = uv_5;
    let _e377: vec2<f32> = uv_5;
    let _e382: vec4<f32> = global.prime_texture_flip[2];
    let _e386: vec4<f32> = textureSample(prime_tex_specular_tex, prime_sampler_specular_tex, vec2<f32>(_e362.x, mix(_e374.y, (1f - _e377.y), _e382.x)));
    return _e386;
}

fn prime_sample_emissive_tex(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e340: vec2<f32> = uv_7;
    let _e342: vec2<f32> = uv_7;
    let _e345: vec2<f32> = uv_7;
    let _e350: vec4<f32> = global.prime_texture_flip[3];
    let _e352: vec2<f32> = uv_7;
    let _e355: vec2<f32> = uv_7;
    let _e360: vec4<f32> = global.prime_texture_flip[3];
    let _e364: vec2<f32> = uv_7;
    let _e366: vec2<f32> = uv_7;
    let _e369: vec2<f32> = uv_7;
    let _e374: vec4<f32> = global.prime_texture_flip[3];
    let _e376: vec2<f32> = uv_7;
    let _e379: vec2<f32> = uv_7;
    let _e384: vec4<f32> = global.prime_texture_flip[3];
    let _e388: vec4<f32> = textureSample(prime_tex_emissive_tex, prime_sampler_emissive_tex, vec2<f32>(_e364.x, mix(_e376.y, (1f - _e379.y), _e384.x)));
    return _e388;
}

fn mapped_normal() -> vec3<f32> {
    var n: vec3<f32>;
    var dp1_: vec3<f32>;
    var dp2_: vec3<f32>;
    var duv1_: vec2<f32>;
    var duv2_: vec2<f32>;
    var uvScale: f32;
    var det: f32;
    var tangent: vec3<f32>;
    var bitangent: vec3<f32>;
    var tangentLength2_: f32;
    var bitangentLength2_: f32;
    var local: f32;
    var orientation: f32;
    var mapNormal: vec3<f32>;

    let _e344: vec3<f32> = surface_normal_1;
    n = normalize(_e344);
    let _e347: i32 = global._prime_use_normal_map;
    if !((_e347 != 0i)) {
        let _e351: vec3<f32> = n;
        return _e351;
    }
    let _e353: vec3<f32> = surface_position_1;
    let _e354: vec3<f32> = dpdx(_e353);
    dp1_ = _e354;
    let _e357: vec3<f32> = surface_position_1;
    let _e358: vec3<f32> = dpdy(_e357);
    dp2_ = _e358;
    let _e361: vec2<f32> = texcoord_1;
    let _e362: vec2<f32> = dpdx(_e361);
    duv1_ = _e362;
    let _e365: vec2<f32> = texcoord_1;
    let _e366: vec2<f32> = dpdy(_e365);
    duv2_ = _e366;
    let _e368: vec2<f32> = duv1_;
    let _e370: vec2<f32> = duv1_;
    let _e373: vec2<f32> = duv1_;
    let _e375: vec2<f32> = duv1_;
    let _e378: vec2<f32> = duv1_;
    let _e380: vec2<f32> = duv1_;
    let _e383: vec2<f32> = duv1_;
    let _e385: vec2<f32> = duv1_;
    let _e389: vec2<f32> = duv2_;
    let _e391: vec2<f32> = duv2_;
    let _e394: vec2<f32> = duv2_;
    let _e396: vec2<f32> = duv2_;
    let _e399: vec2<f32> = duv2_;
    let _e401: vec2<f32> = duv2_;
    let _e404: vec2<f32> = duv2_;
    let _e406: vec2<f32> = duv2_;
    let _e410: vec2<f32> = duv1_;
    let _e412: vec2<f32> = duv1_;
    let _e415: vec2<f32> = duv1_;
    let _e417: vec2<f32> = duv1_;
    let _e420: vec2<f32> = duv1_;
    let _e422: vec2<f32> = duv1_;
    let _e425: vec2<f32> = duv1_;
    let _e427: vec2<f32> = duv1_;
    let _e431: vec2<f32> = duv2_;
    let _e433: vec2<f32> = duv2_;
    let _e436: vec2<f32> = duv2_;
    let _e438: vec2<f32> = duv2_;
    let _e441: vec2<f32> = duv2_;
    let _e443: vec2<f32> = duv2_;
    let _e446: vec2<f32> = duv2_;
    let _e448: vec2<f32> = duv2_;
    uvScale = max(max(abs(_e422.x), abs(_e427.y)), max(abs(_e443.x), abs(_e448.y)));
    let _e454: f32 = uvScale;
    if !((_e454 > 0f)) {
        let _e458: vec3<f32> = n;
        return _e458;
    }
    let _e459: vec2<f32> = duv1_;
    let _e460: f32 = uvScale;
    duv1_ = (_e459 / vec2(_e460));
    let _e463: vec2<f32> = duv2_;
    let _e464: f32 = uvScale;
    duv2_ = (_e463 / vec2(_e464));
    let _e467: vec2<f32> = duv1_;
    let _e469: vec2<f32> = duv2_;
    let _e472: vec2<f32> = duv1_;
    let _e474: vec2<f32> = duv2_;
    det = ((_e467.x * _e469.y) - (_e472.y * _e474.x));
    let _e480: f32 = det;
    let _e484: vec2<f32> = duv1_;
    let _e488: vec2<f32> = duv2_;
    if (abs(_e480) <= ((0.000001f * length(_e484)) * length(_e488))) {
        let _e492: vec3<f32> = n;
        return _e492;
    }
    let _e493: vec3<f32> = dp1_;
    let _e494: vec2<f32> = duv2_;
    let _e497: vec3<f32> = dp2_;
    let _e498: vec2<f32> = duv1_;
    tangent = ((_e493 * _e494.y) - (_e497 * _e498.y));
    let _e503: vec3<f32> = dp1_;
    let _e505: vec2<f32> = duv2_;
    let _e508: vec3<f32> = dp2_;
    let _e509: vec2<f32> = duv1_;
    bitangent = ((-(_e503) * _e505.x) + (_e508 * _e509.x));
    let _e516: vec3<f32> = tangent;
    let _e517: vec3<f32> = tangent;
    tangentLength2_ = dot(_e516, _e517);
    let _e522: vec3<f32> = bitangent;
    let _e523: vec3<f32> = bitangent;
    bitangentLength2_ = dot(_e522, _e523);
    let _e526: f32 = tangentLength2_;
    let _e530: f32 = bitangentLength2_;
    if (!((_e526 > 0f)) || !((_e530 > 0f))) {
        let _e535: vec3<f32> = n;
        return _e535;
    }
    let _e536: f32 = det;
    if (_e536 < 0f) {
        local = -1f;
    } else {
        local = 1f;
    }
    let _e543: f32 = local;
    orientation = _e543;
    let _e545: vec3<f32> = tangent;
    let _e546: f32 = orientation;
    let _e548: f32 = tangentLength2_;
    tangent = (_e545 * (_e546 * inverseSqrt(_e548)));
    let _e552: vec3<f32> = bitangent;
    let _e553: f32 = orientation;
    let _e555: f32 = bitangentLength2_;
    bitangent = (_e552 * (_e553 * inverseSqrt(_e555)));
    let _e560: vec2<f32> = texcoord_1;
    let _e561: vec4<f32> = prime_sample_normal_tex(_e560);
    mapNormal = ((_e561.xyz * 2f) - vec3(1f));
    let _e569: vec3<f32> = tangent;
    let _e570: vec3<f32> = mapNormal;
    let _e573: vec3<f32> = bitangent;
    let _e574: vec3<f32> = mapNormal;
    let _e578: vec3<f32> = n;
    let _e579: vec3<f32> = mapNormal;
    let _e583: vec3<f32> = tangent;
    let _e584: vec3<f32> = mapNormal;
    let _e587: vec3<f32> = bitangent;
    let _e588: vec3<f32> = mapNormal;
    let _e592: vec3<f32> = n;
    let _e593: vec3<f32> = mapNormal;
    return normalize((((_e583 * _e584.x) + (_e587 * _e588.y)) + (_e592 * _e593.z)));
}

fn cosmetic_noise(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e349: vec2<f32> = p_1;
    let _e358: vec2<f32> = p_1;
    let _e370: vec2<f32> = p_1;
    let _e379: vec2<f32> = p_1;
    return fract((sin(dot(_e379, vec2<f32>(127.1f, 311.7f))) * 43758.547f));
}

fn cosmetic_soft_noise(p_2: vec2<f32>) -> f32 {
    var p_3: vec2<f32>;
    var cell: vec2<f32>;
    var f: vec2<f32>;

    p_3 = p_2;
    let _e346: vec2<f32> = p_3;
    cell = floor(_e346);
    let _e350: vec2<f32> = p_3;
    f = fract(_e350);
    let _e353: vec2<f32> = f;
    let _e354: vec2<f32> = f;
    let _e358: vec2<f32> = f;
    f = ((_e353 * _e354) * (vec2(3f) - (2f * _e358)));
    let _e364: vec2<f32> = cell;
    let _e365: f32 = cosmetic_noise(_e364);
    let _e366: vec2<f32> = cell;
    let _e371: vec2<f32> = cell;
    let _e376: f32 = cosmetic_noise((_e371 + vec2<f32>(1f, 0f)));
    let _e377: vec2<f32> = f;
    let _e380: vec2<f32> = cell;
    let _e381: f32 = cosmetic_noise(_e380);
    let _e382: vec2<f32> = cell;
    let _e387: vec2<f32> = cell;
    let _e392: f32 = cosmetic_noise((_e387 + vec2<f32>(1f, 0f)));
    let _e393: vec2<f32> = f;
    let _e396: vec2<f32> = cell;
    let _e401: vec2<f32> = cell;
    let _e406: f32 = cosmetic_noise((_e401 + vec2<f32>(0f, 1f)));
    let _e407: vec2<f32> = cell;
    let _e411: vec2<f32> = cell;
    let _e415: f32 = cosmetic_noise((_e411 + vec2(1f)));
    let _e416: vec2<f32> = f;
    let _e418: vec2<f32> = cell;
    let _e423: vec2<f32> = cell;
    let _e428: f32 = cosmetic_noise((_e423 + vec2<f32>(0f, 1f)));
    let _e429: vec2<f32> = cell;
    let _e433: vec2<f32> = cell;
    let _e437: f32 = cosmetic_noise((_e433 + vec2(1f)));
    let _e438: vec2<f32> = f;
    let _e441: vec2<f32> = f;
    let _e444: vec2<f32> = cell;
    let _e445: f32 = cosmetic_noise(_e444);
    let _e446: vec2<f32> = cell;
    let _e451: vec2<f32> = cell;
    let _e456: f32 = cosmetic_noise((_e451 + vec2<f32>(1f, 0f)));
    let _e457: vec2<f32> = f;
    let _e460: vec2<f32> = cell;
    let _e461: f32 = cosmetic_noise(_e460);
    let _e462: vec2<f32> = cell;
    let _e467: vec2<f32> = cell;
    let _e472: f32 = cosmetic_noise((_e467 + vec2<f32>(1f, 0f)));
    let _e473: vec2<f32> = f;
    let _e476: vec2<f32> = cell;
    let _e481: vec2<f32> = cell;
    let _e486: f32 = cosmetic_noise((_e481 + vec2<f32>(0f, 1f)));
    let _e487: vec2<f32> = cell;
    let _e491: vec2<f32> = cell;
    let _e495: f32 = cosmetic_noise((_e491 + vec2(1f)));
    let _e496: vec2<f32> = f;
    let _e498: vec2<f32> = cell;
    let _e503: vec2<f32> = cell;
    let _e508: f32 = cosmetic_noise((_e503 + vec2<f32>(0f, 1f)));
    let _e509: vec2<f32> = cell;
    let _e513: vec2<f32> = cell;
    let _e517: f32 = cosmetic_noise((_e513 + vec2(1f)));
    let _e518: vec2<f32> = f;
    let _e521: vec2<f32> = f;
    return mix(mix(_e461, _e472, _e473.x), mix(_e508, _e517, _e518.x), _e521.y);
}

fn cosmetic_finish() -> vec2<f32> {
    let _e343: i32 = global.cosmetic_skin;
    if (_e343 == 1i) {
        return vec2<f32>(0.78f, 0.3f);
    }
    let _e349: i32 = global.cosmetic_skin;
    if (_e349 == 2i) {
        return vec2<f32>(0.85f, 0.38f);
    }
    let _e355: i32 = global.cosmetic_skin;
    if (_e355 == 3i) {
        return vec2<f32>(0.05f, 0.24f);
    }
    let _e361: i32 = global.cosmetic_skin;
    if (_e361 == 4i) {
        return vec2<f32>(0.55f, 0.42f);
    }
    let _e367: i32 = global.cosmetic_skin;
    if (_e367 == 5i) {
        return vec2<f32>(0.12f, 0.68f);
    }
    return vec2<f32>(0.35f, 0.28f);
}

fn cosmetic_circuit() -> f32 {
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var travel: f32;

    let _e343: vec2<f32> = texcoord_1;
    let _e346: vec2<f32> = texcoord_1;
    grid = fract((_e346 * 18f));
    let _e354: vec2<f32> = grid;
    let _e356: vec2<f32> = grid;
    let _e358: vec2<f32> = grid;
    let _e360: vec2<f32> = grid;
    let _e365: vec2<f32> = grid;
    let _e367: vec2<f32> = grid;
    let _e369: vec2<f32> = grid;
    let _e371: vec2<f32> = grid;
    trace = (1f - smoothstep(0.035f, 0.09f, min(_e369.x, _e371.y)));
    let _e380: vec2<f32> = grid;
    let _e384: vec2<f32> = grid;
    let _e391: vec2<f32> = grid;
    let _e395: vec2<f32> = grid;
    node = (1f - smoothstep(0.08f, 0.16f, length((_e395 - vec2(0.15f)))));
    let _e405: vec2<f32> = texcoord_1;
    let _e407: vec2<f32> = texcoord_1;
    let _e412: f32 = global.cosmetic_time;
    let _e416: vec2<f32> = texcoord_1;
    let _e418: vec2<f32> = texcoord_1;
    let _e423: f32 = global.cosmetic_time;
    let _e433: vec2<f32> = texcoord_1;
    let _e435: vec2<f32> = texcoord_1;
    let _e440: f32 = global.cosmetic_time;
    let _e444: vec2<f32> = texcoord_1;
    let _e446: vec2<f32> = texcoord_1;
    let _e451: f32 = global.cosmetic_time;
    travel = pow((0.5f + (0.5f * sin((((_e444.x + _e446.y) * 32f) - (_e451 * 1.8f))))), 8f);
    let _e461: f32 = trace;
    let _e463: f32 = travel;
    let _e468: f32 = node;
    let _e471: f32 = trace;
    let _e473: f32 = travel;
    let _e478: f32 = node;
    return max((_e471 * (0.3f + (_e473 * 0.7f))), (_e478 * 0.75f));
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

    let _e344: i32 = global.cosmetic_skin;
    let _e347: i32 = global.cosmetic_effect;
    let _e351: f32 = global.cosmetic_dissolve;
    if (((_e344 == 0i) && (_e347 == 0i)) && (_e351 <= 0f)) {
        return;
    }
    let _e355: vec4<f32> = (*col);
    nativeColor = _e355.xyz;
    let _e358: vec4<f32> = (*col);
    let _e364: vec4<f32> = (*col);
    lum = dot(_e364.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e372: vec3<f32> = surface_normal_1;
    let _e375: vec3<f32> = surface_normal_1;
    let _e376: vec3<f32> = surface_normal_1;
    let _e381: vec3<f32> = surface_normal_1;
    let _e382: vec3<f32> = surface_normal_1;
    let _e388: vec3<f32> = surface_normal_1;
    let _e389: vec3<f32> = surface_normal_1;
    let _e394: vec3<f32> = surface_normal_1;
    let _e395: vec3<f32> = surface_normal_1;
    n_1 = (_e372 * inverseSqrt(max(dot(_e394, _e395), 0.0001f)));
    let _e402: mat4x4<f32> = global.view_mtx;
    let _e412: vec3<f32> = n_1;
    viewNormal = (mat3x3<f32>(_e402[0].xyz, _e402[1].xyz, _e402[2].xyz) * _e412);
    let _e415: mat4x4<f32> = global.view_mtx;
    let _e416: vec3<f32> = surface_position_1;
    toEye = -((_e415 * vec4<f32>(_e416.x, _e416.y, _e416.z, 1f)).xyz);
    let _e426: vec3<f32> = toEye;
    let _e429: vec3<f32> = toEye;
    let _e430: vec3<f32> = toEye;
    let _e435: vec3<f32> = toEye;
    let _e436: vec3<f32> = toEye;
    let _e442: vec3<f32> = toEye;
    let _e443: vec3<f32> = toEye;
    let _e448: vec3<f32> = toEye;
    let _e449: vec3<f32> = toEye;
    toEye = (_e426 * inverseSqrt(max(dot(_e448, _e449), 0.0001f)));
    let _e458: vec3<f32> = viewNormal;
    let _e459: vec3<f32> = toEye;
    let _e463: vec3<f32> = viewNormal;
    let _e464: vec3<f32> = toEye;
    let _e473: vec3<f32> = viewNormal;
    let _e474: vec3<f32> = toEye;
    let _e478: vec3<f32> = viewNormal;
    let _e479: vec3<f32> = toEye;
    let _e490: vec3<f32> = viewNormal;
    let _e491: vec3<f32> = toEye;
    let _e495: vec3<f32> = viewNormal;
    let _e496: vec3<f32> = toEye;
    let _e505: vec3<f32> = viewNormal;
    let _e506: vec3<f32> = toEye;
    let _e510: vec3<f32> = viewNormal;
    let _e511: vec3<f32> = toEye;
    rim = pow(clamp((1f - abs(dot(_e510, _e511))), 0f, 1f), 2.2f);
    let _e521: i32 = global.cosmetic_skin;
    if (_e521 == 1i) {
        {
            let _e524: vec4<f32> = (*col);
            let _e526: vec4<f32> = (*col);
            let _e533: f32 = lum;
            let _e537: vec4<f32> = (*col);
            let _e544: f32 = lum;
            let _e549: vec3<f32> = mix(_e537.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e544)), vec3(0.88f));
            (*col).x = _e549.x;
            (*col).y = _e549.y;
            (*col).z = _e549.z;
        }
    } else {
        let _e556: i32 = global.cosmetic_skin;
        if (_e556 == 2i) {
            {
                let _e561: vec2<f32> = texcoord_1;
                let _e565: vec2<f32> = texcoord_1;
                let _e570: vec2<f32> = texcoord_1;
                let _e574: vec2<f32> = texcoord_1;
                let _e582: vec2<f32> = texcoord_1;
                let _e586: vec2<f32> = texcoord_1;
                let _e591: vec2<f32> = texcoord_1;
                let _e595: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e586.x * 85f)) * sin((_e595.y * 85f))));
                let _e603: vec4<f32> = (*col);
                let _e605: vec4<f32> = (*col);
                let _e612: f32 = lum;
                let _e616: vec4<f32> = (*col);
                let _e623: f32 = lum;
                let _e628: vec3<f32> = mix(_e616.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e623)), vec3(0.82f));
                (*col).x = _e628.x;
                (*col).y = _e628.y;
                (*col).z = _e628.z;
                let _e635: vec4<f32> = (*col);
                let _e637: vec4<f32> = (*col);
                let _e639: f32 = etch;
                let _e645: vec3<f32> = (_e637.xyz + (_e639 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e645.x;
                (*col).y = _e645.y;
                (*col).z = _e645.z;
            }
        }
    }
    let _e652: i32 = global.cosmetic_skin;
    if (_e652 == 3i) {
        {
            let _e655: vec2<f32> = texcoord_1;
            let _e658: vec2<f32> = texcoord_1;
            panel = fract((_e658 * 12f));
            let _e665: vec2<f32> = panel;
            let _e669: vec2<f32> = panel;
            let _e674: vec2<f32> = panel;
            let _e678: vec2<f32> = panel;
            seam = (smoothstep(0.025f, 0.075f, _e669.x) * smoothstep(0.025f, 0.075f, _e678.y));
            let _e683: vec4<f32> = (*col);
            let _e702: f32 = seam;
            let _e706: f32 = lum;
            let _e710: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e702)) * (0.5f + (_e706 * 0.6f)));
            (*col).x = _e710.x;
            (*col).y = _e710.y;
            (*col).z = _e710.z;
        }
    } else {
        let _e717: i32 = global.cosmetic_skin;
        if (_e717 == 4i) {
            {
                let _e720: vec4<f32> = (*col);
                let _e727: f32 = lum;
                let _e734: f32 = cosmetic_circuit();
                let _e736: vec3<f32> = ((vec3<f32>(0.13f, 0.18f, 0.22f) * (0.6f + _e727)) + (vec3<f32>(0.08f, 0.8f, 0.65f) * _e734));
                (*col).x = _e736.x;
                (*col).y = _e736.y;
                (*col).z = _e736.z;
            }
        } else {
            let _e743: i32 = global.cosmetic_skin;
            if (_e743 == 5i) {
                {
                    let _e748: vec2<f32> = texcoord_1;
                    let _e752: vec2<f32> = texcoord_1;
                    let _e757: vec2<f32> = texcoord_1;
                    let _e761: vec2<f32> = texcoord_1;
                    let _e769: vec2<f32> = texcoord_1;
                    let _e773: vec2<f32> = texcoord_1;
                    let _e778: vec2<f32> = texcoord_1;
                    let _e782: vec2<f32> = texcoord_1;
                    let _e793: vec2<f32> = texcoord_1;
                    let _e797: vec2<f32> = texcoord_1;
                    let _e802: vec2<f32> = texcoord_1;
                    let _e806: vec2<f32> = texcoord_1;
                    let _e814: vec2<f32> = texcoord_1;
                    let _e818: vec2<f32> = texcoord_1;
                    let _e823: vec2<f32> = texcoord_1;
                    let _e827: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e814.x * 65f) + (_e818.y * 38f)) + (sin((_e827.y * 25f)) * 2f))));
                    let _e838: vec4<f32> = (*col);
                    let _e857: f32 = stripe;
                    let _e861: f32 = lum;
                    let _e863: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e857)) * (0.55f + _e861));
                    (*col).x = _e863.x;
                    (*col).y = _e863.y;
                    (*col).z = _e863.z;
                }
            } else {
                let _e870: i32 = global.cosmetic_skin;
                if (_e870 == 6i) {
                    {
                        let _e873: vec2<f32> = texcoord_1;
                        let _e876: f32 = global.cosmetic_time;
                        let _e882: vec2<f32> = texcoord_1;
                        let _e885: f32 = global.cosmetic_time;
                        let _e891: f32 = cosmetic_soft_noise(((_e882 * 9f) + vec2<f32>((_e885 * 0.025f), 0f)));
                        cloud = _e891;
                        let _e894: vec2<f32> = texcoord_1;
                        let _e897: vec2<f32> = texcoord_1;
                        let _e901: vec2<f32> = texcoord_1;
                        let _e904: vec2<f32> = texcoord_1;
                        let _e908: f32 = cosmetic_noise(floor((_e904 * 100f)));
                        let _e910: vec2<f32> = texcoord_1;
                        let _e913: vec2<f32> = texcoord_1;
                        let _e917: vec2<f32> = texcoord_1;
                        let _e920: vec2<f32> = texcoord_1;
                        let _e924: f32 = cosmetic_noise(floor((_e920 * 100f)));
                        star = step(0.985f, _e924);
                        let _e927: vec4<f32> = (*col);
                        let _e946: f32 = cloud;
                        let _e950: f32 = lum;
                        let _e953: f32 = star;
                        let _e957: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e946)) * (0.5f + _e950)) + vec3((_e953 * 0.65f)));
                        (*col).x = _e957.x;
                        (*col).y = _e957.y;
                        (*col).z = _e957.z;
                    }
                }
            }
        }
    }
    let _e964: i32 = global.cosmetic_skin;
    if (_e964 != 0i) {
        {
            let _e967: vec2<f32> = cosmetic_finish();
            finish = _e967;
            let _e981: vec3<f32> = viewNormal;
            let _e1008: vec3<f32> = viewNormal;
            let _e1026: vec2<f32> = finish;
            let _e1030: vec2<f32> = finish;
            let _e1045: vec3<f32> = viewNormal;
            let _e1072: vec3<f32> = viewNormal;
            let _e1090: vec2<f32> = finish;
            let _e1094: vec2<f32> = finish;
            sheen = pow(clamp(dot(_e1072, normalize(vec3<f32>(-0.35f, 0.6f, 0.72f))), 0f, 1f), mix(44f, 8f, _e1094.y));
            let _e1099: vec4<f32> = (*col);
            let _e1101: vec4<f32> = (*col);
            let _e1107: f32 = rim;
            let _e1110: vec2<f32> = finish;
            let _e1116: vec4<f32> = (*col);
            let _e1120: vec2<f32> = finish;
            let _e1124: vec4<f32> = (*col);
            let _e1128: vec2<f32> = finish;
            let _e1132: f32 = sheen;
            let _e1135: vec3<f32> = (_e1101.xyz + (((vec3<f32>(0.12f, 0.16f, 0.2f) * _e1107) * (1f - _e1110.y)) + (mix(vec3(0.12f), (_e1124.xyz * 0.28f), vec3(_e1128.x)) * _e1132)));
            (*col).x = _e1135.x;
            (*col).y = _e1135.y;
            (*col).z = _e1135.z;
        }
    }
    let _e1142: i32 = global.cosmetic_preserve_palette;
    let _e1145: i32 = global.cosmetic_skin;
    if ((_e1142 != 0i) && (_e1145 != 0i)) {
        {
            let _e1149: vec3<f32> = nativeColor;
            let _e1151: vec3<f32> = nativeColor;
            let _e1153: vec3<f32> = nativeColor;
            let _e1155: vec3<f32> = nativeColor;
            let _e1157: vec3<f32> = nativeColor;
            let _e1160: vec3<f32> = nativeColor;
            let _e1162: vec3<f32> = nativeColor;
            let _e1164: vec3<f32> = nativeColor;
            let _e1166: vec3<f32> = nativeColor;
            let _e1168: vec3<f32> = nativeColor;
            let _e1172: vec3<f32> = nativeColor;
            let _e1174: vec3<f32> = nativeColor;
            let _e1176: vec3<f32> = nativeColor;
            let _e1178: vec3<f32> = nativeColor;
            let _e1180: vec3<f32> = nativeColor;
            let _e1183: vec3<f32> = nativeColor;
            let _e1185: vec3<f32> = nativeColor;
            let _e1187: vec3<f32> = nativeColor;
            let _e1189: vec3<f32> = nativeColor;
            let _e1191: vec3<f32> = nativeColor;
            chroma = (max(_e1160.x, max(_e1166.y, _e1168.z)) - min(_e1183.x, min(_e1189.y, _e1191.z)));
            let _e1197: vec4<f32> = (*col);
            let _e1199: vec4<f32> = (*col);
            let _e1207: f32 = chroma;
            let _e1211: vec4<f32> = (*col);
            let _e1213: vec3<f32> = nativeColor;
            let _e1219: f32 = chroma;
            let _e1224: vec3<f32> = mix(_e1211.xyz, _e1213, vec3((smoothstep(0.1f, 0.35f, _e1219) * 0.9f)));
            (*col).x = _e1224.x;
            (*col).y = _e1224.y;
            (*col).z = _e1224.z;
        }
    }
    let _e1231: i32 = global.cosmetic_effect;
    if (_e1231 != 0i) {
        {
            let _e1234: f32 = global.cosmetic_time;
            let _e1235: f32 = global.cosmetic_scroll;
            t = (_e1234 * _e1235);
            let _e1240: f32 = global.cosmetic_time;
            let _e1241: f32 = global.cosmetic_pulse;
            let _e1243: f32 = global.cosmetic_time;
            let _e1244: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1243 * _e1244))));
            let _e1252: vec2<f32> = texcoord_1;
            let _e1256: f32 = t;
            let _e1260: vec2<f32> = texcoord_1;
            let _e1264: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1260.y * 30f) - (_e1264 * 2f)))));
            let _e1273: f32 = rim;
            mask = (0.28f + (_e1273 * 0.72f));
            let _e1278: i32 = global.cosmetic_effect;
            if (_e1278 == 1i) {
                let _e1283: f32 = pulse;
                mask = (0.25f + (0.55f * _e1283));
            }
            let _e1286: i32 = global.cosmetic_effect;
            if (_e1286 == 3i) {
                let _e1294: f32 = wave;
                let _e1298: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1294) * 0.5f) + _e1298);
            }
            let _e1300: i32 = global.cosmetic_effect;
            if (_e1300 == 4i) {
                let _e1303: vec2<f32> = texcoord_1;
                let _e1306: f32 = t;
                let _e1309: f32 = t;
                let _e1315: vec2<f32> = texcoord_1;
                let _e1318: f32 = t;
                let _e1321: f32 = t;
                let _e1327: f32 = cosmetic_soft_noise(((_e1315 * 16f) + vec2<f32>((_e1318 * 0.3f), (-(_e1321) * 0.5f))));
                let _e1330: f32 = rim;
                mask = ((_e1327 * 0.45f) + (_e1330 * 0.6f));
            }
            let _e1334: i32 = global.cosmetic_effect;
            let _e1337: i32 = global.cosmetic_effect;
            if ((_e1334 == 5i) || (_e1337 == 7i)) {
                let _e1347: f32 = wave;
                let _e1352: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1347) * 0.65f)) + (_e1352 * 0.45f));
            }
            let _e1356: i32 = global.cosmetic_effect;
            if (_e1356 == 6i) {
                let _e1360: vec2<f32> = texcoord_1;
                let _e1364: vec2<f32> = texcoord_1;
                let _e1368: f32 = t;
                let _e1370: vec2<f32> = texcoord_1;
                let _e1374: f32 = t;
                let _e1378: vec2<f32> = texcoord_1;
                let _e1382: vec2<f32> = texcoord_1;
                let _e1386: f32 = t;
                let _e1388: vec2<f32> = texcoord_1;
                let _e1392: f32 = t;
                let _e1397: vec2<f32> = texcoord_1;
                let _e1401: vec2<f32> = texcoord_1;
                let _e1405: f32 = t;
                let _e1407: vec2<f32> = texcoord_1;
                let _e1411: f32 = t;
                let _e1415: vec2<f32> = texcoord_1;
                let _e1419: vec2<f32> = texcoord_1;
                let _e1423: f32 = t;
                let _e1425: vec2<f32> = texcoord_1;
                let _e1429: f32 = t;
                let _e1436: vec2<f32> = texcoord_1;
                let _e1440: vec2<f32> = texcoord_1;
                let _e1444: f32 = t;
                let _e1446: vec2<f32> = texcoord_1;
                let _e1450: f32 = t;
                let _e1454: vec2<f32> = texcoord_1;
                let _e1458: vec2<f32> = texcoord_1;
                let _e1462: f32 = t;
                let _e1464: vec2<f32> = texcoord_1;
                let _e1468: f32 = t;
                let _e1473: vec2<f32> = texcoord_1;
                let _e1477: vec2<f32> = texcoord_1;
                let _e1481: f32 = t;
                let _e1483: vec2<f32> = texcoord_1;
                let _e1487: f32 = t;
                let _e1491: vec2<f32> = texcoord_1;
                let _e1495: vec2<f32> = texcoord_1;
                let _e1499: f32 = t;
                let _e1501: vec2<f32> = texcoord_1;
                let _e1505: f32 = t;
                let _e1514: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1491.x * 31f) + sin(((_e1501.y * 29f) + _e1505))))), 16f)) + (_e1514 * 0.5f));
            }
            let _e1518: i32 = global.cosmetic_effect;
            if (_e1518 == 8i) {
                let _e1521: f32 = wave;
                let _e1524: f32 = rim;
                mask = ((_e1521 * 0.35f) + (_e1524 * 0.7f));
            }
            let _e1528: i32 = global.cosmetic_effect;
            if (_e1528 == 9i) {
                let _e1533: vec2<f32> = texcoord_1;
                let _e1537: f32 = t;
                let _e1539: vec2<f32> = texcoord_1;
                let _e1543: f32 = t;
                let _e1548: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1539.x * 40f) + _e1543)))) * _e1548);
            }
            let _e1553: vec3<f32> = global.cosmetic_primary;
            let _e1554: vec3<f32> = global.cosmetic_secondary;
            let _e1555: f32 = wave;
            energy = mix(_e1553, _e1554, vec3(_e1555));
            let _e1562: f32 = mask;
            let _e1566: f32 = global.cosmetic_intensity;
            let _e1568: f32 = pulse;
            strength = ((clamp(_e1562, 0f, 1f) * _e1566) * _e1568);
            let _e1571: i32 = global.cosmetic_preserve_palette;
            if (_e1571 != 0i) {
                let _e1574: f32 = strength;
                strength = (_e1574 * 0.65f);
            }
            let _e1577: vec4<f32> = (*col);
            let _e1579: vec4<f32> = (*col);
            let _e1581: vec3<f32> = energy;
            let _e1583: f32 = lum;
            let _e1588: f32 = strength;
            let _e1593: f32 = strength;
            let _e1599: vec4<f32> = (*col);
            let _e1601: vec3<f32> = energy;
            let _e1603: f32 = lum;
            let _e1608: f32 = strength;
            let _e1613: f32 = strength;
            let _e1620: vec3<f32> = mix(_e1599.xyz, (_e1601 * (0.4f + (_e1603 * 0.6f))), vec3(clamp((_e1613 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1620.x;
            (*col).y = _e1620.y;
            (*col).z = _e1620.z;
            let _e1627: vec4<f32> = (*col);
            let _e1629: vec4<f32> = (*col);
            let _e1631: vec3<f32> = energy;
            let _e1632: f32 = strength;
            let _e1636: vec3<f32> = (_e1629.xyz + ((_e1631 * _e1632) * 0.45f));
            (*col).x = _e1636.x;
            (*col).y = _e1636.y;
            (*col).z = _e1636.z;
        }
    }
    let _e1643: f32 = global.cosmetic_dissolve;
    if (_e1643 > 0f) {
        {
            let _e1646: vec2<f32> = texcoord_1;
            let _e1649: vec2<f32> = texcoord_1;
            let _e1653: vec2<f32> = texcoord_1;
            let _e1656: vec2<f32> = texcoord_1;
            let _e1660: f32 = cosmetic_noise(floor((_e1656 * 64f)));
            n_2 = _e1660;
            let _e1662: f32 = n_2;
            let _e1663: f32 = global.cosmetic_dissolve;
            if (_e1662 < _e1663) {
                discard;
            }
            let _e1665: vec4<f32> = (*col);
            let _e1667: vec4<f32> = (*col);
            let _e1669: vec3<f32> = global.cosmetic_primary;
            let _e1672: f32 = global.cosmetic_dissolve;
            let _e1676: f32 = global.cosmetic_dissolve;
            let _e1677: f32 = global.cosmetic_dissolve;
            let _e1680: f32 = n_2;
            let _e1686: vec3<f32> = (_e1667.xyz + ((_e1669 * (1f - smoothstep(_e1676, (_e1677 + 0.08f), _e1680))) * 0.3f));
            (*col).x = _e1686.x;
            (*col).y = _e1686.y;
            (*col).z = _e1686.z;
            return;
        }
    } else {
        return;
    }
}

fn physical_to_linear(c: vec3<f32>) -> vec3<f32> {
    var c_1: vec3<f32>;
    var low: vec3<f32>;
    var high: vec3<f32>;

    c_1 = c;
    let _e345: vec3<f32> = c_1;
    low = (_e345 / vec3(12.92f));
    let _e350: vec3<f32> = c_1;
    let _e359: vec3<f32> = c_1;
    let _e371: vec3<f32> = c_1;
    let _e380: vec3<f32> = c_1;
    high = pow(max(((_e380 + vec3(0.055f)) / vec3(1.055f)), vec3(0f)), vec3(2.4f));
    let _e401: vec3<f32> = c_1;
    let _e403: vec3<f32> = low;
    let _e404: vec3<f32> = high;
    let _e410: vec3<f32> = c_1;
    return mix(_e403, _e404, step(vec3(0.04045f), _e410));
}

fn physical_to_srgb(c_2: vec3<f32>) -> vec3<f32> {
    var c_3: vec3<f32>;
    var low_1: vec3<f32>;
    var high_1: vec3<f32>;

    c_3 = c_2;
    let _e348: vec3<f32> = c_3;
    c_3 = max(_e348, vec3(0f));
    let _e352: vec3<f32> = c_3;
    low_1 = (_e352 * 12.92f);
    let _e362: vec3<f32> = c_3;
    high_1 = ((1.055f * pow(_e362, vec3(0.41666666f))) - vec3(0.055f));
    let _e380: vec3<f32> = c_3;
    let _e382: vec3<f32> = low_1;
    let _e383: vec3<f32> = high_1;
    let _e389: vec3<f32> = c_3;
    return mix(_e382, _e383, step(vec3(0.0031308f), _e389));
}

fn physical_direct(albedo: vec3<f32>, n_3: vec3<f32>, v: vec3<f32>, l: vec3<f32>, radiance: vec3<f32>, metallic: f32, roughness: f32) -> vec3<f32> {
    var albedo_1: vec3<f32>;
    var n_4: vec3<f32>;
    var v_1: vec3<f32>;
    var l_1: vec3<f32>;
    var radiance_1: vec3<f32>;
    var metallic_1: f32;
    var roughness_1: f32;
    var nl: f32;
    var nv: f32;
    var halfSum: vec3<f32>;
    var h: vec3<f32>;
    var nh: f32;
    var vh: f32;
    var a: f32;
    var a2_: f32;
    var denominator: f32;
    var distribution: f32;
    var k: f32;
    var geometry: f32;
    var f0_: vec3<f32>;
    var fresnel: vec3<f32>;
    var spec: vec3<f32>;
    var kd: vec3<f32>;

    albedo_1 = albedo;
    n_4 = n_3;
    v_1 = v;
    l_1 = l;
    radiance_1 = radiance;
    metallic_1 = metallic;
    roughness_1 = roughness;
    let _e359: vec3<f32> = n_4;
    let _e360: vec3<f32> = l_1;
    let _e365: vec3<f32> = n_4;
    let _e366: vec3<f32> = l_1;
    nl = max(dot(_e365, _e366), 0f);
    let _e373: vec3<f32> = n_4;
    let _e374: vec3<f32> = v_1;
    let _e379: vec3<f32> = n_4;
    let _e380: vec3<f32> = v_1;
    nv = max(dot(_e379, _e380), 0f);
    let _e385: f32 = nl;
    let _e388: f32 = nv;
    if ((_e385 <= 0f) || (_e388 <= 0f)) {
        return vec3(0f);
    }
    let _e394: vec3<f32> = v_1;
    let _e395: vec3<f32> = l_1;
    halfSum = (_e394 + _e395);
    let _e398: vec3<f32> = halfSum;
    let _e401: vec3<f32> = halfSum;
    let _e402: vec3<f32> = halfSum;
    let _e407: vec3<f32> = halfSum;
    let _e408: vec3<f32> = halfSum;
    let _e414: vec3<f32> = halfSum;
    let _e415: vec3<f32> = halfSum;
    let _e420: vec3<f32> = halfSum;
    let _e421: vec3<f32> = halfSum;
    h = (_e398 * inverseSqrt(max(dot(_e420, _e421), 0.000001f)));
    let _e430: vec3<f32> = n_4;
    let _e431: vec3<f32> = h;
    let _e437: vec3<f32> = n_4;
    let _e438: vec3<f32> = h;
    nh = clamp(dot(_e437, _e438), 0f, 1f);
    let _e446: vec3<f32> = v_1;
    let _e447: vec3<f32> = h;
    let _e453: vec3<f32> = v_1;
    let _e454: vec3<f32> = h;
    vh = clamp(dot(_e453, _e454), 0f, 1f);
    let _e460: f32 = roughness_1;
    let _e461: f32 = roughness_1;
    a = (_e460 * _e461);
    let _e464: f32 = a;
    let _e465: f32 = a;
    a2_ = (_e464 * _e465);
    let _e468: f32 = nh;
    let _e469: f32 = nh;
    let _e471: f32 = a2_;
    denominator = (((_e468 * _e469) * (_e471 - 1f)) + 1f);
    let _e478: f32 = a2_;
    let _e480: f32 = denominator;
    let _e482: f32 = denominator;
    let _e486: f32 = denominator;
    let _e488: f32 = denominator;
    distribution = (_e478 / max(((3.1415927f * _e486) * _e488), 0.000001f));
    let _e494: f32 = roughness_1;
    let _e497: f32 = roughness_1;
    k = (((_e494 + 1f) * (_e497 + 1f)) / 8f);
    let _e504: f32 = nv;
    let _e505: f32 = nv;
    let _e507: f32 = k;
    let _e510: f32 = k;
    let _e513: f32 = nv;
    let _e515: f32 = k;
    let _e518: f32 = k;
    let _e523: f32 = nl;
    let _e524: f32 = nl;
    let _e526: f32 = k;
    let _e529: f32 = k;
    let _e532: f32 = nl;
    let _e534: f32 = k;
    let _e537: f32 = k;
    geometry = ((_e504 / max(((_e513 * (1f - _e515)) + _e518), 0.000001f)) * (_e523 / max(((_e532 * (1f - _e534)) + _e537), 0.000001f)));
    let _e550: vec3<f32> = albedo_1;
    let _e551: f32 = metallic_1;
    f0_ = mix(vec3(0.04f), _e550, vec3(_e551));
    let _e555: vec3<f32> = f0_;
    let _e558: vec3<f32> = f0_;
    let _e561: f32 = vh;
    let _e565: f32 = vh;
    fresnel = (_e555 + ((vec3(1f) - _e558) * pow((1f - _e565), 5f)));
    let _e572: f32 = distribution;
    let _e573: f32 = geometry;
    let _e575: vec3<f32> = fresnel;
    let _e578: f32 = nv;
    let _e580: f32 = nl;
    let _e584: f32 = nv;
    let _e586: f32 = nl;
    spec = (((_e572 * _e573) * _e575) / vec3(max(((4f * _e584) * _e586), 0.0001f)));
    let _e595: vec3<f32> = fresnel;
    let _e598: f32 = metallic_1;
    kd = ((vec3(1f) - _e595) * (1f - _e598));
    let _e602: vec3<f32> = kd;
    let _e603: vec3<f32> = albedo_1;
    let _e608: vec3<f32> = spec;
    let _e610: vec3<f32> = radiance_1;
    let _e612: f32 = nl;
    return (((((_e602 * _e603) / vec3(3.1415927f)) + _e608) * _e610) * _e612);
}

fn physical_material(base: vec3<f32>, n_5: vec3<f32>, orm: vec4<f32>) -> vec3<f32> {
    var base_1: vec3<f32>;
    var n_6: vec3<f32>;
    var orm_1: vec4<f32>;
    var ao: f32;
    var roughness_2: f32;
    var metallic_2: f32;
    var eye: vec3<f32>;
    var v_2: vec3<f32>;
    var albedo_2: vec3<f32>;
    var ambientRadiance: vec3<f32>;
    var f0_1: vec3<f32>;
    var result: vec3<f32>;
    var l1_: vec3<f32>;
    var l2_: vec3<f32>;

    base_1 = base;
    n_6 = n_5;
    orm_1 = orm;
    let _e349: vec4<f32> = orm_1;
    let _e353: vec4<f32> = orm_1;
    ao = clamp(_e353.x, 0f, 1f);
    let _e359: vec4<f32> = orm_1;
    let _e363: vec4<f32> = orm_1;
    roughness_2 = clamp(_e363.y, 0.08f, 1f);
    let _e369: vec4<f32> = orm_1;
    let _e373: vec4<f32> = orm_1;
    metallic_2 = clamp(_e373.z, 0f, 1f);
    let _e379: mat4x4<f32> = global.view_mtx;
    let _e380: vec3<f32> = surface_position_1;
    eye = -((_e379 * vec4<f32>(_e380.x, _e380.y, _e380.z, 1f)).xyz);
    let _e390: vec3<f32> = eye;
    let _e393: vec3<f32> = eye;
    let _e394: vec3<f32> = eye;
    let _e399: vec3<f32> = eye;
    let _e400: vec3<f32> = eye;
    let _e406: vec3<f32> = eye;
    let _e407: vec3<f32> = eye;
    let _e412: vec3<f32> = eye;
    let _e413: vec3<f32> = eye;
    eye = (_e390 * inverseSqrt(max(dot(_e412, _e413), 0.000001f)));
    let _e419: mat4x4<f32> = global.view_mtx;
    let _e429: mat4x4<f32> = global.view_mtx;
    let _e440: vec3<f32> = eye;
    v_2 = (transpose(mat3x3<f32>(_e429[0].xyz, _e429[1].xyz, _e429[2].xyz)) * _e440);
    let _e443: vec3<f32> = base_1;
    let _e444: vec3<f32> = global.diffuse;
    let _e448: vec3<f32> = base_1;
    let _e449: vec3<f32> = global.diffuse;
    let _e456: vec3<f32> = base_1;
    let _e457: vec3<f32> = global.diffuse;
    let _e461: vec3<f32> = base_1;
    let _e462: vec3<f32> = global.diffuse;
    let _e469: vec3<f32> = physical_to_linear(clamp((_e461 * _e462), vec3(0f), vec3(1f)));
    albedo_2 = _e469;
    let _e471: vec3<f32> = global.ambient;
    let _e472: vec3<f32> = global.light1col;
    let _e473: vec3<f32> = global.light2col;
    let _e478: vec3<f32> = global.ambient;
    let _e479: vec3<f32> = global.light1col;
    let _e480: vec3<f32> = global.light2col;
    ambientRadiance = max((_e478 * (_e479 + _e480)), vec3(0f));
    let _e493: vec3<f32> = albedo_2;
    let _e494: f32 = metallic_2;
    f0_1 = mix(vec3(0.04f), _e493, vec3(_e494));
    let _e499: f32 = metallic_2;
    let _e501: vec3<f32> = albedo_2;
    let _e503: vec3<f32> = f0_1;
    let _e507: vec3<f32> = ambientRadiance;
    let _e509: f32 = ao;
    result = (((((1f - _e499) * _e501) + (_e503 * 0.35f)) * _e507) * _e509);
    let _e512: vec3<f32> = global.light1vec;
    let _e516: vec3<f32> = global.light1vec;
    let _e517: vec3<f32> = global.light1vec;
    let _e522: vec3<f32> = global.light1vec;
    let _e523: vec3<f32> = global.light1vec;
    let _e529: vec3<f32> = global.light1vec;
    let _e530: vec3<f32> = global.light1vec;
    let _e535: vec3<f32> = global.light1vec;
    let _e536: vec3<f32> = global.light1vec;
    l1_ = (-(_e512) * inverseSqrt(max(dot(_e535, _e536), 0.000001f)));
    let _e543: vec3<f32> = global.light2vec;
    let _e547: vec3<f32> = global.light2vec;
    let _e548: vec3<f32> = global.light2vec;
    let _e553: vec3<f32> = global.light2vec;
    let _e554: vec3<f32> = global.light2vec;
    let _e560: vec3<f32> = global.light2vec;
    let _e561: vec3<f32> = global.light2vec;
    let _e566: vec3<f32> = global.light2vec;
    let _e567: vec3<f32> = global.light2vec;
    l2_ = (-(_e543) * inverseSqrt(max(dot(_e566, _e567), 0.000001f)));
    let _e574: vec3<f32> = result;
    let _e582: vec3<f32> = global.light1col;
    let _e590: vec3<f32> = albedo_2;
    let _e591: vec3<f32> = n_6;
    let _e592: vec3<f32> = v_2;
    let _e593: vec3<f32> = l1_;
    let _e597: vec3<f32> = global.light1col;
    let _e603: f32 = metallic_2;
    let _e604: f32 = roughness_2;
    let _e605: vec3<f32> = physical_direct(_e590, _e591, _e592, _e593, (max(_e597, vec3(0f)) * 3.1415927f), _e603, _e604);
    result = (_e574 + _e605);
    let _e607: vec3<f32> = result;
    let _e615: vec3<f32> = global.light2col;
    let _e623: vec3<f32> = albedo_2;
    let _e624: vec3<f32> = n_6;
    let _e625: vec3<f32> = v_2;
    let _e626: vec3<f32> = l2_;
    let _e630: vec3<f32> = global.light2col;
    let _e636: f32 = metallic_2;
    let _e637: f32 = roughness_2;
    let _e638: vec3<f32> = physical_direct(_e623, _e624, _e625, _e626, (max(_e630, vec3(0f)) * 3.1415927f), _e636, _e637);
    result = (_e607 + _e638);
    let _e640: vec3<f32> = result;
    let _e644: vec3<f32> = global.emission;
    let _e651: vec3<f32> = global.emission;
    let _e655: vec3<f32> = physical_to_linear(max(_e651, vec3(0f)));
    result = (_e640 + _e655);
    let _e658: vec3<f32> = result;
    let _e659: vec3<f32> = physical_to_srgb(_e658);
    return _e659;
}

fn apply_material_lighting(col_1: ptr<function, vec4<f32>>) {
    var physicalActive: bool = false;
    var n_7: vec3<f32>;
    var d1_: f32;
    var d2_: f32;
    var l1_1: f32;
    var l2_1: f32;
    var local_1: vec4<f32>;
    var sm: vec4<f32>;
    var roughness_3: f32;
    var viewDir: vec3<f32> = vec3<f32>(0f, 0f, 1f);
    var h1_: vec3<f32>;
    var h2_: vec3<f32>;
    var exponent: f32;
    var highlight: f32;
    var e: vec3<f32>;

    let _e344: i32 = global._prime_advanced_materials;
    if !((_e344 != 0i)) {
        return;
    }
    let _e350: i32 = global._prime_use_light;
    if (_e350 != 0i) {
        {
            let _e353: vec3<f32> = mapped_normal();
            n_7 = _e353;
            let _e358: vec3<f32> = global.light1vec;
            let _e359: vec3<f32> = n_7;
            let _e365: vec3<f32> = global.light1vec;
            let _e366: vec3<f32> = n_7;
            d1_ = max(0f, -(dot(_e365, _e366)));
            let _e374: vec3<f32> = global.light2vec;
            let _e375: vec3<f32> = n_7;
            let _e381: vec3<f32> = global.light2vec;
            let _e382: vec3<f32> = n_7;
            d2_ = max(0f, -(dot(_e381, _e382)));
            let _e392: vec3<f32> = global.light1col;
            l1_1 = dot(_e392, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            let _e404: vec3<f32> = global.light2col;
            l2_1 = dot(_e404, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
            let _e411: i32 = global._prime_use_specular_map;
            if (_e411 != 0i) {
                let _e415: vec2<f32> = texcoord_1;
                let _e416: vec4<f32> = prime_sample_specular_tex(_e415);
                local_1 = _e416;
            } else {
                let _e417: vec3<f32> = global.specular;
                let _e419: vec3<f32> = global.specular;
                let _e421: vec3<f32> = global.specular;
                let _e423: vec3<f32> = global.specular;
                let _e426: vec3<f32> = global.specular;
                let _e428: vec3<f32> = global.specular;
                let _e430: vec3<f32> = global.specular;
                let _e432: vec3<f32> = global.specular;
                let _e434: vec3<f32> = global.specular;
                let _e437: vec3<f32> = global.specular;
                local_1 = vec4<f32>(max(max(_e432.x, _e434.y), _e437.z), 0.55f, 0f, 1f);
            }
            let _e445: vec4<f32> = local_1;
            sm = _e445;
            let _e447: i32 = global._prime_use_specular_map;
            let _e450: vec4<f32> = sm;
            if ((_e447 != 0i) && (_e450.w < 0.5f)) {
                {
                    let _e455: i32 = global._prime_use_texture;
                    let _e458: i32 = global.mat_mode;
                    let _e462: i32 = global._prime_use_override;
                    let _e467: i32 = global._prime_use_pal_override;
                    let _e472: i32 = global._prime_use_flat;
                    if (((((_e455 != 0i) && (_e458 == 0i)) && !((_e462 != 0i))) && !((_e467 != 0i))) && !((_e472 != 0i))) {
                        {
                            physicalActive = true;
                            let _e478: vec4<f32> = (*col_1);
                            let _e481: vec2<f32> = texcoord_1;
                            let _e482: vec4<f32> = prime_sample_tex(_e481);
                            let _e487: vec2<f32> = texcoord_1;
                            let _e488: vec4<f32> = prime_sample_tex(_e487);
                            let _e490: vec3<f32> = n_7;
                            let _e491: vec4<f32> = sm;
                            let _e492: vec3<f32> = physical_material(_e488.xyz, _e490, _e491);
                            (*col_1).x = _e492.x;
                            (*col_1).y = _e492.y;
                            (*col_1).z = _e492.z;
                        }
                    }
                }
            } else {
                {
                    let _e499: vec4<f32> = (*col_1);
                    let _e501: vec4<f32> = (*col_1);
                    let _e505: f32 = d1_;
                    let _e506: f32 = l1_1;
                    let _e508: f32 = d2_;
                    let _e509: f32 = l2_1;
                    let _e516: f32 = d1_;
                    let _e517: f32 = l1_1;
                    let _e519: f32 = d2_;
                    let _e520: f32 = l2_1;
                    let _e530: f32 = d1_;
                    let _e531: f32 = l1_1;
                    let _e533: f32 = d2_;
                    let _e534: f32 = l2_1;
                    let _e541: f32 = d1_;
                    let _e542: f32 = l1_1;
                    let _e544: f32 = d2_;
                    let _e545: f32 = l2_1;
                    let _e554: vec3<f32> = (_e501.xyz * mix(0.92f, 1.1f, clamp((((_e541 * _e542) + (_e544 * _e545)) * 0.65f), 0f, 1f)));
                    (*col_1).x = _e554.x;
                    (*col_1).y = _e554.y;
                    (*col_1).z = _e554.z;
                    let _e561: vec4<f32> = sm;
                    let _e565: vec4<f32> = sm;
                    roughness_3 = clamp(_e565.y, 0.04f, 1f);
                    let _e576: vec3<f32> = global.light1vec;
                    let _e578: vec3<f32> = viewDir;
                    let _e580: vec3<f32> = global.light1vec;
                    let _e582: vec3<f32> = viewDir;
                    h1_ = normalize((-(_e580) + _e582));
                    let _e586: vec3<f32> = global.light2vec;
                    let _e588: vec3<f32> = viewDir;
                    let _e590: vec3<f32> = global.light2vec;
                    let _e592: vec3<f32> = viewDir;
                    h2_ = normalize((-(_e590) + _e592));
                    let _e601: f32 = roughness_3;
                    exponent = mix(72f, 4f, _e601);
                    let _e606: vec3<f32> = n_7;
                    let _e607: vec3<f32> = h1_;
                    let _e612: vec3<f32> = n_7;
                    let _e613: vec3<f32> = h1_;
                    let _e620: vec3<f32> = n_7;
                    let _e621: vec3<f32> = h1_;
                    let _e626: vec3<f32> = n_7;
                    let _e627: vec3<f32> = h1_;
                    let _e631: f32 = exponent;
                    let _e633: f32 = l1_1;
                    let _e637: vec3<f32> = n_7;
                    let _e638: vec3<f32> = h2_;
                    let _e643: vec3<f32> = n_7;
                    let _e644: vec3<f32> = h2_;
                    let _e651: vec3<f32> = n_7;
                    let _e652: vec3<f32> = h2_;
                    let _e657: vec3<f32> = n_7;
                    let _e658: vec3<f32> = h2_;
                    let _e662: f32 = exponent;
                    let _e664: f32 = l2_1;
                    highlight = ((pow(max(dot(_e626, _e627), 0f), _e631) * _e633) + (pow(max(dot(_e657, _e658), 0f), _e662) * _e664));
                    let _e668: vec4<f32> = (*col_1);
                    let _e670: vec4<f32> = (*col_1);
                    let _e672: f32 = highlight;
                    let _e673: vec4<f32> = sm;
                    let _e677: vec4<f32> = sm;
                    let _e686: vec3<f32> = (_e670.xyz + vec3(((_e672 * clamp(_e677.x, 0f, 1f)) * 0.16f)));
                    (*col_1).x = _e686.x;
                    (*col_1).y = _e686.y;
                    (*col_1).z = _e686.z;
                }
            }
        }
    }
    let _e693: i32 = global._prime_use_emissive_map;
    if (_e693 != 0i) {
        {
            let _e697: vec2<f32> = texcoord_1;
            let _e698: vec4<f32> = prime_sample_emissive_tex(_e697);
            e = _e698.xyz;
            let _e701: bool = physicalActive;
            if _e701 {
                let _e702: vec4<f32> = (*col_1);
                let _e704: vec4<f32> = (*col_1);
                let _e706: vec4<f32> = (*col_1);
                let _e708: vec3<f32> = physical_to_linear(_e706.xyz);
                let _e710: vec3<f32> = e;
                let _e711: vec3<f32> = physical_to_linear(_e710);
                let _e714: f32 = global.emissive_intensity;
                let _e717: vec4<f32> = (*col_1);
                let _e719: vec4<f32> = (*col_1);
                let _e721: vec3<f32> = physical_to_linear(_e719.xyz);
                let _e723: vec3<f32> = e;
                let _e724: vec3<f32> = physical_to_linear(_e723);
                let _e727: f32 = global.emissive_intensity;
                let _e730: vec3<f32> = physical_to_srgb((_e721 + ((_e724 * 0.75f) * _e727)));
                (*col_1).x = _e730.x;
                (*col_1).y = _e730.y;
                (*col_1).z = _e730.z;
                return;
            } else {
                let _e737: vec4<f32> = (*col_1);
                let _e739: vec4<f32> = (*col_1);
                let _e741: vec3<f32> = e;
                let _e744: f32 = global.emissive_intensity;
                let _e746: vec3<f32> = (_e739.xyz + ((_e741 * 0.75f) * _e744));
                (*col_1).x = _e746.x;
                (*col_1).y = _e746.y;
                (*col_1).z = _e746.z;
                return;
            }
        }
    } else {
        return;
    }
}

fn toon_color(vtx_color: vec4<f32>) -> vec4<f32> {
    var vtx_color_1: vec4<f32>;

    vtx_color_1 = vtx_color;
    let _e345: vec4<f32> = vtx_color_1;
    let _e351: vec4<f32> = vtx_color_1;
    let _e360: vec3<f32> = global.toon_table[i32(clamp((_e351.x * 31f), 0f, 31f))];
    let _e361: vec4<f32> = vtx_color_1;
    return vec4<f32>(_e360.x, _e360.y, _e360.z, _e361.w);
}

fn cel_shade(c_4: vec3<f32>) -> vec3<f32> {
    var c_5: vec3<f32>;
    var steps: f32;
    var lum_1: f32;
    var scaled: f32;
    var lower: f32;
    var level: f32;
    var banded: vec3<f32>;
    var grey: f32;

    c_5 = c_4;
    let _e345: i32 = global.cel_bands;
    steps = f32(_e345);
    let _e348: vec3<f32> = c_5;
    let _e350: vec3<f32> = c_5;
    let _e352: vec3<f32> = c_5;
    let _e354: vec3<f32> = c_5;
    let _e357: vec3<f32> = c_5;
    let _e359: vec3<f32> = c_5;
    let _e361: vec3<f32> = c_5;
    let _e363: vec3<f32> = c_5;
    let _e365: vec3<f32> = c_5;
    let _e368: vec3<f32> = c_5;
    lum_1 = max(max(_e363.x, _e365.y), _e368.z);
    let _e372: f32 = lum_1;
    if (_e372 <= 0f) {
        {
            let _e375: vec3<f32> = c_5;
            return _e375;
        }
    }
    let _e376: f32 = lum_1;
    let _e377: f32 = steps;
    scaled = ((_e376 * _e377) - 0.5f);
    let _e383: f32 = scaled;
    lower = floor(_e383);
    let _e386: f32 = lower;
    let _e391: f32 = scaled;
    let _e392: f32 = lower;
    let _e396: f32 = scaled;
    let _e397: f32 = lower;
    let _e401: f32 = steps;
    level = (((_e386 + 0.5f) + smoothstep(0.46f, 0.54f, (_e396 - _e397))) / _e401);
    let _e404: vec3<f32> = c_5;
    let _e405: f32 = level;
    let _e406: f32 = lum_1;
    banded = (_e404 * (_e405 / _e406));
    let _e415: vec3<f32> = banded;
    grey = dot(_e415, vec3<f32>(0.299f, 0.587f, 0.114f));
    let _e422: f32 = grey;
    let _e426: f32 = grey;
    let _e428: vec3<f32> = banded;
    let _e434: f32 = grey;
    let _e438: f32 = grey;
    let _e440: vec3<f32> = banded;
    return clamp(mix(vec3(_e438), _e440, vec3(1.35f)), vec3(0f), vec3(1f));
}

fn prime_original_main() {
    var col_2: vec4<f32>;
    var local_2: vec4<f32>;
    var texcolor: vec4<f32>;
    var toon: vec4<f32>;
    var detail: f32;
    var tinted: vec3<f32>;
    var local_3: vec4<f32>;
    var depth: f32;
    var density: f32 = 0f;

    let _e344: i32 = global._prime_use_texture;
    if (_e344 != 0i) {
        {
            let _e347: i32 = global._prime_use_pal_override;
            if (_e347 != 0i) {
                let _e350: vec4<f32> = global.pal_override_color;
                let _e351: vec3<f32> = _e350.xyz;
                let _e353: vec2<f32> = texcoord_1;
                let _e354: vec4<f32> = prime_sample_tex(_e353);
                local_2 = vec4<f32>(_e351.x, _e351.y, _e351.z, _e354.w);
            } else {
                let _e361: vec2<f32> = texcoord_1;
                let _e362: vec4<f32> = prime_sample_tex(_e361);
                local_2 = _e362;
            }
            let _e364: vec4<f32> = local_2;
            texcolor = _e364;
            let _e366: i32 = global._prime_use_flat;
            let _e369: i32 = global._prime_use_pal_override;
            let _e374: i32 = global.textured_player_skin;
            if (((_e366 != 0i) && !((_e369 != 0i))) && (_e374 == 0i)) {
                {
                    let _e378: vec4<f32> = texcolor;
                    let _e380: vec3<f32> = global.flat_color;
                    texcolor.x = _e380.x;
                    texcolor.y = _e380.y;
                    texcolor.z = _e380.z;
                }
            }
            let _e387: i32 = global.mat_mode;
            if (_e387 == 1i) {
                {
                    let _e390: vec4<f32> = texcolor;
                    let _e392: vec4<f32> = texcolor;
                    let _e395: vec4<f32> = color_1;
                    let _e398: vec4<f32> = texcolor;
                    let _e404: vec4<f32> = texcolor;
                    let _e406: vec4<f32> = texcolor;
                    let _e409: vec4<f32> = color_1;
                    let _e412: vec4<f32> = texcolor;
                    let _e418: vec4<f32> = texcolor;
                    let _e420: vec4<f32> = texcolor;
                    let _e423: vec4<f32> = color_1;
                    let _e426: vec4<f32> = texcolor;
                    let _e432: f32 = global.mat_alpha;
                    let _e433: vec4<f32> = color_1;
                    col_2 = vec4<f32>(((_e390.x * _e392.w) + (_e395.x * (1f - _e398.w))), ((_e404.y * _e406.w) + (_e409.y * (1f - _e412.w))), ((_e418.z * _e420.w) + (_e423.z * (1f - _e426.w))), (_e432 * _e433.w));
                }
            } else {
                let _e437: i32 = global.mat_mode;
                if (_e437 == 2i) {
                    {
                        let _e441: vec4<f32> = color_1;
                        let _e442: vec4<f32> = toon_color(_e441);
                        toon = _e442;
                        let _e444: vec4<f32> = texcolor;
                        let _e446: vec4<f32> = color_1;
                        let _e449: vec4<f32> = toon;
                        let _e451: vec3<f32> = ((_e444.xyz * _e446.x) + _e449.xyz);
                        let _e452: f32 = global.mat_alpha;
                        let _e453: vec4<f32> = texcolor;
                        let _e456: vec4<f32> = color_1;
                        col_2 = vec4<f32>(_e451.x, _e451.y, _e451.z, ((_e452 * _e453.w) * _e456.w));
                    }
                } else {
                    {
                        let _e463: vec4<f32> = color_1;
                        let _e464: vec4<f32> = texcolor;
                        let _e465: vec3<f32> = _e464.xyz;
                        let _e466: f32 = global.mat_alpha;
                        let _e467: vec4<f32> = texcolor;
                        col_2 = (_e463 * vec4<f32>(_e465.x, _e465.y, _e465.z, (_e466 * _e467.w)));
                    }
                }
            }
            let _e475: i32 = global._prime_use_override;
            if (_e475 != 0i) {
                {
                    let _e478: i32 = global.textured_player_skin;
                    if (_e478 > 0i) {
                        {
                            let _e481: i32 = global.textured_player_skin;
                            if (_e481 == 2i) {
                                {
                                    let _e486: vec4<f32> = texcolor;
                                    let _e492: vec4<f32> = texcolor;
                                    let _e501: vec4<f32> = texcolor;
                                    let _e507: vec4<f32> = texcolor;
                                    detail = smoothstep(0.05f, 0.85f, dot(_e507.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f)));
                                    let _e516: vec4<f32> = global.override_color;
                                    let _e520: f32 = detail;
                                    tinted = (_e516.xyz * (0.25f + (0.75f * _e520)));
                                    let _e525: vec4<f32> = col_2;
                                    let _e528: vec4<f32> = texcolor;
                                    let _e532: vec4<f32> = texcolor;
                                    let _e538: vec3<f32> = tinted;
                                    let _e539: vec4<f32> = texcolor;
                                    let _e543: vec4<f32> = texcolor;
                                    let _e550: vec3<f32> = mix(_e538, pow(_e543.xyz, vec3(0.7f)), vec3(0.25f));
                                    col_2.x = _e550.x;
                                    col_2.y = _e550.y;
                                    col_2.z = _e550.z;
                                }
                            } else {
                                {
                                    let _e557: vec4<f32> = col_2;
                                    let _e559: vec4<f32> = col_2;
                                    let _e561: vec4<f32> = texcolor;
                                    let _e564: vec4<f32> = col_2;
                                    let _e566: vec4<f32> = texcolor;
                                    let _e575: vec4<f32> = col_2;
                                    let _e577: vec4<f32> = texcolor;
                                    let _e580: vec4<f32> = col_2;
                                    let _e582: vec4<f32> = texcolor;
                                    let _e593: vec3<f32> = clamp((mix(_e580.xyz, _e582.xyz, vec3(0.8f)) * 1.25f), vec3(0f), vec3(1f));
                                    col_2.x = _e593.x;
                                    col_2.y = _e593.y;
                                    col_2.z = _e593.z;
                                }
                            }
                        }
                    } else {
                        {
                            let _e600: vec4<f32> = col_2;
                            let _e602: vec4<f32> = global.override_color;
                            let _e603: vec3<f32> = _e602.xyz;
                            col_2.x = _e603.x;
                            col_2.y = _e603.y;
                            col_2.z = _e603.z;
                        }
                    }
                    let _e611: vec4<f32> = col_2;
                    let _e613: vec4<f32> = global.override_color;
                    col_2.w = (_e611.w * _e613.w);
                }
            }
        }
    } else {
        let _e616: i32 = global._prime_use_override;
        if (_e616 != 0i) {
            {
                let _e619: vec4<f32> = global.override_color;
                col_2 = _e619;
            }
        } else {
            {
                let _e620: i32 = global.mat_mode;
                if (_e620 == 2i) {
                    let _e624: vec4<f32> = color_1;
                    let _e625: vec4<f32> = toon_color(_e624);
                    local_3 = _e625;
                } else {
                    let _e626: vec4<f32> = color_1;
                    local_3 = _e626;
                }
                let _e628: vec4<f32> = local_3;
                col_2 = _e628;
                let _e630: vec4<f32> = col_2;
                let _e632: f32 = global.mat_alpha;
                col_2.w = (_e630.w * _e632);
            }
        }
    }
    apply_material_lighting((&col_2));
    apply_cosmetics((&col_2));
    let _e638: i32 = global._prime_player_outline_mask;
    if (_e638 != 0i) {
        {
            let _e641: vec4<f32> = col_2;
            if (_e641.w <= 0.01f) {
                discard;
            }
            let _e645: vec4<f32> = col_2;
            let _e647: vec3<f32> = global.player_outline_color;
            col_2.x = _e647.x;
            col_2.y = _e647.y;
            col_2.z = _e647.z;
        }
    }
    let _e654: i32 = global.cel_bands;
    if (_e654 > 0i) {
        {
            let _e657: vec4<f32> = col_2;
            let _e659: vec4<f32> = col_2;
            let _e661: vec4<f32> = col_2;
            let _e663: vec3<f32> = cel_shade(_e661.xyz);
            col_2.x = _e663.x;
            col_2.y = _e663.y;
            col_2.z = _e663.z;
        }
    }
    let _e670: i32 = global._prime_fog_enable;
    if (_e670 != 0i) {
        {
            let _e674: vec4<f32> = gl_FragCoord;
            depth = _e674.z;
            let _e679: f32 = depth;
            let _e680: f32 = global.fog_max;
            if (_e679 >= _e680) {
                {
                    density = 1f;
                }
            } else {
                let _e683: f32 = depth;
                let _e684: f32 = global.fog_min;
                if (_e683 > _e684) {
                    {
                        let _e686: f32 = depth;
                        let _e687: f32 = global.fog_min;
                        let _e689: f32 = global.fog_max;
                        let _e690: f32 = global.fog_min;
                        density = ((((_e686 - _e687) / (_e689 - _e690)) * 124f) / 128f);
                    }
                }
            }
            let _e697: vec4<f32> = col_2;
            let _e699: f32 = density;
            let _e702: vec4<f32> = global.fog_color;
            let _e703: f32 = density;
            let _e706: vec3<f32> = ((_e697 * (1f - _e699)) + (_e702 * _e703)).xyz;
            let _e707: vec4<f32> = col_2;
            col_2 = vec4<f32>(_e706.x, _e706.y, _e706.z, _e707.w);
        }
    }
    let _e713: vec4<f32> = col_2;
    prime_output = _e713;
    return;
}

fn main_1() {
    var a_1: f32;
    var r: f32;
    var keep: bool;

    prime_original_main();
    let _e344: vec4<f32> = prime_output;
    a_1 = _e344.w;
    let _e347: f32 = global.prime_alpha_ref;
    r = _e347;
    let _e349: i32 = global.prime_alpha_func;
    let _e352: i32 = global.prime_alpha_func;
    let _e355: f32 = a_1;
    let _e356: f32 = r;
    let _e360: i32 = global.prime_alpha_func;
    let _e363: f32 = a_1;
    let _e364: f32 = r;
    let _e368: i32 = global.prime_alpha_func;
    let _e371: f32 = a_1;
    let _e372: f32 = r;
    let _e376: i32 = global.prime_alpha_func;
    let _e379: f32 = a_1;
    let _e380: f32 = r;
    let _e384: i32 = global.prime_alpha_func;
    let _e387: f32 = a_1;
    let _e388: f32 = r;
    let _e392: i32 = global.prime_alpha_func;
    let _e395: f32 = a_1;
    let _e396: f32 = r;
    keep = (((((((_e349 == 519i) || ((_e352 == 513i) && (_e355 < _e356))) || ((_e360 == 514i) && (_e363 == _e364))) || ((_e368 == 515i) && (_e371 <= _e372))) || ((_e376 == 516i) && (_e379 > _e380))) || ((_e384 == 517i) && (_e387 != _e388))) || ((_e392 == 518i) && (_e395 >= _e396)));
    let _e401: bool = keep;
    if !(_e401) {
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
    let _e367: vec4<f32> = prime_output;
    return FragmentOutput(_e367);
}
