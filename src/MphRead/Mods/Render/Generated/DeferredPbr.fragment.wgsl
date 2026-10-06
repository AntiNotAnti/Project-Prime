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
var<private> vertex_color_1: vec4<f32>;
var<private> surface_normal_1: vec3<f32>;
var<private> surface_position_1: vec3<f32>;

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

fn cosmetic_noise(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e201: vec2<f32> = p_1;
    let _e210: vec2<f32> = p_1;
    let _e222: vec2<f32> = p_1;
    let _e231: vec2<f32> = p_1;
    return fract((sin(dot(_e231, vec2<f32>(127.1f, 311.7f))) * 43758.547f));
}

fn cosmetic_soft_noise(p_2: vec2<f32>) -> f32 {
    var p_3: vec2<f32>;
    var cell: vec2<f32>;
    var f: vec2<f32>;

    p_3 = p_2;
    let _e198: vec2<f32> = p_3;
    cell = floor(_e198);
    let _e202: vec2<f32> = p_3;
    f = fract(_e202);
    let _e205: vec2<f32> = f;
    let _e206: vec2<f32> = f;
    let _e210: vec2<f32> = f;
    f = ((_e205 * _e206) * (vec2(3f) - (2f * _e210)));
    let _e216: vec2<f32> = cell;
    let _e217: f32 = cosmetic_noise(_e216);
    let _e218: vec2<f32> = cell;
    let _e223: vec2<f32> = cell;
    let _e228: f32 = cosmetic_noise((_e223 + vec2<f32>(1f, 0f)));
    let _e229: vec2<f32> = f;
    let _e232: vec2<f32> = cell;
    let _e233: f32 = cosmetic_noise(_e232);
    let _e234: vec2<f32> = cell;
    let _e239: vec2<f32> = cell;
    let _e244: f32 = cosmetic_noise((_e239 + vec2<f32>(1f, 0f)));
    let _e245: vec2<f32> = f;
    let _e248: vec2<f32> = cell;
    let _e253: vec2<f32> = cell;
    let _e258: f32 = cosmetic_noise((_e253 + vec2<f32>(0f, 1f)));
    let _e259: vec2<f32> = cell;
    let _e263: vec2<f32> = cell;
    let _e267: f32 = cosmetic_noise((_e263 + vec2(1f)));
    let _e268: vec2<f32> = f;
    let _e270: vec2<f32> = cell;
    let _e275: vec2<f32> = cell;
    let _e280: f32 = cosmetic_noise((_e275 + vec2<f32>(0f, 1f)));
    let _e281: vec2<f32> = cell;
    let _e285: vec2<f32> = cell;
    let _e289: f32 = cosmetic_noise((_e285 + vec2(1f)));
    let _e290: vec2<f32> = f;
    let _e293: vec2<f32> = f;
    let _e296: vec2<f32> = cell;
    let _e297: f32 = cosmetic_noise(_e296);
    let _e298: vec2<f32> = cell;
    let _e303: vec2<f32> = cell;
    let _e308: f32 = cosmetic_noise((_e303 + vec2<f32>(1f, 0f)));
    let _e309: vec2<f32> = f;
    let _e312: vec2<f32> = cell;
    let _e313: f32 = cosmetic_noise(_e312);
    let _e314: vec2<f32> = cell;
    let _e319: vec2<f32> = cell;
    let _e324: f32 = cosmetic_noise((_e319 + vec2<f32>(1f, 0f)));
    let _e325: vec2<f32> = f;
    let _e328: vec2<f32> = cell;
    let _e333: vec2<f32> = cell;
    let _e338: f32 = cosmetic_noise((_e333 + vec2<f32>(0f, 1f)));
    let _e339: vec2<f32> = cell;
    let _e343: vec2<f32> = cell;
    let _e347: f32 = cosmetic_noise((_e343 + vec2(1f)));
    let _e348: vec2<f32> = f;
    let _e350: vec2<f32> = cell;
    let _e355: vec2<f32> = cell;
    let _e360: f32 = cosmetic_noise((_e355 + vec2<f32>(0f, 1f)));
    let _e361: vec2<f32> = cell;
    let _e365: vec2<f32> = cell;
    let _e369: f32 = cosmetic_noise((_e365 + vec2(1f)));
    let _e370: vec2<f32> = f;
    let _e373: vec2<f32> = f;
    return mix(mix(_e313, _e324, _e325.x), mix(_e360, _e369, _e370.x), _e373.y);
}

fn cosmetic_finish() -> vec2<f32> {
    let _e195: i32 = global.cosmetic_skin;
    if (_e195 == 1i) {
        return vec2<f32>(0.78f, 0.3f);
    }
    let _e201: i32 = global.cosmetic_skin;
    if (_e201 == 2i) {
        return vec2<f32>(0.85f, 0.38f);
    }
    let _e207: i32 = global.cosmetic_skin;
    if (_e207 == 3i) {
        return vec2<f32>(0.05f, 0.24f);
    }
    let _e213: i32 = global.cosmetic_skin;
    if (_e213 == 4i) {
        return vec2<f32>(0.55f, 0.42f);
    }
    let _e219: i32 = global.cosmetic_skin;
    if (_e219 == 5i) {
        return vec2<f32>(0.12f, 0.68f);
    }
    return vec2<f32>(0.35f, 0.28f);
}

fn cosmetic_circuit() -> f32 {
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var travel: f32;

    let _e195: vec2<f32> = texcoord_1;
    let _e198: vec2<f32> = texcoord_1;
    grid = fract((_e198 * 18f));
    let _e206: vec2<f32> = grid;
    let _e208: vec2<f32> = grid;
    let _e210: vec2<f32> = grid;
    let _e212: vec2<f32> = grid;
    let _e217: vec2<f32> = grid;
    let _e219: vec2<f32> = grid;
    let _e221: vec2<f32> = grid;
    let _e223: vec2<f32> = grid;
    trace = (1f - smoothstep(0.035f, 0.09f, min(_e221.x, _e223.y)));
    let _e232: vec2<f32> = grid;
    let _e236: vec2<f32> = grid;
    let _e243: vec2<f32> = grid;
    let _e247: vec2<f32> = grid;
    node = (1f - smoothstep(0.08f, 0.16f, length((_e247 - vec2(0.15f)))));
    let _e257: vec2<f32> = texcoord_1;
    let _e259: vec2<f32> = texcoord_1;
    let _e264: f32 = global.cosmetic_time;
    let _e268: vec2<f32> = texcoord_1;
    let _e270: vec2<f32> = texcoord_1;
    let _e275: f32 = global.cosmetic_time;
    let _e285: vec2<f32> = texcoord_1;
    let _e287: vec2<f32> = texcoord_1;
    let _e292: f32 = global.cosmetic_time;
    let _e296: vec2<f32> = texcoord_1;
    let _e298: vec2<f32> = texcoord_1;
    let _e303: f32 = global.cosmetic_time;
    travel = pow((0.5f + (0.5f * sin((((_e296.x + _e298.y) * 32f) - (_e303 * 1.8f))))), 8f);
    let _e313: f32 = trace;
    let _e315: f32 = travel;
    let _e320: f32 = node;
    let _e323: f32 = trace;
    let _e325: f32 = travel;
    let _e330: f32 = node;
    return max((_e323 * (0.3f + (_e325 * 0.7f))), (_e330 * 0.75f));
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

    let _e196: i32 = global.cosmetic_skin;
    let _e199: i32 = global.cosmetic_effect;
    let _e203: f32 = global.cosmetic_dissolve;
    if (((_e196 == 0i) && (_e199 == 0i)) && (_e203 <= 0f)) {
        return;
    }
    let _e207: vec4<f32> = (*col);
    nativeColor = _e207.xyz;
    let _e210: vec4<f32> = (*col);
    let _e216: vec4<f32> = (*col);
    lum = dot(_e216.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e224: vec3<f32> = surface_normal_1;
    let _e227: vec3<f32> = surface_normal_1;
    let _e228: vec3<f32> = surface_normal_1;
    let _e233: vec3<f32> = surface_normal_1;
    let _e234: vec3<f32> = surface_normal_1;
    let _e240: vec3<f32> = surface_normal_1;
    let _e241: vec3<f32> = surface_normal_1;
    let _e246: vec3<f32> = surface_normal_1;
    let _e247: vec3<f32> = surface_normal_1;
    n = (_e224 * inverseSqrt(max(dot(_e246, _e247), 0.0001f)));
    let _e254: mat4x4<f32> = global.view_mtx;
    let _e264: vec3<f32> = n;
    viewNormal = (mat3x3<f32>(_e254[0].xyz, _e254[1].xyz, _e254[2].xyz) * _e264);
    let _e267: mat4x4<f32> = global.view_mtx;
    let _e268: vec3<f32> = surface_position_1;
    toEye = -((_e267 * vec4<f32>(_e268.x, _e268.y, _e268.z, 1f)).xyz);
    let _e278: vec3<f32> = toEye;
    let _e281: vec3<f32> = toEye;
    let _e282: vec3<f32> = toEye;
    let _e287: vec3<f32> = toEye;
    let _e288: vec3<f32> = toEye;
    let _e294: vec3<f32> = toEye;
    let _e295: vec3<f32> = toEye;
    let _e300: vec3<f32> = toEye;
    let _e301: vec3<f32> = toEye;
    toEye = (_e278 * inverseSqrt(max(dot(_e300, _e301), 0.0001f)));
    let _e310: vec3<f32> = viewNormal;
    let _e311: vec3<f32> = toEye;
    let _e315: vec3<f32> = viewNormal;
    let _e316: vec3<f32> = toEye;
    let _e325: vec3<f32> = viewNormal;
    let _e326: vec3<f32> = toEye;
    let _e330: vec3<f32> = viewNormal;
    let _e331: vec3<f32> = toEye;
    let _e342: vec3<f32> = viewNormal;
    let _e343: vec3<f32> = toEye;
    let _e347: vec3<f32> = viewNormal;
    let _e348: vec3<f32> = toEye;
    let _e357: vec3<f32> = viewNormal;
    let _e358: vec3<f32> = toEye;
    let _e362: vec3<f32> = viewNormal;
    let _e363: vec3<f32> = toEye;
    rim = pow(clamp((1f - abs(dot(_e362, _e363))), 0f, 1f), 2.2f);
    let _e373: i32 = global.cosmetic_skin;
    if (_e373 == 1i) {
        {
            let _e376: vec4<f32> = (*col);
            let _e378: vec4<f32> = (*col);
            let _e385: f32 = lum;
            let _e389: vec4<f32> = (*col);
            let _e396: f32 = lum;
            let _e401: vec3<f32> = mix(_e389.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e396)), vec3(0.88f));
            (*col).x = _e401.x;
            (*col).y = _e401.y;
            (*col).z = _e401.z;
        }
    } else {
        let _e408: i32 = global.cosmetic_skin;
        if (_e408 == 2i) {
            {
                let _e413: vec2<f32> = texcoord_1;
                let _e417: vec2<f32> = texcoord_1;
                let _e422: vec2<f32> = texcoord_1;
                let _e426: vec2<f32> = texcoord_1;
                let _e434: vec2<f32> = texcoord_1;
                let _e438: vec2<f32> = texcoord_1;
                let _e443: vec2<f32> = texcoord_1;
                let _e447: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e438.x * 85f)) * sin((_e447.y * 85f))));
                let _e455: vec4<f32> = (*col);
                let _e457: vec4<f32> = (*col);
                let _e464: f32 = lum;
                let _e468: vec4<f32> = (*col);
                let _e475: f32 = lum;
                let _e480: vec3<f32> = mix(_e468.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e475)), vec3(0.82f));
                (*col).x = _e480.x;
                (*col).y = _e480.y;
                (*col).z = _e480.z;
                let _e487: vec4<f32> = (*col);
                let _e489: vec4<f32> = (*col);
                let _e491: f32 = etch;
                let _e497: vec3<f32> = (_e489.xyz + (_e491 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e497.x;
                (*col).y = _e497.y;
                (*col).z = _e497.z;
            }
        }
    }
    let _e504: i32 = global.cosmetic_skin;
    if (_e504 == 3i) {
        {
            let _e507: vec2<f32> = texcoord_1;
            let _e510: vec2<f32> = texcoord_1;
            panel = fract((_e510 * 12f));
            let _e517: vec2<f32> = panel;
            let _e521: vec2<f32> = panel;
            let _e526: vec2<f32> = panel;
            let _e530: vec2<f32> = panel;
            seam = (smoothstep(0.025f, 0.075f, _e521.x) * smoothstep(0.025f, 0.075f, _e530.y));
            let _e535: vec4<f32> = (*col);
            let _e554: f32 = seam;
            let _e558: f32 = lum;
            let _e562: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e554)) * (0.5f + (_e558 * 0.6f)));
            (*col).x = _e562.x;
            (*col).y = _e562.y;
            (*col).z = _e562.z;
        }
    } else {
        let _e569: i32 = global.cosmetic_skin;
        if (_e569 == 4i) {
            {
                let _e572: vec4<f32> = (*col);
                let _e579: f32 = lum;
                let _e586: f32 = cosmetic_circuit();
                let _e588: vec3<f32> = ((vec3<f32>(0.13f, 0.18f, 0.22f) * (0.6f + _e579)) + (vec3<f32>(0.08f, 0.8f, 0.65f) * _e586));
                (*col).x = _e588.x;
                (*col).y = _e588.y;
                (*col).z = _e588.z;
            }
        } else {
            let _e595: i32 = global.cosmetic_skin;
            if (_e595 == 5i) {
                {
                    let _e600: vec2<f32> = texcoord_1;
                    let _e604: vec2<f32> = texcoord_1;
                    let _e609: vec2<f32> = texcoord_1;
                    let _e613: vec2<f32> = texcoord_1;
                    let _e621: vec2<f32> = texcoord_1;
                    let _e625: vec2<f32> = texcoord_1;
                    let _e630: vec2<f32> = texcoord_1;
                    let _e634: vec2<f32> = texcoord_1;
                    let _e645: vec2<f32> = texcoord_1;
                    let _e649: vec2<f32> = texcoord_1;
                    let _e654: vec2<f32> = texcoord_1;
                    let _e658: vec2<f32> = texcoord_1;
                    let _e666: vec2<f32> = texcoord_1;
                    let _e670: vec2<f32> = texcoord_1;
                    let _e675: vec2<f32> = texcoord_1;
                    let _e679: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e666.x * 65f) + (_e670.y * 38f)) + (sin((_e679.y * 25f)) * 2f))));
                    let _e690: vec4<f32> = (*col);
                    let _e709: f32 = stripe;
                    let _e713: f32 = lum;
                    let _e715: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e709)) * (0.55f + _e713));
                    (*col).x = _e715.x;
                    (*col).y = _e715.y;
                    (*col).z = _e715.z;
                }
            } else {
                let _e722: i32 = global.cosmetic_skin;
                if (_e722 == 6i) {
                    {
                        let _e725: vec2<f32> = texcoord_1;
                        let _e728: f32 = global.cosmetic_time;
                        let _e734: vec2<f32> = texcoord_1;
                        let _e737: f32 = global.cosmetic_time;
                        let _e743: f32 = cosmetic_soft_noise(((_e734 * 9f) + vec2<f32>((_e737 * 0.025f), 0f)));
                        cloud = _e743;
                        let _e746: vec2<f32> = texcoord_1;
                        let _e749: vec2<f32> = texcoord_1;
                        let _e753: vec2<f32> = texcoord_1;
                        let _e756: vec2<f32> = texcoord_1;
                        let _e760: f32 = cosmetic_noise(floor((_e756 * 100f)));
                        let _e762: vec2<f32> = texcoord_1;
                        let _e765: vec2<f32> = texcoord_1;
                        let _e769: vec2<f32> = texcoord_1;
                        let _e772: vec2<f32> = texcoord_1;
                        let _e776: f32 = cosmetic_noise(floor((_e772 * 100f)));
                        star = step(0.985f, _e776);
                        let _e779: vec4<f32> = (*col);
                        let _e798: f32 = cloud;
                        let _e802: f32 = lum;
                        let _e805: f32 = star;
                        let _e809: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e798)) * (0.5f + _e802)) + vec3((_e805 * 0.65f)));
                        (*col).x = _e809.x;
                        (*col).y = _e809.y;
                        (*col).z = _e809.z;
                    }
                }
            }
        }
    }
    let _e816: i32 = global.cosmetic_skin;
    if (_e816 != 0i) {
        {
            let _e819: vec2<f32> = cosmetic_finish();
            finish = _e819;
            let _e833: vec3<f32> = viewNormal;
            let _e860: vec3<f32> = viewNormal;
            let _e878: vec2<f32> = finish;
            let _e882: vec2<f32> = finish;
            let _e897: vec3<f32> = viewNormal;
            let _e924: vec3<f32> = viewNormal;
            let _e942: vec2<f32> = finish;
            let _e946: vec2<f32> = finish;
            sheen = pow(clamp(dot(_e924, normalize(vec3<f32>(-0.35f, 0.6f, 0.72f))), 0f, 1f), mix(44f, 8f, _e946.y));
            let _e951: vec4<f32> = (*col);
            let _e953: vec4<f32> = (*col);
            let _e959: f32 = rim;
            let _e962: vec2<f32> = finish;
            let _e968: vec4<f32> = (*col);
            let _e972: vec2<f32> = finish;
            let _e976: vec4<f32> = (*col);
            let _e980: vec2<f32> = finish;
            let _e984: f32 = sheen;
            let _e987: vec3<f32> = (_e953.xyz + (((vec3<f32>(0.12f, 0.16f, 0.2f) * _e959) * (1f - _e962.y)) + (mix(vec3(0.12f), (_e976.xyz * 0.28f), vec3(_e980.x)) * _e984)));
            (*col).x = _e987.x;
            (*col).y = _e987.y;
            (*col).z = _e987.z;
        }
    }
    let _e994: i32 = global.cosmetic_preserve_palette;
    let _e997: i32 = global.cosmetic_skin;
    if ((_e994 != 0i) && (_e997 != 0i)) {
        {
            let _e1001: vec3<f32> = nativeColor;
            let _e1003: vec3<f32> = nativeColor;
            let _e1005: vec3<f32> = nativeColor;
            let _e1007: vec3<f32> = nativeColor;
            let _e1009: vec3<f32> = nativeColor;
            let _e1012: vec3<f32> = nativeColor;
            let _e1014: vec3<f32> = nativeColor;
            let _e1016: vec3<f32> = nativeColor;
            let _e1018: vec3<f32> = nativeColor;
            let _e1020: vec3<f32> = nativeColor;
            let _e1024: vec3<f32> = nativeColor;
            let _e1026: vec3<f32> = nativeColor;
            let _e1028: vec3<f32> = nativeColor;
            let _e1030: vec3<f32> = nativeColor;
            let _e1032: vec3<f32> = nativeColor;
            let _e1035: vec3<f32> = nativeColor;
            let _e1037: vec3<f32> = nativeColor;
            let _e1039: vec3<f32> = nativeColor;
            let _e1041: vec3<f32> = nativeColor;
            let _e1043: vec3<f32> = nativeColor;
            chroma = (max(_e1012.x, max(_e1018.y, _e1020.z)) - min(_e1035.x, min(_e1041.y, _e1043.z)));
            let _e1049: vec4<f32> = (*col);
            let _e1051: vec4<f32> = (*col);
            let _e1059: f32 = chroma;
            let _e1063: vec4<f32> = (*col);
            let _e1065: vec3<f32> = nativeColor;
            let _e1071: f32 = chroma;
            let _e1076: vec3<f32> = mix(_e1063.xyz, _e1065, vec3((smoothstep(0.1f, 0.35f, _e1071) * 0.9f)));
            (*col).x = _e1076.x;
            (*col).y = _e1076.y;
            (*col).z = _e1076.z;
        }
    }
    let _e1083: i32 = global.cosmetic_effect;
    if (_e1083 != 0i) {
        {
            let _e1086: f32 = global.cosmetic_time;
            let _e1087: f32 = global.cosmetic_scroll;
            t = (_e1086 * _e1087);
            let _e1092: f32 = global.cosmetic_time;
            let _e1093: f32 = global.cosmetic_pulse;
            let _e1095: f32 = global.cosmetic_time;
            let _e1096: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1095 * _e1096))));
            let _e1104: vec2<f32> = texcoord_1;
            let _e1108: f32 = t;
            let _e1112: vec2<f32> = texcoord_1;
            let _e1116: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1112.y * 30f) - (_e1116 * 2f)))));
            let _e1125: f32 = rim;
            mask = (0.28f + (_e1125 * 0.72f));
            let _e1130: i32 = global.cosmetic_effect;
            if (_e1130 == 1i) {
                let _e1135: f32 = pulse;
                mask = (0.25f + (0.55f * _e1135));
            }
            let _e1138: i32 = global.cosmetic_effect;
            if (_e1138 == 3i) {
                let _e1146: f32 = wave;
                let _e1150: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1146) * 0.5f) + _e1150);
            }
            let _e1152: i32 = global.cosmetic_effect;
            if (_e1152 == 4i) {
                let _e1155: vec2<f32> = texcoord_1;
                let _e1158: f32 = t;
                let _e1161: f32 = t;
                let _e1167: vec2<f32> = texcoord_1;
                let _e1170: f32 = t;
                let _e1173: f32 = t;
                let _e1179: f32 = cosmetic_soft_noise(((_e1167 * 16f) + vec2<f32>((_e1170 * 0.3f), (-(_e1173) * 0.5f))));
                let _e1182: f32 = rim;
                mask = ((_e1179 * 0.45f) + (_e1182 * 0.6f));
            }
            let _e1186: i32 = global.cosmetic_effect;
            let _e1189: i32 = global.cosmetic_effect;
            if ((_e1186 == 5i) || (_e1189 == 7i)) {
                let _e1199: f32 = wave;
                let _e1204: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1199) * 0.65f)) + (_e1204 * 0.45f));
            }
            let _e1208: i32 = global.cosmetic_effect;
            if (_e1208 == 6i) {
                let _e1212: vec2<f32> = texcoord_1;
                let _e1216: vec2<f32> = texcoord_1;
                let _e1220: f32 = t;
                let _e1222: vec2<f32> = texcoord_1;
                let _e1226: f32 = t;
                let _e1230: vec2<f32> = texcoord_1;
                let _e1234: vec2<f32> = texcoord_1;
                let _e1238: f32 = t;
                let _e1240: vec2<f32> = texcoord_1;
                let _e1244: f32 = t;
                let _e1249: vec2<f32> = texcoord_1;
                let _e1253: vec2<f32> = texcoord_1;
                let _e1257: f32 = t;
                let _e1259: vec2<f32> = texcoord_1;
                let _e1263: f32 = t;
                let _e1267: vec2<f32> = texcoord_1;
                let _e1271: vec2<f32> = texcoord_1;
                let _e1275: f32 = t;
                let _e1277: vec2<f32> = texcoord_1;
                let _e1281: f32 = t;
                let _e1288: vec2<f32> = texcoord_1;
                let _e1292: vec2<f32> = texcoord_1;
                let _e1296: f32 = t;
                let _e1298: vec2<f32> = texcoord_1;
                let _e1302: f32 = t;
                let _e1306: vec2<f32> = texcoord_1;
                let _e1310: vec2<f32> = texcoord_1;
                let _e1314: f32 = t;
                let _e1316: vec2<f32> = texcoord_1;
                let _e1320: f32 = t;
                let _e1325: vec2<f32> = texcoord_1;
                let _e1329: vec2<f32> = texcoord_1;
                let _e1333: f32 = t;
                let _e1335: vec2<f32> = texcoord_1;
                let _e1339: f32 = t;
                let _e1343: vec2<f32> = texcoord_1;
                let _e1347: vec2<f32> = texcoord_1;
                let _e1351: f32 = t;
                let _e1353: vec2<f32> = texcoord_1;
                let _e1357: f32 = t;
                let _e1366: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1343.x * 31f) + sin(((_e1353.y * 29f) + _e1357))))), 16f)) + (_e1366 * 0.5f));
            }
            let _e1370: i32 = global.cosmetic_effect;
            if (_e1370 == 8i) {
                let _e1373: f32 = wave;
                let _e1376: f32 = rim;
                mask = ((_e1373 * 0.35f) + (_e1376 * 0.7f));
            }
            let _e1380: i32 = global.cosmetic_effect;
            if (_e1380 == 9i) {
                let _e1385: vec2<f32> = texcoord_1;
                let _e1389: f32 = t;
                let _e1391: vec2<f32> = texcoord_1;
                let _e1395: f32 = t;
                let _e1400: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1391.x * 40f) + _e1395)))) * _e1400);
            }
            let _e1405: vec3<f32> = global.cosmetic_primary;
            let _e1406: vec3<f32> = global.cosmetic_secondary;
            let _e1407: f32 = wave;
            energy = mix(_e1405, _e1406, vec3(_e1407));
            let _e1414: f32 = mask;
            let _e1418: f32 = global.cosmetic_intensity;
            let _e1420: f32 = pulse;
            strength = ((clamp(_e1414, 0f, 1f) * _e1418) * _e1420);
            let _e1423: i32 = global.cosmetic_preserve_palette;
            if (_e1423 != 0i) {
                let _e1426: f32 = strength;
                strength = (_e1426 * 0.65f);
            }
            let _e1429: vec4<f32> = (*col);
            let _e1431: vec4<f32> = (*col);
            let _e1433: vec3<f32> = energy;
            let _e1435: f32 = lum;
            let _e1440: f32 = strength;
            let _e1445: f32 = strength;
            let _e1451: vec4<f32> = (*col);
            let _e1453: vec3<f32> = energy;
            let _e1455: f32 = lum;
            let _e1460: f32 = strength;
            let _e1465: f32 = strength;
            let _e1472: vec3<f32> = mix(_e1451.xyz, (_e1453 * (0.4f + (_e1455 * 0.6f))), vec3(clamp((_e1465 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1472.x;
            (*col).y = _e1472.y;
            (*col).z = _e1472.z;
            let _e1479: vec4<f32> = (*col);
            let _e1481: vec4<f32> = (*col);
            let _e1483: vec3<f32> = energy;
            let _e1484: f32 = strength;
            let _e1488: vec3<f32> = (_e1481.xyz + ((_e1483 * _e1484) * 0.45f));
            (*col).x = _e1488.x;
            (*col).y = _e1488.y;
            (*col).z = _e1488.z;
        }
    }
    let _e1495: f32 = global.cosmetic_dissolve;
    if (_e1495 > 0f) {
        {
            let _e1498: vec2<f32> = texcoord_1;
            let _e1501: vec2<f32> = texcoord_1;
            let _e1505: vec2<f32> = texcoord_1;
            let _e1508: vec2<f32> = texcoord_1;
            let _e1512: f32 = cosmetic_noise(floor((_e1508 * 64f)));
            n_1 = _e1512;
            let _e1514: f32 = n_1;
            let _e1515: f32 = global.cosmetic_dissolve;
            if (_e1514 < _e1515) {
                discard;
            }
            let _e1517: vec4<f32> = (*col);
            let _e1519: vec4<f32> = (*col);
            let _e1521: vec3<f32> = global.cosmetic_primary;
            let _e1524: f32 = global.cosmetic_dissolve;
            let _e1528: f32 = global.cosmetic_dissolve;
            let _e1529: f32 = global.cosmetic_dissolve;
            let _e1532: f32 = n_1;
            let _e1538: vec3<f32> = (_e1519.xyz + ((_e1521 * (1f - smoothstep(_e1528, (_e1529 + 0.08f), _e1532))) * 0.3f));
            (*col).x = _e1538.x;
            (*col).y = _e1538.y;
            (*col).z = _e1538.z;
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
    var uvScale: f32;
    var det: f32;
    var tangent: vec3<f32>;
    var bitangent: vec3<f32>;
    var tangentLength2_: f32;
    var bitangentLength2_: f32;
    var local: f32;
    var orientation: f32;
    var nm: vec3<f32>;

    let _e196: vec3<f32> = surface_normal_1;
    n_2 = normalize(_e196);
    let _e199: i32 = global._prime_use_normal_map;
    if !((_e199 != 0i)) {
        let _e203: vec3<f32> = n_2;
        return _e203;
    }
    let _e205: vec3<f32> = surface_position_1;
    let _e206: vec3<f32> = dpdx(_e205);
    dp1_ = _e206;
    let _e209: vec3<f32> = surface_position_1;
    let _e210: vec3<f32> = dpdy(_e209);
    dp2_ = _e210;
    let _e213: vec2<f32> = texcoord_1;
    let _e214: vec2<f32> = dpdx(_e213);
    duv1_ = _e214;
    let _e217: vec2<f32> = texcoord_1;
    let _e218: vec2<f32> = dpdy(_e217);
    duv2_ = _e218;
    let _e220: vec2<f32> = duv1_;
    let _e222: vec2<f32> = duv1_;
    let _e225: vec2<f32> = duv1_;
    let _e227: vec2<f32> = duv1_;
    let _e230: vec2<f32> = duv1_;
    let _e232: vec2<f32> = duv1_;
    let _e235: vec2<f32> = duv1_;
    let _e237: vec2<f32> = duv1_;
    let _e241: vec2<f32> = duv2_;
    let _e243: vec2<f32> = duv2_;
    let _e246: vec2<f32> = duv2_;
    let _e248: vec2<f32> = duv2_;
    let _e251: vec2<f32> = duv2_;
    let _e253: vec2<f32> = duv2_;
    let _e256: vec2<f32> = duv2_;
    let _e258: vec2<f32> = duv2_;
    let _e262: vec2<f32> = duv1_;
    let _e264: vec2<f32> = duv1_;
    let _e267: vec2<f32> = duv1_;
    let _e269: vec2<f32> = duv1_;
    let _e272: vec2<f32> = duv1_;
    let _e274: vec2<f32> = duv1_;
    let _e277: vec2<f32> = duv1_;
    let _e279: vec2<f32> = duv1_;
    let _e283: vec2<f32> = duv2_;
    let _e285: vec2<f32> = duv2_;
    let _e288: vec2<f32> = duv2_;
    let _e290: vec2<f32> = duv2_;
    let _e293: vec2<f32> = duv2_;
    let _e295: vec2<f32> = duv2_;
    let _e298: vec2<f32> = duv2_;
    let _e300: vec2<f32> = duv2_;
    uvScale = max(max(abs(_e274.x), abs(_e279.y)), max(abs(_e295.x), abs(_e300.y)));
    let _e306: f32 = uvScale;
    if !((_e306 > 0f)) {
        let _e310: vec3<f32> = n_2;
        return _e310;
    }
    let _e311: vec2<f32> = duv1_;
    let _e312: f32 = uvScale;
    duv1_ = (_e311 / vec2(_e312));
    let _e315: vec2<f32> = duv2_;
    let _e316: f32 = uvScale;
    duv2_ = (_e315 / vec2(_e316));
    let _e319: vec2<f32> = duv1_;
    let _e321: vec2<f32> = duv2_;
    let _e324: vec2<f32> = duv1_;
    let _e326: vec2<f32> = duv2_;
    det = ((_e319.x * _e321.y) - (_e324.y * _e326.x));
    let _e332: f32 = det;
    let _e336: vec2<f32> = duv1_;
    let _e340: vec2<f32> = duv2_;
    if (abs(_e332) <= ((0.000001f * length(_e336)) * length(_e340))) {
        let _e344: vec3<f32> = n_2;
        return _e344;
    }
    let _e345: vec3<f32> = dp1_;
    let _e346: vec2<f32> = duv2_;
    let _e349: vec3<f32> = dp2_;
    let _e350: vec2<f32> = duv1_;
    tangent = ((_e345 * _e346.y) - (_e349 * _e350.y));
    let _e355: vec3<f32> = dp1_;
    let _e357: vec2<f32> = duv2_;
    let _e360: vec3<f32> = dp2_;
    let _e361: vec2<f32> = duv1_;
    bitangent = ((-(_e355) * _e357.x) + (_e360 * _e361.x));
    let _e368: vec3<f32> = tangent;
    let _e369: vec3<f32> = tangent;
    tangentLength2_ = dot(_e368, _e369);
    let _e374: vec3<f32> = bitangent;
    let _e375: vec3<f32> = bitangent;
    bitangentLength2_ = dot(_e374, _e375);
    let _e378: f32 = tangentLength2_;
    let _e382: f32 = bitangentLength2_;
    if (!((_e378 > 0f)) || !((_e382 > 0f))) {
        let _e387: vec3<f32> = n_2;
        return _e387;
    }
    let _e388: f32 = det;
    if (_e388 < 0f) {
        local = -1f;
    } else {
        local = 1f;
    }
    let _e395: f32 = local;
    orientation = _e395;
    let _e397: vec3<f32> = tangent;
    let _e398: f32 = orientation;
    let _e400: f32 = tangentLength2_;
    tangent = (_e397 * (_e398 * inverseSqrt(_e400)));
    let _e404: vec3<f32> = bitangent;
    let _e405: f32 = orientation;
    let _e407: f32 = bitangentLength2_;
    bitangent = (_e404 * (_e405 * inverseSqrt(_e407)));
    let _e412: vec2<f32> = texcoord_1;
    let _e413: vec4<f32> = prime_sample_normal_tex(_e412);
    nm = ((_e413.xyz * 2f) - vec3(1f));
    let _e421: vec3<f32> = tangent;
    let _e422: vec3<f32> = nm;
    let _e425: vec3<f32> = bitangent;
    let _e426: vec3<f32> = nm;
    let _e430: vec3<f32> = n_2;
    let _e431: vec3<f32> = nm;
    let _e435: vec3<f32> = tangent;
    let _e436: vec3<f32> = nm;
    let _e439: vec3<f32> = bitangent;
    let _e440: vec3<f32> = nm;
    let _e444: vec3<f32> = n_2;
    let _e445: vec3<f32> = nm;
    return normalize((((_e435 * _e436.x) + (_e439 * _e440.y)) + (_e444 * _e445.z)));
}

fn main_1() {
    var local_1: vec4<f32>;
    var base: vec4<f32>;
    var albedo: vec3<f32>;
    var cosmetic: vec4<f32>;
    var local_2: vec4<f32>;
    var sm: vec4<f32>;
    var roughness: f32;
    var physicalOrm: bool;
    var local_3: f32;
    var metallic: f32;
    var local_4: f32;
    var ambientOcclusion: f32;
    var emissive: f32;
    var e: vec3<f32>;
    var finish_1: vec2<f32>;

    let _e195: i32 = global._prime_use_texture;
    if (_e195 != 0i) {
        let _e199: vec2<f32> = texcoord_1;
        let _e200: vec4<f32> = prime_sample_tex(_e199);
        local_1 = _e200;
    } else {
        local_1 = vec4(1f);
    }
    let _e204: vec4<f32> = local_1;
    base = _e204;
    let _e206: vec4<f32> = base;
    if (_e206.w < 1f) {
        discard;
    }
    let _e210: vec4<f32> = base;
    let _e212: vec4<f32> = vertex_color_1;
    albedo = (_e210.xyz * _e212.xyz);
    let _e216: i32 = global._prime_use_pal_override;
    if (_e216 != 0i) {
        let _e219: vec4<f32> = global.pal_override_color;
        let _e221: vec4<f32> = vertex_color_1;
        albedo = (_e219.xyz * _e221.xyz);
    }
    let _e224: i32 = global._prime_use_override;
    if (_e224 != 0i) {
        let _e227: vec4<f32> = global.override_color;
        albedo = _e227.xyz;
    }
    let _e229: vec3<f32> = albedo;
    cosmetic = vec4<f32>(_e229.x, _e229.y, _e229.z, 1f);
    apply_cosmetics((&cosmetic));
    let _e238: vec4<f32> = cosmetic;
    albedo = _e238.xyz;
    let _e240: i32 = global.gbuffer_mode;
    if (_e240 == 1i) {
        {
            let _e246: vec3<f32> = albedo;
            let _e251: vec3<f32> = clamp(_e246, vec3(0f), vec3(1f));
            prime_output = vec4<f32>(_e251.x, _e251.y, _e251.z, 1f);
            return;
        }
    }
    let _e257: i32 = global.gbuffer_mode;
    if (_e257 == 2i) {
        {
            let _e260: vec3<f32> = mapped_normal();
            let _e265: vec3<f32> = ((_e260 * 0.5f) + vec3(0.5f));
            prime_output = vec4<f32>(_e265.x, _e265.y, _e265.z, 1f);
            return;
        }
    }
    let _e271: i32 = global._prime_use_specular_map;
    if (_e271 != 0i) {
        let _e275: vec2<f32> = texcoord_1;
        let _e276: vec4<f32> = prime_sample_specular_tex(_e275);
        local_2 = _e276;
    } else {
        let _e277: vec3<f32> = global.material_specular;
        let _e279: vec3<f32> = global.material_specular;
        let _e281: vec3<f32> = global.material_specular;
        let _e283: vec3<f32> = global.material_specular;
        let _e286: vec3<f32> = global.material_specular;
        let _e288: vec3<f32> = global.material_specular;
        let _e290: vec3<f32> = global.material_specular;
        let _e292: vec3<f32> = global.material_specular;
        let _e294: vec3<f32> = global.material_specular;
        let _e297: vec3<f32> = global.material_specular;
        local_2 = vec4<f32>(max(max(_e292.x, _e294.y), _e297.z), 0.62f, 0f, 1f);
    }
    let _e305: vec4<f32> = local_2;
    sm = _e305;
    let _e307: vec4<f32> = sm;
    let _e311: vec4<f32> = sm;
    roughness = clamp(_e311.y, 0.04f, 1f);
    let _e317: i32 = global._prime_use_specular_map;
    let _e320: vec4<f32> = sm;
    physicalOrm = ((_e317 != 0i) && (_e320.w < 0.5f));
    let _e326: bool = physicalOrm;
    if _e326 {
        let _e327: vec4<f32> = sm;
        let _e331: vec4<f32> = sm;
        local_3 = clamp(_e331.z, 0f, 1f);
    } else {
        let _e338: vec4<f32> = sm;
        let _e342: vec4<f32> = sm;
        let _e349: vec4<f32> = sm;
        let _e353: vec4<f32> = sm;
        local_3 = (smoothstep(0.45f, 0.95f, clamp(_e353.x, 0f, 1f)) * 0.75f);
    }
    let _e362: f32 = local_3;
    metallic = _e362;
    let _e364: bool = physicalOrm;
    if _e364 {
        let _e365: vec4<f32> = sm;
        let _e369: vec4<f32> = sm;
        local_4 = clamp(_e369.x, 0f, 1f);
    } else {
        local_4 = 1f;
    }
    let _e376: f32 = local_4;
    ambientOcclusion = _e376;
    let _e378: vec3<f32> = global.material_emission;
    let _e380: vec3<f32> = global.material_emission;
    let _e382: vec3<f32> = global.material_emission;
    let _e384: vec3<f32> = global.material_emission;
    let _e387: vec3<f32> = global.material_emission;
    let _e389: vec3<f32> = global.material_emission;
    let _e391: vec3<f32> = global.material_emission;
    let _e393: vec3<f32> = global.material_emission;
    let _e395: vec3<f32> = global.material_emission;
    let _e398: vec3<f32> = global.material_emission;
    emissive = max(max(_e393.x, _e395.y), _e398.z);
    let _e402: i32 = global._prime_use_emissive_map;
    if (_e402 != 0i) {
        {
            let _e406: vec2<f32> = texcoord_1;
            let _e407: vec4<f32> = prime_sample_emissive_tex(_e406);
            e = _e407.xyz;
            let _e416: vec3<f32> = e;
            let _e422: f32 = emissive;
            let _e428: vec3<f32> = e;
            emissive = max(_e422, dot(_e428, vec3<f32>(0.2126f, 0.7152f, 0.0722f)));
        }
    }
    let _e435: f32 = emissive;
    let _e436: f32 = global.emissive_intensity;
    emissive = (_e435 * _e436);
    let _e438: i32 = global.cosmetic_skin;
    let _e441: i32 = global._prime_use_specular_map;
    if ((_e438 != 0i) && !((_e441 != 0i))) {
        {
            let _e446: vec2<f32> = cosmetic_finish();
            finish_1 = _e446;
            let _e448: vec2<f32> = finish_1;
            metallic = _e448.x;
            let _e450: vec2<f32> = finish_1;
            roughness = _e450.y;
        }
    }
    let _e452: i32 = global.cosmetic_skin;
    if (_e452 == 4i) {
        let _e456: f32 = cosmetic_circuit();
        let _e459: f32 = emissive;
        let _e460: f32 = cosmetic_circuit();
        emissive = max(_e459, (_e460 * 0.55f));
    }
    let _e464: f32 = metallic;
    let _e465: f32 = roughness;
    let _e469: f32 = emissive;
    let _e473: f32 = ambientOcclusion;
    prime_output = vec4<f32>(_e464, _e465, clamp(_e469, 0f, 1f), _e473);
    return;
}

@fragment
fn main(@location(0) texcoord: vec2<f32>, @location(1) vertex_color: vec4<f32>, @location(2) surface_normal: vec3<f32>, @location(3) surface_position: vec3<f32>) -> FragmentOutput {
    texcoord_1 = texcoord;
    vertex_color_1 = vertex_color;
    surface_normal_1 = surface_normal;
    surface_position_1 = surface_position;
    main_1();
    let _e217: vec4<f32> = prime_output;
    return FragmentOutput(_e217);
}
