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
    let _e806: f32 = global.time_value;
    if (_e806 > 0f) {
        let _e809: f32 = global.time_value;
        let _e813: f32 = global.time_value;
        let _e815: f32 = (_e813 * 0.045f);
        local = ((_e815 - (floor((_e815 / 1.16f)) * 1.16f)) - 0.08f);
    } else {
        local = 0.53f;
    }
    let _e825: f32 = local;
    sweepCenter = _e825;
    let _e830: vec2<f32> = uv_8;
    let _e832: f32 = sweepCenter;
    let _e834: vec2<f32> = uv_8;
    let _e836: f32 = sweepCenter;
    let _e841: vec2<f32> = uv_8;
    let _e843: f32 = sweepCenter;
    let _e845: vec2<f32> = uv_8;
    let _e847: f32 = sweepCenter;
    wallSweep = (1f - smoothstep(0.015f, 0.095f, abs((_e845.x - _e847))));
    let _e861: vec2<f32> = uv_8;
    let _e869: f32 = box_mask(_e861, vec2<f32>(0.29f, 0.27f), vec2<f32>(0.91f, 0.7f), 0.035f);
    wallSweepGate = _e869;
    let _e871: vec3<f32> = col;
    let _e876: f32 = wallSweep;
    let _e878: f32 = wallSweepGate;
    let _e882: f32 = global.energy;
    let _e884: f32 = global.structure_amount;
    col = (_e871 + (((((vec3<f32>(0.17f, 0.25f, 0.32f) * _e876) * _e878) * 0.24f) * _e882) * _e884));
    let _e895: vec2<f32> = uv_8;
    let _e903: f32 = box_mask(_e895, vec2<f32>(0.31f, 0.275f), vec2<f32>(0.855f, 0.305f), 0.008f);
    signalGate = _e903;
    let _e905: vec2<f32> = uv_8;
    let _e909: f32 = global.time_value;
    let _e911: f32 = global.pulse_speed;
    let _e917: vec2<f32> = uv_8;
    let _e921: f32 = global.time_value;
    let _e923: f32 = global.pulse_speed;
    signalPhase = sin(((_e917.x * 36f) - (_e921 * (0.45f + (_e923 * 0.65f)))));
    let _e933: f32 = signalPhase;
    activeSignal = (0.5f + (0.5f * _e933));
    let _e937: vec3<f32> = col;
    let _e941: vec3<f32> = global.activity_secondary;
    let _e942: vec3<f32> = global.hunter_rim;
    let _e946: f32 = signalGate;
    let _e948: f32 = activeSignal;
    let _e952: f32 = global.energy;
    col = (_e937 + (((mix(_e941, _e942, vec3(0.44f)) * _e946) * _e948) * (0.045f + (0.04f * _e952))));
    let _e957: f32 = stageHeroX;
    heroCenter = vec2<f32>(_e957, 0.49f);
    let _e966: vec2<f32> = uv_8;
    let _e967: vec2<f32> = heroCenter;
    let _e971: f32 = ellipse_mask(_e966, _e967, vec2<f32>(0.205f, 0.39f));
    hero = _e971;
    let _e975: f32 = global.time_value;
    if (_e975 > 0f) {
        let _e978: f32 = pulse;
        let _e979: f32 = global.time_value;
        let _e981: f32 = global.pulse_speed;
        let _e986: f32 = global.time_value;
        let _e988: f32 = global.pulse_speed;
        pulse = (_e978 + (sin((_e986 * (0.55f + (_e988 * 0.45f)))) * 0.035f));
    }
    let _e997: vec3<f32> = col;
    let _e998: vec3<f32> = global.hunter_halo;
    let _e999: f32 = hero;
    let _e1001: f32 = global.halo_strength;
    let _e1003: f32 = global.hero_light;
    let _e1007: f32 = global.energy;
    let _e1011: f32 = pulse;
    col = (_e997 + (((((_e998 * _e999) * _e1001) * _e1003) * (0.215f + (0.09f * _e1007))) * _e1011));
    let _e1015: f32 = stageHeroX;
    let _e1021: vec2<f32> = uv_8;
    let _e1022: f32 = stageHeroX;
    let _e1028: f32 = ellipse_mask(_e1021, vec2<f32>(_e1022, 0.43f), vec2<f32>(0.105f, 0.32f));
    rimColumn = _e1028;
    let _e1030: vec3<f32> = col;
    let _e1031: vec3<f32> = global.hunter_rim;
    let _e1032: f32 = rimColumn;
    let _e1036: f32 = global.hero_light;
    col = (_e1030 + (((_e1031 * _e1032) * 0.09f) * _e1036));
    let _e1040: f32 = stageHeroX;
    let _e1046: vec2<f32> = uv_8;
    let _e1047: f32 = stageHeroX;
    let _e1053: f32 = ellipse_mask(_e1046, vec2<f32>(_e1047, 0.39f), vec2<f32>(0.165f, 0.225f));
    heroKey = _e1053;
    let _e1060: vec3<f32> = col;
    let _e1061: vec3<f32> = heroKeyColor;
    let _e1062: f32 = heroKey;
    let _e1066: f32 = global.hero_light;
    col = (_e1060 + ((_e1061 * _e1062) * (0.04f + (0.02f * _e1066))));
    let _e1073: vec2<f32> = uv_8;
    let _e1079: vec2<f32> = uv_8;
    let _e1086: vec2<f32> = uv_8;
    let _e1092: vec2<f32> = uv_8;
    let _e1103: vec2<f32> = uv_8;
    let _e1109: vec2<f32> = uv_8;
    let _e1116: vec2<f32> = uv_8;
    let _e1122: vec2<f32> = uv_8;
    shaftA = max(0f, (1f - abs((((_e1116.x - 0.58f) * 6.4f) + ((_e1122.y - 0.15f) * 0.55f)))));
    let _e1135: vec2<f32> = uv_8;
    let _e1141: vec2<f32> = uv_8;
    let _e1148: vec2<f32> = uv_8;
    let _e1154: vec2<f32> = uv_8;
    let _e1165: vec2<f32> = uv_8;
    let _e1171: vec2<f32> = uv_8;
    let _e1178: vec2<f32> = uv_8;
    let _e1184: vec2<f32> = uv_8;
    shaftB = max(0f, (1f - abs((((_e1178.x - 0.7f) * 7.1f) - ((_e1184.y - 0.12f) * 0.45f)))));
    let _e1198: vec2<f32> = uv_8;
    let _e1202: vec2<f32> = uv_8;
    let _e1208: vec2<f32> = uv_8;
    let _e1212: vec2<f32> = uv_8;
    shaftGate = ((1f - smoothstep(0.2f, 0.86f, _e1202.y)) * smoothstep(0.04f, 0.18f, _e1212.y));
    let _e1217: vec3<f32> = col;
    let _e1218: vec3<f32> = global.activity_secondary;
    let _e1219: f32 = shaftA;
    let _e1220: f32 = shaftB;
    let _e1223: f32 = shaftGate;
    let _e1227: f32 = global.beam_intensity;
    let _e1229: f32 = global.energy;
    col = (_e1217 + (((((_e1218 * (_e1219 + _e1220)) * _e1223) * 0.028f) * _e1227) * _e1229));
    let _e1234: vec2<f32> = uv_8;
    let _e1238: vec2<f32> = uv_8;
    floorMask = smoothstep(0.58f, 0.78f, _e1238.y);
    let _e1243: vec3<f32> = global.base_bottom;
    let _e1246: f32 = floorMask;
    let _e1249: vec3<f32> = col;
    let _e1250: vec3<f32> = global.base_bottom;
    let _e1253: f32 = floorMask;
    col = mix(_e1249, (_e1250 * 0.72f), vec3((_e1253 * 0.56f)));
    let _e1258: vec2<f32> = uv_8;
    let _e1263: vec2<f32> = uv_8;
    fy = max((_e1263.y - 0.56f), 0.001f);
    let _e1271: f32 = fy;
    perspectiveY = (1f / ((_e1271 * 10f) + 0.55f));
    let _e1278: f32 = perspectiveY;
    let _e1281: f32 = perspectiveY;
    let _e1288: f32 = perspectiveY;
    let _e1291: f32 = perspectiveY;
    let _e1298: f32 = line((fract((_e1291 * 4.2f)) - 0.5f), 0.06f);
    horizontal = _e1298;
    let _e1300: vec2<f32> = uv_8;
    let _e1302: f32 = stageHeroX;
    let _e1304: f32 = fy;
    let _e1308: f32 = fy;
    centeredX = ((_e1300.x - _e1302) / max((_e1308 + 0.2f), 0.2f));
    let _e1315: f32 = centeredX;
    let _e1318: f32 = centeredX;
    let _e1325: f32 = centeredX;
    let _e1328: f32 = centeredX;
    let _e1335: f32 = line((fract((_e1328 * 4.5f)) - 0.5f), 0.035f);
    vertical = _e1335;
    let _e1339: f32 = horizontal;
    let _e1340: f32 = vertical;
    let _e1342: f32 = floorMask;
    grid = (max(_e1339, _e1340) * _e1342);
    let _e1345: vec3<f32> = col;
    let _e1349: vec3<f32> = global.activity_accent;
    let _e1350: vec3<f32> = global.hunter_halo;
    let _e1354: f32 = grid;
    let _e1358: f32 = global.floor_grid;
    col = (_e1345 + (((mix(_e1349, _e1350, vec3(0.26f)) * _e1354) * 0.058f) * _e1358));
    let _e1361: f32 = global.lobby_mode;
    if (_e1361 > 0.5f) {
        {
            let _e1364: vec2<f32> = uv_8;
            let _e1368: vec2<f32> = uv_8;
            let _e1374: vec2<f32> = uv_8;
            let _e1381: vec2<f32> = uv_8;
            let _e1385: vec2<f32> = uv_8;
            let _e1391: vec2<f32> = uv_8;
            let _e1398: f32 = line((abs((_e1385.x - 0.5f)) - ((0.79f - _e1391.y) * 0.42f)), 0.0035f);
            serviceSpine = _e1398;
            let _e1402: vec2<f32> = uv_8;
            let _e1406: vec2<f32> = uv_8;
            let _e1412: vec2<f32> = uv_8;
            let _e1416: vec2<f32> = uv_8;
            serviceGate = (smoothstep(0.29f, 0.43f, _e1406.y) * (1f - smoothstep(0.75f, 0.83f, _e1416.y)));
            let _e1422: vec3<f32> = col;
            let _e1423: vec3<f32> = global.activity_secondary;
            let _e1424: f32 = serviceSpine;
            let _e1426: f32 = serviceGate;
            let _e1430: f32 = global.energy;
            col = (_e1422 + ((((_e1423 * _e1424) * _e1426) * 0.13f) * _e1430));
            loop {
                let _e1435: i32 = slot;
                if !((_e1435 >= 0i)) {
                    break;
                }
                {
                    let _e1442: i32 = slot;
                    let _e1444: vec4<f32> = global.lobby_pad_geometry[_e1442];
                    geometry = _e1444;
                    let _e1446: vec4<f32> = geometry;
                    centre = _e1446.xy;
                    let _e1449: vec4<f32> = geometry;
                    radius_6 = _e1449.zw;
                    let _e1452: i32 = slot;
                    if (_e1452 < 4i) {
                        let _e1455: i32 = slot;
                        let _e1457: f32 = global.lobby_occupancy_a[_e1455];
                        local_1 = _e1457;
                    } else {
                        let _e1458: i32 = slot;
                        let _e1462: f32 = global.lobby_occupancy_b[(_e1458 - 4i)];
                        local_1 = _e1462;
                    }
                    let _e1464: f32 = local_1;
                    occupied = _e1464;
                    let _e1466: vec2<f32> = uv_8;
                    let _e1467: vec2<f32> = centre;
                    let _e1469: vec2<f32> = radius_6;
                    foot = ((_e1466 - _e1467) / _e1469);
                    let _e1474: vec2<f32> = foot;
                    let _e1475: vec2<f32> = foot;
                    q_3 = dot(_e1474, _e1475);
                    let _e1478: vec2<f32> = uv_8;
                    let _e1479: vec2<f32> = centre;
                    let _e1485: vec2<f32> = radius_6;
                    outer = ((_e1478 - (_e1479 + vec2<f32>(0f, 0.014f))) / (_e1485 * 1.12f));
                    let _e1495: vec2<f32> = outer;
                    let _e1496: vec2<f32> = outer;
                    let _e1502: vec2<f32> = outer;
                    let _e1503: vec2<f32> = outer;
                    shadow = (1f - smoothstep(0.52f, 1.22f, dot(_e1502, _e1503)));
                    let _e1508: vec3<f32> = col;
                    let _e1510: f32 = shadow;
                    let _e1512: f32 = occupied;
                    col = (_e1508 * (1f - (_e1510 * (0.1f + (_e1512 * 0.11f)))));
                    let _e1525: f32 = q_3;
                    body = (1f - smoothstep(0.64f, 0.98f, _e1525));
                    let _e1534: f32 = q_3;
                    let _e1542: f32 = q_3;
                    lip = (smoothstep(0.71f, 0.82f, _e1534) * (1f - smoothstep(0.93f, 1.05f, _e1542)));
                    let _e1547: f32 = lip;
                    let _e1551: vec2<f32> = foot;
                    let _e1556: vec2<f32> = foot;
                    raisedEdge = (_e1547 * smoothstep(-0.25f, 0.7f, _e1556.y));
                    let _e1567: f32 = q_3;
                    innerDish = (1f - smoothstep(0.2f, 0.72f, _e1567));
                    let _e1580: vec2<f32> = foot;
                    let _e1588: vec2<f32> = foot;
                    let _e1605: vec2<f32> = foot;
                    let _e1613: vec2<f32> = foot;
                    metal = mix(vec3<f32>(0.017f, 0.033f, 0.047f), vec3<f32>(0.043f, 0.075f, 0.095f), vec3(clamp(((1f - _e1613.y) * 0.34f), 0f, 1f)));
                    let _e1626: f32 = body;
                    let _e1629: vec3<f32> = col;
                    let _e1630: vec3<f32> = metal;
                    let _e1631: f32 = body;
                    col = mix(_e1629, _e1630, vec3((_e1631 * 0.85f)));
                    let _e1636: vec3<f32> = col;
                    let _e1641: f32 = raisedEdge;
                    col = (_e1636 + ((vec3<f32>(0.075f, 0.118f, 0.15f) * _e1641) * 0.18f));
                    let _e1649: vec3<f32> = global.activity_secondary;
                    let _e1650: vec3<f32> = global.hunter_halo;
                    activeLight = mix(_e1649, _e1650, vec3(0.38f));
                    let _e1655: vec3<f32> = col;
                    let _e1656: vec3<f32> = activeLight;
                    let _e1657: f32 = lip;
                    let _e1660: f32 = occupied;
                    col = (_e1655 + ((_e1656 * _e1657) * (0.034f + (_e1660 * 0.2f))));
                    let _e1666: vec3<f32> = col;
                    let _e1671: f32 = innerDish;
                    let _e1674: f32 = occupied;
                    col = (_e1666 + ((vec3<f32>(0.075f, 0.135f, 0.17f) * _e1671) * (0.014f + (_e1674 * 0.017f))));
                    let _e1683: f32 = q_3;
                    let _e1686: f32 = q_3;
                    let _e1692: f32 = q_3;
                    let _e1695: f32 = q_3;
                    ring1_ = (1f - smoothstep(0.026f, 0.052f, abs((_e1695 - 0.22f))));
                    let _e1705: f32 = q_3;
                    let _e1708: f32 = q_3;
                    let _e1714: f32 = q_3;
                    let _e1717: f32 = q_3;
                    ring2_ = (1f - smoothstep(0.025f, 0.05f, abs((_e1717 - 0.43f))));
                    let _e1724: vec2<f32> = foot;
                    let _e1726: vec2<f32> = foot;
                    let _e1728: vec2<f32> = foot;
                    let _e1730: vec2<f32> = foot;
                    angle = atan2(_e1728.y, _e1730.x);
                    let _e1734: f32 = angle;
                    let _e1741: f32 = angle;
                    phase = fract((((_e1741 + 3.1415927f) / 6.2831855f) * 12f));
                    let _e1755: f32 = phase;
                    let _e1763: f32 = phase;
                    segments = (smoothstep(0.06f, 0.15f, _e1755) * (1f - smoothstep(0.73f, 0.86f, _e1763)));
                    let _e1768: vec3<f32> = col;
                    let _e1769: vec3<f32> = activeLight;
                    let _e1772: f32 = ring1_;
                    let _e1773: f32 = ring2_;
                    let _e1776: f32 = segments;
                    let _e1779: f32 = occupied;
                    col = (_e1768 + (((_e1769 * max(_e1772, _e1773)) * _e1776) * (0.034f + (_e1779 * 0.084f))));
                    let _e1785: vec2<f32> = centre;
                    let _e1786: vec2<f32> = radius_6;
                    let _e1791: vec2<f32> = radius_6;
                    leftBoot = (_e1785 + vec2<f32>((-(_e1786.x) * 0.3f), (-(_e1791.y) * 0.05f)));
                    let _e1799: vec2<f32> = centre;
                    let _e1800: vec2<f32> = radius_6;
                    let _e1804: vec2<f32> = radius_6;
                    rightBoot = (_e1799 + vec2<f32>((_e1800.x * 0.3f), (-(_e1804.y) * 0.05f)));
                    let _e1814: vec2<f32> = radius_6;
                    let _e1818: vec2<f32> = radius_6;
                    let _e1823: vec2<f32> = uv_8;
                    let _e1824: vec2<f32> = leftBoot;
                    let _e1825: vec2<f32> = radius_6;
                    let _e1829: vec2<f32> = radius_6;
                    let _e1834: f32 = ellipse_mask(_e1823, _e1824, vec2<f32>((_e1825.x * 0.23f), (_e1829.y * 0.44f)));
                    let _e1837: vec2<f32> = radius_6;
                    let _e1841: vec2<f32> = radius_6;
                    let _e1846: vec2<f32> = uv_8;
                    let _e1847: vec2<f32> = rightBoot;
                    let _e1848: vec2<f32> = radius_6;
                    let _e1852: vec2<f32> = radius_6;
                    let _e1857: f32 = ellipse_mask(_e1846, _e1847, vec2<f32>((_e1848.x * 0.23f), (_e1852.y * 0.44f)));
                    boots = (_e1834 + _e1857);
                    let _e1860: vec3<f32> = col;
                    let _e1865: f32 = boots;
                    let _e1867: f32 = occupied;
                    col = (_e1860 * (1f - ((min(1f, _e1865) * _e1867) * 0.13f)));
                }
                continuing {
                    let _e1439: i32 = slot;
                    slot = (_e1439 - 1i);
                }
            }
        }
    } else {
        {
            let _e1879: vec2<f32> = uv_8;
            let _e1880: vec4<f32> = soloPad;
            let _e1883: vec4<f32> = soloPad;
            platformDelta = ((_e1879 - _e1880.xy) / _e1883.zw);
            let _e1889: vec2<f32> = platformDelta;
            let _e1890: vec2<f32> = platformDelta;
            platformQ = dot(_e1889, _e1890);
            let _e1899: f32 = platformQ;
            platformFill = (1f - smoothstep(0.62f, 1f, _e1899));
            let _e1908: f32 = platformQ;
            let _e1916: f32 = platformQ;
            platformEdge = (smoothstep(0.62f, 0.77f, _e1908) * (1f - smoothstep(0.88f, 1f, _e1916)));
            let _e1926: f32 = platformFill;
            let _e1929: vec3<f32> = col;
            let _e1934: f32 = platformFill;
            col = mix(_e1929, vec3<f32>(0.006f, 0.012f, 0.022f), vec3((_e1934 * 0.7f)));
            let _e1945: f32 = platformQ;
            platformCore = (1f - smoothstep(0.08f, 0.7f, _e1945));
            let _e1949: vec3<f32> = col;
            let _e1953: vec3<f32> = global.activity_accent;
            let _e1954: vec3<f32> = global.hunter_halo;
            let _e1958: f32 = platformCore;
            let _e1960: f32 = global.floor_glow;
            col = (_e1949 + (((mix(_e1953, _e1954, vec3(0.3f)) * _e1958) * _e1960) * 0.092f));
            let _e1965: vec3<f32> = col;
            let _e1966: vec3<f32> = global.hunter_halo;
            let _e1967: f32 = platformEdge;
            let _e1969: f32 = global.floor_glow;
            col = (_e1965 + (((_e1966 * _e1967) * _e1969) * 0.3f));
            let _e1974: vec3<f32> = col;
            let _e1975: vec3<f32> = global.activity_secondary;
            let _e1976: f32 = platformEdge;
            let _e1978: f32 = global.floor_glow;
            col = (_e1974 + (((_e1975 * _e1976) * _e1978) * 0.075f));
            let _e1986: f32 = platformQ;
            let _e1989: f32 = platformQ;
            let _e1995: f32 = platformQ;
            let _e1998: f32 = platformQ;
            ringA = (1f - smoothstep(0.018f, 0.05f, abs((_e1998 - 0.2f))));
            let _e2008: f32 = platformQ;
            let _e2011: f32 = platformQ;
            let _e2017: f32 = platformQ;
            let _e2020: f32 = platformQ;
            ringB = (1f - smoothstep(0.018f, 0.05f, abs((_e2020 - 0.4f))));
            let _e2030: f32 = platformQ;
            let _e2033: f32 = platformQ;
            let _e2039: f32 = platformQ;
            let _e2042: f32 = platformQ;
            ringC = (1f - smoothstep(0.018f, 0.05f, abs((_e2042 - 0.6f))));
            let _e2049: vec2<f32> = platformDelta;
            let _e2051: vec2<f32> = platformDelta;
            let _e2053: vec2<f32> = platformDelta;
            let _e2055: vec2<f32> = platformDelta;
            ringAngle = atan2(_e2053.y, _e2055.x);
            let _e2059: f32 = ringAngle;
            let _e2066: f32 = ringAngle;
            ringPhase = fract((((_e2066 + 3.1415927f) / 6.2831855f) * 12f));
            let _e2080: f32 = ringPhase;
            let _e2088: f32 = ringPhase;
            ringSegments = (smoothstep(0.08f, 0.18f, _e2080) * (1f - smoothstep(0.72f, 0.82f, _e2088)));
            let _e2096: f32 = ringB;
            let _e2097: f32 = ringC;
            let _e2099: f32 = ringA;
            let _e2102: f32 = ringB;
            let _e2103: f32 = ringC;
            let _e2106: f32 = ringSegments;
            serviceRings = (max(_e2099, max(_e2102, _e2103)) * _e2106);
            let _e2109: vec3<f32> = col;
            let _e2113: vec3<f32> = global.activity_secondary;
            let _e2114: vec3<f32> = global.hunter_halo;
            let _e2118: f32 = serviceRings;
            let _e2120: f32 = global.floor_glow;
            col = (_e2109 + (((mix(_e2113, _e2114, vec3(0.22f)) * _e2118) * _e2120) * 0.11f));
        }
    }
    let _e2127: vec2<f32> = uv_8;
    let _e2131: vec2<f32> = uv_8;
    let _e2137: vec2<f32> = uv_8;
    let _e2141: vec2<f32> = uv_8;
    lowFog = (smoothstep(0.52f, 0.92f, _e2131.y) * (1f - smoothstep(0.88f, 1f, _e2141.y)));
    let _e2149: f32 = global.time_value;
    if (_e2149 > 0f) {
        let _e2152: f32 = fogWave;
        let _e2153: vec2<f32> = uv_8;
        let _e2157: f32 = global.time_value;
        let _e2161: vec2<f32> = uv_8;
        let _e2165: f32 = global.time_value;
        fogWave = (_e2152 + (sin(((_e2161.x * 8f) + (_e2165 * 0.18f))) * 0.1f));
    }
    let _e2179: vec3<f32> = global.activity_accent;
    fogColor = mix(_e2179, vec3<f32>(0.16f, 0.24f, 0.31f), vec3(0.58f));
    let _e2190: f32 = lowFog;
    let _e2191: f32 = global.fog_amount;
    let _e2195: f32 = fogWave;
    let _e2197: vec3<f32> = col;
    let _e2198: vec3<f32> = fogColor;
    let _e2199: f32 = lowFog;
    let _e2200: f32 = global.fog_amount;
    let _e2204: f32 = fogWave;
    col = mix(_e2197, _e2198, vec3((((_e2199 * _e2200) * 0.16f) * _e2204)));
    let _e2209: f32 = stageHeroX;
    let _e2215: vec2<f32> = uv_8;
    let _e2216: f32 = stageHeroX;
    let _e2222: f32 = ellipse_mask(_e2215, vec2<f32>(_e2216, 0.685f), vec2<f32>(0.315f, 0.135f));
    rearLegFog = _e2222;
    let _e2230: vec3<f32> = global.activity_accent;
    rearFogColor = mix(_e2230, vec3<f32>(0.22f, 0.28f, 0.33f), vec3(0.72f));
    let _e2241: f32 = rearLegFog;
    let _e2242: f32 = global.fog_amount;
    let _e2246: f32 = global.energy;
    let _e2250: vec3<f32> = col;
    let _e2251: vec3<f32> = rearFogColor;
    let _e2252: f32 = rearLegFog;
    let _e2253: f32 = global.fog_amount;
    let _e2257: f32 = global.energy;
    col = mix(_e2250, _e2251, vec3(((_e2252 * _e2253) * (0.045f + (0.02f * _e2257)))));
    let _e2263: f32 = global.time_value;
    let _e2266: f32 = global.time_value;
    drift = (sin((_e2266 * 0.23f)) * 0.04f);
    let _e2273: vec2<f32> = uv_8;
    let _e2277: f32 = drift;
    let _e2279: vec2<f32> = uv_8;
    let _e2287: vec2<f32> = uv_8;
    let _e2291: f32 = drift;
    let _e2293: vec2<f32> = uv_8;
    let _e2303: vec2<f32> = uv_8;
    let _e2307: f32 = drift;
    let _e2309: vec2<f32> = uv_8;
    let _e2317: vec2<f32> = uv_8;
    let _e2321: f32 = drift;
    let _e2323: vec2<f32> = uv_8;
    let _e2334: vec2<f32> = uv_8;
    let _e2338: f32 = drift;
    let _e2340: vec2<f32> = uv_8;
    let _e2348: vec2<f32> = uv_8;
    let _e2352: f32 = drift;
    let _e2354: vec2<f32> = uv_8;
    let _e2364: vec2<f32> = uv_8;
    let _e2368: f32 = drift;
    let _e2370: vec2<f32> = uv_8;
    let _e2378: vec2<f32> = uv_8;
    let _e2382: f32 = drift;
    let _e2384: vec2<f32> = uv_8;
    shafts = (exp(-(pow(((((_e2317.x - 0.36f) - _e2321) + (_e2323.y * 0.07f)) * 29f), 2f))) + exp(-(pow(((((_e2378.x - 0.66f) + _e2382) - (_e2384.y * 0.05f)) * 34f), 2f))));
    let _e2399: vec2<f32> = uv_8;
    let _e2403: vec2<f32> = uv_8;
    let _e2409: vec2<f32> = uv_8;
    let _e2413: vec2<f32> = uv_8;
    reactorShaftGate = (smoothstep(0.18f, 0.3f, _e2403.y) * (1f - smoothstep(0.64f, 0.84f, _e2413.y)));
    let _e2419: vec3<f32> = col;
    let _e2423: vec3<f32> = global.activity_secondary;
    let _e2424: vec3<f32> = global.hunter_rim;
    let _e2428: f32 = shafts;
    let _e2430: f32 = reactorShaftGate;
    let _e2434: f32 = global.energy;
    col = (_e2419 + ((((mix(_e2423, _e2424, vec3(0.38f)) * _e2428) * _e2430) * 0.34f) * _e2434));
    let _e2437: vec2<f32> = uv_8;
    let _e2438: f32 = global.time_value;
    let _e2441: f32 = global.time_value;
    let _e2447: f32 = global.time_value;
    dustUv = (_e2437 + vec2<f32>((sin((_e2441 * 0.09f)) * 0.008f), (_e2447 * 0.014f)));
    let _e2453: vec2<f32> = dustUv;
    let _e2458: vec2<f32> = dustUv;
    cells = floor((_e2458 * vec2<f32>(64f, 36f)));
    let _e2465: vec2<f32> = dustUv;
    let _e2470: vec2<f32> = dustUv;
    cellUv = (fract((_e2470 * vec2<f32>(64f, 36f))) - vec2(0.5f));
    let _e2481: vec2<f32> = cells;
    let _e2482: f32 = hash21_(_e2481);
    seed = _e2482;
    let _e2485: f32 = global.particle_amount;
    let _e2491: f32 = global.particle_amount;
    let _e2495: f32 = seed;
    mote = step((0.955f - (_e2491 * 0.018f)), _e2495);
    let _e2502: vec2<f32> = cellUv;
    let _e2507: vec2<f32> = cellUv;
    sparkle = (1f - smoothstep(0.015f, 0.09f, length(_e2507)));
    let _e2512: f32 = sparkle;
    let _e2517: vec2<f32> = cellUv;
    let _e2522: vec2<f32> = cellUv;
    sparkle = (_e2512 + ((1f - smoothstep(0.03f, 0.28f, length(_e2522))) * 0.16f));
    let _e2531: f32 = global.time_value;
    if (_e2531 > 0f) {
        let _e2534: f32 = twinkle;
        let _e2536: f32 = global.time_value;
        let _e2538: f32 = seed;
        let _e2541: f32 = seed;
        let _e2545: f32 = global.time_value;
        let _e2547: f32 = seed;
        let _e2550: f32 = seed;
        twinkle = (_e2534 + (0.25f * sin(((_e2545 * (0.7f + _e2547)) + (_e2550 * 18f)))));
    }
    let _e2557: vec3<f32> = col;
    let _e2561: vec3<f32> = global.activity_secondary;
    let _e2562: vec3<f32> = global.hunter_rim;
    let _e2566: f32 = mote;
    let _e2568: f32 = sparkle;
    let _e2570: f32 = twinkle;
    let _e2572: f32 = global.particle_amount;
    col = (_e2557 + (((((mix(_e2561, _e2562, vec3(0.45f)) * _e2566) * _e2568) * _e2570) * _e2572) * 1.15f));
    let _e2578: vec3<f32> = col;
    let _e2584: f32 = global.warmth;
    let _e2587: vec3<f32> = col;
    let _e2588: vec3<f32> = col;
    let _e2594: f32 = global.warmth;
    col = mix(_e2587, (_e2588 * vec3<f32>(1.1f, 0.97f, 0.84f)), vec3((_e2594 * 0.3f)));
    let _e2602: vec2<f32> = uv_8;
    let _e2606: vec2<f32> = uv_8;
    leftMask = (1f - smoothstep(0.02f, 0.37f, _e2606.x));
    let _e2613: vec2<f32> = uv_8;
    let _e2617: vec2<f32> = uv_8;
    rightMask = smoothstep(0.78f, 1f, _e2617.x);
    let _e2621: vec3<f32> = col;
    let _e2623: f32 = leftMask;
    let _e2624: f32 = global.left_darken;
    col = (_e2621 * (1f - (_e2623 * _e2624)));
    let _e2628: vec3<f32> = col;
    let _e2630: f32 = rightMask;
    let _e2631: f32 = global.right_darken;
    col = (_e2628 * (1f - (_e2630 * _e2631)));
    let _e2637: vec2<f32> = uv_8;
    let _e2643: vec2<f32> = uv_8;
    let _e2650: vec2<f32> = uv_8;
    let _e2656: vec2<f32> = uv_8;
    let _e2666: vec2<f32> = uv_8;
    let _e2672: vec2<f32> = uv_8;
    let _e2679: vec2<f32> = uv_8;
    let _e2685: vec2<f32> = uv_8;
    vignette = smoothstep(0.56f, 1.03f, length(vec2<f32>(((_e2679.x - 0.52f) * 1.08f), ((_e2685.y - 0.48f) * 0.92f))));
    let _e2695: vec3<f32> = col;
    let _e2697: f32 = vignette;
    let _e2699: f32 = global.background_softness;
    col = (_e2695 * (1f - (_e2697 * (0.2f + (_e2699 * 0.18f)))));
    let _e2707: vec4<f32> = gl_FragCoord;
    let _e2709: f32 = global.time_value;
    let _e2715: vec4<f32> = gl_FragCoord;
    let _e2717: f32 = global.time_value;
    let _e2723: f32 = hash21_((_e2715.xy + vec2<f32>((_e2717 * 3f), 0f)));
    grain = (_e2723 - 0.5f);
    let _e2727: vec3<f32> = col;
    let _e2728: f32 = grain;
    col = (_e2727 + vec3((_e2728 * 0.006f)));
    let _e2736: vec3<f32> = col;
    let _e2741: vec3<f32> = clamp(_e2736, vec3(0f), vec3(1f));
    prime_output = vec4<f32>(_e2741.x, _e2741.y, _e2741.z, 1f);
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
