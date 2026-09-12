namespace MphRead.Mods.Render
{
    /// <summary>
    /// Standalone OpenGL ES 3.0 sources for the optional Enhanced path. They
    /// are kept separate from EsShaders so Original and Performance cannot
    /// change until the GLES backend explicitly opts into this contract.
    /// </summary>
    internal static class GlesEnhancedShaders
    {
        public static string VertexShader { get; } = @"#version 300 es
precision highp float;
precision highp int;

layout(location = 0) in vec4 a_position;
layout(location = 1) in vec4 a_color;
layout(location = 2) in vec3 a_normal;
layout(location = 3) in vec3 a_texcoord;
layout(location = 4) in float a_color_set;
layout(location = 5) in vec4 a_tangent;

uniform vec4 imm_color;
uniform bool show_colors;
uniform bool use_texture;
uniform int texgen_mode;
uniform vec3 diffuse;
uniform vec3 ambient;
uniform mat4 proj_mtx;
uniform mat4 view_mtx;
uniform mat4 view_inv_mtx;
uniform mat4 tex_mtx;
uniform mat4 mtx_stack[32];

out vec3 world_position;
out vec3 world_normal;
out vec4 world_tangent;
out vec2 texcoord;
out vec4 vertex_color;
out vec3 lighting_diffuse;
out vec3 lighting_ambient;

vec3 safe_normalize(vec3 value, vec3 fallback_value)
{
    float length_squared = dot(value, value);
    return length_squared > 0.00000001
        ? value * inversesqrt(length_squared) : fallback_value;
}

vec3 perpendicular(vec3 normal_value)
{
    vec3 axis = abs(normal_value.x) <= abs(normal_value.y)
        && abs(normal_value.x) <= abs(normal_value.z) ? vec3(1.0, 0.0, 0.0)
        : (abs(normal_value.y) <= abs(normal_value.z)
            ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0));
    return safe_normalize(cross(axis, normal_value), vec3(1.0, 0.0, 0.0));
}

vec3 transform_normal(vec3 normal_value, mat4 model_matrix)
{
    mat3 model = mat3(model_matrix);
    mat3 cofactor = mat3(cross(model[1], model[2]),
        cross(model[2], model[0]), cross(model[0], model[1]));
    float determinant = dot(model[0], cross(model[1], model[2]));
    vec3 transformed = cofactor * normal_value
        * (determinant < 0.0 ? -1.0 : 1.0);
    return safe_normalize(transformed,
        safe_normalize(model * normal_value, vec3(0.0, 1.0, 0.0)));
}

void main()
{
    int matrix_index = clamp(int(a_texcoord.z), 0, 31);
    mat4 stack_mtx = mtx_stack[matrix_index];
    mat4 model_mtx = stack_mtx * view_inv_mtx;
    vec4 world = model_mtx * a_position;
    world_position = world.xyz;
    gl_Position = proj_mtx * view_mtx * world;

    world_normal = transform_normal(a_normal, model_mtx);
    vec3 tangent = mat3(model_mtx) * a_tangent.xyz;
    tangent -= world_normal * dot(world_normal, tangent);
    tangent = safe_normalize(tangent, perpendicular(world_normal));
    mat3 model = mat3(model_mtx);
    float determinant = dot(model[0], cross(model[1], model[2]));
    float source_handedness = a_tangent.w < 0.0 ? -1.0 : 1.0;
    world_tangent = vec4(tangent,
        source_handedness * (determinant < 0.0 ? -1.0 : 1.0));

    vec4 resolved_vertex_color = a_color_set > 0.5 ? a_color : imm_color;
    vertex_color = vec4(show_colors ? resolved_vertex_color.rgb : vec3(1.0), 1.0);
    lighting_diffuse = diffuse;
    lighting_ambient = ambient;
    if (a_color_set > 0.5 && a_color.a == 0.0) {
        lighting_diffuse = vertex_color.rgb;
        lighting_ambient = vec3(0.0);
    }

    texcoord = vec2(0.0);
    if (use_texture) {
        if (texgen_mode == 0 || texgen_mode == 1) {
            texcoord = (tex_mtx * vec4(a_texcoord.xy, 0.0, 1.0)).xy;
        }
        else {
            mat4 tex_mul = tex_mtx;
            if (texgen_mode == 2) {
                tex_mul = transpose(tex_mtx * view_mtx * mat4(mat3(stack_mtx)));
            }
            mat2x4 texgen_mtx = mat2x4(
                vec4(tex_mul[0][0], tex_mul[0][1], tex_mul[0][2], a_texcoord.x),
                vec4(tex_mul[1][0], tex_mul[1][1], tex_mul[1][2], a_texcoord.y));
            texcoord = texgen_mode == 2
                ? vec4(a_normal, 1.0) * texgen_mtx
                : vec4(a_position.xyz, 1.0) * texgen_mtx;
        }
    }
}
";

        public static string FragmentShader { get; } = @"#version 300 es
precision highp float;
precision highp int;

uniform sampler2D albedo_tex;
uniform sampler2D normal_tex;
uniform sampler2D emissive_tex;
uniform bool use_texture;
uniform bool use_normal_map;
uniform bool use_emissive_map;
uniform bool use_light;
uniform bool use_override;
uniform bool use_pal_override;
uniform bool use_flat;
uniform vec4 override_color;
uniform vec4 pal_override_color;
uniform vec3 flat_color;
uniform float mat_alpha;
uniform int mat_mode;
uniform int alpha_test;
uniform int cel_bands;
uniform vec3 toon_table[32];
uniform vec3 light1vec;
uniform vec3 light1col;
uniform vec3 light2vec;
uniform vec3 light2col;
uniform vec3 specular;
uniform float smoothness;
uniform vec3 emission;
uniform vec4 enhanced_emission;
uniform vec3 camera_world_position;
uniform int visual_light_count;
uniform vec4 visual_light_position_radius[8];
uniform vec4 visual_light_color_intensity[8];
uniform mat4 tex_mtx;

in vec3 world_position;
in vec3 world_normal;
in vec4 world_tangent;
in vec2 texcoord;
in vec4 vertex_color;
in vec3 lighting_diffuse;
in vec3 lighting_ambient;

out vec4 frag_color;

vec3 safe_normalize(vec3 value, vec3 fallback_value)
{
    float length_squared = dot(value, value);
    return length_squared > 0.00000001
        ? value * inversesqrt(length_squared) : fallback_value;
}

float srgb_to_linear_component(float value)
{
    value = clamp(value, 0.0, 1.0);
    return value <= 0.04045 ? value / 12.92
        : pow((value + 0.055) / 1.055, 2.4);
}

vec3 srgb_to_linear(vec3 value)
{
    return vec3(srgb_to_linear_component(value.r),
        srgb_to_linear_component(value.g), srgb_to_linear_component(value.b));
}

vec3 resolve_normal_map(vec3 geometry_normal)
{
    vec3 tangent = world_tangent.xyz
        - geometry_normal * dot(geometry_normal, world_tangent.xyz);
    tangent = safe_normalize(tangent, vec3(1.0, 0.0, 0.0));
    vec3 bitangent = safe_normalize(cross(geometry_normal, tangent)
        * (world_tangent.w < 0.0 ? -1.0 : 1.0), vec3(0.0, 0.0, 1.0));

    float a = tex_mtx[0][0];
    float b = tex_mtx[0][1];
    float c = tex_mtx[1][0];
    float d = tex_mtx[1][1];
    float texture_determinant = a * d - b * c;
    vec3 adjusted_tangent = tangent;
    vec3 adjusted_bitangent = bitangent;
    if (abs(texture_determinant) > 0.00000001) {
        float reciprocal_determinant = 1.0 / texture_determinant;
        adjusted_tangent = safe_normalize(
            (d * tangent - c * bitangent) * reciprocal_determinant, tangent);
        adjusted_bitangent = safe_normalize(
            (-b * tangent + a * bitangent) * reciprocal_determinant, bitangent);
    }

    vec3 mapped = texture(normal_tex, texcoord).rgb * 2.0 - 1.0;
    mapped = safe_normalize(mapped, vec3(0.0, 0.0, 1.0));
    return safe_normalize(adjusted_tangent * mapped.x + adjusted_bitangent * mapped.y
        + geometry_normal * mapped.z, geometry_normal);
}

float evaluate_specular(vec3 normal_value, vec3 light_direction,
    vec3 view_direction)
{
    vec3 half_vector = safe_normalize(light_direction + view_direction, normal_value);
    float clamped_smoothness = clamp(smoothness, 0.0, 1.0);
    float exponent = 4.0 + 124.0 * clamped_smoothness * clamped_smoothness;
    float normalization = (exponent + 8.0) / (8.0 * 3.14159265);
    return normalization * pow(max(dot(normal_value, half_vector), 0.0), exponent);
}

vec3 evaluate_directional_light(vec3 light_vector, vec3 light_color,
    vec3 normal_value, vec3 view_direction, vec3 diffuse_color)
{
    vec3 light_direction = safe_normalize(-light_vector, normal_value);
    float diffuse_factor = max(dot(normal_value, light_direction), 0.0);
    float specular_factor = diffuse_factor > 0.0
        ? evaluate_specular(normal_value, light_direction, view_direction) : 0.0;
    return light_color * (diffuse_color * diffuse_factor
        + srgb_to_linear(specular) * specular_factor);
}

vec3 evaluate_room_lighting(vec3 normal_value, vec3 diffuse_color,
    vec3 ambient_color)
{
    vec3 view_direction = safe_normalize(camera_world_position - world_position,
        -light1vec);
    vec3 room_light1 = srgb_to_linear(light1col);
    vec3 room_light2 = srgb_to_linear(light2col);
    return ambient_color * (room_light1 + room_light2)
        + evaluate_directional_light(light1vec, room_light1, normal_value,
            view_direction, diffuse_color)
        + evaluate_directional_light(light2vec, room_light2, normal_value,
            view_direction, diffuse_color);
}

vec3 evaluate_point_light(int light_index, vec3 normal_value,
    vec3 diffuse_color, bool include_specular)
{
    vec3 delta = visual_light_position_radius[light_index].xyz - world_position;
    float distance_to_light = length(delta);
    float radius = max(visual_light_position_radius[light_index].w, 0.0001);
    float attenuation = clamp(1.0 - distance_to_light / radius, 0.0, 1.0);
    attenuation *= attenuation;
    vec3 light_direction = distance_to_light > 0.0001
        ? delta / distance_to_light : normal_value;
    float diffuse_factor = max(dot(normal_value, light_direction), 0.0);
    vec3 view_direction = safe_normalize(camera_world_position - world_position,
        normal_value);
    float specular_factor = include_specular && diffuse_factor > 0.0
        ? evaluate_specular(normal_value, light_direction, view_direction) : 0.0;
    vec3 radiance = srgb_to_linear(visual_light_color_intensity[light_index].rgb)
        * visual_light_color_intensity[light_index].w * attenuation;
    return radiance * (diffuse_color * diffuse_factor
        + srgb_to_linear(specular) * specular_factor);
}

vec3 cel_shade(vec3 color_value)
{
    float steps = float(cel_bands);
    float luminance = max(max(color_value.r, color_value.g), color_value.b);
    if (luminance <= 0.0) return color_value;
    float scaled = luminance * steps - 0.5;
    float lower = floor(scaled);
    float level = (lower + 0.5 + smoothstep(0.46, 0.54, scaled - lower)) / steps;
    vec3 banded = color_value * (level / luminance);
    float grey = dot(banded, vec3(0.299, 0.587, 0.114));
    return max(mix(vec3(grey), banded, 1.35), vec3(0.0));
}

void main()
{
    vec3 normal_value = safe_normalize(world_normal, vec3(0.0, 1.0, 0.0));
    if (use_light && use_normal_map) normal_value = resolve_normal_map(normal_value);

    vec3 diffuse_color = srgb_to_linear(lighting_diffuse);
    vec3 ambient_color = srgb_to_linear(lighting_ambient);
    vec3 surface_color = srgb_to_linear(vertex_color.rgb);
    if (use_light) {
        surface_color = evaluate_room_lighting(normal_value,
            diffuse_color, ambient_color);
    }

    vec3 visual_lighting = vec3(0.0);
    int point_light_count = clamp(visual_light_count, 0, 8);
    for (int light_index = 0; light_index < 8; ++light_index) {
        if (light_index >= point_light_count) break;
        visual_lighting += evaluate_point_light(light_index, normal_value,
            diffuse_color, use_light);
    }
    surface_color = max(surface_color + visual_lighting, vec3(0.0));

    vec4 texture_color = use_texture ? texture(albedo_tex, texcoord) : vec4(1.0);
    if (use_pal_override) texture_color = vec4(pal_override_color.rgb, texture_color.a);
    else if (use_flat) texture_color.rgb = flat_color;
    texture_color.rgb = srgb_to_linear(texture_color.rgb);

    vec4 result;
    if (!use_texture) {
        result = use_override ? vec4(srgb_to_linear(override_color.rgb), override_color.a)
            : vec4(surface_color, mat_alpha);
    }
    else if (mat_mode == 1) {
        result = vec4(mix(surface_color, texture_color.rgb, texture_color.a), mat_alpha);
    }
    else if (mat_mode == 2) {
        vec3 toon = srgb_to_linear(toon_table[int(clamp(surface_color.r, 0.0, 1.0) * 31.0)]);
        result = vec4(texture_color.rgb * surface_color.r + toon,
            mat_alpha * texture_color.a);
    }
    else {
        result = vec4(surface_color * texture_color.rgb,
            mat_alpha * texture_color.a);
    }
    if (!use_texture && mat_mode == 2) {
        result.rgb = srgb_to_linear(toon_table[int(clamp(surface_color.r, 0.0, 1.0) * 31.0)]);
    }
    if (use_override && use_texture) {
        result.rgb = srgb_to_linear(override_color.rgb);
        result.a *= override_color.a;
    }
    if (cel_bands > 0) result.rgb = cel_shade(result.rgb);

    vec3 material_emission = use_emissive_map
        ? srgb_to_linear(texture(emissive_tex, texcoord).rgb)
            * enhanced_emission.rgb * max(enhanced_emission.a, 0.0)
        : srgb_to_linear(emission);
    result.rgb += material_emission;

    if (alpha_test == 1 && result.a != 1.0) discard;
    if (alpha_test == 2 && result.a >= 1.0) discard;
    frag_color = result;
}
";

        public static string FullscreenVertexShader { get; } = @"#version 300 es
precision highp float;
layout(location = 0) in vec2 a_position;
layout(location = 3) in vec3 a_texcoord;
out vec2 texcoord;
void main()
{
    gl_Position = vec4(a_position, 0.0, 1.0);
    texcoord = a_texcoord.xy;
}
";

        public static string ToneMapFragmentShader { get; } = @"#version 300 es
precision highp float;

uniform sampler2D scene_tex;
uniform sampler2D color_grade_lut;
uniform float exposure;
uniform bool apply_tone_map;
uniform float color_grade_strength;
uniform bool use_color_grade_lut;
uniform bool apply_output_transfer;

in vec2 texcoord;
out vec4 frag_color;

vec3 tone_map_aces(vec3 linear_hdr)
{
    vec3 x = max(linear_hdr * exposure, vec3(0.0));
    return clamp((x * (2.51 * x + 0.03))
        / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

vec3 sample_color_grade_lut(vec3 display_linear)
{
    const float dimension = 16.0;
    const float texture_width = 256.0;
    float blue = display_linear.b * (dimension - 1.0);
    float lower_slice = floor(blue);
    float upper_slice = min(lower_slice + 1.0, dimension - 1.0);
    float pixel_y = display_linear.g * (dimension - 1.0) + 0.5;
    vec2 lower_uv = vec2((lower_slice * dimension
        + display_linear.r * (dimension - 1.0) + 0.5) / texture_width,
        pixel_y / dimension);
    vec2 upper_uv = vec2((upper_slice * dimension
        + display_linear.r * (dimension - 1.0) + 0.5) / texture_width,
        pixel_y / dimension);
    return mix(texture(color_grade_lut, lower_uv).rgb,
        texture(color_grade_lut, upper_uv).rgb, blue - lower_slice);
}

float linear_to_srgb_component(float value)
{
    value = clamp(value, 0.0, 1.0);
    return value <= 0.0031308 ? value * 12.92
        : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
}

vec3 linear_to_srgb(vec3 value)
{
    return vec3(linear_to_srgb_component(value.r),
        linear_to_srgb_component(value.g), linear_to_srgb_component(value.b));
}

void main()
{
    vec4 source = texture(scene_tex, texcoord);
    vec3 display_linear = apply_tone_map
        ? tone_map_aces(source.rgb) : clamp(source.rgb, 0.0, 1.0);
    if (use_color_grade_lut) {
        vec3 graded = sample_color_grade_lut(display_linear);
        display_linear = mix(display_linear, graded,
            clamp(color_grade_strength, 0.0, 1.0));
    }
    vec3 output_color = apply_output_transfer
        ? linear_to_srgb(display_linear) : display_linear;
    frag_color = vec4(output_color, clamp(source.a, 0.0, 1.0));
}
";

        public static string CompositionVertexShader { get; } = @"#version 300 es
precision highp float;
layout(location = 0) in vec2 a_position;
layout(location = 1) in vec4 a_color;
layout(location = 3) in vec3 a_texcoord;
out vec2 texcoord;
out vec4 hud_color;
void main()
{
    gl_Position = vec4(a_position, 0.0, 1.0);
    texcoord = a_texcoord.xy;
    hud_color = a_color;
}
";

        // Authored HUD textures, vertex colours and fades are display-referred.
        // Decode them before source-over blending into the display-linear target;
        // ToneMapFragmentShader performs the sole output transfer afterward.
        public static string CompositionFragmentShader { get; } = @"#version 300 es
precision highp float;
uniform sampler2D tex;
uniform sampler2D mask;
uniform float alpha;
uniform bool use_mask;
uniform float view_width;
uniform float view_height;
uniform vec4 fade_color;
uniform bool use_hud_vertex_color;
uniform bool use_hud_texture;
in vec2 texcoord;
in vec4 hud_color;
out vec4 frag_color;

float srgb_to_linear_component(float value)
{
    value = clamp(value, 0.0, 1.0);
    return value <= 0.04045 ? value / 12.92
        : pow((value + 0.055) / 1.055, 2.4);
}
vec3 srgb_to_linear(vec3 value)
{
    return vec3(srgb_to_linear_component(value.r),
        srgb_to_linear_component(value.g), srgb_to_linear_component(value.b));
}
void main()
{
    vec4 result;
    if (use_hud_vertex_color) {
        vec4 sampled = use_hud_texture ? texture(tex, texcoord) : vec4(1.0);
        result = vec4(srgb_to_linear(sampled.rgb) * srgb_to_linear(hud_color.rgb),
            sampled.a * hud_color.a * alpha);
    }
    else if (fade_color.a > 0.0) {
        result = vec4(srgb_to_linear(fade_color.rgb), fade_color.a);
    }
    else {
        vec4 sampled = texture(tex, texcoord);
        float mask_alpha = use_mask
            ? texture(mask, gl_FragCoord.xy / vec2(view_width, view_height)).a : 1.0;
        result = vec4(srgb_to_linear(sampled.rgb), sampled.a * mask_alpha * alpha);
    }
    frag_color = result;
}
";
    }
}
