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
    let _e292: vec3<f32> = global.activity_accent;
    let _e293: f32 = horizon;
    let _e297: f32 = global.energy;
    col = (_e291 + (((_e292 * _e293) * 0.035f) * _e297));
    let _e308: vec2<f32> = uv_8;
    let _e316: f32 = box_mask(_e308, vec2<f32>(0.305f, 0.14f), vec2<f32>(0.855f, 0.72f), 0.035f);
    bay = _e316;
    let _e318: vec3<f32> = col;
    let _e322: vec3<f32> = global.activity_accent;
    let _e323: vec3<f32> = global.hunter_halo;
    let _e327: f32 = bay;
    let _e331: f32 = global.structure_amount;
    col = (_e318 + (((mix(_e322, _e323, vec3(0.28f)) * _e327) * 0.04f) * _e331));
    let _e335: f32 = stageHeroX;
    let _e341: vec2<f32> = uv_8;
    let _e342: f32 = stageHeroX;
    let _e348: f32 = ellipse_mask(_e341, vec2<f32>(_e342, 0.49f), vec2<f32>(0.255f, 0.335f));
    torsoLift = _e348;
    let _e356: vec3<f32> = global.base_mid;
    heroAmbient = mix(_e356, vec3<f32>(0.105f, 0.145f, 0.185f), vec3(0.55f));
    let _e365: vec3<f32> = col;
    let _e366: vec3<f32> = heroAmbient;
    let _e367: f32 = torsoLift;
    let _e371: f32 = global.hero_light;
    col = (_e365 + ((_e366 * _e367) * (0.15f + (0.05f * _e371))));
    let _e384: vec2<f32> = uv_8;
    let _e392: f32 = box_mask(_e384, vec2<f32>(0.355f, 0.3f), vec2<f32>(0.455f, 0.69f), 0.014f);
    leftRecess = _e392;
    let _e402: vec2<f32> = uv_8;
    let _e410: f32 = box_mask(_e402, vec2<f32>(0.775f, 0.3f), vec2<f32>(0.855f, 0.69f), 0.014f);
    rightRecess = _e410;
    let _e413: f32 = leftRecess;
    let _e414: f32 = rightRecess;
    let _e417: f32 = leftRecess;
    let _e418: f32 = rightRecess;
    recess = min(1f, (_e417 + _e418));
    let _e422: vec3<f32> = col;
    let _e424: f32 = recess;
    let _e427: f32 = global.structure_amount;
    col = (_e422 * (1f - ((_e424 * 0.1f) * _e427)));
    let _e431: vec2<f32> = uv_8;
    let _e436: vec2<f32> = uv_8;
    let _e441: f32 = line((_e436.x - 0.355f), 0.0022f);
    let _e442: vec2<f32> = uv_8;
    let _e447: vec2<f32> = uv_8;
    let _e452: f32 = line((_e447.x - 0.455f), 0.0022f);
    let _e454: vec2<f32> = uv_8;
    let _e459: vec2<f32> = uv_8;
    let _e464: f32 = line((_e459.x - 0.775f), 0.0022f);
    let _e466: vec2<f32> = uv_8;
    let _e471: vec2<f32> = uv_8;
    let _e476: f32 = line((_e471.x - 0.855f), 0.0022f);
    innerEdges = (((_e441 + _e452) + _e464) + _e476);
    let _e481: vec2<f32> = uv_8;
    let _e485: vec2<f32> = uv_8;
    let _e491: vec2<f32> = uv_8;
    let _e495: vec2<f32> = uv_8;
    innerGate = (smoothstep(0.27f, 0.34f, _e485.y) * (1f - smoothstep(0.68f, 0.74f, _e495.y)));
    let _e501: vec3<f32> = col;
    let _e502: vec3<f32> = global.activity_secondary;
    let _e503: f32 = innerEdges;
    let _e505: f32 = innerGate;
    let _e509: f32 = global.structure_amount;
    col = (_e501 + ((((_e502 * _e503) * _e505) * 0.018f) * _e509));
    let _e520: vec2<f32> = uv_8;
    let _e528: f32 = box_mask(_e520, vec2<f32>(0.245f, 0.23f), vec2<f32>(0.325f, 0.7f), 0.02f);
    farLeftWall = _e528;
    let _e538: vec2<f32> = uv_8;
    let _e546: f32 = box_mask(_e538, vec2<f32>(0.87f, 0.18f), vec2<f32>(0.945f, 0.66f), 0.02f);
    farRightWall = _e546;
    let _e549: f32 = farLeftWall;
    let _e550: f32 = farRightWall;
    let _e553: f32 = farLeftWall;
    let _e554: f32 = farRightWall;
    farWall = min(1f, (_e553 + _e554));
    let _e559: vec3<f32> = global.base_bottom;
    let _e562: f32 = farWall;
    let _e565: f32 = global.structure_amount;
    let _e567: vec3<f32> = col;
    let _e568: vec3<f32> = global.base_bottom;
    let _e571: f32 = farWall;
    let _e574: f32 = global.structure_amount;
    col = mix(_e567, (_e568 * 0.58f), vec3(((_e571 * 0.52f) * _e574)));
    let _e586: vec2<f32> = uv_8;
    let _e594: f32 = box_mask(_e586, vec2<f32>(0.255f, 0.285f), vec2<f32>(0.93f, 0.302f), 0.008f);
    farCrossbeam = _e594;
    let _e597: vec3<f32> = global.base_bottom;
    let _e600: f32 = farCrossbeam;
    let _e603: f32 = global.structure_amount;
    let _e605: vec3<f32> = col;
    let _e606: vec3<f32> = global.base_bottom;
    let _e609: f32 = farCrossbeam;
    let _e612: f32 = global.structure_amount;
    col = mix(_e605, (_e606 * 0.44f), vec3(((_e609 * 0.7f) * _e612)));
    let _e616: vec3<f32> = col;
    let _e617: vec3<f32> = global.activity_accent;
    let _e618: f32 = farCrossbeam;
    let _e622: f32 = global.structure_amount;
    col = (_e616 + (((_e617 * _e618) * 0.02f) * _e622));
    loop {
        let _e627: i32 = i;
        if !((_e627 < 7i)) {
            break;
        }
        {
            let _e634: i32 = i;
            fi = f32(_e634);
            let _e638: f32 = fi;
            x = (0.16f + (_e638 * 0.135f));
            let _e643: vec2<f32> = uv_8;
            let _e645: f32 = x;
            let _e649: vec2<f32> = uv_8;
            let _e653: vec2<f32> = uv_8;
            let _e655: f32 = x;
            let _e659: vec2<f32> = uv_8;
            let _e663: f32 = line((_e653.x - _e655), (0.0018f + (0.001f * _e659.y)));
            rib = _e663;
            let _e667: vec2<f32> = uv_8;
            let _e671: vec2<f32> = uv_8;
            let _e677: vec2<f32> = uv_8;
            let _e681: vec2<f32> = uv_8;
            gate = (smoothstep(0.1f, 0.2f, _e671.y) * (1f - smoothstep(0.76f, 0.9f, _e681.y)));
            let _e687: vec3<f32> = col;
            let _e688: vec3<f32> = global.activity_accent;
            let _e689: f32 = rib;
            let _e691: f32 = gate;
            let _e695: f32 = global.structure_amount;
            col = (_e687 + ((((_e688 * _e689) * _e691) * 0.045f) * _e695));
        }
        continuing {
            let _e631: i32 = i;
            i = (_e631 + 1i);
        }
    }
    let _e698: vec2<f32> = uv_8;
    let _e703: vec2<f32> = uv_8;
    let _e710: vec2<f32> = uv_8;
    let _e715: vec2<f32> = uv_8;
    let _e722: f32 = line(((_e710.x - 0.17f) - ((0.64f - _e715.y) * 0.26f)), 0.006f);
    leftBrace = _e722;
    let _e725: vec2<f32> = uv_8;
    let _e729: vec2<f32> = uv_8;
    let _e737: vec2<f32> = uv_8;
    let _e741: vec2<f32> = uv_8;
    let _e748: f32 = line(((0.91f - _e737.x) - ((0.64f - _e741.y) * 0.22f)), 0.006f);
    rightBrace = _e748;
    let _e752: vec2<f32> = uv_8;
    let _e756: vec2<f32> = uv_8;
    let _e762: vec2<f32> = uv_8;
    let _e766: vec2<f32> = uv_8;
    braceGate = (smoothstep(0.16f, 0.3f, _e756.y) * (1f - smoothstep(0.66f, 0.82f, _e766.y)));
    let _e772: vec3<f32> = col;
    let _e773: vec3<f32> = global.activity_secondary;
    let _e774: f32 = leftBrace;
    let _e775: f32 = rightBrace;
    let _e778: f32 = braceGate;
    let _e782: f32 = global.structure_amount;
    col = (_e772 + ((((_e773 * (_e774 + _e775)) * _e778) * 0.06f) * _e782));
    let _e793: vec2<f32> = uv_8;
    let _e801: f32 = box_mask(_e793, vec2<f32>(0.335f, 0.185f), vec2<f32>(0.845f, 0.255f), 0.01f);
    railChannel = _e801;
    let _e804: vec3<f32> = global.base_bottom;
    let _e807: f32 = railChannel;
    let _e810: vec3<f32> = col;
    let _e811: vec3<f32> = global.base_bottom;
    let _e814: f32 = railChannel;
    col = mix(_e810, (_e811 * 0.38f), vec3((_e814 * 0.76f)));
    let _e827: vec2<f32> = uv_8;
    let _e835: f32 = box_mask(_e827, vec2<f32>(0.345f, 0.211f), vec2<f32>(0.835f, 0.222f), 0.004f);
    railSpine = _e835;
    let _e837: vec3<f32> = col;
    let _e838: vec3<f32> = global.activity_accent;
    let _e839: f32 = railSpine;
    let _e843: f32 = global.structure_amount;
    col = (_e837 + (((_e838 * _e839) * 0.028f) * _e843));
    let _e854: vec2<f32> = uv_8;
    let _e862: f32 = box_mask(_e854, vec2<f32>(0.36f, 0.2f), vec2<f32>(0.82f, 0.24f), 0.008f);
    railBand = _e862;
    let _e865: vec2<f32> = uv_8;
    let _e869: vec2<f32> = uv_8;
    let _e875: vec2<f32> = uv_8;
    let _e879: vec2<f32> = uv_8;
    railPattern = step(0.58f, fract((_e879.x * 22f)));
    let _e886: vec3<f32> = col;
    let _e887: vec3<f32> = global.activity_secondary;
    let _e888: f32 = railBand;
    let _e890: f32 = railPattern;
    let _e894: f32 = global.energy;
    col = (_e886 + (((_e887 * _e888) * _e890) * (0.075f + (0.075f * _e894))));
    let _e899: f32 = global.time_value;
    if (_e899 > 0f) {
        let _e902: f32 = global.time_value;
        let _e906: f32 = global.time_value;
        let _e908: f32 = (_e906 * 0.018f);
        local = ((_e908 - (floor((_e908 / 1.16f)) * 1.16f)) - 0.08f);
    } else {
        local = 0.53f;
    }
    let _e918: f32 = local;
    sweepCenter = _e918;
    let _e923: vec2<f32> = uv_8;
    let _e925: f32 = sweepCenter;
    let _e927: vec2<f32> = uv_8;
    let _e929: f32 = sweepCenter;
    let _e934: vec2<f32> = uv_8;
    let _e936: f32 = sweepCenter;
    let _e938: vec2<f32> = uv_8;
    let _e940: f32 = sweepCenter;
    wallSweep = (1f - smoothstep(0.015f, 0.095f, abs((_e938.x - _e940))));
    let _e954: vec2<f32> = uv_8;
    let _e962: f32 = box_mask(_e954, vec2<f32>(0.29f, 0.27f), vec2<f32>(0.91f, 0.7f), 0.035f);
    wallSweepGate = _e962;
    let _e964: vec3<f32> = col;
    let _e969: f32 = wallSweep;
    let _e971: f32 = wallSweepGate;
    let _e975: f32 = global.energy;
    let _e977: f32 = global.structure_amount;
    col = (_e964 + (((((vec3<f32>(0.17f, 0.25f, 0.32f) * _e969) * _e971) * 0.034f) * _e975) * _e977));
    let _e980: f32 = stageHeroX;
    heroCenter = vec2<f32>(_e980, 0.49f);
    let _e989: vec2<f32> = uv_8;
    let _e990: vec2<f32> = heroCenter;
    let _e994: f32 = ellipse_mask(_e989, _e990, vec2<f32>(0.205f, 0.39f));
    hero = _e994;
    let _e998: f32 = global.time_value;
    if (_e998 > 0f) {
        let _e1001: f32 = pulse;
        let _e1002: f32 = global.time_value;
        let _e1004: f32 = global.pulse_speed;
        let _e1009: f32 = global.time_value;
        let _e1011: f32 = global.pulse_speed;
        pulse = (_e1001 + (sin((_e1009 * (0.55f + (_e1011 * 0.45f)))) * 0.035f));
    }
    let _e1020: vec3<f32> = col;
    let _e1021: vec3<f32> = global.hunter_halo;
    let _e1022: f32 = hero;
    let _e1024: f32 = global.halo_strength;
    let _e1026: f32 = global.hero_light;
    let _e1030: f32 = global.energy;
    let _e1034: f32 = pulse;
    col = (_e1020 + (((((_e1021 * _e1022) * _e1024) * _e1026) * (0.095f + (0.035f * _e1030))) * _e1034));
    let _e1038: f32 = stageHeroX;
    let _e1044: vec2<f32> = uv_8;
    let _e1045: f32 = stageHeroX;
    let _e1051: f32 = ellipse_mask(_e1044, vec2<f32>(_e1045, 0.43f), vec2<f32>(0.105f, 0.32f));
    rimColumn = _e1051;
    let _e1053: vec3<f32> = col;
    let _e1054: vec3<f32> = global.hunter_rim;
    let _e1055: f32 = rimColumn;
    let _e1059: f32 = global.hero_light;
    col = (_e1053 + (((_e1054 * _e1055) * 0.04f) * _e1059));
    let _e1063: f32 = stageHeroX;
    let _e1069: vec2<f32> = uv_8;
    let _e1070: f32 = stageHeroX;
    let _e1076: f32 = ellipse_mask(_e1069, vec2<f32>(_e1070, 0.39f), vec2<f32>(0.165f, 0.225f));
    heroKey = _e1076;
    let _e1083: vec3<f32> = col;
    let _e1084: vec3<f32> = heroKeyColor;
    let _e1085: f32 = heroKey;
    let _e1089: f32 = global.hero_light;
    col = (_e1083 + ((_e1084 * _e1085) * (0.04f + (0.02f * _e1089))));
    let _e1096: vec2<f32> = uv_8;
    let _e1102: vec2<f32> = uv_8;
    let _e1109: vec2<f32> = uv_8;
    let _e1115: vec2<f32> = uv_8;
    let _e1126: vec2<f32> = uv_8;
    let _e1132: vec2<f32> = uv_8;
    let _e1139: vec2<f32> = uv_8;
    let _e1145: vec2<f32> = uv_8;
    shaftA = max(0f, (1f - abs((((_e1139.x - 0.58f) * 6.4f) + ((_e1145.y - 0.15f) * 0.55f)))));
    let _e1158: vec2<f32> = uv_8;
    let _e1164: vec2<f32> = uv_8;
    let _e1171: vec2<f32> = uv_8;
    let _e1177: vec2<f32> = uv_8;
    let _e1188: vec2<f32> = uv_8;
    let _e1194: vec2<f32> = uv_8;
    let _e1201: vec2<f32> = uv_8;
    let _e1207: vec2<f32> = uv_8;
    shaftB = max(0f, (1f - abs((((_e1201.x - 0.7f) * 7.1f) - ((_e1207.y - 0.12f) * 0.45f)))));
    let _e1221: vec2<f32> = uv_8;
    let _e1225: vec2<f32> = uv_8;
    let _e1231: vec2<f32> = uv_8;
    let _e1235: vec2<f32> = uv_8;
    shaftGate = ((1f - smoothstep(0.2f, 0.86f, _e1225.y)) * smoothstep(0.04f, 0.18f, _e1235.y));
    let _e1240: vec3<f32> = col;
    let _e1241: vec3<f32> = global.activity_secondary;
    let _e1242: f32 = shaftA;
    let _e1243: f32 = shaftB;
    let _e1246: f32 = shaftGate;
    let _e1250: f32 = global.beam_intensity;
    let _e1252: f32 = global.energy;
    col = (_e1240 + (((((_e1241 * (_e1242 + _e1243)) * _e1246) * 0.028f) * _e1250) * _e1252));
    let _e1257: vec2<f32> = uv_8;
    let _e1261: vec2<f32> = uv_8;
    floorMask = smoothstep(0.58f, 0.78f, _e1261.y);
    let _e1266: vec3<f32> = global.base_bottom;
    let _e1269: f32 = floorMask;
    let _e1272: vec3<f32> = col;
    let _e1273: vec3<f32> = global.base_bottom;
    let _e1276: f32 = floorMask;
    col = mix(_e1272, (_e1273 * 0.72f), vec3((_e1276 * 0.56f)));
    let _e1281: vec2<f32> = uv_8;
    let _e1286: vec2<f32> = uv_8;
    fy = max((_e1286.y - 0.56f), 0.001f);
    let _e1294: f32 = fy;
    perspectiveY = (1f / ((_e1294 * 10f) + 0.55f));
    let _e1301: f32 = perspectiveY;
    let _e1304: f32 = perspectiveY;
    let _e1311: f32 = perspectiveY;
    let _e1314: f32 = perspectiveY;
    let _e1321: f32 = line((fract((_e1314 * 4.2f)) - 0.5f), 0.06f);
    horizontal = _e1321;
    let _e1323: vec2<f32> = uv_8;
    let _e1325: f32 = stageHeroX;
    let _e1327: f32 = fy;
    let _e1331: f32 = fy;
    centeredX = ((_e1323.x - _e1325) / max((_e1331 + 0.2f), 0.2f));
    let _e1338: f32 = centeredX;
    let _e1341: f32 = centeredX;
    let _e1348: f32 = centeredX;
    let _e1351: f32 = centeredX;
    let _e1358: f32 = line((fract((_e1351 * 4.5f)) - 0.5f), 0.035f);
    vertical = _e1358;
    let _e1362: f32 = horizontal;
    let _e1363: f32 = vertical;
    let _e1365: f32 = floorMask;
    grid = (max(_e1362, _e1363) * _e1365);
    let _e1368: vec3<f32> = col;
    let _e1369: vec3<f32> = global.activity_accent;
    let _e1370: f32 = grid;
    let _e1374: f32 = global.floor_grid;
    col = (_e1368 + (((_e1369 * _e1370) * 0.038f) * _e1374));
    let _e1377: f32 = global.lobby_mode;
    if (_e1377 > 0.5f) {
        {
            loop {
                let _e1382: i32 = slot;
                if !((_e1382 >= 0i)) {
                    break;
                }
                {
                    let _e1389: i32 = slot;
                    let _e1391: vec4<f32> = global.lobby_pad_geometry[_e1389];
                    geometry = _e1391;
                    let _e1393: vec4<f32> = geometry;
                    centre = _e1393.xy;
                    let _e1396: vec4<f32> = geometry;
                    radius_6 = _e1396.zw;
                    let _e1399: i32 = slot;
                    if (_e1399 < 4i) {
                        let _e1402: i32 = slot;
                        let _e1404: f32 = global.lobby_occupancy_a[_e1402];
                        local_1 = _e1404;
                    } else {
                        let _e1405: i32 = slot;
                        let _e1409: f32 = global.lobby_occupancy_b[(_e1405 - 4i)];
                        local_1 = _e1409;
                    }
                    let _e1411: f32 = local_1;
                    occupied = _e1411;
                    let _e1413: vec2<f32> = uv_8;
                    let _e1414: vec2<f32> = centre;
                    let _e1416: vec2<f32> = radius_6;
                    foot = ((_e1413 - _e1414) / _e1416);
                    let _e1421: vec2<f32> = foot;
                    let _e1422: vec2<f32> = foot;
                    q_3 = dot(_e1421, _e1422);
                    let _e1425: vec2<f32> = uv_8;
                    let _e1426: vec2<f32> = centre;
                    let _e1432: vec2<f32> = radius_6;
                    outer = ((_e1425 - (_e1426 + vec2<f32>(0f, 0.014f))) / (_e1432 * 1.12f));
                    let _e1442: vec2<f32> = outer;
                    let _e1443: vec2<f32> = outer;
                    let _e1449: vec2<f32> = outer;
                    let _e1450: vec2<f32> = outer;
                    shadow = (1f - smoothstep(0.52f, 1.22f, dot(_e1449, _e1450)));
                    let _e1455: vec3<f32> = col;
                    let _e1457: f32 = shadow;
                    let _e1459: f32 = occupied;
                    col = (_e1455 * (1f - (_e1457 * (0.1f + (_e1459 * 0.11f)))));
                    let _e1472: f32 = q_3;
                    body = (1f - smoothstep(0.64f, 0.98f, _e1472));
                    let _e1481: f32 = q_3;
                    let _e1489: f32 = q_3;
                    lip = (smoothstep(0.71f, 0.82f, _e1481) * (1f - smoothstep(0.93f, 1.05f, _e1489)));
                    let _e1494: f32 = lip;
                    let _e1498: vec2<f32> = foot;
                    let _e1503: vec2<f32> = foot;
                    raisedEdge = (_e1494 * smoothstep(-0.25f, 0.7f, _e1503.y));
                    let _e1514: f32 = q_3;
                    innerDish = (1f - smoothstep(0.2f, 0.72f, _e1514));
                    let _e1527: vec2<f32> = foot;
                    let _e1535: vec2<f32> = foot;
                    let _e1552: vec2<f32> = foot;
                    let _e1560: vec2<f32> = foot;
                    metal = mix(vec3<f32>(0.017f, 0.033f, 0.047f), vec3<f32>(0.043f, 0.075f, 0.095f), vec3(clamp(((1f - _e1560.y) * 0.34f), 0f, 1f)));
                    let _e1573: f32 = body;
                    let _e1576: vec3<f32> = col;
                    let _e1577: vec3<f32> = metal;
                    let _e1578: f32 = body;
                    col = mix(_e1576, _e1577, vec3((_e1578 * 0.85f)));
                    let _e1583: vec3<f32> = col;
                    let _e1588: f32 = raisedEdge;
                    col = (_e1583 + ((vec3<f32>(0.075f, 0.118f, 0.15f) * _e1588) * 0.18f));
                    let _e1596: vec3<f32> = global.activity_secondary;
                    let _e1597: vec3<f32> = global.hunter_halo;
                    activeLight = mix(_e1596, _e1597, vec3(0.16f));
                    let _e1602: vec3<f32> = col;
                    let _e1603: vec3<f32> = activeLight;
                    let _e1604: f32 = lip;
                    let _e1607: f32 = occupied;
                    col = (_e1602 + ((_e1603 * _e1604) * (0.025f + (_e1607 * 0.14f))));
                    let _e1613: vec3<f32> = col;
                    let _e1618: f32 = innerDish;
                    let _e1621: f32 = occupied;
                    col = (_e1613 + ((vec3<f32>(0.075f, 0.135f, 0.17f) * _e1618) * (0.014f + (_e1621 * 0.017f))));
                    let _e1630: f32 = q_3;
                    let _e1633: f32 = q_3;
                    let _e1639: f32 = q_3;
                    let _e1642: f32 = q_3;
                    ring1_ = (1f - smoothstep(0.026f, 0.052f, abs((_e1642 - 0.22f))));
                    let _e1652: f32 = q_3;
                    let _e1655: f32 = q_3;
                    let _e1661: f32 = q_3;
                    let _e1664: f32 = q_3;
                    ring2_ = (1f - smoothstep(0.025f, 0.05f, abs((_e1664 - 0.43f))));
                    let _e1671: vec2<f32> = foot;
                    let _e1673: vec2<f32> = foot;
                    let _e1675: vec2<f32> = foot;
                    let _e1677: vec2<f32> = foot;
                    angle = atan2(_e1675.y, _e1677.x);
                    let _e1681: f32 = angle;
                    let _e1688: f32 = angle;
                    phase = fract((((_e1688 + 3.1415927f) / 6.2831855f) * 12f));
                    let _e1702: f32 = phase;
                    let _e1710: f32 = phase;
                    segments = (smoothstep(0.06f, 0.15f, _e1702) * (1f - smoothstep(0.73f, 0.86f, _e1710)));
                    let _e1715: vec3<f32> = col;
                    let _e1716: vec3<f32> = activeLight;
                    let _e1719: f32 = ring1_;
                    let _e1720: f32 = ring2_;
                    let _e1723: f32 = segments;
                    let _e1726: f32 = occupied;
                    col = (_e1715 + (((_e1716 * max(_e1719, _e1720)) * _e1723) * (0.024f + (_e1726 * 0.063f))));
                    let _e1732: vec2<f32> = centre;
                    let _e1733: vec2<f32> = radius_6;
                    let _e1738: vec2<f32> = radius_6;
                    leftBoot = (_e1732 + vec2<f32>((-(_e1733.x) * 0.3f), (-(_e1738.y) * 0.05f)));
                    let _e1746: vec2<f32> = centre;
                    let _e1747: vec2<f32> = radius_6;
                    let _e1751: vec2<f32> = radius_6;
                    rightBoot = (_e1746 + vec2<f32>((_e1747.x * 0.3f), (-(_e1751.y) * 0.05f)));
                    let _e1761: vec2<f32> = radius_6;
                    let _e1765: vec2<f32> = radius_6;
                    let _e1770: vec2<f32> = uv_8;
                    let _e1771: vec2<f32> = leftBoot;
                    let _e1772: vec2<f32> = radius_6;
                    let _e1776: vec2<f32> = radius_6;
                    let _e1781: f32 = ellipse_mask(_e1770, _e1771, vec2<f32>((_e1772.x * 0.23f), (_e1776.y * 0.44f)));
                    let _e1784: vec2<f32> = radius_6;
                    let _e1788: vec2<f32> = radius_6;
                    let _e1793: vec2<f32> = uv_8;
                    let _e1794: vec2<f32> = rightBoot;
                    let _e1795: vec2<f32> = radius_6;
                    let _e1799: vec2<f32> = radius_6;
                    let _e1804: f32 = ellipse_mask(_e1793, _e1794, vec2<f32>((_e1795.x * 0.23f), (_e1799.y * 0.44f)));
                    boots = (_e1781 + _e1804);
                    let _e1807: vec3<f32> = col;
                    let _e1812: f32 = boots;
                    let _e1814: f32 = occupied;
                    col = (_e1807 * (1f - ((min(1f, _e1812) * _e1814) * 0.13f)));
                }
                continuing {
                    let _e1386: i32 = slot;
                    slot = (_e1386 - 1i);
                }
            }
        }
    } else {
        {
            let _e1826: vec2<f32> = uv_8;
            let _e1827: vec4<f32> = soloPad;
            let _e1830: vec4<f32> = soloPad;
            platformDelta = ((_e1826 - _e1827.xy) / _e1830.zw);
            let _e1836: vec2<f32> = platformDelta;
            let _e1837: vec2<f32> = platformDelta;
            platformQ = dot(_e1836, _e1837);
            let _e1846: f32 = platformQ;
            platformFill = (1f - smoothstep(0.62f, 1f, _e1846));
            let _e1855: f32 = platformQ;
            let _e1863: f32 = platformQ;
            platformEdge = (smoothstep(0.62f, 0.77f, _e1855) * (1f - smoothstep(0.88f, 1f, _e1863)));
            let _e1873: f32 = platformFill;
            let _e1876: vec3<f32> = col;
            let _e1881: f32 = platformFill;
            col = mix(_e1876, vec3<f32>(0.006f, 0.012f, 0.022f), vec3((_e1881 * 0.7f)));
            let _e1892: f32 = platformQ;
            platformCore = (1f - smoothstep(0.08f, 0.7f, _e1892));
            let _e1896: vec3<f32> = col;
            let _e1900: vec3<f32> = global.activity_accent;
            let _e1901: vec3<f32> = global.hunter_halo;
            let _e1905: f32 = platformCore;
            let _e1907: f32 = global.floor_glow;
            col = (_e1896 + (((mix(_e1900, _e1901, vec3(0.3f)) * _e1905) * _e1907) * 0.045f));
            let _e1912: vec3<f32> = col;
            let _e1913: vec3<f32> = global.hunter_halo;
            let _e1914: f32 = platformEdge;
            let _e1916: f32 = global.floor_glow;
            col = (_e1912 + (((_e1913 * _e1914) * _e1916) * 0.22f));
            let _e1921: vec3<f32> = col;
            let _e1922: vec3<f32> = global.activity_secondary;
            let _e1923: f32 = platformEdge;
            let _e1925: f32 = global.floor_glow;
            col = (_e1921 + (((_e1922 * _e1923) * _e1925) * 0.075f));
            let _e1933: f32 = platformQ;
            let _e1936: f32 = platformQ;
            let _e1942: f32 = platformQ;
            let _e1945: f32 = platformQ;
            ringA = (1f - smoothstep(0.018f, 0.05f, abs((_e1945 - 0.2f))));
            let _e1955: f32 = platformQ;
            let _e1958: f32 = platformQ;
            let _e1964: f32 = platformQ;
            let _e1967: f32 = platformQ;
            ringB = (1f - smoothstep(0.018f, 0.05f, abs((_e1967 - 0.4f))));
            let _e1977: f32 = platformQ;
            let _e1980: f32 = platformQ;
            let _e1986: f32 = platformQ;
            let _e1989: f32 = platformQ;
            ringC = (1f - smoothstep(0.018f, 0.05f, abs((_e1989 - 0.6f))));
            let _e1996: vec2<f32> = platformDelta;
            let _e1998: vec2<f32> = platformDelta;
            let _e2000: vec2<f32> = platformDelta;
            let _e2002: vec2<f32> = platformDelta;
            ringAngle = atan2(_e2000.y, _e2002.x);
            let _e2006: f32 = ringAngle;
            let _e2013: f32 = ringAngle;
            ringPhase = fract((((_e2013 + 3.1415927f) / 6.2831855f) * 12f));
            let _e2027: f32 = ringPhase;
            let _e2035: f32 = ringPhase;
            ringSegments = (smoothstep(0.08f, 0.18f, _e2027) * (1f - smoothstep(0.72f, 0.82f, _e2035)));
            let _e2043: f32 = ringB;
            let _e2044: f32 = ringC;
            let _e2046: f32 = ringA;
            let _e2049: f32 = ringB;
            let _e2050: f32 = ringC;
            let _e2053: f32 = ringSegments;
            serviceRings = (max(_e2046, max(_e2049, _e2050)) * _e2053);
            let _e2056: vec3<f32> = col;
            let _e2060: vec3<f32> = global.activity_secondary;
            let _e2061: vec3<f32> = global.hunter_halo;
            let _e2065: f32 = serviceRings;
            let _e2067: f32 = global.floor_glow;
            col = (_e2056 + (((mix(_e2060, _e2061, vec3(0.22f)) * _e2065) * _e2067) * 0.11f));
        }
    }
    let _e2074: vec2<f32> = uv_8;
    let _e2078: vec2<f32> = uv_8;
    let _e2084: vec2<f32> = uv_8;
    let _e2088: vec2<f32> = uv_8;
    lowFog = (smoothstep(0.52f, 0.92f, _e2078.y) * (1f - smoothstep(0.88f, 1f, _e2088.y)));
    let _e2096: f32 = global.time_value;
    if (_e2096 > 0f) {
        let _e2099: f32 = fogWave;
        let _e2100: vec2<f32> = uv_8;
        let _e2104: f32 = global.time_value;
        let _e2108: vec2<f32> = uv_8;
        let _e2112: f32 = global.time_value;
        fogWave = (_e2099 + (sin(((_e2108.x * 8f) + (_e2112 * 0.18f))) * 0.1f));
    }
    let _e2126: vec3<f32> = global.activity_accent;
    fogColor = mix(_e2126, vec3<f32>(0.16f, 0.24f, 0.31f), vec3(0.58f));
    let _e2137: f32 = lowFog;
    let _e2138: f32 = global.fog_amount;
    let _e2142: f32 = fogWave;
    let _e2144: vec3<f32> = col;
    let _e2145: vec3<f32> = fogColor;
    let _e2146: f32 = lowFog;
    let _e2147: f32 = global.fog_amount;
    let _e2151: f32 = fogWave;
    col = mix(_e2144, _e2145, vec3((((_e2146 * _e2147) * 0.1f) * _e2151)));
    let _e2156: f32 = stageHeroX;
    let _e2162: vec2<f32> = uv_8;
    let _e2163: f32 = stageHeroX;
    let _e2169: f32 = ellipse_mask(_e2162, vec2<f32>(_e2163, 0.685f), vec2<f32>(0.315f, 0.135f));
    rearLegFog = _e2169;
    let _e2177: vec3<f32> = global.activity_accent;
    rearFogColor = mix(_e2177, vec3<f32>(0.22f, 0.28f, 0.33f), vec3(0.72f));
    let _e2188: f32 = rearLegFog;
    let _e2189: f32 = global.fog_amount;
    let _e2193: f32 = global.energy;
    let _e2197: vec3<f32> = col;
    let _e2198: vec3<f32> = rearFogColor;
    let _e2199: f32 = rearLegFog;
    let _e2200: f32 = global.fog_amount;
    let _e2204: f32 = global.energy;
    col = mix(_e2197, _e2198, vec3(((_e2199 * _e2200) * (0.045f + (0.02f * _e2204)))));
    let _e2210: f32 = global.time_value;
    let _e2213: f32 = global.time_value;
    drift = (sin((_e2213 * 0.14f)) * 0.015f);
    let _e2220: vec2<f32> = uv_8;
    let _e2224: f32 = drift;
    let _e2226: vec2<f32> = uv_8;
    let _e2234: vec2<f32> = uv_8;
    let _e2238: f32 = drift;
    let _e2240: vec2<f32> = uv_8;
    let _e2250: vec2<f32> = uv_8;
    let _e2254: f32 = drift;
    let _e2256: vec2<f32> = uv_8;
    let _e2264: vec2<f32> = uv_8;
    let _e2268: f32 = drift;
    let _e2270: vec2<f32> = uv_8;
    let _e2281: vec2<f32> = uv_8;
    let _e2285: f32 = drift;
    let _e2287: vec2<f32> = uv_8;
    let _e2295: vec2<f32> = uv_8;
    let _e2299: f32 = drift;
    let _e2301: vec2<f32> = uv_8;
    let _e2311: vec2<f32> = uv_8;
    let _e2315: f32 = drift;
    let _e2317: vec2<f32> = uv_8;
    let _e2325: vec2<f32> = uv_8;
    let _e2329: f32 = drift;
    let _e2331: vec2<f32> = uv_8;
    shafts = (exp(-(pow(((((_e2264.x - 0.36f) - _e2268) + (_e2270.y * 0.07f)) * 29f), 2f))) + exp(-(pow(((((_e2325.x - 0.66f) + _e2329) - (_e2331.y * 0.05f)) * 34f), 2f))));
    let _e2346: vec2<f32> = uv_8;
    let _e2350: vec2<f32> = uv_8;
    let _e2356: vec2<f32> = uv_8;
    let _e2360: vec2<f32> = uv_8;
    reactorShaftGate = (smoothstep(0.18f, 0.3f, _e2350.y) * (1f - smoothstep(0.64f, 0.84f, _e2360.y)));
    let _e2366: vec3<f32> = col;
    let _e2377: vec3<f32> = global.activity_accent;
    let _e2381: f32 = shafts;
    let _e2383: f32 = reactorShaftGate;
    let _e2387: f32 = global.energy;
    col = (_e2366 + ((((mix(vec3<f32>(0.14f, 0.58f, 0.85f), _e2377, vec3(0.35f)) * _e2381) * _e2383) * 0.11f) * _e2387));
    let _e2390: vec2<f32> = uv_8;
    let _e2392: f32 = stageHeroX;
    let _e2394: f32 = aspect;
    let _e2396: vec2<f32> = uv_8;
    haloUv = vec2<f32>(((_e2390.x - _e2392) * _e2394), (_e2396.y - 0.46f));
    let _e2403: vec2<f32> = haloUv;
    haloRadius = length(_e2403);
    let _e2406: vec2<f32> = haloUv;
    let _e2408: vec2<f32> = haloUv;
    let _e2410: vec2<f32> = haloUv;
    let _e2412: vec2<f32> = haloUv;
    haloAngle = atan2(_e2410.y, _e2412.x);
    let _e2419: f32 = haloRadius;
    let _e2422: f32 = haloRadius;
    let _e2428: f32 = haloRadius;
    let _e2431: f32 = haloRadius;
    haloRing = (1f - smoothstep(0.0015f, 0.004f, abs((_e2431 - 0.275f))));
    let _e2438: f32 = haloRadius;
    let _e2441: f32 = haloRadius;
    let _e2448: f32 = haloRadius;
    let _e2451: f32 = haloRadius;
    haloGlow = exp((-(abs((_e2451 - 0.275f))) * 90f));
    let _e2462: f32 = haloAngle;
    let _e2465: f32 = global.time_value;
    let _e2469: f32 = haloAngle;
    let _e2472: f32 = global.time_value;
    let _e2479: f32 = haloAngle;
    let _e2482: f32 = global.time_value;
    let _e2486: f32 = haloAngle;
    let _e2489: f32 = global.time_value;
    haloSegments = smoothstep(0.1f, 0.5f, sin(((_e2486 * 12f) + (_e2489 * 0.08f))));
    let _e2496: vec3<f32> = col;
    let _e2503: vec3<f32> = global.activity_accent;
    let _e2511: f32 = haloRing;
    let _e2512: f32 = haloSegments;
    let _e2516: f32 = haloGlow;
    let _e2521: f32 = global.energy;
    col = (_e2496 + ((mix(_e2503, vec3<f32>(0.22f, 0.7f, 0.86f), vec3(0.55f)) * (((_e2511 * _e2512) * 0.19f) + (_e2516 * 0.035f))) * _e2521));
    let _e2524: vec2<f32> = uv_8;
    let _e2525: f32 = global.time_value;
    let _e2528: f32 = global.time_value;
    let _e2534: f32 = global.time_value;
    dustUv = (_e2524 + vec2<f32>((sin((_e2528 * 0.09f)) * 0.008f), (_e2534 * 0.006f)));
    let _e2540: vec2<f32> = dustUv;
    let _e2545: vec2<f32> = dustUv;
    cells = floor((_e2545 * vec2<f32>(64f, 36f)));
    let _e2552: vec2<f32> = dustUv;
    let _e2557: vec2<f32> = dustUv;
    cellUv = (fract((_e2557 * vec2<f32>(64f, 36f))) - vec2(0.5f));
    let _e2568: vec2<f32> = cells;
    let _e2569: f32 = hash21_(_e2568);
    seed = _e2569;
    let _e2572: f32 = global.particle_amount;
    let _e2578: f32 = global.particle_amount;
    let _e2582: f32 = seed;
    mote = step((0.955f - (_e2578 * 0.018f)), _e2582);
    let _e2589: vec2<f32> = cellUv;
    let _e2594: vec2<f32> = cellUv;
    sparkle = (1f - smoothstep(0.015f, 0.09f, length(_e2594)));
    let _e2599: f32 = sparkle;
    let _e2604: vec2<f32> = cellUv;
    let _e2609: vec2<f32> = cellUv;
    sparkle = (_e2599 + ((1f - smoothstep(0.03f, 0.28f, length(_e2609))) * 0.16f));
    let _e2618: f32 = global.time_value;
    if (_e2618 > 0f) {
        let _e2621: f32 = twinkle;
        let _e2623: f32 = global.time_value;
        let _e2625: f32 = seed;
        let _e2628: f32 = seed;
        let _e2632: f32 = global.time_value;
        let _e2634: f32 = seed;
        let _e2637: f32 = seed;
        twinkle = (_e2621 + (0.25f * sin(((_e2632 * (0.7f + _e2634)) + (_e2637 * 18f)))));
    }
    let _e2644: vec3<f32> = col;
    let _e2648: vec3<f32> = global.activity_secondary;
    let _e2649: vec3<f32> = global.hunter_rim;
    let _e2653: f32 = mote;
    let _e2655: f32 = sparkle;
    let _e2657: f32 = twinkle;
    let _e2659: f32 = global.particle_amount;
    col = (_e2644 + (((((mix(_e2648, _e2649, vec3(0.45f)) * _e2653) * _e2655) * _e2657) * _e2659) * 0.6f));
    let _e2665: vec3<f32> = col;
    let _e2671: f32 = global.warmth;
    let _e2674: vec3<f32> = col;
    let _e2675: vec3<f32> = col;
    let _e2681: f32 = global.warmth;
    col = mix(_e2674, (_e2675 * vec3<f32>(1.1f, 0.97f, 0.84f)), vec3((_e2681 * 0.3f)));
    let _e2689: vec2<f32> = uv_8;
    let _e2693: vec2<f32> = uv_8;
    leftMask = (1f - smoothstep(0.02f, 0.37f, _e2693.x));
    let _e2700: vec2<f32> = uv_8;
    let _e2704: vec2<f32> = uv_8;
    rightMask = smoothstep(0.78f, 1f, _e2704.x);
    let _e2708: vec3<f32> = col;
    let _e2710: f32 = leftMask;
    let _e2711: f32 = global.left_darken;
    col = (_e2708 * (1f - (_e2710 * _e2711)));
    let _e2715: vec3<f32> = col;
    let _e2717: f32 = rightMask;
    let _e2718: f32 = global.right_darken;
    col = (_e2715 * (1f - (_e2717 * _e2718)));
    let _e2724: vec2<f32> = uv_8;
    let _e2730: vec2<f32> = uv_8;
    let _e2737: vec2<f32> = uv_8;
    let _e2743: vec2<f32> = uv_8;
    let _e2753: vec2<f32> = uv_8;
    let _e2759: vec2<f32> = uv_8;
    let _e2766: vec2<f32> = uv_8;
    let _e2772: vec2<f32> = uv_8;
    vignette = smoothstep(0.56f, 1.03f, length(vec2<f32>(((_e2766.x - 0.52f) * 1.08f), ((_e2772.y - 0.48f) * 0.92f))));
    let _e2782: vec3<f32> = col;
    let _e2784: f32 = vignette;
    let _e2786: f32 = global.background_softness;
    col = (_e2782 * (1f - (_e2784 * (0.2f + (_e2786 * 0.18f)))));
    let _e2794: vec4<f32> = gl_FragCoord;
    let _e2796: f32 = global.time_value;
    let _e2802: vec4<f32> = gl_FragCoord;
    let _e2804: f32 = global.time_value;
    let _e2810: f32 = hash21_((_e2802.xy + vec2<f32>((_e2804 * 3f), 0f)));
    grain = (_e2810 - 0.5f);
    let _e2814: vec3<f32> = col;
    let _e2815: f32 = grain;
    col = (_e2814 + vec3((_e2815 * 0.006f)));
    let _e2823: vec3<f32> = col;
    let _e2828: vec3<f32> = clamp(_e2823, vec3(0f), vec3(1f));
    prime_output = vec4<f32>(_e2828.x, _e2828.y, _e2828.z, 1f);
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
