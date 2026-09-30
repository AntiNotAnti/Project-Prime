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

struct VertexOutput {
    @builtin(position) @invariant member: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
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
var<private> gl_Position: vec4<f32>;
var<private> prime_position_1: vec3<f32>;
var<private> prime_color_1: vec4<f32>;
var<private> prime_normal_1: vec3<f32>;
var<private> prime_uv_1: vec3<f32>;
var<private> prime_color_set_1: f32;
var<private> prime_normal_set_1: f32;
var<private> texcoord: vec2<f32>;

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
    let _e284: vec2<f32> = uv_5;
    let _e286: vec2<f32> = uv_5;
    let _e289: vec2<f32> = uv_5;
    let _e294: vec4<f32> = global.prime_texture_flip[2];
    let _e296: vec2<f32> = uv_5;
    let _e299: vec2<f32> = uv_5;
    let _e304: vec4<f32> = global.prime_texture_flip[2];
    let _e308: vec4<f32> = textureSample(prime_tex_history_tex, prime_sampler_history_tex, vec2<f32>(_e284.x, mix(_e296.y, (1f - _e299.y), _e304.x)));
    return _e308;
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
    let _e286: vec2<f32> = uv_7;
    let _e288: vec2<f32> = uv_7;
    let _e291: vec2<f32> = uv_7;
    let _e296: vec4<f32> = global.prime_texture_flip[3];
    let _e298: vec2<f32> = uv_7;
    let _e301: vec2<f32> = uv_7;
    let _e306: vec4<f32> = global.prime_texture_flip[3];
    let _e310: vec4<f32> = textureSample(prime_tex_pbr_albedo, prime_sampler_pbr_albedo, vec2<f32>(_e286.x, mix(_e298.y, (1f - _e301.y), _e306.x)));
    return _e310;
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
    let _e288: vec2<f32> = uv_9;
    let _e290: vec2<f32> = uv_9;
    let _e293: vec2<f32> = uv_9;
    let _e298: vec4<f32> = global.prime_texture_flip[4];
    let _e300: vec2<f32> = uv_9;
    let _e303: vec2<f32> = uv_9;
    let _e308: vec4<f32> = global.prime_texture_flip[4];
    let _e312: vec4<f32> = textureSample(prime_tex_pbr_normal, prime_sampler_pbr_normal, vec2<f32>(_e288.x, mix(_e300.y, (1f - _e303.y), _e308.x)));
    return _e312;
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
    let _e290: vec2<f32> = uv_11;
    let _e292: vec2<f32> = uv_11;
    let _e295: vec2<f32> = uv_11;
    let _e300: vec4<f32> = global.prime_texture_flip[5];
    let _e302: vec2<f32> = uv_11;
    let _e305: vec2<f32> = uv_11;
    let _e310: vec4<f32> = global.prime_texture_flip[5];
    let _e314: vec4<f32> = textureSample(prime_tex_pbr_material, prime_sampler_pbr_material, vec2<f32>(_e290.x, mix(_e302.y, (1f - _e305.y), _e310.x)));
    return _e314;
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
    let _e292: vec2<f32> = uv_13;
    let _e294: vec2<f32> = uv_13;
    let _e297: vec2<f32> = uv_13;
    let _e302: vec4<f32> = global.prime_texture_flip[6];
    let _e304: vec2<f32> = uv_13;
    let _e307: vec2<f32> = uv_13;
    let _e312: vec4<f32> = global.prime_texture_flip[6];
    let _e316: vec4<f32> = textureSample(prime_tex_tex, prime_sampler_tex, vec2<f32>(_e292.x, mix(_e304.y, (1f - _e307.y), _e312.x)));
    return _e316;
}

fn prime_original_main() {
    let _e274: vec3<f32> = prime_position_1;
    let _e280: vec2<f32> = vec4<f32>(_e274.x, _e274.y, _e274.z, 1f).xy;
    gl_Position = vec4<f32>(_e280.x, _e280.y, 0f, 1f);
    let _e286: vec3<f32> = prime_uv_1;
    texcoord = vec4<f32>(_e286.x, _e286.y, _e286.z, 0f).xy;
    return;
}

fn main_1() {
    prime_original_main();
    let _e275: vec4<f32> = gl_Position;
    let _e277: vec4<f32> = gl_Position;
    gl_Position.z = ((_e275.z + _e277.w) * 0.5f);
    let _e282: vec4<f32> = gl_Position;
    let _e284: vec4<f32> = gl_Position;
    let _e286: vec4<f32> = global.prime_viewport;
    let _e289: vec4<f32> = global.prime_viewport;
    let _e291: vec4<f32> = gl_Position;
    let _e294: vec2<f32> = ((_e284.xy * _e286.xy) + (_e289.zw * _e291.w));
    gl_Position.x = _e294.x;
    gl_Position.y = _e294.y;
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
    let _e308: vec4<f32> = gl_Position;
    let _e310: vec2<f32> = texcoord;
    return VertexOutput(_e308, _e310);
}
