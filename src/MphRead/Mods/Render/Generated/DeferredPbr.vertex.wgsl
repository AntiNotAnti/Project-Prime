struct PrimeUniforms {
    proj_mtx: mat4x4<f32>,
    view_mtx: mat4x4<f32>,
    view_inv_mtx: mat4x4<f32>,
    tex_mtx: mat4x4<f32>,
    texgen_mode: i32,
    _pad_272_0_: f32,
    _pad_272_1_: f32,
    _pad_272_2_: f32,
    _prime_weighted_skinning: i32,
    _pad_288_0_: f32,
    _pad_288_1_: f32,
    _pad_288_2_: f32,
    mtx_stack: array<mat4x4<f32>, 32>,
    gbuffer_mode: i32,
    _pad_2352_0_: f32,
    _pad_2352_1_: f32,
    _pad_2352_2_: f32,
    _prime_use_texture: i32,
    _pad_2368_0_: f32,
    _pad_2368_1_: f32,
    _pad_2368_2_: f32,
    _prime_use_normal_map: i32,
    _pad_2384_0_: f32,
    _pad_2384_1_: f32,
    _pad_2384_2_: f32,
    _prime_use_specular_map: i32,
    _pad_2400_0_: f32,
    _pad_2400_1_: f32,
    _pad_2400_2_: f32,
    _prime_use_emissive_map: i32,
    _pad_2416_0_: f32,
    _pad_2416_1_: f32,
    _pad_2416_2_: f32,
    _prime_use_override: i32,
    _pad_2432_0_: f32,
    _pad_2432_1_: f32,
    _pad_2432_2_: f32,
    override_color: vec4<f32>,
    _prime_use_pal_override: i32,
    _pad_2464_0_: f32,
    _pad_2464_1_: f32,
    _pad_2464_2_: f32,
    pal_override_color: vec4<f32>,
    material_specular: vec3<f32>,
    _pad_2496_0_: f32,
    material_emission: vec3<f32>,
    _pad_2512_0_: f32,
    emissive_intensity: f32,
    _pad_2528_0_: f32,
    _pad_2528_1_: f32,
    _pad_2528_2_: f32,
    cosmetic_skin: i32,
    _pad_2544_0_: f32,
    _pad_2544_1_: f32,
    _pad_2544_2_: f32,
    cosmetic_preserve_palette: i32,
    _pad_2560_0_: f32,
    _pad_2560_1_: f32,
    _pad_2560_2_: f32,
    cosmetic_effect: i32,
    _pad_2576_0_: f32,
    _pad_2576_1_: f32,
    _pad_2576_2_: f32,
    cosmetic_time: f32,
    _pad_2592_0_: f32,
    _pad_2592_1_: f32,
    _pad_2592_2_: f32,
    cosmetic_primary: vec3<f32>,
    _pad_2608_0_: f32,
    cosmetic_secondary: vec3<f32>,
    _pad_2624_0_: f32,
    cosmetic_intensity: f32,
    _pad_2640_0_: f32,
    _pad_2640_1_: f32,
    _pad_2640_2_: f32,
    cosmetic_pulse: f32,
    _pad_2656_0_: f32,
    _pad_2656_1_: f32,
    _pad_2656_2_: f32,
    cosmetic_scroll: f32,
    _pad_2672_0_: f32,
    _pad_2672_1_: f32,
    _pad_2672_2_: f32,
    cosmetic_dissolve: f32,
    _pad_2688_0_: f32,
    _pad_2688_1_: f32,
    _pad_2688_2_: f32,
    prime_viewport: vec4<f32>,
    prime_imm_color: vec4<f32>,
    prime_imm_normal: vec4<f32>,
    prime_texture_flip: array<vec4<f32>, 4>,
}

struct VertexOutput {
    @builtin(position) @invariant member: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
    @location(1) vertex_color: vec4<f32>,
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
var<private> vertex_color: vec4<f32>;
var<private> surface_normal: vec3<f32>;
var<private> surface_position: vec3<f32>;

fn prime_sample_tex(uv: vec2<f32>) -> vec4<f32> {
    var uv_1: vec2<f32>;

    uv_1 = uv;
    let _e186: vec2<f32> = uv_1;
    let _e188: vec2<f32> = uv_1;
    let _e191: vec2<f32> = uv_1;
    let _e196: vec4<f32> = global.prime_texture_flip[0];
    let _e198: vec2<f32> = uv_1;
    let _e201: vec2<f32> = uv_1;
    let _e206: vec4<f32> = global.prime_texture_flip[0];
    let _e210: vec2<f32> = uv_1;
    let _e212: vec2<f32> = uv_1;
    let _e215: vec2<f32> = uv_1;
    let _e220: vec4<f32> = global.prime_texture_flip[0];
    let _e222: vec2<f32> = uv_1;
    let _e225: vec2<f32> = uv_1;
    let _e230: vec4<f32> = global.prime_texture_flip[0];
    let _e234: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e210.x, mix(_e222.y, (1f - _e225.y), _e230.x)));
    return _e234;
}

fn prime_sample_normal_tex(uv_2: vec2<f32>) -> vec4<f32> {
    var uv_3: vec2<f32>;

    uv_3 = uv_2;
    let _e188: vec2<f32> = uv_3;
    let _e190: vec2<f32> = uv_3;
    let _e193: vec2<f32> = uv_3;
    let _e198: vec4<f32> = global.prime_texture_flip[1];
    let _e200: vec2<f32> = uv_3;
    let _e203: vec2<f32> = uv_3;
    let _e208: vec4<f32> = global.prime_texture_flip[1];
    let _e212: vec2<f32> = uv_3;
    let _e214: vec2<f32> = uv_3;
    let _e217: vec2<f32> = uv_3;
    let _e222: vec4<f32> = global.prime_texture_flip[1];
    let _e224: vec2<f32> = uv_3;
    let _e227: vec2<f32> = uv_3;
    let _e232: vec4<f32> = global.prime_texture_flip[1];
    let _e236: vec4<f32> = textureSample(prime_tex_normal_tex, prime_sampler_normal_tex, vec2<f32>(_e212.x, mix(_e224.y, (1f - _e227.y), _e232.x)));
    return _e236;
}

fn prime_sample_specular_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e190: vec2<f32> = uv_5;
    let _e192: vec2<f32> = uv_5;
    let _e195: vec2<f32> = uv_5;
    let _e200: vec4<f32> = global.prime_texture_flip[2];
    let _e202: vec2<f32> = uv_5;
    let _e205: vec2<f32> = uv_5;
    let _e210: vec4<f32> = global.prime_texture_flip[2];
    let _e214: vec2<f32> = uv_5;
    let _e216: vec2<f32> = uv_5;
    let _e219: vec2<f32> = uv_5;
    let _e224: vec4<f32> = global.prime_texture_flip[2];
    let _e226: vec2<f32> = uv_5;
    let _e229: vec2<f32> = uv_5;
    let _e234: vec4<f32> = global.prime_texture_flip[2];
    let _e238: vec4<f32> = textureSample(prime_tex_specular_tex, prime_sampler_specular_tex, vec2<f32>(_e214.x, mix(_e226.y, (1f - _e229.y), _e234.x)));
    return _e238;
}

fn prime_sample_emissive_tex(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e192: vec2<f32> = uv_7;
    let _e194: vec2<f32> = uv_7;
    let _e197: vec2<f32> = uv_7;
    let _e202: vec4<f32> = global.prime_texture_flip[3];
    let _e204: vec2<f32> = uv_7;
    let _e207: vec2<f32> = uv_7;
    let _e212: vec4<f32> = global.prime_texture_flip[3];
    let _e216: vec2<f32> = uv_7;
    let _e218: vec2<f32> = uv_7;
    let _e221: vec2<f32> = uv_7;
    let _e226: vec4<f32> = global.prime_texture_flip[3];
    let _e228: vec2<f32> = uv_7;
    let _e231: vec2<f32> = uv_7;
    let _e236: vec4<f32> = global.prime_texture_flip[3];
    let _e240: vec4<f32> = textureSample(prime_tex_emissive_tex, prime_sampler_emissive_tex, vec2<f32>(_e216.x, mix(_e228.y, (1f - _e231.y), _e236.x)));
    return _e240;
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
    var local_5: vec3<f32>;
    var local_6: vec3<f32>;
    var tex_mul: mat4x4<f32>;
    var tm: mat2x4<f32>;
    var local_7: vec3<f32>;
    var local_8: vec2<f32>;

    let _e202: i32 = global._prime_weighted_skinning;
    if (_e202 != 0i) {
        {
            let _e205: vec3<f32> = prime_uv_1;
            let _e214: vec3<f32> = prime_uv_1;
            packedJoints = floor((vec4<f32>(_e214.x, _e214.y, _e214.z, 0f).z + 0.5f));
            let _e227: f32 = packedJoints;
            j0_ = i32((_e227 - (floor((_e227 / 32f)) * 32f)));
            let _e235: f32 = packedJoints;
            let _e238: f32 = packedJoints;
            packedJoints = floor((_e238 / 32f));
            let _e244: f32 = packedJoints;
            j1_ = i32((_e244 - (floor((_e244 / 32f)) * 32f)));
            let _e252: f32 = packedJoints;
            let _e255: f32 = packedJoints;
            packedJoints = floor((_e255 / 32f));
            let _e261: f32 = packedJoints;
            j2_ = i32((_e261 - (floor((_e261 / 32f)) * 32f)));
            let _e269: f32 = packedJoints;
            let _e272: f32 = packedJoints;
            packedJoints = floor((_e272 / 32f));
            let _e278: f32 = packedJoints;
            j3_ = i32((_e278 - (floor((_e278 / 32f)) * 32f)));
            let _e286: f32 = prime_color_set_1;
            if (_e286 > 0.5f) {
                let _e289: vec4<f32> = prime_color_1;
                local = _e289;
            } else {
                let _e290: vec4<f32> = global.prime_imm_color;
                local = _e290;
            }
            let _e295: f32 = prime_color_set_1;
            if (_e295 > 0.5f) {
                let _e298: vec4<f32> = prime_color_1;
                local_1 = _e298;
            } else {
                let _e299: vec4<f32> = global.prime_imm_color;
                local_1 = _e299;
            }
            let _e301: vec4<f32> = local_1;
            weights = max(_e301, vec4(0f));
            let _e306: vec4<f32> = weights;
            let _e308: vec4<f32> = weights;
            let _e311: vec4<f32> = weights;
            let _e314: vec4<f32> = weights;
            total = (((_e306.x + _e308.y) + _e311.z) + _e314.w);
            let _e318: f32 = total;
            if (_e318 > 0.000001f) {
                let _e321: vec4<f32> = weights;
                let _e322: f32 = total;
                local_2 = (_e321 / vec4(_e322));
            } else {
                local_2 = vec4<f32>(1f, 0f, 0f, 0f);
            }
            let _e331: vec4<f32> = local_2;
            weights = _e331;
            let _e332: i32 = j0_;
            let _e334: mat4x4<f32> = global.mtx_stack[_e332];
            let _e335: vec4<f32> = weights;
            let _e338: i32 = j1_;
            let _e340: mat4x4<f32> = global.mtx_stack[_e338];
            let _e341: vec4<f32> = weights;
            let _e345: i32 = j2_;
            let _e347: mat4x4<f32> = global.mtx_stack[_e345];
            let _e348: vec4<f32> = weights;
            let _e352: i32 = j3_;
            let _e354: mat4x4<f32> = global.mtx_stack[_e352];
            let _e355: vec4<f32> = weights;
            stack_mtx = ((((_e334 * _e335.x) + (_e340 * _e341.y)) + (_e347 * _e348.z)) + (_e354 * _e355.w));
        }
    } else {
        {
            let _e359: vec3<f32> = prime_uv_1;
            let _e368: vec3<f32> = prime_uv_1;
            let _e380: mat4x4<f32> = global.mtx_stack[i32(clamp(vec4<f32>(_e368.x, _e368.y, _e368.z, 0f).z, 0f, 31f))];
            stack_mtx = _e380;
        }
    }
    let _e381: mat4x4<f32> = stack_mtx;
    let _e382: mat4x4<f32> = global.view_inv_mtx;
    model_mtx = (_e381 * _e382);
    let _e385: mat4x4<f32> = global.proj_mtx;
    let _e386: mat4x4<f32> = global.view_mtx;
    let _e388: mat4x4<f32> = model_mtx;
    let _e390: vec3<f32> = prime_position_1;
    gl_Position = (((_e385 * _e386) * _e388) * vec4<f32>(_e390.x, _e390.y, _e390.z, 1f));
    let _e397: i32 = global._prime_weighted_skinning;
    if (_e397 != 0i) {
        local_4 = vec4(1f);
    } else {
        let _e402: f32 = prime_color_set_1;
        if (_e402 > 0.5f) {
            let _e405: vec4<f32> = prime_color_1;
            local_3 = _e405;
        } else {
            let _e406: vec4<f32> = global.prime_imm_color;
            local_3 = _e406;
        }
        let _e408: vec4<f32> = local_3;
        let _e409: vec3<f32> = _e408.xyz;
        local_4 = vec4<f32>(_e409.x, _e409.y, _e409.z, 1f);
    }
    let _e416: vec4<f32> = local_4;
    vertex_color = _e416;
    let _e417: mat4x4<f32> = model_mtx;
    let _e427: f32 = prime_normal_set_1;
    if (_e427 > 0.5f) {
        let _e430: vec3<f32> = prime_normal_1;
        local_5 = _e430;
    } else {
        let _e431: vec4<f32> = global.prime_imm_normal;
        local_5 = _e431.xyz;
    }
    let _e434: vec3<f32> = local_5;
    let _e436: mat4x4<f32> = model_mtx;
    let _e446: f32 = prime_normal_set_1;
    if (_e446 > 0.5f) {
        let _e449: vec3<f32> = prime_normal_1;
        local_6 = _e449;
    } else {
        let _e450: vec4<f32> = global.prime_imm_normal;
        local_6 = _e450.xyz;
    }
    let _e453: vec3<f32> = local_6;
    surface_normal = normalize((mat3x3<f32>(_e436[0].xyz, _e436[1].xyz, _e436[2].xyz) * _e453));
    let _e456: mat4x4<f32> = model_mtx;
    let _e457: vec3<f32> = prime_position_1;
    surface_position = (_e456 * vec4<f32>(_e457.x, _e457.y, _e457.z, 1f)).xyz;
    texcoord = vec2(0f);
    let _e467: i32 = global.texgen_mode;
    let _e470: i32 = global.texgen_mode;
    if ((_e467 == 0i) || (_e470 == 1i)) {
        {
            let _e474: mat4x4<f32> = global.tex_mtx;
            let _e475: vec3<f32> = prime_uv_1;
            let _e481: vec2<f32> = vec4<f32>(_e475.x, _e475.y, _e475.z, 0f).xy;
            texcoord = vec2<f32>((_e474 * vec4<f32>(_e481.x, _e481.y, 0f, 1f)).xy);
            return;
        }
    } else {
        {
            let _e490: mat4x4<f32> = global.tex_mtx;
            tex_mul = _e490;
            let _e492: i32 = global.texgen_mode;
            if (_e492 == 2i) {
                let _e495: mat4x4<f32> = global.tex_mtx;
                let _e496: mat4x4<f32> = global.view_mtx;
                let _e498: mat4x4<f32> = stack_mtx;
                let _e507: mat3x3<f32> = mat3x3<f32>(_e498[0].xyz, _e498[1].xyz, _e498[2].xyz);
                let _e528: mat4x4<f32> = global.tex_mtx;
                let _e529: mat4x4<f32> = global.view_mtx;
                let _e531: mat4x4<f32> = stack_mtx;
                let _e540: mat3x3<f32> = mat3x3<f32>(_e531[0].xyz, _e531[1].xyz, _e531[2].xyz);
                tex_mul = transpose(((_e528 * _e529) * mat4x4<f32>(vec4<f32>(_e540[0].x, _e540[0].y, _e540[0].z, 0f), vec4<f32>(_e540[1].x, _e540[1].y, _e540[1].z, 0f), vec4<f32>(_e540[2].x, _e540[2].y, _e540[2].z, 0f), vec4<f32>(0f, 0f, 0f, 1f))));
            }
            let _e566: f32 = tex_mul[0][0];
            let _e571: f32 = tex_mul[0][1];
            let _e576: f32 = tex_mul[0][2];
            let _e577: vec3<f32> = prime_uv_1;
            let _e584: vec4<f32> = vec4<f32>(_e566, _e571, _e576, vec4<f32>(_e577.x, _e577.y, _e577.z, 0f).x);
            let _e589: f32 = tex_mul[1][0];
            let _e594: f32 = tex_mul[1][1];
            let _e599: f32 = tex_mul[1][2];
            let _e600: vec3<f32> = prime_uv_1;
            let _e607: vec4<f32> = vec4<f32>(_e589, _e594, _e599, vec4<f32>(_e600.x, _e600.y, _e600.z, 0f).y);
            tm = mat2x4<f32>(vec4<f32>(_e584.x, _e584.y, _e584.z, _e584.w), vec4<f32>(_e607.x, _e607.y, _e607.z, _e607.w));
            let _e620: i32 = global.texgen_mode;
            if (_e620 == 2i) {
                let _e623: f32 = prime_normal_set_1;
                if (_e623 > 0.5f) {
                    let _e626: vec3<f32> = prime_normal_1;
                    local_7 = _e626;
                } else {
                    let _e627: vec4<f32> = global.prime_imm_normal;
                    local_7 = _e627.xyz;
                }
                let _e630: vec3<f32> = local_7;
                let _e636: mat2x4<f32> = tm;
                local_8 = (vec4<f32>(_e630.x, _e630.y, _e630.z, 1f) * _e636);
            } else {
                let _e638: vec3<f32> = prime_position_1;
                let _e644: vec3<f32> = vec4<f32>(_e638.x, _e638.y, _e638.z, 1f).xyz;
                let _e650: mat2x4<f32> = tm;
                local_8 = (vec4<f32>(_e644.x, _e644.y, _e644.z, 1f) * _e650);
            }
            let _e653: vec2<f32> = local_8;
            texcoord = _e653;
            return;
        }
    }
}

fn main_1() {
    prime_original_main();
    let _e202: vec4<f32> = gl_Position;
    let _e204: vec4<f32> = gl_Position;
    gl_Position.z = ((_e202.z + _e204.w) * 0.5f);
    let _e209: vec4<f32> = gl_Position;
    let _e211: vec4<f32> = gl_Position;
    let _e213: vec4<f32> = global.prime_viewport;
    let _e216: vec4<f32> = global.prime_viewport;
    let _e218: vec4<f32> = gl_Position;
    let _e221: vec2<f32> = ((_e211.xy * _e213.xy) + (_e216.zw * _e218.w));
    gl_Position.x = _e221.x;
    gl_Position.y = _e221.y;
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
    let _e232: vec4<f32> = gl_Position;
    let _e234: vec2<f32> = texcoord;
    let _e236: vec4<f32> = vertex_color;
    let _e238: vec3<f32> = surface_normal;
    let _e240: vec3<f32> = surface_position;
    return VertexOutput(_e232, _e234, _e236, _e238, _e240);
}
