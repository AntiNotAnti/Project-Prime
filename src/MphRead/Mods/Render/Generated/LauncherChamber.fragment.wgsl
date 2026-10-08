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
    var serviceRings: f32;
    var lowFog: f32;
    var fogWave: f32 = 1f;
    var fogColor: vec3<f32>;
    var rearLegFog: f32;
    var rearFogColor: vec3<f32>;
    var drift: f32;
    var shafts: f32;
    var reactorShaftGate: f32;
    var haloUv: vec2<f32>;
    var haloRadius: f32;
    var haloAngle: f32;
    var haloRing: f32;
    var haloGlow: f32;
    var haloSegments: f32;
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

    let _e174: vec2<f32> = chamber_uv_1;
    uv_8 = _e174;
    let _e178: vec2<f32> = global.resolution;
    let _e180: vec2<f32> = global.resolution;
    let _e183: vec2<f32> = global.resolution;
    let _e189: vec2<f32> = global.resolution;
    let _e191: vec2<f32> = global.resolution;
    let _e194: vec2<f32> = global.resolution;
    aspect = max((_e189.x / max(_e194.y, 1f)), 1f);
    let _e202: vec2<f32> = uv_8;
    let _e206: f32 = aspect;
    let _e208: vec2<f32> = uv_8;
    p_2 = vec2<f32>(((_e202.x - 0.5f) * _e206), (_e208.y - 0.5f));
    let _e216: vec2<f32> = uv_8;
    let _e220: vec2<f32> = uv_8;
    upper = smoothstep(0f, 0.52f, _e220.y);
    let _e227: vec3<f32> = global.base_top;
    let _e228: vec3<f32> = global.base_mid;
    let _e229: f32 = upper;
    col = mix(_e227, _e228, vec3(_e229));
    let _e237: vec2<f32> = uv_8;
    let _e241: vec2<f32> = uv_8;
    let _e244: vec3<f32> = col;
    let _e245: vec3<f32> = global.base_bottom;
    let _e248: vec2<f32> = uv_8;
    let _e252: vec2<f32> = uv_8;
    col = mix(_e244, _e245, vec3(smoothstep(0.58f, 1f, _e252.y)));
    let _e257: vec2<f32> = uv_8;
    let _e264: vec2<f32> = uv_8;
    let _e273: vec2<f32> = uv_8;
    let _e280: vec2<f32> = uv_8;
    horizon = exp(-(pow(((_e280.y - 0.52f) * 4.8f), 2f)));
    let _e291: vec3<f32> = col;
    let _e295: vec3<f32> = global.activity_accent;
    let _e296: vec3<f32> = global.hunter_halo;
    let _e300: f32 = horizon;
    let _e304: f32 = global.energy;
    col = (_e291 + (((mix(_e295, _e296, vec3(0.35f)) * _e300) * 0.087f) * _e304));
    let _e315: vec2<f32> = uv_8;
    let _e323: f32 = box_mask(_e315, vec2<f32>(0.305f, 0.14f), vec2<f32>(0.855f, 0.72f), 0.035f);
    bay = _e323;
    let _e325: vec3<f32> = col;
    let _e329: vec3<f32> = global.activity_accent;
    let _e330: vec3<f32> = global.hunter_halo;
    let _e334: f32 = bay;
    let _e338: f32 = global.structure_amount;
    col = (_e325 + (((mix(_e329, _e330, vec3(0.64f)) * _e334) * 0.075f) * _e338));
    let _e342: f32 = stageHeroX;
    let _e348: vec2<f32> = uv_8;
    let _e349: f32 = stageHeroX;
    let _e355: f32 = ellipse_mask(_e348, vec2<f32>(_e349, 0.49f), vec2<f32>(0.255f, 0.335f));
    torsoLift = _e355;
    let _e363: vec3<f32> = global.base_mid;
    heroAmbient = mix(_e363, vec3<f32>(0.105f, 0.145f, 0.185f), vec3(0.55f));
    let _e372: vec3<f32> = col;
    let _e373: vec3<f32> = heroAmbient;
    let _e374: f32 = torsoLift;
    let _e378: f32 = global.hero_light;
    col = (_e372 + ((_e373 * _e374) * (0.15f + (0.05f * _e378))));
    let _e391: vec2<f32> = uv_8;
    let _e399: f32 = box_mask(_e391, vec2<f32>(0.355f, 0.3f), vec2<f32>(0.455f, 0.69f), 0.014f);
    leftRecess = _e399;
    let _e409: vec2<f32> = uv_8;
    let _e417: f32 = box_mask(_e409, vec2<f32>(0.775f, 0.3f), vec2<f32>(0.855f, 0.69f), 0.014f);
    rightRecess = _e417;
    let _e420: f32 = leftRecess;
    let _e421: f32 = rightRecess;
    let _e424: f32 = leftRecess;
    let _e425: f32 = rightRecess;
    recess = min(1f, (_e424 + _e425));
    let _e429: vec3<f32> = col;
    let _e431: f32 = recess;
    let _e434: f32 = global.structure_amount;
    col = (_e429 * (1f - ((_e431 * 0.1f) * _e434)));
    let _e438: vec2<f32> = uv_8;
    let _e443: vec2<f32> = uv_8;
    let _e448: f32 = line((_e443.x - 0.355f), 0.0022f);
    let _e449: vec2<f32> = uv_8;
    let _e454: vec2<f32> = uv_8;
    let _e459: f32 = line((_e454.x - 0.455f), 0.0022f);
    let _e461: vec2<f32> = uv_8;
    let _e466: vec2<f32> = uv_8;
    let _e471: f32 = line((_e466.x - 0.775f), 0.0022f);
    let _e473: vec2<f32> = uv_8;
    let _e478: vec2<f32> = uv_8;
    let _e483: f32 = line((_e478.x - 0.855f), 0.0022f);
    innerEdges = (((_e448 + _e459) + _e471) + _e483);
    let _e488: vec2<f32> = uv_8;
    let _e492: vec2<f32> = uv_8;
    let _e498: vec2<f32> = uv_8;
    let _e502: vec2<f32> = uv_8;
    innerGate = (smoothstep(0.27f, 0.34f, _e492.y) * (1f - smoothstep(0.68f, 0.74f, _e502.y)));
    let _e508: vec3<f32> = col;
    let _e512: vec3<f32> = global.activity_secondary;
    let _e513: vec3<f32> = global.hunter_rim;
    let _e517: f32 = innerEdges;
    let _e519: f32 = innerGate;
    let _e523: f32 = global.structure_amount;
    col = (_e508 + ((((mix(_e512, _e513, vec3(0.42f)) * _e517) * _e519) * 0.043f) * _e523));
    let _e534: vec2<f32> = uv_8;
    let _e542: f32 = box_mask(_e534, vec2<f32>(0.245f, 0.23f), vec2<f32>(0.325f, 0.7f), 0.02f);
    farLeftWall = _e542;
    let _e552: vec2<f32> = uv_8;
    let _e560: f32 = box_mask(_e552, vec2<f32>(0.87f, 0.18f), vec2<f32>(0.945f, 0.66f), 0.02f);
    farRightWall = _e560;
    let _e563: f32 = farLeftWall;
    let _e564: f32 = farRightWall;
    let _e567: f32 = farLeftWall;
    let _e568: f32 = farRightWall;
    farWall = min(1f, (_e567 + _e568));
    let _e573: vec3<f32> = global.base_bottom;
    let _e576: f32 = farWall;
    let _e579: f32 = global.structure_amount;
    let _e581: vec3<f32> = col;
    let _e582: vec3<f32> = global.base_bottom;
    let _e585: f32 = farWall;
    let _e588: f32 = global.structure_amount;
    col = mix(_e581, (_e582 * 0.58f), vec3(((_e585 * 0.52f) * _e588)));
    let _e600: vec2<f32> = uv_8;
    let _e608: f32 = box_mask(_e600, vec2<f32>(0.255f, 0.285f), vec2<f32>(0.93f, 0.302f), 0.008f);
    farCrossbeam = _e608;
    let _e611: vec3<f32> = global.base_bottom;
    let _e614: f32 = farCrossbeam;
    let _e617: f32 = global.structure_amount;
    let _e619: vec3<f32> = col;
    let _e620: vec3<f32> = global.base_bottom;
    let _e623: f32 = farCrossbeam;
    let _e626: f32 = global.structure_amount;
    col = mix(_e619, (_e620 * 0.44f), vec3(((_e623 * 0.7f) * _e626)));
    let _e630: vec3<f32> = col;
    let _e631: vec3<f32> = global.activity_accent;
    let _e632: f32 = farCrossbeam;
    let _e636: f32 = global.structure_amount;
    col = (_e630 + (((_e631 * _e632) * 0.02f) * _e636));
    loop {
        let _e641: i32 = i;
        if !((_e641 < 7i)) {
            break;
        }
        {
            let _e648: i32 = i;
            fi = f32(_e648);
            let _e652: f32 = fi;
            x = (0.16f + (_e652 * 0.135f));
            let _e657: vec2<f32> = uv_8;
            let _e659: f32 = x;
            let _e663: vec2<f32> = uv_8;
            let _e667: vec2<f32> = uv_8;
            let _e669: f32 = x;
            let _e673: vec2<f32> = uv_8;
            let _e677: f32 = line((_e667.x - _e669), (0.0018f + (0.001f * _e673.y)));
            rib = _e677;
            let _e681: vec2<f32> = uv_8;
            let _e685: vec2<f32> = uv_8;
            let _e691: vec2<f32> = uv_8;
            let _e695: vec2<f32> = uv_8;
            gate = (smoothstep(0.1f, 0.2f, _e685.y) * (1f - smoothstep(0.76f, 0.9f, _e695.y)));
            let _e701: vec3<f32> = col;
            let _e705: vec3<f32> = global.activity_accent;
            let _e706: vec3<f32> = global.hunter_halo;
            let _e710: f32 = rib;
            let _e712: f32 = gate;
            let _e716: f32 = global.structure_amount;
            col = (_e701 + ((((mix(_e705, _e706, vec3(0.25f)) * _e710) * _e712) * 0.065f) * _e716));
        }
        continuing {
            let _e645: i32 = i;
            i = (_e645 + 1i);
        }
    }
    let _e719: vec2<f32> = uv_8;
    let _e724: vec2<f32> = uv_8;
    let _e731: vec2<f32> = uv_8;
    let _e736: vec2<f32> = uv_8;
    let _e743: f32 = line(((_e731.x - 0.17f) - ((0.64f - _e736.y) * 0.26f)), 0.006f);
    leftBrace = _e743;
    let _e746: vec2<f32> = uv_8;
    let _e750: vec2<f32> = uv_8;
    let _e758: vec2<f32> = uv_8;
    let _e762: vec2<f32> = uv_8;
    let _e769: f32 = line(((0.91f - _e758.x) - ((0.64f - _e762.y) * 0.22f)), 0.006f);
    rightBrace = _e769;
    let _e773: vec2<f32> = uv_8;
    let _e777: vec2<f32> = uv_8;
    let _e783: vec2<f32> = uv_8;
    let _e787: vec2<f32> = uv_8;
    braceGate = (smoothstep(0.16f, 0.3f, _e777.y) * (1f - smoothstep(0.66f, 0.82f, _e787.y)));
    let _e793: vec3<f32> = col;
    let _e794: vec3<f32> = global.activity_secondary;
    let _e795: f32 = leftBrace;
    let _e796: f32 = rightBrace;
    let _e799: f32 = braceGate;
    let _e803: f32 = global.structure_amount;
    col = (_e793 + ((((_e794 * (_e795 + _e796)) * _e799) * 0.092f) * _e803));
    let _e814: vec2<f32> = uv_8;
    let _e822: f32 = box_mask(_e814, vec2<f32>(0.335f, 0.185f), vec2<f32>(0.845f, 0.255f), 0.01f);
    railChannel = _e822;
    let _e825: vec3<f32> = global.base_bottom;
    let _e828: f32 = railChannel;
    let _e831: vec3<f32> = col;
    let _e832: vec3<f32> = global.base_bottom;
    let _e835: f32 = railChannel;
    col = mix(_e831, (_e832 * 0.38f), vec3((_e835 * 0.76f)));
    let _e848: vec2<f32> = uv_8;
    let _e856: f32 = box_mask(_e848, vec2<f32>(0.345f, 0.211f), vec2<f32>(0.835f, 0.222f), 0.004f);
    railSpine = _e856;
    let _e858: vec3<f32> = col;
    let _e859: vec3<f32> = global.activity_accent;
    let _e860: f32 = railSpine;
    let _e864: f32 = global.structure_amount;
    col = (_e858 + (((_e859 * _e860) * 0.028f) * _e864));
    let _e875: vec2<f32> = uv_8;
    let _e883: f32 = box_mask(_e875, vec2<f32>(0.36f, 0.2f), vec2<f32>(0.82f, 0.24f), 0.008f);
    railBand = _e883;
    let _e886: vec2<f32> = uv_8;
    let _e890: vec2<f32> = uv_8;
    let _e896: vec2<f32> = uv_8;
    let _e900: vec2<f32> = uv_8;
    railPattern = step(0.58f, fract((_e900.x * 22f)));
    let _e907: vec3<f32> = col;
    let _e911: vec3<f32> = global.activity_secondary;
    let _e912: vec3<f32> = global.hunter_rim;
    let _e916: f32 = railBand;
    let _e918: f32 = railPattern;
    let _e922: f32 = global.energy;
    col = (_e907 + (((mix(_e911, _e912, vec3(0.3f)) * _e916) * _e918) * (0.095f + (0.1f * _e922))));
    let _e927: f32 = global.time_value;
    if (_e927 > 0f) {
        let _e930: f32 = global.time_value;
        let _e934: f32 = global.time_value;
        let _e936: f32 = (_e934 * 0.018f);
        local = ((_e936 - (floor((_e936 / 1.16f)) * 1.16f)) - 0.08f);
    } else {
        local = 0.53f;
    }
    let _e946: f32 = local;
    sweepCenter = _e946;
    let _e951: vec2<f32> = uv_8;
    let _e953: f32 = sweepCenter;
    let _e955: vec2<f32> = uv_8;
    let _e957: f32 = sweepCenter;
    let _e962: vec2<f32> = uv_8;
    let _e964: f32 = sweepCenter;
    let _e966: vec2<f32> = uv_8;
    let _e968: f32 = sweepCenter;
    wallSweep = (1f - smoothstep(0.015f, 0.095f, abs((_e966.x - _e968))));
    let _e982: vec2<f32> = uv_8;
    let _e990: f32 = box_mask(_e982, vec2<f32>(0.29f, 0.27f), vec2<f32>(0.91f, 0.7f), 0.035f);
    wallSweepGate = _e990;
    let _e992: vec3<f32> = col;
    let _e997: f32 = wallSweep;
    let _e999: f32 = wallSweepGate;
    let _e1003: f32 = global.energy;
    let _e1005: f32 = global.structure_amount;
    col = (_e992 + (((((vec3<f32>(0.17f, 0.25f, 0.32f) * _e997) * _e999) * 0.034f) * _e1003) * _e1005));
    let _e1016: vec2<f32> = uv_8;
    let _e1024: f32 = box_mask(_e1016, vec2<f32>(0.31f, 0.275f), vec2<f32>(0.855f, 0.305f), 0.008f);
    signalGate = _e1024;
    let _e1026: vec2<f32> = uv_8;
    let _e1030: f32 = global.time_value;
    let _e1032: f32 = global.pulse_speed;
    let _e1038: vec2<f32> = uv_8;
    let _e1042: f32 = global.time_value;
    let _e1044: f32 = global.pulse_speed;
    signalPhase = sin(((_e1038.x * 36f) - (_e1042 * (0.45f + (_e1044 * 0.65f)))));
    let _e1054: f32 = signalPhase;
    activeSignal = (0.5f + (0.5f * _e1054));
    let _e1058: vec3<f32> = col;
    let _e1062: vec3<f32> = global.activity_secondary;
    let _e1063: vec3<f32> = global.hunter_rim;
    let _e1067: f32 = signalGate;
    let _e1069: f32 = activeSignal;
    let _e1073: f32 = global.energy;
    col = (_e1058 + (((mix(_e1062, _e1063, vec3(0.44f)) * _e1067) * _e1069) * (0.045f + (0.04f * _e1073))));
    let _e1078: f32 = stageHeroX;
    heroCenter = vec2<f32>(_e1078, 0.49f);
    let _e1087: vec2<f32> = uv_8;
    let _e1088: vec2<f32> = heroCenter;
    let _e1092: f32 = ellipse_mask(_e1087, _e1088, vec2<f32>(0.205f, 0.39f));
    hero = _e1092;
    let _e1096: f32 = global.time_value;
    if (_e1096 > 0f) {
        let _e1099: f32 = pulse;
        let _e1100: f32 = global.time_value;
        let _e1102: f32 = global.pulse_speed;
        let _e1107: f32 = global.time_value;
        let _e1109: f32 = global.pulse_speed;
        pulse = (_e1099 + (sin((_e1107 * (0.55f + (_e1109 * 0.45f)))) * 0.035f));
    }
    let _e1118: vec3<f32> = col;
    let _e1119: vec3<f32> = global.hunter_halo;
    let _e1120: f32 = hero;
    let _e1122: f32 = global.halo_strength;
    let _e1124: f32 = global.hero_light;
    let _e1128: f32 = global.energy;
    let _e1132: f32 = pulse;
    col = (_e1118 + (((((_e1119 * _e1120) * _e1122) * _e1124) * (0.215f + (0.09f * _e1128))) * _e1132));
    let _e1136: f32 = stageHeroX;
    let _e1142: vec2<f32> = uv_8;
    let _e1143: f32 = stageHeroX;
    let _e1149: f32 = ellipse_mask(_e1142, vec2<f32>(_e1143, 0.43f), vec2<f32>(0.105f, 0.32f));
    rimColumn = _e1149;
    let _e1151: vec3<f32> = col;
    let _e1152: vec3<f32> = global.hunter_rim;
    let _e1153: f32 = rimColumn;
    let _e1157: f32 = global.hero_light;
    col = (_e1151 + (((_e1152 * _e1153) * 0.09f) * _e1157));
    let _e1161: f32 = stageHeroX;
    let _e1167: vec2<f32> = uv_8;
    let _e1168: f32 = stageHeroX;
    let _e1174: f32 = ellipse_mask(_e1167, vec2<f32>(_e1168, 0.39f), vec2<f32>(0.165f, 0.225f));
    heroKey = _e1174;
    let _e1181: vec3<f32> = col;
    let _e1182: vec3<f32> = heroKeyColor;
    let _e1183: f32 = heroKey;
    let _e1187: f32 = global.hero_light;
    col = (_e1181 + ((_e1182 * _e1183) * (0.04f + (0.02f * _e1187))));
    let _e1194: vec2<f32> = uv_8;
    let _e1200: vec2<f32> = uv_8;
    let _e1207: vec2<f32> = uv_8;
    let _e1213: vec2<f32> = uv_8;
    let _e1224: vec2<f32> = uv_8;
    let _e1230: vec2<f32> = uv_8;
    let _e1237: vec2<f32> = uv_8;
    let _e1243: vec2<f32> = uv_8;
    shaftA = max(0f, (1f - abs((((_e1237.x - 0.58f) * 6.4f) + ((_e1243.y - 0.15f) * 0.55f)))));
    let _e1256: vec2<f32> = uv_8;
    let _e1262: vec2<f32> = uv_8;
    let _e1269: vec2<f32> = uv_8;
    let _e1275: vec2<f32> = uv_8;
    let _e1286: vec2<f32> = uv_8;
    let _e1292: vec2<f32> = uv_8;
    let _e1299: vec2<f32> = uv_8;
    let _e1305: vec2<f32> = uv_8;
    shaftB = max(0f, (1f - abs((((_e1299.x - 0.7f) * 7.1f) - ((_e1305.y - 0.12f) * 0.45f)))));
    let _e1319: vec2<f32> = uv_8;
    let _e1323: vec2<f32> = uv_8;
    let _e1329: vec2<f32> = uv_8;
    let _e1333: vec2<f32> = uv_8;
    shaftGate = ((1f - smoothstep(0.2f, 0.86f, _e1323.y)) * smoothstep(0.04f, 0.18f, _e1333.y));
    let _e1338: vec3<f32> = col;
    let _e1339: vec3<f32> = global.activity_secondary;
    let _e1340: f32 = shaftA;
    let _e1341: f32 = shaftB;
    let _e1344: f32 = shaftGate;
    let _e1348: f32 = global.beam_intensity;
    let _e1350: f32 = global.energy;
    col = (_e1338 + (((((_e1339 * (_e1340 + _e1341)) * _e1344) * 0.028f) * _e1348) * _e1350));
    let _e1355: vec2<f32> = uv_8;
    let _e1359: vec2<f32> = uv_8;
    floorMask = smoothstep(0.58f, 0.78f, _e1359.y);
    let _e1364: vec3<f32> = global.base_bottom;
    let _e1367: f32 = floorMask;
    let _e1370: vec3<f32> = col;
    let _e1371: vec3<f32> = global.base_bottom;
    let _e1374: f32 = floorMask;
    col = mix(_e1370, (_e1371 * 0.72f), vec3((_e1374 * 0.56f)));
    let _e1379: vec2<f32> = uv_8;
    let _e1384: vec2<f32> = uv_8;
    fy = max((_e1384.y - 0.56f), 0.001f);
    let _e1392: f32 = fy;
    perspectiveY = (1f / ((_e1392 * 10f) + 0.55f));
    let _e1399: f32 = perspectiveY;
    let _e1402: f32 = perspectiveY;
    let _e1409: f32 = perspectiveY;
    let _e1412: f32 = perspectiveY;
    let _e1419: f32 = line((fract((_e1412 * 4.2f)) - 0.5f), 0.06f);
    horizontal = _e1419;
    let _e1421: vec2<f32> = uv_8;
    let _e1423: f32 = stageHeroX;
    let _e1425: f32 = fy;
    let _e1429: f32 = fy;
    centeredX = ((_e1421.x - _e1423) / max((_e1429 + 0.2f), 0.2f));
    let _e1436: f32 = centeredX;
    let _e1439: f32 = centeredX;
    let _e1446: f32 = centeredX;
    let _e1449: f32 = centeredX;
    let _e1456: f32 = line((fract((_e1449 * 4.5f)) - 0.5f), 0.035f);
    vertical = _e1456;
    let _e1460: f32 = horizontal;
    let _e1461: f32 = vertical;
    let _e1463: f32 = floorMask;
    grid = (max(_e1460, _e1461) * _e1463);
    let _e1466: vec3<f32> = col;
    let _e1470: vec3<f32> = global.activity_accent;
    let _e1471: vec3<f32> = global.hunter_halo;
    let _e1475: f32 = grid;
    let _e1479: f32 = global.floor_grid;
    col = (_e1466 + (((mix(_e1470, _e1471, vec3(0.26f)) * _e1475) * 0.058f) * _e1479));
    let _e1482: f32 = global.lobby_mode;
    if (_e1482 > 0.5f) {
        {
            let _e1485: vec2<f32> = uv_8;
            let _e1489: vec2<f32> = uv_8;
            let _e1495: vec2<f32> = uv_8;
            let _e1502: vec2<f32> = uv_8;
            let _e1506: vec2<f32> = uv_8;
            let _e1512: vec2<f32> = uv_8;
            let _e1519: f32 = line((abs((_e1506.x - 0.5f)) - ((0.79f - _e1512.y) * 0.42f)), 0.0035f);
            serviceSpine = _e1519;
            let _e1523: vec2<f32> = uv_8;
            let _e1527: vec2<f32> = uv_8;
            let _e1533: vec2<f32> = uv_8;
            let _e1537: vec2<f32> = uv_8;
            serviceGate = (smoothstep(0.29f, 0.43f, _e1527.y) * (1f - smoothstep(0.75f, 0.83f, _e1537.y)));
            let _e1543: vec3<f32> = col;
            let _e1544: vec3<f32> = global.activity_secondary;
            let _e1545: f32 = serviceSpine;
            let _e1547: f32 = serviceGate;
            let _e1551: f32 = global.energy;
            col = (_e1543 + ((((_e1544 * _e1545) * _e1547) * 0.13f) * _e1551));
            loop {
                let _e1556: i32 = slot;
                if !((_e1556 >= 0i)) {
                    break;
                }
                {
                    let _e1563: i32 = slot;
                    let _e1565: vec4<f32> = global.lobby_pad_geometry[_e1563];
                    geometry = _e1565;
                    let _e1567: vec4<f32> = geometry;
                    centre = _e1567.xy;
                    let _e1570: vec4<f32> = geometry;
                    radius_6 = _e1570.zw;
                    let _e1573: i32 = slot;
                    if (_e1573 < 4i) {
                        let _e1576: i32 = slot;
                        let _e1578: f32 = global.lobby_occupancy_a[_e1576];
                        local_1 = _e1578;
                    } else {
                        let _e1579: i32 = slot;
                        let _e1583: f32 = global.lobby_occupancy_b[(_e1579 - 4i)];
                        local_1 = _e1583;
                    }
                    let _e1585: f32 = local_1;
                    occupied = _e1585;
                    let _e1587: vec2<f32> = uv_8;
                    let _e1588: vec2<f32> = centre;
                    let _e1590: vec2<f32> = radius_6;
                    foot = ((_e1587 - _e1588) / _e1590);
                    let _e1595: vec2<f32> = foot;
                    let _e1596: vec2<f32> = foot;
                    q_3 = dot(_e1595, _e1596);
                    let _e1599: vec2<f32> = uv_8;
                    let _e1600: vec2<f32> = centre;
                    let _e1606: vec2<f32> = radius_6;
                    outer = ((_e1599 - (_e1600 + vec2<f32>(0f, 0.014f))) / (_e1606 * 1.12f));
                    let _e1616: vec2<f32> = outer;
                    let _e1617: vec2<f32> = outer;
                    let _e1623: vec2<f32> = outer;
                    let _e1624: vec2<f32> = outer;
                    shadow = (1f - smoothstep(0.52f, 1.22f, dot(_e1623, _e1624)));
                    let _e1629: vec3<f32> = col;
                    let _e1631: f32 = shadow;
                    let _e1633: f32 = occupied;
                    col = (_e1629 * (1f - (_e1631 * (0.1f + (_e1633 * 0.11f)))));
                    let _e1646: f32 = q_3;
                    body = (1f - smoothstep(0.64f, 0.98f, _e1646));
                    let _e1655: f32 = q_3;
                    let _e1663: f32 = q_3;
                    lip = (smoothstep(0.71f, 0.82f, _e1655) * (1f - smoothstep(0.93f, 1.05f, _e1663)));
                    let _e1668: f32 = lip;
                    let _e1672: vec2<f32> = foot;
                    let _e1677: vec2<f32> = foot;
                    raisedEdge = (_e1668 * smoothstep(-0.25f, 0.7f, _e1677.y));
                    let _e1688: f32 = q_3;
                    innerDish = (1f - smoothstep(0.2f, 0.72f, _e1688));
                    let _e1701: vec2<f32> = foot;
                    let _e1709: vec2<f32> = foot;
                    let _e1726: vec2<f32> = foot;
                    let _e1734: vec2<f32> = foot;
                    metal = mix(vec3<f32>(0.017f, 0.033f, 0.047f), vec3<f32>(0.043f, 0.075f, 0.095f), vec3(clamp(((1f - _e1734.y) * 0.34f), 0f, 1f)));
                    let _e1747: f32 = body;
                    let _e1750: vec3<f32> = col;
                    let _e1751: vec3<f32> = metal;
                    let _e1752: f32 = body;
                    col = mix(_e1750, _e1751, vec3((_e1752 * 0.85f)));
                    let _e1757: vec3<f32> = col;
                    let _e1762: f32 = raisedEdge;
                    col = (_e1757 + ((vec3<f32>(0.075f, 0.118f, 0.15f) * _e1762) * 0.18f));
                    let _e1770: vec3<f32> = global.activity_secondary;
                    let _e1771: vec3<f32> = global.hunter_halo;
                    activeLight = mix(_e1770, _e1771, vec3(0.38f));
                    let _e1776: vec3<f32> = col;
                    let _e1777: vec3<f32> = activeLight;
                    let _e1778: f32 = lip;
                    let _e1781: f32 = occupied;
                    col = (_e1776 + ((_e1777 * _e1778) * (0.034f + (_e1781 * 0.2f))));
                    let _e1787: vec3<f32> = col;
                    let _e1792: f32 = innerDish;
                    let _e1795: f32 = occupied;
                    col = (_e1787 + ((vec3<f32>(0.075f, 0.135f, 0.17f) * _e1792) * (0.014f + (_e1795 * 0.017f))));
                    let _e1804: f32 = q_3;
                    let _e1807: f32 = q_3;
                    let _e1813: f32 = q_3;
                    let _e1816: f32 = q_3;
                    ring1_ = (1f - smoothstep(0.026f, 0.052f, abs((_e1816 - 0.22f))));
                    let _e1826: f32 = q_3;
                    let _e1829: f32 = q_3;
                    let _e1835: f32 = q_3;
                    let _e1838: f32 = q_3;
                    ring2_ = (1f - smoothstep(0.025f, 0.05f, abs((_e1838 - 0.43f))));
                    let _e1845: vec2<f32> = foot;
                    let _e1847: vec2<f32> = foot;
                    let _e1849: vec2<f32> = foot;
                    let _e1851: vec2<f32> = foot;
                    angle = atan2(_e1849.y, _e1851.x);
                    let _e1855: f32 = angle;
                    let _e1862: f32 = angle;
                    phase = fract((((_e1862 + 3.1415927f) / 6.2831855f) * 12f));
                    let _e1876: f32 = phase;
                    let _e1884: f32 = phase;
                    segments = (smoothstep(0.06f, 0.15f, _e1876) * (1f - smoothstep(0.73f, 0.86f, _e1884)));
                    let _e1889: vec3<f32> = col;
                    let _e1890: vec3<f32> = activeLight;
                    let _e1893: f32 = ring1_;
                    let _e1894: f32 = ring2_;
                    let _e1897: f32 = segments;
                    let _e1900: f32 = occupied;
                    col = (_e1889 + (((_e1890 * max(_e1893, _e1894)) * _e1897) * (0.034f + (_e1900 * 0.084f))));
                    let _e1906: vec2<f32> = centre;
                    let _e1907: vec2<f32> = radius_6;
                    let _e1912: vec2<f32> = radius_6;
                    leftBoot = (_e1906 + vec2<f32>((-(_e1907.x) * 0.3f), (-(_e1912.y) * 0.05f)));
                    let _e1920: vec2<f32> = centre;
                    let _e1921: vec2<f32> = radius_6;
                    let _e1925: vec2<f32> = radius_6;
                    rightBoot = (_e1920 + vec2<f32>((_e1921.x * 0.3f), (-(_e1925.y) * 0.05f)));
                    let _e1935: vec2<f32> = radius_6;
                    let _e1939: vec2<f32> = radius_6;
                    let _e1944: vec2<f32> = uv_8;
                    let _e1945: vec2<f32> = leftBoot;
                    let _e1946: vec2<f32> = radius_6;
                    let _e1950: vec2<f32> = radius_6;
                    let _e1955: f32 = ellipse_mask(_e1944, _e1945, vec2<f32>((_e1946.x * 0.23f), (_e1950.y * 0.44f)));
                    let _e1958: vec2<f32> = radius_6;
                    let _e1962: vec2<f32> = radius_6;
                    let _e1967: vec2<f32> = uv_8;
                    let _e1968: vec2<f32> = rightBoot;
                    let _e1969: vec2<f32> = radius_6;
                    let _e1973: vec2<f32> = radius_6;
                    let _e1978: f32 = ellipse_mask(_e1967, _e1968, vec2<f32>((_e1969.x * 0.23f), (_e1973.y * 0.44f)));
                    boots = (_e1955 + _e1978);
                    let _e1981: vec3<f32> = col;
                    let _e1986: f32 = boots;
                    let _e1988: f32 = occupied;
                    col = (_e1981 * (1f - ((min(1f, _e1986) * _e1988) * 0.13f)));
                }
                continuing {
                    let _e1560: i32 = slot;
                    slot = (_e1560 - 1i);
                }
            }
        }
    } else {
        {
            let _e2000: vec2<f32> = uv_8;
            let _e2001: vec4<f32> = soloPad;
            let _e2004: vec4<f32> = soloPad;
            platformDelta = ((_e2000 - _e2001.xy) / _e2004.zw);
            let _e2010: vec2<f32> = platformDelta;
            let _e2011: vec2<f32> = platformDelta;
            platformQ = dot(_e2010, _e2011);
            let _e2020: f32 = platformQ;
            platformFill = (1f - smoothstep(0.62f, 1f, _e2020));
            let _e2029: f32 = platformQ;
            let _e2037: f32 = platformQ;
            platformEdge = (smoothstep(0.62f, 0.77f, _e2029) * (1f - smoothstep(0.88f, 1f, _e2037)));
            let _e2047: f32 = platformFill;
            let _e2050: vec3<f32> = col;
            let _e2055: f32 = platformFill;
            col = mix(_e2050, vec3<f32>(0.006f, 0.012f, 0.022f), vec3((_e2055 * 0.7f)));
            let _e2066: f32 = platformQ;
            platformCore = (1f - smoothstep(0.08f, 0.7f, _e2066));
            let _e2070: vec3<f32> = col;
            let _e2074: vec3<f32> = global.activity_accent;
            let _e2075: vec3<f32> = global.hunter_halo;
            let _e2079: f32 = platformCore;
            let _e2081: f32 = global.floor_glow;
            col = (_e2070 + (((mix(_e2074, _e2075, vec3(0.3f)) * _e2079) * _e2081) * 0.092f));
            let _e2086: vec3<f32> = col;
            let _e2087: vec3<f32> = global.hunter_halo;
            let _e2088: f32 = platformEdge;
            let _e2090: f32 = global.floor_glow;
            col = (_e2086 + (((_e2087 * _e2088) * _e2090) * 0.3f));
            let _e2095: vec3<f32> = col;
            let _e2096: vec3<f32> = global.activity_secondary;
            let _e2097: f32 = platformEdge;
            let _e2099: f32 = global.floor_glow;
            col = (_e2095 + (((_e2096 * _e2097) * _e2099) * 0.075f));
            let _e2107: f32 = platformQ;
            let _e2110: f32 = platformQ;
            let _e2116: f32 = platformQ;
            let _e2119: f32 = platformQ;
            ringA = (1f - smoothstep(0.018f, 0.05f, abs((_e2119 - 0.2f))));
            let _e2129: f32 = platformQ;
            let _e2132: f32 = platformQ;
            let _e2138: f32 = platformQ;
            let _e2141: f32 = platformQ;
            ringB = (1f - smoothstep(0.018f, 0.05f, abs((_e2141 - 0.4f))));
            let _e2151: f32 = platformQ;
            let _e2154: f32 = platformQ;
            let _e2160: f32 = platformQ;
            let _e2163: f32 = platformQ;
            ringC = (1f - smoothstep(0.018f, 0.05f, abs((_e2163 - 0.6f))));
            let _e2170: vec2<f32> = platformDelta;
            let _e2172: vec2<f32> = platformDelta;
            let _e2174: vec2<f32> = platformDelta;
            let _e2176: vec2<f32> = platformDelta;
            ringAngle = atan2(_e2174.y, _e2176.x);
            let _e2180: f32 = ringAngle;
            let _e2187: f32 = ringAngle;
            ringPhase = fract((((_e2187 + 3.1415927f) / 6.2831855f) * 12f));
            let _e2201: f32 = ringPhase;
            let _e2209: f32 = ringPhase;
            ringSegments = (smoothstep(0.08f, 0.18f, _e2201) * (1f - smoothstep(0.72f, 0.82f, _e2209)));
            let _e2217: f32 = ringB;
            let _e2218: f32 = ringC;
            let _e2220: f32 = ringA;
            let _e2223: f32 = ringB;
            let _e2224: f32 = ringC;
            let _e2227: f32 = ringSegments;
            serviceRings = (max(_e2220, max(_e2223, _e2224)) * _e2227);
            let _e2230: vec3<f32> = col;
            let _e2234: vec3<f32> = global.activity_secondary;
            let _e2235: vec3<f32> = global.hunter_halo;
            let _e2239: f32 = serviceRings;
            let _e2241: f32 = global.floor_glow;
            col = (_e2230 + (((mix(_e2234, _e2235, vec3(0.22f)) * _e2239) * _e2241) * 0.11f));
        }
    }
    let _e2248: vec2<f32> = uv_8;
    let _e2252: vec2<f32> = uv_8;
    let _e2258: vec2<f32> = uv_8;
    let _e2262: vec2<f32> = uv_8;
    lowFog = (smoothstep(0.52f, 0.92f, _e2252.y) * (1f - smoothstep(0.88f, 1f, _e2262.y)));
    let _e2270: f32 = global.time_value;
    if (_e2270 > 0f) {
        let _e2273: f32 = fogWave;
        let _e2274: vec2<f32> = uv_8;
        let _e2278: f32 = global.time_value;
        let _e2282: vec2<f32> = uv_8;
        let _e2286: f32 = global.time_value;
        fogWave = (_e2273 + (sin(((_e2282.x * 8f) + (_e2286 * 0.18f))) * 0.1f));
    }
    let _e2300: vec3<f32> = global.activity_accent;
    fogColor = mix(_e2300, vec3<f32>(0.16f, 0.24f, 0.31f), vec3(0.58f));
    let _e2311: f32 = lowFog;
    let _e2312: f32 = global.fog_amount;
    let _e2316: f32 = fogWave;
    let _e2318: vec3<f32> = col;
    let _e2319: vec3<f32> = fogColor;
    let _e2320: f32 = lowFog;
    let _e2321: f32 = global.fog_amount;
    let _e2325: f32 = fogWave;
    col = mix(_e2318, _e2319, vec3((((_e2320 * _e2321) * 0.1f) * _e2325)));
    let _e2330: f32 = stageHeroX;
    let _e2336: vec2<f32> = uv_8;
    let _e2337: f32 = stageHeroX;
    let _e2343: f32 = ellipse_mask(_e2336, vec2<f32>(_e2337, 0.685f), vec2<f32>(0.315f, 0.135f));
    rearLegFog = _e2343;
    let _e2351: vec3<f32> = global.activity_accent;
    rearFogColor = mix(_e2351, vec3<f32>(0.22f, 0.28f, 0.33f), vec3(0.72f));
    let _e2362: f32 = rearLegFog;
    let _e2363: f32 = global.fog_amount;
    let _e2367: f32 = global.energy;
    let _e2371: vec3<f32> = col;
    let _e2372: vec3<f32> = rearFogColor;
    let _e2373: f32 = rearLegFog;
    let _e2374: f32 = global.fog_amount;
    let _e2378: f32 = global.energy;
    col = mix(_e2371, _e2372, vec3(((_e2373 * _e2374) * (0.045f + (0.02f * _e2378)))));
    let _e2384: f32 = global.time_value;
    let _e2387: f32 = global.time_value;
    drift = (sin((_e2387 * 0.14f)) * 0.015f);
    let _e2394: vec2<f32> = uv_8;
    let _e2398: f32 = drift;
    let _e2400: vec2<f32> = uv_8;
    let _e2408: vec2<f32> = uv_8;
    let _e2412: f32 = drift;
    let _e2414: vec2<f32> = uv_8;
    let _e2424: vec2<f32> = uv_8;
    let _e2428: f32 = drift;
    let _e2430: vec2<f32> = uv_8;
    let _e2438: vec2<f32> = uv_8;
    let _e2442: f32 = drift;
    let _e2444: vec2<f32> = uv_8;
    let _e2455: vec2<f32> = uv_8;
    let _e2459: f32 = drift;
    let _e2461: vec2<f32> = uv_8;
    let _e2469: vec2<f32> = uv_8;
    let _e2473: f32 = drift;
    let _e2475: vec2<f32> = uv_8;
    let _e2485: vec2<f32> = uv_8;
    let _e2489: f32 = drift;
    let _e2491: vec2<f32> = uv_8;
    let _e2499: vec2<f32> = uv_8;
    let _e2503: f32 = drift;
    let _e2505: vec2<f32> = uv_8;
    shafts = (exp(-(pow(((((_e2438.x - 0.36f) - _e2442) + (_e2444.y * 0.07f)) * 29f), 2f))) + exp(-(pow(((((_e2499.x - 0.66f) + _e2503) - (_e2505.y * 0.05f)) * 34f), 2f))));
    let _e2520: vec2<f32> = uv_8;
    let _e2524: vec2<f32> = uv_8;
    let _e2530: vec2<f32> = uv_8;
    let _e2534: vec2<f32> = uv_8;
    reactorShaftGate = (smoothstep(0.18f, 0.3f, _e2524.y) * (1f - smoothstep(0.64f, 0.84f, _e2534.y)));
    let _e2540: vec3<f32> = col;
    let _e2544: vec3<f32> = global.activity_secondary;
    let _e2545: vec3<f32> = global.hunter_rim;
    let _e2549: f32 = shafts;
    let _e2551: f32 = reactorShaftGate;
    let _e2555: f32 = global.energy;
    col = (_e2540 + ((((mix(_e2544, _e2545, vec3(0.38f)) * _e2549) * _e2551) * 0.17f) * _e2555));
    let _e2558: vec2<f32> = uv_8;
    let _e2560: f32 = stageHeroX;
    let _e2562: f32 = aspect;
    let _e2564: vec2<f32> = uv_8;
    haloUv = vec2<f32>(((_e2558.x - _e2560) * _e2562), (_e2564.y - 0.46f));
    let _e2571: vec2<f32> = haloUv;
    haloRadius = length(_e2571);
    let _e2574: vec2<f32> = haloUv;
    let _e2576: vec2<f32> = haloUv;
    let _e2578: vec2<f32> = haloUv;
    let _e2580: vec2<f32> = haloUv;
    haloAngle = atan2(_e2578.y, _e2580.x);
    let _e2587: f32 = haloRadius;
    let _e2590: f32 = haloRadius;
    let _e2596: f32 = haloRadius;
    let _e2599: f32 = haloRadius;
    haloRing = (1f - smoothstep(0.0015f, 0.004f, abs((_e2599 - 0.275f))));
    let _e2606: f32 = haloRadius;
    let _e2609: f32 = haloRadius;
    let _e2616: f32 = haloRadius;
    let _e2619: f32 = haloRadius;
    haloGlow = exp((-(abs((_e2619 - 0.275f))) * 90f));
    let _e2630: f32 = haloAngle;
    let _e2633: f32 = global.time_value;
    let _e2637: f32 = haloAngle;
    let _e2640: f32 = global.time_value;
    let _e2647: f32 = haloAngle;
    let _e2650: f32 = global.time_value;
    let _e2654: f32 = haloAngle;
    let _e2657: f32 = global.time_value;
    haloSegments = smoothstep(0.1f, 0.5f, sin(((_e2654 * 12f) + (_e2657 * 0.08f))));
    let _e2664: vec3<f32> = col;
    let _e2668: vec3<f32> = global.activity_accent;
    let _e2669: vec3<f32> = global.hunter_halo;
    let _e2673: f32 = haloRing;
    let _e2674: f32 = haloSegments;
    let _e2678: f32 = haloGlow;
    let _e2683: f32 = global.energy;
    col = (_e2664 + ((mix(_e2668, _e2669, vec3(0.76f)) * (((_e2673 * _e2674) * 0.3f) + (_e2678 * 0.075f))) * _e2683));
    let _e2686: vec2<f32> = uv_8;
    let _e2687: f32 = global.time_value;
    let _e2690: f32 = global.time_value;
    let _e2696: f32 = global.time_value;
    dustUv = (_e2686 + vec2<f32>((sin((_e2690 * 0.09f)) * 0.008f), (_e2696 * 0.006f)));
    let _e2702: vec2<f32> = dustUv;
    let _e2707: vec2<f32> = dustUv;
    cells = floor((_e2707 * vec2<f32>(64f, 36f)));
    let _e2714: vec2<f32> = dustUv;
    let _e2719: vec2<f32> = dustUv;
    cellUv = (fract((_e2719 * vec2<f32>(64f, 36f))) - vec2(0.5f));
    let _e2730: vec2<f32> = cells;
    let _e2731: f32 = hash21_(_e2730);
    seed = _e2731;
    let _e2734: f32 = global.particle_amount;
    let _e2740: f32 = global.particle_amount;
    let _e2744: f32 = seed;
    mote = step((0.955f - (_e2740 * 0.018f)), _e2744);
    let _e2751: vec2<f32> = cellUv;
    let _e2756: vec2<f32> = cellUv;
    sparkle = (1f - smoothstep(0.015f, 0.09f, length(_e2756)));
    let _e2761: f32 = sparkle;
    let _e2766: vec2<f32> = cellUv;
    let _e2771: vec2<f32> = cellUv;
    sparkle = (_e2761 + ((1f - smoothstep(0.03f, 0.28f, length(_e2771))) * 0.16f));
    let _e2780: f32 = global.time_value;
    if (_e2780 > 0f) {
        let _e2783: f32 = twinkle;
        let _e2785: f32 = global.time_value;
        let _e2787: f32 = seed;
        let _e2790: f32 = seed;
        let _e2794: f32 = global.time_value;
        let _e2796: f32 = seed;
        let _e2799: f32 = seed;
        twinkle = (_e2783 + (0.25f * sin(((_e2794 * (0.7f + _e2796)) + (_e2799 * 18f)))));
    }
    let _e2806: vec3<f32> = col;
    let _e2810: vec3<f32> = global.activity_secondary;
    let _e2811: vec3<f32> = global.hunter_rim;
    let _e2815: f32 = mote;
    let _e2817: f32 = sparkle;
    let _e2819: f32 = twinkle;
    let _e2821: f32 = global.particle_amount;
    col = (_e2806 + (((((mix(_e2810, _e2811, vec3(0.45f)) * _e2815) * _e2817) * _e2819) * _e2821) * 0.6f));
    let _e2827: vec3<f32> = col;
    let _e2833: f32 = global.warmth;
    let _e2836: vec3<f32> = col;
    let _e2837: vec3<f32> = col;
    let _e2843: f32 = global.warmth;
    col = mix(_e2836, (_e2837 * vec3<f32>(1.1f, 0.97f, 0.84f)), vec3((_e2843 * 0.3f)));
    let _e2851: vec2<f32> = uv_8;
    let _e2855: vec2<f32> = uv_8;
    leftMask = (1f - smoothstep(0.02f, 0.37f, _e2855.x));
    let _e2862: vec2<f32> = uv_8;
    let _e2866: vec2<f32> = uv_8;
    rightMask = smoothstep(0.78f, 1f, _e2866.x);
    let _e2870: vec3<f32> = col;
    let _e2872: f32 = leftMask;
    let _e2873: f32 = global.left_darken;
    col = (_e2870 * (1f - (_e2872 * _e2873)));
    let _e2877: vec3<f32> = col;
    let _e2879: f32 = rightMask;
    let _e2880: f32 = global.right_darken;
    col = (_e2877 * (1f - (_e2879 * _e2880)));
    let _e2886: vec2<f32> = uv_8;
    let _e2892: vec2<f32> = uv_8;
    let _e2899: vec2<f32> = uv_8;
    let _e2905: vec2<f32> = uv_8;
    let _e2915: vec2<f32> = uv_8;
    let _e2921: vec2<f32> = uv_8;
    let _e2928: vec2<f32> = uv_8;
    let _e2934: vec2<f32> = uv_8;
    vignette = smoothstep(0.56f, 1.03f, length(vec2<f32>(((_e2928.x - 0.52f) * 1.08f), ((_e2934.y - 0.48f) * 0.92f))));
    let _e2944: vec3<f32> = col;
    let _e2946: f32 = vignette;
    let _e2948: f32 = global.background_softness;
    col = (_e2944 * (1f - (_e2946 * (0.2f + (_e2948 * 0.18f)))));
    let _e2956: vec4<f32> = gl_FragCoord;
    let _e2958: f32 = global.time_value;
    let _e2964: vec4<f32> = gl_FragCoord;
    let _e2966: f32 = global.time_value;
    let _e2972: f32 = hash21_((_e2964.xy + vec2<f32>((_e2966 * 3f), 0f)));
    grain = (_e2972 - 0.5f);
    let _e2976: vec3<f32> = col;
    let _e2977: f32 = grain;
    col = (_e2976 + vec3((_e2977 * 0.006f)));
    let _e2985: vec3<f32> = col;
    let _e2990: vec3<f32> = clamp(_e2985, vec3(0f), vec3(1f));
    prime_output = vec4<f32>(_e2990.x, _e2990.y, _e2990.z, 1f);
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
