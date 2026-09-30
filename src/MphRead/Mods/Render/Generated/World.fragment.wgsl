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

fn apply_cosmetics(col: ptr<function, vec4<f32>>) {
    var nativeColor: vec3<f32>;
    var lum: f32;
    var etch: f32;
    var panel: vec2<f32>;
    var seam: f32;
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var stripe: f32;
    var cloud: f32;
    var star: f32;
    var chroma: f32;
    var t: f32;
    var n_1: vec3<f32>;
    var viewNormal: vec3<f32>;
    var toEye: vec3<f32>;
    var rim: f32;
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
    let _e356: i32 = global.cosmetic_skin;
    if (_e356 == 1i) {
        {
            let _e359: vec4<f32> = (*col);
            let _e361: vec4<f32> = (*col);
            let _e368: f32 = lum;
            let _e372: vec4<f32> = (*col);
            let _e379: f32 = lum;
            let _e384: vec3<f32> = mix(_e372.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e379)), vec3(0.88f));
            (*col).x = _e384.x;
            (*col).y = _e384.y;
            (*col).z = _e384.z;
        }
    } else {
        let _e391: i32 = global.cosmetic_skin;
        if (_e391 == 2i) {
            {
                let _e396: vec2<f32> = texcoord_1;
                let _e400: vec2<f32> = texcoord_1;
                let _e405: vec2<f32> = texcoord_1;
                let _e409: vec2<f32> = texcoord_1;
                let _e417: vec2<f32> = texcoord_1;
                let _e421: vec2<f32> = texcoord_1;
                let _e426: vec2<f32> = texcoord_1;
                let _e430: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e421.x * 85f)) * sin((_e430.y * 85f))));
                let _e438: vec4<f32> = (*col);
                let _e440: vec4<f32> = (*col);
                let _e447: f32 = lum;
                let _e451: vec4<f32> = (*col);
                let _e458: f32 = lum;
                let _e463: vec3<f32> = mix(_e451.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e458)), vec3(0.82f));
                (*col).x = _e463.x;
                (*col).y = _e463.y;
                (*col).z = _e463.z;
                let _e470: vec4<f32> = (*col);
                let _e472: vec4<f32> = (*col);
                let _e474: f32 = etch;
                let _e480: vec3<f32> = (_e472.xyz + (_e474 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e480.x;
                (*col).y = _e480.y;
                (*col).z = _e480.z;
            }
        }
    }
    let _e487: i32 = global.cosmetic_skin;
    if (_e487 == 3i) {
        {
            let _e490: vec2<f32> = texcoord_1;
            let _e493: vec2<f32> = texcoord_1;
            panel = fract((_e493 * 12f));
            let _e499: vec2<f32> = panel;
            let _e502: vec2<f32> = panel;
            let _e506: vec2<f32> = panel;
            let _e509: vec2<f32> = panel;
            seam = (step(0.06f, _e502.x) * step(0.06f, _e509.y));
            let _e514: vec4<f32> = (*col);
            let _e533: f32 = seam;
            let _e537: f32 = lum;
            let _e541: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e533)) * (0.5f + (_e537 * 0.6f)));
            (*col).x = _e541.x;
            (*col).y = _e541.y;
            (*col).z = _e541.z;
        }
    } else {
        let _e548: i32 = global.cosmetic_skin;
        if (_e548 == 4i) {
            {
                let _e551: vec2<f32> = texcoord_1;
                let _e554: vec2<f32> = texcoord_1;
                grid = fract((_e554 * 18f));
                let _e561: vec2<f32> = grid;
                let _e563: vec2<f32> = grid;
                let _e565: vec2<f32> = grid;
                let _e567: vec2<f32> = grid;
                let _e571: vec2<f32> = grid;
                let _e573: vec2<f32> = grid;
                let _e575: vec2<f32> = grid;
                let _e577: vec2<f32> = grid;
                trace = (1f - step(0.08f, min(_e575.x, _e577.y)));
                let _e585: vec2<f32> = grid;
                let _e589: vec2<f32> = grid;
                let _e595: vec2<f32> = grid;
                let _e599: vec2<f32> = grid;
                node = (1f - step(0.17f, length((_e599 - vec2(0.15f)))));
                let _e607: vec4<f32> = (*col);
                let _e614: f32 = lum;
                let _e621: f32 = trace;
                let _e625: f32 = trace;
                let _e628: f32 = node;
                let _e631: vec3<f32> = ((vec3<f32>(0.09f, 0.14f, 0.18f) * (0.6f + _e614)) + (vec3<f32>(0.05f, 0.65f, 0.55f) * max((_e625 * 0.55f), _e628)));
                (*col).x = _e631.x;
                (*col).y = _e631.y;
                (*col).z = _e631.z;
            }
        } else {
            let _e638: i32 = global.cosmetic_skin;
            if (_e638 == 5i) {
                {
                    let _e643: vec2<f32> = texcoord_1;
                    let _e647: vec2<f32> = texcoord_1;
                    let _e652: vec2<f32> = texcoord_1;
                    let _e656: vec2<f32> = texcoord_1;
                    let _e664: vec2<f32> = texcoord_1;
                    let _e668: vec2<f32> = texcoord_1;
                    let _e673: vec2<f32> = texcoord_1;
                    let _e677: vec2<f32> = texcoord_1;
                    let _e688: vec2<f32> = texcoord_1;
                    let _e692: vec2<f32> = texcoord_1;
                    let _e697: vec2<f32> = texcoord_1;
                    let _e701: vec2<f32> = texcoord_1;
                    let _e709: vec2<f32> = texcoord_1;
                    let _e713: vec2<f32> = texcoord_1;
                    let _e718: vec2<f32> = texcoord_1;
                    let _e722: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e709.x * 65f) + (_e713.y * 38f)) + (sin((_e722.y * 25f)) * 2f))));
                    let _e733: vec4<f32> = (*col);
                    let _e752: f32 = stripe;
                    let _e756: f32 = lum;
                    let _e758: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e752)) * (0.55f + _e756));
                    (*col).x = _e758.x;
                    (*col).y = _e758.y;
                    (*col).z = _e758.z;
                }
            } else {
                let _e765: i32 = global.cosmetic_skin;
                if (_e765 == 6i) {
                    {
                        let _e770: vec2<f32> = texcoord_1;
                        let _e774: vec2<f32> = texcoord_1;
                        let _e778: vec2<f32> = texcoord_1;
                        let _e786: vec2<f32> = texcoord_1;
                        let _e790: vec2<f32> = texcoord_1;
                        let _e794: vec2<f32> = texcoord_1;
                        cloud = (0.5f + (0.5f * sin(((_e786.x * 17f) + (sin((_e794.y * 23f)) * 2f)))));
                        let _e807: vec2<f32> = texcoord_1;
                        let _e810: vec2<f32> = texcoord_1;
                        let _e814: vec2<f32> = texcoord_1;
                        let _e817: vec2<f32> = texcoord_1;
                        let _e821: f32 = cosmetic_noise(floor((_e817 * 100f)));
                        let _e823: vec2<f32> = texcoord_1;
                        let _e826: vec2<f32> = texcoord_1;
                        let _e830: vec2<f32> = texcoord_1;
                        let _e833: vec2<f32> = texcoord_1;
                        let _e837: f32 = cosmetic_noise(floor((_e833 * 100f)));
                        star = step(0.985f, _e837);
                        let _e840: vec4<f32> = (*col);
                        let _e859: f32 = cloud;
                        let _e863: f32 = lum;
                        let _e866: f32 = star;
                        let _e870: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e859)) * (0.5f + _e863)) + vec3((_e866 * 0.65f)));
                        (*col).x = _e870.x;
                        (*col).y = _e870.y;
                        (*col).z = _e870.z;
                    }
                }
            }
        }
    }
    let _e877: i32 = global.cosmetic_preserve_palette;
    let _e880: i32 = global.cosmetic_skin;
    if ((_e877 != 0i) && (_e880 != 0i)) {
        {
            let _e884: vec3<f32> = nativeColor;
            let _e886: vec3<f32> = nativeColor;
            let _e888: vec3<f32> = nativeColor;
            let _e890: vec3<f32> = nativeColor;
            let _e892: vec3<f32> = nativeColor;
            let _e895: vec3<f32> = nativeColor;
            let _e897: vec3<f32> = nativeColor;
            let _e899: vec3<f32> = nativeColor;
            let _e901: vec3<f32> = nativeColor;
            let _e903: vec3<f32> = nativeColor;
            let _e907: vec3<f32> = nativeColor;
            let _e909: vec3<f32> = nativeColor;
            let _e911: vec3<f32> = nativeColor;
            let _e913: vec3<f32> = nativeColor;
            let _e915: vec3<f32> = nativeColor;
            let _e918: vec3<f32> = nativeColor;
            let _e920: vec3<f32> = nativeColor;
            let _e922: vec3<f32> = nativeColor;
            let _e924: vec3<f32> = nativeColor;
            let _e926: vec3<f32> = nativeColor;
            chroma = (max(_e895.x, max(_e901.y, _e903.z)) - min(_e918.x, min(_e924.y, _e926.z)));
            let _e932: vec4<f32> = (*col);
            let _e934: vec4<f32> = (*col);
            let _e942: f32 = chroma;
            let _e946: vec4<f32> = (*col);
            let _e948: vec3<f32> = nativeColor;
            let _e954: f32 = chroma;
            let _e959: vec3<f32> = mix(_e946.xyz, _e948, vec3((smoothstep(0.1f, 0.35f, _e954) * 0.9f)));
            (*col).x = _e959.x;
            (*col).y = _e959.y;
            (*col).z = _e959.z;
        }
    }
    let _e966: i32 = global.cosmetic_effect;
    if (_e966 != 0i) {
        {
            let _e969: f32 = global.cosmetic_time;
            let _e970: f32 = global.cosmetic_scroll;
            t = (_e969 * _e970);
            let _e973: vec3<f32> = surface_normal_1;
            let _e976: vec3<f32> = surface_normal_1;
            let _e977: vec3<f32> = surface_normal_1;
            let _e982: vec3<f32> = surface_normal_1;
            let _e983: vec3<f32> = surface_normal_1;
            let _e989: vec3<f32> = surface_normal_1;
            let _e990: vec3<f32> = surface_normal_1;
            let _e995: vec3<f32> = surface_normal_1;
            let _e996: vec3<f32> = surface_normal_1;
            n_1 = (_e973 * inverseSqrt(max(dot(_e995, _e996), 0.0001f)));
            let _e1003: mat4x4<f32> = global.view_mtx;
            let _e1013: vec3<f32> = n_1;
            viewNormal = (mat3x3<f32>(_e1003[0].xyz, _e1003[1].xyz, _e1003[2].xyz) * _e1013);
            let _e1016: mat4x4<f32> = global.view_mtx;
            let _e1017: vec3<f32> = surface_position_1;
            toEye = -((_e1016 * vec4<f32>(_e1017.x, _e1017.y, _e1017.z, 1f)).xyz);
            let _e1027: vec3<f32> = toEye;
            let _e1030: vec3<f32> = toEye;
            let _e1031: vec3<f32> = toEye;
            let _e1036: vec3<f32> = toEye;
            let _e1037: vec3<f32> = toEye;
            let _e1043: vec3<f32> = toEye;
            let _e1044: vec3<f32> = toEye;
            let _e1049: vec3<f32> = toEye;
            let _e1050: vec3<f32> = toEye;
            toEye = (_e1027 * inverseSqrt(max(dot(_e1049, _e1050), 0.0001f)));
            let _e1059: vec3<f32> = viewNormal;
            let _e1060: vec3<f32> = toEye;
            let _e1064: vec3<f32> = viewNormal;
            let _e1065: vec3<f32> = toEye;
            let _e1074: vec3<f32> = viewNormal;
            let _e1075: vec3<f32> = toEye;
            let _e1079: vec3<f32> = viewNormal;
            let _e1080: vec3<f32> = toEye;
            let _e1091: vec3<f32> = viewNormal;
            let _e1092: vec3<f32> = toEye;
            let _e1096: vec3<f32> = viewNormal;
            let _e1097: vec3<f32> = toEye;
            let _e1106: vec3<f32> = viewNormal;
            let _e1107: vec3<f32> = toEye;
            let _e1111: vec3<f32> = viewNormal;
            let _e1112: vec3<f32> = toEye;
            rim = pow(clamp((1f - abs(dot(_e1111, _e1112))), 0f, 1f), 1.6f);
            let _e1124: f32 = global.cosmetic_time;
            let _e1125: f32 = global.cosmetic_pulse;
            let _e1127: f32 = global.cosmetic_time;
            let _e1128: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1127 * _e1128))));
            let _e1136: vec2<f32> = texcoord_1;
            let _e1140: f32 = t;
            let _e1144: vec2<f32> = texcoord_1;
            let _e1148: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1144.y * 30f) - (_e1148 * 2f)))));
            let _e1157: f32 = rim;
            mask = (0.28f + (_e1157 * 0.72f));
            let _e1162: i32 = global.cosmetic_effect;
            if (_e1162 == 1i) {
                let _e1167: f32 = pulse;
                mask = (0.25f + (0.55f * _e1167));
            }
            let _e1170: i32 = global.cosmetic_effect;
            if (_e1170 == 3i) {
                let _e1178: f32 = wave;
                let _e1182: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1178) * 0.5f) + _e1182);
            }
            let _e1184: i32 = global.cosmetic_effect;
            if (_e1184 == 4i) {
                let _e1187: vec2<f32> = texcoord_1;
                let _e1190: vec2<f32> = texcoord_1;
                let _e1194: f32 = t;
                let _e1197: f32 = t;
                let _e1203: vec2<f32> = texcoord_1;
                let _e1206: vec2<f32> = texcoord_1;
                let _e1210: f32 = t;
                let _e1213: f32 = t;
                let _e1219: f32 = cosmetic_noise((floor((_e1206 * 24f)) + vec2(floor((_e1213 * 3f)))));
                let _e1222: f32 = rim;
                mask = ((_e1219 * 0.3f) + _e1222);
            }
            let _e1224: i32 = global.cosmetic_effect;
            let _e1227: i32 = global.cosmetic_effect;
            if ((_e1224 == 5i) || (_e1227 == 7i)) {
                let _e1237: f32 = wave;
                let _e1242: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1237) * 0.65f)) + (_e1242 * 0.45f));
            }
            let _e1246: i32 = global.cosmetic_effect;
            if (_e1246 == 6i) {
                let _e1250: vec2<f32> = texcoord_1;
                let _e1254: vec2<f32> = texcoord_1;
                let _e1258: f32 = t;
                let _e1260: vec2<f32> = texcoord_1;
                let _e1264: f32 = t;
                let _e1268: vec2<f32> = texcoord_1;
                let _e1272: vec2<f32> = texcoord_1;
                let _e1276: f32 = t;
                let _e1278: vec2<f32> = texcoord_1;
                let _e1282: f32 = t;
                let _e1287: vec2<f32> = texcoord_1;
                let _e1291: vec2<f32> = texcoord_1;
                let _e1295: f32 = t;
                let _e1297: vec2<f32> = texcoord_1;
                let _e1301: f32 = t;
                let _e1305: vec2<f32> = texcoord_1;
                let _e1309: vec2<f32> = texcoord_1;
                let _e1313: f32 = t;
                let _e1315: vec2<f32> = texcoord_1;
                let _e1319: f32 = t;
                let _e1326: vec2<f32> = texcoord_1;
                let _e1330: vec2<f32> = texcoord_1;
                let _e1334: f32 = t;
                let _e1336: vec2<f32> = texcoord_1;
                let _e1340: f32 = t;
                let _e1344: vec2<f32> = texcoord_1;
                let _e1348: vec2<f32> = texcoord_1;
                let _e1352: f32 = t;
                let _e1354: vec2<f32> = texcoord_1;
                let _e1358: f32 = t;
                let _e1363: vec2<f32> = texcoord_1;
                let _e1367: vec2<f32> = texcoord_1;
                let _e1371: f32 = t;
                let _e1373: vec2<f32> = texcoord_1;
                let _e1377: f32 = t;
                let _e1381: vec2<f32> = texcoord_1;
                let _e1385: vec2<f32> = texcoord_1;
                let _e1389: f32 = t;
                let _e1391: vec2<f32> = texcoord_1;
                let _e1395: f32 = t;
                let _e1404: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1381.x * 31f) + sin(((_e1391.y * 29f) + _e1395))))), 16f)) + (_e1404 * 0.5f));
            }
            let _e1408: i32 = global.cosmetic_effect;
            if (_e1408 == 8i) {
                let _e1411: f32 = wave;
                let _e1414: f32 = rim;
                mask = ((_e1411 * 0.35f) + (_e1414 * 0.7f));
            }
            let _e1418: i32 = global.cosmetic_effect;
            if (_e1418 == 9i) {
                let _e1423: vec2<f32> = texcoord_1;
                let _e1427: f32 = t;
                let _e1429: vec2<f32> = texcoord_1;
                let _e1433: f32 = t;
                let _e1438: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1429.x * 40f) + _e1433)))) * _e1438);
            }
            let _e1443: vec3<f32> = global.cosmetic_primary;
            let _e1444: vec3<f32> = global.cosmetic_secondary;
            let _e1445: f32 = wave;
            energy = mix(_e1443, _e1444, vec3(_e1445));
            let _e1452: f32 = mask;
            let _e1456: f32 = global.cosmetic_intensity;
            let _e1458: f32 = pulse;
            strength = ((clamp(_e1452, 0f, 1f) * _e1456) * _e1458);
            let _e1461: i32 = global.cosmetic_preserve_palette;
            if (_e1461 != 0i) {
                let _e1464: f32 = strength;
                strength = (_e1464 * 0.65f);
            }
            let _e1467: vec4<f32> = (*col);
            let _e1469: vec4<f32> = (*col);
            let _e1471: vec3<f32> = energy;
            let _e1473: f32 = lum;
            let _e1478: f32 = strength;
            let _e1483: f32 = strength;
            let _e1489: vec4<f32> = (*col);
            let _e1491: vec3<f32> = energy;
            let _e1493: f32 = lum;
            let _e1498: f32 = strength;
            let _e1503: f32 = strength;
            let _e1510: vec3<f32> = mix(_e1489.xyz, (_e1491 * (0.4f + (_e1493 * 0.6f))), vec3(clamp((_e1503 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1510.x;
            (*col).y = _e1510.y;
            (*col).z = _e1510.z;
            let _e1517: vec4<f32> = (*col);
            let _e1519: vec4<f32> = (*col);
            let _e1521: vec3<f32> = energy;
            let _e1522: f32 = strength;
            let _e1526: vec3<f32> = (_e1519.xyz + ((_e1521 * _e1522) * 0.45f));
            (*col).x = _e1526.x;
            (*col).y = _e1526.y;
            (*col).z = _e1526.z;
        }
    }
    let _e1533: f32 = global.cosmetic_dissolve;
    if (_e1533 > 0f) {
        {
            let _e1536: vec2<f32> = texcoord_1;
            let _e1539: vec2<f32> = texcoord_1;
            let _e1543: vec2<f32> = texcoord_1;
            let _e1546: vec2<f32> = texcoord_1;
            let _e1550: f32 = cosmetic_noise(floor((_e1546 * 64f)));
            n_2 = _e1550;
            let _e1552: f32 = n_2;
            let _e1553: f32 = global.cosmetic_dissolve;
            if (_e1552 < _e1553) {
                discard;
            }
            let _e1555: vec4<f32> = (*col);
            let _e1557: vec4<f32> = (*col);
            let _e1559: vec3<f32> = global.cosmetic_primary;
            let _e1562: f32 = global.cosmetic_dissolve;
            let _e1566: f32 = global.cosmetic_dissolve;
            let _e1567: f32 = global.cosmetic_dissolve;
            let _e1570: f32 = n_2;
            let _e1576: vec3<f32> = (_e1557.xyz + ((_e1559 * (1f - smoothstep(_e1566, (_e1567 + 0.08f), _e1570))) * 0.3f));
            (*col).x = _e1576.x;
            (*col).y = _e1576.y;
            (*col).z = _e1576.z;
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
