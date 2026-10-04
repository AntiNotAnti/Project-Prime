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

struct VertexOutput {
    @builtin(position) @invariant member: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) surface_normal: vec3<f32>,
    @location(3) surface_position: vec3<f32>,
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
var<private> gl_Position: vec4<f32>;
var<private> prime_position_1: vec3<f32>;
var<private> prime_color_1: vec4<f32>;
var<private> prime_normal_1: vec3<f32>;
var<private> prime_uv_1: vec3<f32>;
var<private> prime_color_set_1: f32;
var<private> prime_normal_set_1: f32;
var<private> texcoord: vec2<f32>;
var<private> color: vec4<f32>;
var<private> surface_normal: vec3<f32>;
var<private> surface_position: vec3<f32>;

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

fn light_calc(light_vec: vec3<f32>, light_col: vec3<f32>, normal_vec: vec3<f32>, dif_col: vec3<f32>, amb_col: vec3<f32>, spe_col: vec3<f32>) -> vec3<f32> {
    var light_vec_1: vec3<f32>;
    var light_col_1: vec3<f32>;
    var normal_vec_1: vec3<f32>;
    var dif_col_1: vec3<f32>;
    var amb_col_1: vec3<f32>;
    var spe_col_1: vec3<f32>;
    var sight_vec: vec3<f32> = vec3<f32>(0f, 0f, -1f);
    var dif_factor: f32;
    var half_vec: vec3<f32>;
    var spe_factor: f32;
    var spe_out: vec3<f32>;
    var dif_out: vec3<f32>;
    var amb_out: vec3<f32>;

    light_vec_1 = light_vec;
    light_col_1 = light_col;
    normal_vec_1 = normal_vec;
    dif_col_1 = dif_col;
    amb_col_1 = amb_col;
    spe_col_1 = spe_col;
    let _e362: vec3<f32> = light_vec_1;
    let _e363: vec3<f32> = normal_vec_1;
    let _e369: vec3<f32> = light_vec_1;
    let _e370: vec3<f32> = normal_vec_1;
    dif_factor = max(0f, -(dot(_e369, _e370)));
    let _e375: vec3<f32> = light_vec_1;
    let _e376: vec3<f32> = sight_vec;
    half_vec = ((_e375 + _e376) / vec3(2f));
    let _e383: vec3<f32> = half_vec;
    let _e386: vec3<f32> = half_vec;
    let _e388: vec3<f32> = normal_vec_1;
    let _e391: vec3<f32> = half_vec;
    let _e394: vec3<f32> = half_vec;
    let _e396: vec3<f32> = normal_vec_1;
    spe_factor = max(0f, dot(-(_e394), _e396));
    let _e400: f32 = spe_factor;
    let _e401: f32 = spe_factor;
    spe_factor = (_e400 * _e401);
    let _e403: vec3<f32> = spe_col_1;
    let _e404: vec3<f32> = light_col_1;
    let _e406: f32 = spe_factor;
    spe_out = ((_e403 * _e404) * _e406);
    let _e409: vec3<f32> = dif_col_1;
    let _e410: vec3<f32> = light_col_1;
    let _e412: f32 = dif_factor;
    dif_out = ((_e409 * _e410) * _e412);
    let _e415: vec3<f32> = amb_col_1;
    let _e416: vec3<f32> = light_col_1;
    amb_out = (_e415 * _e416);
    let _e419: vec3<f32> = spe_out;
    let _e420: vec3<f32> = dif_out;
    let _e422: vec3<f32> = amb_out;
    return ((_e419 + _e420) + _e422);
}

fn prime_original_main() {
    var stack_mtx: mat4x4<f32>;
    var packed: f32;
    var j0_: i32;
    var j1_: i32;
    var j2_: i32;
    var j3_: i32;
    var local: vec4<f32>;
    var local_1: vec4<f32>;
    var weights: vec4<f32>;
    var total: f32;
    var local_2: vec4<f32>;
    var model_mtx: mat4x4<f32>;
    var local_3: vec4<f32>;
    var local_4: vec4<f32>;
    var local_5: vec4<f32>;
    var vtx_color: vec4<f32>;
    var local_6: vec3<f32>;
    var local_7: vec3<f32>;
    var normal: vec3<f32>;
    var dif_current: vec3<f32>;
    var amb_current: vec3<f32>;
    var local_8: vec4<f32>;
    var col1_: vec3<f32>;
    var col2_: vec3<f32>;
    var tex_mul: mat4x4<f32>;
    var local_9: mat4x4<f32>;
    var local_10: mat4x4<f32>;
    var texgen_mtx: mat2x4<f32>;
    var local_11: vec3<f32>;

    let _e342: i32 = global._prime_weighted_skinning;
    if (_e342 != 0i) {
        {
            let _e345: vec3<f32> = prime_uv_1;
            let _e354: vec3<f32> = prime_uv_1;
            packed = floor((vec4<f32>(_e354.x, _e354.y, _e354.z, 0f).z + 0.5f));
            let _e367: f32 = packed;
            j0_ = i32((_e367 - (floor((_e367 / 32f)) * 32f)));
            let _e375: f32 = packed;
            let _e378: f32 = packed;
            packed = floor((_e378 / 32f));
            let _e384: f32 = packed;
            j1_ = i32((_e384 - (floor((_e384 / 32f)) * 32f)));
            let _e392: f32 = packed;
            let _e395: f32 = packed;
            packed = floor((_e395 / 32f));
            let _e401: f32 = packed;
            j2_ = i32((_e401 - (floor((_e401 / 32f)) * 32f)));
            let _e409: f32 = packed;
            let _e412: f32 = packed;
            packed = floor((_e412 / 32f));
            let _e418: f32 = packed;
            j3_ = i32((_e418 - (floor((_e418 / 32f)) * 32f)));
            let _e426: f32 = prime_color_set_1;
            if (_e426 > 0.5f) {
                let _e429: vec4<f32> = prime_color_1;
                local = _e429;
            } else {
                let _e430: vec4<f32> = global.prime_imm_color;
                local = _e430;
            }
            let _e435: f32 = prime_color_set_1;
            if (_e435 > 0.5f) {
                let _e438: vec4<f32> = prime_color_1;
                local_1 = _e438;
            } else {
                let _e439: vec4<f32> = global.prime_imm_color;
                local_1 = _e439;
            }
            let _e441: vec4<f32> = local_1;
            weights = max(_e441, vec4(0f));
            let _e446: vec4<f32> = weights;
            let _e448: vec4<f32> = weights;
            let _e451: vec4<f32> = weights;
            let _e454: vec4<f32> = weights;
            total = (((_e446.x + _e448.y) + _e451.z) + _e454.w);
            let _e458: f32 = total;
            if (_e458 > 0.000001f) {
                let _e461: vec4<f32> = weights;
                let _e462: f32 = total;
                local_2 = (_e461 / vec4(_e462));
            } else {
                local_2 = vec4<f32>(1f, 0f, 0f, 0f);
            }
            let _e471: vec4<f32> = local_2;
            weights = _e471;
            let _e472: i32 = j0_;
            let _e474: mat4x4<f32> = global.mtx_stack[_e472];
            let _e475: vec4<f32> = weights;
            let _e478: i32 = j1_;
            let _e480: mat4x4<f32> = global.mtx_stack[_e478];
            let _e481: vec4<f32> = weights;
            let _e485: i32 = j2_;
            let _e487: mat4x4<f32> = global.mtx_stack[_e485];
            let _e488: vec4<f32> = weights;
            let _e492: i32 = j3_;
            let _e494: mat4x4<f32> = global.mtx_stack[_e492];
            let _e495: vec4<f32> = weights;
            stack_mtx = ((((_e474 * _e475.x) + (_e480 * _e481.y)) + (_e487 * _e488.z)) + (_e494 * _e495.w));
        }
    } else {
        {
            let _e499: vec3<f32> = prime_uv_1;
            let _e508: vec3<f32> = prime_uv_1;
            let _e520: mat4x4<f32> = global.mtx_stack[i32(clamp(vec4<f32>(_e508.x, _e508.y, _e508.z, 0f).z, 0f, 31f))];
            stack_mtx = _e520;
        }
    }
    let _e521: mat4x4<f32> = stack_mtx;
    let _e522: mat4x4<f32> = global.view_inv_mtx;
    model_mtx = (_e521 * _e522);
    let _e525: mat4x4<f32> = global.proj_mtx;
    let _e526: mat4x4<f32> = global.view_mtx;
    let _e528: mat4x4<f32> = model_mtx;
    let _e530: vec3<f32> = prime_position_1;
    gl_Position = (((_e525 * _e526) * _e528) * vec4<f32>(_e530.x, _e530.y, _e530.z, 1f));
    let _e537: i32 = global._prime_weighted_skinning;
    if (_e537 != 0i) {
        local_5 = vec4(1f);
    } else {
        let _e542: i32 = global._prime_show_colors;
        if (_e542 != 0i) {
            let _e545: f32 = prime_color_set_1;
            if (_e545 > 0.5f) {
                let _e548: vec4<f32> = prime_color_1;
                local_3 = _e548;
            } else {
                let _e549: vec4<f32> = global.prime_imm_color;
                local_3 = _e549;
            }
            let _e551: vec4<f32> = local_3;
            local_4 = _e551;
        } else {
            local_4 = vec4(1f);
        }
        let _e555: vec4<f32> = local_4;
        local_5 = _e555;
    }
    let _e557: vec4<f32> = local_5;
    vtx_color = _e557;
    let _e559: mat4x4<f32> = model_mtx;
    let _e569: f32 = prime_normal_set_1;
    if (_e569 > 0.5f) {
        let _e572: vec3<f32> = prime_normal_1;
        local_6 = _e572;
    } else {
        let _e573: vec4<f32> = global.prime_imm_normal;
        local_6 = _e573.xyz;
    }
    let _e576: vec3<f32> = local_6;
    let _e578: mat4x4<f32> = model_mtx;
    let _e588: f32 = prime_normal_set_1;
    if (_e588 > 0.5f) {
        let _e591: vec3<f32> = prime_normal_1;
        local_7 = _e591;
    } else {
        let _e592: vec4<f32> = global.prime_imm_normal;
        local_7 = _e592.xyz;
    }
    let _e595: vec3<f32> = local_7;
    normal = normalize((mat3x3<f32>(_e578[0].xyz, _e578[1].xyz, _e578[2].xyz) * _e595));
    let _e599: vec3<f32> = normal;
    surface_normal = _e599;
    let _e600: mat4x4<f32> = model_mtx;
    let _e601: vec3<f32> = prime_position_1;
    surface_position = (_e600 * vec4<f32>(_e601.x, _e601.y, _e601.z, 1f)).xyz;
    let _e609: i32 = global._prime_use_light;
    if (_e609 != 0i) {
        {
            let _e612: vec3<f32> = global.diffuse;
            dif_current = _e612;
            let _e614: vec3<f32> = global.ambient;
            amb_current = _e614;
            let _e616: i32 = global._prime_weighted_skinning;
            let _e620: f32 = prime_color_set_1;
            if (_e620 > 0.5f) {
                let _e623: vec4<f32> = prime_color_1;
                local_8 = _e623;
            } else {
                let _e624: vec4<f32> = global.prime_imm_color;
                local_8 = _e624;
            }
            let _e626: vec4<f32> = local_8;
            if (!((_e616 != 0i)) && (_e626.w == 0f)) {
                {
                    let _e631: vec4<f32> = vtx_color;
                    dif_current = _e631.xyz;
                    amb_current = vec3<f32>(0f, 0f, 0f);
                }
            }
            let _e643: vec3<f32> = global.light1vec;
            let _e644: vec3<f32> = global.light1col;
            let _e645: vec3<f32> = normal;
            let _e646: vec3<f32> = dif_current;
            let _e647: vec3<f32> = amb_current;
            let _e648: vec3<f32> = global.specular;
            let _e649: vec3<f32> = light_calc(_e643, _e644, _e645, _e646, _e647, _e648);
            col1_ = _e649;
            let _e657: vec3<f32> = global.light2vec;
            let _e658: vec3<f32> = global.light2col;
            let _e659: vec3<f32> = normal;
            let _e660: vec3<f32> = dif_current;
            let _e661: vec3<f32> = amb_current;
            let _e662: vec3<f32> = global.specular;
            let _e663: vec3<f32> = light_calc(_e657, _e658, _e659, _e660, _e661, _e662);
            col2_ = _e663;
            let _e665: vec3<f32> = col1_;
            let _e666: vec3<f32> = col2_;
            let _e668: vec3<f32> = global.emission;
            let _e674: vec3<f32> = col1_;
            let _e675: vec3<f32> = col2_;
            let _e677: vec3<f32> = global.emission;
            let _e683: vec3<f32> = min(((_e674 + _e675) + _e677), vec3<f32>(1f, 1f, 1f));
            color = vec4<f32>(_e683.x, _e683.y, _e683.z, 1f);
        }
    } else {
        {
            let _e689: vec4<f32> = vtx_color;
            let _e690: vec3<f32> = _e689.xyz;
            color = vec4<f32>(_e690.x, _e690.y, _e690.z, 1f);
        }
    }
    let _e696: i32 = global._prime_use_texture;
    if (_e696 != 0i) {
        {
            let _e699: i32 = global.texgen_mode;
            let _e702: i32 = global.texgen_mode;
            if ((_e699 == 0i) || (_e702 == 1i)) {
                {
                    let _e706: mat4x4<f32> = global.tex_mtx;
                    let _e707: vec3<f32> = prime_uv_1;
                    let _e713: vec2<f32> = vec4<f32>(_e707.x, _e707.y, _e707.z, 0f).xy;
                    texcoord = vec2<f32>((_e706 * vec4<f32>(_e713.x, _e713.y, 0f, 1f)).xy);
                    return;
                }
            } else {
                let _e724: i32 = global.texgen_mode;
                let _e727: i32 = global.texgen_mode;
                if ((_e724 == 2i) || (_e727 == 3i)) {
                    {
                        let _e731: mat4x4<f32> = global.tex_mtx;
                        tex_mul = _e731;
                        let _e733: i32 = global.texgen_mode;
                        if (_e733 == 2i) {
                            {
                                let _e736: mat4x4<f32> = global.tex_mtx;
                                let _e737: i32 = global._prime_use_light;
                                if (_e737 != 0i) {
                                    let _e740: mat4x4<f32> = global.view_mtx;
                                    local_9 = _e740;
                                } else {
                                    local_9 = mat4x4<f32>(vec4<f32>(1f, 0f, 0f, 0f), vec4<f32>(0f, 1f, 0f, 0f), vec4<f32>(0f, 0f, 1f, 0f), vec4<f32>(0f, 0f, 0f, 1f));
                                }
                                let _e749: mat4x4<f32> = local_9;
                                let _e751: mat4x4<f32> = stack_mtx;
                                let _e760: mat3x3<f32> = mat3x3<f32>(_e751[0].xyz, _e751[1].xyz, _e751[2].xyz);
                                let _e781: mat4x4<f32> = global.tex_mtx;
                                let _e782: i32 = global._prime_use_light;
                                if (_e782 != 0i) {
                                    let _e785: mat4x4<f32> = global.view_mtx;
                                    local_10 = _e785;
                                } else {
                                    local_10 = mat4x4<f32>(vec4<f32>(1f, 0f, 0f, 0f), vec4<f32>(0f, 1f, 0f, 0f), vec4<f32>(0f, 0f, 1f, 0f), vec4<f32>(0f, 0f, 0f, 1f));
                                }
                                let _e794: mat4x4<f32> = local_10;
                                let _e796: mat4x4<f32> = stack_mtx;
                                let _e805: mat3x3<f32> = mat3x3<f32>(_e796[0].xyz, _e796[1].xyz, _e796[2].xyz);
                                tex_mul = transpose(((_e781 * _e794) * mat4x4<f32>(vec4<f32>(_e805[0].x, _e805[0].y, _e805[0].z, 0f), vec4<f32>(_e805[1].x, _e805[1].y, _e805[1].z, 0f), vec4<f32>(_e805[2].x, _e805[2].y, _e805[2].z, 0f), vec4<f32>(0f, 0f, 0f, 1f))));
                            }
                        }
                        let _e831: f32 = tex_mul[0][0];
                        let _e836: f32 = tex_mul[0][1];
                        let _e841: f32 = tex_mul[0][2];
                        let _e842: vec3<f32> = prime_uv_1;
                        let _e849: vec4<f32> = vec4<f32>(_e831, _e836, _e841, vec4<f32>(_e842.x, _e842.y, _e842.z, 0f).x);
                        let _e854: f32 = tex_mul[1][0];
                        let _e859: f32 = tex_mul[1][1];
                        let _e864: f32 = tex_mul[1][2];
                        let _e865: vec3<f32> = prime_uv_1;
                        let _e872: vec4<f32> = vec4<f32>(_e854, _e859, _e864, vec4<f32>(_e865.x, _e865.y, _e865.z, 0f).y);
                        texgen_mtx = mat2x4<f32>(vec4<f32>(_e849.x, _e849.y, _e849.z, _e849.w), vec4<f32>(_e872.x, _e872.y, _e872.z, _e872.w));
                        let _e885: i32 = global.texgen_mode;
                        if (_e885 == 2i) {
                            {
                                let _e888: f32 = prime_normal_set_1;
                                if (_e888 > 0.5f) {
                                    let _e891: vec3<f32> = prime_normal_1;
                                    local_11 = _e891;
                                } else {
                                    let _e892: vec4<f32> = global.prime_imm_normal;
                                    local_11 = _e892.xyz;
                                }
                                let _e895: vec3<f32> = local_11;
                                let _e901: mat2x4<f32> = texgen_mtx;
                                texcoord = (vec4<f32>(_e895.x, _e895.y, _e895.z, 1f) * _e901);
                                return;
                            }
                        } else {
                            {
                                let _e903: vec3<f32> = prime_position_1;
                                let _e909: vec3<f32> = vec4<f32>(_e903.x, _e903.y, _e903.z, 1f).xyz;
                                let _e915: mat2x4<f32> = texgen_mtx;
                                texcoord = (vec4<f32>(_e909.x, _e909.y, _e909.z, 1f) * _e915);
                                return;
                            }
                        }
                    }
                } else {
                    return;
                }
            }
        }
    } else {
        {
            texcoord = vec2<f32>(0f, 0f);
            return;
        }
    }
}

fn main_1() {
    prime_original_main();
    let _e342: vec4<f32> = gl_Position;
    let _e344: vec4<f32> = gl_Position;
    gl_Position.z = ((_e342.z + _e344.w) * 0.5f);
    let _e349: vec4<f32> = gl_Position;
    let _e351: vec4<f32> = gl_Position;
    let _e353: vec4<f32> = global.prime_viewport;
    let _e356: vec4<f32> = global.prime_viewport;
    let _e358: vec4<f32> = gl_Position;
    let _e361: vec2<f32> = ((_e351.xy * _e353.xy) + (_e356.zw * _e358.w));
    gl_Position.x = _e361.x;
    gl_Position.y = _e361.y;
    return;
}

@vertex
fn main(@location(0) prime_position: vec3<f32>, @location(1) prime_color: vec4<f32>, @location(2) prime_normal: vec3<f32>, @location(3) prime_uv: vec3<f32>, @location(4) prime_color_set: f32, @location(5) prime_normal_set: f32) -> VertexOutput {
    prime_position_1 = prime_position;
    prime_color_1 = prime_color;
    prime_normal_1 = prime_normal;
    prime_uv_1 = prime_uv;
    prime_color_set_1 = prime_color_set;
    prime_normal_set_1 = prime_normal_set;
    main_1();
    let _e372: vec4<f32> = gl_Position;
    let _e374: vec2<f32> = texcoord;
    let _e376: vec4<f32> = color;
    let _e378: vec3<f32> = surface_normal;
    let _e380: vec3<f32> = surface_position;
    return VertexOutput(_e372, _e374, _e376, _e378, _e380);
}
