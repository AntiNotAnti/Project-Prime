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
    cosmetic_skin: i32,
    _pad_2528_0_: f32,
    _pad_2528_1_: f32,
    _pad_2528_2_: f32,
    cosmetic_preserve_palette: i32,
    _pad_2544_0_: f32,
    _pad_2544_1_: f32,
    _pad_2544_2_: f32,
    cosmetic_effect: i32,
    _pad_2560_0_: f32,
    _pad_2560_1_: f32,
    _pad_2560_2_: f32,
    cosmetic_time: f32,
    _pad_2576_0_: f32,
    _pad_2576_1_: f32,
    _pad_2576_2_: f32,
    cosmetic_primary: vec3<f32>,
    _pad_2592_0_: f32,
    cosmetic_secondary: vec3<f32>,
    _pad_2608_0_: f32,
    cosmetic_intensity: f32,
    _pad_2624_0_: f32,
    _pad_2624_1_: f32,
    _pad_2624_2_: f32,
    cosmetic_pulse: f32,
    _pad_2640_0_: f32,
    _pad_2640_1_: f32,
    _pad_2640_2_: f32,
    cosmetic_scroll: f32,
    _pad_2656_0_: f32,
    _pad_2656_1_: f32,
    _pad_2656_2_: f32,
    cosmetic_dissolve: f32,
    _pad_2672_0_: f32,
    _pad_2672_1_: f32,
    _pad_2672_2_: f32,
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
    let _e178: vec2<f32> = uv_1;
    let _e180: vec2<f32> = uv_1;
    let _e183: vec2<f32> = uv_1;
    let _e188: vec4<f32> = global.prime_texture_flip[0];
    let _e190: vec2<f32> = uv_1;
    let _e193: vec2<f32> = uv_1;
    let _e198: vec4<f32> = global.prime_texture_flip[0];
    let _e202: vec2<f32> = uv_1;
    let _e204: vec2<f32> = uv_1;
    let _e207: vec2<f32> = uv_1;
    let _e212: vec4<f32> = global.prime_texture_flip[0];
    let _e214: vec2<f32> = uv_1;
    let _e217: vec2<f32> = uv_1;
    let _e222: vec4<f32> = global.prime_texture_flip[0];
    let _e226: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e202.x, mix(_e214.y, (1f - _e217.y), _e222.x)));
    return _e226;
}

fn prime_sample_normal_tex(uv_2: vec2<f32>) -> vec4<f32> {
    var uv_3: vec2<f32>;

    uv_3 = uv_2;
    let _e180: vec2<f32> = uv_3;
    let _e182: vec2<f32> = uv_3;
    let _e185: vec2<f32> = uv_3;
    let _e190: vec4<f32> = global.prime_texture_flip[1];
    let _e192: vec2<f32> = uv_3;
    let _e195: vec2<f32> = uv_3;
    let _e200: vec4<f32> = global.prime_texture_flip[1];
    let _e204: vec2<f32> = uv_3;
    let _e206: vec2<f32> = uv_3;
    let _e209: vec2<f32> = uv_3;
    let _e214: vec4<f32> = global.prime_texture_flip[1];
    let _e216: vec2<f32> = uv_3;
    let _e219: vec2<f32> = uv_3;
    let _e224: vec4<f32> = global.prime_texture_flip[1];
    let _e228: vec4<f32> = textureSample(prime_tex_normal_tex, prime_sampler_normal_tex, vec2<f32>(_e204.x, mix(_e216.y, (1f - _e219.y), _e224.x)));
    return _e228;
}

fn prime_sample_specular_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e182: vec2<f32> = uv_5;
    let _e184: vec2<f32> = uv_5;
    let _e187: vec2<f32> = uv_5;
    let _e192: vec4<f32> = global.prime_texture_flip[2];
    let _e194: vec2<f32> = uv_5;
    let _e197: vec2<f32> = uv_5;
    let _e202: vec4<f32> = global.prime_texture_flip[2];
    let _e206: vec2<f32> = uv_5;
    let _e208: vec2<f32> = uv_5;
    let _e211: vec2<f32> = uv_5;
    let _e216: vec4<f32> = global.prime_texture_flip[2];
    let _e218: vec2<f32> = uv_5;
    let _e221: vec2<f32> = uv_5;
    let _e226: vec4<f32> = global.prime_texture_flip[2];
    let _e230: vec4<f32> = textureSample(prime_tex_specular_tex, prime_sampler_specular_tex, vec2<f32>(_e206.x, mix(_e218.y, (1f - _e221.y), _e226.x)));
    return _e230;
}

fn prime_sample_emissive_tex(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e184: vec2<f32> = uv_7;
    let _e186: vec2<f32> = uv_7;
    let _e189: vec2<f32> = uv_7;
    let _e194: vec4<f32> = global.prime_texture_flip[3];
    let _e196: vec2<f32> = uv_7;
    let _e199: vec2<f32> = uv_7;
    let _e204: vec4<f32> = global.prime_texture_flip[3];
    let _e208: vec2<f32> = uv_7;
    let _e210: vec2<f32> = uv_7;
    let _e213: vec2<f32> = uv_7;
    let _e218: vec4<f32> = global.prime_texture_flip[3];
    let _e220: vec2<f32> = uv_7;
    let _e223: vec2<f32> = uv_7;
    let _e228: vec4<f32> = global.prime_texture_flip[3];
    let _e232: vec4<f32> = textureSample(prime_tex_emissive_tex, prime_sampler_emissive_tex, vec2<f32>(_e208.x, mix(_e220.y, (1f - _e223.y), _e228.x)));
    return _e232;
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
    var local_5: vec3<f32>;
    var local_6: vec3<f32>;
    var tex_mul: mat4x4<f32>;
    var tm: mat2x4<f32>;
    var local_7: vec3<f32>;
    var local_8: vec2<f32>;

    let _e194: i32 = global._prime_weighted_skinning;
    if (_e194 != 0i) {
        {
            let _e197: vec3<f32> = prime_uv_1;
            let _e206: vec3<f32> = prime_uv_1;
            packed = floor((vec4<f32>(_e206.x, _e206.y, _e206.z, 0f).z + 0.5f));
            let _e219: f32 = packed;
            j0_ = i32((_e219 - (floor((_e219 / 32f)) * 32f)));
            let _e227: f32 = packed;
            let _e230: f32 = packed;
            packed = floor((_e230 / 32f));
            let _e236: f32 = packed;
            j1_ = i32((_e236 - (floor((_e236 / 32f)) * 32f)));
            let _e244: f32 = packed;
            let _e247: f32 = packed;
            packed = floor((_e247 / 32f));
            let _e253: f32 = packed;
            j2_ = i32((_e253 - (floor((_e253 / 32f)) * 32f)));
            let _e261: f32 = packed;
            let _e264: f32 = packed;
            packed = floor((_e264 / 32f));
            let _e270: f32 = packed;
            j3_ = i32((_e270 - (floor((_e270 / 32f)) * 32f)));
            let _e278: f32 = prime_color_set_1;
            if (_e278 > 0.5f) {
                let _e281: vec4<f32> = prime_color_1;
                local = _e281;
            } else {
                let _e282: vec4<f32> = global.prime_imm_color;
                local = _e282;
            }
            let _e287: f32 = prime_color_set_1;
            if (_e287 > 0.5f) {
                let _e290: vec4<f32> = prime_color_1;
                local_1 = _e290;
            } else {
                let _e291: vec4<f32> = global.prime_imm_color;
                local_1 = _e291;
            }
            let _e293: vec4<f32> = local_1;
            weights = max(_e293, vec4(0f));
            let _e298: vec4<f32> = weights;
            let _e300: vec4<f32> = weights;
            let _e303: vec4<f32> = weights;
            let _e306: vec4<f32> = weights;
            total = (((_e298.x + _e300.y) + _e303.z) + _e306.w);
            let _e310: f32 = total;
            if (_e310 > 0.000001f) {
                let _e313: vec4<f32> = weights;
                let _e314: f32 = total;
                local_2 = (_e313 / vec4(_e314));
            } else {
                local_2 = vec4<f32>(1f, 0f, 0f, 0f);
            }
            let _e323: vec4<f32> = local_2;
            weights = _e323;
            let _e324: i32 = j0_;
            let _e326: mat4x4<f32> = global.mtx_stack[_e324];
            let _e327: vec4<f32> = weights;
            let _e330: i32 = j1_;
            let _e332: mat4x4<f32> = global.mtx_stack[_e330];
            let _e333: vec4<f32> = weights;
            let _e337: i32 = j2_;
            let _e339: mat4x4<f32> = global.mtx_stack[_e337];
            let _e340: vec4<f32> = weights;
            let _e344: i32 = j3_;
            let _e346: mat4x4<f32> = global.mtx_stack[_e344];
            let _e347: vec4<f32> = weights;
            stack_mtx = ((((_e326 * _e327.x) + (_e332 * _e333.y)) + (_e339 * _e340.z)) + (_e346 * _e347.w));
        }
    } else {
        {
            let _e351: vec3<f32> = prime_uv_1;
            let _e360: vec3<f32> = prime_uv_1;
            let _e372: mat4x4<f32> = global.mtx_stack[i32(clamp(vec4<f32>(_e360.x, _e360.y, _e360.z, 0f).z, 0f, 31f))];
            stack_mtx = _e372;
        }
    }
    let _e373: mat4x4<f32> = stack_mtx;
    let _e374: mat4x4<f32> = global.view_inv_mtx;
    model_mtx = (_e373 * _e374);
    let _e377: mat4x4<f32> = global.proj_mtx;
    let _e378: mat4x4<f32> = global.view_mtx;
    let _e380: mat4x4<f32> = model_mtx;
    let _e382: vec3<f32> = prime_position_1;
    gl_Position = (((_e377 * _e378) * _e380) * vec4<f32>(_e382.x, _e382.y, _e382.z, 1f));
    let _e389: i32 = global._prime_weighted_skinning;
    if (_e389 != 0i) {
        local_4 = vec4(1f);
    } else {
        let _e394: f32 = prime_color_set_1;
        if (_e394 > 0.5f) {
            let _e397: vec4<f32> = prime_color_1;
            local_3 = _e397;
        } else {
            let _e398: vec4<f32> = global.prime_imm_color;
            local_3 = _e398;
        }
        let _e400: vec4<f32> = local_3;
        let _e401: vec3<f32> = _e400.xyz;
        local_4 = vec4<f32>(_e401.x, _e401.y, _e401.z, 1f);
    }
    let _e408: vec4<f32> = local_4;
    vertex_color = _e408;
    let _e409: mat4x4<f32> = model_mtx;
    let _e419: f32 = prime_normal_set_1;
    if (_e419 > 0.5f) {
        let _e422: vec3<f32> = prime_normal_1;
        local_5 = _e422;
    } else {
        let _e423: vec4<f32> = global.prime_imm_normal;
        local_5 = _e423.xyz;
    }
    let _e426: vec3<f32> = local_5;
    let _e428: mat4x4<f32> = model_mtx;
    let _e438: f32 = prime_normal_set_1;
    if (_e438 > 0.5f) {
        let _e441: vec3<f32> = prime_normal_1;
        local_6 = _e441;
    } else {
        let _e442: vec4<f32> = global.prime_imm_normal;
        local_6 = _e442.xyz;
    }
    let _e445: vec3<f32> = local_6;
    surface_normal = normalize((mat3x3<f32>(_e428[0].xyz, _e428[1].xyz, _e428[2].xyz) * _e445));
    let _e448: mat4x4<f32> = model_mtx;
    let _e449: vec3<f32> = prime_position_1;
    surface_position = (_e448 * vec4<f32>(_e449.x, _e449.y, _e449.z, 1f)).xyz;
    texcoord = vec2(0f);
    let _e459: i32 = global.texgen_mode;
    let _e462: i32 = global.texgen_mode;
    if ((_e459 == 0i) || (_e462 == 1i)) {
        {
            let _e466: mat4x4<f32> = global.tex_mtx;
            let _e467: vec3<f32> = prime_uv_1;
            let _e473: vec2<f32> = vec4<f32>(_e467.x, _e467.y, _e467.z, 0f).xy;
            texcoord = vec2<f32>((_e466 * vec4<f32>(_e473.x, _e473.y, 0f, 1f)).xy);
            return;
        }
    } else {
        {
            let _e482: mat4x4<f32> = global.tex_mtx;
            tex_mul = _e482;
            let _e484: i32 = global.texgen_mode;
            if (_e484 == 2i) {
                let _e487: mat4x4<f32> = global.tex_mtx;
                let _e488: mat4x4<f32> = global.view_mtx;
                let _e490: mat4x4<f32> = stack_mtx;
                let _e499: mat3x3<f32> = mat3x3<f32>(_e490[0].xyz, _e490[1].xyz, _e490[2].xyz);
                let _e520: mat4x4<f32> = global.tex_mtx;
                let _e521: mat4x4<f32> = global.view_mtx;
                let _e523: mat4x4<f32> = stack_mtx;
                let _e532: mat3x3<f32> = mat3x3<f32>(_e523[0].xyz, _e523[1].xyz, _e523[2].xyz);
                tex_mul = transpose(((_e520 * _e521) * mat4x4<f32>(vec4<f32>(_e532[0].x, _e532[0].y, _e532[0].z, 0f), vec4<f32>(_e532[1].x, _e532[1].y, _e532[1].z, 0f), vec4<f32>(_e532[2].x, _e532[2].y, _e532[2].z, 0f), vec4<f32>(0f, 0f, 0f, 1f))));
            }
            let _e558: f32 = tex_mul[0][0];
            let _e563: f32 = tex_mul[0][1];
            let _e568: f32 = tex_mul[0][2];
            let _e569: vec3<f32> = prime_uv_1;
            let _e576: vec4<f32> = vec4<f32>(_e558, _e563, _e568, vec4<f32>(_e569.x, _e569.y, _e569.z, 0f).x);
            let _e581: f32 = tex_mul[1][0];
            let _e586: f32 = tex_mul[1][1];
            let _e591: f32 = tex_mul[1][2];
            let _e592: vec3<f32> = prime_uv_1;
            let _e599: vec4<f32> = vec4<f32>(_e581, _e586, _e591, vec4<f32>(_e592.x, _e592.y, _e592.z, 0f).y);
            tm = mat2x4<f32>(vec4<f32>(_e576.x, _e576.y, _e576.z, _e576.w), vec4<f32>(_e599.x, _e599.y, _e599.z, _e599.w));
            let _e612: i32 = global.texgen_mode;
            if (_e612 == 2i) {
                let _e615: f32 = prime_normal_set_1;
                if (_e615 > 0.5f) {
                    let _e618: vec3<f32> = prime_normal_1;
                    local_7 = _e618;
                } else {
                    let _e619: vec4<f32> = global.prime_imm_normal;
                    local_7 = _e619.xyz;
                }
                let _e622: vec3<f32> = local_7;
                let _e628: mat2x4<f32> = tm;
                local_8 = (vec4<f32>(_e622.x, _e622.y, _e622.z, 1f) * _e628);
            } else {
                let _e630: vec3<f32> = prime_position_1;
                let _e636: vec3<f32> = vec4<f32>(_e630.x, _e630.y, _e630.z, 1f).xyz;
                let _e642: mat2x4<f32> = tm;
                local_8 = (vec4<f32>(_e636.x, _e636.y, _e636.z, 1f) * _e642);
            }
            let _e645: vec2<f32> = local_8;
            texcoord = _e645;
            return;
        }
    }
}

fn main_1() {
    prime_original_main();
    let _e194: vec4<f32> = gl_Position;
    let _e196: vec4<f32> = gl_Position;
    gl_Position.z = ((_e194.z + _e196.w) * 0.5f);
    let _e201: vec4<f32> = gl_Position;
    let _e203: vec4<f32> = gl_Position;
    let _e205: vec4<f32> = global.prime_viewport;
    let _e208: vec4<f32> = global.prime_viewport;
    let _e210: vec4<f32> = gl_Position;
    let _e213: vec2<f32> = ((_e203.xy * _e205.xy) + (_e208.zw * _e210.w));
    gl_Position.x = _e213.x;
    gl_Position.y = _e213.y;
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
    let _e224: vec4<f32> = gl_Position;
    let _e226: vec2<f32> = texcoord;
    let _e228: vec4<f32> = vertex_color;
    let _e230: vec3<f32> = surface_normal;
    let _e232: vec3<f32> = surface_position;
    return VertexOutput(_e224, _e226, _e228, _e230, _e232);
}
