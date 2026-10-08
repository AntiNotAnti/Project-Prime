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

struct FragmentOutput {
    @location(0) prime_output: vec4<f32>,
}

@group(0) @binding(0)
var<uniform> global: PrimeUniforms;
var<private> prime_output: vec4<f32>;
var<private> chamber_uv_1: vec2<f32>;
var<private> gl_FragCoord: vec4<f32>;

fn hash21_(p: vec2<f32>) -> f32 {
    var p_1: vec2<f32>;

    p_1 = p;
    let _e198: vec2<f32> = p_1;
    let _e203: vec2<f32> = p_1;
    p_1 = fract((_e203 * vec2<f32>(123.34f, 345.45f)));
    let _e209: vec2<f32> = p_1;
    let _e211: vec2<f32> = p_1;
    let _e215: vec2<f32> = p_1;
    let _e216: vec2<f32> = p_1;
    p_1 = (_e209 + vec2(dot(_e215, (_e216 + vec2(34.345f)))));
    let _e223: vec2<f32> = p_1;
    let _e225: vec2<f32> = p_1;
    let _e228: vec2<f32> = p_1;
    let _e230: vec2<f32> = p_1;
    return fract((_e228.x * _e230.y));
}

fn line(value: f32, width: f32) -> f32 {
    var value_1: f32;
    var width_1: f32;

    value_1 = value;
    width_1 = width;
    let _e202: f32 = width_1;
    let _e206: f32 = value_1;
    let _e208: f32 = width_1;
    let _e209: f32 = width_1;
    let _e213: f32 = value_1;
    return (1f - smoothstep(_e208, (_e209 * 2.2f), abs(_e213)));
}

fn box_mask(uv: vec2<f32>, lo: vec2<f32>, hi: vec2<f32>, feather: f32) -> f32 {
    var uv_1: vec2<f32>;
    var lo_1: vec2<f32>;
    var hi_1: vec2<f32>;
    var feather_1: f32;
    var a: vec2<f32>;
    var b: vec2<f32>;

    uv_1 = uv;
    lo_1 = lo;
    hi_1 = hi;
    feather_1 = feather;
    let _e204: vec2<f32> = lo_1;
    let _e205: f32 = feather_1;
    let _e210: vec2<f32> = lo_1;
    let _e211: f32 = feather_1;
    let _e214: vec2<f32> = lo_1;
    let _e215: vec2<f32> = uv_1;
    a = smoothstep((_e210 - vec2(_e211)), _e214, _e215);
    let _e220: vec2<f32> = hi_1;
    let _e221: f32 = feather_1;
    let _e225: vec2<f32> = hi_1;
    let _e226: vec2<f32> = hi_1;
    let _e227: f32 = feather_1;
    let _e230: vec2<f32> = uv_1;
    b = (vec2(1f) - smoothstep(_e225, (_e226 + vec2(_e227)), _e230));
    let _e235: vec2<f32> = a;
    let _e237: vec2<f32> = a;
    let _e240: vec2<f32> = b;
    let _e243: vec2<f32> = b;
    return (((_e235.x * _e237.y) * _e240.x) * _e243.y);
}

fn ellipse_mask(uv_2: vec2<f32>, center: vec2<f32>, radius: vec2<f32>) -> f32 {
    var uv_3: vec2<f32>;
    var center_1: vec2<f32>;
    var radius_1: vec2<f32>;
    var d: vec2<f32>;
    var q: f32;

    uv_3 = uv_2;
    center_1 = center;
    radius_1 = radius;
    let _e202: vec2<f32> = uv_3;
    let _e203: vec2<f32> = center_1;
    let _e208: vec2<f32> = radius_1;
    d = ((_e202 - _e203) / max(_e208, vec2(0.001f)));
    let _e216: vec2<f32> = d;
    let _e217: vec2<f32> = d;
    q = dot(_e216, _e217);
    let _e226: f32 = q;
    return (1f - smoothstep(0.35f, 1f, _e226));
}

fn platform_edge_mask(uv_4: vec2<f32>, center_2: vec2<f32>, radius_2: vec2<f32>) -> f32 {
    var uv_5: vec2<f32>;
    var center_3: vec2<f32>;
    var radius_3: vec2<f32>;
    var d_1: vec2<f32>;
    var q_1: f32;

    uv_5 = uv_4;
    center_3 = center_2;
    radius_3 = radius_2;
    let _e202: vec2<f32> = uv_5;
    let _e203: vec2<f32> = center_3;
    let _e208: vec2<f32> = radius_3;
    d_1 = ((_e202 - _e203) / max(_e208, vec2(0.001f)));
    let _e216: vec2<f32> = d_1;
    let _e217: vec2<f32> = d_1;
    q_1 = dot(_e216, _e217);
    let _e225: f32 = q_1;
    let _e233: f32 = q_1;
    return (smoothstep(0.6f, 0.75f, _e225) * (1f - smoothstep(0.86f, 1f, _e233)));
}

fn platform_fill_mask(uv_6: vec2<f32>, center_4: vec2<f32>, radius_4: vec2<f32>) -> f32 {
    var uv_7: vec2<f32>;
    var center_5: vec2<f32>;
    var radius_5: vec2<f32>;
    var d_2: vec2<f32>;
    var q_2: f32;

    uv_7 = uv_6;
    center_5 = center_4;
    radius_5 = radius_4;
    let _e202: vec2<f32> = uv_7;
    let _e203: vec2<f32> = center_5;
    let _e208: vec2<f32> = radius_5;
    d_2 = ((_e202 - _e203) / max(_e208, vec2(0.001f)));
    let _e216: vec2<f32> = d_2;
    let _e217: vec2<f32> = d_2;
    q_2 = dot(_e216, _e217);
    let _e226: f32 = q_2;
    return (1f - smoothstep(0.62f, 1f, _e226));
}

fn main_1() {
    var uv_8: vec2<f32>;
    var stageHeroX: f32 = 0.5f;
    var aspect: f32;
    var p_2: vec2<f32>;
    var upper: f32;
    var col: vec3<f32>;
    var horizon: f32;
    var bay: f32;
    var torsoLift: f32;
    var heroAmbient: vec3<f32>;
    var leftRecess: f32;
    var rightRecess: f32;
    var recess: f32;
    var innerEdges: f32;
    var innerGate: f32;
    var farLeftWall: f32;
    var farRightWall: f32;
    var farWall: f32;
    var farCrossbeam: f32;
    var gantryTravel: f32;
    var gantryGate: f32;
    var gantry: f32;
    var gantryEdge: f32;
    var surge: f32;
    var channel: i32 = 0i;
    var fi: f32;
    var cx: f32;
    var travel: f32;
    var conduit: f32;
    var samus: f32;
    var kanden: f32;
    var trace: f32;
    var sylux: f32;
    var noxus: f32;
    var spire: f32;
    var weavel: f32;
    var scanY: f32;
    var tactical: f32;
    var bio: f32;
    var electric: f32;
    var thermal: f32;
    var motifGate: f32;
    var sideGate: f32;
    var i: i32 = 0i;
    var fi_1: f32;
    var x: f32;
    var rib: f32;
    var gate: f32;
    var leftBrace: f32;
    var rightBrace: f32;
    var braceGate: f32;
    var local: f32;
    var sweepCenter: f32;
    var wallSweep: f32;
    var wallSweepGate: f32;
    var signalGate: f32;
    var signalPhase: f32;
    var activeSignal: f32;
    var heroCenter: vec2<f32>;
    var hero: f32;
    var pulse: f32 = 1f;
    var rimColumn: f32;
    var heroKey: f32;
    var heroKeyColor: vec3<f32> = vec3<f32>(0.31f, 0.39f, 0.46f);
    var shaftA: f32;
    var shaftB: f32;
    var shaftGate: f32;
    var floorMask: f32;
    var fy: f32;
    var perspectiveY: f32;
    var horizontal: f32;
    var centeredX: f32;
    var vertical: f32;
    var grid: f32;
    var serviceSpine: f32;
    var serviceGate: f32;
    var slot: i32 = 7i;
    var geometry: vec4<f32>;
    var centre: vec2<f32>;
    var radius_6: vec2<f32>;
    var local_1: f32;
    var occupied: f32;
    var foot: vec2<f32>;
    var q_3: f32;
    var outer: vec2<f32>;
    var shadow: f32;
    var body: f32;
    var lip: f32;
    var raisedEdge: f32;
    var innerDish: f32;
    var metal: vec3<f32>;
    var activeLight: vec3<f32>;
    var confirmation: f32;
    var ring1_: f32;
    var ring2_: f32;
    var angle: f32;
    var phase: f32;
    var segments: f32;
    var leftBoot: vec2<f32>;
    var rightBoot: vec2<f32>;
    var boots: f32;
    var soloPad: vec4<f32> = vec4<f32>(0.5f, 0.805f, 0.235f, 0.06f);
    var platformDelta: vec2<f32>;
    var platformQ: f32;
    var platformFill: f32;
    var platformEdge: f32;
    var platformCore: f32;
    var ringA: f32;
    var ringB: f32;
    var ringC: f32;
    var ringAngle: f32;
    var ringPhase: f32;
    var ringSegments: f32;
    var counterPhase: f32;
    var serviceRings: f32;
    var lowFog: f32;
    var fogWave: f32 = 1f;
    var fogColor: vec3<f32>;
    var rearLegFog: f32;
    var rearFogColor: vec3<f32>;
    var drift: f32;
    var shafts: f32;
    var reactorShaftGate: f32;
    var dustUv: vec2<f32>;
    var cells: vec2<f32>;
    var cellUv: vec2<f32>;
    var seed: f32;
    var mote: f32;
    var sparkle: f32;
    var twinkle: f32 = 0.75f;
    var leftMask: f32;
    var rightMask: f32;
    var vignette: f32;
    var grain: f32;

    let _e196: vec2<f32> = chamber_uv_1;
    uv_8 = _e196;
    let _e200: vec2<f32> = global.resolution;
    let _e202: vec2<f32> = global.resolution;
    let _e205: vec2<f32> = global.resolution;
    let _e211: vec2<f32> = global.resolution;
    let _e213: vec2<f32> = global.resolution;
    let _e216: vec2<f32> = global.resolution;
    aspect = max((_e211.x / max(_e216.y, 1f)), 1f);
    let _e224: vec2<f32> = uv_8;
    let _e228: f32 = aspect;
    let _e230: vec2<f32> = uv_8;
    p_2 = vec2<f32>(((_e224.x - 0.5f) * _e228), (_e230.y - 0.5f));
    let _e238: vec2<f32> = uv_8;
    let _e242: vec2<f32> = uv_8;
    upper = smoothstep(0f, 0.52f, _e242.y);
    let _e249: vec3<f32> = global.base_top;
    let _e250: vec3<f32> = global.base_mid;
    let _e251: f32 = upper;
    col = mix(_e249, _e250, vec3(_e251));
    let _e259: vec2<f32> = uv_8;
    let _e263: vec2<f32> = uv_8;
    let _e266: vec3<f32> = col;
    let _e267: vec3<f32> = global.base_bottom;
    let _e270: vec2<f32> = uv_8;
    let _e274: vec2<f32> = uv_8;
    col = mix(_e266, _e267, vec3(smoothstep(0.58f, 1f, _e274.y)));
    let _e279: vec2<f32> = uv_8;
    let _e286: vec2<f32> = uv_8;
    let _e295: vec2<f32> = uv_8;
    let _e302: vec2<f32> = uv_8;
    horizon = exp(-(pow(((_e302.y - 0.52f) * 4.8f), 2f)));
    let _e313: vec3<f32> = col;
    let _e317: vec3<f32> = global.activity_accent;
    let _e318: vec3<f32> = global.hunter_halo;
    let _e322: f32 = horizon;
    let _e326: f32 = global.energy;
    col = (_e313 + (((mix(_e317, _e318, vec3(0.35f)) * _e322) * 0.087f) * _e326));
    let _e337: vec2<f32> = uv_8;
    let _e345: f32 = box_mask(_e337, vec2<f32>(0.305f, 0.14f), vec2<f32>(0.855f, 0.72f), 0.035f);
    bay = _e345;
    let _e347: vec3<f32> = col;
    let _e351: vec3<f32> = global.activity_accent;
    let _e352: vec3<f32> = global.hunter_halo;
    let _e356: f32 = bay;
    let _e360: f32 = global.structure_amount;
    col = (_e347 + (((mix(_e351, _e352, vec3(0.64f)) * _e356) * 0.075f) * _e360));
    let _e364: f32 = stageHeroX;
    let _e370: vec2<f32> = uv_8;
    let _e371: f32 = stageHeroX;
    let _e377: f32 = ellipse_mask(_e370, vec2<f32>(_e371, 0.49f), vec2<f32>(0.255f, 0.335f));
    torsoLift = _e377;
    let _e385: vec3<f32> = global.base_mid;
    heroAmbient = mix(_e385, vec3<f32>(0.105f, 0.145f, 0.185f), vec3(0.55f));
    let _e394: vec3<f32> = col;
    let _e395: vec3<f32> = heroAmbient;
    let _e396: f32 = torsoLift;
    let _e400: f32 = global.hero_light;
    col = (_e394 + ((_e395 * _e396) * (0.15f + (0.05f * _e400))));
    let _e413: vec2<f32> = uv_8;
    let _e421: f32 = box_mask(_e413, vec2<f32>(0.355f, 0.3f), vec2<f32>(0.455f, 0.69f), 0.014f);
    leftRecess = _e421;
    let _e431: vec2<f32> = uv_8;
    let _e439: f32 = box_mask(_e431, vec2<f32>(0.775f, 0.3f), vec2<f32>(0.855f, 0.69f), 0.014f);
    rightRecess = _e439;
    let _e442: f32 = leftRecess;
    let _e443: f32 = rightRecess;
    let _e446: f32 = leftRecess;
    let _e447: f32 = rightRecess;
    recess = min(1f, (_e446 + _e447));
    let _e451: vec3<f32> = col;
    let _e453: f32 = recess;
    let _e456: f32 = global.structure_amount;
    col = (_e451 * (1f - ((_e453 * 0.1f) * _e456)));
    let _e460: vec2<f32> = uv_8;
    let _e465: vec2<f32> = uv_8;
    let _e470: f32 = line((_e465.x - 0.355f), 0.0022f);
    let _e471: vec2<f32> = uv_8;
    let _e476: vec2<f32> = uv_8;
    let _e481: f32 = line((_e476.x - 0.455f), 0.0022f);
    let _e483: vec2<f32> = uv_8;
    let _e488: vec2<f32> = uv_8;
    let _e493: f32 = line((_e488.x - 0.775f), 0.0022f);
    let _e495: vec2<f32> = uv_8;
    let _e500: vec2<f32> = uv_8;
    let _e505: f32 = line((_e500.x - 0.855f), 0.0022f);
    innerEdges = (((_e470 + _e481) + _e493) + _e505);
    let _e510: vec2<f32> = uv_8;
    let _e514: vec2<f32> = uv_8;
    let _e520: vec2<f32> = uv_8;
    let _e524: vec2<f32> = uv_8;
    innerGate = (smoothstep(0.27f, 0.34f, _e514.y) * (1f - smoothstep(0.68f, 0.74f, _e524.y)));
    let _e530: vec3<f32> = col;
    let _e534: vec3<f32> = global.activity_secondary;
    let _e535: vec3<f32> = global.hunter_rim;
    let _e539: f32 = innerEdges;
    let _e541: f32 = innerGate;
    let _e545: f32 = global.structure_amount;
    col = (_e530 + ((((mix(_e534, _e535, vec3(0.42f)) * _e539) * _e541) * 0.043f) * _e545));
    let _e556: vec2<f32> = uv_8;
    let _e564: f32 = box_mask(_e556, vec2<f32>(0.245f, 0.23f), vec2<f32>(0.325f, 0.7f), 0.02f);
    farLeftWall = _e564;
    let _e574: vec2<f32> = uv_8;
    let _e582: f32 = box_mask(_e574, vec2<f32>(0.87f, 0.18f), vec2<f32>(0.945f, 0.66f), 0.02f);
    farRightWall = _e582;
    let _e585: f32 = farLeftWall;
    let _e586: f32 = farRightWall;
    let _e589: f32 = farLeftWall;
    let _e590: f32 = farRightWall;
    farWall = min(1f, (_e589 + _e590));
    let _e595: vec3<f32> = global.base_bottom;
    let _e598: f32 = farWall;
    let _e601: f32 = global.structure_amount;
    let _e603: vec3<f32> = col;
    let _e604: vec3<f32> = global.base_bottom;
    let _e607: f32 = farWall;
    let _e610: f32 = global.structure_amount;
    col = mix(_e603, (_e604 * 0.58f), vec3(((_e607 * 0.52f) * _e610)));
    let _e622: vec2<f32> = uv_8;
    let _e630: f32 = box_mask(_e622, vec2<f32>(0.255f, 0.285f), vec2<f32>(0.93f, 0.302f), 0.008f);
    farCrossbeam = _e630;
    let _e633: vec3<f32> = global.base_bottom;
    let _e636: f32 = farCrossbeam;
    let _e639: f32 = global.structure_amount;
    let _e641: vec3<f32> = col;
    let _e642: vec3<f32> = global.base_bottom;
    let _e645: f32 = farCrossbeam;
    let _e648: f32 = global.structure_amount;
    col = mix(_e641, (_e642 * 0.44f), vec3(((_e645 * 0.7f) * _e648)));
    let _e652: vec3<f32> = col;
    let _e653: vec3<f32> = global.activity_accent;
    let _e654: f32 = farCrossbeam;
    let _e658: f32 = global.structure_amount;
    col = (_e652 + (((_e653 * _e654) * 0.02f) * _e658));
    let _e661: f32 = global.time_value;
    let _e664: f32 = global.time_value;
    gantryTravel = (sin((_e664 * 0.065f)) * 0.022f);
    let _e679: vec2<f32> = uv_8;
    let _e687: f32 = box_mask(_e679, vec2<f32>(0.22f, 0.17f), vec2<f32>(0.88f, 0.65f), 0.025f);
    gantryGate = _e687;
    let _e689: vec2<f32> = uv_8;
    let _e693: vec2<f32> = uv_8;
    let _e699: f32 = gantryTravel;
    let _e701: vec2<f32> = uv_8;
    let _e710: vec2<f32> = uv_8;
    let _e714: vec2<f32> = uv_8;
    let _e720: f32 = gantryTravel;
    let _e722: vec2<f32> = uv_8;
    let _e731: f32 = line((abs((_e714.x - 0.5f)) - ((0.255f + _e720) + ((_e722.y - 0.3f) * 0.13f))), 0.01f);
    gantry = _e731;
    let _e735: f32 = gantry;
    let _e736: f32 = gantryGate;
    let _e740: vec3<f32> = col;
    let _e741: vec3<f32> = global.base_bottom;
    let _e742: f32 = gantry;
    let _e743: f32 = gantryGate;
    col = mix(_e740, _e741, vec3(((_e742 * _e743) * 0.58f)));
    let _e749: vec2<f32> = uv_8;
    let _e753: vec2<f32> = uv_8;
    let _e759: f32 = gantryTravel;
    let _e761: vec2<f32> = uv_8;
    let _e770: vec2<f32> = uv_8;
    let _e774: vec2<f32> = uv_8;
    let _e780: f32 = gantryTravel;
    let _e782: vec2<f32> = uv_8;
    let _e791: f32 = line((abs((_e774.x - 0.5f)) - ((0.244f + _e780) + ((_e782.y - 0.3f) * 0.13f))), 0.0012f);
    gantryEdge = _e791;
    let _e793: vec3<f32> = col;
    let _e794: vec3<f32> = global.hunter_rim;
    let _e795: f32 = gantryEdge;
    let _e797: f32 = gantryGate;
    col = (_e793 + (((_e794 * _e795) * _e797) * 0.16f));
    let _e804: f32 = global.energy_time;
    let _e807: f32 = global.energy_time;
    let _e816: f32 = global.energy_time;
    let _e819: f32 = global.energy_time;
    surge = pow((0.5f + (0.5f * sin((_e819 * 0.57f)))), 8f);
    loop {
        let _e830: i32 = channel;
        if !((_e830 < 4i)) {
            break;
        }
        {
            let _e837: i32 = channel;
            fi = f32(_e837);
            let _e841: f32 = fi;
            cx = (0.285f + (_e841 * 0.145f));
            let _e848: f32 = global.energy_time;
            let _e851: vec2<f32> = uv_8;
            let _e856: f32 = fi;
            let _e860: f32 = global.energy_time;
            let _e863: vec2<f32> = uv_8;
            let _e868: f32 = fi;
            travel = (0.5f + (0.5f * sin((((_e860 * 0.7f) - (_e863.y * 12f)) + (_e868 * 1.6f)))));
            let _e876: vec2<f32> = uv_8;
            let _e878: f32 = cx;
            let _e881: vec2<f32> = uv_8;
            let _e883: f32 = cx;
            let _e886: f32 = line((_e881.x - _e883), 0.0013f);
            let _e895: vec2<f32> = uv_8;
            let _e903: f32 = box_mask(_e895, vec2<f32>(0.2f, 0.22f), vec2<f32>(0.86f, 0.7f), 0.025f);
            conduit = (_e886 * _e903);
            let _e906: vec3<f32> = col;
            let _e910: vec3<f32> = global.activity_secondary;
            let _e911: vec3<f32> = global.hunter_rim;
            let _e915: f32 = conduit;
            let _e920: f32 = travel;
            let _e924: f32 = surge;
            col = (_e906 + ((mix(_e910, _e911, vec3(0.7f)) * _e915) * (0.035f + (pow(_e920, 5f) * (0.26f + (_e924 * 0.16f))))));
        }
        continuing {
            let _e834: i32 = channel;
            channel = (_e834 + 1i);
        }
    }
    let _e932: vec4<f32> = global.hunter_weights_a;
    samus = _e932.x;
    let _e935: vec4<f32> = global.hunter_weights_a;
    kanden = _e935.y;
    let _e938: vec4<f32> = global.hunter_weights_a;
    trace = _e938.z;
    let _e941: vec4<f32> = global.hunter_weights_a;
    sylux = _e941.w;
    let _e944: vec4<f32> = global.hunter_weights_b;
    noxus = _e944.x;
    let _e947: vec4<f32> = global.hunter_weights_b;
    spire = _e947.y;
    let _e950: vec4<f32> = global.hunter_weights_b;
    weavel = _e950.z;
    let _e954: f32 = global.time_value;
    let _e957: f32 = global.time_value;
    scanY = (0.44f + (sin((_e957 * 0.32f)) * 0.17f));
    let _e965: vec2<f32> = uv_8;
    let _e967: f32 = scanY;
    let _e969: vec2<f32> = uv_8;
    let _e973: vec2<f32> = uv_8;
    let _e982: vec2<f32> = uv_8;
    let _e984: f32 = scanY;
    let _e986: vec2<f32> = uv_8;
    let _e990: vec2<f32> = uv_8;
    let _e999: f32 = line(((_e982.y - _e984) - (abs((_e990.x - 0.5f)) * 0.23f)), 0.0018f);
    tactical = _e999;
    let _e1003: vec2<f32> = uv_8;
    let _e1007: f32 = global.time_value;
    let _e1010: f32 = global.time_value;
    let _e1017: f32 = global.energy_time;
    let _e1019: vec2<f32> = uv_8;
    let _e1023: f32 = global.time_value;
    let _e1026: f32 = global.time_value;
    let _e1033: f32 = global.energy_time;
    let _e1041: vec2<f32> = uv_8;
    let _e1045: f32 = global.time_value;
    let _e1048: f32 = global.time_value;
    let _e1055: f32 = global.energy_time;
    let _e1057: vec2<f32> = uv_8;
    let _e1061: f32 = global.time_value;
    let _e1064: f32 = global.time_value;
    let _e1071: f32 = global.energy_time;
    bio = pow((0.5f + (0.5f * sin((((_e1057.y * 27f) + (sin((_e1064 * 1.7f)) * 2f)) + _e1071)))), 6f);
    let _e1081: vec2<f32> = uv_8;
    let _e1085: f32 = global.energy_time;
    let _e1089: vec2<f32> = uv_8;
    let _e1093: f32 = global.energy_time;
    let _e1103: vec2<f32> = uv_8;
    let _e1107: f32 = global.energy_time;
    let _e1111: vec2<f32> = uv_8;
    let _e1115: f32 = global.energy_time;
    electric = pow((0.5f + (0.5f * sin(((_e1111.y * 35f) - (_e1115 * 3f))))), 14f);
    let _e1127: vec2<f32> = uv_8;
    let _e1131: f32 = global.time_value;
    let _e1134: vec2<f32> = uv_8;
    let _e1139: f32 = global.time_value;
    let _e1142: vec2<f32> = uv_8;
    let _e1149: vec2<f32> = uv_8;
    let _e1153: f32 = global.time_value;
    let _e1156: vec2<f32> = uv_8;
    let _e1161: f32 = global.time_value;
    let _e1164: vec2<f32> = uv_8;
    thermal = (0.5f + (0.5f * sin(((_e1149.x * 28f) + sin(((_e1161 * 0.3f) + (_e1164.y * 8f)))))));
    let _e1182: vec2<f32> = uv_8;
    let _e1189: f32 = ellipse_mask(_e1182, vec2<f32>(0.5f, 0.47f), vec2<f32>(0.34f, 0.34f));
    motifGate = _e1189;
    let _e1193: vec2<f32> = uv_8;
    let _e1197: vec2<f32> = uv_8;
    let _e1204: vec2<f32> = uv_8;
    let _e1208: vec2<f32> = uv_8;
    sideGate = smoothstep(0.1f, 0.22f, abs((_e1208.x - 0.5f)));
    let _e1215: vec3<f32> = col;
    let _e1216: vec3<f32> = global.hunter_rim;
    let _e1217: f32 = motifGate;
    let _e1219: f32 = trace;
    let _e1220: f32 = tactical;
    let _e1224: f32 = kanden;
    let _e1225: f32 = bio;
    let _e1227: f32 = sideGate;
    let _e1232: f32 = sylux;
    let _e1233: f32 = electric;
    let _e1235: f32 = sideGate;
    let _e1240: f32 = samus;
    let _e1241: f32 = surge;
    let _e1246: f32 = noxus;
    let _e1247: f32 = thermal;
    let _e1252: f32 = spire;
    let _e1253: f32 = thermal;
    let _e1257: vec2<f32> = uv_8;
    let _e1261: vec2<f32> = uv_8;
    let _e1268: f32 = weavel;
    let _e1269: f32 = tactical;
    let _e1271: f32 = sideGate;
    let _e1275: vec2<f32> = uv_8;
    let _e1278: vec2<f32> = uv_8;
    col = (_e1215 + ((_e1216 * _e1217) * ((((((((_e1219 * _e1220) * 0.4f) + (((_e1224 * _e1225) * _e1227) * 0.1f)) + (((_e1232 * _e1233) * _e1235) * 0.18f)) + ((_e1240 * _e1241) * 0.065f)) + ((_e1246 * _e1247) * 0.03f)) + (((_e1252 * _e1253) * smoothstep(0.4f, 0.78f, _e1261.y)) * 0.13f)) + (((_e1268 * _e1269) * _e1271) * (0.18f + (step(0.5f, _e1278.x) * 0.15f))))));
    loop {
        let _e1290: i32 = i;
        if !((_e1290 < 7i)) {
            break;
        }
        {
            let _e1297: i32 = i;
            fi_1 = f32(_e1297);
            let _e1301: f32 = fi_1;
            x = (0.16f + (_e1301 * 0.135f));
            let _e1306: vec2<f32> = uv_8;
            let _e1308: f32 = x;
            let _e1312: vec2<f32> = uv_8;
            let _e1316: vec2<f32> = uv_8;
            let _e1318: f32 = x;
            let _e1322: vec2<f32> = uv_8;
            let _e1326: f32 = line((_e1316.x - _e1318), (0.0018f + (0.001f * _e1322.y)));
            rib = _e1326;
            let _e1330: vec2<f32> = uv_8;
            let _e1334: vec2<f32> = uv_8;
            let _e1340: vec2<f32> = uv_8;
            let _e1344: vec2<f32> = uv_8;
            gate = (smoothstep(0.1f, 0.2f, _e1334.y) * (1f - smoothstep(0.76f, 0.9f, _e1344.y)));
            let _e1350: vec3<f32> = col;
            let _e1354: vec3<f32> = global.activity_accent;
            let _e1355: vec3<f32> = global.hunter_halo;
            let _e1359: f32 = rib;
            let _e1361: f32 = gate;
            let _e1365: f32 = global.structure_amount;
            col = (_e1350 + ((((mix(_e1354, _e1355, vec3(0.25f)) * _e1359) * _e1361) * 0.065f) * _e1365));
        }
        continuing {
            let _e1294: i32 = i;
            i = (_e1294 + 1i);
        }
    }
    let _e1368: vec2<f32> = uv_8;
    let _e1373: vec2<f32> = uv_8;
    let _e1380: vec2<f32> = uv_8;
    let _e1385: vec2<f32> = uv_8;
    let _e1392: f32 = line(((_e1380.x - 0.17f) - ((0.64f - _e1385.y) * 0.26f)), 0.006f);
    leftBrace = _e1392;
    let _e1395: vec2<f32> = uv_8;
    let _e1399: vec2<f32> = uv_8;
    let _e1407: vec2<f32> = uv_8;
    let _e1411: vec2<f32> = uv_8;
    let _e1418: f32 = line(((0.91f - _e1407.x) - ((0.64f - _e1411.y) * 0.22f)), 0.006f);
    rightBrace = _e1418;
    let _e1422: vec2<f32> = uv_8;
    let _e1426: vec2<f32> = uv_8;
    let _e1432: vec2<f32> = uv_8;
    let _e1436: vec2<f32> = uv_8;
    braceGate = (smoothstep(0.16f, 0.3f, _e1426.y) * (1f - smoothstep(0.66f, 0.82f, _e1436.y)));
    let _e1442: vec3<f32> = col;
    let _e1443: vec3<f32> = global.activity_secondary;
    let _e1444: f32 = leftBrace;
    let _e1445: f32 = rightBrace;
    let _e1448: f32 = braceGate;
    let _e1452: f32 = global.structure_amount;
    col = (_e1442 + ((((_e1443 * (_e1444 + _e1445)) * _e1448) * 0.092f) * _e1452));
    let _e1455: f32 = global.time_value;
    if (_e1455 > 0f) {
        let _e1458: f32 = global.time_value;
        let _e1462: f32 = global.time_value;
        let _e1464: f32 = (_e1462 * 0.045f);
        local = ((_e1464 - (floor((_e1464 / 1.16f)) * 1.16f)) - 0.08f);
    } else {
        local = 0.53f;
    }
    let _e1474: f32 = local;
    sweepCenter = _e1474;
    let _e1479: vec2<f32> = uv_8;
    let _e1481: f32 = sweepCenter;
    let _e1483: vec2<f32> = uv_8;
    let _e1485: f32 = sweepCenter;
    let _e1490: vec2<f32> = uv_8;
    let _e1492: f32 = sweepCenter;
    let _e1494: vec2<f32> = uv_8;
    let _e1496: f32 = sweepCenter;
    wallSweep = (1f - smoothstep(0.015f, 0.095f, abs((_e1494.x - _e1496))));
    let _e1510: vec2<f32> = uv_8;
    let _e1518: f32 = box_mask(_e1510, vec2<f32>(0.29f, 0.27f), vec2<f32>(0.91f, 0.7f), 0.035f);
    wallSweepGate = _e1518;
    let _e1520: vec3<f32> = col;
    let _e1525: f32 = wallSweep;
    let _e1527: f32 = wallSweepGate;
    let _e1531: f32 = global.energy;
    let _e1533: f32 = global.structure_amount;
    col = (_e1520 + (((((vec3<f32>(0.17f, 0.25f, 0.32f) * _e1525) * _e1527) * 0.24f) * _e1531) * _e1533));
    let _e1544: vec2<f32> = uv_8;
    let _e1552: f32 = box_mask(_e1544, vec2<f32>(0.31f, 0.275f), vec2<f32>(0.855f, 0.305f), 0.008f);
    signalGate = _e1552;
    let _e1554: vec2<f32> = uv_8;
    let _e1558: f32 = global.time_value;
    let _e1562: f32 = global.energy_time;
    let _e1566: vec2<f32> = uv_8;
    let _e1570: f32 = global.time_value;
    let _e1574: f32 = global.energy_time;
    signalPhase = sin((((_e1566.x * 36f) - (_e1570 * 0.45f)) - (_e1574 * 0.65f)));
    let _e1582: f32 = signalPhase;
    activeSignal = (0.5f + (0.5f * _e1582));
    let _e1586: vec3<f32> = col;
    let _e1590: vec3<f32> = global.activity_secondary;
    let _e1591: vec3<f32> = global.hunter_rim;
    let _e1595: f32 = signalGate;
    let _e1597: f32 = activeSignal;
    let _e1601: f32 = global.energy;
    col = (_e1586 + (((mix(_e1590, _e1591, vec3(0.44f)) * _e1595) * _e1597) * (0.045f + (0.04f * _e1601))));
    let _e1606: f32 = stageHeroX;
    heroCenter = vec2<f32>(_e1606, 0.49f);
    let _e1615: vec2<f32> = uv_8;
    let _e1616: vec2<f32> = heroCenter;
    let _e1620: f32 = ellipse_mask(_e1615, _e1616, vec2<f32>(0.205f, 0.39f));
    hero = _e1620;
    let _e1624: f32 = global.time_value;
    if (_e1624 > 0f) {
        let _e1627: f32 = pulse;
        let _e1628: f32 = global.time_value;
        let _e1631: f32 = global.energy_time;
        let _e1635: f32 = global.time_value;
        let _e1638: f32 = global.energy_time;
        pulse = (_e1627 + (sin(((_e1635 * 0.55f) + (_e1638 * 0.45f))) * 0.035f));
    }
    let _e1646: vec3<f32> = col;
    let _e1647: vec3<f32> = global.hunter_halo;
    let _e1648: f32 = hero;
    let _e1650: f32 = global.halo_strength;
    let _e1652: f32 = global.hero_light;
    let _e1656: f32 = global.energy;
    let _e1660: f32 = pulse;
    col = (_e1646 + (((((_e1647 * _e1648) * _e1650) * _e1652) * (0.215f + (0.09f * _e1656))) * _e1660));
    let _e1664: f32 = stageHeroX;
    let _e1670: vec2<f32> = uv_8;
    let _e1671: f32 = stageHeroX;
    let _e1677: f32 = ellipse_mask(_e1670, vec2<f32>(_e1671, 0.43f), vec2<f32>(0.105f, 0.32f));
    rimColumn = _e1677;
    let _e1679: vec3<f32> = col;
    let _e1680: vec3<f32> = global.hunter_rim;
    let _e1681: f32 = rimColumn;
    let _e1685: f32 = global.hero_light;
    col = (_e1679 + (((_e1680 * _e1681) * 0.09f) * _e1685));
    let _e1689: f32 = stageHeroX;
    let _e1695: vec2<f32> = uv_8;
    let _e1696: f32 = stageHeroX;
    let _e1702: f32 = ellipse_mask(_e1695, vec2<f32>(_e1696, 0.39f), vec2<f32>(0.165f, 0.225f));
    heroKey = _e1702;
    let _e1709: vec3<f32> = col;
    let _e1710: vec3<f32> = heroKeyColor;
    let _e1711: f32 = heroKey;
    let _e1715: f32 = global.hero_light;
    col = (_e1709 + ((_e1710 * _e1711) * (0.04f + (0.02f * _e1715))));
    let _e1722: vec2<f32> = uv_8;
    let _e1728: vec2<f32> = uv_8;
    let _e1735: vec2<f32> = uv_8;
    let _e1741: vec2<f32> = uv_8;
    let _e1752: vec2<f32> = uv_8;
    let _e1758: vec2<f32> = uv_8;
    let _e1765: vec2<f32> = uv_8;
    let _e1771: vec2<f32> = uv_8;
    shaftA = max(0f, (1f - abs((((_e1765.x - 0.58f) * 6.4f) + ((_e1771.y - 0.15f) * 0.55f)))));
    let _e1784: vec2<f32> = uv_8;
    let _e1790: vec2<f32> = uv_8;
    let _e1797: vec2<f32> = uv_8;
    let _e1803: vec2<f32> = uv_8;
    let _e1814: vec2<f32> = uv_8;
    let _e1820: vec2<f32> = uv_8;
    let _e1827: vec2<f32> = uv_8;
    let _e1833: vec2<f32> = uv_8;
    shaftB = max(0f, (1f - abs((((_e1827.x - 0.7f) * 7.1f) - ((_e1833.y - 0.12f) * 0.45f)))));
    let _e1847: vec2<f32> = uv_8;
    let _e1851: vec2<f32> = uv_8;
    let _e1857: vec2<f32> = uv_8;
    let _e1861: vec2<f32> = uv_8;
    shaftGate = ((1f - smoothstep(0.2f, 0.86f, _e1851.y)) * smoothstep(0.04f, 0.18f, _e1861.y));
    let _e1866: vec3<f32> = col;
    let _e1867: vec3<f32> = global.activity_secondary;
    let _e1868: f32 = shaftA;
    let _e1869: f32 = shaftB;
    let _e1872: f32 = shaftGate;
    let _e1876: f32 = global.beam_intensity;
    let _e1878: f32 = global.energy;
    col = (_e1866 + (((((_e1867 * (_e1868 + _e1869)) * _e1872) * 0.028f) * _e1876) * _e1878));
    let _e1883: vec2<f32> = uv_8;
    let _e1887: vec2<f32> = uv_8;
    floorMask = smoothstep(0.58f, 0.78f, _e1887.y);
    let _e1892: vec3<f32> = global.base_bottom;
    let _e1895: f32 = floorMask;
    let _e1898: vec3<f32> = col;
    let _e1899: vec3<f32> = global.base_bottom;
    let _e1902: f32 = floorMask;
    col = mix(_e1898, (_e1899 * 0.72f), vec3((_e1902 * 0.56f)));
    let _e1907: vec2<f32> = uv_8;
    let _e1912: vec2<f32> = uv_8;
    fy = max((_e1912.y - 0.56f), 0.001f);
    let _e1920: f32 = fy;
    perspectiveY = (1f / ((_e1920 * 10f) + 0.55f));
    let _e1927: f32 = perspectiveY;
    let _e1930: f32 = perspectiveY;
    let _e1937: f32 = perspectiveY;
    let _e1940: f32 = perspectiveY;
    let _e1947: f32 = line((fract((_e1940 * 4.2f)) - 0.5f), 0.06f);
    horizontal = _e1947;
    let _e1949: vec2<f32> = uv_8;
    let _e1951: f32 = stageHeroX;
    let _e1953: f32 = fy;
    let _e1957: f32 = fy;
    centeredX = ((_e1949.x - _e1951) / max((_e1957 + 0.2f), 0.2f));
    let _e1964: f32 = centeredX;
    let _e1967: f32 = centeredX;
    let _e1974: f32 = centeredX;
    let _e1977: f32 = centeredX;
    let _e1984: f32 = line((fract((_e1977 * 4.5f)) - 0.5f), 0.035f);
    vertical = _e1984;
    let _e1988: f32 = horizontal;
    let _e1989: f32 = vertical;
    let _e1991: f32 = floorMask;
    grid = (max(_e1988, _e1989) * _e1991);
    let _e1994: vec3<f32> = col;
    let _e1998: vec3<f32> = global.activity_accent;
    let _e1999: vec3<f32> = global.hunter_halo;
    let _e2003: f32 = grid;
    let _e2007: f32 = global.floor_grid;
    col = (_e1994 + (((mix(_e1998, _e1999, vec3(0.26f)) * _e2003) * 0.058f) * _e2007));
    let _e2010: f32 = global.lobby_mode;
    if (_e2010 > 0.5f) {
        {
            let _e2013: vec2<f32> = uv_8;
            let _e2017: vec2<f32> = uv_8;
            let _e2023: vec2<f32> = uv_8;
            let _e2030: vec2<f32> = uv_8;
            let _e2034: vec2<f32> = uv_8;
            let _e2040: vec2<f32> = uv_8;
            let _e2047: f32 = line((abs((_e2034.x - 0.5f)) - ((0.79f - _e2040.y) * 0.42f)), 0.0035f);
            serviceSpine = _e2047;
            let _e2051: vec2<f32> = uv_8;
            let _e2055: vec2<f32> = uv_8;
            let _e2061: vec2<f32> = uv_8;
            let _e2065: vec2<f32> = uv_8;
            serviceGate = (smoothstep(0.29f, 0.43f, _e2055.y) * (1f - smoothstep(0.75f, 0.83f, _e2065.y)));
            let _e2071: vec3<f32> = col;
            let _e2072: vec3<f32> = global.activity_secondary;
            let _e2073: f32 = serviceSpine;
            let _e2075: f32 = serviceGate;
            let _e2078: f32 = global.launch_amount;
            let _e2081: vec2<f32> = uv_8;
            let _e2085: f32 = global.energy_time;
            let _e2089: vec2<f32> = uv_8;
            let _e2093: f32 = global.energy_time;
            let _e2103: f32 = global.energy;
            col = (_e2071 + ((((_e2072 * _e2073) * _e2075) * (0.13f + (_e2078 * (0.18f + (0.1f * sin(((_e2089.y * 40f) - (_e2093 * 5f)))))))) * _e2103));
            loop {
                let _e2108: i32 = slot;
                if !((_e2108 >= 0i)) {
                    break;
                }
                {
                    let _e2115: i32 = slot;
                    let _e2117: vec4<f32> = global.lobby_pad_geometry[_e2115];
                    geometry = _e2117;
                    let _e2119: vec4<f32> = geometry;
                    centre = _e2119.xy;
                    let _e2122: vec4<f32> = geometry;
                    radius_6 = _e2122.zw;
                    let _e2125: i32 = slot;
                    if (_e2125 < 4i) {
                        let _e2128: i32 = slot;
                        let _e2130: f32 = global.lobby_occupancy_a[_e2128];
                        local_1 = _e2130;
                    } else {
                        let _e2131: i32 = slot;
                        let _e2135: f32 = global.lobby_occupancy_b[(_e2131 - 4i)];
                        local_1 = _e2135;
                    }
                    let _e2137: f32 = local_1;
                    occupied = _e2137;
                    let _e2139: vec2<f32> = uv_8;
                    let _e2140: vec2<f32> = centre;
                    let _e2142: vec2<f32> = radius_6;
                    foot = ((_e2139 - _e2140) / _e2142);
                    let _e2147: vec2<f32> = foot;
                    let _e2148: vec2<f32> = foot;
                    q_3 = dot(_e2147, _e2148);
                    let _e2151: vec2<f32> = uv_8;
                    let _e2152: vec2<f32> = centre;
                    let _e2158: vec2<f32> = radius_6;
                    outer = ((_e2151 - (_e2152 + vec2<f32>(0f, 0.014f))) / (_e2158 * 1.12f));
                    let _e2168: vec2<f32> = outer;
                    let _e2169: vec2<f32> = outer;
                    let _e2175: vec2<f32> = outer;
                    let _e2176: vec2<f32> = outer;
                    shadow = (1f - smoothstep(0.52f, 1.22f, dot(_e2175, _e2176)));
                    let _e2181: vec3<f32> = col;
                    let _e2183: f32 = shadow;
                    let _e2185: f32 = occupied;
                    col = (_e2181 * (1f - (_e2183 * (0.1f + (_e2185 * 0.11f)))));
                    let _e2198: f32 = q_3;
                    body = (1f - smoothstep(0.64f, 0.98f, _e2198));
                    let _e2207: f32 = q_3;
                    let _e2215: f32 = q_3;
                    lip = (smoothstep(0.71f, 0.82f, _e2207) * (1f - smoothstep(0.93f, 1.05f, _e2215)));
                    let _e2220: f32 = lip;
                    let _e2224: vec2<f32> = foot;
                    let _e2229: vec2<f32> = foot;
                    raisedEdge = (_e2220 * smoothstep(-0.25f, 0.7f, _e2229.y));
                    let _e2240: f32 = q_3;
                    innerDish = (1f - smoothstep(0.2f, 0.72f, _e2240));
                    let _e2253: vec2<f32> = foot;
                    let _e2261: vec2<f32> = foot;
                    let _e2278: vec2<f32> = foot;
                    let _e2286: vec2<f32> = foot;
                    metal = mix(vec3<f32>(0.017f, 0.033f, 0.047f), vec3<f32>(0.043f, 0.075f, 0.095f), vec3(clamp(((1f - _e2286.y) * 0.34f), 0f, 1f)));
                    let _e2299: f32 = body;
                    let _e2302: vec3<f32> = col;
                    let _e2303: vec3<f32> = metal;
                    let _e2304: f32 = body;
                    col = mix(_e2302, _e2303, vec3((_e2304 * 0.85f)));
                    let _e2309: vec3<f32> = col;
                    let _e2314: f32 = raisedEdge;
                    col = (_e2309 + ((vec3<f32>(0.075f, 0.118f, 0.15f) * _e2314) * 0.18f));
                    let _e2319: vec3<f32> = global.activity_secondary;
                    let _e2322: i32 = slot;
                    let _e2324: vec4<f32> = global.slot_light[_e2322];
                    let _e2327: vec3<f32> = global.activity_secondary;
                    let _e2330: i32 = slot;
                    let _e2332: vec4<f32> = global.slot_light[_e2330];
                    let _e2334: f32 = occupied;
                    activeLight = mix((_e2327 * 0.28f), _e2332.xyz, vec3(_e2334));
                    let _e2338: i32 = slot;
                    let _e2340: vec4<f32> = global.slot_light[_e2338];
                    confirmation = _e2340.w;
                    let _e2343: vec3<f32> = col;
                    let _e2344: vec3<f32> = activeLight;
                    let _e2345: f32 = lip;
                    let _e2348: f32 = occupied;
                    col = (_e2343 + ((_e2344 * _e2345) * (0.034f + (_e2348 * 0.2f))));
                    let _e2354: vec3<f32> = col;
                    let _e2359: f32 = innerDish;
                    let _e2362: f32 = occupied;
                    col = (_e2354 + ((vec3<f32>(0.075f, 0.135f, 0.17f) * _e2359) * (0.014f + (_e2362 * 0.017f))));
                    let _e2371: f32 = q_3;
                    let _e2374: f32 = q_3;
                    let _e2380: f32 = q_3;
                    let _e2383: f32 = q_3;
                    ring1_ = (1f - smoothstep(0.026f, 0.052f, abs((_e2383 - 0.22f))));
                    let _e2393: f32 = q_3;
                    let _e2396: f32 = q_3;
                    let _e2402: f32 = q_3;
                    let _e2405: f32 = q_3;
                    ring2_ = (1f - smoothstep(0.025f, 0.05f, abs((_e2405 - 0.43f))));
                    let _e2412: vec2<f32> = foot;
                    let _e2414: vec2<f32> = foot;
                    let _e2416: vec2<f32> = foot;
                    let _e2418: vec2<f32> = foot;
                    angle = atan2(_e2416.y, _e2418.x);
                    let _e2422: f32 = angle;
                    let _e2429: f32 = global.energy_time;
                    let _e2433: i32 = slot;
                    let _e2438: f32 = angle;
                    let _e2445: f32 = global.energy_time;
                    let _e2449: i32 = slot;
                    phase = fract((((((_e2438 + 3.1415927f) / 6.2831855f) * 12f) - (_e2445 * 0.1f)) + (f32(_e2449) * 0.37f)));
                    let _e2461: f32 = phase;
                    let _e2469: f32 = phase;
                    segments = (smoothstep(0.06f, 0.15f, _e2461) * (1f - smoothstep(0.73f, 0.86f, _e2469)));
                    let _e2474: vec3<f32> = col;
                    let _e2475: vec3<f32> = activeLight;
                    let _e2478: f32 = ring1_;
                    let _e2479: f32 = ring2_;
                    let _e2482: f32 = segments;
                    let _e2485: f32 = occupied;
                    let _e2487: f32 = confirmation;
                    let _e2491: f32 = global.launch_amount;
                    col = (_e2474 + (((_e2475 * max(_e2478, _e2479)) * _e2482) * (0.034f + (_e2485 * ((0.12f + (_e2487 * 0.32f)) + (_e2491 * 0.22f))))));
                    let _e2499: vec2<f32> = centre;
                    let _e2500: vec2<f32> = radius_6;
                    let _e2505: vec2<f32> = radius_6;
                    leftBoot = (_e2499 + vec2<f32>((-(_e2500.x) * 0.3f), (-(_e2505.y) * 0.05f)));
                    let _e2513: vec2<f32> = centre;
                    let _e2514: vec2<f32> = radius_6;
                    let _e2518: vec2<f32> = radius_6;
                    rightBoot = (_e2513 + vec2<f32>((_e2514.x * 0.3f), (-(_e2518.y) * 0.05f)));
                    let _e2528: vec2<f32> = radius_6;
                    let _e2532: vec2<f32> = radius_6;
                    let _e2537: vec2<f32> = uv_8;
                    let _e2538: vec2<f32> = leftBoot;
                    let _e2539: vec2<f32> = radius_6;
                    let _e2543: vec2<f32> = radius_6;
                    let _e2548: f32 = ellipse_mask(_e2537, _e2538, vec2<f32>((_e2539.x * 0.23f), (_e2543.y * 0.44f)));
                    let _e2551: vec2<f32> = radius_6;
                    let _e2555: vec2<f32> = radius_6;
                    let _e2560: vec2<f32> = uv_8;
                    let _e2561: vec2<f32> = rightBoot;
                    let _e2562: vec2<f32> = radius_6;
                    let _e2566: vec2<f32> = radius_6;
                    let _e2571: f32 = ellipse_mask(_e2560, _e2561, vec2<f32>((_e2562.x * 0.23f), (_e2566.y * 0.44f)));
                    boots = (_e2548 + _e2571);
                    let _e2574: vec3<f32> = col;
                    let _e2579: f32 = boots;
                    let _e2581: f32 = occupied;
                    col = (_e2574 * (1f - ((min(1f, _e2579) * _e2581) * 0.13f)));
                }
                continuing {
                    let _e2112: i32 = slot;
                    slot = (_e2112 - 1i);
                }
            }
        }
    } else {
        {
            let _e2593: vec2<f32> = uv_8;
            let _e2594: vec4<f32> = soloPad;
            let _e2597: vec4<f32> = soloPad;
            platformDelta = ((_e2593 - _e2594.xy) / _e2597.zw);
            let _e2603: vec2<f32> = platformDelta;
            let _e2604: vec2<f32> = platformDelta;
            platformQ = dot(_e2603, _e2604);
            let _e2613: f32 = platformQ;
            platformFill = (1f - smoothstep(0.62f, 1f, _e2613));
            let _e2622: f32 = platformQ;
            let _e2630: f32 = platformQ;
            platformEdge = (smoothstep(0.62f, 0.77f, _e2622) * (1f - smoothstep(0.88f, 1f, _e2630)));
            let _e2640: f32 = platformFill;
            let _e2643: vec3<f32> = col;
            let _e2648: f32 = platformFill;
            col = mix(_e2643, vec3<f32>(0.006f, 0.012f, 0.022f), vec3((_e2648 * 0.7f)));
            let _e2659: f32 = platformQ;
            platformCore = (1f - smoothstep(0.08f, 0.7f, _e2659));
            let _e2663: vec3<f32> = col;
            let _e2667: vec3<f32> = global.activity_accent;
            let _e2668: vec3<f32> = global.hunter_halo;
            let _e2672: f32 = platformCore;
            let _e2674: f32 = global.floor_glow;
            col = (_e2663 + (((mix(_e2667, _e2668, vec3(0.3f)) * _e2672) * _e2674) * 0.092f));
            let _e2679: vec3<f32> = col;
            let _e2680: vec3<f32> = global.hunter_halo;
            let _e2681: f32 = platformEdge;
            let _e2683: f32 = global.floor_glow;
            col = (_e2679 + (((_e2680 * _e2681) * _e2683) * 0.3f));
            let _e2688: vec3<f32> = col;
            let _e2689: vec3<f32> = global.activity_secondary;
            let _e2690: f32 = platformEdge;
            let _e2692: f32 = global.floor_glow;
            col = (_e2688 + (((_e2689 * _e2690) * _e2692) * 0.075f));
            let _e2700: f32 = platformQ;
            let _e2703: f32 = platformQ;
            let _e2709: f32 = platformQ;
            let _e2712: f32 = platformQ;
            ringA = (1f - smoothstep(0.018f, 0.05f, abs((_e2712 - 0.2f))));
            let _e2722: f32 = platformQ;
            let _e2725: f32 = platformQ;
            let _e2731: f32 = platformQ;
            let _e2734: f32 = platformQ;
            ringB = (1f - smoothstep(0.018f, 0.05f, abs((_e2734 - 0.4f))));
            let _e2744: f32 = platformQ;
            let _e2747: f32 = platformQ;
            let _e2753: f32 = platformQ;
            let _e2756: f32 = platformQ;
            ringC = (1f - smoothstep(0.018f, 0.05f, abs((_e2756 - 0.6f))));
            let _e2763: vec2<f32> = platformDelta;
            let _e2765: vec2<f32> = platformDelta;
            let _e2767: vec2<f32> = platformDelta;
            let _e2769: vec2<f32> = platformDelta;
            ringAngle = atan2(_e2767.y, _e2769.x);
            let _e2773: f32 = ringAngle;
            let _e2780: f32 = global.energy_time;
            let _e2784: f32 = ringAngle;
            let _e2791: f32 = global.energy_time;
            ringPhase = fract(((((_e2784 + 3.1415927f) / 6.2831855f) * 12f) - (_e2791 * 0.12f)));
            let _e2802: f32 = ringPhase;
            let _e2810: f32 = ringPhase;
            ringSegments = (smoothstep(0.08f, 0.18f, _e2802) * (1f - smoothstep(0.72f, 0.82f, _e2810)));
            let _e2817: f32 = ringAngle;
            let _e2820: f32 = global.energy_time;
            let _e2824: f32 = ringAngle;
            let _e2827: f32 = global.energy_time;
            counterPhase = (0.5f + (0.5f * sin(((_e2824 * 5f) + (_e2827 * 0.64f)))));
            let _e2835: f32 = ringA;
            let _e2836: f32 = ringSegments;
            let _e2838: f32 = ringB;
            let _e2839: f32 = counterPhase;
            let _e2841: f32 = ringC;
            let _e2842: f32 = ringSegments;
            let _e2844: f32 = ringB;
            let _e2845: f32 = counterPhase;
            let _e2847: f32 = ringC;
            let _e2848: f32 = ringSegments;
            let _e2851: f32 = ringA;
            let _e2852: f32 = ringSegments;
            let _e2854: f32 = ringB;
            let _e2855: f32 = counterPhase;
            let _e2857: f32 = ringC;
            let _e2858: f32 = ringSegments;
            let _e2860: f32 = ringB;
            let _e2861: f32 = counterPhase;
            let _e2863: f32 = ringC;
            let _e2864: f32 = ringSegments;
            serviceRings = max((_e2851 * _e2852), max((_e2860 * _e2861), (_e2863 * _e2864)));
            let _e2869: vec3<f32> = col;
            let _e2873: vec3<f32> = global.activity_secondary;
            let _e2874: vec3<f32> = global.hunter_halo;
            let _e2878: f32 = serviceRings;
            let _e2880: f32 = global.floor_glow;
            col = (_e2869 + (((mix(_e2873, _e2874, vec3(0.22f)) * _e2878) * _e2880) * 0.22f));
        }
    }
    let _e2887: vec2<f32> = uv_8;
    let _e2891: vec2<f32> = uv_8;
    let _e2897: vec2<f32> = uv_8;
    let _e2901: vec2<f32> = uv_8;
    lowFog = (smoothstep(0.52f, 0.92f, _e2891.y) * (1f - smoothstep(0.88f, 1f, _e2901.y)));
    let _e2909: f32 = global.time_value;
    if (_e2909 > 0f) {
        let _e2912: f32 = fogWave;
        let _e2913: vec2<f32> = uv_8;
        let _e2917: f32 = global.time_value;
        let _e2921: vec2<f32> = uv_8;
        let _e2925: f32 = global.time_value;
        fogWave = (_e2912 + (sin(((_e2921.x * 8f) + (_e2925 * 0.18f))) * 0.1f));
    }
    let _e2939: vec3<f32> = global.activity_accent;
    fogColor = mix(_e2939, vec3<f32>(0.16f, 0.24f, 0.31f), vec3(0.58f));
    let _e2950: f32 = lowFog;
    let _e2951: f32 = global.fog_amount;
    let _e2955: f32 = fogWave;
    let _e2957: vec3<f32> = col;
    let _e2958: vec3<f32> = fogColor;
    let _e2959: f32 = lowFog;
    let _e2960: f32 = global.fog_amount;
    let _e2964: f32 = fogWave;
    col = mix(_e2957, _e2958, vec3((((_e2959 * _e2960) * 0.16f) * _e2964)));
    let _e2969: f32 = stageHeroX;
    let _e2975: vec2<f32> = uv_8;
    let _e2976: f32 = stageHeroX;
    let _e2982: f32 = ellipse_mask(_e2975, vec2<f32>(_e2976, 0.685f), vec2<f32>(0.315f, 0.135f));
    rearLegFog = _e2982;
    let _e2990: vec3<f32> = global.activity_accent;
    rearFogColor = mix(_e2990, vec3<f32>(0.22f, 0.28f, 0.33f), vec3(0.72f));
    let _e3001: f32 = rearLegFog;
    let _e3002: f32 = global.fog_amount;
    let _e3006: f32 = global.energy;
    let _e3010: vec3<f32> = col;
    let _e3011: vec3<f32> = rearFogColor;
    let _e3012: f32 = rearLegFog;
    let _e3013: f32 = global.fog_amount;
    let _e3017: f32 = global.energy;
    col = mix(_e3010, _e3011, vec3(((_e3012 * _e3013) * (0.045f + (0.02f * _e3017)))));
    let _e3023: f32 = global.time_value;
    let _e3026: f32 = global.time_value;
    drift = (sin((_e3026 * 0.23f)) * 0.04f);
    let _e3033: vec2<f32> = uv_8;
    let _e3037: f32 = drift;
    let _e3039: vec2<f32> = uv_8;
    let _e3047: vec2<f32> = uv_8;
    let _e3051: f32 = drift;
    let _e3053: vec2<f32> = uv_8;
    let _e3063: vec2<f32> = uv_8;
    let _e3067: f32 = drift;
    let _e3069: vec2<f32> = uv_8;
    let _e3077: vec2<f32> = uv_8;
    let _e3081: f32 = drift;
    let _e3083: vec2<f32> = uv_8;
    let _e3094: vec2<f32> = uv_8;
    let _e3098: f32 = drift;
    let _e3100: vec2<f32> = uv_8;
    let _e3108: vec2<f32> = uv_8;
    let _e3112: f32 = drift;
    let _e3114: vec2<f32> = uv_8;
    let _e3124: vec2<f32> = uv_8;
    let _e3128: f32 = drift;
    let _e3130: vec2<f32> = uv_8;
    let _e3138: vec2<f32> = uv_8;
    let _e3142: f32 = drift;
    let _e3144: vec2<f32> = uv_8;
    shafts = (exp(-(pow(((((_e3077.x - 0.36f) - _e3081) + (_e3083.y * 0.07f)) * 29f), 2f))) + exp(-(pow(((((_e3138.x - 0.66f) + _e3142) - (_e3144.y * 0.05f)) * 34f), 2f))));
    let _e3159: vec2<f32> = uv_8;
    let _e3163: vec2<f32> = uv_8;
    let _e3169: vec2<f32> = uv_8;
    let _e3173: vec2<f32> = uv_8;
    reactorShaftGate = (smoothstep(0.18f, 0.3f, _e3163.y) * (1f - smoothstep(0.64f, 0.84f, _e3173.y)));
    let _e3179: vec3<f32> = col;
    let _e3183: vec3<f32> = global.activity_secondary;
    let _e3184: vec3<f32> = global.hunter_rim;
    let _e3188: f32 = shafts;
    let _e3190: f32 = reactorShaftGate;
    let _e3194: f32 = global.energy;
    col = (_e3179 + ((((mix(_e3183, _e3184, vec3(0.38f)) * _e3188) * _e3190) * 0.34f) * _e3194));
    let _e3197: vec2<f32> = uv_8;
    let _e3198: f32 = global.time_value;
    let _e3201: f32 = global.time_value;
    let _e3207: f32 = global.time_value;
    dustUv = (_e3197 + vec2<f32>((sin((_e3201 * 0.09f)) * 0.008f), (_e3207 * 0.014f)));
    let _e3213: vec2<f32> = dustUv;
    let _e3218: vec2<f32> = dustUv;
    cells = floor((_e3218 * vec2<f32>(64f, 36f)));
    let _e3225: vec2<f32> = dustUv;
    let _e3230: vec2<f32> = dustUv;
    cellUv = (fract((_e3230 * vec2<f32>(64f, 36f))) - vec2(0.5f));
    let _e3241: vec2<f32> = cells;
    let _e3242: f32 = hash21_(_e3241);
    seed = _e3242;
    let _e3245: f32 = global.particle_amount;
    let _e3251: f32 = global.particle_amount;
    let _e3255: f32 = seed;
    mote = step((0.955f - (_e3251 * 0.018f)), _e3255);
    let _e3262: vec2<f32> = cellUv;
    let _e3267: vec2<f32> = cellUv;
    sparkle = (1f - smoothstep(0.015f, 0.09f, length(_e3267)));
    let _e3272: f32 = sparkle;
    let _e3277: vec2<f32> = cellUv;
    let _e3282: vec2<f32> = cellUv;
    sparkle = (_e3272 + ((1f - smoothstep(0.03f, 0.28f, length(_e3282))) * 0.16f));
    let _e3291: f32 = global.time_value;
    if (_e3291 > 0f) {
        let _e3294: f32 = twinkle;
        let _e3296: f32 = global.time_value;
        let _e3298: f32 = seed;
        let _e3301: f32 = seed;
        let _e3305: f32 = global.time_value;
        let _e3307: f32 = seed;
        let _e3310: f32 = seed;
        twinkle = (_e3294 + (0.25f * sin(((_e3305 * (0.7f + _e3307)) + (_e3310 * 18f)))));
    }
    let _e3317: vec3<f32> = col;
    let _e3321: vec3<f32> = global.activity_secondary;
    let _e3322: vec3<f32> = global.hunter_rim;
    let _e3326: f32 = mote;
    let _e3328: f32 = sparkle;
    let _e3330: f32 = twinkle;
    let _e3332: f32 = global.particle_amount;
    col = (_e3317 + (((((mix(_e3321, _e3322, vec3(0.45f)) * _e3326) * _e3328) * _e3330) * _e3332) * 1.15f));
    let _e3338: vec3<f32> = col;
    let _e3344: f32 = global.warmth;
    let _e3347: vec3<f32> = col;
    let _e3348: vec3<f32> = col;
    let _e3354: f32 = global.warmth;
    col = mix(_e3347, (_e3348 * vec3<f32>(1.1f, 0.97f, 0.84f)), vec3((_e3354 * 0.3f)));
    let _e3362: vec2<f32> = uv_8;
    let _e3366: vec2<f32> = uv_8;
    leftMask = (1f - smoothstep(0.02f, 0.37f, _e3366.x));
    let _e3373: vec2<f32> = uv_8;
    let _e3377: vec2<f32> = uv_8;
    rightMask = smoothstep(0.78f, 1f, _e3377.x);
    let _e3381: vec3<f32> = col;
    let _e3383: f32 = leftMask;
    let _e3384: f32 = global.left_darken;
    col = (_e3381 * (1f - (_e3383 * _e3384)));
    let _e3388: vec3<f32> = col;
    let _e3390: f32 = rightMask;
    let _e3391: f32 = global.right_darken;
    col = (_e3388 * (1f - (_e3390 * _e3391)));
    let _e3397: vec2<f32> = uv_8;
    let _e3403: vec2<f32> = uv_8;
    let _e3410: vec2<f32> = uv_8;
    let _e3416: vec2<f32> = uv_8;
    let _e3426: vec2<f32> = uv_8;
    let _e3432: vec2<f32> = uv_8;
    let _e3439: vec2<f32> = uv_8;
    let _e3445: vec2<f32> = uv_8;
    vignette = smoothstep(0.56f, 1.03f, length(vec2<f32>(((_e3439.x - 0.52f) * 1.08f), ((_e3445.y - 0.48f) * 0.92f))));
    let _e3455: vec3<f32> = col;
    let _e3457: f32 = vignette;
    let _e3459: f32 = global.background_softness;
    col = (_e3455 * (1f - (_e3457 * (0.2f + (_e3459 * 0.18f)))));
    let _e3467: vec4<f32> = gl_FragCoord;
    let _e3469: vec4<f32> = gl_FragCoord;
    let _e3471: f32 = hash21_(_e3469.xy);
    grain = (_e3471 - 0.5f);
    let _e3475: vec3<f32> = col;
    let _e3476: f32 = grain;
    col = (_e3475 + vec3((_e3476 * 0.006f)));
    let _e3484: vec3<f32> = col;
    let _e3489: vec3<f32> = clamp(_e3484, vec3(0f), vec3(1f));
    prime_output = vec4<f32>(_e3489.x, _e3489.y, _e3489.z, 1f);
    return;
}

@fragment
fn main(@location(0) chamber_uv: vec2<f32>, @builtin(position) param: vec4<f32>) -> FragmentOutput {
    chamber_uv_1 = chamber_uv;
    gl_FragCoord = param;
    main_1();
    let _e203: vec4<f32> = prime_output;
    return FragmentOutput(_e203);
}
