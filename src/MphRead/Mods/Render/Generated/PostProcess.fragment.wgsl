struct PrimeUniforms {
    texel: vec2<f32>,
    _pad_16_0_: f32,
    _pad_16_1_: f32,
    near_plane: f32,
    _pad_32_0_: f32,
    _pad_32_1_: f32,
    _pad_32_2_: f32,
    far_plane: f32,
    _pad_48_0_: f32,
    _pad_48_1_: f32,
    _pad_48_2_: f32,
    depth_available: i32,
    _pad_64_0_: f32,
    _pad_64_1_: f32,
    _pad_64_2_: f32,
    aa_mode: i32,
    _pad_80_0_: f32,
    _pad_80_1_: f32,
    _pad_80_2_: f32,
    sharpen_strength: f32,
    _pad_96_0_: f32,
    _pad_96_1_: f32,
    _pad_96_2_: f32,
    bloom_enable: i32,
    _pad_112_0_: f32,
    _pad_112_1_: f32,
    _pad_112_2_: f32,
    bloom_intensity: f32,
    _pad_128_0_: f32,
    _pad_128_1_: f32,
    _pad_128_2_: f32,
    grade_mode: i32,
    _pad_144_0_: f32,
    _pad_144_1_: f32,
    _pad_144_2_: f32,
    gamma_value: f32,
    _pad_160_0_: f32,
    _pad_160_1_: f32,
    _pad_160_2_: f32,
    contrast_value: f32,
    _pad_176_0_: f32,
    _pad_176_1_: f32,
    _pad_176_2_: f32,
    saturation_value: f32,
    _pad_192_0_: f32,
    _pad_192_1_: f32,
    _pad_192_2_: f32,
    enhanced_lighting: i32,
    _pad_208_0_: f32,
    _pad_208_1_: f32,
    _pad_208_2_: f32,
    ao_quality: i32,
    _pad_224_0_: f32,
    _pad_224_1_: f32,
    _pad_224_2_: f32,
    contact_shadows: i32,
    _pad_240_0_: f32,
    _pad_240_1_: f32,
    _pad_240_2_: f32,
    enhanced_fog: i32,
    _pad_256_0_: f32,
    _pad_256_1_: f32,
    _pad_256_2_: f32,
    volumetric_fog: i32,
    _pad_272_0_: f32,
    _pad_272_1_: f32,
    _pad_272_2_: f32,
    hdr_mode: i32,
    _pad_288_0_: f32,
    _pad_288_1_: f32,
    _pad_288_2_: f32,
    reflections: i32,
    _pad_304_0_: f32,
    _pad_304_1_: f32,
    _pad_304_2_: f32,
    dynamic_glow: i32,
    _pad_320_0_: f32,
    _pad_320_1_: f32,
    _pad_320_2_: f32,
    fog_color: vec4<f32>,
    time_value: f32,
    _pad_352_0_: f32,
    _pad_352_1_: f32,
    _pad_352_2_: f32,
    inv_projection: mat4x4<f32>,
    inv_view: mat4x4<f32>,
    view_matrix: mat4x4<f32>,
    previous_view_projection: mat4x4<f32>,
    history_valid: i32,
    _pad_624_0_: f32,
    _pad_624_1_: f32,
    _pad_624_2_: f32,
    projection: mat4x4<f32>,
    camera_position: vec3<f32>,
    _pad_704_0_: f32,
    shadow_enabled: i32,
    _pad_720_0_: f32,
    _pad_720_1_: f32,
    _pad_720_2_: f32,
    shadow_view: mat4x4<f32>,
    shadow_projection: mat4x4<f32>,
    shadow_texel: vec2<f32>,
    _pad_864_0_: f32,
    _pad_864_1_: f32,
    shadow_light_dir: vec3<f32>,
    _pad_880_0_: f32,
    pbr_enabled: i32,
    _pad_896_0_: f32,
    _pad_896_1_: f32,
    _pad_896_2_: f32,
    pbr_light1_dir: vec3<f32>,
    _pad_912_0_: f32,
    pbr_light1_color: vec3<f32>,
    _pad_928_0_: f32,
    pbr_light2_dir: vec3<f32>,
    _pad_944_0_: f32,
    pbr_light2_color: vec3<f32>,
    _pad_960_0_: f32,
    dynamic_light_count: i32,
    _pad_976_0_: f32,
    _pad_976_1_: f32,
    _pad_976_2_: f32,
    dynamic_light_pos: array<vec4<f32>, 8>,
    dynamic_light_color: array<vec4<f32>, 8>,
    prime_viewport: vec4<f32>,
    prime_texture_flip: array<vec4<f32>, 7>,
}

struct FragmentOutput {
    @location(0) prime_output: vec4<f32>,
}

@group(0) @binding(0)
var<uniform> global: PrimeUniforms;
@group(0) @binding(1)
var prime_tex_depth_tex: texture_depth_2d;
@group(0) @binding(2)
var prime_sampler_depth_tex: sampler;
@group(0) @binding(3)
var prime_tex_shadow_tex: texture_depth_2d;
@group(0) @binding(4)
var prime_sampler_shadow_tex: sampler;
@group(0) @binding(5)
var prime_tex_history_tex: texture_2d<f32>;
@group(0) @binding(6)
var prime_sampler_history_tex: sampler;
@group(0) @binding(7)
var prime_tex_pbr_albedo: texture_2d<f32>;
@group(0) @binding(8)
var prime_sampler_pbr_albedo: sampler;
@group(0) @binding(9)
var prime_tex_pbr_normal: texture_2d<f32>;
@group(0) @binding(10)
var prime_sampler_pbr_normal: sampler;
@group(0) @binding(11)
var prime_tex_pbr_material: texture_2d<f32>;
@group(0) @binding(12)
var prime_sampler_pbr_material: sampler;
@group(0) @binding(13)
var prime_tex_tex: texture_2d<f32>;
@group(0) @binding(14)
var prime_sampler_tex: sampler;
var<private> prime_output: vec4<f32>;
var<private> texcoord_1: vec2<f32>;

fn prime_sample_depth_tex(uv: vec2<f32>) -> vec4<f32> {
    let dims = vec2<i32>(textureDimensions(prime_tex_depth_tex));
    let p = clamp(vec2<i32>(vec2<f32>(uv.x, 1.0-uv.y) * vec2<f32>(dims)), vec2<i32>(0), dims-vec2<i32>(1));
    return vec4<f32>(textureLoad(prime_tex_depth_tex, p, 0));
}

fn prime_sample_shadow_tex(uv: vec2<f32>) -> vec4<f32> {
    let dims = vec2<i32>(textureDimensions(prime_tex_shadow_tex));
    let p = clamp(vec2<i32>(vec2<f32>(uv.x, 1.0-uv.y) * vec2<f32>(dims)), vec2<i32>(0), dims-vec2<i32>(1));
    return vec4<f32>(textureLoad(prime_tex_shadow_tex, p, 0));
}

fn prime_sample_history_tex(uv_4: vec2<f32>) -> vec4<f32> {
    var uv_5: vec2<f32>;

    uv_5 = uv_4;
    let _e260: vec2<f32> = uv_5;
    let _e262: vec2<f32> = uv_5;
    let _e265: vec2<f32> = uv_5;
    let _e270: vec4<f32> = global.prime_texture_flip[2];
    let _e272: vec2<f32> = uv_5;
    let _e275: vec2<f32> = uv_5;
    let _e280: vec4<f32> = global.prime_texture_flip[2];
    let _e285: vec2<f32> = uv_5;
    let _e287: vec2<f32> = uv_5;
    let _e290: vec2<f32> = uv_5;
    let _e295: vec4<f32> = global.prime_texture_flip[2];
    let _e297: vec2<f32> = uv_5;
    let _e300: vec2<f32> = uv_5;
    let _e305: vec4<f32> = global.prime_texture_flip[2];
    let _e310: vec4<f32> = textureSampleLevel(prime_tex_history_tex, prime_sampler_history_tex, vec2<f32>(_e285.x, mix(_e297.y, (1f - _e300.y), _e305.x)), 0f);
    return _e310;
}

fn prime_sample_pbr_albedo(uv_6: vec2<f32>) -> vec4<f32> {
    var uv_7: vec2<f32>;

    uv_7 = uv_6;
    let _e262: vec2<f32> = uv_7;
    let _e264: vec2<f32> = uv_7;
    let _e267: vec2<f32> = uv_7;
    let _e272: vec4<f32> = global.prime_texture_flip[3];
    let _e274: vec2<f32> = uv_7;
    let _e277: vec2<f32> = uv_7;
    let _e282: vec4<f32> = global.prime_texture_flip[3];
    let _e287: vec2<f32> = uv_7;
    let _e289: vec2<f32> = uv_7;
    let _e292: vec2<f32> = uv_7;
    let _e297: vec4<f32> = global.prime_texture_flip[3];
    let _e299: vec2<f32> = uv_7;
    let _e302: vec2<f32> = uv_7;
    let _e307: vec4<f32> = global.prime_texture_flip[3];
    let _e312: vec4<f32> = textureSampleLevel(prime_tex_pbr_albedo, prime_sampler_pbr_albedo, vec2<f32>(_e287.x, mix(_e299.y, (1f - _e302.y), _e307.x)), 0f);
    return _e312;
}

fn prime_sample_pbr_normal(uv_8: vec2<f32>) -> vec4<f32> {
    var uv_9: vec2<f32>;

    uv_9 = uv_8;
    let _e264: vec2<f32> = uv_9;
    let _e266: vec2<f32> = uv_9;
    let _e269: vec2<f32> = uv_9;
    let _e274: vec4<f32> = global.prime_texture_flip[4];
    let _e276: vec2<f32> = uv_9;
    let _e279: vec2<f32> = uv_9;
    let _e284: vec4<f32> = global.prime_texture_flip[4];
    let _e289: vec2<f32> = uv_9;
    let _e291: vec2<f32> = uv_9;
    let _e294: vec2<f32> = uv_9;
    let _e299: vec4<f32> = global.prime_texture_flip[4];
    let _e301: vec2<f32> = uv_9;
    let _e304: vec2<f32> = uv_9;
    let _e309: vec4<f32> = global.prime_texture_flip[4];
    let _e314: vec4<f32> = textureSampleLevel(prime_tex_pbr_normal, prime_sampler_pbr_normal, vec2<f32>(_e289.x, mix(_e301.y, (1f - _e304.y), _e309.x)), 0f);
    return _e314;
}

fn prime_sample_pbr_material(uv_10: vec2<f32>) -> vec4<f32> {
    var uv_11: vec2<f32>;

    uv_11 = uv_10;
    let _e266: vec2<f32> = uv_11;
    let _e268: vec2<f32> = uv_11;
    let _e271: vec2<f32> = uv_11;
    let _e276: vec4<f32> = global.prime_texture_flip[5];
    let _e278: vec2<f32> = uv_11;
    let _e281: vec2<f32> = uv_11;
    let _e286: vec4<f32> = global.prime_texture_flip[5];
    let _e291: vec2<f32> = uv_11;
    let _e293: vec2<f32> = uv_11;
    let _e296: vec2<f32> = uv_11;
    let _e301: vec4<f32> = global.prime_texture_flip[5];
    let _e303: vec2<f32> = uv_11;
    let _e306: vec2<f32> = uv_11;
    let _e311: vec4<f32> = global.prime_texture_flip[5];
    let _e316: vec4<f32> = textureSampleLevel(prime_tex_pbr_material, prime_sampler_pbr_material, vec2<f32>(_e291.x, mix(_e303.y, (1f - _e306.y), _e311.x)), 0f);
    return _e316;
}

fn prime_sample_tex(uv_12: vec2<f32>) -> vec4<f32> {
    var uv_13: vec2<f32>;

    uv_13 = uv_12;
    let _e268: vec2<f32> = uv_13;
    let _e270: vec2<f32> = uv_13;
    let _e273: vec2<f32> = uv_13;
    let _e278: vec4<f32> = global.prime_texture_flip[6];
    let _e280: vec2<f32> = uv_13;
    let _e283: vec2<f32> = uv_13;
    let _e288: vec4<f32> = global.prime_texture_flip[6];
    let _e293: vec2<f32> = uv_13;
    let _e295: vec2<f32> = uv_13;
    let _e298: vec2<f32> = uv_13;
    let _e303: vec4<f32> = global.prime_texture_flip[6];
    let _e305: vec2<f32> = uv_13;
    let _e308: vec2<f32> = uv_13;
    let _e313: vec4<f32> = global.prime_texture_flip[6];
    let _e318: vec4<f32> = textureSampleLevel(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e293.x, mix(_e305.y, (1f - _e308.y), _e313.x)), 0f);
    return _e318;
}

fn uv_clamp(uv_14: vec2<f32>) -> vec2<f32> {
    var uv_15: vec2<f32>;

    uv_15 = uv_14;
    let _e275: vec2<f32> = uv_15;
    return clamp(_e275, vec2(0f), vec2(1f));
}

fn scene(uv_16: vec2<f32>) -> vec3<f32> {
    var uv_17: vec2<f32>;

    uv_17 = uv_16;
    let _e271: vec2<f32> = uv_17;
    let _e272: vec2<f32> = uv_clamp(_e271);
    let _e274: vec2<f32> = uv_17;
    let _e275: vec2<f32> = uv_clamp(_e274);
    let _e276: vec4<f32> = prime_sample_tex(_e275);
    return _e276.xyz;
}

fn luma(c: vec3<f32>) -> f32 {
    var c_1: vec3<f32>;

    c_1 = c;
    let _e275: vec3<f32> = c_1;
    return dot(_e275, vec3<f32>(0.2126f, 0.7152f, 0.0722f));
}

fn raw_depth(uv_18: vec2<f32>) -> f32 {
    var uv_19: vec2<f32>;

    uv_19 = uv_18;
    let _e270: i32 = global.depth_available;
    if (_e270 == 0i) {
        return 1f;
    }
    let _e275: vec2<f32> = uv_19;
    let _e276: vec2<f32> = uv_clamp(_e275);
    let _e278: vec2<f32> = uv_19;
    let _e279: vec2<f32> = uv_clamp(_e278);
    let _e280: vec4<f32> = prime_sample_depth_tex(_e279);
    return _e280.x;
}

fn view_depth(d: f32) -> f32 {
    var d_1: f32;
    var n: f32;
    var f: f32;
    var z: f32;

    d_1 = d;
    let _e272: f32 = global.near_plane;
    n = max(_e272, 0.0001f);
    let _e277: f32 = n;
    let _e280: f32 = global.far_plane;
    let _e281: f32 = n;
    f = max(_e280, (_e281 + 0.01f));
    let _e286: f32 = d_1;
    z = ((_e286 * 2f) - 1f);
    let _e293: f32 = n;
    let _e295: f32 = f;
    let _e298: f32 = f;
    let _e299: f32 = n;
    let _e301: f32 = z;
    let _e302: f32 = f;
    let _e303: f32 = n;
    let _e308: f32 = f;
    let _e309: f32 = n;
    let _e311: f32 = z;
    let _e312: f32 = f;
    let _e313: f32 = n;
    return (((2f * _e293) * _e295) / max(0.0001f, ((_e308 + _e309) - (_e311 * (_e312 - _e313)))));
}

fn smaa_resolve(uv_20: vec2<f32>, center: vec3<f32>) -> vec3<f32> {
    var uv_21: vec2<f32>;
    var center_1: vec3<f32>;
    var m: f32;
    var cn: vec3<f32>;
    var cs: vec3<f32>;
    var ce: vec3<f32>;
    var cw: vec3<f32>;
    var n_1: f32;
    var s: f32;
    var e: f32;
    var w: f32;
    var edgeX: f32;
    var edgeY: f32;
    var localMin: f32;
    var localMax: f32;
    var contrast: f32;
    var local: vec2<f32>;
    var axis: vec2<f32>;
    var a: vec3<f32>;
    var b: vec3<f32>;
    var c_2: vec3<f32>;
    var d_2: vec3<f32>;
    var subpixel: f32;
    var blend: f32;
    var morphological: vec3<f32>;

    uv_21 = uv_20;
    center_1 = center;
    let _e273: vec3<f32> = center_1;
    let _e274: f32 = luma(_e273);
    m = _e274;
    let _e276: vec2<f32> = uv_21;
    let _e278: vec2<f32> = global.texel;
    let _e282: vec2<f32> = uv_21;
    let _e284: vec2<f32> = global.texel;
    let _e288: vec3<f32> = scene((_e282 + vec2<f32>(0f, _e284.y)));
    cn = _e288;
    let _e290: vec2<f32> = uv_21;
    let _e292: vec2<f32> = global.texel;
    let _e296: vec2<f32> = uv_21;
    let _e298: vec2<f32> = global.texel;
    let _e302: vec3<f32> = scene((_e296 - vec2<f32>(0f, _e298.y)));
    cs = _e302;
    let _e304: vec2<f32> = uv_21;
    let _e305: vec2<f32> = global.texel;
    let _e310: vec2<f32> = uv_21;
    let _e311: vec2<f32> = global.texel;
    let _e316: vec3<f32> = scene((_e310 + vec2<f32>(_e311.x, 0f)));
    ce = _e316;
    let _e318: vec2<f32> = uv_21;
    let _e319: vec2<f32> = global.texel;
    let _e324: vec2<f32> = uv_21;
    let _e325: vec2<f32> = global.texel;
    let _e330: vec3<f32> = scene((_e324 - vec2<f32>(_e325.x, 0f)));
    cw = _e330;
    let _e333: vec3<f32> = cn;
    let _e334: f32 = luma(_e333);
    n_1 = _e334;
    let _e337: vec3<f32> = cs;
    let _e338: f32 = luma(_e337);
    s = _e338;
    let _e341: vec3<f32> = ce;
    let _e342: f32 = luma(_e341);
    e = _e342;
    let _e345: vec3<f32> = cw;
    let _e346: f32 = luma(_e345);
    w = _e346;
    let _e348: f32 = w;
    let _e349: f32 = e;
    let _e351: f32 = w;
    let _e352: f32 = e;
    edgeX = abs((_e351 - _e352));
    let _e356: f32 = n_1;
    let _e357: f32 = s;
    let _e359: f32 = n_1;
    let _e360: f32 = s;
    edgeY = abs((_e359 - _e360));
    let _e367: f32 = n_1;
    let _e368: f32 = s;
    let _e372: f32 = e;
    let _e373: f32 = w;
    let _e377: f32 = n_1;
    let _e378: f32 = s;
    let _e382: f32 = e;
    let _e383: f32 = w;
    let _e386: f32 = m;
    let _e389: f32 = n_1;
    let _e390: f32 = s;
    let _e394: f32 = e;
    let _e395: f32 = w;
    let _e399: f32 = n_1;
    let _e400: f32 = s;
    let _e404: f32 = e;
    let _e405: f32 = w;
    localMin = min(_e386, min(min(_e399, _e400), min(_e404, _e405)));
    let _e413: f32 = n_1;
    let _e414: f32 = s;
    let _e418: f32 = e;
    let _e419: f32 = w;
    let _e423: f32 = n_1;
    let _e424: f32 = s;
    let _e428: f32 = e;
    let _e429: f32 = w;
    let _e432: f32 = m;
    let _e435: f32 = n_1;
    let _e436: f32 = s;
    let _e440: f32 = e;
    let _e441: f32 = w;
    let _e445: f32 = n_1;
    let _e446: f32 = s;
    let _e450: f32 = e;
    let _e451: f32 = w;
    localMax = max(_e432, max(max(_e445, _e446), max(_e450, _e451)));
    let _e456: f32 = localMax;
    let _e457: f32 = localMin;
    contrast = (_e456 - _e457);
    let _e460: f32 = contrast;
    let _e462: f32 = localMax;
    let _e466: f32 = localMax;
    if (_e460 < max(0.025f, (_e466 * 0.055f))) {
        let _e471: vec3<f32> = center_1;
        return _e471;
    }
    let _e472: f32 = edgeX;
    let _e473: f32 = edgeY;
    if (_e472 > _e473) {
        let _e475: vec2<f32> = global.texel;
        local = vec2<f32>(_e475.x, 0f);
    } else {
        let _e480: vec2<f32> = global.texel;
        local = vec2<f32>(0f, _e480.y);
    }
    let _e484: vec2<f32> = local;
    axis = _e484;
    let _e486: vec2<f32> = uv_21;
    let _e487: vec2<f32> = axis;
    let _e491: vec2<f32> = uv_21;
    let _e492: vec2<f32> = axis;
    let _e496: vec3<f32> = scene((_e491 - (_e492 * 0.5f)));
    a = _e496;
    let _e498: vec2<f32> = uv_21;
    let _e499: vec2<f32> = axis;
    let _e503: vec2<f32> = uv_21;
    let _e504: vec2<f32> = axis;
    let _e508: vec3<f32> = scene((_e503 + (_e504 * 0.5f)));
    b = _e508;
    let _e510: vec2<f32> = uv_21;
    let _e511: vec2<f32> = axis;
    let _e515: vec2<f32> = uv_21;
    let _e516: vec2<f32> = axis;
    let _e520: vec3<f32> = scene((_e515 - (_e516 * 1.5f)));
    c_2 = _e520;
    let _e522: vec2<f32> = uv_21;
    let _e523: vec2<f32> = axis;
    let _e527: vec2<f32> = uv_21;
    let _e528: vec2<f32> = axis;
    let _e532: vec3<f32> = scene((_e527 + (_e528 * 1.5f)));
    d_2 = _e532;
    let _e534: f32 = m;
    let _e535: f32 = n_1;
    let _e536: f32 = s;
    let _e538: f32 = e;
    let _e540: f32 = w;
    let _e545: f32 = m;
    let _e546: f32 = n_1;
    let _e547: f32 = s;
    let _e549: f32 = e;
    let _e551: f32 = w;
    let _e559: f32 = contrast;
    let _e565: f32 = m;
    let _e566: f32 = n_1;
    let _e567: f32 = s;
    let _e569: f32 = e;
    let _e571: f32 = w;
    let _e576: f32 = m;
    let _e577: f32 = n_1;
    let _e578: f32 = s;
    let _e580: f32 = e;
    let _e582: f32 = w;
    let _e590: f32 = contrast;
    subpixel = clamp((abs((_e576 - ((((_e577 + _e578) + _e580) + _e582) * 0.25f))) / max(_e590, 0.0001f)), 0f, 1f);
    let _e599: f32 = subpixel;
    blend = (0.42f + (_e599 * 0.28f));
    let _e604: vec3<f32> = a;
    let _e605: vec3<f32> = b;
    let _e609: vec3<f32> = c_2;
    let _e610: vec3<f32> = d_2;
    morphological = (((_e604 + _e605) * 0.375f) + ((_e609 + _e610) * 0.125f));
    let _e619: vec3<f32> = center_1;
    let _e620: vec3<f32> = morphological;
    let _e621: f32 = blend;
    return mix(_e619, _e620, vec3(_e621));
}

fn fxaa(uv_22: vec2<f32>, center_2: vec3<f32>) -> vec3<f32> {
    var uv_23: vec2<f32>;
    var center_3: vec3<f32>;
    var m_1: f32;
    var n_2: f32;
    var s_1: f32;
    var e_1: f32;
    var w_1: f32;
    var lo: f32;
    var hi: f32;
    var edgeX_1: f32;
    var edgeY_1: f32;
    var local_1: vec2<f32>;
    var dir: vec2<f32>;
    var a_1: vec3<f32>;
    var b_1: vec3<f32>;
    var resolved: vec3<f32>;
    var c_3: vec3<f32>;
    var d_3: vec3<f32>;

    uv_23 = uv_22;
    center_3 = center_2;
    let _e272: i32 = global.aa_mode;
    if (_e272 == 0i) {
        let _e275: vec3<f32> = center_3;
        return _e275;
    }
    let _e276: i32 = global.aa_mode;
    if (_e276 == 3i) {
        let _e281: vec2<f32> = uv_23;
        let _e282: vec3<f32> = center_3;
        let _e283: vec3<f32> = smaa_resolve(_e281, _e282);
        return _e283;
    }
    let _e285: vec3<f32> = center_3;
    let _e286: f32 = luma(_e285);
    m_1 = _e286;
    let _e288: vec2<f32> = uv_23;
    let _e290: vec2<f32> = global.texel;
    let _e294: vec2<f32> = uv_23;
    let _e296: vec2<f32> = global.texel;
    let _e300: vec3<f32> = scene((_e294 + vec2<f32>(0f, _e296.y)));
    let _e301: vec2<f32> = uv_23;
    let _e303: vec2<f32> = global.texel;
    let _e307: vec2<f32> = uv_23;
    let _e309: vec2<f32> = global.texel;
    let _e313: vec3<f32> = scene((_e307 + vec2<f32>(0f, _e309.y)));
    let _e314: f32 = luma(_e313);
    n_2 = _e314;
    let _e316: vec2<f32> = uv_23;
    let _e318: vec2<f32> = global.texel;
    let _e322: vec2<f32> = uv_23;
    let _e324: vec2<f32> = global.texel;
    let _e328: vec3<f32> = scene((_e322 - vec2<f32>(0f, _e324.y)));
    let _e329: vec2<f32> = uv_23;
    let _e331: vec2<f32> = global.texel;
    let _e335: vec2<f32> = uv_23;
    let _e337: vec2<f32> = global.texel;
    let _e341: vec3<f32> = scene((_e335 - vec2<f32>(0f, _e337.y)));
    let _e342: f32 = luma(_e341);
    s_1 = _e342;
    let _e344: vec2<f32> = uv_23;
    let _e345: vec2<f32> = global.texel;
    let _e350: vec2<f32> = uv_23;
    let _e351: vec2<f32> = global.texel;
    let _e356: vec3<f32> = scene((_e350 + vec2<f32>(_e351.x, 0f)));
    let _e357: vec2<f32> = uv_23;
    let _e358: vec2<f32> = global.texel;
    let _e363: vec2<f32> = uv_23;
    let _e364: vec2<f32> = global.texel;
    let _e369: vec3<f32> = scene((_e363 + vec2<f32>(_e364.x, 0f)));
    let _e370: f32 = luma(_e369);
    e_1 = _e370;
    let _e372: vec2<f32> = uv_23;
    let _e373: vec2<f32> = global.texel;
    let _e378: vec2<f32> = uv_23;
    let _e379: vec2<f32> = global.texel;
    let _e384: vec3<f32> = scene((_e378 - vec2<f32>(_e379.x, 0f)));
    let _e385: vec2<f32> = uv_23;
    let _e386: vec2<f32> = global.texel;
    let _e391: vec2<f32> = uv_23;
    let _e392: vec2<f32> = global.texel;
    let _e397: vec3<f32> = scene((_e391 - vec2<f32>(_e392.x, 0f)));
    let _e398: f32 = luma(_e397);
    w_1 = _e398;
    let _e403: f32 = n_2;
    let _e404: f32 = s_1;
    let _e408: f32 = e_1;
    let _e409: f32 = w_1;
    let _e413: f32 = n_2;
    let _e414: f32 = s_1;
    let _e418: f32 = e_1;
    let _e419: f32 = w_1;
    let _e422: f32 = m_1;
    let _e425: f32 = n_2;
    let _e426: f32 = s_1;
    let _e430: f32 = e_1;
    let _e431: f32 = w_1;
    let _e435: f32 = n_2;
    let _e436: f32 = s_1;
    let _e440: f32 = e_1;
    let _e441: f32 = w_1;
    lo = min(_e422, min(min(_e435, _e436), min(_e440, _e441)));
    let _e449: f32 = n_2;
    let _e450: f32 = s_1;
    let _e454: f32 = e_1;
    let _e455: f32 = w_1;
    let _e459: f32 = n_2;
    let _e460: f32 = s_1;
    let _e464: f32 = e_1;
    let _e465: f32 = w_1;
    let _e468: f32 = m_1;
    let _e471: f32 = n_2;
    let _e472: f32 = s_1;
    let _e476: f32 = e_1;
    let _e477: f32 = w_1;
    let _e481: f32 = n_2;
    let _e482: f32 = s_1;
    let _e486: f32 = e_1;
    let _e487: f32 = w_1;
    hi = max(_e468, max(max(_e481, _e482), max(_e486, _e487)));
    let _e492: f32 = hi;
    let _e493: f32 = lo;
    let _e496: f32 = hi;
    let _e500: f32 = hi;
    if ((_e492 - _e493) < max(0.035f, (_e500 * 0.08f))) {
        let _e505: vec3<f32> = center_3;
        return _e505;
    }
    let _e506: f32 = w_1;
    let _e507: f32 = e_1;
    let _e509: f32 = w_1;
    let _e510: f32 = e_1;
    edgeX_1 = abs((_e509 - _e510));
    let _e514: f32 = n_2;
    let _e515: f32 = s_1;
    let _e517: f32 = n_2;
    let _e518: f32 = s_1;
    edgeY_1 = abs((_e517 - _e518));
    let _e522: f32 = edgeX_1;
    let _e523: f32 = edgeY_1;
    if (_e522 > _e523) {
        let _e526: vec2<f32> = global.texel;
        local_1 = vec2<f32>(0f, _e526.y);
    } else {
        let _e529: vec2<f32> = global.texel;
        local_1 = vec2<f32>(_e529.x, 0f);
    }
    let _e534: vec2<f32> = local_1;
    dir = _e534;
    let _e536: vec2<f32> = uv_23;
    let _e537: vec2<f32> = dir;
    let _e541: vec2<f32> = uv_23;
    let _e542: vec2<f32> = dir;
    let _e546: vec3<f32> = scene((_e541 - (_e542 * 0.5f)));
    a_1 = _e546;
    let _e548: vec2<f32> = uv_23;
    let _e549: vec2<f32> = dir;
    let _e553: vec2<f32> = uv_23;
    let _e554: vec2<f32> = dir;
    let _e558: vec3<f32> = scene((_e553 + (_e554 * 0.5f)));
    b_1 = _e558;
    let _e560: vec3<f32> = a_1;
    let _e561: vec3<f32> = b_1;
    resolved = ((_e560 + _e561) * 0.5f);
    let _e566: i32 = global.aa_mode;
    if (_e566 > 1i) {
        {
            let _e569: vec2<f32> = uv_23;
            let _e570: vec2<f32> = dir;
            let _e574: vec2<f32> = uv_23;
            let _e575: vec2<f32> = dir;
            let _e579: vec3<f32> = scene((_e574 - (_e575 * 1.5f)));
            c_3 = _e579;
            let _e581: vec2<f32> = uv_23;
            let _e582: vec2<f32> = dir;
            let _e586: vec2<f32> = uv_23;
            let _e587: vec2<f32> = dir;
            let _e591: vec3<f32> = scene((_e586 + (_e587 * 1.5f)));
            d_3 = _e591;
            let _e593: vec3<f32> = resolved;
            let _e596: vec3<f32> = c_3;
            let _e597: vec3<f32> = d_3;
            resolved = ((_e593 * 0.75f) + ((_e596 + _e597) * 0.125f));
        }
    }
    let _e602: vec3<f32> = resolved;
    return _e602;
}

fn view_position(uv_24: vec2<f32>, d_4: f32) -> vec3<f32> {
    var uv_25: vec2<f32>;
    var d_5: f32;
    var clip: vec4<f32>;
    var view: vec4<f32>;

    uv_25 = uv_24;
    d_5 = d_4;
    let _e272: vec2<f32> = uv_25;
    let _e277: vec2<f32> = ((_e272 * 2f) - vec2(1f));
    let _e278: f32 = d_5;
    clip = vec4<f32>(_e277.x, _e277.y, ((_e278 * 2f) - 1f), 1f);
    let _e288: mat4x4<f32> = global.inv_projection;
    let _e289: vec4<f32> = clip;
    view = (_e288 * _e289);
    let _e292: vec4<f32> = view;
    let _e294: vec4<f32> = view;
    let _e296: vec4<f32> = view;
    let _e300: vec4<f32> = view;
    let _e302: vec4<f32> = view;
    return (_e292.xyz / vec3(max(abs(_e302.w), 0.000001f)));
}

fn world_position(uv_26: vec2<f32>, d_6: f32) -> vec3<f32> {
    var uv_27: vec2<f32>;
    var d_7: f32;
    var world: vec4<f32>;

    uv_27 = uv_26;
    d_7 = d_6;
    let _e272: mat4x4<f32> = global.inv_view;
    let _e275: vec2<f32> = uv_27;
    let _e276: f32 = d_7;
    let _e277: vec3<f32> = view_position(_e275, _e276);
    world = (_e272 * vec4<f32>(_e277.x, _e277.y, _e277.z, 1f));
    let _e285: vec4<f32> = world;
    let _e287: vec4<f32> = world;
    let _e289: vec4<f32> = world;
    let _e293: vec4<f32> = world;
    let _e295: vec4<f32> = world;
    return (_e285.xyz / vec3(max(abs(_e295.w), 0.000001f)));
}

fn project_view(p: vec3<f32>, clipW: ptr<function, f32>) -> vec2<f32> {
    var p_1: vec3<f32>;
    var clip_1: vec4<f32>;

    p_1 = p;
    let _e271: mat4x4<f32> = global.projection;
    let _e272: vec3<f32> = p_1;
    clip_1 = (_e271 * vec4<f32>(_e272.x, _e272.y, _e272.z, 1f));
    let _e280: vec4<f32> = clip_1;
    (*clipW) = _e280.w;
    let _e282: vec4<f32> = clip_1;
    let _e284: vec4<f32> = clip_1;
    let _e286: vec4<f32> = clip_1;
    let _e290: vec4<f32> = clip_1;
    let _e292: vec4<f32> = clip_1;
    return (((_e282.xy / vec2(max(abs(_e292.w), 0.000001f))) * 0.5f) + vec2(0.5f));
}

fn projectile_lighting(worldPos: vec3<f32>) -> vec3<f32> {
    var worldPos_1: vec3<f32>;
    var result: vec3<f32> = vec3(0f);
    var i: i32 = 0i;
    var delta: vec3<f32>;
    var radius: f32;
    var distanceToLight: f32;
    var falloff: f32;

    worldPos_1 = worldPos;
    let _e270: i32 = global.dynamic_light_count;
    if (_e270 <= 0i) {
        return vec3(0f);
    }
    loop {
        let _e280: i32 = i;
        if !((_e280 < 8i)) {
            break;
        }
        {
            let _e287: i32 = i;
            let _e288: i32 = global.dynamic_light_count;
            if (_e287 >= _e288) {
                break;
            }
            let _e290: i32 = i;
            let _e292: vec4<f32> = global.dynamic_light_pos[_e290];
            let _e294: vec3<f32> = worldPos_1;
            delta = (_e292.xyz - _e294);
            let _e297: i32 = i;
            let _e299: vec4<f32> = global.dynamic_light_pos[_e297];
            let _e302: i32 = i;
            let _e304: vec4<f32> = global.dynamic_light_pos[_e302];
            radius = max(_e304.w, 0.01f);
            let _e310: vec3<f32> = delta;
            distanceToLight = length(_e310);
            let _e314: f32 = radius;
            let _e319: f32 = radius;
            let _e322: f32 = radius;
            let _e323: f32 = distanceToLight;
            falloff = (1f - smoothstep((_e319 * 0.12f), _e322, _e323));
            let _e327: f32 = falloff;
            let _e328: f32 = falloff;
            falloff = (_e327 * _e328);
            let _e330: vec3<f32> = result;
            let _e331: i32 = i;
            let _e333: vec4<f32> = global.dynamic_light_color[_e331];
            let _e335: i32 = i;
            let _e337: vec4<f32> = global.dynamic_light_color[_e335];
            let _e340: f32 = falloff;
            result = (_e330 + (((_e333.xyz * _e337.w) * _e340) * 0.34f));
        }
        continuing {
            let _e284: i32 = i;
            i = (_e284 + 1i);
        }
    }
    let _e345: vec3<f32> = result;
    return _e345;
}

fn temporal_resolve(uv_28: vec2<f32>, current: vec3<f32>, d_8: f32) -> vec3<f32> {
    var uv_29: vec2<f32>;
    var current_1: vec3<f32>;
    var d_9: f32;
    var world_1: vec3<f32>;
    var previousClip: vec4<f32>;
    var previousUv: vec2<f32>;
    var lo_1: vec3<f32>;
    var hi_1: vec3<f32>;
    var a_2: vec3<f32>;
    var b_2: vec3<f32>;
    var c_4: vec3<f32>;
    var e_2: vec3<f32>;
    var history: vec3<f32>;
    var motionPixels: f32;
    var historyWeight: f32;
    var luminanceDelta: f32;

    uv_29 = uv_28;
    current_1 = current;
    d_9 = d_8;
    let _e274: i32 = global.aa_mode;
    let _e277: i32 = global.hdr_mode;
    let _e281: i32 = global.pbr_enabled;
    let _e285: i32 = global.history_valid;
    let _e289: i32 = global.depth_available;
    let _e293: f32 = d_9;
    if ((((((_e274 != 4i) || (_e277 != 0i)) || (_e281 != 0i)) || (_e285 == 0i)) || (_e289 == 0i)) || (_e293 >= 0.999999f)) {
        let _e297: vec3<f32> = current_1;
        return _e297;
    }
    let _e300: vec2<f32> = uv_29;
    let _e301: f32 = d_9;
    let _e302: vec3<f32> = world_position(_e300, _e301);
    world_1 = _e302;
    let _e304: mat4x4<f32> = global.previous_view_projection;
    let _e305: vec3<f32> = world_1;
    previousClip = (_e304 * vec4<f32>(_e305.x, _e305.y, _e305.z, 1f));
    let _e313: vec4<f32> = previousClip;
    if (_e313.w <= 0.000001f) {
        let _e317: vec3<f32> = current_1;
        return _e317;
    }
    let _e318: vec4<f32> = previousClip;
    let _e320: vec4<f32> = previousClip;
    previousUv = (((_e318.xy / vec2(_e320.w)) * 0.5f) + vec2(0.5f));
    let _e330: vec2<f32> = previousUv;
    let _e334: vec2<f32> = previousUv;
    let _e339: vec2<f32> = previousUv;
    let _e344: vec2<f32> = previousUv;
    if ((((_e330.x <= 0f) || (_e334.x >= 1f)) || (_e339.y <= 0f)) || (_e344.y >= 1f)) {
        let _e349: vec3<f32> = current_1;
        return _e349;
    }
    let _e350: vec3<f32> = current_1;
    lo_1 = _e350;
    let _e352: vec3<f32> = current_1;
    hi_1 = _e352;
    let _e354: vec2<f32> = uv_29;
    let _e355: vec2<f32> = global.texel;
    let _e360: vec2<f32> = uv_29;
    let _e361: vec2<f32> = global.texel;
    let _e366: vec3<f32> = scene((_e360 + vec2<f32>(_e361.x, 0f)));
    a_2 = _e366;
    let _e368: vec2<f32> = uv_29;
    let _e369: vec2<f32> = global.texel;
    let _e374: vec2<f32> = uv_29;
    let _e375: vec2<f32> = global.texel;
    let _e380: vec3<f32> = scene((_e374 - vec2<f32>(_e375.x, 0f)));
    b_2 = _e380;
    let _e382: vec2<f32> = uv_29;
    let _e384: vec2<f32> = global.texel;
    let _e388: vec2<f32> = uv_29;
    let _e390: vec2<f32> = global.texel;
    let _e394: vec3<f32> = scene((_e388 + vec2<f32>(0f, _e390.y)));
    c_4 = _e394;
    let _e396: vec2<f32> = uv_29;
    let _e398: vec2<f32> = global.texel;
    let _e402: vec2<f32> = uv_29;
    let _e404: vec2<f32> = global.texel;
    let _e408: vec3<f32> = scene((_e402 - vec2<f32>(0f, _e404.y)));
    e_2 = _e408;
    let _e413: vec3<f32> = a_2;
    let _e414: vec3<f32> = b_2;
    let _e418: vec3<f32> = c_4;
    let _e419: vec3<f32> = e_2;
    let _e423: vec3<f32> = a_2;
    let _e424: vec3<f32> = b_2;
    let _e428: vec3<f32> = c_4;
    let _e429: vec3<f32> = e_2;
    let _e432: vec3<f32> = lo_1;
    let _e435: vec3<f32> = a_2;
    let _e436: vec3<f32> = b_2;
    let _e440: vec3<f32> = c_4;
    let _e441: vec3<f32> = e_2;
    let _e445: vec3<f32> = a_2;
    let _e446: vec3<f32> = b_2;
    let _e450: vec3<f32> = c_4;
    let _e451: vec3<f32> = e_2;
    lo_1 = min(_e432, min(min(_e445, _e446), min(_e450, _e451)));
    let _e458: vec3<f32> = a_2;
    let _e459: vec3<f32> = b_2;
    let _e463: vec3<f32> = c_4;
    let _e464: vec3<f32> = e_2;
    let _e468: vec3<f32> = a_2;
    let _e469: vec3<f32> = b_2;
    let _e473: vec3<f32> = c_4;
    let _e474: vec3<f32> = e_2;
    let _e477: vec3<f32> = hi_1;
    let _e480: vec3<f32> = a_2;
    let _e481: vec3<f32> = b_2;
    let _e485: vec3<f32> = c_4;
    let _e486: vec3<f32> = e_2;
    let _e490: vec3<f32> = a_2;
    let _e491: vec3<f32> = b_2;
    let _e495: vec3<f32> = c_4;
    let _e496: vec3<f32> = e_2;
    hi_1 = max(_e477, max(max(_e490, _e491), max(_e495, _e496)));
    let _e501: vec2<f32> = previousUv;
    let _e502: vec4<f32> = prime_sample_history_tex(_e501);
    let _e504: vec3<f32> = lo_1;
    let _e508: vec3<f32> = hi_1;
    let _e513: vec2<f32> = previousUv;
    let _e514: vec4<f32> = prime_sample_history_tex(_e513);
    let _e516: vec3<f32> = lo_1;
    let _e520: vec3<f32> = hi_1;
    history = clamp(_e514.xyz, (_e516 - vec3(0.025f)), (_e520 + vec3(0.025f)));
    let _e526: vec2<f32> = previousUv;
    let _e527: vec2<f32> = uv_29;
    let _e529: vec2<f32> = global.texel;
    let _e531: vec2<f32> = previousUv;
    let _e532: vec2<f32> = uv_29;
    let _e534: vec2<f32> = global.texel;
    motionPixels = length(((_e531 - _e532) / _e534));
    let _e545: f32 = motionPixels;
    historyWeight = (0.88f * (1f - smoothstep(0.35f, 2.5f, _e545)));
    let _e551: vec3<f32> = history;
    let _e552: f32 = luma(_e551);
    let _e554: vec3<f32> = current_1;
    let _e555: f32 = luma(_e554);
    let _e558: vec3<f32> = history;
    let _e559: f32 = luma(_e558);
    let _e561: vec3<f32> = current_1;
    let _e562: f32 = luma(_e561);
    luminanceDelta = abs((_e559 - _e562));
    let _e566: f32 = historyWeight;
    let _e573: f32 = luminanceDelta;
    historyWeight = (_e566 * (1f - smoothstep(0.08f, 0.35f, _e573)));
    let _e582: f32 = historyWeight;
    let _e586: vec3<f32> = current_1;
    let _e587: vec3<f32> = history;
    let _e591: f32 = historyWeight;
    return mix(_e586, _e587, vec3(clamp(_e591, 0f, 0.88f)));
}

fn pbr_distribution_ggx(n_3: vec3<f32>, h: vec3<f32>, roughness: f32) -> f32 {
    var n_4: vec3<f32>;
    var h_1: vec3<f32>;
    var roughness_1: f32;
    var a_3: f32;
    var a2_: f32;
    var nh: f32;
    var d_10: f32;

    n_4 = n_3;
    h_1 = h;
    roughness_1 = roughness;
    let _e274: f32 = roughness_1;
    let _e275: f32 = roughness_1;
    a_3 = (_e274 * _e275);
    let _e278: f32 = a_3;
    let _e279: f32 = a_3;
    a2_ = (_e278 * _e279);
    let _e284: vec3<f32> = n_4;
    let _e285: vec3<f32> = h_1;
    let _e290: vec3<f32> = n_4;
    let _e291: vec3<f32> = h_1;
    nh = max(dot(_e290, _e291), 0f);
    let _e296: f32 = nh;
    let _e297: f32 = nh;
    let _e299: f32 = a2_;
    d_10 = (((_e296 * _e297) * (_e299 - 1f)) + 1f);
    let _e306: f32 = a2_;
    let _e308: f32 = d_10;
    let _e310: f32 = d_10;
    let _e314: f32 = d_10;
    let _e316: f32 = d_10;
    return (_e306 / max(((3.1415927f * _e314) * _e316), 0.0001f));
}

fn pbr_geometry_schlick(nv: f32, roughness_2: f32) -> f32 {
    var nv_1: f32;
    var roughness_3: f32;
    var r: f32;
    var k: f32;

    nv_1 = nv;
    roughness_3 = roughness_2;
    let _e272: f32 = roughness_3;
    r = (_e272 + 1f);
    let _e276: f32 = r;
    let _e277: f32 = r;
    k = ((_e276 * _e277) / 8f);
    let _e282: f32 = nv_1;
    let _e283: f32 = nv_1;
    let _e285: f32 = k;
    let _e288: f32 = k;
    let _e291: f32 = nv_1;
    let _e293: f32 = k;
    let _e296: f32 = k;
    return (_e282 / max(((_e291 * (1f - _e293)) + _e296), 0.0001f));
}

fn pbr_fresnel(cosTheta: f32, f0_: vec3<f32>) -> vec3<f32> {
    var cosTheta_1: f32;
    var f0_1: vec3<f32>;

    cosTheta_1 = cosTheta;
    f0_1 = f0_;
    let _e272: vec3<f32> = f0_1;
    let _e274: vec3<f32> = f0_1;
    let _e278: f32 = cosTheta_1;
    let _e283: f32 = cosTheta_1;
    let _e290: f32 = cosTheta_1;
    let _e295: f32 = cosTheta_1;
    return (_e272 + ((vec3(1f) - _e274) * pow(clamp((1f - _e295), 0f, 1f), 5f)));
}

fn pbr_direct(albedo: vec3<f32>, n_5: vec3<f32>, v: vec3<f32>, l: vec3<f32>, radiance: vec3<f32>, metallic: f32, roughness_4: f32) -> vec3<f32> {
    var albedo_1: vec3<f32>;
    var n_6: vec3<f32>;
    var v_1: vec3<f32>;
    var l_1: vec3<f32>;
    var radiance_1: vec3<f32>;
    var metallic_1: f32;
    var roughness_5: f32;
    var h_2: vec3<f32>;
    var nv_2: f32;
    var nl: f32;
    var f0_2: vec3<f32>;
    var f_1: vec3<f32>;
    var d_11: f32;
    var g: f32;
    var spec: vec3<f32>;
    var kd: vec3<f32>;

    albedo_1 = albedo;
    n_6 = n_5;
    v_1 = v;
    l_1 = l;
    radiance_1 = radiance;
    metallic_1 = metallic;
    roughness_5 = roughness_4;
    let _e282: vec3<f32> = v_1;
    let _e283: vec3<f32> = l_1;
    let _e285: vec3<f32> = v_1;
    let _e286: vec3<f32> = l_1;
    h_2 = normalize((_e285 + _e286));
    let _e292: vec3<f32> = n_6;
    let _e293: vec3<f32> = v_1;
    let _e298: vec3<f32> = n_6;
    let _e299: vec3<f32> = v_1;
    nv_2 = max(dot(_e298, _e299), 0f);
    let _e306: vec3<f32> = n_6;
    let _e307: vec3<f32> = l_1;
    let _e312: vec3<f32> = n_6;
    let _e313: vec3<f32> = l_1;
    nl = max(dot(_e312, _e313), 0f);
    let _e318: f32 = nl;
    let _e321: f32 = nv_2;
    if ((_e318 <= 0f) || (_e321 <= 0f)) {
        return vec3(0f);
    }
    let _e333: vec3<f32> = albedo_1;
    let _e334: f32 = metallic_1;
    f0_2 = mix(vec3(0.04f), _e333, vec3(_e334));
    let _e340: vec3<f32> = h_2;
    let _e341: vec3<f32> = v_1;
    let _e346: vec3<f32> = h_2;
    let _e347: vec3<f32> = v_1;
    let _e354: vec3<f32> = h_2;
    let _e355: vec3<f32> = v_1;
    let _e360: vec3<f32> = h_2;
    let _e361: vec3<f32> = v_1;
    let _e365: vec3<f32> = f0_2;
    let _e366: vec3<f32> = pbr_fresnel(max(dot(_e360, _e361), 0f), _e365);
    f_1 = _e366;
    let _e371: vec3<f32> = n_6;
    let _e372: vec3<f32> = h_2;
    let _e373: f32 = roughness_5;
    let _e374: f32 = pbr_distribution_ggx(_e371, _e372, _e373);
    d_11 = _e374;
    let _e378: f32 = nv_2;
    let _e379: f32 = roughness_5;
    let _e380: f32 = pbr_geometry_schlick(_e378, _e379);
    let _e383: f32 = nl;
    let _e384: f32 = roughness_5;
    let _e385: f32 = pbr_geometry_schlick(_e383, _e384);
    g = (_e380 * _e385);
    let _e388: f32 = d_11;
    let _e389: f32 = g;
    let _e391: vec3<f32> = f_1;
    let _e394: f32 = nv_2;
    let _e396: f32 = nl;
    let _e400: f32 = nv_2;
    let _e402: f32 = nl;
    spec = (((_e388 * _e389) * _e391) / vec3(max(((4f * _e400) * _e402), 0.001f)));
    let _e411: vec3<f32> = f_1;
    let _e414: f32 = metallic_1;
    kd = ((vec3(1f) - _e411) * (1f - _e414));
    let _e418: vec3<f32> = kd;
    let _e419: vec3<f32> = albedo_1;
    let _e424: vec3<f32> = spec;
    let _e426: vec3<f32> = radiance_1;
    let _e428: f32 = nl;
    return (((((_e418 * _e419) / vec3(3.1415927f)) + _e424) * _e426) * _e428);
}

fn deferred_pbr(uv_30: vec2<f32>, worldPos_2: vec3<f32>) -> vec3<f32> {
    var uv_31: vec2<f32>;
    var worldPos_3: vec3<f32>;
    var a_4: vec4<f32>;
    var n_7: vec3<f32>;
    var m_2: vec4<f32>;
    var metallic_2: f32;
    var roughness_6: f32;
    var v_2: vec3<f32>;
    var result_1: vec3<f32>;
    var i_1: i32 = 0i;
    var delta_1: vec3<f32>;
    var dist: f32;
    var radius_1: f32;
    var attenuation: f32;

    uv_31 = uv_30;
    worldPos_3 = worldPos_2;
    let _e273: vec2<f32> = uv_31;
    let _e274: vec4<f32> = prime_sample_pbr_albedo(_e273);
    a_4 = _e274;
    let _e276: i32 = global.pbr_enabled;
    let _e279: vec4<f32> = a_4;
    if ((_e276 == 0i) || (_e279.w < 0.5f)) {
        return vec3(-1f);
    }
    let _e288: vec2<f32> = uv_31;
    let _e289: vec4<f32> = prime_sample_pbr_normal(_e288);
    let _e297: vec2<f32> = uv_31;
    let _e298: vec4<f32> = prime_sample_pbr_normal(_e297);
    n_7 = normalize(((_e298.xyz * 2f) - vec3(1f)));
    let _e308: vec2<f32> = uv_31;
    let _e309: vec4<f32> = prime_sample_pbr_material(_e308);
    m_2 = _e309;
    let _e311: vec4<f32> = m_2;
    let _e315: vec4<f32> = m_2;
    metallic_2 = clamp(_e315.x, 0f, 1f);
    let _e321: vec4<f32> = m_2;
    let _e325: vec4<f32> = m_2;
    roughness_6 = clamp(_e325.y, 0.04f, 1f);
    let _e331: vec3<f32> = global.camera_position;
    let _e332: vec3<f32> = worldPos_3;
    let _e334: vec3<f32> = global.camera_position;
    let _e335: vec3<f32> = worldPos_3;
    v_2 = normalize((_e334 - _e335));
    let _e339: vec4<f32> = a_4;
    let _e344: f32 = metallic_2;
    let _e349: vec4<f32> = m_2;
    let _e353: vec4<f32> = m_2;
    result_1 = ((_e339.xyz * (0.1f + (0.08f * (1f - _e344)))) * clamp(_e353.w, 0f, 1f));
    let _e360: vec3<f32> = result_1;
    let _e361: vec4<f32> = a_4;
    let _e365: vec3<f32> = global.pbr_light1_dir;
    let _e367: vec3<f32> = global.pbr_light1_dir;
    let _e373: vec4<f32> = a_4;
    let _e375: vec3<f32> = n_7;
    let _e376: vec3<f32> = v_2;
    let _e377: vec3<f32> = global.pbr_light1_dir;
    let _e379: vec3<f32> = global.pbr_light1_dir;
    let _e382: vec3<f32> = global.pbr_light1_color;
    let _e383: f32 = metallic_2;
    let _e384: f32 = roughness_6;
    let _e385: vec3<f32> = pbr_direct(_e373.xyz, _e375, _e376, normalize(-(_e379)), _e382, _e383, _e384);
    result_1 = (_e360 + _e385);
    let _e387: vec3<f32> = result_1;
    let _e388: vec4<f32> = a_4;
    let _e392: vec3<f32> = global.pbr_light2_dir;
    let _e394: vec3<f32> = global.pbr_light2_dir;
    let _e400: vec4<f32> = a_4;
    let _e402: vec3<f32> = n_7;
    let _e403: vec3<f32> = v_2;
    let _e404: vec3<f32> = global.pbr_light2_dir;
    let _e406: vec3<f32> = global.pbr_light2_dir;
    let _e409: vec3<f32> = global.pbr_light2_color;
    let _e410: f32 = metallic_2;
    let _e411: f32 = roughness_6;
    let _e412: vec3<f32> = pbr_direct(_e400.xyz, _e402, _e403, normalize(-(_e406)), _e409, _e410, _e411);
    result_1 = (_e387 + _e412);
    loop {
        let _e416: i32 = i_1;
        if !((_e416 < 8i)) {
            break;
        }
        {
            let _e423: i32 = i_1;
            let _e424: i32 = global.dynamic_light_count;
            if (_e423 >= _e424) {
                break;
            }
            let _e426: i32 = i_1;
            let _e428: vec4<f32> = global.dynamic_light_pos[_e426];
            let _e430: vec3<f32> = worldPos_3;
            delta_1 = (_e428.xyz - _e430);
            let _e434: vec3<f32> = delta_1;
            dist = length(_e434);
            let _e437: i32 = i_1;
            let _e439: vec4<f32> = global.dynamic_light_pos[_e437];
            let _e442: i32 = i_1;
            let _e444: vec4<f32> = global.dynamic_light_pos[_e442];
            radius_1 = max(_e444.w, 0.01f);
            let _e449: f32 = dist;
            let _e450: f32 = radius_1;
            if (_e449 < _e450) {
                {
                    let _e453: f32 = radius_1;
                    let _e458: f32 = radius_1;
                    let _e461: f32 = radius_1;
                    let _e462: f32 = dist;
                    attenuation = (1f - smoothstep((_e458 * 0.1f), _e461, _e462));
                    let _e466: f32 = attenuation;
                    let _e467: f32 = attenuation;
                    attenuation = (_e466 * _e467);
                    let _e469: vec3<f32> = result_1;
                    let _e470: vec4<f32> = a_4;
                    let _e475: vec3<f32> = delta_1;
                    let _e477: i32 = i_1;
                    let _e479: vec4<f32> = global.dynamic_light_color[_e477];
                    let _e481: i32 = i_1;
                    let _e483: vec4<f32> = global.dynamic_light_color[_e481];
                    let _e486: f32 = attenuation;
                    let _e490: vec4<f32> = a_4;
                    let _e492: vec3<f32> = n_7;
                    let _e493: vec3<f32> = v_2;
                    let _e495: vec3<f32> = delta_1;
                    let _e497: i32 = i_1;
                    let _e499: vec4<f32> = global.dynamic_light_color[_e497];
                    let _e501: i32 = i_1;
                    let _e503: vec4<f32> = global.dynamic_light_color[_e501];
                    let _e506: f32 = attenuation;
                    let _e508: f32 = metallic_2;
                    let _e509: f32 = roughness_6;
                    let _e510: vec3<f32> = pbr_direct(_e490.xyz, _e492, _e493, normalize(_e495), ((_e499.xyz * _e503.w) * _e506), _e508, _e509);
                    result_1 = (_e469 + _e510);
                }
            }
        }
        continuing {
            let _e420: i32 = i_1;
            i_1 = (_e420 + 1i);
        }
    }
    let _e512: vec3<f32> = result_1;
    let _e513: vec4<f32> = a_4;
    let _e515: vec4<f32> = m_2;
    result_1 = (_e512 + ((_e513.xyz * _e515.z) * 1.4f));
    let _e521: vec3<f32> = result_1;
    return _e521;
}

fn depth_normal(uv_32: vec2<f32>, centerDepth: f32) -> vec3<f32> {
    var uv_33: vec2<f32>;
    var centerDepth_1: f32;
    var p_2: vec3<f32>;
    var dx: f32;
    var dy: f32;
    var px: vec3<f32>;
    var py: vec3<f32>;
    var n_8: vec3<f32>;

    uv_33 = uv_32;
    centerDepth_1 = centerDepth;
    let _e272: i32 = global.depth_available;
    let _e275: f32 = centerDepth_1;
    if ((_e272 == 0i) || (_e275 >= 0.999999f)) {
        return vec3<f32>(0f, 0f, 1f);
    }
    let _e285: vec2<f32> = uv_33;
    let _e286: f32 = centerDepth_1;
    let _e287: vec3<f32> = view_position(_e285, _e286);
    p_2 = _e287;
    let _e289: vec2<f32> = uv_33;
    let _e290: vec2<f32> = global.texel;
    let _e295: vec2<f32> = uv_33;
    let _e296: vec2<f32> = global.texel;
    let _e301: f32 = raw_depth((_e295 + vec2<f32>(_e296.x, 0f)));
    dx = _e301;
    let _e303: vec2<f32> = uv_33;
    let _e305: vec2<f32> = global.texel;
    let _e309: vec2<f32> = uv_33;
    let _e311: vec2<f32> = global.texel;
    let _e315: f32 = raw_depth((_e309 + vec2<f32>(0f, _e311.y)));
    dy = _e315;
    let _e317: vec2<f32> = uv_33;
    let _e318: vec2<f32> = global.texel;
    let _e324: vec2<f32> = uv_33;
    let _e325: vec2<f32> = global.texel;
    let _e330: f32 = dx;
    let _e331: vec3<f32> = view_position((_e324 + vec2<f32>(_e325.x, 0f)), _e330);
    px = _e331;
    let _e333: vec2<f32> = uv_33;
    let _e335: vec2<f32> = global.texel;
    let _e340: vec2<f32> = uv_33;
    let _e342: vec2<f32> = global.texel;
    let _e346: f32 = dy;
    let _e347: vec3<f32> = view_position((_e340 + vec2<f32>(0f, _e342.y)), _e346);
    py = _e347;
    let _e349: vec3<f32> = px;
    let _e350: vec3<f32> = p_2;
    let _e352: vec3<f32> = py;
    let _e353: vec3<f32> = p_2;
    let _e355: vec3<f32> = px;
    let _e356: vec3<f32> = p_2;
    let _e358: vec3<f32> = py;
    let _e359: vec3<f32> = p_2;
    let _e362: vec3<f32> = px;
    let _e363: vec3<f32> = p_2;
    let _e365: vec3<f32> = py;
    let _e366: vec3<f32> = p_2;
    let _e368: vec3<f32> = px;
    let _e369: vec3<f32> = p_2;
    let _e371: vec3<f32> = py;
    let _e372: vec3<f32> = p_2;
    n_8 = normalize(cross((_e368 - _e369), (_e371 - _e372)));
    let _e377: vec3<f32> = n_8;
    if (_e377.z < 0f) {
        let _e381: vec3<f32> = n_8;
        n_8 = -(_e381);
    }
    let _e383: vec3<f32> = n_8;
    return _e383;
}

fn directional_shadow(worldPos_4: vec3<f32>, viewNormal: vec3<f32>) -> f32 {
    var worldPos_5: vec3<f32>;
    var viewNormal_1: vec3<f32>;
    var lightClip: vec4<f32>;
    var ndc: vec3<f32>;
    var suv: vec2<f32>;
    var receiver: f32;
    var worldNormal: vec3<f32>;
    var alignment: f32;
    var bias: f32;
    var lit: f32 = 0f;
    var y: i32 = -1i;
    var x: i32;
    var stored: f32;
    var local_2: f32;

    worldPos_5 = worldPos_4;
    viewNormal_1 = viewNormal;
    let _e272: i32 = global.shadow_enabled;
    if (_e272 == 0i) {
        return 1f;
    }
    let _e276: mat4x4<f32> = global.shadow_projection;
    let _e277: mat4x4<f32> = global.shadow_view;
    let _e279: vec3<f32> = worldPos_5;
    lightClip = ((_e276 * _e277) * vec4<f32>(_e279.x, _e279.y, _e279.z, 1f));
    let _e287: vec4<f32> = lightClip;
    if (_e287.w <= 0f) {
        return 1f;
    }
    let _e292: vec4<f32> = lightClip;
    let _e294: vec4<f32> = lightClip;
    ndc = (_e292.xyz / vec3(_e294.w));
    let _e299: vec3<f32> = ndc;
    suv = ((_e299.xy * 0.5f) + vec2(0.5f));
    let _e307: vec3<f32> = ndc;
    receiver = ((_e307.z * 0.5f) + 0.5f);
    let _e314: vec2<f32> = suv;
    let _e318: vec2<f32> = suv;
    let _e323: vec2<f32> = suv;
    let _e328: vec2<f32> = suv;
    let _e333: f32 = receiver;
    let _e337: f32 = receiver;
    if ((((((_e314.x <= 0.002f) || (_e318.x >= 0.998f)) || (_e323.y <= 0.002f)) || (_e328.y >= 0.998f)) || (_e333 <= 0f)) || (_e337 >= 1f)) {
        return 1f;
    }
    let _e342: mat4x4<f32> = global.inv_view;
    let _e352: vec3<f32> = viewNormal_1;
    let _e354: mat4x4<f32> = global.inv_view;
    let _e364: vec3<f32> = viewNormal_1;
    worldNormal = normalize((mat3x3<f32>(_e354[0].xyz, _e354[1].xyz, _e354[2].xyz) * _e364));
    let _e370: vec3<f32> = global.shadow_light_dir;
    let _e372: vec3<f32> = worldNormal;
    let _e374: vec3<f32> = global.shadow_light_dir;
    let _e379: vec3<f32> = global.shadow_light_dir;
    let _e381: vec3<f32> = worldNormal;
    let _e383: vec3<f32> = global.shadow_light_dir;
    alignment = abs(dot(_e381, normalize(_e383)));
    let _e393: f32 = alignment;
    bias = mix(0.0032f, 0.0007f, _e393);
    loop {
        let _e401: i32 = y;
        if !((_e401 <= 1i)) {
            break;
        }
        {
            x = -1i;
            loop {
                let _e411: i32 = x;
                if !((_e411 <= 1i)) {
                    break;
                }
                {
                    let _e418: vec2<f32> = suv;
                    let _e419: i32 = x;
                    let _e421: i32 = y;
                    let _e424: vec2<f32> = global.shadow_texel;
                    let _e427: vec2<f32> = suv;
                    let _e428: i32 = x;
                    let _e430: i32 = y;
                    let _e433: vec2<f32> = global.shadow_texel;
                    let _e436: vec4<f32> = prime_sample_shadow_tex((_e427 + (vec2<f32>(f32(_e428), f32(_e430)) * _e433)));
                    stored = _e436.x;
                    let _e439: f32 = lit;
                    let _e440: f32 = receiver;
                    let _e441: f32 = bias;
                    let _e443: f32 = stored;
                    if ((_e440 - _e441) <= _e443) {
                        local_2 = 1f;
                    } else {
                        local_2 = 0f;
                    }
                    let _e448: f32 = local_2;
                    lit = (_e439 + _e448);
                }
                continuing {
                    let _e415: i32 = x;
                    x = (_e415 + 1i);
                }
            }
        }
        continuing {
            let _e405: i32 = y;
            y = (_e405 + 1i);
        }
    }
    let _e450: f32 = lit;
    lit = (_e450 / 9f);
    let _e458: f32 = lit;
    return mix(0.58f, 1f, _e458);
}

fn reflection_depth_continuity(uv_34: vec2<f32>, d_12: f32) -> f32 {
    var uv_35: vec2<f32>;
    var d_13: f32;
    var c_5: f32;
    var tolerance: f32;
    var d0_: f32;
    var d1_: f32;
    var d2_: f32;
    var d3_: f32;
    var worst: f32;

    uv_35 = uv_34;
    d_13 = d_12;
    let _e272: f32 = d_13;
    if (_e272 >= 0.999999f) {
        return 0f;
    }
    let _e277: f32 = d_13;
    let _e278: f32 = view_depth(_e277);
    c_5 = _e278;
    let _e282: f32 = c_5;
    let _e288: f32 = c_5;
    tolerance = max(0.1f, (abs(_e288) * 0.012f));
    let _e294: vec2<f32> = uv_35;
    let _e295: vec2<f32> = global.texel;
    let _e300: vec2<f32> = uv_35;
    let _e301: vec2<f32> = global.texel;
    let _e306: f32 = raw_depth((_e300 + vec2<f32>(_e301.x, 0f)));
    d0_ = _e306;
    let _e308: vec2<f32> = uv_35;
    let _e309: vec2<f32> = global.texel;
    let _e314: vec2<f32> = uv_35;
    let _e315: vec2<f32> = global.texel;
    let _e320: f32 = raw_depth((_e314 - vec2<f32>(_e315.x, 0f)));
    d1_ = _e320;
    let _e322: vec2<f32> = uv_35;
    let _e324: vec2<f32> = global.texel;
    let _e328: vec2<f32> = uv_35;
    let _e330: vec2<f32> = global.texel;
    let _e334: f32 = raw_depth((_e328 + vec2<f32>(0f, _e330.y)));
    d2_ = _e334;
    let _e336: vec2<f32> = uv_35;
    let _e338: vec2<f32> = global.texel;
    let _e342: vec2<f32> = uv_35;
    let _e344: vec2<f32> = global.texel;
    let _e348: f32 = raw_depth((_e342 - vec2<f32>(0f, _e344.y)));
    d3_ = _e348;
    let _e350: f32 = d0_;
    let _e353: f32 = d1_;
    let _e357: f32 = d2_;
    let _e361: f32 = d3_;
    if ((((_e350 >= 0.999999f) || (_e353 >= 0.999999f)) || (_e357 >= 0.999999f)) || (_e361 >= 0.999999f)) {
        {
            return 0f;
        }
    }
    let _e367: f32 = d0_;
    let _e368: f32 = view_depth(_e367);
    let _e369: f32 = c_5;
    let _e372: f32 = d0_;
    let _e373: f32 = view_depth(_e372);
    let _e374: f32 = c_5;
    let _e378: f32 = d1_;
    let _e379: f32 = view_depth(_e378);
    let _e380: f32 = c_5;
    let _e383: f32 = d1_;
    let _e384: f32 = view_depth(_e383);
    let _e385: f32 = c_5;
    let _e389: f32 = d0_;
    let _e390: f32 = view_depth(_e389);
    let _e391: f32 = c_5;
    let _e394: f32 = d0_;
    let _e395: f32 = view_depth(_e394);
    let _e396: f32 = c_5;
    let _e400: f32 = d1_;
    let _e401: f32 = view_depth(_e400);
    let _e402: f32 = c_5;
    let _e405: f32 = d1_;
    let _e406: f32 = view_depth(_e405);
    let _e407: f32 = c_5;
    let _e412: f32 = d2_;
    let _e413: f32 = view_depth(_e412);
    let _e414: f32 = c_5;
    let _e417: f32 = d2_;
    let _e418: f32 = view_depth(_e417);
    let _e419: f32 = c_5;
    let _e423: f32 = d3_;
    let _e424: f32 = view_depth(_e423);
    let _e425: f32 = c_5;
    let _e428: f32 = d3_;
    let _e429: f32 = view_depth(_e428);
    let _e430: f32 = c_5;
    let _e434: f32 = d2_;
    let _e435: f32 = view_depth(_e434);
    let _e436: f32 = c_5;
    let _e439: f32 = d2_;
    let _e440: f32 = view_depth(_e439);
    let _e441: f32 = c_5;
    let _e445: f32 = d3_;
    let _e446: f32 = view_depth(_e445);
    let _e447: f32 = c_5;
    let _e450: f32 = d3_;
    let _e451: f32 = view_depth(_e450);
    let _e452: f32 = c_5;
    let _e457: f32 = d0_;
    let _e458: f32 = view_depth(_e457);
    let _e459: f32 = c_5;
    let _e462: f32 = d0_;
    let _e463: f32 = view_depth(_e462);
    let _e464: f32 = c_5;
    let _e468: f32 = d1_;
    let _e469: f32 = view_depth(_e468);
    let _e470: f32 = c_5;
    let _e473: f32 = d1_;
    let _e474: f32 = view_depth(_e473);
    let _e475: f32 = c_5;
    let _e479: f32 = d0_;
    let _e480: f32 = view_depth(_e479);
    let _e481: f32 = c_5;
    let _e484: f32 = d0_;
    let _e485: f32 = view_depth(_e484);
    let _e486: f32 = c_5;
    let _e490: f32 = d1_;
    let _e491: f32 = view_depth(_e490);
    let _e492: f32 = c_5;
    let _e495: f32 = d1_;
    let _e496: f32 = view_depth(_e495);
    let _e497: f32 = c_5;
    let _e502: f32 = d2_;
    let _e503: f32 = view_depth(_e502);
    let _e504: f32 = c_5;
    let _e507: f32 = d2_;
    let _e508: f32 = view_depth(_e507);
    let _e509: f32 = c_5;
    let _e513: f32 = d3_;
    let _e514: f32 = view_depth(_e513);
    let _e515: f32 = c_5;
    let _e518: f32 = d3_;
    let _e519: f32 = view_depth(_e518);
    let _e520: f32 = c_5;
    let _e524: f32 = d2_;
    let _e525: f32 = view_depth(_e524);
    let _e526: f32 = c_5;
    let _e529: f32 = d2_;
    let _e530: f32 = view_depth(_e529);
    let _e531: f32 = c_5;
    let _e535: f32 = d3_;
    let _e536: f32 = view_depth(_e535);
    let _e537: f32 = c_5;
    let _e540: f32 = d3_;
    let _e541: f32 = view_depth(_e540);
    let _e542: f32 = c_5;
    worst = max(max(abs((_e485 - _e486)), abs((_e496 - _e497))), max(abs((_e530 - _e531)), abs((_e541 - _e542))));
    let _e550: f32 = tolerance;
    let _e554: f32 = tolerance;
    let _e555: f32 = tolerance;
    let _e558: f32 = worst;
    return (1f - smoothstep(_e554, (_e555 * 3f), _e558));
}

fn screen_reflection(uv_36: vec2<f32>, d_14: f32, n_9: vec3<f32>) -> vec4<f32> {
    var uv_37: vec2<f32>;
    var d_15: f32;
    var n_10: vec3<f32>;
    var sourceContinuity: f32;
    var origin: vec3<f32>;
    var incident: vec3<f32>;
    var rayDir: vec3<f32>;
    var stepLength: f32;
    var ray: vec3<f32>;
    var i_2: i32 = 0i;
    var fi: f32;
    var clipW_1: f32;
    var hitUv: vec2<f32>;
    var sd: f32;
    var hitContinuity: f32;
    var surface: vec3<f32>;
    var thickness: f32;
    var separation: f32;
    var edge: f32;
    var edgeFade: f32;
    var travel: f32;
    var maxTravel: f32;
    var travelFade: f32;
    var slabConfidence: f32;
    var confidence: f32;

    uv_37 = uv_36;
    d_15 = d_14;
    n_10 = n_9;
    let _e274: i32 = global.reflections;
    let _e277: i32 = global.depth_available;
    if ((_e274 == 0i) || (_e277 == 0i)) {
        return vec4(0f);
    }
    let _e285: vec2<f32> = uv_37;
    let _e286: f32 = d_15;
    let _e287: f32 = reflection_depth_continuity(_e285, _e286);
    sourceContinuity = _e287;
    let _e289: f32 = sourceContinuity;
    if (_e289 < 0.2f) {
        return vec4(0f);
    }
    let _e296: vec2<f32> = uv_37;
    let _e297: f32 = d_15;
    let _e298: vec3<f32> = view_position(_e296, _e297);
    origin = _e298;
    let _e301: vec3<f32> = origin;
    incident = normalize(_e301);
    let _e306: vec3<f32> = incident;
    let _e307: vec3<f32> = n_10;
    let _e311: vec3<f32> = incident;
    let _e312: vec3<f32> = n_10;
    rayDir = normalize(reflect(_e311, _e312));
    let _e316: vec3<f32> = rayDir;
    if (_e316.z >= -0.08f) {
        return vec4(0f);
    }
    let _e324: vec3<f32> = origin;
    let _e326: vec3<f32> = origin;
    let _e332: vec3<f32> = origin;
    let _e334: vec3<f32> = origin;
    stepLength = max(0.12f, (abs(_e334.z) * 0.012f));
    let _e341: vec3<f32> = origin;
    ray = _e341;
    loop {
        let _e345: i32 = i_2;
        if !((_e345 < 14i)) {
            break;
        }
        {
            let _e352: i32 = i_2;
            fi = f32(_e352);
            let _e355: vec3<f32> = ray;
            let _e356: vec3<f32> = rayDir;
            let _e357: f32 = stepLength;
            let _e360: f32 = fi;
            ray = (_e355 + ((_e356 * _e357) * (1f + (_e360 * 0.1f))));
            clipW_1 = 1f;
            let _e370: vec3<f32> = ray;
            let _e372: vec2<f32> = project_view(_e370, (&clipW_1));
            hitUv = _e372;
            let _e374: f32 = clipW_1;
            let _e377: vec2<f32> = hitUv;
            let _e382: vec2<f32> = hitUv;
            let _e387: vec2<f32> = hitUv;
            let _e392: vec2<f32> = hitUv;
            if (((((_e374 <= 0f) || (_e377.x <= 0.015f)) || (_e382.x >= 0.985f)) || (_e387.y <= 0.015f)) || (_e392.y >= 0.985f)) {
                break;
            }
            let _e398: vec2<f32> = hitUv;
            let _e399: f32 = raw_depth(_e398);
            sd = _e399;
            let _e401: f32 = sd;
            if (_e401 >= 0.999999f) {
                continue;
            }
            let _e406: vec2<f32> = hitUv;
            let _e407: f32 = sd;
            let _e408: f32 = reflection_depth_continuity(_e406, _e407);
            hitContinuity = _e408;
            let _e410: f32 = hitContinuity;
            if (_e410 < 0.25f) {
                continue;
            }
            let _e415: vec2<f32> = hitUv;
            let _e416: f32 = sd;
            let _e417: vec3<f32> = view_position(_e415, _e416);
            surface = _e417;
            let _e420: vec3<f32> = surface;
            let _e422: vec3<f32> = surface;
            let _e428: vec3<f32> = surface;
            let _e430: vec3<f32> = surface;
            thickness = max(0.045f, (abs(_e430.z) * 0.0045f));
            let _e437: vec3<f32> = ray;
            let _e439: vec3<f32> = surface;
            let _e442: vec3<f32> = ray;
            let _e444: vec3<f32> = surface;
            separation = abs((_e442.z - _e444.z));
            let _e449: f32 = separation;
            let _e450: f32 = thickness;
            if (_e449 <= _e450) {
                {
                    let _e452: vec2<f32> = hitUv;
                    let _e455: vec2<f32> = hitUv;
                    let _e458: vec2<f32> = hitUv;
                    let _e461: vec2<f32> = hitUv;
                    let _e465: vec2<f32> = hitUv;
                    let _e468: vec2<f32> = hitUv;
                    let _e471: vec2<f32> = hitUv;
                    let _e474: vec2<f32> = hitUv;
                    let _e478: vec2<f32> = hitUv;
                    let _e481: vec2<f32> = hitUv;
                    let _e484: vec2<f32> = hitUv;
                    let _e487: vec2<f32> = hitUv;
                    let _e491: vec2<f32> = hitUv;
                    let _e494: vec2<f32> = hitUv;
                    let _e497: vec2<f32> = hitUv;
                    let _e500: vec2<f32> = hitUv;
                    edge = min(min(_e484.x, (1f - _e487.x)), min(_e497.y, (1f - _e500.y)));
                    let _e511: f32 = edge;
                    edgeFade = smoothstep(0.015f, 0.12f, _e511);
                    let _e514: vec3<f32> = ray;
                    let _e515: vec3<f32> = origin;
                    let _e517: vec3<f32> = ray;
                    let _e518: vec3<f32> = origin;
                    travel = length((_e517 - _e518));
                    let _e523: vec3<f32> = origin;
                    let _e525: vec3<f32> = origin;
                    let _e531: vec3<f32> = origin;
                    let _e533: vec3<f32> = origin;
                    maxTravel = max(3f, (abs(_e533.z) * 0.7f));
                    let _e541: f32 = maxTravel;
                    let _e546: f32 = maxTravel;
                    let _e549: f32 = maxTravel;
                    let _e550: f32 = travel;
                    travelFade = (1f - smoothstep((_e546 * 0.45f), _e549, _e550));
                    let _e559: f32 = thickness;
                    let _e560: f32 = separation;
                    slabConfidence = (1f - smoothstep(0f, _e559, _e560));
                    let _e564: f32 = sourceContinuity;
                    let _e565: f32 = hitContinuity;
                    let _e567: f32 = edgeFade;
                    let _e569: f32 = travelFade;
                    let _e571: f32 = slabConfidence;
                    confidence = ((((_e564 * _e565) * _e567) * _e569) * _e571);
                    let _e575: vec2<f32> = hitUv;
                    let _e576: vec3<f32> = scene(_e575);
                    let _e580: f32 = confidence;
                    return vec4<f32>(_e576.x, _e576.y, _e576.z, clamp(_e580, 0f, 1f));
                }
            }
        }
        continuing {
            let _e349: i32 = i_2;
            i_2 = (_e349 + 1i);
        }
    }
    return vec4(0f);
}

fn volumetric_scattering(worldPos_6: vec3<f32>) -> vec3<f32> {
    var worldPos_7: vec3<f32>;
    var cameraRay: vec3<f32>;
    var rayLength: f32;
    var rayDir_1: vec3<f32>;
    var scatter: vec3<f32> = vec3(0f);
    var i_3: i32 = 0i;
    var toLight: vec3<f32>;
    var alongRay: f32;
    var closest: vec3<f32>;
    var radius_2: f32;
    var distanceToRay: f32;
    var beam: f32;
    var distanceFade: f32;

    worldPos_7 = worldPos_6;
    let _e270: i32 = global.volumetric_fog;
    let _e273: i32 = global.dynamic_light_count;
    if ((_e270 == 0i) || (_e273 <= 0i)) {
        return vec3(0f);
    }
    let _e279: vec3<f32> = worldPos_7;
    let _e280: vec3<f32> = global.camera_position;
    cameraRay = (_e279 - _e280);
    let _e284: vec3<f32> = cameraRay;
    rayLength = length(_e284);
    let _e287: f32 = rayLength;
    if (_e287 <= 0.001f) {
        return vec3(0f);
    }
    let _e292: vec3<f32> = cameraRay;
    let _e293: f32 = rayLength;
    rayDir_1 = (_e292 / vec3(_e293));
    loop {
        let _e302: i32 = i_3;
        if !((_e302 < 8i)) {
            break;
        }
        {
            let _e309: i32 = i_3;
            let _e310: i32 = global.dynamic_light_count;
            if (_e309 >= _e310) {
                break;
            }
            let _e312: i32 = i_3;
            let _e314: vec4<f32> = global.dynamic_light_pos[_e312];
            let _e316: vec3<f32> = global.camera_position;
            toLight = (_e314.xyz - _e316);
            let _e321: vec3<f32> = toLight;
            let _e322: vec3<f32> = rayDir_1;
            let _e328: vec3<f32> = toLight;
            let _e329: vec3<f32> = rayDir_1;
            let _e332: f32 = rayLength;
            alongRay = clamp(dot(_e328, _e329), 0f, _e332);
            let _e335: vec3<f32> = global.camera_position;
            let _e336: vec3<f32> = rayDir_1;
            let _e337: f32 = alongRay;
            closest = (_e335 + (_e336 * _e337));
            let _e341: i32 = i_3;
            let _e343: vec4<f32> = global.dynamic_light_pos[_e341];
            let _e348: i32 = i_3;
            let _e350: vec4<f32> = global.dynamic_light_pos[_e348];
            radius_2 = max((_e350.w * 1.4f), 0.5f);
            let _e357: i32 = i_3;
            let _e359: vec4<f32> = global.dynamic_light_pos[_e357];
            let _e361: vec3<f32> = closest;
            let _e363: i32 = i_3;
            let _e365: vec4<f32> = global.dynamic_light_pos[_e363];
            let _e367: vec3<f32> = closest;
            distanceToRay = length((_e365.xyz - _e367));
            let _e372: f32 = radius_2;
            let _e377: f32 = radius_2;
            let _e380: f32 = radius_2;
            let _e381: f32 = distanceToRay;
            beam = (1f - smoothstep((_e377 * 0.08f), _e380, _e381));
            let _e389: vec3<f32> = toLight;
            let _e394: vec3<f32> = toLight;
            distanceFade = (1f - smoothstep(8f, 85f, length(_e394)));
            let _e399: vec3<f32> = scatter;
            let _e400: i32 = i_3;
            let _e402: vec4<f32> = global.dynamic_light_color[_e400];
            let _e404: i32 = i_3;
            let _e406: vec4<f32> = global.dynamic_light_color[_e404];
            let _e409: f32 = beam;
            let _e411: f32 = beam;
            let _e413: f32 = distanceFade;
            scatter = (_e399 + (((((_e402.xyz * _e406.w) * _e409) * _e411) * _e413) * 0.1f));
        }
        continuing {
            let _e306: i32 = i_3;
            i_3 = (_e306 + 1i);
        }
    }
    let _e418: vec3<f32> = scatter;
    return _e418;
}

fn ambient_occlusion(uv_38: vec2<f32>, centerDepth_2: f32) -> f32 {
    var uv_39: vec2<f32>;
    var centerDepth_3: f32;
    var c_6: f32;
    var local_3: f32;
    var local_4: f32;
    var radius_3: f32;
    var bias_1: f32;
    var range: f32;
    var occ: f32 = 0f;
    var dx_1: vec2<f32>;
    var dy_1: vec2<f32>;
    var dg: vec2<f32>;
    var z0_: f32;
    var z1_: f32;
    var z2_: f32;
    var z3_: f32;
    var z4_: f32;
    var z5_: f32;
    var z6_: f32;
    var z7_: f32;
    var local_5: f32;
    var local_6: f32;
    var strength: f32;

    uv_39 = uv_38;
    centerDepth_3 = centerDepth_2;
    let _e272: i32 = global.depth_available;
    let _e275: i32 = global.ao_quality;
    let _e279: f32 = centerDepth_3;
    if (((_e272 == 0i) || (_e275 == 0i)) || (_e279 >= 0.999999f)) {
        return 1f;
    }
    let _e285: f32 = centerDepth_3;
    let _e286: f32 = view_depth(_e285);
    c_6 = _e286;
    let _e288: i32 = global.ao_quality;
    if (_e288 == 1i) {
        local_4 = 1.5f;
    } else {
        let _e292: i32 = global.ao_quality;
        if (_e292 == 2i) {
            local_3 = 2.5f;
        } else {
            local_3 = 4f;
        }
        let _e298: f32 = local_3;
        local_4 = _e298;
    }
    let _e300: f32 = local_4;
    radius_3 = _e300;
    let _e303: f32 = c_6;
    let _e307: f32 = c_6;
    bias_1 = max(0.015f, (_e307 * 0.002f));
    let _e313: f32 = c_6;
    let _e317: f32 = c_6;
    range = max(0.12f, (_e317 * 0.035f));
    let _e324: vec2<f32> = global.texel;
    let _e326: f32 = radius_3;
    dx_1 = vec2<f32>((_e324.x * _e326), 0f);
    let _e332: vec2<f32> = global.texel;
    let _e334: f32 = radius_3;
    dy_1 = vec2<f32>(0f, (_e332.y * _e334));
    let _e338: vec2<f32> = global.texel;
    let _e340: f32 = radius_3;
    let _e344: vec2<f32> = global.texel;
    let _e346: f32 = radius_3;
    dg = vec2<f32>(((_e338.x * _e340) * 0.7071f), ((_e344.y * _e346) * 0.7071f));
    let _e352: vec2<f32> = uv_39;
    let _e353: vec2<f32> = dx_1;
    let _e355: vec2<f32> = uv_39;
    let _e356: vec2<f32> = dx_1;
    let _e358: f32 = raw_depth((_e355 + _e356));
    let _e359: vec2<f32> = uv_39;
    let _e360: vec2<f32> = dx_1;
    let _e362: vec2<f32> = uv_39;
    let _e363: vec2<f32> = dx_1;
    let _e365: f32 = raw_depth((_e362 + _e363));
    let _e366: f32 = view_depth(_e365);
    z0_ = _e366;
    let _e368: vec2<f32> = uv_39;
    let _e369: vec2<f32> = dx_1;
    let _e371: vec2<f32> = uv_39;
    let _e372: vec2<f32> = dx_1;
    let _e374: f32 = raw_depth((_e371 - _e372));
    let _e375: vec2<f32> = uv_39;
    let _e376: vec2<f32> = dx_1;
    let _e378: vec2<f32> = uv_39;
    let _e379: vec2<f32> = dx_1;
    let _e381: f32 = raw_depth((_e378 - _e379));
    let _e382: f32 = view_depth(_e381);
    z1_ = _e382;
    let _e384: vec2<f32> = uv_39;
    let _e385: vec2<f32> = dy_1;
    let _e387: vec2<f32> = uv_39;
    let _e388: vec2<f32> = dy_1;
    let _e390: f32 = raw_depth((_e387 + _e388));
    let _e391: vec2<f32> = uv_39;
    let _e392: vec2<f32> = dy_1;
    let _e394: vec2<f32> = uv_39;
    let _e395: vec2<f32> = dy_1;
    let _e397: f32 = raw_depth((_e394 + _e395));
    let _e398: f32 = view_depth(_e397);
    z2_ = _e398;
    let _e400: vec2<f32> = uv_39;
    let _e401: vec2<f32> = dy_1;
    let _e403: vec2<f32> = uv_39;
    let _e404: vec2<f32> = dy_1;
    let _e406: f32 = raw_depth((_e403 - _e404));
    let _e407: vec2<f32> = uv_39;
    let _e408: vec2<f32> = dy_1;
    let _e410: vec2<f32> = uv_39;
    let _e411: vec2<f32> = dy_1;
    let _e413: f32 = raw_depth((_e410 - _e411));
    let _e414: f32 = view_depth(_e413);
    z3_ = _e414;
    let _e416: vec2<f32> = uv_39;
    let _e417: vec2<f32> = dg;
    let _e419: vec2<f32> = uv_39;
    let _e420: vec2<f32> = dg;
    let _e422: f32 = raw_depth((_e419 + _e420));
    let _e423: vec2<f32> = uv_39;
    let _e424: vec2<f32> = dg;
    let _e426: vec2<f32> = uv_39;
    let _e427: vec2<f32> = dg;
    let _e429: f32 = raw_depth((_e426 + _e427));
    let _e430: f32 = view_depth(_e429);
    z4_ = _e430;
    let _e432: vec2<f32> = uv_39;
    let _e433: vec2<f32> = dg;
    let _e435: vec2<f32> = uv_39;
    let _e436: vec2<f32> = dg;
    let _e438: f32 = raw_depth((_e435 - _e436));
    let _e439: vec2<f32> = uv_39;
    let _e440: vec2<f32> = dg;
    let _e442: vec2<f32> = uv_39;
    let _e443: vec2<f32> = dg;
    let _e445: f32 = raw_depth((_e442 - _e443));
    let _e446: f32 = view_depth(_e445);
    z5_ = _e446;
    let _e448: vec2<f32> = uv_39;
    let _e449: vec2<f32> = dg;
    let _e451: vec2<f32> = dg;
    let _e456: vec2<f32> = uv_39;
    let _e457: vec2<f32> = dg;
    let _e459: vec2<f32> = dg;
    let _e464: f32 = raw_depth((_e456 + vec2<f32>(_e457.x, -(_e459.y))));
    let _e465: vec2<f32> = uv_39;
    let _e466: vec2<f32> = dg;
    let _e468: vec2<f32> = dg;
    let _e473: vec2<f32> = uv_39;
    let _e474: vec2<f32> = dg;
    let _e476: vec2<f32> = dg;
    let _e481: f32 = raw_depth((_e473 + vec2<f32>(_e474.x, -(_e476.y))));
    let _e482: f32 = view_depth(_e481);
    z6_ = _e482;
    let _e484: vec2<f32> = uv_39;
    let _e485: vec2<f32> = dg;
    let _e488: vec2<f32> = dg;
    let _e492: vec2<f32> = uv_39;
    let _e493: vec2<f32> = dg;
    let _e496: vec2<f32> = dg;
    let _e500: f32 = raw_depth((_e492 + vec2<f32>(-(_e493.x), _e496.y)));
    let _e501: vec2<f32> = uv_39;
    let _e502: vec2<f32> = dg;
    let _e505: vec2<f32> = dg;
    let _e509: vec2<f32> = uv_39;
    let _e510: vec2<f32> = dg;
    let _e513: vec2<f32> = dg;
    let _e517: f32 = raw_depth((_e509 + vec2<f32>(-(_e510.x), _e513.y)));
    let _e518: f32 = view_depth(_e517);
    z7_ = _e518;
    let _e520: f32 = occ;
    let _e523: f32 = c_6;
    let _e524: f32 = z0_;
    let _e526: f32 = bias_1;
    let _e527: f32 = range;
    let _e528: f32 = c_6;
    let _e529: f32 = z0_;
    occ = (_e520 + smoothstep(_e526, _e527, (_e528 - _e529)));
    let _e533: f32 = occ;
    let _e536: f32 = c_6;
    let _e537: f32 = z1_;
    let _e539: f32 = bias_1;
    let _e540: f32 = range;
    let _e541: f32 = c_6;
    let _e542: f32 = z1_;
    occ = (_e533 + smoothstep(_e539, _e540, (_e541 - _e542)));
    let _e546: f32 = occ;
    let _e549: f32 = c_6;
    let _e550: f32 = z2_;
    let _e552: f32 = bias_1;
    let _e553: f32 = range;
    let _e554: f32 = c_6;
    let _e555: f32 = z2_;
    occ = (_e546 + smoothstep(_e552, _e553, (_e554 - _e555)));
    let _e559: f32 = occ;
    let _e562: f32 = c_6;
    let _e563: f32 = z3_;
    let _e565: f32 = bias_1;
    let _e566: f32 = range;
    let _e567: f32 = c_6;
    let _e568: f32 = z3_;
    occ = (_e559 + smoothstep(_e565, _e566, (_e567 - _e568)));
    let _e572: f32 = occ;
    let _e575: f32 = c_6;
    let _e576: f32 = z4_;
    let _e578: f32 = bias_1;
    let _e579: f32 = range;
    let _e580: f32 = c_6;
    let _e581: f32 = z4_;
    occ = (_e572 + smoothstep(_e578, _e579, (_e580 - _e581)));
    let _e585: f32 = occ;
    let _e588: f32 = c_6;
    let _e589: f32 = z5_;
    let _e591: f32 = bias_1;
    let _e592: f32 = range;
    let _e593: f32 = c_6;
    let _e594: f32 = z5_;
    occ = (_e585 + smoothstep(_e591, _e592, (_e593 - _e594)));
    let _e598: f32 = occ;
    let _e601: f32 = c_6;
    let _e602: f32 = z6_;
    let _e604: f32 = bias_1;
    let _e605: f32 = range;
    let _e606: f32 = c_6;
    let _e607: f32 = z6_;
    occ = (_e598 + smoothstep(_e604, _e605, (_e606 - _e607)));
    let _e611: f32 = occ;
    let _e614: f32 = c_6;
    let _e615: f32 = z7_;
    let _e617: f32 = bias_1;
    let _e618: f32 = range;
    let _e619: f32 = c_6;
    let _e620: f32 = z7_;
    occ = (_e611 + smoothstep(_e617, _e618, (_e619 - _e620)));
    let _e624: i32 = global.ao_quality;
    if (_e624 == 1i) {
        local_6 = 0.1f;
    } else {
        let _e628: i32 = global.ao_quality;
        if (_e628 == 2i) {
            local_5 = 0.17f;
        } else {
            local_5 = 0.24f;
        }
        let _e634: f32 = local_5;
        local_6 = _e634;
    }
    let _e636: f32 = local_6;
    strength = _e636;
    let _e639: f32 = occ;
    let _e640: f32 = strength;
    let _e648: f32 = occ;
    let _e649: f32 = strength;
    return clamp((1f - (_e648 * (_e649 / 8f))), 0.68f, 1f);
}

fn contact_shadow(uv_40: vec2<f32>, centerDepth_4: f32) -> f32 {
    var uv_41: vec2<f32>;
    var centerDepth_5: f32;
    var c_7: f32;
    var dir_1: vec2<f32>;
    var shadow: f32 = 0f;
    var bias_2: f32;
    var i_4: i32 = 1i;
    var fi_1: f32;
    var z_1: f32;
    var delta_2: f32;

    uv_41 = uv_40;
    centerDepth_5 = centerDepth_4;
    let _e272: i32 = global.depth_available;
    let _e275: i32 = global.contact_shadows;
    let _e279: f32 = centerDepth_5;
    if (((_e272 == 0i) || (_e275 == 0i)) || (_e279 >= 0.999999f)) {
        return 1f;
    }
    let _e285: f32 = centerDepth_5;
    let _e286: f32 = view_depth(_e285);
    c_7 = _e286;
    dir_1 = normalize(vec2<f32>(-0.65f, 0.75f));
    let _e301: f32 = c_7;
    let _e305: f32 = c_7;
    bias_2 = max(0.01f, (_e305 * 0.0015f));
    loop {
        let _e312: i32 = i_4;
        if !((_e312 <= 4i)) {
            break;
        }
        {
            let _e319: i32 = i_4;
            fi_1 = f32(_e319);
            let _e322: vec2<f32> = uv_41;
            let _e323: vec2<f32> = dir_1;
            let _e324: vec2<f32> = global.texel;
            let _e326: f32 = fi_1;
            let _e331: vec2<f32> = uv_41;
            let _e332: vec2<f32> = dir_1;
            let _e333: vec2<f32> = global.texel;
            let _e335: f32 = fi_1;
            let _e340: f32 = raw_depth((_e331 + ((_e332 * _e333) * (_e335 * 2f))));
            let _e341: vec2<f32> = uv_41;
            let _e342: vec2<f32> = dir_1;
            let _e343: vec2<f32> = global.texel;
            let _e345: f32 = fi_1;
            let _e350: vec2<f32> = uv_41;
            let _e351: vec2<f32> = dir_1;
            let _e352: vec2<f32> = global.texel;
            let _e354: f32 = fi_1;
            let _e359: f32 = raw_depth((_e350 + ((_e351 * _e352) * (_e354 * 2f))));
            let _e360: f32 = view_depth(_e359);
            z_1 = _e360;
            let _e362: f32 = c_7;
            let _e363: f32 = z_1;
            delta_2 = (_e362 - _e363);
            let _e366: f32 = shadow;
            let _e368: f32 = bias_2;
            let _e371: f32 = c_7;
            let _e374: f32 = bias_2;
            let _e377: f32 = c_7;
            let _e382: f32 = bias_2;
            let _e383: f32 = bias_2;
            let _e386: f32 = c_7;
            let _e389: f32 = bias_2;
            let _e392: f32 = c_7;
            let _e396: f32 = delta_2;
            let _e399: f32 = fi_1;
            shadow = (_e366 + (smoothstep(_e382, max((_e389 * 8f), (_e392 * 0.025f)), _e396) * (1f / _e399)));
        }
        continuing {
            let _e316: i32 = i_4;
            i_4 = (_e316 + 1i);
        }
    }
    let _e404: f32 = shadow;
    let _e409: f32 = shadow;
    return (1f - clamp((_e409 * 0.055f), 0f, 0.18f));
}

fn bloom_value(uv_42: vec2<f32>) -> vec3<f32> {
    var uv_43: vec2<f32>;
    var local_7: f32;
    var threshold: f32;
    var sum: vec3<f32> = vec3(0f);
    var weight: f32 = 0f;
    var x2_: vec2<f32>;
    var y2_: vec2<f32>;
    var x4_: vec2<f32>;
    var y4_: vec2<f32>;
    var taps: array<vec3<f32>, 8>;
    var i_5: i32 = 0i;
    var b_3: f32;
    var local_8: f32;
    var intensity: f32;

    uv_43 = uv_42;
    let _e270: i32 = global.bloom_enable;
    let _e273: i32 = global.dynamic_glow;
    if ((_e270 == 0i) && (_e273 == 0i)) {
        return vec3(0f);
    }
    let _e279: i32 = global.dynamic_glow;
    if (_e279 != 0i) {
        local_7 = 0.48f;
    } else {
        local_7 = 0.62f;
    }
    let _e285: f32 = local_7;
    threshold = _e285;
    let _e292: vec2<f32> = global.texel;
    x2_ = vec2<f32>((_e292.x * 2f), 0f);
    let _e300: vec2<f32> = global.texel;
    y2_ = vec2<f32>(0f, (_e300.y * 2f));
    let _e306: vec2<f32> = global.texel;
    x4_ = vec2<f32>((_e306.x * 4f), 0f);
    let _e314: vec2<f32> = global.texel;
    y4_ = vec2<f32>(0f, (_e314.y * 4f));
    let _e323: vec2<f32> = uv_43;
    let _e324: vec2<f32> = x2_;
    let _e326: vec2<f32> = uv_43;
    let _e327: vec2<f32> = x2_;
    let _e329: vec3<f32> = scene((_e326 + _e327));
    taps[0i] = _e329;
    let _e332: vec2<f32> = uv_43;
    let _e333: vec2<f32> = x2_;
    let _e335: vec2<f32> = uv_43;
    let _e336: vec2<f32> = x2_;
    let _e338: vec3<f32> = scene((_e335 - _e336));
    taps[1i] = _e338;
    let _e341: vec2<f32> = uv_43;
    let _e342: vec2<f32> = y2_;
    let _e344: vec2<f32> = uv_43;
    let _e345: vec2<f32> = y2_;
    let _e347: vec3<f32> = scene((_e344 + _e345));
    taps[2i] = _e347;
    let _e350: vec2<f32> = uv_43;
    let _e351: vec2<f32> = y2_;
    let _e353: vec2<f32> = uv_43;
    let _e354: vec2<f32> = y2_;
    let _e356: vec3<f32> = scene((_e353 - _e354));
    taps[3i] = _e356;
    let _e359: vec2<f32> = uv_43;
    let _e360: vec2<f32> = x4_;
    let _e362: vec2<f32> = uv_43;
    let _e363: vec2<f32> = x4_;
    let _e365: vec3<f32> = scene((_e362 + _e363));
    taps[4i] = _e365;
    let _e368: vec2<f32> = uv_43;
    let _e369: vec2<f32> = x4_;
    let _e371: vec2<f32> = uv_43;
    let _e372: vec2<f32> = x4_;
    let _e374: vec3<f32> = scene((_e371 - _e372));
    taps[5i] = _e374;
    let _e377: vec2<f32> = uv_43;
    let _e378: vec2<f32> = y4_;
    let _e380: vec2<f32> = uv_43;
    let _e381: vec2<f32> = y4_;
    let _e383: vec3<f32> = scene((_e380 + _e381));
    taps[6i] = _e383;
    let _e386: vec2<f32> = uv_43;
    let _e387: vec2<f32> = y4_;
    let _e389: vec2<f32> = uv_43;
    let _e390: vec2<f32> = y4_;
    let _e392: vec3<f32> = scene((_e389 - _e390));
    taps[7i] = _e392;
    loop {
        let _e395: i32 = i_5;
        if !((_e395 < 8i)) {
            break;
        }
        {
            let _e403: i32 = i_5;
            let _e406: i32 = i_5;
            let _e408: vec3<f32> = taps[_e406];
            let _e409: f32 = luma(_e408);
            let _e410: f32 = threshold;
            let _e413: i32 = i_5;
            let _e416: i32 = i_5;
            let _e418: vec3<f32> = taps[_e416];
            let _e419: f32 = luma(_e418);
            let _e420: f32 = threshold;
            b_3 = max(0f, (_e419 - _e420));
            let _e424: f32 = b_3;
            let _e427: f32 = threshold;
            let _e431: f32 = threshold;
            b_3 = (_e424 / max(0.001f, (1f - _e431)));
            let _e435: vec3<f32> = sum;
            let _e436: i32 = i_5;
            let _e438: vec3<f32> = taps[_e436];
            let _e439: f32 = b_3;
            sum = (_e435 + (_e438 * _e439));
            let _e442: f32 = weight;
            let _e443: f32 = b_3;
            weight = (_e442 + _e443);
        }
        continuing {
            let _e399: i32 = i_5;
            i_5 = (_e399 + 1i);
        }
    }
    let _e445: f32 = weight;
    if (_e445 <= 0.0001f) {
        return vec3(0f);
    }
    let _e450: i32 = global.bloom_enable;
    if (_e450 != 0i) {
        let _e453: f32 = global.bloom_intensity;
        local_8 = _e453;
    } else {
        local_8 = 0.25f;
    }
    let _e456: f32 = local_8;
    intensity = _e456;
    let _e458: i32 = global.dynamic_glow;
    if (_e458 != 0i) {
        let _e461: f32 = intensity;
        intensity = (_e461 + 0.18f);
    }
    let _e464: vec3<f32> = sum;
    let _e467: f32 = weight;
    let _e472: f32 = weight;
    let _e477: f32 = weight;
    let _e484: f32 = intensity;
    return (((_e464 / vec3(max(_e467, 1f))) * clamp((_e477 / 5f), 0f, 1f)) * _e484);
}

fn grade(c_8: vec3<f32>) -> vec3<f32> {
    var c_9: vec3<f32>;
    var y_1: f32;
    var y_2: f32;
    var shadows: vec3<f32>;
    var highlights: vec3<f32>;

    c_9 = c_8;
    let _e270: i32 = global.grade_mode;
    if (_e270 == 1i) {
        {
            let _e276: vec3<f32> = c_9;
            let _e285: vec3<f32> = c_9;
            c_9 = pow(max(_e285, vec3(0f)), vec3(0.96f));
            let _e292: vec3<f32> = c_9;
            c_9 = (_e292 * vec3<f32>(1.015f, 1.01f, 1.02f));
        }
    } else {
        let _e298: i32 = global.grade_mode;
        if (_e298 == 2i) {
            {
                let _e302: vec3<f32> = c_9;
                let _e303: f32 = luma(_e302);
                y_1 = _e303;
                let _e305: f32 = y_1;
                let _e309: f32 = y_1;
                let _e311: vec3<f32> = c_9;
                c_9 = mix(vec3(_e309), _e311, vec3(1.16f));
                let _e315: vec3<f32> = c_9;
                c_9 = (_e315 * vec3<f32>(1.025f, 1.01f, 1.02f));
            }
        } else {
            let _e321: i32 = global.grade_mode;
            if (_e321 == 3i) {
                {
                    let _e325: vec3<f32> = c_9;
                    let _e326: f32 = luma(_e325);
                    y_2 = _e326;
                    let _e328: vec3<f32> = c_9;
                    shadows = (_e328 * vec3<f32>(0.94f, 0.98f, 1.06f));
                    let _e335: vec3<f32> = c_9;
                    highlights = (_e335 * vec3<f32>(1.06f, 1.015f, 0.96f));
                    let _e349: f32 = y_2;
                    let _e351: vec3<f32> = shadows;
                    let _e352: vec3<f32> = highlights;
                    let _e358: f32 = y_2;
                    c_9 = mix(_e351, _e352, vec3(smoothstep(0.2f, 0.82f, _e358)));
                    let _e363: vec3<f32> = c_9;
                    let _e364: f32 = luma(_e363);
                    let _e369: vec3<f32> = c_9;
                    let _e370: f32 = luma(_e369);
                    let _e372: vec3<f32> = c_9;
                    c_9 = mix(vec3(_e370), _e372, vec3(1.1f));
                }
            }
        }
    }
    let _e376: vec3<f32> = c_9;
    return _e376;
}

fn aces(x_1: vec3<f32>) -> vec3<f32> {
    var x_2: vec3<f32>;

    x_2 = x_1;
    let _e270: vec3<f32> = x_2;
    let _e272: vec3<f32> = x_2;
    let _e278: vec3<f32> = x_2;
    let _e280: vec3<f32> = x_2;
    let _e292: vec3<f32> = x_2;
    let _e294: vec3<f32> = x_2;
    let _e300: vec3<f32> = x_2;
    let _e302: vec3<f32> = x_2;
    return clamp(((_e292 * ((2.51f * _e294) + vec3(0.03f))) / ((_e300 * ((2.43f * _e302) + vec3(0.59f))) + vec3(0.14f))), vec3(0f), vec3(1f));
}

fn main_1() {
    var uv_44: vec2<f32>;
    var center_4: vec3<f32>;
    var d_16: f32;
    var needWorld: bool;
    var local_9: vec3<f32>;
    var worldP: vec3<f32>;
    var local_10: vec3<f32>;
    var deferred: vec3<f32>;
    var color: vec3<f32>;
    var authoredY: f32;
    var pbrY: f32;
    var exposure: f32;
    var balancedPbr: vec3<f32>;
    var local_11: vec3<f32>;
    var blur: vec3<f32>;
    var detail: vec3<f32>;
    var guard: f32;
    var n_11: vec3<f32> = vec3<f32>(0f, 0f, 1f);
    var pbrPixel: bool;
    var local_12: vec3<f32>;
    var ld: vec3<f32>;
    var diffuse: f32;
    var relief: f32;
    var spec_1: f32;
    var worldPos_8: vec3<f32>;
    var fresnel: f32;
    var reflected: vec4<f32>;
    var reflectionWeight: f32;
    var dist_1: f32;
    var f_2: f32;
    var normalizedDistance: f32;
    var fog: f32;
    var wave: f32;
    var local_13: f32;
    var amount: f32;
    var y_3: f32;

    let _e268: vec2<f32> = texcoord_1;
    uv_44 = _e268;
    let _e271: vec2<f32> = uv_44;
    let _e272: vec3<f32> = scene(_e271);
    center_4 = _e272;
    let _e275: vec2<f32> = uv_44;
    let _e276: f32 = raw_depth(_e275);
    d_16 = _e276;
    let _e278: i32 = global.pbr_enabled;
    let _e281: i32 = global.shadow_enabled;
    let _e285: i32 = global.dynamic_light_count;
    needWorld = (((_e278 != 0i) || (_e281 != 0i)) || (_e285 > 0i));
    let _e290: bool = needWorld;
    let _e291: f32 = d_16;
    if (_e290 && (_e291 < 0.999999f)) {
        let _e297: vec2<f32> = uv_44;
        let _e298: f32 = d_16;
        let _e299: vec3<f32> = world_position(_e297, _e298);
        local_9 = _e299;
    } else {
        local_9 = vec3(0f);
    }
    let _e303: vec3<f32> = local_9;
    worldP = _e303;
    let _e305: i32 = global.pbr_enabled;
    let _e308: f32 = d_16;
    if ((_e305 != 0i) && (_e308 < 0.999999f)) {
        let _e314: vec2<f32> = uv_44;
        let _e315: vec3<f32> = worldP;
        let _e316: vec3<f32> = deferred_pbr(_e314, _e315);
        local_10 = _e316;
    } else {
        local_10 = vec3(-1f);
    }
    let _e321: vec3<f32> = local_10;
    deferred = _e321;
    let _e324: vec3<f32> = deferred;
    if (_e324.x >= 0f) {
        {
            let _e329: vec3<f32> = center_4;
            let _e330: f32 = luma(_e329);
            let _e333: vec3<f32> = center_4;
            let _e334: f32 = luma(_e333);
            authoredY = max(_e334, 0.025f);
            let _e339: vec3<f32> = deferred;
            let _e340: f32 = luma(_e339);
            let _e343: vec3<f32> = deferred;
            let _e344: f32 = luma(_e343);
            pbrY = max(_e344, 0.025f);
            let _e348: f32 = authoredY;
            let _e349: f32 = pbrY;
            let _e353: f32 = authoredY;
            let _e354: f32 = pbrY;
            exposure = clamp((_e353 / _e354), 0.65f, 1.55f);
            let _e360: vec3<f32> = deferred;
            let _e361: f32 = exposure;
            balancedPbr = (_e360 * _e361);
            let _e367: vec3<f32> = center_4;
            let _e368: vec3<f32> = balancedPbr;
            color = mix(_e367, _e368, vec3(0.48f));
        }
    } else {
        {
            let _e372: i32 = global.aa_mode;
            if (_e372 == 4i) {
                let _e378: vec2<f32> = uv_44;
                let _e379: vec3<f32> = center_4;
                let _e380: f32 = d_16;
                let _e381: vec3<f32> = temporal_resolve(_e378, _e379, _e380);
                local_11 = _e381;
            } else {
                let _e384: vec2<f32> = uv_44;
                let _e385: vec3<f32> = center_4;
                let _e386: vec3<f32> = fxaa(_e384, _e385);
                local_11 = _e386;
            }
            let _e388: vec3<f32> = local_11;
            color = _e388;
        }
    }
    let _e389: f32 = global.sharpen_strength;
    let _e392: vec3<f32> = deferred;
    if ((_e389 > 0.0001f) && (_e392.x < 0f)) {
        {
            let _e397: vec2<f32> = uv_44;
            let _e398: vec2<f32> = global.texel;
            let _e403: vec2<f32> = uv_44;
            let _e404: vec2<f32> = global.texel;
            let _e409: vec3<f32> = scene((_e403 + vec2<f32>(_e404.x, 0f)));
            let _e410: vec2<f32> = uv_44;
            let _e411: vec2<f32> = global.texel;
            let _e416: vec2<f32> = uv_44;
            let _e417: vec2<f32> = global.texel;
            let _e422: vec3<f32> = scene((_e416 - vec2<f32>(_e417.x, 0f)));
            let _e424: vec2<f32> = uv_44;
            let _e426: vec2<f32> = global.texel;
            let _e430: vec2<f32> = uv_44;
            let _e432: vec2<f32> = global.texel;
            let _e436: vec3<f32> = scene((_e430 + vec2<f32>(0f, _e432.y)));
            let _e438: vec2<f32> = uv_44;
            let _e440: vec2<f32> = global.texel;
            let _e444: vec2<f32> = uv_44;
            let _e446: vec2<f32> = global.texel;
            let _e450: vec3<f32> = scene((_e444 - vec2<f32>(0f, _e446.y)));
            blur = ((((_e409 + _e422) + _e436) + _e450) * 0.25f);
            let _e455: vec3<f32> = color;
            let _e456: vec3<f32> = blur;
            detail = (_e455 - _e456);
            let _e463: vec3<f32> = detail;
            let _e468: vec3<f32> = detail;
            guard = (1f - smoothstep(0.16f, 0.45f, length(_e468)));
            let _e473: vec3<f32> = color;
            let _e474: vec3<f32> = detail;
            let _e475: f32 = global.sharpen_strength;
            let _e479: f32 = guard;
            color = (_e473 + (((_e474 * _e475) * 0.75f) * _e479));
        }
    }
    let _e482: i32 = global.depth_available;
    let _e485: f32 = d_16;
    if ((_e482 != 0i) && (_e485 < 0.999999f)) {
        {
            let _e494: i32 = global.enhanced_lighting;
            let _e497: i32 = global.shadow_enabled;
            let _e501: i32 = global.reflections;
            let _e505: i32 = global.volumetric_fog;
            if ((((_e494 != 0i) || (_e497 != 0i)) || (_e501 != 0i)) || (_e505 != 0i)) {
                {
                    let _e509: i32 = global.pbr_enabled;
                    let _e513: vec2<f32> = uv_44;
                    let _e514: vec4<f32> = prime_sample_pbr_normal(_e513);
                    pbrPixel = ((_e509 != 0i) && (_e514.w > 0.5f));
                    let _e520: bool = pbrPixel;
                    if _e520 {
                        let _e521: mat4x4<f32> = global.view_matrix;
                        let _e532: vec2<f32> = uv_44;
                        let _e533: vec4<f32> = prime_sample_pbr_normal(_e532);
                        let _e541: mat4x4<f32> = global.view_matrix;
                        let _e552: vec2<f32> = uv_44;
                        let _e553: vec4<f32> = prime_sample_pbr_normal(_e552);
                        local_12 = normalize((mat3x3<f32>(_e541[0].xyz, _e541[1].xyz, _e541[2].xyz) * ((_e553.xyz * 2f) - vec3(1f))));
                    } else {
                        let _e564: vec2<f32> = uv_44;
                        let _e565: f32 = d_16;
                        let _e566: vec3<f32> = depth_normal(_e564, _e565);
                        local_12 = _e566;
                    }
                    let _e568: vec3<f32> = local_12;
                    n_11 = _e568;
                }
            }
            let _e569: i32 = global.enhanced_lighting;
            let _e572: vec3<f32> = deferred;
            if ((_e569 != 0i) && (_e572.x < 0f)) {
                {
                    ld = normalize(vec3<f32>(-0.45f, 0.58f, 0.68f));
                    let _e591: vec3<f32> = n_11;
                    let _e592: vec3<f32> = ld;
                    diffuse = ((dot(_e591, _e592) * 0.5f) + 0.5f);
                    let _e604: f32 = diffuse;
                    relief = mix(0.93f, 1.09f, _e604);
                    let _e608: vec3<f32> = ld;
                    let _e611: vec3<f32> = ld;
                    let _e613: vec3<f32> = n_11;
                    let _e619: vec3<f32> = ld;
                    let _e622: vec3<f32> = ld;
                    let _e624: vec3<f32> = n_11;
                    let _e632: vec3<f32> = ld;
                    let _e635: vec3<f32> = ld;
                    let _e637: vec3<f32> = n_11;
                    let _e643: vec3<f32> = ld;
                    let _e646: vec3<f32> = ld;
                    let _e648: vec3<f32> = n_11;
                    let _e658: vec3<f32> = ld;
                    let _e661: vec3<f32> = ld;
                    let _e663: vec3<f32> = n_11;
                    let _e669: vec3<f32> = ld;
                    let _e672: vec3<f32> = ld;
                    let _e674: vec3<f32> = n_11;
                    let _e682: vec3<f32> = ld;
                    let _e685: vec3<f32> = ld;
                    let _e687: vec3<f32> = n_11;
                    let _e693: vec3<f32> = ld;
                    let _e696: vec3<f32> = ld;
                    let _e698: vec3<f32> = n_11;
                    spec_1 = pow(max(0f, dot(reflect(-(_e696), _e698), vec3<f32>(0f, 0f, 1f))), 18f);
                    let _e709: vec3<f32> = color;
                    let _e710: f32 = relief;
                    let _e712: f32 = spec_1;
                    color = ((_e709 * _e710) + vec3((_e712 * 0.035f)));
                }
            }
            let _e717: vec3<f32> = worldP;
            worldPos_8 = _e717;
            let _e719: vec3<f32> = color;
            let _e722: vec2<f32> = uv_44;
            let _e723: f32 = d_16;
            let _e724: f32 = ambient_occlusion(_e722, _e723);
            color = (_e719 * _e724);
            let _e726: vec3<f32> = color;
            let _e729: vec2<f32> = uv_44;
            let _e730: f32 = d_16;
            let _e731: f32 = contact_shadow(_e729, _e730);
            color = (_e726 * _e731);
            let _e733: vec3<f32> = color;
            let _e736: vec3<f32> = worldPos_8;
            let _e737: vec3<f32> = n_11;
            let _e738: f32 = directional_shadow(_e736, _e737);
            color = (_e733 * _e738);
            let _e740: vec3<f32> = deferred;
            if (_e740.x < 0f) {
                let _e744: vec3<f32> = color;
                let _e746: vec3<f32> = worldPos_8;
                let _e747: vec3<f32> = projectile_lighting(_e746);
                color = (_e744 + _e747);
            }
            let _e749: i32 = global.reflections;
            if (_e749 != 0i) {
                {
                    let _e753: vec3<f32> = n_11;
                    let _e759: vec3<f32> = n_11;
                    let _e767: vec3<f32> = n_11;
                    let _e773: vec3<f32> = n_11;
                    fresnel = pow(clamp((1f - _e773.z), 0f, 1f), 3f);
                    let _e785: vec2<f32> = uv_44;
                    let _e786: f32 = d_16;
                    let _e787: vec3<f32> = n_11;
                    let _e788: vec4<f32> = screen_reflection(_e785, _e786, _e787);
                    reflected = _e788;
                    let _e790: f32 = fresnel;
                    let _e793: vec4<f32> = reflected;
                    reflectionWeight = ((_e790 * 0.18f) * _e793.w);
                    let _e798: vec4<f32> = reflected;
                    let _e801: vec3<f32> = color;
                    let _e802: vec4<f32> = reflected;
                    let _e804: f32 = reflectionWeight;
                    color = mix(_e801, _e802.xyz, vec3(_e804));
                }
            }
            let _e807: i32 = global.enhanced_fog;
            let _e810: i32 = global.volumetric_fog;
            if ((_e807 != 0i) || (_e810 != 0i)) {
                {
                    let _e815: f32 = d_16;
                    let _e816: f32 = view_depth(_e815);
                    dist_1 = _e816;
                    let _e819: f32 = global.near_plane;
                    let _e822: f32 = global.far_plane;
                    let _e823: f32 = global.near_plane;
                    f_2 = max(_e822, (_e823 + 1f));
                    let _e828: f32 = dist_1;
                    let _e829: f32 = f_2;
                    let _e833: f32 = dist_1;
                    let _e834: f32 = f_2;
                    normalizedDistance = clamp((_e833 / _e834), 0f, 1f);
                    let _e841: f32 = normalizedDistance;
                    let _e845: f32 = normalizedDistance;
                    fog = (1f - exp((-(_e845) * 2.35f)));
                    let _e852: i32 = global.volumetric_fog;
                    if (_e852 != 0i) {
                        {
                            let _e855: vec2<f32> = uv_44;
                            let _e859: f32 = global.time_value;
                            let _e863: vec2<f32> = uv_44;
                            let _e867: f32 = global.time_value;
                            let _e872: vec2<f32> = uv_44;
                            let _e876: f32 = global.time_value;
                            let _e880: vec2<f32> = uv_44;
                            let _e884: f32 = global.time_value;
                            wave = (sin(((_e863.x * 37f) + (_e867 * 0.27f))) * sin(((_e880.y * 29f) - (_e884 * 0.19f))));
                            let _e891: f32 = fog;
                            let _e894: f32 = wave;
                            fog = (_e891 * (0.88f + (0.12f * _e894)));
                            let _e898: f32 = fog;
                            let _e900: vec3<f32> = n_11;
                            fog = (_e898 + ((1f - _e900.z) * 0.025f));
                        }
                    }
                    let _e906: i32 = global.enhanced_fog;
                    if (_e906 != 0i) {
                        local_13 = 0.18f;
                    } else {
                        local_13 = 0.08f;
                    }
                    let _e912: f32 = local_13;
                    amount = _e912;
                    let _e914: i32 = global.volumetric_fog;
                    if (_e914 != 0i) {
                        let _e917: f32 = amount;
                        amount = (_e917 + 0.1f);
                    }
                    let _e921: vec4<f32> = global.fog_color;
                    let _e923: f32 = fog;
                    let _e924: f32 = amount;
                    let _e928: f32 = fog;
                    let _e929: f32 = amount;
                    let _e934: vec3<f32> = color;
                    let _e935: vec4<f32> = global.fog_color;
                    let _e937: f32 = fog;
                    let _e938: f32 = amount;
                    let _e942: f32 = fog;
                    let _e943: f32 = amount;
                    color = mix(_e934, _e935.xyz, vec3(clamp((_e942 * _e943), 0f, 0.32f)));
                    let _e950: vec3<f32> = color;
                    let _e952: vec3<f32> = worldPos_8;
                    let _e953: vec3<f32> = volumetric_scattering(_e952);
                    color = (_e950 + _e953);
                }
            }
        }
    }
    let _e955: i32 = global.pbr_enabled;
    if (_e955 == 2i) {
        {
            let _e958: vec3<f32> = color;
            prime_output = vec4<f32>(_e958.x, _e958.y, _e958.z, 1f);
            return;
        }
    }
    let _e964: vec3<f32> = color;
    let _e966: vec2<f32> = uv_44;
    let _e967: vec3<f32> = bloom_value(_e966);
    color = (_e964 + _e967);
    let _e970: vec3<f32> = color;
    let _e971: vec3<f32> = grade(_e970);
    color = _e971;
    let _e973: vec3<f32> = color;
    let _e974: f32 = luma(_e973);
    y_3 = _e974;
    let _e976: f32 = y_3;
    let _e982: f32 = global.saturation_value;
    let _e984: f32 = y_3;
    let _e986: vec3<f32> = color;
    let _e990: f32 = global.saturation_value;
    color = mix(vec3(_e984), _e986, vec3(max(0f, _e990)));
    let _e994: vec3<f32> = color;
    let _e998: f32 = global.contrast_value;
    color = (((_e994 - vec3(0.5f)) * _e998) + vec3(0.5f));
    let _e1006: vec3<f32> = color;
    let _e1014: f32 = global.gamma_value;
    let _e1021: vec3<f32> = color;
    let _e1029: f32 = global.gamma_value;
    color = pow(max(_e1021, vec3(0f)), vec3((1f / max(0.25f, _e1029))));
    let _e1034: i32 = global.hdr_mode;
    if (_e1034 != 0i) {
        {
            let _e1040: vec3<f32> = color;
            let _e1043: vec3<f32> = max(_e1040, vec3(0f));
            prime_output = vec4<f32>(_e1043.x, _e1043.y, _e1043.z, 1f);
            return;
        }
    } else {
        {
            let _e1052: vec3<f32> = color;
            let _e1057: vec3<f32> = clamp(_e1052, vec3(0f), vec3(1f));
            prime_output = vec4<f32>(_e1057.x, _e1057.y, _e1057.z, 1f);
            return;
        }
    }
}

@fragment
fn main(@location(0) texcoord: vec2<f32>) -> FragmentOutput {
    texcoord_1 = texcoord;
    main_1();
    let _e287: vec4<f32> = prime_output;
    return FragmentOutput(_e287);
}
