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

struct FragmentOutput {
    @location(0) prime_output: vec4<f32>,
    @location(1) prime_output_normal: vec4<f32>,
    @location(2) prime_output_material: vec4<f32>,
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
var<private> prime_mrt_mode: i32;
var<private> texcoord_1: vec2<f32>;
var<private> vertex_color_1: vec4<f32>;
var<private> surface_normal_1: vec3<f32>;
var<private> surface_position_1: vec3<f32>;

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

fn cosmetic_noise(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e185: vec2<f32> = p_1;
    let _e194: vec2<f32> = p_1;
    let _e206: vec2<f32> = p_1;
    let _e215: vec2<f32> = p_1;
    return fract((sin(dot(_e215, vec2<f32>(127.1f, 311.7f))) * 43758.547f));
}

fn cosmetic_soft_noise(p_2: vec2<f32>) -> f32 {
    var p_3: vec2<f32>;
    var cell: vec2<f32>;
    var f: vec2<f32>;

    p_3 = p_2;
    let _e182: vec2<f32> = p_3;
    cell = floor(_e182);
    let _e186: vec2<f32> = p_3;
    f = fract(_e186);
    let _e189: vec2<f32> = f;
    let _e190: vec2<f32> = f;
    let _e194: vec2<f32> = f;
    f = ((_e189 * _e190) * (vec2(3f) - (2f * _e194)));
    let _e200: vec2<f32> = cell;
    let _e201: f32 = cosmetic_noise(_e200);
    let _e202: vec2<f32> = cell;
    let _e207: vec2<f32> = cell;
    let _e212: f32 = cosmetic_noise((_e207 + vec2<f32>(1f, 0f)));
    let _e213: vec2<f32> = f;
    let _e216: vec2<f32> = cell;
    let _e217: f32 = cosmetic_noise(_e216);
    let _e218: vec2<f32> = cell;
    let _e223: vec2<f32> = cell;
    let _e228: f32 = cosmetic_noise((_e223 + vec2<f32>(1f, 0f)));
    let _e229: vec2<f32> = f;
    let _e232: vec2<f32> = cell;
    let _e237: vec2<f32> = cell;
    let _e242: f32 = cosmetic_noise((_e237 + vec2<f32>(0f, 1f)));
    let _e243: vec2<f32> = cell;
    let _e247: vec2<f32> = cell;
    let _e251: f32 = cosmetic_noise((_e247 + vec2(1f)));
    let _e252: vec2<f32> = f;
    let _e254: vec2<f32> = cell;
    let _e259: vec2<f32> = cell;
    let _e264: f32 = cosmetic_noise((_e259 + vec2<f32>(0f, 1f)));
    let _e265: vec2<f32> = cell;
    let _e269: vec2<f32> = cell;
    let _e273: f32 = cosmetic_noise((_e269 + vec2(1f)));
    let _e274: vec2<f32> = f;
    let _e277: vec2<f32> = f;
    let _e280: vec2<f32> = cell;
    let _e281: f32 = cosmetic_noise(_e280);
    let _e282: vec2<f32> = cell;
    let _e287: vec2<f32> = cell;
    let _e292: f32 = cosmetic_noise((_e287 + vec2<f32>(1f, 0f)));
    let _e293: vec2<f32> = f;
    let _e296: vec2<f32> = cell;
    let _e297: f32 = cosmetic_noise(_e296);
    let _e298: vec2<f32> = cell;
    let _e303: vec2<f32> = cell;
    let _e308: f32 = cosmetic_noise((_e303 + vec2<f32>(1f, 0f)));
    let _e309: vec2<f32> = f;
    let _e312: vec2<f32> = cell;
    let _e317: vec2<f32> = cell;
    let _e322: f32 = cosmetic_noise((_e317 + vec2<f32>(0f, 1f)));
    let _e323: vec2<f32> = cell;
    let _e327: vec2<f32> = cell;
    let _e331: f32 = cosmetic_noise((_e327 + vec2(1f)));
    let _e332: vec2<f32> = f;
    let _e334: vec2<f32> = cell;
    let _e339: vec2<f32> = cell;
    let _e344: f32 = cosmetic_noise((_e339 + vec2<f32>(0f, 1f)));
    let _e345: vec2<f32> = cell;
    let _e349: vec2<f32> = cell;
    let _e353: f32 = cosmetic_noise((_e349 + vec2(1f)));
    let _e354: vec2<f32> = f;
    let _e357: vec2<f32> = f;
    return mix(mix(_e297, _e308, _e309.x), mix(_e344, _e353, _e354.x), _e357.y);
}

fn cosmetic_finish() -> vec2<f32> {
    let _e179: i32 = global.cosmetic_skin;
    if (_e179 == 1i) {
        return vec2<f32>(0.78f, 0.3f);
    }
    let _e185: i32 = global.cosmetic_skin;
    if (_e185 == 2i) {
        return vec2<f32>(0.85f, 0.38f);
    }
    let _e191: i32 = global.cosmetic_skin;
    if (_e191 == 3i) {
        return vec2<f32>(0.05f, 0.24f);
    }
    let _e197: i32 = global.cosmetic_skin;
    if (_e197 == 4i) {
        return vec2<f32>(0.55f, 0.42f);
    }
    let _e203: i32 = global.cosmetic_skin;
    if (_e203 == 5i) {
        return vec2<f32>(0.12f, 0.68f);
    }
    return vec2<f32>(0.35f, 0.28f);
}

fn cosmetic_circuit() -> f32 {
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var travel: f32;

    let _e179: vec2<f32> = texcoord_1;
    let _e182: vec2<f32> = texcoord_1;
    grid = fract((_e182 * 18f));
    let _e190: vec2<f32> = grid;
    let _e192: vec2<f32> = grid;
    let _e194: vec2<f32> = grid;
    let _e196: vec2<f32> = grid;
    let _e201: vec2<f32> = grid;
    let _e203: vec2<f32> = grid;
    let _e205: vec2<f32> = grid;
    let _e207: vec2<f32> = grid;
    trace = (1f - smoothstep(0.035f, 0.09f, min(_e205.x, _e207.y)));
    let _e216: vec2<f32> = grid;
    let _e220: vec2<f32> = grid;
    let _e227: vec2<f32> = grid;
    let _e231: vec2<f32> = grid;
    node = (1f - smoothstep(0.08f, 0.16f, length((_e231 - vec2(0.15f)))));
    let _e241: vec2<f32> = texcoord_1;
    let _e243: vec2<f32> = texcoord_1;
    let _e248: f32 = global.cosmetic_time;
    let _e252: vec2<f32> = texcoord_1;
    let _e254: vec2<f32> = texcoord_1;
    let _e259: f32 = global.cosmetic_time;
    let _e269: vec2<f32> = texcoord_1;
    let _e271: vec2<f32> = texcoord_1;
    let _e276: f32 = global.cosmetic_time;
    let _e280: vec2<f32> = texcoord_1;
    let _e282: vec2<f32> = texcoord_1;
    let _e287: f32 = global.cosmetic_time;
    travel = pow((0.5f + (0.5f * sin((((_e280.x + _e282.y) * 32f) - (_e287 * 1.8f))))), 8f);
    let _e297: f32 = trace;
    let _e299: f32 = travel;
    let _e304: f32 = node;
    let _e307: f32 = trace;
    let _e309: f32 = travel;
    let _e314: f32 = node;
    return max((_e307 * (0.3f + (_e309 * 0.7f))), (_e314 * 0.75f));
}

fn apply_cosmetics(col: ptr<function, vec4<f32>>) {
    var nativeColor: vec3<f32>;
    var lum: f32;
    var n: vec3<f32>;
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
    var n_1: f32;

    let _e180: i32 = global.cosmetic_skin;
    let _e183: i32 = global.cosmetic_effect;
    let _e187: f32 = global.cosmetic_dissolve;
    if (((_e180 == 0i) && (_e183 == 0i)) && (_e187 <= 0f)) {
        return;
    }
    let _e191: vec4<f32> = (*col);
    nativeColor = _e191.xyz;
    let _e194: vec4<f32> = (*col);
    let _e200: vec4<f32> = (*col);
    lum = dot(_e200.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e208: vec3<f32> = surface_normal_1;
    let _e211: vec3<f32> = surface_normal_1;
    let _e212: vec3<f32> = surface_normal_1;
    let _e217: vec3<f32> = surface_normal_1;
    let _e218: vec3<f32> = surface_normal_1;
    let _e224: vec3<f32> = surface_normal_1;
    let _e225: vec3<f32> = surface_normal_1;
    let _e230: vec3<f32> = surface_normal_1;
    let _e231: vec3<f32> = surface_normal_1;
    n = (_e208 * inverseSqrt(max(dot(_e230, _e231), 0.0001f)));
    let _e238: mat4x4<f32> = global.view_mtx;
    let _e248: vec3<f32> = n;
    viewNormal = (mat3x3<f32>(_e238[0].xyz, _e238[1].xyz, _e238[2].xyz) * _e248);
    let _e251: mat4x4<f32> = global.view_mtx;
    let _e252: vec3<f32> = surface_position_1;
    toEye = -((_e251 * vec4<f32>(_e252.x, _e252.y, _e252.z, 1f)).xyz);
    let _e262: vec3<f32> = toEye;
    let _e265: vec3<f32> = toEye;
    let _e266: vec3<f32> = toEye;
    let _e271: vec3<f32> = toEye;
    let _e272: vec3<f32> = toEye;
    let _e278: vec3<f32> = toEye;
    let _e279: vec3<f32> = toEye;
    let _e284: vec3<f32> = toEye;
    let _e285: vec3<f32> = toEye;
    toEye = (_e262 * inverseSqrt(max(dot(_e284, _e285), 0.0001f)));
    let _e294: vec3<f32> = viewNormal;
    let _e295: vec3<f32> = toEye;
    let _e299: vec3<f32> = viewNormal;
    let _e300: vec3<f32> = toEye;
    let _e309: vec3<f32> = viewNormal;
    let _e310: vec3<f32> = toEye;
    let _e314: vec3<f32> = viewNormal;
    let _e315: vec3<f32> = toEye;
    let _e326: vec3<f32> = viewNormal;
    let _e327: vec3<f32> = toEye;
    let _e331: vec3<f32> = viewNormal;
    let _e332: vec3<f32> = toEye;
    let _e341: vec3<f32> = viewNormal;
    let _e342: vec3<f32> = toEye;
    let _e346: vec3<f32> = viewNormal;
    let _e347: vec3<f32> = toEye;
    rim = pow(clamp((1f - abs(dot(_e346, _e347))), 0f, 1f), 2.2f);
    let _e357: i32 = global.cosmetic_skin;
    if (_e357 == 1i) {
        {
            let _e360: vec4<f32> = (*col);
            let _e362: vec4<f32> = (*col);
            let _e369: f32 = lum;
            let _e373: vec4<f32> = (*col);
            let _e380: f32 = lum;
            let _e385: vec3<f32> = mix(_e373.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e380)), vec3(0.88f));
            (*col).x = _e385.x;
            (*col).y = _e385.y;
            (*col).z = _e385.z;
        }
    } else {
        let _e392: i32 = global.cosmetic_skin;
        if (_e392 == 2i) {
            {
                let _e397: vec2<f32> = texcoord_1;
                let _e401: vec2<f32> = texcoord_1;
                let _e406: vec2<f32> = texcoord_1;
                let _e410: vec2<f32> = texcoord_1;
                let _e418: vec2<f32> = texcoord_1;
                let _e422: vec2<f32> = texcoord_1;
                let _e427: vec2<f32> = texcoord_1;
                let _e431: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e422.x * 85f)) * sin((_e431.y * 85f))));
                let _e439: vec4<f32> = (*col);
                let _e441: vec4<f32> = (*col);
                let _e448: f32 = lum;
                let _e452: vec4<f32> = (*col);
                let _e459: f32 = lum;
                let _e464: vec3<f32> = mix(_e452.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e459)), vec3(0.82f));
                (*col).x = _e464.x;
                (*col).y = _e464.y;
                (*col).z = _e464.z;
                let _e471: vec4<f32> = (*col);
                let _e473: vec4<f32> = (*col);
                let _e475: f32 = etch;
                let _e481: vec3<f32> = (_e473.xyz + (_e475 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e481.x;
                (*col).y = _e481.y;
                (*col).z = _e481.z;
            }
        }
    }
    let _e488: i32 = global.cosmetic_skin;
    if (_e488 == 3i) {
        {
            let _e491: vec2<f32> = texcoord_1;
            let _e494: vec2<f32> = texcoord_1;
            panel = fract((_e494 * 12f));
            let _e501: vec2<f32> = panel;
            let _e505: vec2<f32> = panel;
            let _e510: vec2<f32> = panel;
            let _e514: vec2<f32> = panel;
            seam = (smoothstep(0.025f, 0.075f, _e505.x) * smoothstep(0.025f, 0.075f, _e514.y));
            let _e519: vec4<f32> = (*col);
            let _e538: f32 = seam;
            let _e542: f32 = lum;
            let _e546: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e538)) * (0.5f + (_e542 * 0.6f)));
            (*col).x = _e546.x;
            (*col).y = _e546.y;
            (*col).z = _e546.z;
        }
    } else {
        let _e553: i32 = global.cosmetic_skin;
        if (_e553 == 4i) {
            {
                let _e556: vec4<f32> = (*col);
                let _e563: f32 = lum;
                let _e570: f32 = cosmetic_circuit();
                let _e572: vec3<f32> = ((vec3<f32>(0.13f, 0.18f, 0.22f) * (0.6f + _e563)) + (vec3<f32>(0.08f, 0.8f, 0.65f) * _e570));
                (*col).x = _e572.x;
                (*col).y = _e572.y;
                (*col).z = _e572.z;
            }
        } else {
            let _e579: i32 = global.cosmetic_skin;
            if (_e579 == 5i) {
                {
                    let _e584: vec2<f32> = texcoord_1;
                    let _e588: vec2<f32> = texcoord_1;
                    let _e593: vec2<f32> = texcoord_1;
                    let _e597: vec2<f32> = texcoord_1;
                    let _e605: vec2<f32> = texcoord_1;
                    let _e609: vec2<f32> = texcoord_1;
                    let _e614: vec2<f32> = texcoord_1;
                    let _e618: vec2<f32> = texcoord_1;
                    let _e629: vec2<f32> = texcoord_1;
                    let _e633: vec2<f32> = texcoord_1;
                    let _e638: vec2<f32> = texcoord_1;
                    let _e642: vec2<f32> = texcoord_1;
                    let _e650: vec2<f32> = texcoord_1;
                    let _e654: vec2<f32> = texcoord_1;
                    let _e659: vec2<f32> = texcoord_1;
                    let _e663: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e650.x * 65f) + (_e654.y * 38f)) + (sin((_e663.y * 25f)) * 2f))));
                    let _e674: vec4<f32> = (*col);
                    let _e693: f32 = stripe;
                    let _e697: f32 = lum;
                    let _e699: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e693)) * (0.55f + _e697));
                    (*col).x = _e699.x;
                    (*col).y = _e699.y;
                    (*col).z = _e699.z;
                }
            } else {
                let _e706: i32 = global.cosmetic_skin;
                if (_e706 == 6i) {
                    {
                        let _e709: vec2<f32> = texcoord_1;
                        let _e712: f32 = global.cosmetic_time;
                        let _e718: vec2<f32> = texcoord_1;
                        let _e721: f32 = global.cosmetic_time;
                        let _e727: f32 = cosmetic_soft_noise(((_e718 * 9f) + vec2<f32>((_e721 * 0.025f), 0f)));
                        cloud = _e727;
                        let _e730: vec2<f32> = texcoord_1;
                        let _e733: vec2<f32> = texcoord_1;
                        let _e737: vec2<f32> = texcoord_1;
                        let _e740: vec2<f32> = texcoord_1;
                        let _e744: f32 = cosmetic_noise(floor((_e740 * 100f)));
                        let _e746: vec2<f32> = texcoord_1;
                        let _e749: vec2<f32> = texcoord_1;
                        let _e753: vec2<f32> = texcoord_1;
                        let _e756: vec2<f32> = texcoord_1;
                        let _e760: f32 = cosmetic_noise(floor((_e756 * 100f)));
                        star = step(0.985f, _e760);
                        let _e763: vec4<f32> = (*col);
                        let _e782: f32 = cloud;
                        let _e786: f32 = lum;
                        let _e789: f32 = star;
                        let _e793: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e782)) * (0.5f + _e786)) + vec3((_e789 * 0.65f)));
                        (*col).x = _e793.x;
                        (*col).y = _e793.y;
                        (*col).z = _e793.z;
                    }
                }
            }
        }
    }
    let _e800: i32 = global.cosmetic_skin;
    if (_e800 != 0i) {
        {
            let _e803: vec2<f32> = cosmetic_finish();
            finish = _e803;
            let _e817: vec3<f32> = viewNormal;
            let _e844: vec3<f32> = viewNormal;
            let _e862: vec2<f32> = finish;
            let _e866: vec2<f32> = finish;
            let _e881: vec3<f32> = viewNormal;
            let _e908: vec3<f32> = viewNormal;
            let _e926: vec2<f32> = finish;
            let _e930: vec2<f32> = finish;
            sheen = pow(clamp(dot(_e908, normalize(vec3<f32>(-0.35f, 0.6f, 0.72f))), 0f, 1f), mix(44f, 8f, _e930.y));
            let _e935: vec4<f32> = (*col);
            let _e937: vec4<f32> = (*col);
            let _e943: f32 = rim;
            let _e946: vec2<f32> = finish;
            let _e952: vec4<f32> = (*col);
            let _e956: vec2<f32> = finish;
            let _e960: vec4<f32> = (*col);
            let _e964: vec2<f32> = finish;
            let _e968: f32 = sheen;
            let _e971: vec3<f32> = (_e937.xyz + (((vec3<f32>(0.12f, 0.16f, 0.2f) * _e943) * (1f - _e946.y)) + (mix(vec3(0.12f), (_e960.xyz * 0.28f), vec3(_e964.x)) * _e968)));
            (*col).x = _e971.x;
            (*col).y = _e971.y;
            (*col).z = _e971.z;
        }
    }
    let _e978: i32 = global.cosmetic_preserve_palette;
    let _e981: i32 = global.cosmetic_skin;
    if ((_e978 != 0i) && (_e981 != 0i)) {
        {
            let _e985: vec3<f32> = nativeColor;
            let _e987: vec3<f32> = nativeColor;
            let _e989: vec3<f32> = nativeColor;
            let _e991: vec3<f32> = nativeColor;
            let _e993: vec3<f32> = nativeColor;
            let _e996: vec3<f32> = nativeColor;
            let _e998: vec3<f32> = nativeColor;
            let _e1000: vec3<f32> = nativeColor;
            let _e1002: vec3<f32> = nativeColor;
            let _e1004: vec3<f32> = nativeColor;
            let _e1008: vec3<f32> = nativeColor;
            let _e1010: vec3<f32> = nativeColor;
            let _e1012: vec3<f32> = nativeColor;
            let _e1014: vec3<f32> = nativeColor;
            let _e1016: vec3<f32> = nativeColor;
            let _e1019: vec3<f32> = nativeColor;
            let _e1021: vec3<f32> = nativeColor;
            let _e1023: vec3<f32> = nativeColor;
            let _e1025: vec3<f32> = nativeColor;
            let _e1027: vec3<f32> = nativeColor;
            chroma = (max(_e996.x, max(_e1002.y, _e1004.z)) - min(_e1019.x, min(_e1025.y, _e1027.z)));
            let _e1033: vec4<f32> = (*col);
            let _e1035: vec4<f32> = (*col);
            let _e1043: f32 = chroma;
            let _e1047: vec4<f32> = (*col);
            let _e1049: vec3<f32> = nativeColor;
            let _e1055: f32 = chroma;
            let _e1060: vec3<f32> = mix(_e1047.xyz, _e1049, vec3((smoothstep(0.1f, 0.35f, _e1055) * 0.9f)));
            (*col).x = _e1060.x;
            (*col).y = _e1060.y;
            (*col).z = _e1060.z;
        }
    }
    let _e1067: i32 = global.cosmetic_effect;
    if (_e1067 != 0i) {
        {
            let _e1070: f32 = global.cosmetic_time;
            let _e1071: f32 = global.cosmetic_scroll;
            t = (_e1070 * _e1071);
            let _e1076: f32 = global.cosmetic_time;
            let _e1077: f32 = global.cosmetic_pulse;
            let _e1079: f32 = global.cosmetic_time;
            let _e1080: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1079 * _e1080))));
            let _e1088: vec2<f32> = texcoord_1;
            let _e1092: f32 = t;
            let _e1096: vec2<f32> = texcoord_1;
            let _e1100: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1096.y * 30f) - (_e1100 * 2f)))));
            let _e1109: f32 = rim;
            mask = (0.28f + (_e1109 * 0.72f));
            let _e1114: i32 = global.cosmetic_effect;
            if (_e1114 == 1i) {
                let _e1119: f32 = pulse;
                mask = (0.25f + (0.55f * _e1119));
            }
            let _e1122: i32 = global.cosmetic_effect;
            if (_e1122 == 3i) {
                let _e1130: f32 = wave;
                let _e1134: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1130) * 0.5f) + _e1134);
            }
            let _e1136: i32 = global.cosmetic_effect;
            if (_e1136 == 4i) {
                let _e1139: vec2<f32> = texcoord_1;
                let _e1142: f32 = t;
                let _e1145: f32 = t;
                let _e1151: vec2<f32> = texcoord_1;
                let _e1154: f32 = t;
                let _e1157: f32 = t;
                let _e1163: f32 = cosmetic_soft_noise(((_e1151 * 16f) + vec2<f32>((_e1154 * 0.3f), (-(_e1157) * 0.5f))));
                let _e1166: f32 = rim;
                mask = ((_e1163 * 0.45f) + (_e1166 * 0.6f));
            }
            let _e1170: i32 = global.cosmetic_effect;
            let _e1173: i32 = global.cosmetic_effect;
            if ((_e1170 == 5i) || (_e1173 == 7i)) {
                let _e1183: f32 = wave;
                let _e1188: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1183) * 0.65f)) + (_e1188 * 0.45f));
            }
            let _e1192: i32 = global.cosmetic_effect;
            if (_e1192 == 6i) {
                let _e1196: vec2<f32> = texcoord_1;
                let _e1200: vec2<f32> = texcoord_1;
                let _e1204: f32 = t;
                let _e1206: vec2<f32> = texcoord_1;
                let _e1210: f32 = t;
                let _e1214: vec2<f32> = texcoord_1;
                let _e1218: vec2<f32> = texcoord_1;
                let _e1222: f32 = t;
                let _e1224: vec2<f32> = texcoord_1;
                let _e1228: f32 = t;
                let _e1233: vec2<f32> = texcoord_1;
                let _e1237: vec2<f32> = texcoord_1;
                let _e1241: f32 = t;
                let _e1243: vec2<f32> = texcoord_1;
                let _e1247: f32 = t;
                let _e1251: vec2<f32> = texcoord_1;
                let _e1255: vec2<f32> = texcoord_1;
                let _e1259: f32 = t;
                let _e1261: vec2<f32> = texcoord_1;
                let _e1265: f32 = t;
                let _e1272: vec2<f32> = texcoord_1;
                let _e1276: vec2<f32> = texcoord_1;
                let _e1280: f32 = t;
                let _e1282: vec2<f32> = texcoord_1;
                let _e1286: f32 = t;
                let _e1290: vec2<f32> = texcoord_1;
                let _e1294: vec2<f32> = texcoord_1;
                let _e1298: f32 = t;
                let _e1300: vec2<f32> = texcoord_1;
                let _e1304: f32 = t;
                let _e1309: vec2<f32> = texcoord_1;
                let _e1313: vec2<f32> = texcoord_1;
                let _e1317: f32 = t;
                let _e1319: vec2<f32> = texcoord_1;
                let _e1323: f32 = t;
                let _e1327: vec2<f32> = texcoord_1;
                let _e1331: vec2<f32> = texcoord_1;
                let _e1335: f32 = t;
                let _e1337: vec2<f32> = texcoord_1;
                let _e1341: f32 = t;
                let _e1350: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1327.x * 31f) + sin(((_e1337.y * 29f) + _e1341))))), 16f)) + (_e1350 * 0.5f));
            }
            let _e1354: i32 = global.cosmetic_effect;
            if (_e1354 == 8i) {
                let _e1357: f32 = wave;
                let _e1360: f32 = rim;
                mask = ((_e1357 * 0.35f) + (_e1360 * 0.7f));
            }
            let _e1364: i32 = global.cosmetic_effect;
            if (_e1364 == 9i) {
                let _e1369: vec2<f32> = texcoord_1;
                let _e1373: f32 = t;
                let _e1375: vec2<f32> = texcoord_1;
                let _e1379: f32 = t;
                let _e1384: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1375.x * 40f) + _e1379)))) * _e1384);
            }
            let _e1389: vec3<f32> = global.cosmetic_primary;
            let _e1390: vec3<f32> = global.cosmetic_secondary;
            let _e1391: f32 = wave;
            energy = mix(_e1389, _e1390, vec3(_e1391));
            let _e1398: f32 = mask;
            let _e1402: f32 = global.cosmetic_intensity;
            let _e1404: f32 = pulse;
            strength = ((clamp(_e1398, 0f, 1f) * _e1402) * _e1404);
            let _e1407: i32 = global.cosmetic_preserve_palette;
            if (_e1407 != 0i) {
                let _e1410: f32 = strength;
                strength = (_e1410 * 0.65f);
            }
            let _e1413: vec4<f32> = (*col);
            let _e1415: vec4<f32> = (*col);
            let _e1417: vec3<f32> = energy;
            let _e1419: f32 = lum;
            let _e1424: f32 = strength;
            let _e1429: f32 = strength;
            let _e1435: vec4<f32> = (*col);
            let _e1437: vec3<f32> = energy;
            let _e1439: f32 = lum;
            let _e1444: f32 = strength;
            let _e1449: f32 = strength;
            let _e1456: vec3<f32> = mix(_e1435.xyz, (_e1437 * (0.4f + (_e1439 * 0.6f))), vec3(clamp((_e1449 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1456.x;
            (*col).y = _e1456.y;
            (*col).z = _e1456.z;
            let _e1463: vec4<f32> = (*col);
            let _e1465: vec4<f32> = (*col);
            let _e1467: vec3<f32> = energy;
            let _e1468: f32 = strength;
            let _e1472: vec3<f32> = (_e1465.xyz + ((_e1467 * _e1468) * 0.45f));
            (*col).x = _e1472.x;
            (*col).y = _e1472.y;
            (*col).z = _e1472.z;
        }
    }
    let _e1479: f32 = global.cosmetic_dissolve;
    if (_e1479 > 0f) {
        {
            let _e1482: vec2<f32> = texcoord_1;
            let _e1485: vec2<f32> = texcoord_1;
            let _e1489: vec2<f32> = texcoord_1;
            let _e1492: vec2<f32> = texcoord_1;
            let _e1496: f32 = cosmetic_noise(floor((_e1492 * 64f)));
            n_1 = _e1496;
            let _e1498: f32 = n_1;
            let _e1499: f32 = global.cosmetic_dissolve;
            if (_e1498 < _e1499) {
                discard;
            }
            let _e1501: vec4<f32> = (*col);
            let _e1503: vec4<f32> = (*col);
            let _e1505: vec3<f32> = global.cosmetic_primary;
            let _e1508: f32 = global.cosmetic_dissolve;
            let _e1512: f32 = global.cosmetic_dissolve;
            let _e1513: f32 = global.cosmetic_dissolve;
            let _e1516: f32 = n_1;
            let _e1522: vec3<f32> = (_e1503.xyz + ((_e1505 * (1f - smoothstep(_e1512, (_e1513 + 0.08f), _e1516))) * 0.3f));
            (*col).x = _e1522.x;
            (*col).y = _e1522.y;
            (*col).z = _e1522.z;
            return;
        }
    } else {
        return;
    }
}

fn mapped_normal() -> vec3<f32> {
    var n_2: vec3<f32>;
    var dp1_: vec3<f32>;
    var dp2_: vec3<f32>;
    var duv1_: vec2<f32>;
    var duv2_: vec2<f32>;
    var det: f32;
    var tangent: vec3<f32>;
    var bitangent: vec3<f32>;
    var nm: vec3<f32>;

    let _e180: vec3<f32> = surface_normal_1;
    n_2 = normalize(_e180);
    let _e183: i32 = global._prime_use_normal_map;
    if !((_e183 != 0i)) {
        let _e187: vec3<f32> = n_2;
        return _e187;
    }
    let _e189: vec3<f32> = surface_position_1;
    let _e190: vec3<f32> = dpdx(_e189);
    dp1_ = _e190;
    let _e193: vec3<f32> = surface_position_1;
    let _e194: vec3<f32> = dpdy(_e193);
    dp2_ = _e194;
    let _e197: vec2<f32> = texcoord_1;
    let _e198: vec2<f32> = dpdx(_e197);
    duv1_ = _e198;
    let _e201: vec2<f32> = texcoord_1;
    let _e202: vec2<f32> = dpdy(_e201);
    duv2_ = _e202;
    let _e204: vec2<f32> = duv1_;
    let _e206: vec2<f32> = duv2_;
    let _e209: vec2<f32> = duv1_;
    let _e211: vec2<f32> = duv2_;
    det = ((_e204.x * _e206.y) - (_e209.y * _e211.x));
    let _e217: f32 = det;
    if (abs(_e217) < 0.000001f) {
        let _e221: vec3<f32> = n_2;
        return _e221;
    }
    let _e222: vec3<f32> = dp1_;
    let _e223: vec2<f32> = duv2_;
    let _e226: vec3<f32> = dp2_;
    let _e227: vec2<f32> = duv1_;
    let _e231: f32 = det;
    let _e234: vec3<f32> = dp1_;
    let _e235: vec2<f32> = duv2_;
    let _e238: vec3<f32> = dp2_;
    let _e239: vec2<f32> = duv1_;
    let _e243: f32 = det;
    tangent = normalize((((_e234 * _e235.y) - (_e238 * _e239.y)) / vec3(_e243)));
    let _e248: vec3<f32> = dp1_;
    let _e250: vec2<f32> = duv2_;
    let _e253: vec3<f32> = dp2_;
    let _e254: vec2<f32> = duv1_;
    let _e258: f32 = det;
    let _e261: vec3<f32> = dp1_;
    let _e263: vec2<f32> = duv2_;
    let _e266: vec3<f32> = dp2_;
    let _e267: vec2<f32> = duv1_;
    let _e271: f32 = det;
    bitangent = normalize((((-(_e261) * _e263.x) + (_e266 * _e267.x)) / vec3(_e271)));
    let _e277: vec2<f32> = texcoord_1;
    let _e278: vec4<f32> = prime_sample_normal_tex(_e277);
    nm = ((_e278.xyz * 2f) - vec3(1f));
    let _e286: vec3<f32> = tangent;
    let _e287: vec3<f32> = nm;
    let _e290: vec3<f32> = bitangent;
    let _e291: vec3<f32> = nm;
    let _e295: vec3<f32> = n_2;
    let _e296: vec3<f32> = nm;
    let _e300: vec3<f32> = tangent;
    let _e301: vec3<f32> = nm;
    let _e304: vec3<f32> = bitangent;
    let _e305: vec3<f32> = nm;
    let _e309: vec3<f32> = n_2;
    let _e310: vec3<f32> = nm;
    return normalize((((_e300 * _e301.x) + (_e304 * _e305.y)) + (_e309 * _e310.z)));
}

fn main_1() {
    var local: vec4<f32>;
    var base: vec4<f32>;
    var albedo: vec3<f32>;
    var cosmetic: vec4<f32>;
    var local_1: vec4<f32>;
    var sm: vec4<f32>;
    var roughness: f32;
    var metallic: f32;
    var emissive: f32;
    var e: vec3<f32>;
    var finish_1: vec2<f32>;

    let _e179: i32 = global._prime_use_texture;
    if (_e179 != 0i) {
        let _e183: vec2<f32> = texcoord_1;
        let _e184: vec4<f32> = prime_sample_tex(_e183);
        local = _e184;
    } else {
        local = vec4(1f);
    }
    let _e188: vec4<f32> = local;
    base = _e188;
    let _e190: vec4<f32> = base;
    if (_e190.w < 0.99f) {
        discard;
    }
    let _e194: vec4<f32> = base;
    let _e196: vec4<f32> = vertex_color_1;
    albedo = (_e194.xyz * _e196.xyz);
    let _e200: i32 = global._prime_use_pal_override;
    if (_e200 != 0i) {
        let _e203: vec4<f32> = global.pal_override_color;
        let _e205: vec4<f32> = vertex_color_1;
        albedo = (_e203.xyz * _e205.xyz);
    }
    let _e208: i32 = global._prime_use_override;
    if (_e208 != 0i) {
        let _e211: vec4<f32> = global.override_color;
        albedo = _e211.xyz;
    }
    let _e213: vec3<f32> = albedo;
    cosmetic = vec4<f32>(_e213.x, _e213.y, _e213.z, 1f);
    apply_cosmetics((&cosmetic));
    let _e222: vec4<f32> = cosmetic;
    albedo = _e222.xyz;
    let _e224: i32 = prime_mrt_mode;
    if (_e224 == 1i) {
        {
            let _e230: vec3<f32> = albedo;
            let _e235: vec3<f32> = clamp(_e230, vec3(0f), vec3(1f));
            prime_output = vec4<f32>(_e235.x, _e235.y, _e235.z, 1f);
            return;
        }
    }
    let _e241: i32 = prime_mrt_mode;
    if (_e241 == 2i) {
        {
            let _e244: vec3<f32> = mapped_normal();
            let _e249: vec3<f32> = ((_e244 * 0.5f) + vec3(0.5f));
            prime_output = vec4<f32>(_e249.x, _e249.y, _e249.z, 1f);
            return;
        }
    }
    let _e255: i32 = global._prime_use_specular_map;
    if (_e255 != 0i) {
        let _e259: vec2<f32> = texcoord_1;
        let _e260: vec4<f32> = prime_sample_specular_tex(_e259);
        local_1 = _e260;
    } else {
        let _e261: vec3<f32> = global.material_specular;
        let _e263: vec3<f32> = global.material_specular;
        let _e265: vec3<f32> = global.material_specular;
        let _e267: vec3<f32> = global.material_specular;
        let _e270: vec3<f32> = global.material_specular;
        let _e272: vec3<f32> = global.material_specular;
        let _e274: vec3<f32> = global.material_specular;
        let _e276: vec3<f32> = global.material_specular;
        let _e278: vec3<f32> = global.material_specular;
        let _e281: vec3<f32> = global.material_specular;
        local_1 = vec4<f32>(max(max(_e276.x, _e278.y), _e281.z), 0.62f, 0f, 1f);
    }
    let _e289: vec4<f32> = local_1;
    sm = _e289;
    let _e291: vec4<f32> = sm;
    let _e295: vec4<f32> = sm;
    roughness = clamp(_e295.y, 0.04f, 1f);
    let _e303: vec4<f32> = sm;
    let _e307: vec4<f32> = sm;
    let _e314: vec4<f32> = sm;
    let _e318: vec4<f32> = sm;
    metallic = (smoothstep(0.45f, 0.95f, clamp(_e318.x, 0f, 1f)) * 0.75f);
    let _e327: vec3<f32> = global.material_emission;
    let _e329: vec3<f32> = global.material_emission;
    let _e331: vec3<f32> = global.material_emission;
    let _e333: vec3<f32> = global.material_emission;
    let _e336: vec3<f32> = global.material_emission;
    let _e338: vec3<f32> = global.material_emission;
    let _e340: vec3<f32> = global.material_emission;
    let _e342: vec3<f32> = global.material_emission;
    let _e344: vec3<f32> = global.material_emission;
    let _e347: vec3<f32> = global.material_emission;
    emissive = max(max(_e342.x, _e344.y), _e347.z);
    let _e351: i32 = global._prime_use_emissive_map;
    if (_e351 != 0i) {
        {
            let _e355: vec2<f32> = texcoord_1;
            let _e356: vec4<f32> = prime_sample_emissive_tex(_e355);
            e = _e356.xyz;
            let _e365: vec3<f32> = e;
            let _e371: f32 = emissive;
            let _e377: vec3<f32> = e;
            emissive = max(_e371, dot(_e377, vec3<f32>(0.2126f, 0.7152f, 0.0722f)));
        }
    }
    let _e384: i32 = global.cosmetic_skin;
    let _e387: i32 = global._prime_use_specular_map;
    if ((_e384 != 0i) && !((_e387 != 0i))) {
        {
            let _e392: vec2<f32> = cosmetic_finish();
            finish_1 = _e392;
            let _e394: vec2<f32> = finish_1;
            metallic = _e394.x;
            let _e396: vec2<f32> = finish_1;
            roughness = _e396.y;
        }
    }
    let _e398: i32 = global.cosmetic_skin;
    if (_e398 == 4i) {
        let _e402: f32 = cosmetic_circuit();
        let _e405: f32 = emissive;
        let _e406: f32 = cosmetic_circuit();
        emissive = max(_e405, (_e406 * 0.55f));
    }
    let _e410: f32 = metallic;
    let _e411: f32 = roughness;
    let _e415: f32 = emissive;
    prime_output = vec4<f32>(_e410, _e411, clamp(_e415, 0f, 1f), 1f);
    return;
}

@fragment
fn main(@location(0) texcoord: vec2<f32>, @location(1) vertex_color: vec4<f32>, @location(2) surface_normal: vec3<f32>, @location(3) surface_position: vec3<f32>) -> FragmentOutput {
    texcoord_1 = texcoord;
    vertex_color_1 = vertex_color;
    surface_normal_1 = surface_normal;
    surface_position_1 = surface_position;
    prime_mrt_mode = 1i;
    main_1();
    let prime_mrt_albedo = prime_output;
    prime_mrt_mode = 2i;
    main_1();
    let prime_mrt_normal = prime_output;
    prime_mrt_mode = 3i;
    main_1();
    let prime_mrt_material = prime_output;
    return FragmentOutput(prime_mrt_albedo, prime_mrt_normal, prime_mrt_material);
}
