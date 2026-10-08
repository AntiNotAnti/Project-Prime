struct PrimeUniforms {
    resolution: vec2<f32>,
    _pad_16_0_: f32,
    _pad_16_1_: f32,
    time_value: f32,
    _pad_32_0_: f32,
    _pad_32_1_: f32,
    _pad_32_2_: f32,
    energy_time: f32,
    _pad_48_0_: f32,
    _pad_48_1_: f32,
    _pad_48_2_: f32,
    launch_amount: f32,
    _pad_64_0_: f32,
    _pad_64_1_: f32,
    _pad_64_2_: f32,
    hunter_weights_a: vec4<f32>,
    hunter_weights_b: vec4<f32>,
    slot_light: array<vec4<f32>, 8>,
    base_top: vec3<f32>,
    _pad_240_0_: f32,
    base_mid: vec3<f32>,
    _pad_256_0_: f32,
    base_bottom: vec3<f32>,
    _pad_272_0_: f32,
    activity_accent: vec3<f32>,
    _pad_288_0_: f32,
    activity_secondary: vec3<f32>,
    _pad_304_0_: f32,
    hunter_halo: vec3<f32>,
    _pad_320_0_: f32,
    hunter_rim: vec3<f32>,
    _pad_336_0_: f32,
    energy: f32,
    _pad_352_0_: f32,
    _pad_352_1_: f32,
    _pad_352_2_: f32,
    fog_amount: f32,
    _pad_368_0_: f32,
    _pad_368_1_: f32,
    _pad_368_2_: f32,
    particle_amount: f32,
    _pad_384_0_: f32,
    _pad_384_1_: f32,
    _pad_384_2_: f32,
    structure_amount: f32,
    _pad_400_0_: f32,
    _pad_400_1_: f32,
    _pad_400_2_: f32,
    floor_grid: f32,
    _pad_416_0_: f32,
    _pad_416_1_: f32,
    _pad_416_2_: f32,
    hero_light: f32,
    _pad_432_0_: f32,
    _pad_432_1_: f32,
    _pad_432_2_: f32,
    pulse_speed: f32,
    _pad_448_0_: f32,
    _pad_448_1_: f32,
    _pad_448_2_: f32,
    warmth: f32,
    _pad_464_0_: f32,
    _pad_464_1_: f32,
    _pad_464_2_: f32,
    halo_strength: f32,
    _pad_480_0_: f32,
    _pad_480_1_: f32,
    _pad_480_2_: f32,
    floor_glow: f32,
    _pad_496_0_: f32,
    _pad_496_1_: f32,
    _pad_496_2_: f32,
    left_darken: f32,
    _pad_512_0_: f32,
    _pad_512_1_: f32,
    _pad_512_2_: f32,
    right_darken: f32,
    _pad_528_0_: f32,
    _pad_528_1_: f32,
    _pad_528_2_: f32,
    beam_intensity: f32,
    _pad_544_0_: f32,
    _pad_544_1_: f32,
    _pad_544_2_: f32,
    background_softness: f32,
    _pad_560_0_: f32,
    _pad_560_1_: f32,
    _pad_560_2_: f32,
    lobby_mode: f32,
    _pad_576_0_: f32,
    _pad_576_1_: f32,
    _pad_576_2_: f32,
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
    let _e202: vec3<f32> = prime_uv_1;
    chamber_uv = vec4<f32>(_e202.x, _e202.y, _e202.z, 0f).xy;
    let _e209: vec3<f32> = prime_position_1;
    gl_Position = vec4<f32>(_e209.x, _e209.y, _e209.z, 1f);
    return;
}

fn main_1() {
    prime_original_main();
    let _e203: vec4<f32> = gl_Position;
    let _e205: vec4<f32> = gl_Position;
    gl_Position.z = ((_e203.z + _e205.w) * 0.5f);
    let _e210: vec4<f32> = gl_Position;
    let _e212: vec4<f32> = gl_Position;
    let _e214: vec4<f32> = global.prime_viewport;
    let _e217: vec4<f32> = global.prime_viewport;
    let _e219: vec4<f32> = gl_Position;
    let _e222: vec2<f32> = ((_e212.xy * _e214.xy) + (_e217.zw * _e219.w));
    gl_Position.x = _e222.x;
    gl_Position.y = _e222.y;
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
    let _e222: vec4<f32> = gl_Position;
    let _e224: vec2<f32> = chamber_uv;
    return VertexOutput(_e222, _e224);
}
