#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Text;

namespace MphRead.Mods.StudioRendering;

/// <summary>Creator graphs consume the authoritative generated World material functions.</summary>
internal static class CreatorMaterialShader
{
    internal static string Functions { get; } = Read();
    private static string Read()
    {
        using var stream = typeof(CreatorMaterialShader).Assembly.GetManifestResourceStream("MphRead.Mods.Render.Generated.World.fragment.wgsl")
            ?? throw new InvalidOperationException("The shared World material shader is absent.");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();
        var output = new StringBuilder();
        foreach (string function in new[] { "mapped_normal", "physical_to_linear", "physical_to_srgb", "physical_direct", "physical_material", "apply_material_lighting" })
        {
            int start = source.IndexOf("fn " + function + "(", StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("The shared World shader lacks creator material function " + function + ".");
            int cursor = source.IndexOf('{',start), depth=0;
            do { if(source[cursor]=='{')depth++;else if(source[cursor]=='}')depth--;cursor++; } while(depth>0 && cursor<source.Length);
            output.AppendLine(source[start..cursor]);
        }
        // Replace only the World uniform bindings with the creator graph's
        // explicit camera and canonical preview-light constants. Every normal,
        // roughness/ORM, BRDF and color-space equation stays in the same source.
        return output.ToString()
            .Replace("global._prime_advanced_materials","1i")
            .Replace("global._prime_use_normal_map","i32(p.features.y)")
            .Replace("global._prime_use_specular_map","i32(p.features.z)")
            .Replace("global._prime_use_emissive_map","i32(p.features.w)")
            .Replace("global._prime_use_light","i32(p.features.x)")
            .Replace("global._prime_use_texture","i32(p.uv.z)")
            .Replace("global._prime_use_override","0i")
            .Replace("global._prime_use_pal_override","0i")
            .Replace("global._prime_use_flat","0i")
            .Replace("global.mat_mode","0i")
            .Replace("global.view_mtx","p.view")
            .Replace("global.light1vec","normalize(vec3<f32>(0.3f,-1f,0.2f))")
            .Replace("global.light2vec","normalize(vec3<f32>(-0.3f,0.4f,-0.2f))")
            .Replace("global.light1col","vec3<f32>(0.85f)")
            .Replace("global.light2col","vec3<f32>(0.3f)")
            .Replace("global.diffuse","vec3<f32>(1f)")
            .Replace("global.ambient","vec3<f32>(0.35f)")
            .Replace("global.specular","vec3<f32>(0f)")
            .Replace("global.emissive_intensity","1f")
            .Replace("global.emission","vec3<f32>(0f)");
    }
}
#endif
