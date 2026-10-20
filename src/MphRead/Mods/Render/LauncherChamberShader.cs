#if !MPHREAD_SERVER
namespace MphRead.Mods.Render
{
    /// <summary>One authored deployment chamber shader, shared by GL and the generated modern pipeline.</summary>
    internal static class LauncherChamberShader
    {
        public static string VertexSource => @"#version 120
varying vec2 chamber_uv;
void main()
{
    chamber_uv = gl_MultiTexCoord0.xy;
    gl_Position = gl_Vertex;
}
";

        public static string FragmentSource => @"#version 120
varying vec2 chamber_uv;

uniform vec2 resolution;
uniform float time_value;
uniform float energy_time;
uniform float launch_amount;
uniform vec4 hunter_weights_a;
uniform vec4 hunter_weights_b;
uniform vec4 slot_light[8];
// Ready is stable authority; join/leave are short presentation-only envelopes.
uniform vec4 slot_signal[8];
uniform float detail_level;
uniform float effect_strength;
uniform float scene_blend;
uniform float hero_reveal;
uniform vec3 base_top;
uniform vec3 base_mid;
uniform vec3 base_bottom;
uniform vec3 activity_accent;
uniform vec3 activity_secondary;
uniform vec3 hunter_halo;
uniform vec3 hunter_rim;
uniform float energy;
uniform float fog_amount;
uniform float particle_amount;
uniform float structure_amount;
uniform float floor_grid;
uniform float hero_light;
uniform float pulse_speed;
uniform float warmth;
uniform float halo_strength;
uniform float floor_glow;
uniform float left_darken;
uniform float right_darken;
uniform float beam_intensity;
uniform float background_softness;
uniform float lobby_mode;
uniform float home_party_mode;
uniform float home_layout_mode;
uniform vec4 home_pad_geometry[4];
uniform vec4 lobby_occupancy_a;
uniform vec4 lobby_occupancy_b;
uniform vec4 lobby_pad_geometry[8];

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return fract(p.x * p.y);
}

float line(float value, float width)
{
    return 1.0 - smoothstep(width, width * 2.2, abs(value));
}

float box_mask(vec2 uv, vec2 lo, vec2 hi, float feather)
{
    vec2 a = smoothstep(lo - feather, lo, uv);
    vec2 b = 1.0 - smoothstep(hi, hi + feather, uv);
    return a.x * a.y * b.x * b.y;
}

float ellipse_mask(vec2 uv, vec2 center, vec2 radius)
{
    vec2 d = (uv - center) / max(radius, vec2(0.001));
    float q = dot(d, d);
    return 1.0 - smoothstep(0.35, 1.0, q);
}

float platform_edge_mask(vec2 uv, vec2 center, vec2 radius)
{
    vec2 d = (uv - center) / max(radius, vec2(0.001));
    float q = dot(d, d);
    return smoothstep(0.60, 0.75, q) * (1.0 - smoothstep(0.86, 1.0, q));
}

float platform_fill_mask(vec2 uv, vec2 center, vec2 radius)
{
    vec2 d = (uv - center) / max(radius, vec2(0.001));
    float q = dot(d, d);
    return 1.0 - smoothstep(0.62, 1.0, q);
}

void main()
{
    vec2 uv = chamber_uv;
    // Shared with the centered solo preview and lobby formation.
    float stageHeroX = home_layout_mode > 0.5 ? home_pad_geometry[0].x : 0.5;
    vec2 roomUv = uv + vec2(0.5 - stageHeroX, 0.0);
    float aspect = max(resolution.x / max(resolution.y, 1.0), 1.0);
    vec2 p = vec2((uv.x - 0.5) * aspect, uv.y - 0.5);

    // Deep three-stop chamber base, brighter near the horizon and central bay.
    float upper = smoothstep(0.0, 0.52, uv.y);
    vec3 col = mix(base_top, base_mid, upper);
    col = mix(col, base_bottom, smoothstep(0.58, 1.0, uv.y));
    float horizon = exp(-pow((uv.y - 0.52) * 4.8, 2.0));
    col += mix(activity_accent, hunter_halo, 0.35) * horizon * 0.087 * energy;

    // Minimal keeps an anchored steel room and truthful pads, while skipping
    // machinery, trigonometric motifs, shafts, haze and particle evaluation.
    if (detail_level < 0.5)
    {
        float wall = box_mask(roomUv, vec2(0.24, 0.18), vec2(0.76, 0.72), 0.025);
        col += vec3(0.024, 0.040, 0.052) * wall;
        col *= 1.0 - smoothstep(0.62, 0.92, uv.y) * 0.32;
        for (int slot = 7; slot >= 0; slot--)
        {
            if (lobby_mode < 0.5 && slot > 0) continue;
            vec4 pad = lobby_mode > 0.5 ? lobby_pad_geometry[slot]
                : (home_layout_mode > 0.5 ? home_pad_geometry[0] : vec4(0.5, 0.805, 0.235, 0.060));
            float body = platform_fill_mask(uv, pad.xy, pad.zw);
            float edge = platform_edge_mask(uv, pad.xy, pad.zw);
            float occupied = lobby_mode > 0.5 ? slot_signal[slot].w : 1.0;
            vec3 lightColor = lobby_mode > 0.5 ? slot_light[slot].rgb : hunter_rim;
            col = mix(col, vec3(0.012, 0.025, 0.036), body * 0.88);
            col += mix(vec3(0.045, 0.100, 0.135), lightColor, occupied)
                * edge * (0.10 + occupied * 0.20);
            if (lobby_mode > 0.5)
                col += vec3(0.25, 0.88, 0.58) * edge * slot_signal[slot].x
                    * smoothstep(0.0, 0.035, uv.y - pad.y) * 0.30;
        }
        col *= 1.0 - (1.0 - smoothstep(0.02, 0.37, uv.x)) * left_darken;
        col *= 1.0 - smoothstep(0.78, 1.0, uv.x) * right_darken;
        gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
        return;
    }

    // Distant central bay. It is intentionally broad and low-detail so Hunter
    // silhouettes remain the only detailed subject in the middle of the frame.
    float bay = box_mask(roomUv, vec2(0.225, 0.14), vec2(0.775, 0.72), 0.035);
    col += mix(activity_accent, hunter_halo, 0.64) * bay * 0.075 * structure_amount;

    // Neutral torso-level lift keeps the selected Hunter readable without
    // painting Hunter identity color through the model itself. This is the
    // brightest part of the room, but still reads as reflected chamber light.
    float torsoLift = ellipse_mask(uv, vec2(stageHeroX, 0.49), vec2(0.255, 0.335));
    vec3 heroAmbient = mix(base_mid, vec3(0.105, 0.145, 0.185), 0.55);
    col += heroAmbient * torsoLift * (0.15 + 0.05 * hero_light);

    // Recessed inner wall panels add a second depth plane behind the hero.
    float leftRecess = box_mask(roomUv, vec2(0.275, 0.30), vec2(0.375, 0.69), 0.014);
    float rightRecess = box_mask(roomUv, vec2(0.625, 0.30), vec2(0.725, 0.69), 0.014);
    float recess = min(1.0, leftRecess + rightRecess);
    col *= 1.0 - recess * 0.10 * structure_amount;
    float innerEdges = line(roomUv.x - 0.275, 0.0022) + line(roomUv.x - 0.375, 0.0022)
        + line(roomUv.x - 0.625, 0.0022) + line(roomUv.x - 0.725, 0.0022);
    float innerGate = smoothstep(0.27, 0.34, roomUv.y) * (1.0 - smoothstep(0.68, 0.74, roomUv.y));
    col += mix(activity_secondary, hunter_rim, 0.42) * innerEdges * innerGate * 0.043 * structure_amount;

    // A deeper, deliberately asymmetric wall plane prevents the chamber from
    // reading as a row of evenly spaced primitives. These broad silhouettes
    // sit behind the lit ribs and leave the hero bay visually open.
    float farLeftWall = box_mask(roomUv, vec2(0.245, 0.23), vec2(0.325, 0.70), 0.020);
    float farRightWall = box_mask(roomUv, vec2(0.870, 0.18), vec2(0.945, 0.66), 0.020);
    float farWall = min(1.0, farLeftWall + farRightWall);
    col = mix(col, base_bottom * 0.58, farWall * 0.52 * structure_amount);
    float farCrossbeam = box_mask(roomUv, vec2(0.255, 0.285), vec2(0.930, 0.302), 0.008);
    col = mix(col, base_bottom * 0.44, farCrossbeam * 0.70 * structure_amount);
    col += activity_accent * farCrossbeam * 0.020 * structure_amount;

    // Architecture stays anchored; reflected light supplies the movement.
    float gantryTravel = 0.0;
    float gantryGate = box_mask(roomUv, vec2(0.22, 0.17), vec2(0.88, 0.65), 0.025);
    float gantry = line(abs(roomUv.x - 0.5) - (0.255 + gantryTravel + (roomUv.y - 0.3) * 0.13), 0.010);
    col = mix(col, base_bottom, gantry * gantryGate * 0.58);
    float gantryEdge = line(abs(roomUv.x - 0.5) - (0.244 + gantryTravel + (roomUv.y - 0.3) * 0.13), 0.0012);
    col += hunter_rim * gantryEdge * gantryGate * 0.16;

    // Recessed maintenance rotors turn inside stationary steel housings.
    // Their blades stay in the distant wall plane, clear of UI interaction.
    if (detail_level >= 2.0)
    {
        for (int machine = 0; machine < 2; machine++)
        {
            float side = float(machine);
            vec2 centre = vec2(0.315 + side * 0.370, 0.425 - side * 0.035);
            vec2 local = (roomUv - centre) / vec2(0.052, 0.092);
            float radius = length(local);
            float housing = 1.0 - smoothstep(0.92, 1.05, radius);
            col = mix(col, vec3(0.008, 0.019, 0.029), housing * 0.82);
            float collar = line(radius - 0.90, 0.025);
            float inner = line(radius - 0.68, 0.018);
            float angle = atan(local.y, local.x) + time_value * (side < 0.5 ? 0.095 : -0.075);
            float blades = pow(max(0.0, cos(angle * 6.0)), 5.0)
                * smoothstep(0.15, 0.30, radius) * (1.0 - smoothstep(0.67, 0.78, radius));
            vec3 metal = vec3(0.11, 0.17, 0.21);
            col += metal * (collar * 0.46 + blades * 0.36) * structure_amount;
            col += mix(activity_secondary, hunter_rim, 0.28) * inner * 0.17;
            float hub = 1.0 - smoothstep(0.06, 0.14, radius);
            col += vec3(0.22, 0.35, 0.41) * hub * 0.25;
            // Cast casing shadow, not a model/world shadow-map substitute.
            float bracket = box_mask(roomUv, centre + vec2(-0.060, 0.100),
                centre + vec2(0.060, 0.112), 0.003);
            col = mix(col, base_bottom * 0.70, bracket * 0.72);
        }
    }

    // An overhead service carriage moves only along its authored gantry.
    float railGate = box_mask(roomUv, vec2(0.29, 0.205), vec2(0.71, 0.248), 0.004);
    col = mix(col, vec3(0.009, 0.022, 0.033), railGate * 0.72);
    col += activity_accent * line(roomUv.y - 0.210, 0.001) * railGate * 0.18;
    if (detail_level >= 3.0)
    {
        float carriageX = 0.5 + sin(time_value * 0.065) * 0.16;
        float carriage = box_mask(roomUv, vec2(carriageX - 0.024, 0.214), vec2(carriageX + 0.024, 0.244), 0.002);
        col = mix(col, vec3(0.060, 0.088, 0.106), carriage * 0.82);
        col += activity_secondary * line(roomUv.y - 0.239, 0.0012) * carriage * 0.22;
    }

    // Sequenced vertical power conduits: staggered cycles, restrained surges.
    float surge = pow(0.5 + 0.5 * sin(energy_time * 0.57), 8.0);
    for (int channel = 0; channel < 4; channel++) {
        if (detail_level < 2.0 && channel >= 2) continue;
        float fi = float(channel);
        float cx = 0.285 + fi * 0.145;
        float travel = 0.5 + 0.5 * sin(energy_time * 0.7 - roomUv.y * 12.0 + fi * 1.6);
        float conduit = line(roomUv.x - cx, 0.0013) * box_mask(roomUv, vec2(0.20, 0.22), vec2(0.86, 0.70), 0.025);
        col += mix(activity_secondary, hunter_rim, 0.7) * conduit * (0.035 + pow(travel, 5.0) * (0.26 + surge * 0.16));
    }

    if (detail_level >= 2.0) {
    // Seven identities crossfade as weights; model textures are never tinted.
    float samus = hunter_weights_a.x;
    float kanden = hunter_weights_a.y;
    float trace = hunter_weights_a.z;
    float sylux = hunter_weights_a.w;
    float noxus = hunter_weights_b.x;
    float spire = hunter_weights_b.y;
    float weavel = hunter_weights_b.z;
    float scanY = 0.44 + sin(time_value * 0.32) * 0.17;
    float tactical = line(roomUv.y - scanY - abs(roomUv.x - 0.5) * 0.23, 0.0018);
    float bio = pow(0.5 + 0.5 * sin(roomUv.y * 27.0 + sin(time_value * 1.7) * 2.0 + energy_time), 6.0);
    float electric = pow(0.5 + 0.5 * sin(roomUv.y * 35.0 - energy_time * 3.0), 14.0);
    float thermal = 0.5 + 0.5 * sin(roomUv.x * 28.0 + sin(time_value * 0.3 + roomUv.y * 8.0));
    float motifGate = ellipse_mask(roomUv, vec2(0.5, 0.47), vec2(0.34, 0.34));
    float sideGate = smoothstep(0.10, 0.22, abs(roomUv.x - 0.5));
    col += hunter_rim * motifGate * (trace * tactical * 0.40 + kanden * bio * sideGate * 0.10
        + sylux * electric * sideGate * 0.18 + samus * surge * 0.065
        + noxus * thermal * 0.030 + spire * thermal * smoothstep(0.4, 0.78, roomUv.y) * 0.13
        + weavel * tactical * sideGate * (0.18 + step(0.5, roomUv.x) * 0.15));

    }

    // Vertical chamber ribs and recessed side panels.
    for (int i = 0; i < 7; i++)
    {
        float fi = float(i);
        float x = 0.16 + fi * 0.135;
        float rib = line(roomUv.x - x, 0.0018 + 0.001 * roomUv.y);
        float gate = smoothstep(0.10, 0.20, roomUv.y) * (1.0 - smoothstep(0.76, 0.90, roomUv.y));
        col += mix(activity_accent, hunter_halo, 0.25) * rib * gate * 0.065 * structure_amount;
    }

    // Angled architectural braces. The opposing slopes frame the hero instead
    // of crossing the torso.
    float leftBrace = line((roomUv.x - 0.17) - (0.64 - roomUv.y) * 0.26, 0.006);
    float rightBrace = line((0.91 - roomUv.x) - (0.64 - roomUv.y) * 0.22, 0.006);
    float braceGate = smoothstep(0.16, 0.30, roomUv.y) * (1.0 - smoothstep(0.66, 0.82, roomUv.y));
    col += activity_secondary * (leftBrace + rightBrace) * braceGate
        * 0.092 * structure_amount;

    // A very slow light sweep crosses only the distant architecture. Reduce
    // Motion supplies time_value == 0, which freezes this at a stable position.
    float sweepCenter = time_value > 0.0
        ? 0.5 + sin(time_value * 0.12) * 0.38
        : 0.53;
    float wallSweep = 1.0 - smoothstep(0.015, 0.095, abs(roomUv.x - sweepCenter));
    float wallSweepGate = box_mask(roomUv, vec2(0.29, 0.27), vec2(0.91, 0.70), 0.035);
    col += vec3(0.17, 0.25, 0.32) * wallSweep * wallSweepGate
        * 0.24 * energy * structure_amount;

    // Operational pulse travels through the architectural seam, not over the
    // model. Activity-specific intensity/speed makes browser, training, and
    // lobby meaningfully different while Reduce Motion locks time to zero.
    float signalGate = box_mask(roomUv, vec2(0.31, 0.275), vec2(0.855, 0.305), 0.008);
    float signalPhase = sin(roomUv.x * 36.0 - time_value * 0.45 - energy_time * 0.65);
    float activeSignal = 0.5 + 0.5 * signalPhase;
    col += mix(activity_secondary, hunter_rim, 0.44)
        * signalGate * activeSignal * (0.045 + 0.040 * energy);

    // Hero halo behind the selected Hunter. Hunter theme drives the hue while
    // activity mood decides how energetic the chamber feels.
    vec2 heroCenter = vec2(stageHeroX, 0.49);
    float hero = ellipse_mask(uv, heroCenter, vec2(0.205, 0.39));
    float pulse = 1.0;
    if (time_value > 0.0)
        pulse += sin(time_value * 0.55 + energy_time * 0.45) * 0.035;
    col += hunter_halo * hero * halo_strength * hero_light
        * (0.120 + 0.060 * energy) * pulse;

    // Narrow rear rim column gives shoulders/head a clean edge without turning
    // the whole background into a glowing circle.
    float rimColumn = ellipse_mask(uv, vec2(stageHeroX, 0.43), vec2(0.105, 0.32));
    col += hunter_rim * rimColumn * 0.090 * hero_light;

    // Neutral cool key behind the head and upper torso. It raises local
    // contrast without recoloring the Hunter's authored red/orange/blue/etc.
    float heroKey = ellipse_mask(uv, vec2(stageHeroX, 0.390), vec2(0.165, 0.225));
    vec3 heroKeyColor = vec3(0.31, 0.39, 0.46);
    col += heroKeyColor * heroKey * (0.040 + 0.020 * hero_light);

    // Soft volumetric shafts from above.
    float shaftA = max(0.0, 1.0 - abs((roomUv.x - 0.43) * 6.4 + (uv.y - 0.15) * 0.55));
    float shaftB = max(0.0, 1.0 - abs((roomUv.x - 0.63) * 7.1 - (uv.y - 0.12) * 0.45));
    float shaftGate = (1.0 - smoothstep(0.20, 0.86, uv.y)) * smoothstep(0.04, 0.18, uv.y);
    col += activity_secondary * (shaftA + shaftB) * shaftGate
        * 0.028 * beam_intensity * energy * effect_strength;

    // Floor plane and perspective grid.
    float floorMask = smoothstep(0.58, 0.78, uv.y);
    col = mix(col, base_bottom * 0.72, floorMask * 0.56);

    float fy = max(uv.y - 0.56, 0.001);
    float perspectiveY = 1.0 / (fy * 10.0 + 0.55);
    float horizontal = line(fract(perspectiveY * 4.2) - 0.5, 0.06);
    float centeredX = (uv.x - stageHeroX) / max(fy + 0.20, 0.20);
    float vertical = line(fract(centeredX * 4.5) - 0.5, 0.035);
    float grid = max(horizontal, vertical) * floorMask;
    col += mix(activity_accent, hunter_halo, 0.26) * grid * 0.058 * floor_grid;

    // Every lobby pad, real Hunter frame, and player plate reads the one
    // authored LauncherLobbyFormation table. There are no shader-local
    // guesses about the positions anymore.
    // The service spine and rear deployment gantry reconfigure smoothly as
    // navigation changes; actual roster/model ownership changes immediately.
    float serviceSpine = line(abs(uv.x - 0.5) - (0.79 - uv.y) * 0.42, 0.0025);
    float serviceGate = smoothstep(0.29, 0.43, uv.y)
        * (1.0 - smoothstep(0.75, 0.83, uv.y));
    col += activity_secondary * serviceSpine * serviceGate * scene_blend
        * (0.12 + launch_amount * 0.20) * energy;
    // Eight recessed occupancy lamps use current authority, without displaying
    // a smoothed or fabricated player count or deployment countdown.
    for (int lamp = 0; lamp < 8; lamp++)
    {
        float x = 0.395 + float(lamp) * 0.030;
        float recess = box_mask(roomUv, vec2(x, 0.265), vec2(x + 0.015, 0.272), 0.001);
        col = mix(col, vec3(0.009, 0.023, 0.031), recess * scene_blend);
        col += mix(activity_secondary * 0.05, slot_light[lamp].rgb * 0.42, slot_signal[lamp].w)
            * recess * scene_blend;
    }
    if (lobby_mode > 0.5)
    {
        for (int slot = 7; slot >= 0; slot--)
        {
            vec4 geometry = lobby_pad_geometry[slot];
            vec2 centre = geometry.xy;
            vec2 radius = geometry.zw;
            float occupied = slot < 4
                ? lobby_occupancy_a[slot] : lobby_occupancy_b[slot - 4];

            // A raised steel pedestal, not a floating dark ellipse.
            vec2 foot = (uv - centre) / radius;
            float q = dot(foot, foot);
            vec2 outer = (uv - (centre + vec2(0.0, 0.014))) / (radius * 1.12);
            float shadow = 1.0 - smoothstep(0.52, 1.22, dot(outer, outer));
            col *= 1.0 - shadow * (0.10 + occupied * 0.11);

            float body = 1.0 - smoothstep(0.64, 0.98, q);
            float lip = smoothstep(0.71, 0.82, q)
                * (1.0 - smoothstep(0.93, 1.05, q));
            float raisedEdge = lip * smoothstep(-0.25, 0.70, foot.y);
            float innerDish = 1.0 - smoothstep(0.20, 0.72, q);

            // Muted steel panels and illuminated rim. Unoccupied pads retain
            // structure but never compete with an occupied Hunter.
            vec3 metal = mix(vec3(0.017, 0.033, 0.047),
                             vec3(0.043, 0.075, 0.095),
                             clamp((1.0 - foot.y) * 0.34, 0.0, 1.0));
            col = mix(col, metal, body * 0.85);
            col += vec3(0.075, 0.118, 0.150) * raisedEdge * 0.18;
            vec3 activeLight = mix(activity_secondary * 0.28, slot_light[slot].rgb, occupied);
            float confirmation = slot_light[slot].a;
            float ready = slot_signal[slot].x;
            float joining = slot_signal[slot].y;
            float leaving = slot_signal[slot].z;
            col += activeLight * lip * (0.034 + occupied * (0.20 + ready * 0.10));
            // A stable front-facing green marker remains after the confirmation
            // pulse. It disappears on the exact frame authority withdraws ready.
            col += vec3(0.25, 0.88, 0.58) * lip * ready
                * smoothstep(0.05, 0.45, foot.y) * 0.40;
            float wake = line(sqrt(q) - (1.0 - joining * 0.68), 0.022) * joining;
            float fade = line(sqrt(q) - (0.45 + leaving * 0.48), 0.026) * leaving;
            col += activeLight * wake * 0.30;
            col += vec3(0.14, 0.29, 0.35) * fade * 0.22;
            col += vec3(0.075, 0.135, 0.170) * innerDish
                * (0.014 + occupied * 0.017);

            // Four ring cuts and twelve broken light segments. They communicate
            // hardware, not eight huge pools of opaque ink.
            float ring1 = 1.0 - smoothstep(0.026, 0.052, abs(q - 0.22));
            float ring2 = 1.0 - smoothstep(0.025, 0.050, abs(q - 0.43));
            float angle = atan(foot.y, foot.x);
            float phase = fract((angle + 3.14159265) / 6.28318530 * 12.0 - energy_time * 0.10 + float(slot) * 0.37);
            float segments = smoothstep(0.06, 0.15, phase)
                * (1.0 - smoothstep(0.73, 0.86, phase));
            col += activeLight * max(ring1, ring2) * segments
                * (0.034 + occupied * (0.12 + confirmation * 0.32 + launch_amount * 0.22));

            // Contact darkening happens at the exact same pad coordinates as
            // the models' authored stance, before those models are rendered.
            vec2 leftBoot = centre + vec2(-radius.x * 0.30, -radius.y * 0.05);
            vec2 rightBoot = centre + vec2(radius.x * 0.30, -radius.y * 0.05);
            float boots = ellipse_mask(uv, leftBoot,
                    vec2(radius.x * 0.23, radius.y * 0.44))
                + ellipse_mask(uv, rightBoot,
                    vec2(radius.x * 0.23, radius.y * 0.44));
            col *= 1.0 - min(1.0, boots) * occupied * 0.13;
        }
    }
    else
    {
        // Home's smaller party pedestals have separate horizontal bounds
        // and depth rows. The original foreground hero pad remains below them.
        if (home_party_mode > 0.5)
        {
            for (int seat = 0; seat < 3; seat++)
            {
                vec4 pad = /* HOME_PARTY_PAD_GEOMETRY */;
                if (home_layout_mode > 0.5) pad = home_pad_geometry[seat + 1];
                vec2 foot = (uv - pad.xy) / pad.zw;
                float q = dot(foot, foot);
                float body = 1.0 - smoothstep(0.64, 0.98, q);
                float edge = smoothstep(0.71, 0.82, q)
                    * (1.0 - smoothstep(0.93, 1.05, q));
                vec3 metal = mix(vec3(0.017, 0.033, 0.047), vec3(0.043, 0.075, 0.095),
                    clamp((1.0 - foot.y) * 0.34, 0.0, 1.0));
                col = mix(col, metal, body * 0.85);
                col += vec3(0.075, 0.118, 0.150) * edge * 0.18;
                col += activity_secondary * edge * floor_glow * 0.18;
                float rings = max(1.0 - smoothstep(0.026, 0.052, abs(q - 0.22)),
                    1.0 - smoothstep(0.025, 0.050, abs(q - 0.43)));
                col += activity_secondary * rings * floor_glow * 0.055;
            }
        }

        // The solo pad shares the preview's horizontal center.
        vec4 soloPad = vec4(0.5, 0.805, 0.235, 0.060);
        if (home_layout_mode > 0.5) soloPad = home_pad_geometry[0];
        vec2 platformDelta = (uv - soloPad.xy) / soloPad.zw;
        float platformQ = dot(platformDelta, platformDelta);
        float platformFill = 1.0 - smoothstep(0.62, 1.0, platformQ);
        float platformEdge = smoothstep(0.62, 0.77, platformQ)
            * (1.0 - smoothstep(0.88, 1.0, platformQ));
        col = mix(col, vec3(0.006, 0.012, 0.022), platformFill * 0.70);
        float platformCore = 1.0 - smoothstep(0.08, 0.70, platformQ);
        col += mix(activity_accent, hunter_halo, 0.30)
            * platformCore * floor_glow * 0.092;
        col += hunter_halo * platformEdge * floor_glow * 0.30;
        col += activity_secondary * platformEdge * floor_glow * 0.075;
        // A short stabilization ring acknowledges a new Hunter without retaining
        // the departed mesh, tinting textures or delaying selection/navigation.
        float settling = 1.0 - hero_reveal;
        float stabilize = line(sqrt(platformQ) - (0.25 + hero_reveal * 0.65), 0.024);
        col += hunter_rim * stabilize * settling * 0.42;
        float ringA = 1.0 - smoothstep(0.018, 0.050, abs(platformQ - 0.20));
        float ringB = 1.0 - smoothstep(0.018, 0.050, abs(platformQ - 0.40));
        float ringC = 1.0 - smoothstep(0.018, 0.050, abs(platformQ - 0.60));
        float ringAngle = atan(platformDelta.y, platformDelta.x);
        float ringPhase = fract((ringAngle + 3.14159265) / 6.28318530 * 12.0 - energy_time * 0.12);
        float ringSegments = smoothstep(0.08, 0.18, ringPhase)
            * (1.0 - smoothstep(0.72, 0.82, ringPhase));
        float counterPhase = 0.5 + 0.5 * sin(ringAngle * 5.0 + energy_time * 0.64);
        float serviceRings = max(ringA * ringSegments, max(ringB * counterPhase, ringC * ringSegments));
        col += mix(activity_secondary, hunter_halo, 0.22)
            * serviceRings * floor_glow * 0.22;
    }

    if (detail_level >= 2.0) {
    // Low chamber haze.
    float lowFog = smoothstep(0.52, 0.92, uv.y)
        * (1.0 - smoothstep(0.88, 1.0, uv.y));
    // Two continuous low-frequency fields carry haze across the same room.
    // Both clocks freeze under Reduce Motion; there are no scrolling textures.
    float flow = sin(roomUv.x * 9.0 + uv.y * 5.0 - time_value * 0.14);
    float eddy = sin(roomUv.x * 17.0 - uv.y * 8.0 + time_value * 0.09 + flow * 0.7);
    float fogWave = 1.0 + flow * 0.10 + eddy * 0.055;
    vec3 fogColor = mix(activity_accent, vec3(0.16, 0.24, 0.31), 0.58);
    col = mix(col, fogColor, lowFog * fog_amount * 0.16 * fogWave);

    // Local rear haze sits under the model pass, so it gives the legs depth
    // while the Hunter itself remains crisp and materially neutral.
    float rearLegFog = ellipse_mask(uv, vec2(stageHeroX, 0.685), vec2(0.315, 0.135));
    vec3 rearFogColor = mix(activity_accent, vec3(0.22, 0.28, 0.33), 0.72);
    col = mix(col, rearFogColor,
        rearLegFog * fog_amount * (0.045 + 0.020 * energy));

    // Soft moving light shafts add depth behind
    // the hero. All animation uses the same reduced-motion-aware clock.
    float drift = sin(time_value * 0.23) * 0.040;
    float shafts = exp(-pow((roomUv.x - 0.34 - drift + uv.y * 0.07) * 29.0, 2.0))
        + exp(-pow((roomUv.x - 0.66 + drift - uv.y * 0.05) * 34.0, 2.0));
    float reactorShaftGate = smoothstep(0.18, 0.30, uv.y) * (1.0 - smoothstep(0.64, 0.84, uv.y));
    col += mix(activity_secondary, hunter_rim, 0.38) * shafts * reactorShaftGate * 0.18 * energy * effect_strength;
    // Slowly rising energy dust, with a soft halo around each bright core.
    vec2 dustUv = uv + vec2(sin(time_value * 0.09) * 0.008, time_value * 0.014);
    vec2 cells = floor(dustUv * vec2(64.0, 36.0));
    vec2 cellUv = fract(dustUv * vec2(64.0, 36.0)) - 0.5;
    float seed = hash21(cells);
    float mote = step(0.955 - particle_amount * 0.018, seed);
    float sparkle = 1.0 - smoothstep(0.015, 0.09, length(cellUv));
    sparkle += (1.0 - smoothstep(0.03, 0.28, length(cellUv))) * 0.16;
    float twinkle = 0.75;
    if (time_value > 0.0)
        twinkle += 0.25 * sin(time_value * (0.7 + seed) + seed * 18.0);
    col += mix(activity_secondary, hunter_rim, 0.45)
        * mote * sparkle * twinkle * particle_amount * 1.15;

    }

    // Adventure can warm the distance without recoloring the Hunter itself.
    col = mix(col, col * vec3(1.10, 0.97, 0.84), warmth * 0.30);

    // UI readability zones. These are part of the world art, not opaque panels.
    float leftMask = 1.0 - smoothstep(0.02, 0.37, uv.x);
    float rightMask = smoothstep(0.78, 1.0, uv.x);
    col *= 1.0 - leftMask * left_darken;
    col *= 1.0 - rightMask * right_darken;

    // Soft global depth and vignette.
    float vignette = smoothstep(0.56, 1.03,
        length(vec2((uv.x - 0.52) * 1.08, (uv.y - 0.48) * 0.92)));
    col *= 1.0 - vignette * (0.20 + background_softness * 0.18);

    // Tiny film grain prevents large gradients from banding at dark values.
    float grain = hash21(gl_FragCoord.xy) - 0.5;
    col += grain * 0.006;

    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
".Replace("/* HOME_PARTY_PAD_GEOMETRY */", MphRead.Mods.Launcher.LauncherHomePartyPads.ShaderSelection);

    }
}
#endif
