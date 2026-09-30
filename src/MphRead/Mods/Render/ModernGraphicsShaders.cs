#if !MPHREAD_SERVER
namespace MphRead.Mods.Render
{
    /// <summary>
    /// WGSL equivalents of Project Prime's core/original renderer. Uniforms
    /// live in a 16-byte-slot compatibility block populated from the existing
    /// GL.Uniform* calls; this keeps the engine-facing API stable while DX12,
    /// Vulkan and Metal receive WebGPU-native shaders.
    /// </summary>
    internal static class ModernGraphicsShaders
    {
        internal const int UniformSlots = 256;

        internal const int ImmColor = 0;
        internal const int Flags0 = 1;
        internal const int Light1Vector = 2;
        internal const int Light1Color = 3;
        internal const int Light2Vector = 4;
        internal const int Light2Color = 5;
        internal const int Diffuse = 6;
        internal const int Ambient = 7;
        internal const int Specular = 8;
        internal const int Emission = 9;
        internal const int FogColor = 10;
        internal const int Scalars0 = 11;
        internal const int Projection = 12;
        internal const int View = 16;
        internal const int ViewInverse = 20;
        internal const int TextureMatrix = 24;
        internal const int MatrixStack = 28; // 32 mat4 = 128 slots, through 155.
        internal const int FragmentFlags0 = 156;
        internal const int OverrideColor = 157;
        internal const int PaletteOverrideColor = 158;
        internal const int FragmentFlags1 = 159;
        internal const int FlatColor = 160;
        internal const int PlayerOutlineColor = 161;
        internal const int FragmentFlags2 = 162;
        internal const int FragmentFlags3 = 163;
        internal const int ToonTable = 164; // 32 vec3 slots, through 195.
        internal const int RttScalars = 196;
        internal const int FadeColor = 197;

        private const string Common = @"
struct LegacyUniforms {
    data: array<vec4<u32>, 256>,
};
@group(0) @binding(0) var<uniform> u: LegacyUniforms;

fn uf4(slot: u32) -> vec4<f32> {
    return bitcast<vec4<f32>>(u.data[slot]);
}
fn uf3(slot: u32) -> vec3<f32> {
    return uf4(slot).xyz;
}
fn uf1(slot: u32) -> f32 {
    return bitcast<f32>(u.data[slot].x);
}
fn ui(slot: u32, component: u32) -> i32 {
    return bitcast<i32>(u.data[slot][component]);
}
fn ub(slot: u32, component: u32) -> bool {
    return u.data[slot][component] != 0u;
}
fn umat4(slot: u32) -> mat4x4<f32> {
    return mat4x4<f32>(uf4(slot), uf4(slot + 1u), uf4(slot + 2u), uf4(slot + 3u));
}
";

        internal static string World { get; } = Common + @"
@group(0) @binding(1) var base_tex: texture_2d<f32>;
@group(0) @binding(2) var base_sampler: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) normal: vec3<f32>,
    @location(3) texcoord: vec3<f32>,
    @location(4) color_set: f32,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) surface_normal: vec3<f32>,
    @location(3) surface_position: vec3<f32>,
};

fn light_calc(light_vec: vec3<f32>, light_col: vec3<f32>, normal_vec: vec3<f32>,
              dif_col: vec3<f32>, amb_col: vec3<f32>, spe_col: vec3<f32>) -> vec3<f32> {
    let sight_vec = vec3<f32>(0.0, 0.0, -1.0);
    let dif_factor = max(0.0, -dot(light_vec, normal_vec));
    let half_vec = (light_vec + sight_vec) / 2.0;
    var spe_factor = max(0.0, dot(-half_vec, normal_vec));
    spe_factor = spe_factor * spe_factor;
    let spe_out = spe_col * light_col * spe_factor;
    let dif_out = dif_col * light_col * dif_factor;
    let amb_out = amb_col * light_col;
    return spe_out + dif_out + amb_out;
}

fn stack_matrix(index: i32) -> mat4x4<f32> {
    let safe = u32(clamp(index, 0, 31));
    return umat4(28u + safe * 4u);
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    let use_light = ub(1u, 0u);
    let use_texture = ub(1u, 1u);
    let show_colors = ub(1u, 2u);
    let imm_color = uf4(0u);
    let vtx_in_color = select(imm_color, input.color, input.color_set > 0.5);

    let stack_mtx = stack_matrix(i32(clamp(input.texcoord.z, 0.0, 31.0)));
    let model_mtx = stack_mtx * umat4(20u);
    output.position = umat4(12u) * umat4(16u) * model_mtx * vec4<f32>(input.position, 1.0);

    let vtx_color = select(vec4<f32>(1.0), vtx_in_color, show_colors);
    let normal_matrix = mat3x3<f32>(model_mtx[0].xyz, model_mtx[1].xyz, model_mtx[2].xyz);
    let normal = normalize(normal_matrix * input.normal);
    output.surface_normal = normal;
    output.surface_position = (model_mtx * vec4<f32>(input.position, 1.0)).xyz;

    if (use_light) {
        var dif_current = uf3(6u);
        var amb_current = uf3(7u);
        if (vtx_in_color.a == 0.0) {
            dif_current = vtx_color.rgb;
            amb_current = vec3<f32>(0.0);
        }
        let col1 = light_calc(uf3(2u), uf3(3u), normal, dif_current, amb_current, uf3(8u));
        let col2 = light_calc(uf3(4u), uf3(5u), normal, dif_current, amb_current, uf3(8u));
        output.color = vec4<f32>(min(col1 + col2 + uf3(9u), vec3<f32>(1.0)), 1.0);
    }
    else {
        output.color = vec4<f32>(vtx_color.rgb, 1.0);
    }

    output.texcoord = vec2<f32>(0.0);
    if (use_texture) {
        let tex_mtx = umat4(24u);
        let texgen_mode = ui(11u, 3u);
        if (texgen_mode == 0 || texgen_mode == 1) {
            output.texcoord = (tex_mtx * vec4<f32>(input.texcoord.xy, 0.0, 1.0)).xy;
        }
        else {
            var tex_mul = tex_mtx;
            if (texgen_mode == 2) {
                let base = select(mat4x4<f32>(), umat4(16u), use_light);
                let stack3 = mat3x3<f32>(stack_mtx[0].xyz, stack_mtx[1].xyz, stack_mtx[2].xyz);
                let stack4 = mat4x4<f32>(
                    vec4<f32>(stack3[0], 0.0),
                    vec4<f32>(stack3[1], 0.0),
                    vec4<f32>(stack3[2], 0.0),
                    vec4<f32>(0.0, 0.0, 0.0, 1.0));
                tex_mul = transpose(tex_mtx * base * stack4);
            }
            let c0 = vec4<f32>(tex_mul[0].xyz, input.texcoord.x);
            let c1 = vec4<f32>(tex_mul[1].xyz, input.texcoord.y);
            let source = select(vec4<f32>(input.position, 1.0),
                                vec4<f32>(input.normal, 1.0), texgen_mode == 2);
            output.texcoord = vec2<f32>(dot(source, c0), dot(source, c1));
        }
    }
    return output;
}

struct FragmentInput {
    @builtin(position) position: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) surface_normal: vec3<f32>,
    @location(3) surface_position: vec3<f32>,
};

fn toon_color(input: vec4<f32>) -> vec4<f32> {
    let index = u32(clamp(i32(input.r * 31.0), 0, 31));
    return vec4<f32>(uf3(164u + index), input.a);
}

fn cel_shade(c: vec3<f32>, bands: i32) -> vec3<f32> {
    let steps = f32(bands);
    let lum = max(max(c.r, c.g), c.b);
    if (lum <= 0.0) {
        return c;
    }
    let scaled = lum * steps - 0.5;
    let lower = floor(scaled);
    let level = (lower + 0.5 + smoothstep(0.46, 0.54, scaled - lower)) / steps;
    let banded = c * (level / lum);
    let grey = dot(banded, vec3<f32>(0.299, 0.587, 0.114));
    return clamp(mix(vec3<f32>(grey), banded, vec3<f32>(1.35)), vec3<f32>(0.0), vec3<f32>(1.0));
}

@fragment
fn fs_main(input: FragmentInput) -> @location(0) vec4<f32> {
    let use_texture = ub(1u, 1u);
    let fog_enable = ub(1u, 3u);
    let mat_alpha = uf4(156u).x;
    let mat_mode = ui(156u, 1u);
    let use_override = ub(156u, 2u);
    let use_pal_override = ub(156u, 3u);
    let alpha_test = ui(159u, 0u);
    let cel_bands = ui(159u, 1u);
    let use_flat = ub(159u, 2u);
    let textured_player_skin = ui(159u, 3u);
    let player_outline_mask = ub(162u, 0u);

    var col: vec4<f32>;
    if (use_texture) {
        var texcolor = textureSample(base_tex, base_sampler, input.texcoord);
        if (use_pal_override) {
            texcolor = vec4<f32>(uf4(158u).xyz, texcolor.a);
        }
        if (use_flat && !use_pal_override && textured_player_skin == 0) {
            texcolor = vec4<f32>(uf3(160u), texcolor.a);
        }

        if (mat_mode == 1) {
            col = vec4<f32>(
                texcolor.r * texcolor.a + input.color.r * (1.0 - texcolor.a),
                texcolor.g * texcolor.a + input.color.g * (1.0 - texcolor.a),
                texcolor.b * texcolor.a + input.color.b * (1.0 - texcolor.a),
                mat_alpha * input.color.a);
        }
        else if (mat_mode == 2) {
            let toon = toon_color(input.color);
            col = vec4<f32>(texcolor.rgb * input.color.r + toon.rgb,
                            mat_alpha * texcolor.a * input.color.a);
        }
        else {
            col = input.color * vec4<f32>(texcolor.rgb, mat_alpha * texcolor.a);
        }

        if (use_override) {
            let override_color = uf4(157u);
            if (textured_player_skin > 0) {
                if (textured_player_skin == 2) {
                    let detail = smoothstep(0.05, 0.85,
                        dot(texcolor.rgb, vec3<f32>(0.2126, 0.7152, 0.0722)));
                    let tinted = override_color.rgb * (0.25 + 0.75 * detail);
                    col = vec4<f32>(mix(tinted, pow(texcolor.rgb, vec3<f32>(0.7)), vec3<f32>(0.25)), col.a);
                }
                else {
                    col = vec4<f32>(clamp(mix(col.rgb, texcolor.rgb, vec3<f32>(0.8)) * 1.25,
                                              vec3<f32>(0.0), vec3<f32>(1.0)), col.a);
                }
            }
            else {
                col = vec4<f32>(override_color.rgb, col.a);
            }
            col.a = col.a * override_color.a;
        }
    }
    else if (use_override) {
        col = uf4(157u);
    }
    else {
        col = select(input.color, toon_color(input.color), mat_mode == 2);
        col.a = col.a * mat_alpha;
    }

    if (player_outline_mask) {
        if (col.a <= 0.01) {
            discard;
        }
        col = vec4<f32>(uf3(161u), col.a);
    }

    if (cel_bands > 0) {
        col = vec4<f32>(cel_shade(col.rgb, cel_bands), col.a);
    }

    if (fog_enable) {
        let fog_min = uf4(11u).y;
        let fog_max = uf4(11u).z;
        var density = 0.0;
        if (input.position.z >= fog_max) {
            density = 1.0;
        }
        else if (input.position.z > fog_min) {
            density = (input.position.z - fog_min) / (fog_max - fog_min) * 124.0 / 128.0;
        }
        col = vec4<f32>((col * (1.0 - density) + uf4(10u) * density).xyz, col.a);
    }

    if (alpha_test == 1 && col.a < 1.0) {
        discard;
    }
    if (alpha_test == 2 && col.a >= 1.0) {
        discard;
    }
    return col;
}
";

        internal static string Rtt { get; } = Common + @"
@group(0) @binding(1) var base_tex: texture_2d<f32>;
@group(0) @binding(2) var base_sampler: sampler;
@group(0) @binding(3) var mask_tex: texture_2d<f32>;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(3) texcoord: vec3<f32>,
};
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
};

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.position.xy, 0.0, 1.0);
    output.texcoord = input.texcoord.xy;
    return output;
}

struct FragmentInput {
    @builtin(position) position: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
};

@fragment
fn fs_main(input: FragmentInput) -> @location(0) vec4<f32> {
    let alpha = uf4(196u).x;
    let use_mask = ub(196u, 1u);
    let view_width = uf4(196u).z;
    let view_height = uf4(196u).w;
    let fade_color = uf4(197u);
    if (fade_color.a > 0.0) {
        return fade_color;
    }

    var color = textureSample(base_tex, base_sampler, input.texcoord);
    if (use_mask) {
        let mask_y = input.position.y + (view_width - view_height) / 2.0;
        let mask_uv = vec2<f32>(input.position.x / view_width, 1.0 - mask_y / view_width);
        let mask_color = textureSample(mask_tex, base_sampler, mask_uv);
        if (mask_color.a > 0.0) {
            color.a = 0.0;
        }
    }
    color.a = color.a * alpha;
    return color;
}
";
    }
}
#endif
