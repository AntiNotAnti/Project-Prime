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
    var n: vec3<f32>;
    var viewNormal: vec3<f32>;
    var toEye: vec3<f32>;
    var rim: f32;
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
    let _e208: i32 = global.cosmetic_skin;
    if (_e208 == 1i) {
        {
            let _e211: vec4<f32> = (*col);
            let _e213: vec4<f32> = (*col);
            let _e220: f32 = lum;
            let _e224: vec4<f32> = (*col);
            let _e231: f32 = lum;
            let _e236: vec3<f32> = mix(_e224.xyz, (vec3<f32>(0.2f, 0.23f, 0.28f) * (0.65f + _e231)), vec3(0.88f));
            (*col).x = _e236.x;
            (*col).y = _e236.y;
            (*col).z = _e236.z;
        }
    } else {
        let _e243: i32 = global.cosmetic_skin;
        if (_e243 == 2i) {
            {
                let _e248: vec2<f32> = texcoord_1;
                let _e252: vec2<f32> = texcoord_1;
                let _e257: vec2<f32> = texcoord_1;
                let _e261: vec2<f32> = texcoord_1;
                let _e269: vec2<f32> = texcoord_1;
                let _e273: vec2<f32> = texcoord_1;
                let _e278: vec2<f32> = texcoord_1;
                let _e282: vec2<f32> = texcoord_1;
                etch = smoothstep(0.92f, 0.99f, (sin((_e273.x * 85f)) * sin((_e282.y * 85f))));
                let _e290: vec4<f32> = (*col);
                let _e292: vec4<f32> = (*col);
                let _e299: f32 = lum;
                let _e303: vec4<f32> = (*col);
                let _e310: f32 = lum;
                let _e315: vec3<f32> = mix(_e303.xyz, (vec3<f32>(0.76f, 0.58f, 0.28f) * (0.45f + _e310)), vec3(0.82f));
                (*col).x = _e315.x;
                (*col).y = _e315.y;
                (*col).z = _e315.z;
                let _e322: vec4<f32> = (*col);
                let _e324: vec4<f32> = (*col);
                let _e326: f32 = etch;
                let _e332: vec3<f32> = (_e324.xyz + (_e326 * vec3<f32>(0.08f, 0.32f, 0.28f)));
                (*col).x = _e332.x;
                (*col).y = _e332.y;
                (*col).z = _e332.z;
            }
        }
    }
    let _e339: i32 = global.cosmetic_skin;
    if (_e339 == 3i) {
        {
            let _e342: vec2<f32> = texcoord_1;
            let _e345: vec2<f32> = texcoord_1;
            panel = fract((_e345 * 12f));
            let _e351: vec2<f32> = panel;
            let _e354: vec2<f32> = panel;
            let _e358: vec2<f32> = panel;
            let _e361: vec2<f32> = panel;
            seam = (step(0.06f, _e354.x) * step(0.06f, _e361.y));
            let _e366: vec4<f32> = (*col);
            let _e385: f32 = seam;
            let _e389: f32 = lum;
            let _e393: vec3<f32> = (mix(vec3<f32>(0.1f, 0.14f, 0.19f), vec3<f32>(0.88f, 0.9f, 0.85f), vec3(_e385)) * (0.5f + (_e389 * 0.6f)));
            (*col).x = _e393.x;
            (*col).y = _e393.y;
            (*col).z = _e393.z;
        }
    } else {
        let _e400: i32 = global.cosmetic_skin;
        if (_e400 == 4i) {
            {
                let _e403: vec2<f32> = texcoord_1;
                let _e406: vec2<f32> = texcoord_1;
                grid = fract((_e406 * 18f));
                let _e413: vec2<f32> = grid;
                let _e415: vec2<f32> = grid;
                let _e417: vec2<f32> = grid;
                let _e419: vec2<f32> = grid;
                let _e423: vec2<f32> = grid;
                let _e425: vec2<f32> = grid;
                let _e427: vec2<f32> = grid;
                let _e429: vec2<f32> = grid;
                trace = (1f - step(0.08f, min(_e427.x, _e429.y)));
                let _e437: vec2<f32> = grid;
                let _e441: vec2<f32> = grid;
                let _e447: vec2<f32> = grid;
                let _e451: vec2<f32> = grid;
                node = (1f - step(0.17f, length((_e451 - vec2(0.15f)))));
                let _e459: vec4<f32> = (*col);
                let _e466: f32 = lum;
                let _e473: f32 = trace;
                let _e477: f32 = trace;
                let _e480: f32 = node;
                let _e483: vec3<f32> = ((vec3<f32>(0.09f, 0.14f, 0.18f) * (0.6f + _e466)) + (vec3<f32>(0.05f, 0.65f, 0.55f) * max((_e477 * 0.55f), _e480)));
                (*col).x = _e483.x;
                (*col).y = _e483.y;
                (*col).z = _e483.z;
            }
        } else {
            let _e490: i32 = global.cosmetic_skin;
            if (_e490 == 5i) {
                {
                    let _e495: vec2<f32> = texcoord_1;
                    let _e499: vec2<f32> = texcoord_1;
                    let _e504: vec2<f32> = texcoord_1;
                    let _e508: vec2<f32> = texcoord_1;
                    let _e516: vec2<f32> = texcoord_1;
                    let _e520: vec2<f32> = texcoord_1;
                    let _e525: vec2<f32> = texcoord_1;
                    let _e529: vec2<f32> = texcoord_1;
                    let _e540: vec2<f32> = texcoord_1;
                    let _e544: vec2<f32> = texcoord_1;
                    let _e549: vec2<f32> = texcoord_1;
                    let _e553: vec2<f32> = texcoord_1;
                    let _e561: vec2<f32> = texcoord_1;
                    let _e565: vec2<f32> = texcoord_1;
                    let _e570: vec2<f32> = texcoord_1;
                    let _e574: vec2<f32> = texcoord_1;
                    stripe = smoothstep(0.35f, 0.45f, sin((((_e561.x * 65f) + (_e565.y * 38f)) + (sin((_e574.y * 25f)) * 2f))));
                    let _e585: vec4<f32> = (*col);
                    let _e604: f32 = stripe;
                    let _e608: f32 = lum;
                    let _e610: vec3<f32> = (mix(vec3<f32>(0.85f, 0.4f, 0.07f), vec3<f32>(0.08f, 0.07f, 0.09f), vec3(_e604)) * (0.55f + _e608));
                    (*col).x = _e610.x;
                    (*col).y = _e610.y;
                    (*col).z = _e610.z;
                }
            } else {
                let _e617: i32 = global.cosmetic_skin;
                if (_e617 == 6i) {
                    {
                        let _e622: vec2<f32> = texcoord_1;
                        let _e626: vec2<f32> = texcoord_1;
                        let _e630: vec2<f32> = texcoord_1;
                        let _e638: vec2<f32> = texcoord_1;
                        let _e642: vec2<f32> = texcoord_1;
                        let _e646: vec2<f32> = texcoord_1;
                        cloud = (0.5f + (0.5f * sin(((_e638.x * 17f) + (sin((_e646.y * 23f)) * 2f)))));
                        let _e659: vec2<f32> = texcoord_1;
                        let _e662: vec2<f32> = texcoord_1;
                        let _e666: vec2<f32> = texcoord_1;
                        let _e669: vec2<f32> = texcoord_1;
                        let _e673: f32 = cosmetic_noise(floor((_e669 * 100f)));
                        let _e675: vec2<f32> = texcoord_1;
                        let _e678: vec2<f32> = texcoord_1;
                        let _e682: vec2<f32> = texcoord_1;
                        let _e685: vec2<f32> = texcoord_1;
                        let _e689: f32 = cosmetic_noise(floor((_e685 * 100f)));
                        star = step(0.985f, _e689);
                        let _e692: vec4<f32> = (*col);
                        let _e711: f32 = cloud;
                        let _e715: f32 = lum;
                        let _e718: f32 = star;
                        let _e722: vec3<f32> = ((mix(vec3<f32>(0.1f, 0.12f, 0.32f), vec3<f32>(0.48f, 0.16f, 0.56f), vec3(_e711)) * (0.5f + _e715)) + vec3((_e718 * 0.65f)));
                        (*col).x = _e722.x;
                        (*col).y = _e722.y;
                        (*col).z = _e722.z;
                    }
                }
            }
        }
    }
    let _e729: i32 = global.cosmetic_preserve_palette;
    let _e732: i32 = global.cosmetic_skin;
    if ((_e729 != 0i) && (_e732 != 0i)) {
        {
            let _e736: vec3<f32> = nativeColor;
            let _e738: vec3<f32> = nativeColor;
            let _e740: vec3<f32> = nativeColor;
            let _e742: vec3<f32> = nativeColor;
            let _e744: vec3<f32> = nativeColor;
            let _e747: vec3<f32> = nativeColor;
            let _e749: vec3<f32> = nativeColor;
            let _e751: vec3<f32> = nativeColor;
            let _e753: vec3<f32> = nativeColor;
            let _e755: vec3<f32> = nativeColor;
            let _e759: vec3<f32> = nativeColor;
            let _e761: vec3<f32> = nativeColor;
            let _e763: vec3<f32> = nativeColor;
            let _e765: vec3<f32> = nativeColor;
            let _e767: vec3<f32> = nativeColor;
            let _e770: vec3<f32> = nativeColor;
            let _e772: vec3<f32> = nativeColor;
            let _e774: vec3<f32> = nativeColor;
            let _e776: vec3<f32> = nativeColor;
            let _e778: vec3<f32> = nativeColor;
            chroma = (max(_e747.x, max(_e753.y, _e755.z)) - min(_e770.x, min(_e776.y, _e778.z)));
            let _e784: vec4<f32> = (*col);
            let _e786: vec4<f32> = (*col);
            let _e794: f32 = chroma;
            let _e798: vec4<f32> = (*col);
            let _e800: vec3<f32> = nativeColor;
            let _e806: f32 = chroma;
            let _e811: vec3<f32> = mix(_e798.xyz, _e800, vec3((smoothstep(0.1f, 0.35f, _e806) * 0.9f)));
            (*col).x = _e811.x;
            (*col).y = _e811.y;
            (*col).z = _e811.z;
        }
    }
    let _e818: i32 = global.cosmetic_effect;
    if (_e818 != 0i) {
        {
            let _e821: f32 = global.cosmetic_time;
            let _e822: f32 = global.cosmetic_scroll;
            t = (_e821 * _e822);
            let _e825: vec3<f32> = surface_normal_1;
            let _e828: vec3<f32> = surface_normal_1;
            let _e829: vec3<f32> = surface_normal_1;
            let _e834: vec3<f32> = surface_normal_1;
            let _e835: vec3<f32> = surface_normal_1;
            let _e841: vec3<f32> = surface_normal_1;
            let _e842: vec3<f32> = surface_normal_1;
            let _e847: vec3<f32> = surface_normal_1;
            let _e848: vec3<f32> = surface_normal_1;
            n = (_e825 * inverseSqrt(max(dot(_e847, _e848), 0.0001f)));
            let _e855: mat4x4<f32> = global.view_mtx;
            let _e865: vec3<f32> = n;
            viewNormal = (mat3x3<f32>(_e855[0].xyz, _e855[1].xyz, _e855[2].xyz) * _e865);
            let _e868: mat4x4<f32> = global.view_mtx;
            let _e869: vec3<f32> = surface_position_1;
            toEye = -((_e868 * vec4<f32>(_e869.x, _e869.y, _e869.z, 1f)).xyz);
            let _e879: vec3<f32> = toEye;
            let _e882: vec3<f32> = toEye;
            let _e883: vec3<f32> = toEye;
            let _e888: vec3<f32> = toEye;
            let _e889: vec3<f32> = toEye;
            let _e895: vec3<f32> = toEye;
            let _e896: vec3<f32> = toEye;
            let _e901: vec3<f32> = toEye;
            let _e902: vec3<f32> = toEye;
            toEye = (_e879 * inverseSqrt(max(dot(_e901, _e902), 0.0001f)));
            let _e911: vec3<f32> = viewNormal;
            let _e912: vec3<f32> = toEye;
            let _e916: vec3<f32> = viewNormal;
            let _e917: vec3<f32> = toEye;
            let _e926: vec3<f32> = viewNormal;
            let _e927: vec3<f32> = toEye;
            let _e931: vec3<f32> = viewNormal;
            let _e932: vec3<f32> = toEye;
            let _e943: vec3<f32> = viewNormal;
            let _e944: vec3<f32> = toEye;
            let _e948: vec3<f32> = viewNormal;
            let _e949: vec3<f32> = toEye;
            let _e958: vec3<f32> = viewNormal;
            let _e959: vec3<f32> = toEye;
            let _e963: vec3<f32> = viewNormal;
            let _e964: vec3<f32> = toEye;
            rim = pow(clamp((1f - abs(dot(_e963, _e964))), 0f, 1f), 1.6f);
            let _e976: f32 = global.cosmetic_time;
            let _e977: f32 = global.cosmetic_pulse;
            let _e979: f32 = global.cosmetic_time;
            let _e980: f32 = global.cosmetic_pulse;
            pulse = (0.82f + (0.18f * sin((_e979 * _e980))));
            let _e988: vec2<f32> = texcoord_1;
            let _e992: f32 = t;
            let _e996: vec2<f32> = texcoord_1;
            let _e1000: f32 = t;
            wave = (0.5f + (0.5f * sin(((_e996.y * 30f) - (_e1000 * 2f)))));
            let _e1009: f32 = rim;
            mask = (0.28f + (_e1009 * 0.72f));
            let _e1014: i32 = global.cosmetic_effect;
            if (_e1014 == 1i) {
                let _e1019: f32 = pulse;
                mask = (0.25f + (0.55f * _e1019));
            }
            let _e1022: i32 = global.cosmetic_effect;
            if (_e1022 == 3i) {
                let _e1030: f32 = wave;
                let _e1034: f32 = rim;
                mask = ((smoothstep(0.78f, 0.95f, _e1030) * 0.5f) + _e1034);
            }
            let _e1036: i32 = global.cosmetic_effect;
            if (_e1036 == 4i) {
                let _e1039: vec2<f32> = texcoord_1;
                let _e1042: vec2<f32> = texcoord_1;
                let _e1046: f32 = t;
                let _e1049: f32 = t;
                let _e1055: vec2<f32> = texcoord_1;
                let _e1058: vec2<f32> = texcoord_1;
                let _e1062: f32 = t;
                let _e1065: f32 = t;
                let _e1071: f32 = cosmetic_noise((floor((_e1058 * 24f)) + vec2(floor((_e1065 * 3f)))));
                let _e1074: f32 = rim;
                mask = ((_e1071 * 0.3f) + _e1074);
            }
            let _e1076: i32 = global.cosmetic_effect;
            let _e1079: i32 = global.cosmetic_effect;
            if ((_e1076 == 5i) || (_e1079 == 7i)) {
                let _e1089: f32 = wave;
                let _e1094: f32 = rim;
                mask = ((0.12f + (smoothstep(0.65f, 0.95f, _e1089) * 0.65f)) + (_e1094 * 0.45f));
            }
            let _e1098: i32 = global.cosmetic_effect;
            if (_e1098 == 6i) {
                let _e1102: vec2<f32> = texcoord_1;
                let _e1106: vec2<f32> = texcoord_1;
                let _e1110: f32 = t;
                let _e1112: vec2<f32> = texcoord_1;
                let _e1116: f32 = t;
                let _e1120: vec2<f32> = texcoord_1;
                let _e1124: vec2<f32> = texcoord_1;
                let _e1128: f32 = t;
                let _e1130: vec2<f32> = texcoord_1;
                let _e1134: f32 = t;
                let _e1139: vec2<f32> = texcoord_1;
                let _e1143: vec2<f32> = texcoord_1;
                let _e1147: f32 = t;
                let _e1149: vec2<f32> = texcoord_1;
                let _e1153: f32 = t;
                let _e1157: vec2<f32> = texcoord_1;
                let _e1161: vec2<f32> = texcoord_1;
                let _e1165: f32 = t;
                let _e1167: vec2<f32> = texcoord_1;
                let _e1171: f32 = t;
                let _e1178: vec2<f32> = texcoord_1;
                let _e1182: vec2<f32> = texcoord_1;
                let _e1186: f32 = t;
                let _e1188: vec2<f32> = texcoord_1;
                let _e1192: f32 = t;
                let _e1196: vec2<f32> = texcoord_1;
                let _e1200: vec2<f32> = texcoord_1;
                let _e1204: f32 = t;
                let _e1206: vec2<f32> = texcoord_1;
                let _e1210: f32 = t;
                let _e1215: vec2<f32> = texcoord_1;
                let _e1219: vec2<f32> = texcoord_1;
                let _e1223: f32 = t;
                let _e1225: vec2<f32> = texcoord_1;
                let _e1229: f32 = t;
                let _e1233: vec2<f32> = texcoord_1;
                let _e1237: vec2<f32> = texcoord_1;
                let _e1241: f32 = t;
                let _e1243: vec2<f32> = texcoord_1;
                let _e1247: f32 = t;
                let _e1256: f32 = rim;
                mask = ((0.12f + pow(abs(sin(((_e1233.x * 31f) + sin(((_e1243.y * 29f) + _e1247))))), 16f)) + (_e1256 * 0.5f));
            }
            let _e1260: i32 = global.cosmetic_effect;
            if (_e1260 == 8i) {
                let _e1263: f32 = wave;
                let _e1266: f32 = rim;
                mask = ((_e1263 * 0.35f) + (_e1266 * 0.7f));
            }
            let _e1270: i32 = global.cosmetic_effect;
            if (_e1270 == 9i) {
                let _e1275: vec2<f32> = texcoord_1;
                let _e1279: f32 = t;
                let _e1281: vec2<f32> = texcoord_1;
                let _e1285: f32 = t;
                let _e1290: f32 = rim;
                mask = ((0.5f + (0.5f * sin(((_e1281.x * 40f) + _e1285)))) * _e1290);
            }
            let _e1295: vec3<f32> = global.cosmetic_primary;
            let _e1296: vec3<f32> = global.cosmetic_secondary;
            let _e1297: f32 = wave;
            energy = mix(_e1295, _e1296, vec3(_e1297));
            let _e1304: f32 = mask;
            let _e1308: f32 = global.cosmetic_intensity;
            let _e1310: f32 = pulse;
            strength = ((clamp(_e1304, 0f, 1f) * _e1308) * _e1310);
            let _e1313: i32 = global.cosmetic_preserve_palette;
            if (_e1313 != 0i) {
                let _e1316: f32 = strength;
                strength = (_e1316 * 0.65f);
            }
            let _e1319: vec4<f32> = (*col);
            let _e1321: vec4<f32> = (*col);
            let _e1323: vec3<f32> = energy;
            let _e1325: f32 = lum;
            let _e1330: f32 = strength;
            let _e1335: f32 = strength;
            let _e1341: vec4<f32> = (*col);
            let _e1343: vec3<f32> = energy;
            let _e1345: f32 = lum;
            let _e1350: f32 = strength;
            let _e1355: f32 = strength;
            let _e1362: vec3<f32> = mix(_e1341.xyz, (_e1343 * (0.4f + (_e1345 * 0.6f))), vec3(clamp((_e1355 * 0.55f), 0f, 0.65f)));
            (*col).x = _e1362.x;
            (*col).y = _e1362.y;
            (*col).z = _e1362.z;
            let _e1369: vec4<f32> = (*col);
            let _e1371: vec4<f32> = (*col);
            let _e1373: vec3<f32> = energy;
            let _e1374: f32 = strength;
            let _e1378: vec3<f32> = (_e1371.xyz + ((_e1373 * _e1374) * 0.45f));
            (*col).x = _e1378.x;
            (*col).y = _e1378.y;
            (*col).z = _e1378.z;
        }
    }
    let _e1385: f32 = global.cosmetic_dissolve;
    if (_e1385 > 0f) {
        {
            let _e1388: vec2<f32> = texcoord_1;
            let _e1391: vec2<f32> = texcoord_1;
            let _e1395: vec2<f32> = texcoord_1;
            let _e1398: vec2<f32> = texcoord_1;
            let _e1402: f32 = cosmetic_noise(floor((_e1398 * 64f)));
            n_1 = _e1402;
            let _e1404: f32 = n_1;
            let _e1405: f32 = global.cosmetic_dissolve;
            if (_e1404 < _e1405) {
                discard;
            }
            let _e1407: vec4<f32> = (*col);
            let _e1409: vec4<f32> = (*col);
            let _e1411: vec3<f32> = global.cosmetic_primary;
            let _e1414: f32 = global.cosmetic_dissolve;
            let _e1418: f32 = global.cosmetic_dissolve;
            let _e1419: f32 = global.cosmetic_dissolve;
            let _e1422: f32 = n_1;
            let _e1428: vec3<f32> = (_e1409.xyz + ((_e1411 * (1f - smoothstep(_e1418, (_e1419 + 0.08f), _e1422))) * 0.3f));
            (*col).x = _e1428.x;
            (*col).y = _e1428.y;
            (*col).z = _e1428.z;
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
    let _e224: i32 = global.gbuffer_mode;
    if (_e224 == 1i) {
        {
            let _e230: vec3<f32> = albedo;
            let _e235: vec3<f32> = clamp(_e230, vec3(0f), vec3(1f));
            prime_output = vec4<f32>(_e235.x, _e235.y, _e235.z, 1f);
            return;
        }
    }
    let _e241: i32 = global.gbuffer_mode;
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
    let _e384: f32 = metallic;
    let _e385: f32 = roughness;
    let _e389: f32 = emissive;
    prime_output = vec4<f32>(_e384, _e385, clamp(_e389, 0f, 1f), 1f);
    return;
}

@fragment
fn main(@location(0) texcoord: vec2<f32>, @location(1) vertex_color: vec4<f32>, @location(2) surface_normal: vec3<f32>, @location(3) surface_position: vec3<f32>) -> FragmentOutput {
    texcoord_1 = texcoord;
    vertex_color_1 = vertex_color;
    surface_normal_1 = surface_normal;
    surface_position_1 = surface_position;
    main_1();
    let _e201: vec4<f32> = prime_output;
    return FragmentOutput(_e201);
}
