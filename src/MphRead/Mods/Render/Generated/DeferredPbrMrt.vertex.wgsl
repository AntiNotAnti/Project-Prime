struct PrimeUniforms {
    proj_mtx: mat4x4<f32>,
    view_mtx: mat4x4<f32>,
    view_inv_mtx: mat4x4<f32>,
    tex_mtx: mat4x4<f32>,
    texgen_mode: i32,
    _pad_272_0_: f32,
    _pad_272_1_: f32,
    _pad_272_2_: f32,
    mtx_stack: array<mat4x4<f32>, 32>,
    gbuffer_mode: i32,
    _pad_2336_0_: f32,
    _pad_2336_1_: f32,
    _pad_2336_2_: f32,
    _prime_use_texture: i32,
    _pad_2352_0_: f32,
    _pad_2352_1_: f32,
    _pad_2352_2_: f32,
    _prime_use_normal_map: i32,
    _pad_2368_0_: f32,
    _pad_2368_1_: f32,
    _pad_2368_2_: f32,
    _prime_use_specular_map: i32,
    _pad_2384_0_: f32,
    _pad_2384_1_: f32,
    _pad_2384_2_: f32,
    _prime_use_emissive_map: i32,
    _pad_2400_0_: f32,
    _pad_2400_1_: f32,
    _pad_2400_2_: f32,
    _prime_use_override: i32,
    _pad_2416_0_: f32,
    _pad_2416_1_: f32,
    _pad_2416_2_: f32,
    override_color: vec4<f32>,
    _prime_use_pal_override: i32,
    _pad_2448_0_: f32,
    _pad_2448_1_: f32,
    _pad_2448_2_: f32,
    pal_override_color: vec4<f32>,
    material_specular: vec3<f32>,
    _pad_2480_0_: f32,
    material_emission: vec3<f32>,
    _pad_2496_0_: f32,
    cosmetic_skin: i32,
    _pad_2512_0_: f32,
    _pad_2512_1_: f32,
    _pad_2512_2_: f32,
    cosmetic_preserve_palette: i32,
    _pad_2528_0_: f32,
    _pad_2528_1_: f32,
    _pad_2528_2_: f32,
    cosmetic_effect: i32,
    _pad_2544_0_: f32,
    _pad_2544_1_: f32,
    _pad_2544_2_: f32,
    cosmetic_time: f32,
    _pad_2560_0_: f32,
    _pad_2560_1_: f32,
    _pad_2560_2_: f32,
    cosmetic_primary: vec3<f32>,
    _pad_2576_0_: f32,
    cosmetic_secondary: vec3<f32>,
    _pad_2592_0_: f32,
    cosmetic_intensity: f32,
    _pad_2608_0_: f32,
    _pad_2608_1_: f32,
    _pad_2608_2_: f32,
    cosmetic_pulse: f32,
    _pad_2624_0_: f32,
    _pad_2624_1_: f32,
    _pad_2624_2_: f32,
    cosmetic_scroll: f32,
    _pad_2640_0_: f32,
    _pad_2640_1_: f32,
    _pad_2640_2_: f32,
    cosmetic_dissolve: f32,
    _pad_2656_0_: f32,
    _pad_2656_1_: f32,
    _pad_2656_2_: f32,
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
    let _e170: vec2<f32> = uv_1;
    let _e172: vec2<f32> = uv_1;
    let _e175: vec2<f32> = uv_1;
    let _e180: vec4<f32> = global.prime_texture_flip[0];
    let _e182: vec2<f32> = uv_1;
    let _e185: vec2<f32> = uv_1;
    let _e190: vec4<f32> = global.prime_texture_flip[0];
    let _e194: vec2<f32> = uv_1;
    let _e196: vec2<f32> = uv_1;
    let _e199: vec2<f32> = uv_1;
    let _e204: vec4<f32> = global.prime_texture_flip[0];
    let _e206: vec2<f32> = uv_1;
    let _e209: vec2<f32> = uv_1;
    let _e214: vec4<f32> = global.prime_texture_flip[0];
    let _e218: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e194.x, mix(_e206.y, (1f - _e209.y), _e214.x)));
    return _e218;
}

fn prime_sample_normal_tex(uv_2: vec2<f32>) -> vec4<f32> {
    var uv_3: vec2<f32>;

    uv_3 = uv_2;
    let _e172: vec2<f32> = uv_3;
    let _e174: vec2<f32> = uv_3;
    let _e177: vec2<f32> = uv_3;
    let _e182: vec4<f32> = global.prime_texture_flip[1];
    let _e184: vec2<f32> = uv_3;
    let _e187: vec2<f32> = uv_3;
    let _e192: vec4<f32> = global.prime_texture_flip[1];
    let _e196: vec2<f32> = uv_3;
    let _e198: vec2<f32> = uv_3;
    let _e201: vec2<f32> = uv_3;
    let _e206: vec4<f32> = global.prime_texture_flip[1];
    let _e208: vec2<f32> = uv_3;
    let _e211: vec2<f32> = uv_3;
    let _e216: vec4<f32> = global.prime_texture_flip[1];
    let _e220: vec4<f32> = textureSample(prime_tex_normal_tex, prime_sampler_normal_tex, vec2<f32>(_e196.x, mix(_e208.y, (1f - _e211.y), _e216.x)));
    return _e220;
}

fn prime_sample_specular_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e174: vec2<f32> = uv_5;
    let _e176: vec2<f32> = uv_5;
    let _e179: vec2<f32> = uv_5;
    let _e184: vec4<f32> = global.prime_texture_flip[2];
    let _e186: vec2<f32> = uv_5;
    let _e189: vec2<f32> = uv_5;
    let _e194: vec4<f32> = global.prime_texture_flip[2];
    let _e198: vec2<f32> = uv_5;
    let _e200: vec2<f32> = uv_5;
    let _e203: vec2<f32> = uv_5;
    let _e208: vec4<f32> = global.prime_texture_flip[2];
    let _e210: vec2<f32> = uv_5;
    let _e213: vec2<f32> = uv_5;
    let _e218: vec4<f32> = global.prime_texture_flip[2];
    let _e222: vec4<f32> = textureSample(prime_tex_specular_tex, prime_sampler_specular_tex, vec2<f32>(_e198.x, mix(_e210.y, (1f - _e213.y), _e218.x)));
    return _e222;
}

fn prime_sample_emissive_tex(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e176: vec2<f32> = uv_7;
    let _e178: vec2<f32> = uv_7;
    let _e181: vec2<f32> = uv_7;
    let _e186: vec4<f32> = global.prime_texture_flip[3];
    let _e188: vec2<f32> = uv_7;
    let _e191: vec2<f32> = uv_7;
    let _e196: vec4<f32> = global.prime_texture_flip[3];
    let _e200: vec2<f32> = uv_7;
    let _e202: vec2<f32> = uv_7;
    let _e205: vec2<f32> = uv_7;
    let _e210: vec4<f32> = global.prime_texture_flip[3];
    let _e212: vec2<f32> = uv_7;
    let _e215: vec2<f32> = uv_7;
    let _e220: vec4<f32> = global.prime_texture_flip[3];
    let _e224: vec4<f32> = textureSample(prime_tex_emissive_tex, prime_sampler_emissive_tex, vec2<f32>(_e200.x, mix(_e212.y, (1f - _e215.y), _e220.x)));
    return _e224;
}

fn prime_original_main() {
    var stack_mtx: mat4x4<f32>;
    var model_mtx: mat4x4<f32>;
    var local: vec4<f32>;
    var local_1: vec3<f32>;
    var local_2: vec3<f32>;
    var tex_mul: mat4x4<f32>;
    var tm: mat2x4<f32>;
    var local_3: vec3<f32>;
    var local_4: vec2<f32>;

    let _e185: vec3<f32> = prime_uv_1;
    let _e194: vec3<f32> = prime_uv_1;
    let _e206: mat4x4<f32> = global.mtx_stack[i32(clamp(vec4<f32>(_e194.x, _e194.y, _e194.z, 0f).z, 0f, 31f))];
    stack_mtx = _e206;
    let _e208: mat4x4<f32> = stack_mtx;
    let _e209: mat4x4<f32> = global.view_inv_mtx;
    model_mtx = (_e208 * _e209);
    let _e212: mat4x4<f32> = global.proj_mtx;
    let _e213: mat4x4<f32> = global.view_mtx;
    let _e215: mat4x4<f32> = model_mtx;
    let _e217: vec3<f32> = prime_position_1;
    gl_Position = (((_e212 * _e213) * _e215) * vec4<f32>(_e217.x, _e217.y, _e217.z, 1f));
    let _e224: f32 = prime_color_set_1;
    if (_e224 > 0.5f) {
        let _e227: vec4<f32> = prime_color_1;
        local = _e227;
    } else {
        let _e228: vec4<f32> = global.prime_imm_color;
        local = _e228;
    }
    let _e230: vec4<f32> = local;
    let _e231: vec3<f32> = _e230.xyz;
    vertex_color = vec4<f32>(_e231.x, _e231.y, _e231.z, 1f);
    let _e237: mat4x4<f32> = model_mtx;
    let _e247: f32 = prime_normal_set_1;
    if (_e247 > 0.5f) {
        let _e250: vec3<f32> = prime_normal_1;
        local_1 = _e250;
    } else {
        let _e251: vec4<f32> = global.prime_imm_normal;
        local_1 = _e251.xyz;
    }
    let _e254: vec3<f32> = local_1;
    let _e256: mat4x4<f32> = model_mtx;
    let _e266: f32 = prime_normal_set_1;
    if (_e266 > 0.5f) {
        let _e269: vec3<f32> = prime_normal_1;
        local_2 = _e269;
    } else {
        let _e270: vec4<f32> = global.prime_imm_normal;
        local_2 = _e270.xyz;
    }
    let _e273: vec3<f32> = local_2;
    surface_normal = normalize((mat3x3<f32>(_e256[0].xyz, _e256[1].xyz, _e256[2].xyz) * _e273));
    let _e276: mat4x4<f32> = model_mtx;
    let _e277: vec3<f32> = prime_position_1;
    surface_position = (_e276 * vec4<f32>(_e277.x, _e277.y, _e277.z, 1f)).xyz;
    texcoord = vec2(0f);
    let _e287: i32 = global.texgen_mode;
    let _e290: i32 = global.texgen_mode;
    if ((_e287 == 0i) || (_e290 == 1i)) {
        {
            let _e294: mat4x4<f32> = global.tex_mtx;
            let _e295: vec3<f32> = prime_uv_1;
            let _e301: vec2<f32> = vec4<f32>(_e295.x, _e295.y, _e295.z, 0f).xy;
            texcoord = vec2<f32>((_e294 * vec4<f32>(_e301.x, _e301.y, 0f, 1f)).xy);
            return;
        }
    } else {
        {
            let _e310: mat4x4<f32> = global.tex_mtx;
            tex_mul = _e310;
            let _e312: i32 = global.texgen_mode;
            if (_e312 == 2i) {
                let _e315: mat4x4<f32> = global.tex_mtx;
                let _e316: mat4x4<f32> = global.view_mtx;
                let _e318: mat4x4<f32> = stack_mtx;
                let _e327: mat3x3<f32> = mat3x3<f32>(_e318[0].xyz, _e318[1].xyz, _e318[2].xyz);
                let _e348: mat4x4<f32> = global.tex_mtx;
                let _e349: mat4x4<f32> = global.view_mtx;
                let _e351: mat4x4<f32> = stack_mtx;
                let _e360: mat3x3<f32> = mat3x3<f32>(_e351[0].xyz, _e351[1].xyz, _e351[2].xyz);
                tex_mul = transpose(((_e348 * _e349) * mat4x4<f32>(vec4<f32>(_e360[0].x, _e360[0].y, _e360[0].z, 0f), vec4<f32>(_e360[1].x, _e360[1].y, _e360[1].z, 0f), vec4<f32>(_e360[2].x, _e360[2].y, _e360[2].z, 0f), vec4<f32>(0f, 0f, 0f, 1f))));
            }
            let _e386: f32 = tex_mul[0][0];
            let _e391: f32 = tex_mul[0][1];
            let _e396: f32 = tex_mul[0][2];
            let _e397: vec3<f32> = prime_uv_1;
            let _e404: vec4<f32> = vec4<f32>(_e386, _e391, _e396, vec4<f32>(_e397.x, _e397.y, _e397.z, 0f).x);
            let _e409: f32 = tex_mul[1][0];
            let _e414: f32 = tex_mul[1][1];
            let _e419: f32 = tex_mul[1][2];
            let _e420: vec3<f32> = prime_uv_1;
            let _e427: vec4<f32> = vec4<f32>(_e409, _e414, _e419, vec4<f32>(_e420.x, _e420.y, _e420.z, 0f).y);
            tm = mat2x4<f32>(vec4<f32>(_e404.x, _e404.y, _e404.z, _e404.w), vec4<f32>(_e427.x, _e427.y, _e427.z, _e427.w));
            let _e440: i32 = global.texgen_mode;
            if (_e440 == 2i) {
                let _e443: f32 = prime_normal_set_1;
                if (_e443 > 0.5f) {
                    let _e446: vec3<f32> = prime_normal_1;
                    local_3 = _e446;
                } else {
                    let _e447: vec4<f32> = global.prime_imm_normal;
                    local_3 = _e447.xyz;
                }
                let _e450: vec3<f32> = local_3;
                let _e456: mat2x4<f32> = tm;
                local_4 = (vec4<f32>(_e450.x, _e450.y, _e450.z, 1f) * _e456);
            } else {
                let _e458: vec3<f32> = prime_position_1;
                let _e464: vec3<f32> = vec4<f32>(_e458.x, _e458.y, _e458.z, 1f).xyz;
                let _e470: mat2x4<f32> = tm;
                local_4 = (vec4<f32>(_e464.x, _e464.y, _e464.z, 1f) * _e470);
            }
            let _e473: vec2<f32> = local_4;
            texcoord = _e473;
            return;
        }
    }
}

fn main_1() {
    prime_original_main();
    let _e186: vec4<f32> = gl_Position;
    let _e188: vec4<f32> = gl_Position;
    gl_Position.z = ((_e186.z + _e188.w) * 0.5f);
    let _e193: vec4<f32> = gl_Position;
    let _e195: vec4<f32> = gl_Position;
    let _e197: vec4<f32> = global.prime_viewport;
    let _e200: vec4<f32> = global.prime_viewport;
    let _e202: vec4<f32> = gl_Position;
    let _e205: vec2<f32> = ((_e195.xy * _e197.xy) + (_e200.zw * _e202.w));
    gl_Position.x = _e205.x;
    gl_Position.y = _e205.y;
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
    let _e216: vec4<f32> = gl_Position;
    let _e218: vec2<f32> = texcoord;
    let _e220: vec4<f32> = vertex_color;
    let _e222: vec3<f32> = surface_normal;
    let _e224: vec3<f32> = surface_position;
    return VertexOutput(_e216, _e218, _e220, _e222, _e224);
}
