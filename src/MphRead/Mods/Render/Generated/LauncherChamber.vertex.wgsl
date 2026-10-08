struct PrimeUniforms {
    resolution: vec2<f32>,
    _pad_16_0_: f32,
    _pad_16_1_: f32,
    time_value: f32,
    _pad_32_0_: f32,
    _pad_32_1_: f32,
    _pad_32_2_: f32,
    base_top: vec3<f32>,
    _pad_48_0_: f32,
    base_mid: vec3<f32>,
    _pad_64_0_: f32,
    base_bottom: vec3<f32>,
    _pad_80_0_: f32,
    activity_accent: vec3<f32>,
    _pad_96_0_: f32,
    activity_secondary: vec3<f32>,
    _pad_112_0_: f32,
    hunter_halo: vec3<f32>,
    _pad_128_0_: f32,
    hunter_rim: vec3<f32>,
    _pad_144_0_: f32,
    energy: f32,
    _pad_160_0_: f32,
    _pad_160_1_: f32,
    _pad_160_2_: f32,
    fog_amount: f32,
    _pad_176_0_: f32,
    _pad_176_1_: f32,
    _pad_176_2_: f32,
    particle_amount: f32,
    _pad_192_0_: f32,
    _pad_192_1_: f32,
    _pad_192_2_: f32,
    structure_amount: f32,
    _pad_208_0_: f32,
    _pad_208_1_: f32,
    _pad_208_2_: f32,
    floor_grid: f32,
    _pad_224_0_: f32,
    _pad_224_1_: f32,
    _pad_224_2_: f32,
    hero_light: f32,
    _pad_240_0_: f32,
    _pad_240_1_: f32,
    _pad_240_2_: f32,
    pulse_speed: f32,
    _pad_256_0_: f32,
    _pad_256_1_: f32,
    _pad_256_2_: f32,
    warmth: f32,
    _pad_272_0_: f32,
    _pad_272_1_: f32,
    _pad_272_2_: f32,
    halo_strength: f32,
    _pad_288_0_: f32,
    _pad_288_1_: f32,
    _pad_288_2_: f32,
    floor_glow: f32,
    _pad_304_0_: f32,
    _pad_304_1_: f32,
    _pad_304_2_: f32,
    left_darken: f32,
    _pad_320_0_: f32,
    _pad_320_1_: f32,
    _pad_320_2_: f32,
    right_darken: f32,
    _pad_336_0_: f32,
    _pad_336_1_: f32,
    _pad_336_2_: f32,
    beam_intensity: f32,
    _pad_352_0_: f32,
    _pad_352_1_: f32,
    _pad_352_2_: f32,
    background_softness: f32,
    _pad_368_0_: f32,
    _pad_368_1_: f32,
    _pad_368_2_: f32,
    lobby_mode: f32,
    _pad_384_0_: f32,
    _pad_384_1_: f32,
    _pad_384_2_: f32,
    lobby_occupancy_a: vec4<f32>,
    lobby_occupancy_b: vec4<f32>,
    lobby_pad_geometry: array<vec4<f32>, 8>,
    prime_viewport: vec4<f32>,
    prime_texture_flip: array<vec4<f32>, 1>,
}

struct VertexOutput {
    @builtin(position) @invariant member: vec4<f32>,
    @location(0) chamber_uv: vec2<f32>,
}

@group(0) @binding(0)
var<uniform> global: PrimeUniforms;
var<private> gl_Position: vec4<f32>;
var<private> prime_position_1: vec3<f32>;
var<private> prime_color_1: vec4<f32>;
var<private> prime_normal_1: vec3<f32>;
var<private> prime_uv_1: vec3<f32>;
var<private> prime_color_set_1: f32;
var<private> prime_normal_set_1: f32;
var<private> chamber_uv: vec2<f32>;

fn prime_original_main() {
    let _e180: vec3<f32> = prime_uv_1;
    chamber_uv = vec4<f32>(_e180.x, _e180.y, _e180.z, 0f).xy;
    let _e187: vec3<f32> = prime_position_1;
    gl_Position = vec4<f32>(_e187.x, _e187.y, _e187.z, 1f);
    return;
}

fn main_1() {
    prime_original_main();
    let _e181: vec4<f32> = gl_Position;
    let _e183: vec4<f32> = gl_Position;
    gl_Position.z = ((_e181.z + _e183.w) * 0.5f);
    let _e188: vec4<f32> = gl_Position;
    let _e190: vec4<f32> = gl_Position;
    let _e192: vec4<f32> = global.prime_viewport;
    let _e195: vec4<f32> = global.prime_viewport;
    let _e197: vec4<f32> = gl_Position;
    let _e200: vec2<f32> = ((_e190.xy * _e192.xy) + (_e195.zw * _e197.w));
    gl_Position.x = _e200.x;
    gl_Position.y = _e200.y;
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
    let _e200: vec4<f32> = gl_Position;
    let _e202: vec2<f32> = chamber_uv;
    return VertexOutput(_e200, _e202);
}
