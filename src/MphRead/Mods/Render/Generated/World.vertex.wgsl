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
    let _e354: vec3<f32> = light_vec_1;
    let _e355: vec3<f32> = normal_vec_1;
    let _e361: vec3<f32> = light_vec_1;
    let _e362: vec3<f32> = normal_vec_1;
    dif_factor = max(0f, -(dot(_e361, _e362)));
    let _e367: vec3<f32> = light_vec_1;
    let _e368: vec3<f32> = sight_vec;
    half_vec = ((_e367 + _e368) / vec3(2f));
    let _e375: vec3<f32> = half_vec;
    let _e378: vec3<f32> = half_vec;
    let _e380: vec3<f32> = normal_vec_1;
    let _e383: vec3<f32> = half_vec;
    let _e386: vec3<f32> = half_vec;
    let _e388: vec3<f32> = normal_vec_1;
    spe_factor = max(0f, dot(-(_e386), _e388));
    let _e392: f32 = spe_factor;
    let _e393: f32 = spe_factor;
    spe_factor = (_e392 * _e393);
    let _e395: vec3<f32> = spe_col_1;
    let _e396: vec3<f32> = light_col_1;
    let _e398: f32 = spe_factor;
    spe_out = ((_e395 * _e396) * _e398);
    let _e401: vec3<f32> = dif_col_1;
    let _e402: vec3<f32> = light_col_1;
    let _e404: f32 = dif_factor;
    dif_out = ((_e401 * _e402) * _e404);
    let _e407: vec3<f32> = amb_col_1;
    let _e408: vec3<f32> = light_col_1;
    amb_out = (_e407 * _e408);
    let _e411: vec3<f32> = spe_out;
    let _e412: vec3<f32> = dif_out;
    let _e414: vec3<f32> = amb_out;
    return ((_e411 + _e412) + _e414);
}

fn prime_original_main() {
    var stack_mtx: mat4x4<f32>;
    var model_mtx: mat4x4<f32>;
    var local: vec4<f32>;
    var local_1: vec4<f32>;
    var vtx_color: vec4<f32>;
    var local_2: vec3<f32>;
    var local_3: vec3<f32>;
    var normal: vec3<f32>;
    var dif_current: vec3<f32>;
    var amb_current: vec3<f32>;
    var local_4: vec4<f32>;
    var col1_: vec3<f32>;
    var col2_: vec3<f32>;
    var tex_mul: mat4x4<f32>;
    var local_5: mat4x4<f32>;
    var local_6: mat4x4<f32>;
    var texgen_mtx: mat2x4<f32>;
    var local_7: vec3<f32>;

    let _e333: vec3<f32> = prime_uv_1;
    let _e342: vec3<f32> = prime_uv_1;
    let _e354: mat4x4<f32> = global.mtx_stack[i32(clamp(vec4<f32>(_e342.x, _e342.y, _e342.z, 0f).z, 0f, 31f))];
    stack_mtx = _e354;
    let _e356: mat4x4<f32> = stack_mtx;
    let _e357: mat4x4<f32> = global.view_inv_mtx;
    model_mtx = (_e356 * _e357);
    let _e360: mat4x4<f32> = global.proj_mtx;
    let _e361: mat4x4<f32> = global.view_mtx;
    let _e363: mat4x4<f32> = model_mtx;
    let _e365: vec3<f32> = prime_position_1;
    gl_Position = (((_e360 * _e361) * _e363) * vec4<f32>(_e365.x, _e365.y, _e365.z, 1f));
    let _e372: i32 = global._prime_show_colors;
    if (_e372 != 0i) {
        let _e375: f32 = prime_color_set_1;
        if (_e375 > 0.5f) {
            let _e378: vec4<f32> = prime_color_1;
            local = _e378;
        } else {
            let _e379: vec4<f32> = global.prime_imm_color;
            local = _e379;
        }
        let _e381: vec4<f32> = local;
        local_1 = _e381;
    } else {
        local_1 = vec4(1f);
    }
    let _e385: vec4<f32> = local_1;
    vtx_color = _e385;
    let _e387: mat4x4<f32> = model_mtx;
    let _e397: f32 = prime_normal_set_1;
    if (_e397 > 0.5f) {
        let _e400: vec3<f32> = prime_normal_1;
        local_2 = _e400;
    } else {
        let _e401: vec4<f32> = global.prime_imm_normal;
        local_2 = _e401.xyz;
    }
    let _e404: vec3<f32> = local_2;
    let _e406: mat4x4<f32> = model_mtx;
    let _e416: f32 = prime_normal_set_1;
    if (_e416 > 0.5f) {
        let _e419: vec3<f32> = prime_normal_1;
        local_3 = _e419;
    } else {
        let _e420: vec4<f32> = global.prime_imm_normal;
        local_3 = _e420.xyz;
    }
    let _e423: vec3<f32> = local_3;
    normal = normalize((mat3x3<f32>(_e406[0].xyz, _e406[1].xyz, _e406[2].xyz) * _e423));
    let _e427: vec3<f32> = normal;
    surface_normal = _e427;
    let _e428: mat4x4<f32> = model_mtx;
    let _e429: vec3<f32> = prime_position_1;
    surface_position = (_e428 * vec4<f32>(_e429.x, _e429.y, _e429.z, 1f)).xyz;
    let _e437: i32 = global._prime_use_light;
    if (_e437 != 0i) {
        {
            let _e440: vec3<f32> = global.diffuse;
            dif_current = _e440;
            let _e442: vec3<f32> = global.ambient;
            amb_current = _e442;
            let _e444: f32 = prime_color_set_1;
            if (_e444 > 0.5f) {
                let _e447: vec4<f32> = prime_color_1;
                local_4 = _e447;
            } else {
                let _e448: vec4<f32> = global.prime_imm_color;
                local_4 = _e448;
            }
            let _e450: vec4<f32> = local_4;
            if (_e450.w == 0f) {
                {
                    let _e454: vec4<f32> = vtx_color;
                    dif_current = _e454.xyz;
                    amb_current = vec3<f32>(0f, 0f, 0f);
                }
            }
            let _e466: vec3<f32> = global.light1vec;
            let _e467: vec3<f32> = global.light1col;
            let _e468: vec3<f32> = normal;
            let _e469: vec3<f32> = dif_current;
            let _e470: vec3<f32> = amb_current;
            let _e471: vec3<f32> = global.specular;
            let _e472: vec3<f32> = light_calc(_e466, _e467, _e468, _e469, _e470, _e471);
            col1_ = _e472;
            let _e480: vec3<f32> = global.light2vec;
            let _e481: vec3<f32> = global.light2col;
            let _e482: vec3<f32> = normal;
            let _e483: vec3<f32> = dif_current;
            let _e484: vec3<f32> = amb_current;
            let _e485: vec3<f32> = global.specular;
            let _e486: vec3<f32> = light_calc(_e480, _e481, _e482, _e483, _e484, _e485);
            col2_ = _e486;
            let _e488: vec3<f32> = col1_;
            let _e489: vec3<f32> = col2_;
            let _e491: vec3<f32> = global.emission;
            let _e497: vec3<f32> = col1_;
            let _e498: vec3<f32> = col2_;
            let _e500: vec3<f32> = global.emission;
            let _e506: vec3<f32> = min(((_e497 + _e498) + _e500), vec3<f32>(1f, 1f, 1f));
            color = vec4<f32>(_e506.x, _e506.y, _e506.z, 1f);
        }
    } else {
        {
            let _e512: vec4<f32> = vtx_color;
            let _e513: vec3<f32> = _e512.xyz;
            color = vec4<f32>(_e513.x, _e513.y, _e513.z, 1f);
        }
    }
    let _e519: i32 = global._prime_use_texture;
    if (_e519 != 0i) {
        {
            let _e522: i32 = global.texgen_mode;
            let _e525: i32 = global.texgen_mode;
            if ((_e522 == 0i) || (_e525 == 1i)) {
                {
                    let _e529: mat4x4<f32> = global.tex_mtx;
                    let _e530: vec3<f32> = prime_uv_1;
                    let _e536: vec2<f32> = vec4<f32>(_e530.x, _e530.y, _e530.z, 0f).xy;
                    texcoord = vec2<f32>((_e529 * vec4<f32>(_e536.x, _e536.y, 0f, 1f)).xy);
                    return;
                }
            } else {
                let _e547: i32 = global.texgen_mode;
                let _e550: i32 = global.texgen_mode;
                if ((_e547 == 2i) || (_e550 == 3i)) {
                    {
                        let _e554: mat4x4<f32> = global.tex_mtx;
                        tex_mul = _e554;
                        let _e556: i32 = global.texgen_mode;
                        if (_e556 == 2i) {
                            {
                                let _e559: mat4x4<f32> = global.tex_mtx;
                                let _e560: i32 = global._prime_use_light;
                                if (_e560 != 0i) {
                                    let _e563: mat4x4<f32> = global.view_mtx;
                                    local_5 = _e563;
                                } else {
                                    local_5 = mat4x4<f32>(vec4<f32>(1f, 0f, 0f, 0f), vec4<f32>(0f, 1f, 0f, 0f), vec4<f32>(0f, 0f, 1f, 0f), vec4<f32>(0f, 0f, 0f, 1f));
                                }
                                let _e572: mat4x4<f32> = local_5;
                                let _e574: mat4x4<f32> = stack_mtx;
                                let _e583: mat3x3<f32> = mat3x3<f32>(_e574[0].xyz, _e574[1].xyz, _e574[2].xyz);
                                let _e604: mat4x4<f32> = global.tex_mtx;
                                let _e605: i32 = global._prime_use_light;
                                if (_e605 != 0i) {
                                    let _e608: mat4x4<f32> = global.view_mtx;
                                    local_6 = _e608;
                                } else {
                                    local_6 = mat4x4<f32>(vec4<f32>(1f, 0f, 0f, 0f), vec4<f32>(0f, 1f, 0f, 0f), vec4<f32>(0f, 0f, 1f, 0f), vec4<f32>(0f, 0f, 0f, 1f));
                                }
                                let _e617: mat4x4<f32> = local_6;
                                let _e619: mat4x4<f32> = stack_mtx;
                                let _e628: mat3x3<f32> = mat3x3<f32>(_e619[0].xyz, _e619[1].xyz, _e619[2].xyz);
                                tex_mul = transpose(((_e604 * _e617) * mat4x4<f32>(vec4<f32>(_e628[0].x, _e628[0].y, _e628[0].z, 0f), vec4<f32>(_e628[1].x, _e628[1].y, _e628[1].z, 0f), vec4<f32>(_e628[2].x, _e628[2].y, _e628[2].z, 0f), vec4<f32>(0f, 0f, 0f, 1f))));
                            }
                        }
                        let _e654: f32 = tex_mul[0][0];
                        let _e659: f32 = tex_mul[0][1];
                        let _e664: f32 = tex_mul[0][2];
                        let _e665: vec3<f32> = prime_uv_1;
                        let _e672: vec4<f32> = vec4<f32>(_e654, _e659, _e664, vec4<f32>(_e665.x, _e665.y, _e665.z, 0f).x);
                        let _e677: f32 = tex_mul[1][0];
                        let _e682: f32 = tex_mul[1][1];
                        let _e687: f32 = tex_mul[1][2];
                        let _e688: vec3<f32> = prime_uv_1;
                        let _e695: vec4<f32> = vec4<f32>(_e677, _e682, _e687, vec4<f32>(_e688.x, _e688.y, _e688.z, 0f).y);
                        texgen_mtx = mat2x4<f32>(vec4<f32>(_e672.x, _e672.y, _e672.z, _e672.w), vec4<f32>(_e695.x, _e695.y, _e695.z, _e695.w));
                        let _e708: i32 = global.texgen_mode;
                        if (_e708 == 2i) {
                            {
                                let _e711: f32 = prime_normal_set_1;
                                if (_e711 > 0.5f) {
                                    let _e714: vec3<f32> = prime_normal_1;
                                    local_7 = _e714;
                                } else {
                                    let _e715: vec4<f32> = global.prime_imm_normal;
                                    local_7 = _e715.xyz;
                                }
                                let _e718: vec3<f32> = local_7;
                                let _e724: mat2x4<f32> = texgen_mtx;
                                texcoord = (vec4<f32>(_e718.x, _e718.y, _e718.z, 1f) * _e724);
                                return;
                            }
                        } else {
                            {
                                let _e726: vec3<f32> = prime_position_1;
                                let _e732: vec3<f32> = vec4<f32>(_e726.x, _e726.y, _e726.z, 1f).xyz;
                                let _e738: mat2x4<f32> = texgen_mtx;
                                texcoord = (vec4<f32>(_e732.x, _e732.y, _e732.z, 1f) * _e738);
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
    let _e334: vec4<f32> = gl_Position;
    let _e336: vec4<f32> = gl_Position;
    gl_Position.z = ((_e334.z + _e336.w) * 0.5f);
    let _e341: vec4<f32> = gl_Position;
    let _e343: vec4<f32> = gl_Position;
    let _e345: vec4<f32> = global.prime_viewport;
    let _e348: vec4<f32> = global.prime_viewport;
    let _e350: vec4<f32> = gl_Position;
    let _e353: vec2<f32> = ((_e343.xy * _e345.xy) + (_e348.zw * _e350.w));
    gl_Position.x = _e353.x;
    gl_Position.y = _e353.y;
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
    let _e364: vec4<f32> = gl_Position;
    let _e366: vec2<f32> = texcoord;
    let _e368: vec4<f32> = color;
    let _e370: vec3<f32> = surface_normal;
    let _e372: vec3<f32> = surface_position;
    return VertexOutput(_e364, _e366, _e368, _e370, _e372);
}
