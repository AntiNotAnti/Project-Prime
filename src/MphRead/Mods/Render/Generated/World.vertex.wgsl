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
    let _e370: vec3<f32> = light_vec_1;
    let _e371: vec3<f32> = normal_vec_1;
    let _e377: vec3<f32> = light_vec_1;
    let _e378: vec3<f32> = normal_vec_1;
    dif_factor = max(0f, -(dot(_e377, _e378)));
    let _e383: vec3<f32> = light_vec_1;
    let _e384: vec3<f32> = sight_vec;
    half_vec = ((_e383 + _e384) / vec3(2f));
    let _e391: vec3<f32> = half_vec;
    let _e394: vec3<f32> = half_vec;
    let _e396: vec3<f32> = normal_vec_1;
    let _e399: vec3<f32> = half_vec;
    let _e402: vec3<f32> = half_vec;
    let _e404: vec3<f32> = normal_vec_1;
    spe_factor = max(0f, dot(-(_e402), _e404));
    let _e408: f32 = spe_factor;
    let _e409: f32 = spe_factor;
    spe_factor = (_e408 * _e409);
    let _e411: vec3<f32> = spe_col_1;
    let _e412: vec3<f32> = light_col_1;
    let _e414: f32 = spe_factor;
    spe_out = ((_e411 * _e412) * _e414);
    let _e417: vec3<f32> = dif_col_1;
    let _e418: vec3<f32> = light_col_1;
    let _e420: f32 = dif_factor;
    dif_out = ((_e417 * _e418) * _e420);
    let _e423: vec3<f32> = amb_col_1;
    let _e424: vec3<f32> = light_col_1;
    amb_out = (_e423 * _e424);
    let _e427: vec3<f32> = spe_out;
    let _e428: vec3<f32> = dif_out;
    let _e430: vec3<f32> = amb_out;
    return ((_e427 + _e428) + _e430);
}

fn prime_original_main() {
    var stack_mtx: mat4x4<f32>;
    var packedJoints: f32;
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

    let _e350: i32 = global._prime_weighted_skinning;
    if (_e350 != 0i) {
        {
            let _e353: vec3<f32> = prime_uv_1;
            let _e362: vec3<f32> = prime_uv_1;
            packedJoints = floor((vec4<f32>(_e362.x, _e362.y, _e362.z, 0f).z + 0.5f));
            let _e375: f32 = packedJoints;
            j0_ = i32((_e375 - (floor((_e375 / 32f)) * 32f)));
            let _e383: f32 = packedJoints;
            let _e386: f32 = packedJoints;
            packedJoints = floor((_e386 / 32f));
            let _e392: f32 = packedJoints;
            j1_ = i32((_e392 - (floor((_e392 / 32f)) * 32f)));
            let _e400: f32 = packedJoints;
            let _e403: f32 = packedJoints;
            packedJoints = floor((_e403 / 32f));
            let _e409: f32 = packedJoints;
            j2_ = i32((_e409 - (floor((_e409 / 32f)) * 32f)));
            let _e417: f32 = packedJoints;
            let _e420: f32 = packedJoints;
            packedJoints = floor((_e420 / 32f));
            let _e426: f32 = packedJoints;
            j3_ = i32((_e426 - (floor((_e426 / 32f)) * 32f)));
            let _e434: f32 = prime_color_set_1;
            if (_e434 > 0.5f) {
                let _e437: vec4<f32> = prime_color_1;
                local = _e437;
            } else {
                let _e438: vec4<f32> = global.prime_imm_color;
                local = _e438;
            }
            let _e443: f32 = prime_color_set_1;
            if (_e443 > 0.5f) {
                let _e446: vec4<f32> = prime_color_1;
                local_1 = _e446;
            } else {
                let _e447: vec4<f32> = global.prime_imm_color;
                local_1 = _e447;
            }
            let _e449: vec4<f32> = local_1;
            weights = max(_e449, vec4(0f));
            let _e454: vec4<f32> = weights;
            let _e456: vec4<f32> = weights;
            let _e459: vec4<f32> = weights;
            let _e462: vec4<f32> = weights;
            total = (((_e454.x + _e456.y) + _e459.z) + _e462.w);
            let _e466: f32 = total;
            if (_e466 > 0.000001f) {
                let _e469: vec4<f32> = weights;
                let _e470: f32 = total;
                local_2 = (_e469 / vec4(_e470));
            } else {
                local_2 = vec4<f32>(1f, 0f, 0f, 0f);
            }
            let _e479: vec4<f32> = local_2;
            weights = _e479;
            let _e480: i32 = j0_;
            let _e482: mat4x4<f32> = global.mtx_stack[_e480];
            let _e483: vec4<f32> = weights;
            let _e486: i32 = j1_;
            let _e488: mat4x4<f32> = global.mtx_stack[_e486];
            let _e489: vec4<f32> = weights;
            let _e493: i32 = j2_;
            let _e495: mat4x4<f32> = global.mtx_stack[_e493];
            let _e496: vec4<f32> = weights;
            let _e500: i32 = j3_;
            let _e502: mat4x4<f32> = global.mtx_stack[_e500];
            let _e503: vec4<f32> = weights;
            stack_mtx = ((((_e482 * _e483.x) + (_e488 * _e489.y)) + (_e495 * _e496.z)) + (_e502 * _e503.w));
        }
    } else {
        {
            let _e507: vec3<f32> = prime_uv_1;
            let _e516: vec3<f32> = prime_uv_1;
            let _e528: mat4x4<f32> = global.mtx_stack[i32(clamp(vec4<f32>(_e516.x, _e516.y, _e516.z, 0f).z, 0f, 31f))];
            stack_mtx = _e528;
        }
    }
    let _e529: mat4x4<f32> = stack_mtx;
    let _e530: mat4x4<f32> = global.view_inv_mtx;
    model_mtx = (_e529 * _e530);
    let _e533: mat4x4<f32> = global.proj_mtx;
    let _e534: mat4x4<f32> = global.view_mtx;
    let _e536: mat4x4<f32> = model_mtx;
    let _e538: vec3<f32> = prime_position_1;
    gl_Position = (((_e533 * _e534) * _e536) * vec4<f32>(_e538.x, _e538.y, _e538.z, 1f));
    let _e545: i32 = global._prime_weighted_skinning;
    if (_e545 != 0i) {
        local_5 = vec4(1f);
    } else {
        let _e550: i32 = global._prime_show_colors;
        if (_e550 != 0i) {
            let _e553: f32 = prime_color_set_1;
            if (_e553 > 0.5f) {
                let _e556: vec4<f32> = prime_color_1;
                local_3 = _e556;
            } else {
                let _e557: vec4<f32> = global.prime_imm_color;
                local_3 = _e557;
            }
            let _e559: vec4<f32> = local_3;
            local_4 = _e559;
        } else {
            local_4 = vec4(1f);
        }
        let _e563: vec4<f32> = local_4;
        local_5 = _e563;
    }
    let _e565: vec4<f32> = local_5;
    vtx_color = _e565;
    let _e567: mat4x4<f32> = model_mtx;
    let _e577: f32 = prime_normal_set_1;
    if (_e577 > 0.5f) {
        let _e580: vec3<f32> = prime_normal_1;
        local_6 = _e580;
    } else {
        let _e581: vec4<f32> = global.prime_imm_normal;
        local_6 = _e581.xyz;
    }
    let _e584: vec3<f32> = local_6;
    let _e586: mat4x4<f32> = model_mtx;
    let _e596: f32 = prime_normal_set_1;
    if (_e596 > 0.5f) {
        let _e599: vec3<f32> = prime_normal_1;
        local_7 = _e599;
    } else {
        let _e600: vec4<f32> = global.prime_imm_normal;
        local_7 = _e600.xyz;
    }
    let _e603: vec3<f32> = local_7;
    normal = normalize((mat3x3<f32>(_e586[0].xyz, _e586[1].xyz, _e586[2].xyz) * _e603));
    let _e607: vec3<f32> = normal;
    surface_normal = _e607;
    let _e608: mat4x4<f32> = model_mtx;
    let _e609: vec3<f32> = prime_position_1;
    surface_position = (_e608 * vec4<f32>(_e609.x, _e609.y, _e609.z, 1f)).xyz;
    let _e617: i32 = global._prime_use_light;
    if (_e617 != 0i) {
        {
            let _e620: vec3<f32> = global.diffuse;
            dif_current = _e620;
            let _e622: vec3<f32> = global.ambient;
            amb_current = _e622;
            let _e624: i32 = global._prime_weighted_skinning;
            let _e628: f32 = prime_color_set_1;
            if (_e628 > 0.5f) {
                let _e631: vec4<f32> = prime_color_1;
                local_8 = _e631;
            } else {
                let _e632: vec4<f32> = global.prime_imm_color;
                local_8 = _e632;
            }
            let _e634: vec4<f32> = local_8;
            if (!((_e624 != 0i)) && (_e634.w == 0f)) {
                {
                    let _e639: vec4<f32> = vtx_color;
                    dif_current = _e639.xyz;
                    amb_current = vec3<f32>(0f, 0f, 0f);
                }
            }
            let _e651: vec3<f32> = global.light1vec;
            let _e652: vec3<f32> = global.light1col;
            let _e653: vec3<f32> = normal;
            let _e654: vec3<f32> = dif_current;
            let _e655: vec3<f32> = amb_current;
            let _e656: vec3<f32> = global.specular;
            let _e657: vec3<f32> = light_calc(_e651, _e652, _e653, _e654, _e655, _e656);
            col1_ = _e657;
            let _e665: vec3<f32> = global.light2vec;
            let _e666: vec3<f32> = global.light2col;
            let _e667: vec3<f32> = normal;
            let _e668: vec3<f32> = dif_current;
            let _e669: vec3<f32> = amb_current;
            let _e670: vec3<f32> = global.specular;
            let _e671: vec3<f32> = light_calc(_e665, _e666, _e667, _e668, _e669, _e670);
            col2_ = _e671;
            let _e673: vec3<f32> = col1_;
            let _e674: vec3<f32> = col2_;
            let _e676: vec3<f32> = global.emission;
            let _e682: vec3<f32> = col1_;
            let _e683: vec3<f32> = col2_;
            let _e685: vec3<f32> = global.emission;
            let _e691: vec3<f32> = min(((_e682 + _e683) + _e685), vec3<f32>(1f, 1f, 1f));
            color = vec4<f32>(_e691.x, _e691.y, _e691.z, 1f);
        }
    } else {
        {
            let _e697: vec4<f32> = vtx_color;
            let _e698: vec3<f32> = _e697.xyz;
            color = vec4<f32>(_e698.x, _e698.y, _e698.z, 1f);
        }
    }
    let _e704: i32 = global._prime_use_texture;
    if (_e704 != 0i) {
        {
            let _e707: i32 = global.texgen_mode;
            let _e710: i32 = global.texgen_mode;
            if ((_e707 == 0i) || (_e710 == 1i)) {
                {
                    let _e714: mat4x4<f32> = global.tex_mtx;
                    let _e715: vec3<f32> = prime_uv_1;
                    let _e721: vec2<f32> = vec4<f32>(_e715.x, _e715.y, _e715.z, 0f).xy;
                    texcoord = vec2<f32>((_e714 * vec4<f32>(_e721.x, _e721.y, 0f, 1f)).xy);
                    return;
                }
            } else {
                let _e732: i32 = global.texgen_mode;
                let _e735: i32 = global.texgen_mode;
                if ((_e732 == 2i) || (_e735 == 3i)) {
                    {
                        let _e739: mat4x4<f32> = global.tex_mtx;
                        tex_mul = _e739;
                        let _e741: i32 = global.texgen_mode;
                        if (_e741 == 2i) {
                            {
                                let _e744: mat4x4<f32> = global.tex_mtx;
                                let _e745: i32 = global._prime_use_light;
                                if (_e745 != 0i) {
                                    let _e748: mat4x4<f32> = global.view_mtx;
                                    local_9 = _e748;
                                } else {
                                    local_9 = mat4x4<f32>(vec4<f32>(1f, 0f, 0f, 0f), vec4<f32>(0f, 1f, 0f, 0f), vec4<f32>(0f, 0f, 1f, 0f), vec4<f32>(0f, 0f, 0f, 1f));
                                }
                                let _e757: mat4x4<f32> = local_9;
                                let _e759: mat4x4<f32> = stack_mtx;
                                let _e768: mat3x3<f32> = mat3x3<f32>(_e759[0].xyz, _e759[1].xyz, _e759[2].xyz);
                                let _e789: mat4x4<f32> = global.tex_mtx;
                                let _e790: i32 = global._prime_use_light;
                                if (_e790 != 0i) {
                                    let _e793: mat4x4<f32> = global.view_mtx;
                                    local_10 = _e793;
                                } else {
                                    local_10 = mat4x4<f32>(vec4<f32>(1f, 0f, 0f, 0f), vec4<f32>(0f, 1f, 0f, 0f), vec4<f32>(0f, 0f, 1f, 0f), vec4<f32>(0f, 0f, 0f, 1f));
                                }
                                let _e802: mat4x4<f32> = local_10;
                                let _e804: mat4x4<f32> = stack_mtx;
                                let _e813: mat3x3<f32> = mat3x3<f32>(_e804[0].xyz, _e804[1].xyz, _e804[2].xyz);
                                tex_mul = transpose(((_e789 * _e802) * mat4x4<f32>(vec4<f32>(_e813[0].x, _e813[0].y, _e813[0].z, 0f), vec4<f32>(_e813[1].x, _e813[1].y, _e813[1].z, 0f), vec4<f32>(_e813[2].x, _e813[2].y, _e813[2].z, 0f), vec4<f32>(0f, 0f, 0f, 1f))));
                            }
                        }
                        let _e839: f32 = tex_mul[0][0];
                        let _e844: f32 = tex_mul[0][1];
                        let _e849: f32 = tex_mul[0][2];
                        let _e850: vec3<f32> = prime_uv_1;
                        let _e857: vec4<f32> = vec4<f32>(_e839, _e844, _e849, vec4<f32>(_e850.x, _e850.y, _e850.z, 0f).x);
                        let _e862: f32 = tex_mul[1][0];
                        let _e867: f32 = tex_mul[1][1];
                        let _e872: f32 = tex_mul[1][2];
                        let _e873: vec3<f32> = prime_uv_1;
                        let _e880: vec4<f32> = vec4<f32>(_e862, _e867, _e872, vec4<f32>(_e873.x, _e873.y, _e873.z, 0f).y);
                        texgen_mtx = mat2x4<f32>(vec4<f32>(_e857.x, _e857.y, _e857.z, _e857.w), vec4<f32>(_e880.x, _e880.y, _e880.z, _e880.w));
                        let _e893: i32 = global.texgen_mode;
                        if (_e893 == 2i) {
                            {
                                let _e896: f32 = prime_normal_set_1;
                                if (_e896 > 0.5f) {
                                    let _e899: vec3<f32> = prime_normal_1;
                                    local_11 = _e899;
                                } else {
                                    let _e900: vec4<f32> = global.prime_imm_normal;
                                    local_11 = _e900.xyz;
                                }
                                let _e903: vec3<f32> = local_11;
                                let _e909: mat2x4<f32> = texgen_mtx;
                                texcoord = (vec4<f32>(_e903.x, _e903.y, _e903.z, 1f) * _e909);
                                return;
                            }
                        } else {
                            {
                                let _e911: vec3<f32> = prime_position_1;
                                let _e917: vec3<f32> = vec4<f32>(_e911.x, _e911.y, _e911.z, 1f).xyz;
                                let _e923: mat2x4<f32> = texgen_mtx;
                                texcoord = (vec4<f32>(_e917.x, _e917.y, _e917.z, 1f) * _e923);
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
    let _e350: vec4<f32> = gl_Position;
    let _e352: vec4<f32> = gl_Position;
    gl_Position.z = ((_e350.z + _e352.w) * 0.5f);
    let _e357: vec4<f32> = gl_Position;
    let _e359: vec4<f32> = gl_Position;
    let _e361: vec4<f32> = global.prime_viewport;
    let _e364: vec4<f32> = global.prime_viewport;
    let _e366: vec4<f32> = gl_Position;
    let _e369: vec2<f32> = ((_e359.xy * _e361.xy) + (_e364.zw * _e366.w));
    gl_Position.x = _e369.x;
    gl_Position.y = _e369.y;
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
    let _e380: vec4<f32> = gl_Position;
    let _e382: vec2<f32> = texcoord;
    let _e384: vec4<f32> = color;
    let _e386: vec3<f32> = surface_normal;
    let _e388: vec3<f32> = surface_position;
    return VertexOutput(_e380, _e382, _e384, _e386, _e388);
}
