using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>Known material fields; atomic validation reuses the existing authoring validator and EditMaterial history writer.</summary>
public static class MapMaterialPropertySchema
{
    private sealed record Binding(MapObjectPropertyField Field,object Value,Action<MapMaterial,object>? Set);
    public static IReadOnlyList<MapObjectPropertyField> Read(MapDefinition definition,int index)
        =>Array.AsReadOnly(Build(Material(definition,index)).Select(b=>b.Field).ToArray());
    private static MapMaterial Material(MapDefinition definition,int index)
        =>index>=0&&index<definition.Materials.Count?definition.Materials[index]:throw new ArgumentOutOfRangeException(nameof(index));
    public static void Apply(MapDocument document,int index,IReadOnlyDictionary<string,string> patch)
    {
        var original=Material(document.Project.Definition,index);var bindings=Build(original).ToDictionary(b=>b.Field.Id,StringComparer.Ordinal);
        var changed=new Dictionary<string,object>(StringComparer.Ordinal);
        foreach(var pair in patch)
        {
            if(!bindings.TryGetValue(pair.Key,out var binding))throw new FormatException("Unknown material property.");
            object value=Parse(binding.Field,pair.Value);if(!Equals(value,binding.Value))changed[pair.Key]=value;
        }
        if(changed.Count==0)return;
        var candidate=JsonSerializer.Deserialize<MapMaterial>(JsonSerializer.Serialize(original))!;
        bool enabled=changed.TryGetValue("animation.enabled",out var state)?(bool)state:original.Animation!=null;
        if(!enabled&&changed.Keys.Any(k=>k.StartsWith("animation.",StringComparison.Ordinal)&&k!="animation.enabled"))
            throw new FormatException("Enable animation before applying its fields.");
        candidate.Animation=enabled?candidate.Animation??new():null;
        foreach(var pair in changed)bindings[pair.Key].Set?.Invoke(candidate,pair.Value);
        Validate(document.Project.Definition,index,candidate);
        document.EditMaterial(index,value=>Copy(candidate,value));
    }
    private static void Copy(MapMaterial source,MapMaterial destination)
    {
        destination.Name=source.Name;destination.SourceMaterial=source.SourceMaterial;destination.TexScale=source.TexScale;
        destination.Alpha=source.Alpha;destination.TwoSided=source.TwoSided;destination.Animation=source.Animation;
    }
    private static void Validate(MapDefinition definition,int index,MapMaterial candidate)
    {
        // A detached material-only definition keeps validation CPU-only and uses
        // the compiler's existing animation/group/name/budget rules directly.
        Guid validationId=Guid.NewGuid();var checkedMaterial=JsonSerializer.Deserialize<MapMaterial>(JsonSerializer.Serialize(candidate))!;
        checkedMaterial.Id=validationId;
        var scratch=new MapDefinition {Name="MATERIAL_CHECK",Materials=definition.Materials.Select((m,i)=>i==index?checkedMaterial:m).ToList(),Assets=definition.Assets};
        var materialIds=scratch.Materials.Select(m=>m.Id).ToHashSet();
        bool Relevant(MapDiagnostic d)=>d.Severity==MapDiagnosticSeverity.Error&&(d.ObjectId is {} id&&materialIds.Contains(id)
            ||d.Message.StartsWith("Animated material",StringComparison.Ordinal));
        string Key(MapDiagnostic d)=>d.Code+"|"+d.ObjectId+"|"+d.Message;
        var candidateErrors=MapValidator.Validate(scratch,checkSources:false).Diagnostics.Where(Relevant).ToArray();
        var original=JsonSerializer.Deserialize<MapMaterial>(JsonSerializer.Serialize(definition.Materials[index]))!;original.Id=validationId;scratch.Materials[index]=original;
        var existing=MapValidator.Validate(scratch,checkSources:false).Diagnostics.Where(Relevant).GroupBy(Key).ToDictionary(g=>g.Key,g=>g.Count());
        foreach(var error in candidateErrors)
        {string key=Key(error);if(existing.GetValueOrDefault(key)>0){existing[key]--;continue;}throw new FormatException(error.Message);}
    }
    private static object Parse(MapObjectPropertyField field,string text)
    {
        if(text==null||text.Length>field.MaxLength||text.Any(char.IsControl))throw new FormatException(field.Label+" exceeds its supported text format.");
        if(field.Id=="alpha")
        {if(text.Length==0)return "";if(!int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out int alpha)||alpha is <0 or >31)throw new FormatException("Alpha must be blank or 0–31.");return alpha.ToString(CultureInfo.InvariantCulture);}
        if(field.Kind==NativeMapPropertyKind.Text)return text;
        if(field.Kind==NativeMapPropertyKind.Boolean){if(bool.TryParse(text,out bool flag))return flag;throw new FormatException(field.Label+" must be true or false.");}
        double numeric;object result;
        if(field.Kind==NativeMapPropertyKind.Integer){if(!int.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int n))throw new FormatException(field.Label+" must be an invariant integer.");numeric=n;result=n;}
        else{if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float n)||!float.IsFinite(n))throw new FormatException(field.Label+" must be a finite invariant number.");numeric=n;result=n;}
        if(field.Min is {} min&&numeric<min||field.Max is {} max&&numeric>max)throw new FormatException(field.Label+" is outside supported bounds.");return result;
    }
    private static List<Binding> Build(MapMaterial material)
    {
        var fields=new List<Binding>();
        void Add(string id,string label,NativeMapPropertyKind kind,object value,Action<MapMaterial,object>? set=null,double? min=null,double? max=null,int length=64)
        {string text=value is bool b?b?"true":"false":Convert.ToString(value,CultureInfo.InvariantCulture)??"";fields.Add(new(new(id,label,kind,text,length,min,max),value,set));}
        void Number(string id,string label,float value,Action<MapMaterial,float> set,double min,double max)=>Add(id,label,NativeMapPropertyKind.Number,value,(m,v)=>set(m,(float)v),min,max);
        void Integer(string id,string label,int value,Action<MapMaterial,int> set,int min,int max)=>Add(id,label,NativeMapPropertyKind.Integer,value,(m,v)=>set(m,(int)v),min,max);
        Add("name","Material name",NativeMapPropertyKind.Text,material.Name,(m,v)=>m.Name=(string)v,length:256);
        Integer("sourceMaterial","Source room material index",material.SourceMaterial,(m,v)=>m.SourceMaterial=v,0,65535);
        Number("texScale","Texels per world unit",material.TexScale,(m,v)=>m.TexScale=v,.001,65536);
        Add("alpha","Alpha (blank = inherit, 0–31)",NativeMapPropertyKind.Text,material.Alpha?.ToString(CultureInfo.InvariantCulture)??"",(m,v)=>m.Alpha=(string)v==""?null:int.Parse((string)v,CultureInfo.InvariantCulture),length:2);
        Add("twoSided","Two-sided surface",NativeMapPropertyKind.Boolean,material.TwoSided,(m,v)=>m.TwoSided=(bool)v);
        Add("animation.enabled","Enable animation",NativeMapPropertyKind.Boolean,material.Animation!=null);
        var animation=material.Animation??new();
        float Axis(float[]? vector,int axis,float fallback)=>vector is {Length:2}?vector[axis]:fallback;
        for(int axis=0;axis<2;axis++)
        {
            int a=axis;string suffix=axis==0?".x":".y";
            Number("animation.scroll"+suffix,"Scroll tiles/sec "+(axis==0?"X":"Y"),Axis(animation.UvScroll,axis,0),(m,v)=>m.Animation!.UvScroll[a]=v,-MapUvAnimation.MaxScrollSpeed,MapUvAnimation.MaxScrollSpeed);
            Number("animation.scale"+suffix,"UV base scale "+(axis==0?"X":"Y"),Axis(animation.UvScale,axis,1),(m,v)=>m.Animation!.UvScale[a]=v,.01,MapUvAnimation.MaxScale);
            Number("animation.pulse"+suffix,"UV scale pulse "+(axis==0?"X":"Y"),Axis(animation.UvScalePulse,axis,0),(m,v)=>m.Animation!.UvScalePulse[a]=v,-MapUvAnimation.MaxScale,MapUvAnimation.MaxScale);
        }
        Number("animation.rotation","Rotation degrees/sec",animation.UvRotationDegreesPerSecond,(m,v)=>m.Animation!.UvRotationDegreesPerSecond=v,-MapUvAnimation.MaxRotationSpeed,MapUvAnimation.MaxRotationSpeed);
        Number("animation.emissive","Emissive intensity",animation.EmissiveIntensity,(m,v)=>m.Animation!.EmissiveIntensity=v,0,1);
        Number("animation.emissivePulse","Emissive pulse",animation.EmissivePulse,(m,v)=>m.Animation!.EmissivePulse=v,-1,1);
        Integer("animation.loop","Loop frames (native 30 Hz)",animation.LoopFrames,(m,v)=>m.Animation!.LoopFrames=v,MapUvAnimation.MinLoopFrames,MapUvAnimation.MaxLoopFrames);
        Integer("animation.phase","Phase frames",animation.PhaseFrames,(m,v)=>m.Animation!.PhaseFrames=v,0,MapUvAnimation.MaxLoopFrames-1);
        Integer("animation.hold","Flipbook hold frames",animation.FlipbookHoldFrames,(m,v)=>m.Animation!.FlipbookHoldFrames=v,1,MapUvAnimation.MaxLoopFrames);
        return fields;
    }
}
