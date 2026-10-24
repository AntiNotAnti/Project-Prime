using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Mods;
using MphRead.Mods.Render.Materials;

sealed partial class CheckWindow
{
    private int? _diagnosticScale;
    private TextureAssetQuality? _diagnosticTextureQuality;
    private bool? _diagnosticLighting;
    private bool _diagnosticMapHd;
    private bool _diagnosticAuthoredMaterials;
    private int _diagnosticShotStep=300;
    internal void ConfigureDiagnosticOptions(string[] args)
    {
        string Value(string key) => args[Array.IndexOf(args,key)+1];
        foreach(string key in new[]{"-render-scale","-texturequality","-lighting"})
            if(args.Contains(key) && Array.IndexOf(args,key)+1>=args.Length) throw new ArgumentException("Missing "+key+" value.");
        if(args.Contains("-render-scale"))
        {
            _diagnosticScale=int.Parse(Value("-render-scale"));
            if(_diagnosticScale<25 || _diagnosticScale>200) throw new ArgumentException("Render scale requires percent 25..200.");
        }
        if(args.Contains("-texturequality")) _diagnosticTextureQuality=Enum.Parse<TextureAssetQuality>(Value("-texturequality"),true);
        if(args.Contains("-lighting")) _diagnosticLighting=bool.Parse(Value("-lighting"));
        _diagnosticMapHd=args.Contains("-map-hd");
        _diagnosticAuthoredMaterials=args.Contains("-authoredmaterials");
        if(args.Contains("-shot-step"))
        {
            _diagnosticShotStep=int.Parse(Value("-shot-step"));
            if(_diagnosticShotStep<1 || _diagnosticShotStep>3600)throw new ArgumentException("Shot step requires 1..3600 simulation frames.");
        }
    }
    private void ApplyDiagnosticOptions()
    {
        if(_diagnosticScale is int scale) RenderOptions.ResolutionScale=scale;
        if(_diagnosticTextureQuality is TextureAssetQuality quality) RenderOptions.TextureQuality=quality;
        if(_diagnosticLighting is bool lighting) RenderOptions.Lighting=lighting;
        if(_diagnosticMapHd) RenderOptions.TextureReplacements=true;
        if(_diagnosticAuthoredMaterials)
        {
            RenderOptions.TextureReplacements=true;
            RenderOptions.AdvancedMaterials=true;
        }
    }
    private object? MaterialField(string name) => typeof(MphRead.Scene).GetField(name,
        BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(Scene);
    private int ModernMaterialBindingCount => (MaterialField("_modernTextureSampling") as System.Collections.IDictionary)?.Count ?? 0;
    private record WorldMaterialBinding(int binding,string key,string model);
    private WorldMaterialBinding[] AuthoredWorldBindings()
    {
        var witnesses=new List<WorldMaterialBinding>();
        var definition=MphRead.Mods.MapGen.CustomRooms.Definitions.FirstOrDefault(d=>d.Name.Equals(_room,StringComparison.OrdinalIgnoreCase));
        if(definition==null || MaterialField("_modernTextureSampling") is not IDictionary modern
            || MaterialField("_textureSources") is not IDictionary sources)return witnesses.ToArray();
        string prefix=$"map/{definition.MapId:N}/material/";
        foreach(DictionaryEntry entry in modern)
        {
            if(entry.Value is not ITuple sampling || sampling[0]?.ToString()!="World" || sampling[1]?.ToString()!="Albedo"
                || sources[entry.Key] is not ITuple source || source[0] is not MphRead.Model model)continue;
            MaterialAssetKey key=source[4] is MaterialAssetKey authored?authored:
                MaterialAssetKey.ForModel(model,(int)source[1]!, (int)source[2]!, (int)source[3]!);
            if(key.Value.StartsWith(prefix,StringComparison.Ordinal))witnesses.Add(new((int)entry.Key,key.Value,model.Name));
        }
        return witnesses.ToArray();
    }
    internal bool AuthoredMaterialsAccepted => !_diagnosticAuthoredMaterials ||
        (RenderOptions.TextureReplacements && RenderOptions.AdvancedMaterials
        && AuthoredWorldBindings().Length>0
        && (!RenderOptions.LinearColor || MaterialField("_sceneEncodedReady") is true));
    private object AuthoredMaterialsResult() => new {
        requested=_diagnosticAuthoredMaterials,passed=AuthoredMaterialsAccepted,
        hdEnabled=RenderOptions.TextureReplacements,advancedEnabled=RenderOptions.AdvancedMaterials,
        linearEnabled=RenderOptions.LinearColor,modernBindingCount=ModernMaterialBindingCount,
        authoredWorldBindings=AuthoredWorldBindings(),
        worldMaterialResidentBytes=MaterialField("_worldMaterialResidentBytes"),
        materialCompanionBindingCount=(MaterialField("_materialMaps") as System.Collections.IDictionary)?.Count ?? 0
    };
}
