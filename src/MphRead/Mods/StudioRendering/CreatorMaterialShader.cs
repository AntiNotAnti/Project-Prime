#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Text;
using System.Globalization;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.StudioRendering;

/// <summary>Creator graphs consume the authoritative generated World material functions.</summary>
internal static class CreatorMaterialShader
{
    internal static string Functions { get; } = Read();
    private static string Read()
    {
        string source = Resource("World");
        var output = new StringBuilder();
        foreach (string function in new[] { "mapped_normal", "physical_to_linear", "physical_to_srgb", "physical_direct", "physical_material", "apply_material_lighting" })
        {
            output.AppendLine(Function(source,function));
        }
        output.AppendLine(Function(Resource("PostProcess"),"directional_shadow"));
        int fog=source.IndexOf("= global._prime_fog_enable;",StringComparison.Ordinal);
        if(fog<0)throw new InvalidOperationException("The shared World shader lacks its canonical fog block.");
        int fogStart=source.LastIndexOf("let ",fog,StringComparison.Ordinal);
        output.AppendLine("fn creator_fog(color: vec4<f32>, clipDepth: f32) -> vec4<f32> { var col_2=color; var depth=0f; var density=0f; let gl_FragCoord=vec4<f32>(0f,0f,clipDepth,1f);");
        output.AppendLine(Block(source,fogStart));output.AppendLine("return col_2; }");
        output.Append("fn terrain_color(index: u32) -> vec3<f32> { var colors=array<vec3<f32>,12>(");
        for(int i=0;i<12;i++)
        {if(i>0)output.Append(',');var color=MapViewportDiagnostics.TerrainColor((Terrain)i);output.Append(Vector(color));}
        output.AppendLine("); return colors[min(index,11u)]; }");
        var low=MapViewportDiagnostics.DensityColor(MapViewportDiagnostics.TargetTexelsPerUnit/4);
        var middle=MapViewportDiagnostics.DensityColor(MapViewportDiagnostics.TargetTexelsPerUnit);
        var high=MapViewportDiagnostics.DensityColor(MapViewportDiagnostics.TargetTexelsPerUnit*4);
        output.AppendLine("fn density_color(density: f32) -> vec3<f32> { let t=clamp(log2(max(density,0.0001f)/"+Float(MapViewportDiagnostics.TargetTexelsPerUnit)+")*0.25f+0.5f,0f,1f); if(t<0.5f){return mix("+Vector(low)+","+Vector(middle)+",t*2f);} return mix("+Vector(middle)+","+Vector(high)+",(t-0.5f)*2f); }");
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
            .Replace("global.light1vec","p.light1vec.xyz")
            .Replace("global.light2vec","p.light2vec.xyz")
            .Replace("global.light1col","p.light1col.xyz")
            .Replace("global.light2col","p.light2col.xyz")
            .Replace("global.diffuse","vec3<f32>(1f)")
            .Replace("global.ambient","vec3<f32>(0.35f)")
            .Replace("global.specular","vec3<f32>(0f)")
            .Replace("global.emissive_intensity","1f")
            .Replace("global.emission","vec3<f32>(0f)")
            .Replace("global._prime_fog_enable","i32(p.settings.x)")
            .Replace("global.fog_min","p.fogRange.x")
            .Replace("global.fog_max","p.fogRange.y")
            .Replace("global.fog_color","p.fogColor")
            .Replace("global.shadow_enabled","i32(p.settings.y)")
            .Replace("global.shadow_view","p.shadowView")
            .Replace("global.shadow_projection","p.shadowProjection")
            .Replace("global.shadow_light_dir","p.light1vec.xyz")
            .Replace("global.shadow_texel","vec2<f32>(p.fogRange.z)")
            .Replace("global.inv_view","mat4x4<f32>(vec4<f32>(1f,0f,0f,0f),vec4<f32>(0f,1f,0f,0f),vec4<f32>(0f,0f,1f,0f),vec4<f32>(0f,0f,0f,1f))");
    }
    private static string Resource(string name)
    {
        using var stream=typeof(CreatorMaterialShader).Assembly.GetManifestResourceStream("MphRead.Mods.Render.Generated."+name+".fragment.wgsl")
            ?? throw new InvalidOperationException("The shared "+name+" shader is absent.");
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    private static string Function(string source,string function)
    {
        int start=source.IndexOf("fn "+function+"(",StringComparison.Ordinal);
        if(start<0)throw new InvalidOperationException("The shared shader lacks function "+function+".");return Block(source,start);
    }
    private static string Block(string source,int start)
    {
        int cursor=source.IndexOf('{',start),depth=0;
        if(cursor<0)throw new InvalidOperationException("The shared shader has an incomplete function/block.");
        do{if(source[cursor]=='{')depth++;else if(source[cursor]=='}')depth--;cursor++;}while(depth>0&&cursor<source.Length);
        if(depth!=0)throw new InvalidOperationException("The shared shader has an unbalanced function/block.");return source[start..cursor];
    }
    private static string Float(float value)=>value.ToString("R",CultureInfo.InvariantCulture)+"f";
    private static string Vector(System.Numerics.Vector3 value)=>"vec3<f32>("+Float(value.X)+","+Float(value.Y)+","+Float(value.Z)+")";
}
#endif
