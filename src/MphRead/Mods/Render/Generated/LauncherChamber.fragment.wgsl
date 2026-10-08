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
    let _e176: vec2<f32> = p_1;
    let _e181: vec2<f32> = p_1;
    p_1 = fract((_e181 * vec2<f32>(123.34f, 345.45f)));
    let _e187: vec2<f32> = p_1;
    let _e189: vec2<f32> = p_1;
    let _e193: vec2<f32> = p_1;
    let _e194: vec2<f32> = p_1;
    p_1 = (_e187 + vec2(dot(_e193, (_e194 + vec2(34.345f)))));
    let _e201: vec2<f32> = p_1;
    let _e203: vec2<f32> = p_1;
    let _e206: vec2<f32> = p_1;
    let _e208: vec2<f32> = p_1;
    return fract((_e206.x * _e208.y));
}

fn line(value: f32, width: f32) -> f32 {
    var value_1: f32;
    var width_1: f32;

    value_1 = value;
    width_1 = width;
    let _e180: f32 = width_1;
    let _e184: f32 = value_1;
    let _e186: f32 = width_1;
    let _e187: f32 = width_1;
    let _e191: f32 = value_1;
    return (1f - smoothstep(_e186, (_e187 * 2.2f), abs(_e191)));
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
    let _e182: vec2<f32> = lo_1;
    let _e183: f32 = feather_1;
    let _e188: vec2<f32> = lo_1;
    let _e189: f32 = feather_1;
    let _e192: vec2<f32> = lo_1;
    let _e193: vec2<f32> = uv_1;
    a = smoothstep((_e188 - vec2(_e189)), _e192, _e193);
    let _e198: vec2<f32> = hi_1;
    let _e199: f32 = feather_1;
    let _e203: vec2<f32> = hi_1;
    let _e204: vec2<f32> = hi_1;
    let _e205: f32 = feather_1;
    let _e208: vec2<f32> = uv_1;
    b = (vec2(1f) - smoothstep(_e203, (_e204 + vec2(_e205)), _e208));
    let _e213: vec2<f32> = a;
    let _e215: vec2<f32> = a;
    let _e218: vec2<f32> = b;
    let _e221: vec2<f32> = b;
    return (((_e213.x * _e215.y) * _e218.x) * _e221.y);
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
    let _e180: vec2<f32> = uv_3;
    let _e181: vec2<f32> = center_1;
    let _e186: vec2<f32> = radius_1;
    d = ((_e180 - _e181) / max(_e186, vec2(0.001f)));
    let _e194: vec2<f32> = d;
    let _e195: vec2<f32> = d;
    q = dot(_e194, _e195);
    let _e204: f32 = q;
    return (1f - smoothstep(0.35f, 1f, _e204));
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
    let _e180: vec2<f32> = uv_5;
    let _e181: vec2<f32> = center_3;
    let _e186: vec2<f32> = radius_3;
    d_1 = ((_e180 - _e181) / max(_e186, vec2(0.001f)));
    let _e194: vec2<f32> = d_1;
    let _e195: vec2<f32> = d_1;
    q_1 = dot(_e194, _e195);
    let _e203: f32 = q_1;
    let _e211: f32 = q_1;
    return (smoothstep(0.6f, 0.75f, _e203) * (1f - smoothstep(0.86f, 1f, _e211)));
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
    let _e180: vec2<f32> = uv_7;
    let _e181: vec2<f32> = center_5;
    let _e186: vec2<f32> = radius_5;
    d_2 = ((_e180 - _e181) / max(_e186, vec2(0.001f)));
    let _e194: vec2<f32> = d_2;
    let _e195: vec2<f32> = d_2;
    q_2 = dot(_e194, _e195);
    let _e204: f32 = q_2;
    return (1f - smoothstep(0.62f, 1f, _e204));
}

fn main_1() {
    var uv_8: vec2<f32>;
    var local: f32;
    var stageHeroX: f32;
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
    var i: i32 = 0i;
    var fi: f32;
    var x: f32;
    var rib: f32;
    var gate: f32;
    var leftBrace: f32;
    var rightBrace: f32;
    var braceGate: f32;
    var railChannel: f32;
    var railSpine: f32;
    var railBand: f32;
    var railPattern: f32;
    var local_1: f32;
    var sweepCenter: f32;
    var wallSweep: f32;
    var wallSweepGate: f32;
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
    var slot: i32 = 7i;
    var geometry: vec4<f32>;
    var centre: vec2<f32>;
    var radius_6: vec2<f32>;
    var local_2: f32;
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
    var ring1_: f32;
    var ring2_: f32;
    var angle: f32;
    var phase: f32;
    var segments: f32;
    var leftBoot: vec2<f32>;
    var rightBoot: vec2<f32>;
    var boots: f32;
    var soloPad: vec4<f32> = vec4<f32>(0.615f, 0.805f, 0.235f, 0.06f);
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
    var serviceRings: f32;
    var lowFog: f32;
    var fogWave: f32 = 1f;
    var fogColor: vec3<f32>;
    var rearLegFog: f32;
    var rearFogColor: vec3<f32>;
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

    let _e174: vec2<f32> = chamber_uv_1;
    uv_8 = _e174;
    let _e176: f32 = global.lobby_mode;
    if (_e176 > 0.5f) {
        local = 0.5f;
    } else {
        local = 0.615f;
    }
    let _e182: f32 = local;
    stageHeroX = _e182;
    let _e184: vec2<f32> = global.resolution;
    let _e186: vec2<f32> = global.resolution;
    let _e189: vec2<f32> = global.resolution;
    let _e195: vec2<f32> = global.resolution;
    let _e197: vec2<f32> = global.resolution;
    let _e200: vec2<f32> = global.resolution;
    aspect = max((_e195.x / max(_e200.y, 1f)), 1f);
    let _e208: vec2<f32> = uv_8;
    let _e212: f32 = aspect;
    let _e214: vec2<f32> = uv_8;
    p_2 = vec2<f32>(((_e208.x - 0.5f) * _e212), (_e214.y - 0.5f));
    let _e222: vec2<f32> = uv_8;
    let _e226: vec2<f32> = uv_8;
    upper = smoothstep(0f, 0.52f, _e226.y);
    let _e233: vec3<f32> = global.base_top;
    let _e234: vec3<f32> = global.base_mid;
    let _e235: f32 = upper;
    col = mix(_e233, _e234, vec3(_e235));
    let _e243: vec2<f32> = uv_8;
    let _e247: vec2<f32> = uv_8;
    let _e250: vec3<f32> = col;
    let _e251: vec3<f32> = global.base_bottom;
    let _e254: vec2<f32> = uv_8;
    let _e258: vec2<f32> = uv_8;
    col = mix(_e250, _e251, vec3(smoothstep(0.58f, 1f, _e258.y)));
    let _e263: vec2<f32> = uv_8;
    let _e270: vec2<f32> = uv_8;
    let _e279: vec2<f32> = uv_8;
    let _e286: vec2<f32> = uv_8;
    horizon = exp(-(pow(((_e286.y - 0.52f) * 4.8f), 2f)));
    let _e297: vec3<f32> = col;
    let _e298: vec3<f32> = global.activity_accent;
    let _e299: f32 = horizon;
    let _e303: f32 = global.energy;
    col = (_e297 + (((_e298 * _e299) * 0.035f) * _e303));
    let _e314: vec2<f32> = uv_8;
    let _e322: f32 = box_mask(_e314, vec2<f32>(0.305f, 0.14f), vec2<f32>(0.855f, 0.72f), 0.035f);
    bay = _e322;
    let _e324: vec3<f32> = col;
    let _e328: vec3<f32> = global.activity_accent;
    let _e329: vec3<f32> = global.hunter_halo;
    let _e333: f32 = bay;
    let _e337: f32 = global.structure_amount;
    col = (_e324 + (((mix(_e328, _e329, vec3(0.28f)) * _e333) * 0.04f) * _e337));
    let _e341: f32 = stageHeroX;
    let _e347: vec2<f32> = uv_8;
    let _e348: f32 = stageHeroX;
    let _e354: f32 = ellipse_mask(_e347, vec2<f32>(_e348, 0.49f), vec2<f32>(0.255f, 0.335f));
    torsoLift = _e354;
    let _e362: vec3<f32> = global.base_mid;
    heroAmbient = mix(_e362, vec3<f32>(0.105f, 0.145f, 0.185f), vec3(0.55f));
    let _e371: vec3<f32> = col;
    let _e372: vec3<f32> = heroAmbient;
    let _e373: f32 = torsoLift;
    let _e377: f32 = global.hero_light;
    col = (_e371 + ((_e372 * _e373) * (0.15f + (0.05f * _e377))));
    let _e390: vec2<f32> = uv_8;
    let _e398: f32 = box_mask(_e390, vec2<f32>(0.355f, 0.3f), vec2<f32>(0.455f, 0.69f), 0.014f);
    leftRecess = _e398;
    let _e408: vec2<f32> = uv_8;
    let _e416: f32 = box_mask(_e408, vec2<f32>(0.775f, 0.3f), vec2<f32>(0.855f, 0.69f), 0.014f);
    rightRecess = _e416;
    let _e419: f32 = leftRecess;
    let _e420: f32 = rightRecess;
    let _e423: f32 = leftRecess;
    let _e424: f32 = rightRecess;
    recess = min(1f, (_e423 + _e424));
    let _e428: vec3<f32> = col;
    let _e430: f32 = recess;
    let _e433: f32 = global.structure_amount;
    col = (_e428 * (1f - ((_e430 * 0.1f) * _e433)));
    let _e437: vec2<f32> = uv_8;
    let _e442: vec2<f32> = uv_8;
    let _e447: f32 = line((_e442.x - 0.355f), 0.0022f);
    let _e448: vec2<f32> = uv_8;
    let _e453: vec2<f32> = uv_8;
    let _e458: f32 = line((_e453.x - 0.455f), 0.0022f);
    let _e460: vec2<f32> = uv_8;
    let _e465: vec2<f32> = uv_8;
    let _e470: f32 = line((_e465.x - 0.775f), 0.0022f);
    let _e472: vec2<f32> = uv_8;
    let _e477: vec2<f32> = uv_8;
    let _e482: f32 = line((_e477.x - 0.855f), 0.0022f);
    innerEdges = (((_e447 + _e458) + _e470) + _e482);
    let _e487: vec2<f32> = uv_8;
    let _e491: vec2<f32> = uv_8;
    let _e497: vec2<f32> = uv_8;
    let _e501: vec2<f32> = uv_8;
    innerGate = (smoothstep(0.27f, 0.34f, _e491.y) * (1f - smoothstep(0.68f, 0.74f, _e501.y)));
    let _e507: vec3<f32> = col;
    let _e508: vec3<f32> = global.activity_secondary;
    let _e509: f32 = innerEdges;
    let _e511: f32 = innerGate;
    let _e515: f32 = global.structure_amount;
    col = (_e507 + ((((_e508 * _e509) * _e511) * 0.018f) * _e515));
    let _e526: vec2<f32> = uv_8;
    let _e534: f32 = box_mask(_e526, vec2<f32>(0.245f, 0.23f), vec2<f32>(0.325f, 0.7f), 0.02f);
    farLeftWall = _e534;
    let _e544: vec2<f32> = uv_8;
    let _e552: f32 = box_mask(_e544, vec2<f32>(0.87f, 0.18f), vec2<f32>(0.945f, 0.66f), 0.02f);
    farRightWall = _e552;
    let _e555: f32 = farLeftWall;
    let _e556: f32 = farRightWall;
    let _e559: f32 = farLeftWall;
    let _e560: f32 = farRightWall;
    farWall = min(1f, (_e559 + _e560));
    let _e565: vec3<f32> = global.base_bottom;
    let _e568: f32 = farWall;
    let _e571: f32 = global.structure_amount;
    let _e573: vec3<f32> = col;
    let _e574: vec3<f32> = global.base_bottom;
    let _e577: f32 = farWall;
    let _e580: f32 = global.structure_amount;
    col = mix(_e573, (_e574 * 0.58f), vec3(((_e577 * 0.52f) * _e580)));
    let _e592: vec2<f32> = uv_8;
    let _e600: f32 = box_mask(_e592, vec2<f32>(0.255f, 0.285f), vec2<f32>(0.93f, 0.302f), 0.008f);
    farCrossbeam = _e600;
    let _e603: vec3<f32> = global.base_bottom;
    let _e606: f32 = farCrossbeam;
    let _e609: f32 = global.structure_amount;
    let _e611: vec3<f32> = col;
    let _e612: vec3<f32> = global.base_bottom;
    let _e615: f32 = farCrossbeam;
    let _e618: f32 = global.structure_amount;
    col = mix(_e611, (_e612 * 0.44f), vec3(((_e615 * 0.7f) * _e618)));
    let _e622: vec3<f32> = col;
    let _e623: vec3<f32> = global.activity_accent;
    let _e624: f32 = farCrossbeam;
    let _e628: f32 = global.structure_amount;
    col = (_e622 + (((_e623 * _e624) * 0.02f) * _e628));
    loop {
        let _e633: i32 = i;
        if !((_e633 < 7i)) {
            break;
        }
        {
            let _e640: i32 = i;
            fi = f32(_e640);
            let _e644: f32 = fi;
            x = (0.16f + (_e644 * 0.135f));
            let _e649: vec2<f32> = uv_8;
            let _e651: f32 = x;
            let _e655: vec2<f32> = uv_8;
            let _e659: vec2<f32> = uv_8;
            let _e661: f32 = x;
            let _e665: vec2<f32> = uv_8;
            let _e669: f32 = line((_e659.x - _e661), (0.0018f + (0.001f * _e665.y)));
            rib = _e669;
            let _e673: vec2<f32> = uv_8;
            let _e677: vec2<f32> = uv_8;
            let _e683: vec2<f32> = uv_8;
            let _e687: vec2<f32> = uv_8;
            gate = (smoothstep(0.1f, 0.2f, _e677.y) * (1f - smoothstep(0.76f, 0.9f, _e687.y)));
            let _e693: vec3<f32> = col;
            let _e694: vec3<f32> = global.activity_accent;
            let _e695: f32 = rib;
            let _e697: f32 = gate;
            let _e701: f32 = global.structure_amount;
            col = (_e693 + ((((_e694 * _e695) * _e697) * 0.045f) * _e701));
        }
        continuing {
            let _e637: i32 = i;
            i = (_e637 + 1i);
        }
    }
    let _e704: vec2<f32> = uv_8;
    let _e709: vec2<f32> = uv_8;
    let _e716: vec2<f32> = uv_8;
    let _e721: vec2<f32> = uv_8;
    let _e728: f32 = line(((_e716.x - 0.17f) - ((0.64f - _e721.y) * 0.26f)), 0.006f);
    leftBrace = _e728;
    let _e731: vec2<f32> = uv_8;
    let _e735: vec2<f32> = uv_8;
    let _e743: vec2<f32> = uv_8;
    let _e747: vec2<f32> = uv_8;
    let _e754: f32 = line(((0.91f - _e743.x) - ((0.64f - _e747.y) * 0.22f)), 0.006f);
    rightBrace = _e754;
    let _e758: vec2<f32> = uv_8;
    let _e762: vec2<f32> = uv_8;
    let _e768: vec2<f32> = uv_8;
    let _e772: vec2<f32> = uv_8;
    braceGate = (smoothstep(0.16f, 0.3f, _e762.y) * (1f - smoothstep(0.66f, 0.82f, _e772.y)));
    let _e778: vec3<f32> = col;
    let _e779: vec3<f32> = global.activity_secondary;
    let _e780: f32 = leftBrace;
    let _e781: f32 = rightBrace;
    let _e784: f32 = braceGate;
    let _e788: f32 = global.structure_amount;
    col = (_e778 + ((((_e779 * (_e780 + _e781)) * _e784) * 0.06f) * _e788));
    let _e799: vec2<f32> = uv_8;
    let _e807: f32 = box_mask(_e799, vec2<f32>(0.335f, 0.185f), vec2<f32>(0.845f, 0.255f), 0.01f);
    railChannel = _e807;
    let _e810: vec3<f32> = global.base_bottom;
    let _e813: f32 = railChannel;
    let _e816: vec3<f32> = col;
    let _e817: vec3<f32> = global.base_bottom;
    let _e820: f32 = railChannel;
    col = mix(_e816, (_e817 * 0.38f), vec3((_e820 * 0.76f)));
    let _e833: vec2<f32> = uv_8;
    let _e841: f32 = box_mask(_e833, vec2<f32>(0.345f, 0.211f), vec2<f32>(0.835f, 0.222f), 0.004f);
    railSpine = _e841;
    let _e843: vec3<f32> = col;
    let _e844: vec3<f32> = global.activity_accent;
    let _e845: f32 = railSpine;
    let _e849: f32 = global.structure_amount;
    col = (_e843 + (((_e844 * _e845) * 0.028f) * _e849));
    let _e860: vec2<f32> = uv_8;
    let _e868: f32 = box_mask(_e860, vec2<f32>(0.36f, 0.2f), vec2<f32>(0.82f, 0.24f), 0.008f);
    railBand = _e868;
    let _e871: vec2<f32> = uv_8;
    let _e875: vec2<f32> = uv_8;
    let _e881: vec2<f32> = uv_8;
    let _e885: vec2<f32> = uv_8;
    railPattern = step(0.58f, fract((_e885.x * 22f)));
    let _e892: vec3<f32> = col;
    let _e893: vec3<f32> = global.activity_secondary;
    let _e894: f32 = railBand;
    let _e896: f32 = railPattern;
    let _e900: f32 = global.energy;
    col = (_e892 + (((_e893 * _e894) * _e896) * (0.075f + (0.075f * _e900))));
    let _e905: f32 = global.time_value;
    if (_e905 > 0f) {
        let _e908: f32 = global.time_value;
        let _e912: f32 = global.time_value;
        let _e914: f32 = (_e912 * 0.018f);
        local_1 = ((_e914 - (floor((_e914 / 1.16f)) * 1.16f)) - 0.08f);
    } else {
        local_1 = 0.53f;
    }
    let _e924: f32 = local_1;
    sweepCenter = _e924;
    let _e929: vec2<f32> = uv_8;
    let _e931: f32 = sweepCenter;
    let _e933: vec2<f32> = uv_8;
    let _e935: f32 = sweepCenter;
    let _e940: vec2<f32> = uv_8;
    let _e942: f32 = sweepCenter;
    let _e944: vec2<f32> = uv_8;
    let _e946: f32 = sweepCenter;
    wallSweep = (1f - smoothstep(0.015f, 0.095f, abs((_e944.x - _e946))));
    let _e960: vec2<f32> = uv_8;
    let _e968: f32 = box_mask(_e960, vec2<f32>(0.29f, 0.27f), vec2<f32>(0.91f, 0.7f), 0.035f);
    wallSweepGate = _e968;
    let _e970: vec3<f32> = col;
    let _e975: f32 = wallSweep;
    let _e977: f32 = wallSweepGate;
    let _e981: f32 = global.energy;
    let _e983: f32 = global.structure_amount;
    col = (_e970 + (((((vec3<f32>(0.17f, 0.25f, 0.32f) * _e975) * _e977) * 0.034f) * _e981) * _e983));
    let _e986: f32 = stageHeroX;
    heroCenter = vec2<f32>(_e986, 0.49f);
    let _e995: vec2<f32> = uv_8;
    let _e996: vec2<f32> = heroCenter;
    let _e1000: f32 = ellipse_mask(_e995, _e996, vec2<f32>(0.205f, 0.39f));
    hero = _e1000;
    let _e1004: f32 = global.time_value;
    if (_e1004 > 0f) {
        let _e1007: f32 = pulse;
        let _e1008: f32 = global.time_value;
        let _e1010: f32 = global.pulse_speed;
        let _e1015: f32 = global.time_value;
        let _e1017: f32 = global.pulse_speed;
        pulse = (_e1007 + (sin((_e1015 * (0.55f + (_e1017 * 0.45f)))) * 0.035f));
    }
    let _e1026: vec3<f32> = col;
    let _e1027: vec3<f32> = global.hunter_halo;
    let _e1028: f32 = hero;
    let _e1030: f32 = global.halo_strength;
    let _e1032: f32 = global.hero_light;
    let _e1036: f32 = global.energy;
    let _e1040: f32 = pulse;
    col = (_e1026 + (((((_e1027 * _e1028) * _e1030) * _e1032) * (0.095f + (0.035f * _e1036))) * _e1040));
    let _e1044: f32 = stageHeroX;
    let _e1050: vec2<f32> = uv_8;
    let _e1051: f32 = stageHeroX;
    let _e1057: f32 = ellipse_mask(_e1050, vec2<f32>(_e1051, 0.43f), vec2<f32>(0.105f, 0.32f));
    rimColumn = _e1057;
    let _e1059: vec3<f32> = col;
    let _e1060: vec3<f32> = global.hunter_rim;
    let _e1061: f32 = rimColumn;
    let _e1065: f32 = global.hero_light;
    col = (_e1059 + (((_e1060 * _e1061) * 0.04f) * _e1065));
    let _e1069: f32 = stageHeroX;
    let _e1075: vec2<f32> = uv_8;
    let _e1076: f32 = stageHeroX;
    let _e1082: f32 = ellipse_mask(_e1075, vec2<f32>(_e1076, 0.39f), vec2<f32>(0.165f, 0.225f));
    heroKey = _e1082;
    let _e1089: vec3<f32> = col;
    let _e1090: vec3<f32> = heroKeyColor;
    let _e1091: f32 = heroKey;
    let _e1095: f32 = global.hero_light;
    col = (_e1089 + ((_e1090 * _e1091) * (0.04f + (0.02f * _e1095))));
    let _e1102: vec2<f32> = uv_8;
    let _e1108: vec2<f32> = uv_8;
    let _e1115: vec2<f32> = uv_8;
    let _e1121: vec2<f32> = uv_8;
    let _e1132: vec2<f32> = uv_8;
    let _e1138: vec2<f32> = uv_8;
    let _e1145: vec2<f32> = uv_8;
    let _e1151: vec2<f32> = uv_8;
    shaftA = max(0f, (1f - abs((((_e1145.x - 0.58f) * 6.4f) + ((_e1151.y - 0.15f) * 0.55f)))));
    let _e1164: vec2<f32> = uv_8;
    let _e1170: vec2<f32> = uv_8;
    let _e1177: vec2<f32> = uv_8;
    let _e1183: vec2<f32> = uv_8;
    let _e1194: vec2<f32> = uv_8;
    let _e1200: vec2<f32> = uv_8;
    let _e1207: vec2<f32> = uv_8;
    let _e1213: vec2<f32> = uv_8;
    shaftB = max(0f, (1f - abs((((_e1207.x - 0.7f) * 7.1f) - ((_e1213.y - 0.12f) * 0.45f)))));
    let _e1227: vec2<f32> = uv_8;
    let _e1231: vec2<f32> = uv_8;
    let _e1237: vec2<f32> = uv_8;
    let _e1241: vec2<f32> = uv_8;
    shaftGate = ((1f - smoothstep(0.2f, 0.86f, _e1231.y)) * smoothstep(0.04f, 0.18f, _e1241.y));
    let _e1246: vec3<f32> = col;
    let _e1247: vec3<f32> = global.activity_secondary;
    let _e1248: f32 = shaftA;
    let _e1249: f32 = shaftB;
    let _e1252: f32 = shaftGate;
    let _e1256: f32 = global.beam_intensity;
    let _e1258: f32 = global.energy;
    col = (_e1246 + (((((_e1247 * (_e1248 + _e1249)) * _e1252) * 0.028f) * _e1256) * _e1258));
    let _e1263: vec2<f32> = uv_8;
    let _e1267: vec2<f32> = uv_8;
    floorMask = smoothstep(0.58f, 0.78f, _e1267.y);
    let _e1272: vec3<f32> = global.base_bottom;
    let _e1275: f32 = floorMask;
    let _e1278: vec3<f32> = col;
    let _e1279: vec3<f32> = global.base_bottom;
    let _e1282: f32 = floorMask;
    col = mix(_e1278, (_e1279 * 0.72f), vec3((_e1282 * 0.56f)));
    let _e1287: vec2<f32> = uv_8;
    let _e1292: vec2<f32> = uv_8;
    fy = max((_e1292.y - 0.56f), 0.001f);
    let _e1300: f32 = fy;
    perspectiveY = (1f / ((_e1300 * 10f) + 0.55f));
    let _e1307: f32 = perspectiveY;
    let _e1310: f32 = perspectiveY;
    let _e1317: f32 = perspectiveY;
    let _e1320: f32 = perspectiveY;
    let _e1327: f32 = line((fract((_e1320 * 4.2f)) - 0.5f), 0.06f);
    horizontal = _e1327;
    let _e1329: vec2<f32> = uv_8;
    let _e1331: f32 = stageHeroX;
    let _e1333: f32 = fy;
    let _e1337: f32 = fy;
    centeredX = ((_e1329.x - _e1331) / max((_e1337 + 0.2f), 0.2f));
    let _e1344: f32 = centeredX;
    let _e1347: f32 = centeredX;
    let _e1354: f32 = centeredX;
    let _e1357: f32 = centeredX;
    let _e1364: f32 = line((fract((_e1357 * 4.5f)) - 0.5f), 0.035f);
    vertical = _e1364;
    let _e1368: f32 = horizontal;
    let _e1369: f32 = vertical;
    let _e1371: f32 = floorMask;
    grid = (max(_e1368, _e1369) * _e1371);
    let _e1374: vec3<f32> = col;
    let _e1375: vec3<f32> = global.activity_accent;
    let _e1376: f32 = grid;
    let _e1380: f32 = global.floor_grid;
    col = (_e1374 + (((_e1375 * _e1376) * 0.038f) * _e1380));
    let _e1383: f32 = global.lobby_mode;
    if (_e1383 > 0.5f) {
        {
            loop {
                let _e1388: i32 = slot;
                if !((_e1388 >= 0i)) {
                    break;
                }
                {
                    let _e1395: i32 = slot;
                    let _e1397: vec4<f32> = global.lobby_pad_geometry[_e1395];
                    geometry = _e1397;
                    let _e1399: vec4<f32> = geometry;
                    centre = _e1399.xy;
                    let _e1402: vec4<f32> = geometry;
                    radius_6 = _e1402.zw;
                    let _e1405: i32 = slot;
                    if (_e1405 < 4i) {
                        let _e1408: i32 = slot;
                        let _e1410: f32 = global.lobby_occupancy_a[_e1408];
                        local_2 = _e1410;
                    } else {
                        let _e1411: i32 = slot;
                        let _e1415: f32 = global.lobby_occupancy_b[(_e1411 - 4i)];
                        local_2 = _e1415;
                    }
                    let _e1417: f32 = local_2;
                    occupied = _e1417;
                    let _e1419: vec2<f32> = uv_8;
                    let _e1420: vec2<f32> = centre;
                    let _e1422: vec2<f32> = radius_6;
                    foot = ((_e1419 - _e1420) / _e1422);
                    let _e1427: vec2<f32> = foot;
                    let _e1428: vec2<f32> = foot;
                    q_3 = dot(_e1427, _e1428);
                    let _e1431: vec2<f32> = uv_8;
                    let _e1432: vec2<f32> = centre;
                    let _e1438: vec2<f32> = radius_6;
                    outer = ((_e1431 - (_e1432 + vec2<f32>(0f, 0.014f))) / (_e1438 * 1.12f));
                    let _e1448: vec2<f32> = outer;
                    let _e1449: vec2<f32> = outer;
                    let _e1455: vec2<f32> = outer;
                    let _e1456: vec2<f32> = outer;
                    shadow = (1f - smoothstep(0.52f, 1.22f, dot(_e1455, _e1456)));
                    let _e1461: vec3<f32> = col;
                    let _e1463: f32 = shadow;
                    let _e1465: f32 = occupied;
                    col = (_e1461 * (1f - (_e1463 * (0.1f + (_e1465 * 0.11f)))));
                    let _e1478: f32 = q_3;
                    body = (1f - smoothstep(0.64f, 0.98f, _e1478));
                    let _e1487: f32 = q_3;
                    let _e1495: f32 = q_3;
                    lip = (smoothstep(0.71f, 0.82f, _e1487) * (1f - smoothstep(0.93f, 1.05f, _e1495)));
                    let _e1500: f32 = lip;
                    let _e1504: vec2<f32> = foot;
                    let _e1509: vec2<f32> = foot;
                    raisedEdge = (_e1500 * smoothstep(-0.25f, 0.7f, _e1509.y));
                    let _e1520: f32 = q_3;
                    innerDish = (1f - smoothstep(0.2f, 0.72f, _e1520));
                    let _e1533: vec2<f32> = foot;
                    let _e1541: vec2<f32> = foot;
                    let _e1558: vec2<f32> = foot;
                    let _e1566: vec2<f32> = foot;
                    metal = mix(vec3<f32>(0.017f, 0.033f, 0.047f), vec3<f32>(0.043f, 0.075f, 0.095f), vec3(clamp(((1f - _e1566.y) * 0.34f), 0f, 1f)));
                    let _e1579: f32 = body;
                    let _e1582: vec3<f32> = col;
                    let _e1583: vec3<f32> = metal;
                    let _e1584: f32 = body;
                    col = mix(_e1582, _e1583, vec3((_e1584 * 0.85f)));
                    let _e1589: vec3<f32> = col;
                    let _e1594: f32 = raisedEdge;
                    col = (_e1589 + ((vec3<f32>(0.075f, 0.118f, 0.15f) * _e1594) * 0.18f));
                    let _e1602: vec3<f32> = global.activity_secondary;
                    let _e1603: vec3<f32> = global.hunter_halo;
                    activeLight = mix(_e1602, _e1603, vec3(0.16f));
                    let _e1608: vec3<f32> = col;
                    let _e1609: vec3<f32> = activeLight;
                    let _e1610: f32 = lip;
                    let _e1613: f32 = occupied;
                    col = (_e1608 + ((_e1609 * _e1610) * (0.025f + (_e1613 * 0.14f))));
                    let _e1619: vec3<f32> = col;
                    let _e1624: f32 = innerDish;
                    let _e1627: f32 = occupied;
                    col = (_e1619 + ((vec3<f32>(0.075f, 0.135f, 0.17f) * _e1624) * (0.014f + (_e1627 * 0.017f))));
                    let _e1636: f32 = q_3;
                    let _e1639: f32 = q_3;
                    let _e1645: f32 = q_3;
                    let _e1648: f32 = q_3;
                    ring1_ = (1f - smoothstep(0.026f, 0.052f, abs((_e1648 - 0.22f))));
                    let _e1658: f32 = q_3;
                    let _e1661: f32 = q_3;
                    let _e1667: f32 = q_3;
                    let _e1670: f32 = q_3;
                    ring2_ = (1f - smoothstep(0.025f, 0.05f, abs((_e1670 - 0.43f))));
                    let _e1677: vec2<f32> = foot;
                    let _e1679: vec2<f32> = foot;
                    let _e1681: vec2<f32> = foot;
                    let _e1683: vec2<f32> = foot;
                    angle = atan2(_e1681.y, _e1683.x);
                    let _e1687: f32 = angle;
                    let _e1694: f32 = angle;
                    phase = fract((((_e1694 + 3.1415927f) / 6.2831855f) * 12f));
                    let _e1708: f32 = phase;
                    let _e1716: f32 = phase;
                    segments = (smoothstep(0.06f, 0.15f, _e1708) * (1f - smoothstep(0.73f, 0.86f, _e1716)));
                    let _e1721: vec3<f32> = col;
                    let _e1722: vec3<f32> = activeLight;
                    let _e1725: f32 = ring1_;
                    let _e1726: f32 = ring2_;
                    let _e1729: f32 = segments;
                    let _e1732: f32 = occupied;
                    col = (_e1721 + (((_e1722 * max(_e1725, _e1726)) * _e1729) * (0.024f + (_e1732 * 0.063f))));
                    let _e1738: vec2<f32> = centre;
                    let _e1739: vec2<f32> = radius_6;
                    let _e1744: vec2<f32> = radius_6;
                    leftBoot = (_e1738 + vec2<f32>((-(_e1739.x) * 0.3f), (-(_e1744.y) * 0.05f)));
                    let _e1752: vec2<f32> = centre;
                    let _e1753: vec2<f32> = radius_6;
                    let _e1757: vec2<f32> = radius_6;
                    rightBoot = (_e1752 + vec2<f32>((_e1753.x * 0.3f), (-(_e1757.y) * 0.05f)));
                    let _e1767: vec2<f32> = radius_6;
                    let _e1771: vec2<f32> = radius_6;
                    let _e1776: vec2<f32> = uv_8;
                    let _e1777: vec2<f32> = leftBoot;
                    let _e1778: vec2<f32> = radius_6;
                    let _e1782: vec2<f32> = radius_6;
                    let _e1787: f32 = ellipse_mask(_e1776, _e1777, vec2<f32>((_e1778.x * 0.23f), (_e1782.y * 0.44f)));
                    let _e1790: vec2<f32> = radius_6;
                    let _e1794: vec2<f32> = radius_6;
                    let _e1799: vec2<f32> = uv_8;
                    let _e1800: vec2<f32> = rightBoot;
                    let _e1801: vec2<f32> = radius_6;
                    let _e1805: vec2<f32> = radius_6;
                    let _e1810: f32 = ellipse_mask(_e1799, _e1800, vec2<f32>((_e1801.x * 0.23f), (_e1805.y * 0.44f)));
                    boots = (_e1787 + _e1810);
                    let _e1813: vec3<f32> = col;
                    let _e1818: f32 = boots;
                    let _e1820: f32 = occupied;
                    col = (_e1813 * (1f - ((min(1f, _e1818) * _e1820) * 0.13f)));
                }
                continuing {
                    let _e1392: i32 = slot;
                    slot = (_e1392 - 1i);
                }
            }
        }
    } else {
        {
            let _e1832: vec2<f32> = uv_8;
            let _e1833: vec4<f32> = soloPad;
            let _e1836: vec4<f32> = soloPad;
            platformDelta = ((_e1832 - _e1833.xy) / _e1836.zw);
            let _e1842: vec2<f32> = platformDelta;
            let _e1843: vec2<f32> = platformDelta;
            platformQ = dot(_e1842, _e1843);
            let _e1852: f32 = platformQ;
            platformFill = (1f - smoothstep(0.62f, 1f, _e1852));
            let _e1861: f32 = platformQ;
            let _e1869: f32 = platformQ;
            platformEdge = (smoothstep(0.62f, 0.77f, _e1861) * (1f - smoothstep(0.88f, 1f, _e1869)));
            let _e1879: f32 = platformFill;
            let _e1882: vec3<f32> = col;
            let _e1887: f32 = platformFill;
            col = mix(_e1882, vec3<f32>(0.006f, 0.012f, 0.022f), vec3((_e1887 * 0.7f)));
            let _e1898: f32 = platformQ;
            platformCore = (1f - smoothstep(0.08f, 0.7f, _e1898));
            let _e1902: vec3<f32> = col;
            let _e1906: vec3<f32> = global.activity_accent;
            let _e1907: vec3<f32> = global.hunter_halo;
            let _e1911: f32 = platformCore;
            let _e1913: f32 = global.floor_glow;
            col = (_e1902 + (((mix(_e1906, _e1907, vec3(0.3f)) * _e1911) * _e1913) * 0.045f));
            let _e1918: vec3<f32> = col;
            let _e1919: vec3<f32> = global.hunter_halo;
            let _e1920: f32 = platformEdge;
            let _e1922: f32 = global.floor_glow;
            col = (_e1918 + (((_e1919 * _e1920) * _e1922) * 0.22f));
            let _e1927: vec3<f32> = col;
            let _e1928: vec3<f32> = global.activity_secondary;
            let _e1929: f32 = platformEdge;
            let _e1931: f32 = global.floor_glow;
            col = (_e1927 + (((_e1928 * _e1929) * _e1931) * 0.075f));
            let _e1939: f32 = platformQ;
            let _e1942: f32 = platformQ;
            let _e1948: f32 = platformQ;
            let _e1951: f32 = platformQ;
            ringA = (1f - smoothstep(0.018f, 0.05f, abs((_e1951 - 0.2f))));
            let _e1961: f32 = platformQ;
            let _e1964: f32 = platformQ;
            let _e1970: f32 = platformQ;
            let _e1973: f32 = platformQ;
            ringB = (1f - smoothstep(0.018f, 0.05f, abs((_e1973 - 0.4f))));
            let _e1983: f32 = platformQ;
            let _e1986: f32 = platformQ;
            let _e1992: f32 = platformQ;
            let _e1995: f32 = platformQ;
            ringC = (1f - smoothstep(0.018f, 0.05f, abs((_e1995 - 0.6f))));
            let _e2002: vec2<f32> = platformDelta;
            let _e2004: vec2<f32> = platformDelta;
            let _e2006: vec2<f32> = platformDelta;
            let _e2008: vec2<f32> = platformDelta;
            ringAngle = atan2(_e2006.y, _e2008.x);
            let _e2012: f32 = ringAngle;
            let _e2019: f32 = ringAngle;
            ringPhase = fract((((_e2019 + 3.1415927f) / 6.2831855f) * 12f));
            let _e2033: f32 = ringPhase;
            let _e2041: f32 = ringPhase;
            ringSegments = (smoothstep(0.08f, 0.18f, _e2033) * (1f - smoothstep(0.72f, 0.82f, _e2041)));
            let _e2049: f32 = ringB;
            let _e2050: f32 = ringC;
            let _e2052: f32 = ringA;
            let _e2055: f32 = ringB;
            let _e2056: f32 = ringC;
            let _e2059: f32 = ringSegments;
            serviceRings = (max(_e2052, max(_e2055, _e2056)) * _e2059);
            let _e2062: vec3<f32> = col;
            let _e2066: vec3<f32> = global.activity_secondary;
            let _e2067: vec3<f32> = global.hunter_halo;
            let _e2071: f32 = serviceRings;
            let _e2073: f32 = global.floor_glow;
            col = (_e2062 + (((mix(_e2066, _e2067, vec3(0.22f)) * _e2071) * _e2073) * 0.11f));
        }
    }
    let _e2080: vec2<f32> = uv_8;
    let _e2084: vec2<f32> = uv_8;
    let _e2090: vec2<f32> = uv_8;
    let _e2094: vec2<f32> = uv_8;
    lowFog = (smoothstep(0.52f, 0.92f, _e2084.y) * (1f - smoothstep(0.88f, 1f, _e2094.y)));
    let _e2102: f32 = global.time_value;
    if (_e2102 > 0f) {
        let _e2105: f32 = fogWave;
        let _e2106: vec2<f32> = uv_8;
        let _e2110: f32 = global.time_value;
        let _e2114: vec2<f32> = uv_8;
        let _e2118: f32 = global.time_value;
        fogWave = (_e2105 + (sin(((_e2114.x * 8f) + (_e2118 * 0.18f))) * 0.1f));
    }
    let _e2132: vec3<f32> = global.activity_accent;
    fogColor = mix(_e2132, vec3<f32>(0.16f, 0.24f, 0.31f), vec3(0.58f));
    let _e2143: f32 = lowFog;
    let _e2144: f32 = global.fog_amount;
    let _e2148: f32 = fogWave;
    let _e2150: vec3<f32> = col;
    let _e2151: vec3<f32> = fogColor;
    let _e2152: f32 = lowFog;
    let _e2153: f32 = global.fog_amount;
    let _e2157: f32 = fogWave;
    col = mix(_e2150, _e2151, vec3((((_e2152 * _e2153) * 0.1f) * _e2157)));
    let _e2162: f32 = stageHeroX;
    let _e2168: vec2<f32> = uv_8;
    let _e2169: f32 = stageHeroX;
    let _e2175: f32 = ellipse_mask(_e2168, vec2<f32>(_e2169, 0.685f), vec2<f32>(0.315f, 0.135f));
    rearLegFog = _e2175;
    let _e2183: vec3<f32> = global.activity_accent;
    rearFogColor = mix(_e2183, vec3<f32>(0.22f, 0.28f, 0.33f), vec3(0.72f));
    let _e2194: f32 = rearLegFog;
    let _e2195: f32 = global.fog_amount;
    let _e2199: f32 = global.energy;
    let _e2203: vec3<f32> = col;
    let _e2204: vec3<f32> = rearFogColor;
    let _e2205: f32 = rearLegFog;
    let _e2206: f32 = global.fog_amount;
    let _e2210: f32 = global.energy;
    col = mix(_e2203, _e2204, vec3(((_e2205 * _e2206) * (0.045f + (0.02f * _e2210)))));
    let _e2216: vec2<f32> = uv_8;
    let _e2221: vec2<f32> = uv_8;
    cells = floor((_e2221 * vec2<f32>(80f, 45f)));
    let _e2228: vec2<f32> = uv_8;
    let _e2233: vec2<f32> = uv_8;
    cellUv = (fract((_e2233 * vec2<f32>(80f, 45f))) - vec2(0.5f));
    let _e2244: vec2<f32> = cells;
    let _e2245: f32 = hash21_(_e2244);
    seed = _e2245;
    let _e2248: f32 = global.particle_amount;
    let _e2254: f32 = global.particle_amount;
    let _e2258: f32 = seed;
    mote = step((0.955f - (_e2254 * 0.018f)), _e2258);
    let _e2265: vec2<f32> = cellUv;
    let _e2270: vec2<f32> = cellUv;
    sparkle = (1f - smoothstep(0.02f, 0.12f, length(_e2270)));
    let _e2277: f32 = global.time_value;
    if (_e2277 > 0f) {
        let _e2280: f32 = twinkle;
        let _e2282: f32 = global.time_value;
        let _e2284: f32 = seed;
        let _e2287: f32 = seed;
        let _e2291: f32 = global.time_value;
        let _e2293: f32 = seed;
        let _e2296: f32 = seed;
        twinkle = (_e2280 + (0.25f * sin(((_e2291 * (0.7f + _e2293)) + (_e2296 * 18f)))));
    }
    let _e2303: vec3<f32> = col;
    let _e2307: vec3<f32> = global.activity_secondary;
    let _e2308: vec3<f32> = global.hunter_rim;
    let _e2312: f32 = mote;
    let _e2314: f32 = sparkle;
    let _e2316: f32 = twinkle;
    let _e2318: f32 = global.particle_amount;
    col = (_e2303 + (((((mix(_e2307, _e2308, vec3(0.45f)) * _e2312) * _e2314) * _e2316) * _e2318) * 0.3f));
    let _e2324: vec3<f32> = col;
    let _e2330: f32 = global.warmth;
    let _e2333: vec3<f32> = col;
    let _e2334: vec3<f32> = col;
    let _e2340: f32 = global.warmth;
    col = mix(_e2333, (_e2334 * vec3<f32>(1.1f, 0.97f, 0.84f)), vec3((_e2340 * 0.3f)));
    let _e2348: vec2<f32> = uv_8;
    let _e2352: vec2<f32> = uv_8;
    leftMask = (1f - smoothstep(0.02f, 0.37f, _e2352.x));
    let _e2359: vec2<f32> = uv_8;
    let _e2363: vec2<f32> = uv_8;
    rightMask = smoothstep(0.78f, 1f, _e2363.x);
    let _e2367: vec3<f32> = col;
    let _e2369: f32 = leftMask;
    let _e2370: f32 = global.left_darken;
    col = (_e2367 * (1f - (_e2369 * _e2370)));
    let _e2374: vec3<f32> = col;
    let _e2376: f32 = rightMask;
    let _e2377: f32 = global.right_darken;
    col = (_e2374 * (1f - (_e2376 * _e2377)));
    let _e2383: vec2<f32> = uv_8;
    let _e2389: vec2<f32> = uv_8;
    let _e2396: vec2<f32> = uv_8;
    let _e2402: vec2<f32> = uv_8;
    let _e2412: vec2<f32> = uv_8;
    let _e2418: vec2<f32> = uv_8;
    let _e2425: vec2<f32> = uv_8;
    let _e2431: vec2<f32> = uv_8;
    vignette = smoothstep(0.56f, 1.03f, length(vec2<f32>(((_e2425.x - 0.52f) * 1.08f), ((_e2431.y - 0.48f) * 0.92f))));
    let _e2441: vec3<f32> = col;
    let _e2443: f32 = vignette;
    let _e2445: f32 = global.background_softness;
    col = (_e2441 * (1f - (_e2443 * (0.2f + (_e2445 * 0.18f)))));
    let _e2453: vec4<f32> = gl_FragCoord;
    let _e2455: f32 = global.time_value;
    let _e2461: vec4<f32> = gl_FragCoord;
    let _e2463: f32 = global.time_value;
    let _e2469: f32 = hash21_((_e2461.xy + vec2<f32>((_e2463 * 3f), 0f)));
    grain = (_e2469 - 0.5f);
    let _e2473: vec3<f32> = col;
    let _e2474: f32 = grain;
    col = (_e2473 + vec3((_e2474 * 0.006f)));
    let _e2482: vec3<f32> = col;
    let _e2487: vec3<f32> = clamp(_e2482, vec3(0f), vec3(1f));
    prime_output = vec4<f32>(_e2487.x, _e2487.y, _e2487.z, 1f);
    return;
}

@fragment
fn main(@location(0) chamber_uv: vec2<f32>, @builtin(position) param: vec4<f32>) -> FragmentOutput {
    chamber_uv_1 = chamber_uv;
    gl_FragCoord = param;
    main_1();
    let _e181: vec4<f32> = prime_output;
    return FragmentOutput(_e181);
}
