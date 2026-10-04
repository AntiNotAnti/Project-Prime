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

fn cosmetic_noise(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e193: vec2<f32> = p_1;
    let _e202: vec2<f32> = p_1;
    let _e214: vec2<f32> = p_1;
    let _e223: vec2<f32> = p_1;
    return fract((sin(dot(_e223, vec2<f32>(127.1f, 311.7f))) * 43758.547f));
}

fn cosmetic_soft_noise(p_2: vec2<f32>) -> f32 {
    var p_3: vec2<f32>;
    var cell: vec2<f32>;
    var f: vec2<f32>;

    p_3 = p_2;
    let _e190: vec2<f32> = p_3;
    cell = floor(_e190);
    let _e194: vec2<f32> = p_3;
    f = fract(_e194);
    let _e197: vec2<f32> = f;
    let _e198: vec2<f32> = f;
    let _e202: vec2<f32> = f;
    f = ((_e197 * _e198) * (vec2(3f) - (2f * _e202)));
    let _e208: vec2<f32> = cell;
    let _e209: f32 = cosmetic_noise(_e208);
    let _e210: vec2<f32> = cell;
    let _e215: vec2<f32> = cell;
    let _e220: f32 = cosmetic_noise((_e215 + vec2<f32>(1f, 0f)));
    let _e221: vec2<f32> = f;
    let _e224: vec2<f32> = cell;
    let _e225: f32 = cosmetic_noise(_e224);
    let _e226: vec2<f32> = cell;
    let _e231: vec2<f32> = cell;
    let _e236: f32 = cosmetic_noise((_e231 + vec2<f32>(1f, 0f)));
    let _e237: vec2<f32> = f;
    let _e240: vec2<f32> = cell;
    let _e245: vec2<f32> = cell;
    let _e250: f32 = cosmetic_noise((_e245 + vec2<f32>(0f, 1f)));
    let _e251: vec2<f32> = cell;
    let _e255: vec2<f32> = cell;
    let _e259: f32 = cosmetic_noise((_e255 + vec2(1f)));
    let _e260: vec2<f32> = f;
    let _e262: vec2<f32> = cell;
    let _e267: vec2<f32> = cell;
    let _e272: f32 = cosmetic_noise((_e267 + vec2<f32>(0f, 1f)));
    let _e273: vec2<f32> = cell;
    let _e277: vec2<f32> = cell;
    let _e281: f32 = cosmetic_noise((_e277 + vec2(1f)));
    let _e282: vec2<f32> = f;
    let _e285: vec2<f32> = f;
    let _e288: vec2<f32> = cell;
    let _e289: f32 = cosmetic_noise(_e288);
    let _e290: vec2<f32> = cell;
    let _e295: vec2<f32> = cell;
    let _e300: f32 = cosmetic_noise((_e295 + vec2<f32>(1f, 0f)));
    let _e301: vec2<f32> = f;
    let _e304: vec2<f32> = cell;
    let _e305: f32 = cosmetic_noise(_e304);
    let _e306: vec2<f32> = cell;
    let _e311: vec2<f32> = cell;
    let _e316: f32 = cosmetic_noise((_e311 + vec2<f32>(1f, 0f)));
    let _e317: vec2<f32> = f;
    let _e320: vec2<f32> = cell;
    let _e325: vec2<f32> = cell;
    let _e330: f32 = cosmetic_noise((_e325 + vec2<f32>(0f, 1f)));
    let _e331: vec2<f32> = cell;
    let _e335: vec2<f32> = cell;
    let _e339: f32 = cosmetic_noise((_e335 + vec2(1f)));
    let _e340: vec2<f32> = f;
    let _e342: vec2<f32> = cell;
    let _e347: vec2<f32> = cell;
    let _e352: f32 = cosmetic_noise((_e347 + vec2<f32>(0f, 1f)));
    let _e353: vec2<f32> = cell;
    let _e357: vec2<f32> = cell;
    let _e361: f32 = cosmetic_noise((_e357 + vec2(1f)));
    let _e362: vec2<f32> = f;
    let _e365: vec2<f32> = f;
    return mix(mix(_e305, _e316, _e317.x), mix(_e352, _e361, _e362.x), _e365.y);
}

fn cosmetic_finish() -> vec2<f32> {
    let _e187: i32 = global.cosmetic_skin;
    if (_e187 == 1i) {
        return vec2<f32>(0.78f, 0.3f);
    }
    let _e193: i32 = global.cosmetic_skin;
    if (_e193 == 2i) {
        return vec2<f32>(0.85f, 0.38f);
    }
    let _e199: i32 = global.cosmetic_skin;
    if (_e199 == 3i) {
        return vec2<f32>(0.05f, 0.24f);
    }
    let _e205: i32 = global.cosmetic_skin;
    if (_e205 == 4i) {
        return vec2<f32>(0.55f, 0.42f);
    }
    let _e211: i32 = global.cosmetic_skin;
    if (_e211 == 5i) {
        return vec2<f32>(0.12f, 0.68f);
    }
    return vec2<f32>(0.35f, 0.28f);
}

fn cosmetic_circuit() -> f32 {
    var grid: vec2<f32>;
    var trace: f32;
    var node: f32;
    var travel: f32;

    let _e187: vec2<f32> = texcoord_1;
    let _e190: vec2<f32> = texcoord_1;
    grid = fract((_e190 * 18f));
    let _e198: vec2<f32> = grid;
    let _e200: vec2<f32> = grid;
    let _e202: vec2<f32> = grid;
    let _e204: vec2<f32> = grid;
    let _e209: vec2<f32> = grid;
    let _e211: vec2<f32> = grid;
    let _e213: vec2<f32> = grid;
    let _e215: vec2<f32> = grid;
    trace = (1f - smoothstep(0.035f, 0.09f, min(_e213.x, _e215.y)));
    let _e224: vec2<f32> = grid;
    let _e228: vec2<f32> = grid;
    let _e235: vec2<f32> = grid;
    let _e239: vec2<f32> = grid;
    node = (1f - smoothstep(0.08f, 0.16f, length((_e239 - vec2(0.15f)))));
    let _e249: vec2<f32> = texcoord_1;
    let _e251: vec2<f32> = texcoord_1;
    let _e256: f32 = global.cosmetic_time;
    let _e260: vec2<f32> = texcoord_1;
    let _e262: vec2<f32> = texcoord_1;
    let _e267: f32 = global.cosmetic_time;
    let _e277: vec2<f32> = texcoord_1;
    let _e279: vec2<f32> = texcoord_1;
    let _e284: f32 = global.cosmetic_time;
    let _e288: vec2<f32> = texcoord_1;
    let _e290: vec2<f32> = texcoord_1;
    let _e295: f32 = global.cosmetic_time;
    travel = pow((0.5f + (0.5f * sin((((_e288.x + _e290.y) * 32f) - (_e295 * 1.8f))))), 8f);
    let _e305: f32 = trace;
    let _e307: f32 = travel;
    let _e312: f32 = node;
    let _e315: f32 = trace;
    let _e317: f32 = travel;
    let _e322: f32 = node;
    return max((_e315 * (0.3f + (_e317 * 0.7f))), (_e322 * 0.75f));
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

    let _e188: i32 = global.cosmetic_skin;
    let _e191: i32 = global.cosmetic_effect;
    let _e195: f32 = global.cosmetic_dissolve;
    if (((_e188 == 0i) && (_e191 == 0i)) && (_e195 <= 0f)) {
        return;
    }
    let _e199: vec4<f32> = (*col);
    nativeColor = _e199.xyz;
    let _e202: vec4<f32> = (*col);
    let _e208: vec4<f32> = (*col);
    lum = dot(_e208.xyz, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
    let _e216: vec3<f32> = surface_normal_1;
    let _e219: vec3<f32> = surface_normal_1;
    let _e220: vec3<f32> = surface_normal_1;
    let _e225: vec3<f32> = surface_normal_1;
    let _e226: vec3<f32> = surface_normal_1;
    let _e232: vec3<f32> = surface_normal_1;
    let _e233: vec3<f32> = surface_normal_1;
    let _e238: vec3<f32> = surface_normal_1;
    let _e239: vec3<f32> = surface_normal_1;
    n = (_e216 * inverseSqrt(max(dot(_e238, _e239), 0.0001f)));
    let _e246: mat4x4<f32> = global.view_mtx;
    let _e256: vec3<f32> = n;
    viewNormal = (mat3x3<f32>(_e246[0].xyz, _e246[1].xyz, _e246[2].xyz) * _e256);
    let _e259: mat4x4<f32> = global.view_mtx;
    let _e260: vec3<f32> = surface_position_1;
    toEye = -((_e259 * vec4<f32>(_e260.x, _e260.y, _e260.z, 1f)).xyz);
    let _e270: vec3<f32> = toEye;
    let _e273: vec3<f32> = toEye;
    let _e274: vec3<f32> = toEye;
    let _e279: vec3<f32> = toEye;
    let _e280: vec3<f32> = toEye;
    let _e286: vec3<f32> = toEye;
    let _e287: vec3<f32> = toEye;
    let _e292: vec3<f32> = toEye;
    let _e293: vec3<f32> = toEye;
    toEye = (_e270 * inverseSqrt(max(dot(_e292, _e293), 0.0001f)));
    let _e302: vec3<f32> = viewNormal;
    let _e303: vec3<f32> = toEye;
    let _e307: vec3<f32> = viewNormal;
    let _e308: vec3<f32> = toEye;
    let _e317: vec3<f32> = viewNormal;
    let _e318: vec3<f32> = toEye;
    let _e322: vec3<f32> = viewNormal;
    let _e323: vec3<f32> = toEye;
    let _e334: vec3<f32> = viewNormal;
    let _e335: vec3<f32> = toEye;
    let _e339: vec3<f32> = viewNormal;
    let _e340: vec3<f32> = toEye;
    let _e349: vec3<f32> = viewNormal;
    let _e350: vec3<f32> = toEye;
    let _e354: vec3<f32> = viewNormal;
    let _e355: vec3<f32> = toEye;
    rim = pow(clamp((1f - abs(dot(_e354, _e355))), 0f, 1f), 2.2f);
    let _e365: i32 = global.cosmetic_skin;
    if (_e365 == 1i) {
        {
            let _e368: vec4<f32> = (*col);
            let _e370: vec4<f32> = (*col);
            let _e377: f32 = lum;
            let _e381: vec4<f32> = (*col);
            let _e388: f32 = lum;
            let _e393: vec3<f32> = mix(_e381.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e388)), vec3(0.88f));
            (*col).x = _e393.x;
            (*col).y = _e393.y;
            (*col).z = _e393.z;
        }
    } else {
        let _e400: i32 = global.cosmetic_skin;
        if (_e400 == 2i) {
            {
                let _e405: vec2<f32> = texcoord_1;
                let _e409: vec2<f32> = texcoord_1;
                let _e414: vec2<f32> = texcoord_1;
                let _e418: vec2<f32> = texcoord_1;
                let _e426: vec2<f32> = texcoord_1;
                let _e430: vec2<f32> = texcoord_1;
                let _e435: vec2<f32> = texcoord_1;
                let _e439: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e430.x * 85f)) * sin((_e439.y * 85f))));
                let _e447: vec4<f32> = (*col);
                let _e449: vec4<f32> = (*col);
                let _e456: f32 = lum;
                let _e460: vec4<f32> = (*col);
                let _e467: f32 = lum;
                let _e472: vec3<f32> = mix(_e460.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e467)), vec3(0.82f));
                (*col).x = _e472.x;
                (*col).y = _e472.y;
                (*col).z = _e472.z;
                let _e479: vec4<f32> = (*col);
                let _e481: vec4<f32> = (*col);
                let _e483: f32 = etch;
                let _e489: vec3<f32> = (_e481.xyz + (_e483 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e489.x;
                (*col).y = _e489.y;
                (*col).z = _e489.z;
            }
        }
    }
    let _e496: i32 = global.cosmetic_skin;
    if (_e496 == 3i) {
        {
            let _e499: vec2<f32> = texcoord_1;
            let _e502: vec2<f32> = texcoord_1;
            panel = fract((_e502 * 12f));
            let _e509: vec2<f32> = panel;
            let _e513: vec2<f32> = panel;
            let _e518: vec2<f32> = panel;
            let _e522: vec2<f32> = panel;
            seam = (smoothstep(0.025f, 0.075f, _e513.x) * smoothstep(0.025f, 0.075f, _e522.y));
            let _e527: vec4<f32> = (*col);
            let _e546: f32 = seam;
            let _e550: f32 = lum;
            let _e554: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e546)) * (0.5f + (_e550 * 0.6f)));
            (*col).x = _e554.x;
            (*col).y = _e554.y;
            (*col).z = _e554.z;
        }
    } else {
        let _e561: i32 = global.cosmetic_skin;
        if (_e561 == 4i) {
            {
                let _e564: vec4<f32> = (*col);
                let _e571: f32 = lum;
                let _e578: f32 = cosmetic_circuit();
                let _e580: vec3<f32> = ((vec3<f32>(0.13f, 0.18f, 0.22f) * (0.6f + _e571)) + (vec3<f32>(0.08f, 0.8f, 0.65f) * _e578));
                (*col).x = _e580.x;
                (*col).y = _e580.y;
                (*col).z = _e580.z;
            }
        } else {
            let _e587: i32 = global.cosmetic_skin;
            if (_e587 == 5i) {
                {
                    let _e592: vec2<f32> = texcoord_1;
                    let _e596: vec2<f32> = texcoord_1;
                    let _e601: vec2<f32> = texcoord_1;
                    let _e605: vec2<f32> = texcoord_1;
                    let _e613: vec2<f32> = texcoord_1;
                    let _e617: vec2<f32> = texcoord_1;
                    let _e622: vec2<f32> = texcoord_1;
                    let _e626: vec2<f32> = texcoord_1;
                    let _e637: vec2<f32> = texcoord_1;
                    let _e641: vec2<f32> = texcoord_1;
                    let _e646: vec2<f32> = texcoord_1;
                    let _e650: vec2<f32> = texcoord_1;
                    let _e658: vec2<f32> = texcoord_1;
                    let _e662: vec2<f32> = texcoord_1;
                    let _e667: vec2<f32> = texcoord_1;
                    let _e671: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e658.x * 65f) + (_e662.y * 38f)) + (sin((_e671.y * 25f)) * 2f))));
                    let _e682: vec4<f32> = (*col);
                    let _e701: f32 = stripe;
                    let _e705: f32 = lum;
                    let _e707: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e701)) * (0.55f + _e705));
                    (*col).x = _e707.x;
                    (*col).y = _e707.y;
                    (*col).z = _e707.z;
                }
            } else {
                let _e714: i32 = global.cosmetic_skin;
                if (_e714 == 6i) {
                    {
                        let _e717: vec2<f32> = texcoord_1;
                        let _e720: f32 = global.cosmetic_time;
                        let _e726: vec2<f32> = texcoord_1;
                        let _e729: f32 = global.cosmetic_time;
                        let _e735: f32 = cosmetic_soft_noise(((_e726 * 9f) + vec2<f32>((_e729 * 0.025f), 0f)));
                        cloud = _e735;
                        let _e738: vec2<f32> = texcoord_1;
                        let _e741: vec2<f32> = texcoord_1;
                        let _e745: vec2<f32> = texcoord_1;
                        let _e748: vec2<f32> = texcoord_1;
                        let _e752: f32 = cosmetic_noise(floor((_e748 * 100f)));
                        let _e754: vec2<f32> = texcoord_1;
                        let _e757: vec2<f32> = texcoord_1;
                        let _e761: vec2<f32> = texcoord_1;
                        let _e764: vec2<f32> = texcoord_1;
                        let _e768: f32 = cosmetic_noise(floor((_e764 * 100f)));
                        star = step(0.985f, _e768);
                        let _e771: vec4<f32> = (*col);
                        let _e790: f32 = cloud;
                        let _e794: f32 = lum;
                        let _e797: f32 = star;
                        let _e801: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e790)) * (0.5f + _e794)) + vec3((_e797 * 0.65f)));
                        (*col).x = _e801.x;
                        (*col).y = _e801.y;
                        (*col).z = _e801.z;
                    }
                }
            }
        }
    }
    let _e808: i32 = global.cosmetic_skin;
    if (_e808 != 0i) {
        {
            let _e811: vec2<f32> = cosmetic_finish();
            finish = _e811;
            let _e825: vec3<f32> = viewNormal;
            let _e852: vec3<f32> = viewNormal;
            let _e870: vec2<f32> = finish;
            let _e874: vec2<f32> = finish;
            let _e889: vec3<f32> = viewNormal;
            let _e916: vec3<f32> = viewNormal;
            let _e934: vec2<f32> = finish;
            let _e938: vec2<f32> = finish;
            sheen = pow(clamp(dot(_e916, normalize(vec3<f32>(-0.35f, 0.6f, 0.72f))), 0f, 1f), mix(44f, 8f, _e938.y));
            let _e943: vec4<f32> = (*col);
            let _e945: vec4<f32> = (*col);
            let _e951: f32 = rim;
            let _e954: vec2<f32> = finish;
            let _e960: vec4<f32> = (*col);
            let _e964: vec2<f32> = finish;
            let _e968: vec4<f32> = (*col);
            let _e972: vec2<f32> = finish;
            let _e976: f32 = sheen;
            let _e979: vec3<f32> = (_e945.xyz + (((vec3<f32>(0.12f, 0.16f, 0.2f) * _e951) * (1f - _e954.y)) + (mix(vec3(0.12f), (_e968.xyz * 0.28f), vec3(_e972.x)) * _e976)));
            (*col).x = _e979.x;
            (*col).y = _e979.y;
            (*col).z = _e979.z;
        }
    }
    let _e986: i32 = global.cosmetic_preserve_palette;
    let _e989: i32 = global.cosmetic_skin;
    if ((_e986 != 0i) && (_e989 != 0i)) {
        {
            let _e993: vec3<f32> = nativeColor;
            let _e995: vec3<f32> = nativeColor;
            let _e997: vec3<f32> = nativeColor;
            let _e999: vec3<f32> = nativeColor;
            let _e1001: vec3<f32> = nativeColor;
            let _e1004: vec3<f32> = nativeColor;
            let _e1006: vec3<f32> = nativeColor;
            let _e1008: vec3<f32> = nativeColor;
            let _e1010: vec3<f32> = nativeColor;
            let _e1012: vec3<f32> = nativeColor;
            let _e1016: vec3<f32> = nativeColor;
            let _e1018: vec3<f32> = nativeColor;
            let _e1020: vec3<f32> = nativeColor;
            let _e1022: vec3<f32> = nativeColor;
            let _e1024: vec3<f32> = nativeColor;
            let _e1027: vec3<f32> = nativeColor;
            let _e1029: vec3<f32> = nativeColor;
            let _e1031: vec3<f32> = nativeColor;
            let _e1033: vec3<f32> = nativeColor;
            let _e1035: vec3<f32> = nativeColor;
            chroma = (max(_e1004.x, max(_e1010.y, _e1012.z)) - min(_e1027.x, min(_e1033.y, _e1035.z)));
            let _e1041: vec4<f32> = (*col);
            let _e1043: vec4<f32> = (*col);
            let _e1051: f32 = chroma;
            let _e1055: vec4<f32> = (*col);
            let _e1057: vec3<f32> = nativeColor;
            let _e1063: f32 = chroma;
            let _e1068: vec3<f32> = mix(_e1055.xyz, _e1057, vec3((smoothstep(0.1f, 0.35f, _e1063) * 0.9f)));
            (*col).x = _e1068.x;
            (*col).y = _e1068.y;
            (*col).z = _e1068.z;
        }
    }
    let _e1075: i32 = global.cosmetic_effect;
    if (_e1075 != 0i) {
        {
            let _e1078: f32 = global.cosmetic_time;
            let _e1079: f32 = global.cosmetic_scroll;
            t = (_e1078 * _e1079);
            let _e1084: f32 = global.cosmetic_time;
            let _e1085: f32 = global.cosmetic_pulse;
            let _e1087: f32 = global.cosmetic_time;
            let _e1088: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e1087 * _e1088))));
            let _e1096: vec2<f32> = texcoord_1;
            let _e1100: f32 = t;
            let _e1104: vec2<f32> = texcoord_1;
            let _e1108: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e1104.y * 30f) - (_e1108 * 2f)))));
            let _e1117: f32 = rim;
            mask = (0.28f + (_e1117 * 0.72f));
            let _e1122: i32 = global.cosmetic_effect;
            if (_e1122 == 1i) {
                let _e1127: f32 = pulse;
                mask = (0.25f + (0.55f * _e1127));
            }
            let _e1130: i32 = global.cosmetic_effect;
            if (_e1130 == 3i) {
                let _e1138: f32 = wave;
                let _e1142: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1138) * 0.5f) + _e1142);
            }
            let _e1144: i32 = global.cosmetic_effect;
            if (_e1144 == 4i) {
                let _e1147: vec2<f32> = texcoord_1;
                let _e1150: f32 = t;
                let _e1153: f32 = t;
                let _e1159: vec2<f32> = texcoord_1;
                let _e1162: f32 = t;
                let _e1165: f32 = t;
                let _e1171: f32 = cosmetic_soft_noise(((_e1159 * 16f) + vec2<f32>((_e1162 * 0.3f), (-(_e1165) * 0.5f))));
                let _e1174: f32 = rim;
                mask = ((_e1171 * 0.45f) + (_e1174 * 0.6f));
            }
            let _e1178: i32 = global.cosmetic_effect;
            let _e1181: i32 = global.cosmetic_effect;
            if ((_e1178 == 5i) || (_e1181 == 7i)) {
                let _e1191: f32 = wave;
                let _e1196: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1191) * 0.65f)) + (_e1196 * 0.45f));
            }
            let _e1200: i32 = global.cosmetic_effect;
            if (_e1200 == 6i) {
                let _e1204: vec2<f32> = texcoord_1;
                let _e1208: vec2<f32> = texcoord_1;
                let _e1212: f32 = t;
                let _e1214: vec2<f32> = texcoord_1;
                let _e1218: f32 = t;
                let _e1222: vec2<f32> = texcoord_1;
                let _e1226: vec2<f32> = texcoord_1;
                let _e1230: f32 = t;
                let _e1232: vec2<f32> = texcoord_1;
                let _e1236: f32 = t;
                let _e1241: vec2<f32> = texcoord_1;
                let _e1245: vec2<f32> = texcoord_1;
                let _e1249: f32 = t;
                let _e1251: vec2<f32> = texcoord_1;
                let _e1255: f32 = t;
                let _e1259: vec2<f32> = texcoord_1;
                let _e1263: vec2<f32> = texcoord_1;
                let _e1267: f32 = t;
                let _e1269: vec2<f32> = texcoord_1;
                let _e1273: f32 = t;
                let _e1280: vec2<f32> = texcoord_1;
                let _e1284: vec2<f32> = texcoord_1;
                let _e1288: f32 = t;
                let _e1290: vec2<f32> = texcoord_1;
                let _e1294: f32 = t;
                let _e1298: vec2<f32> = texcoord_1;
                let _e1302: vec2<f32> = texcoord_1;
                let _e1306: f32 = t;
                let _e1308: vec2<f32> = texcoord_1;
                let _e1312: f32 = t;
                let _e1317: vec2<f32> = texcoord_1;
                let _e1321: vec2<f32> = texcoord_1;
                let _e1325: f32 = t;
                let _e1327: vec2<f32> = texcoord_1;
                let _e1331: f32 = t;
                let _e1335: vec2<f32> = texcoord_1;
                let _e1339: vec2<f32> = texcoord_1;
                let _e1343: f32 = t;
                let _e1345: vec2<f32> = texcoord_1;
                let _e1349: f32 = t;
                let _e1358: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1335.x * 31f) + sin(((_e1345.y * 29f) + _e1349))))), 16f)) + (_e1358 * 0.5f));
            }
            let _e1362: i32 = global.cosmetic_effect;
            if (_e1362 == 8i) {
                let _e1365: f32 = wave;
                let _e1368: f32 = rim;
                mask = ((_e1365 * 0.35f) + (_e1368 * 0.7f));
            }
            let _e1372: i32 = global.cosmetic_effect;
            if (_e1372 == 9i) {
                let _e1377: vec2<f32> = texcoord_1;
                let _e1381: f32 = t;
                let _e1383: vec2<f32> = texcoord_1;
                let _e1387: f32 = t;
                let _e1392: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1383.x * 40f) + _e1387)))) * _e1392);
            }
            let _e1397: vec3<f32> = global.cosmetic_primary;
            let _e1398: vec3<f32> = global.cosmetic_secondary;
            let _e1399: f32 = wave;
            energy = mix(_e1397, _e1398, vec3(_e1399));
            let _e1406: f32 = mask;
            let _e1410: f32 = global.cosmetic_intensity;
            let _e1412: f32 = pulse;
            strength = ((clamp(_e1406, 0f, 1f) * _e1410) * _e1412);
            let _e1415: i32 = global.cosmetic_preserve_palette;
            if (_e1415 != 0i) {
                let _e1418: f32 = strength;
                strength = (_e1418 * 0.65f);
            }
            let _e1421: vec4<f32> = (*col);
            let _e1423: vec4<f32> = (*col);
            let _e1425: vec3<f32> = energy;
            let _e1427: f32 = lum;
            let _e1432: f32 = strength;
            let _e1437: f32 = strength;
            let _e1443: vec4<f32> = (*col);
            let _e1445: vec3<f32> = energy;
            let _e1447: f32 = lum;
            let _e1452: f32 = strength;
            let _e1457: f32 = strength;
            let _e1464: vec3<f32> = mix(_e1443.xyz, (_e1445 * (0.4f + (_e1447 * 0.6f))), vec3(clamp((_e1457 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1464.x;
            (*col).y = _e1464.y;
            (*col).z = _e1464.z;
            let _e1471: vec4<f32> = (*col);
            let _e1473: vec4<f32> = (*col);
            let _e1475: vec3<f32> = energy;
            let _e1476: f32 = strength;
            let _e1480: vec3<f32> = (_e1473.xyz + ((_e1475 * _e1476) * 0.45f));
            (*col).x = _e1480.x;
            (*col).y = _e1480.y;
            (*col).z = _e1480.z;
        }
    }
    let _e1487: f32 = global.cosmetic_dissolve;
    if (_e1487 > 0f) {
        {
            let _e1490: vec2<f32> = texcoord_1;
            let _e1493: vec2<f32> = texcoord_1;
            let _e1497: vec2<f32> = texcoord_1;
            let _e1500: vec2<f32> = texcoord_1;
            let _e1504: f32 = cosmetic_noise(floor((_e1500 * 64f)));
            n_1 = _e1504;
            let _e1506: f32 = n_1;
            let _e1507: f32 = global.cosmetic_dissolve;
            if (_e1506 < _e1507) {
                discard;
            }
            let _e1509: vec4<f32> = (*col);
            let _e1511: vec4<f32> = (*col);
            let _e1513: vec3<f32> = global.cosmetic_primary;
            let _e1516: f32 = global.cosmetic_dissolve;
            let _e1520: f32 = global.cosmetic_dissolve;
            let _e1521: f32 = global.cosmetic_dissolve;
            let _e1524: f32 = n_1;
            let _e1530: vec3<f32> = (_e1511.xyz + ((_e1513 * (1f - smoothstep(_e1520, (_e1521 + 0.08f), _e1524))) * 0.3f));
            (*col).x = _e1530.x;
            (*col).y = _e1530.y;
            (*col).z = _e1530.z;
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

    let _e188: vec3<f32> = surface_normal_1;
    n_2 = normalize(_e188);
    let _e191: i32 = global._prime_use_normal_map;
    if !((_e191 != 0i)) {
        let _e195: vec3<f32> = n_2;
        return _e195;
    }
    let _e197: vec3<f32> = surface_position_1;
    let _e198: vec3<f32> = dpdx(_e197);
    dp1_ = _e198;
    let _e201: vec3<f32> = surface_position_1;
    let _e202: vec3<f32> = dpdy(_e201);
    dp2_ = _e202;
    let _e205: vec2<f32> = texcoord_1;
    let _e206: vec2<f32> = dpdx(_e205);
    duv1_ = _e206;
    let _e209: vec2<f32> = texcoord_1;
    let _e210: vec2<f32> = dpdy(_e209);
    duv2_ = _e210;
    let _e212: vec2<f32> = duv1_;
    let _e214: vec2<f32> = duv2_;
    let _e217: vec2<f32> = duv1_;
    let _e219: vec2<f32> = duv2_;
    det = ((_e212.x * _e214.y) - (_e217.y * _e219.x));
    let _e225: f32 = det;
    if (abs(_e225) < 0.000001f) {
        let _e229: vec3<f32> = n_2;
        return _e229;
    }
    let _e230: vec3<f32> = dp1_;
    let _e231: vec2<f32> = duv2_;
    let _e234: vec3<f32> = dp2_;
    let _e235: vec2<f32> = duv1_;
    let _e239: f32 = det;
    let _e242: vec3<f32> = dp1_;
    let _e243: vec2<f32> = duv2_;
    let _e246: vec3<f32> = dp2_;
    let _e247: vec2<f32> = duv1_;
    let _e251: f32 = det;
    tangent = normalize((((_e242 * _e243.y) - (_e246 * _e247.y)) / vec3(_e251)));
    let _e256: vec3<f32> = dp1_;
    let _e258: vec2<f32> = duv2_;
    let _e261: vec3<f32> = dp2_;
    let _e262: vec2<f32> = duv1_;
    let _e266: f32 = det;
    let _e269: vec3<f32> = dp1_;
    let _e271: vec2<f32> = duv2_;
    let _e274: vec3<f32> = dp2_;
    let _e275: vec2<f32> = duv1_;
    let _e279: f32 = det;
    bitangent = normalize((((-(_e269) * _e271.x) + (_e274 * _e275.x)) / vec3(_e279)));
    let _e285: vec2<f32> = texcoord_1;
    let _e286: vec4<f32> = prime_sample_normal_tex(_e285);
    nm = ((_e286.xyz * 2f) - vec3(1f));
    let _e294: vec3<f32> = tangent;
    let _e295: vec3<f32> = nm;
    let _e298: vec3<f32> = bitangent;
    let _e299: vec3<f32> = nm;
    let _e303: vec3<f32> = n_2;
    let _e304: vec3<f32> = nm;
    let _e308: vec3<f32> = tangent;
    let _e309: vec3<f32> = nm;
    let _e312: vec3<f32> = bitangent;
    let _e313: vec3<f32> = nm;
    let _e317: vec3<f32> = n_2;
    let _e318: vec3<f32> = nm;
    return normalize((((_e308 * _e309.x) + (_e312 * _e313.y)) + (_e317 * _e318.z)));
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

    let _e187: i32 = global._prime_use_texture;
    if (_e187 != 0i) {
        let _e191: vec2<f32> = texcoord_1;
        let _e192: vec4<f32> = prime_sample_tex(_e191);
        local = _e192;
    } else {
        local = vec4(1f);
    }
    let _e196: vec4<f32> = local;
    base = _e196;
    let _e198: vec4<f32> = base;
    if (_e198.w < 0.99f) {
        discard;
    }
    let _e202: vec4<f32> = base;
    let _e204: vec4<f32> = vertex_color_1;
    albedo = (_e202.xyz * _e204.xyz);
    let _e208: i32 = global._prime_use_pal_override;
    if (_e208 != 0i) {
        let _e211: vec4<f32> = global.pal_override_color;
        let _e213: vec4<f32> = vertex_color_1;
        albedo = (_e211.xyz * _e213.xyz);
    }
    let _e216: i32 = global._prime_use_override;
    if (_e216 != 0i) {
        let _e219: vec4<f32> = global.override_color;
        albedo = _e219.xyz;
    }
    let _e221: vec3<f32> = albedo;
    cosmetic = vec4<f32>(_e221.x, _e221.y, _e221.z, 1f);
    apply_cosmetics((&cosmetic));
    let _e230: vec4<f32> = cosmetic;
    albedo = _e230.xyz;
    let _e232: i32 = prime_mrt_mode;
    if (_e232 == 1i) {
        {
            let _e238: vec3<f32> = albedo;
            let _e243: vec3<f32> = clamp(_e238, vec3(0f), vec3(1f));
            prime_output = vec4<f32>(_e243.x, _e243.y, _e243.z, 1f);
            return;
        }
    }
    let _e249: i32 = prime_mrt_mode;
    if (_e249 == 2i) {
        {
            let _e252: vec3<f32> = mapped_normal();
            let _e257: vec3<f32> = ((_e252 * 0.5f) + vec3(0.5f));
            prime_output = vec4<f32>(_e257.x, _e257.y, _e257.z, 1f);
            return;
        }
    }
    let _e263: i32 = global._prime_use_specular_map;
    if (_e263 != 0i) {
        let _e267: vec2<f32> = texcoord_1;
        let _e268: vec4<f32> = prime_sample_specular_tex(_e267);
        local_1 = _e268;
    } else {
        let _e269: vec3<f32> = global.material_specular;
        let _e271: vec3<f32> = global.material_specular;
        let _e273: vec3<f32> = global.material_specular;
        let _e275: vec3<f32> = global.material_specular;
        let _e278: vec3<f32> = global.material_specular;
        let _e280: vec3<f32> = global.material_specular;
        let _e282: vec3<f32> = global.material_specular;
        let _e284: vec3<f32> = global.material_specular;
        let _e286: vec3<f32> = global.material_specular;
        let _e289: vec3<f32> = global.material_specular;
        local_1 = vec4<f32>(max(max(_e284.x, _e286.y), _e289.z), 0.62f, 0f, 1f);
    }
    let _e297: vec4<f32> = local_1;
    sm = _e297;
    let _e299: vec4<f32> = sm;
    let _e303: vec4<f32> = sm;
    roughness = clamp(_e303.y, 0.04f, 1f);
    let _e311: vec4<f32> = sm;
    let _e315: vec4<f32> = sm;
    let _e322: vec4<f32> = sm;
    let _e326: vec4<f32> = sm;
    metallic = (smoothstep(0.45f, 0.95f, clamp(_e326.x, 0f, 1f)) * 0.75f);
    let _e335: vec3<f32> = global.material_emission;
    let _e337: vec3<f32> = global.material_emission;
    let _e339: vec3<f32> = global.material_emission;
    let _e341: vec3<f32> = global.material_emission;
    let _e344: vec3<f32> = global.material_emission;
    let _e346: vec3<f32> = global.material_emission;
    let _e348: vec3<f32> = global.material_emission;
    let _e350: vec3<f32> = global.material_emission;
    let _e352: vec3<f32> = global.material_emission;
    let _e355: vec3<f32> = global.material_emission;
    emissive = max(max(_e350.x, _e352.y), _e355.z);
    let _e359: i32 = global._prime_use_emissive_map;
    if (_e359 != 0i) {
        {
            let _e363: vec2<f32> = texcoord_1;
            let _e364: vec4<f32> = prime_sample_emissive_tex(_e363);
            e = _e364.xyz;
            let _e373: vec3<f32> = e;
            let _e379: f32 = emissive;
            let _e385: vec3<f32> = e;
            emissive = max(_e379, dot(_e385, vec3<f32>(0.2126f, 0.7152f, 0.0722f)));
        }
    }
    let _e392: i32 = global.cosmetic_skin;
    let _e395: i32 = global._prime_use_specular_map;
    if ((_e392 != 0i) && !((_e395 != 0i))) {
        {
            let _e400: vec2<f32> = cosmetic_finish();
            finish_1 = _e400;
            let _e402: vec2<f32> = finish_1;
            metallic = _e402.x;
            let _e404: vec2<f32> = finish_1;
            roughness = _e404.y;
        }
    }
    let _e406: i32 = global.cosmetic_skin;
    if (_e406 == 4i) {
        let _e410: f32 = cosmetic_circuit();
        let _e413: f32 = emissive;
        let _e414: f32 = cosmetic_circuit();
        emissive = max(_e413, (_e414 * 0.55f));
    }
    let _e418: f32 = metallic;
    let _e419: f32 = roughness;
    let _e423: f32 = emissive;
    prime_output = vec4<f32>(_e418, _e419, clamp(_e423, 0f, 1f), 1f);
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
