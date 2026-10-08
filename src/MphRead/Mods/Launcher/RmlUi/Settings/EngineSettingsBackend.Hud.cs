#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
internal sealed partial class EngineSettingsBackend
{
    private static IEnumerable<string> OverrideTargets()
    {
        for(int i=0;i<9;i++)yield return "/weaponCrosshairs/"+i;
        yield return "/zoomCrosshair";
    }
    private void RefreshHud()
    {
        _hudValues.Clear();
        var data=JsonNode.Parse(HudProfileStore.Serialize(HudProfiles.CopyCurrent()))!;
        foreach(var leaf in Leaves(data,""))_hudValues[leaf.Path]=LeafText(leaf.Value);
        foreach(string path in OverrideTargets())
        {
            _hudValues[path+"/$enabled"]=At(data,path)!=null?"true":"false";
            _hudValues[path+"/overrideProperties"]=OverrideText(At(data,path+"/overrideProperties"));
        }
    }
    private void AddHud()
    {
        foreach(string path in OverrideTargets())
            if(At(_hudTemplate,path)==null)Put(_hudTemplate,path,_hudTemplate["crosshair"]!.DeepClone());
        RefreshHud();
        Choice("legacy.CrosshairStyle",SettingsCategory.Display,"Crosshair type",()=>Crosshair.Style.ToString(),v=>Crosshair.Style=Enum.Parse<CrosshairStyle>(v),Names<CrosshairStyle>());
        Choice("legacy.CrosshairSize",SettingsCategory.Display,"Crosshair size",()=>Crosshair.Size.ToString(),v=>Crosshair.Size=Enum.Parse<CrosshairSize>(v),Names<CrosshairSize>());
        foreach(string target in OverrideTargets())
        {
            string path=target;
            Add("hudOverride."+path,SettingsCategory.Display,"HUD "+HudLabel(path)+" override",
                ()=>_hudValues[path+"/$enabled"],v=>_hudValues[path+"/$enabled"]=v,
                SettingsValueValidation.Boolean,SettingsValueKind.Boolean,new[]{"false","true"},help:"Enable a separate crosshair for this weapon or zoom.");
            Add("hud"+path+"/overrideProperties",SettingsCategory.Display,"HUD "+HudLabel(path)+" inherited properties",
                ()=>_hudValues[path+"/overrideProperties"],v=>_hudValues[path+"/overrideProperties"]=v,ValidateOverrides,
                SettingsValueKind.Structured,help:"all: separate crosshair; inherit: inherit every property; or comma separated property names.");
        }
        foreach(var leaf in Leaves(_hudTemplate,""))
        {
            string path=leaf.Path;
            if(path is "/schemaVersion" or "/basePreset")continue;
            JsonValue sample=leaf.Value;
            string[] choices=HudChoices(path);
            var kind=choices.Length>0?SettingsValueKind.Choice:sample.GetValueKind() switch {
                JsonValueKind.True or JsonValueKind.False=>SettingsValueKind.Boolean,
                JsonValueKind.Number=>SettingsValueKind.Number,_=>SettingsValueKind.Text};
            Add("hud"+path,SettingsCategory.Display,"HUD "+HudLabel(path),
                ()=>_hudValues.GetValueOrDefault(path,LeafText(sample)),v=>_hudValues[path]=v,
                v=>kind switch {
                    SettingsValueKind.Boolean=>SettingsValueValidation.Boolean(v),
                    SettingsValueKind.Number=>ValidateHudNumber(path,v),
                    SettingsValueKind.Choice=>SettingsValueValidation.Choice(v,choices),
                    _=>SettingsValueValidation.Text(v)},kind,choices,help:kind==SettingsValueKind.Number?HudRangeHelp(path):"");
        }
    }
    private static string HudLabel(string path)=>System.Text.RegularExpressions.Regex.Replace(path.Trim('/').Replace('/',' ').Replace("core.","").Replace("combat.",""),"([a-z])([A-Z])","$1 $2");
    private static string OverrideText(JsonNode? data)=>data is JsonArray array?array.Count==0?"inherit":string.Join(",",array.Select(v=>v!.GetValue<string>())):"all";
    private static string ValidateOverrides(string text)
    {
        text=SettingsValueValidation.Text(text).Trim();if(text is "all" or "inherit")return text;
        string[] names=text.Split(',').Select(n=>n.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if(names.Length==0||names.Any(n=>!CrosshairProperties.All.Any(d=>d.Id==n)&&!CrosshairProperties.PartNames.Contains(n)))
            throw new FormatException("Use all, inherit, or existing crosshair property names.");
        return string.Join(",",names);
    }
    private static (double Min,double Max) HudRange(string path)
    {
        string name=path[(path.LastIndexOf('/')+1)..];
        if(path.StartsWith("/crosshair/",StringComparison.Ordinal)||path.StartsWith("/weaponCrosshairs/",StringComparison.Ordinal)||path.StartsWith("/zoomCrosshair/",StringComparison.Ordinal))
        {
            string property=char.ToUpperInvariant(name[0])+name[1..];var descriptor=CrosshairProperties.All.FirstOrDefault(d=>d.Id==property);
            if(descriptor!=null&&descriptor.Type!=typeof(bool)&&descriptor.Type!=typeof(string)&&!descriptor.Type.IsEnum)return(descriptor.Min,descriptor.Max);
            return name=="outline"?(-1,20):(0,1);
        }
        return name switch {
            "offsetX"=>(-3840,3840),"offsetY"=>(-2160,2160),"safeArea"=>(0,.2),
            "textScale" or "iconScale"=>(.5,3),"layer"=>(0,3),
            _ when name.Contains("Opacity",StringComparison.OrdinalIgnoreCase)||name is "opacity" or "warning" or "danger"=>(0,1),
            "scale" or "globalScale" or "numberScale" or "gaugeScale"=>(.1,8),
            _=>(-1000000,1000000)};
    }
    private static string ValidateHudNumber(string path,string value){var range=HudRange(path);return SettingsValueValidation.Number(value,range.Min,range.Max);}
    private static string HudRangeHelp(string path){var range=HudRange(path);return $"{range.Min} to {range.Max}";}
    private static string[] HudChoices(string path)
    {
        string field=path[(path.LastIndexOf('/')+1)..];
        return field switch {
            "mode"=>Names<HudMode>(),"anchor"=>Names<HudAnchor>(),"visibility"=>Names<HudVisibility>(),
            "contexts"=>Enumerable.Range(0,16).Select(v=>((HudContext)v).ToString()).ToArray(),"dotShape"=>Names<CrosshairDotShape>(),
            "orientation"=>Names<HudRadarOrientation>(),"outOfRange"=>Names<HudRadarOutOfRangeMode>(),
            "elevation"=>Names<HudRadarElevationMode>(),"queue"=>Names<HudNotificationQueue>(),
            "shape"=>Names<HudHitMarkerShape>(),
            "style" when path.StartsWith("/radar/",StringComparison.Ordinal)=>Names<HudRadarStyle>(),
            _=>Array.Empty<string>()};
    }
    private HudProfile BuildHud(IReadOnlyDictionary<string,string> values)
    {
        JsonNode data=JsonNode.Parse(values["$hud"])!;
        foreach(string path in OverrideTargets())
        {
            bool enabled=bool.Parse(values["hudOverride."+path]);
            if(!enabled){Put(data,path,null);continue;}
            if(At(data,path)==null)Put(data,path,At(_hudTemplate,path)!.DeepClone());
            string overrides=ValidateOverrides(values["hud"+path+"/overrideProperties"]);
            Put(data,path+"/overrideProperties",overrides=="all"?null:overrides=="inherit"?new JsonArray():new JsonArray(overrides.Split(',').Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray()));
        }
        foreach(Field field in _fields.Where(f=>f.Definition.Id.StartsWith("hud/",StringComparison.Ordinal)))
        {
            string path=field.Definition.Id[3..];
            if(path.EndsWith("/overrideProperties",StringComparison.Ordinal)||!values.TryGetValue(field.Definition.Id,out string? text))continue;
            JsonNode? current=At(data,path);if(current==null)continue;
            JsonNode? value=current.GetValueKind() switch {
                JsonValueKind.Number=>JsonNode.Parse(text),JsonValueKind.True or JsonValueKind.False=>JsonValue.Create(bool.Parse(text)),
                _=>JsonValue.Create(text)};
            bool changed=LeafText((JsonValue)current)!=text;
            Put(data,path,value);
            string? target=OverrideTargets().FirstOrDefault(t=>path.StartsWith(t+"/",StringComparison.Ordinal));
            if(changed&&target!=null&&At(data,target+"/overrideProperties") is JsonArray inherited)
            {
                string property=path[(target.Length+1)..].Split('/')[0];property=char.ToUpperInvariant(property[0])+property[1..];
                if(!inherited.Any(n=>n!.GetValue<string>()==property))inherited.Add(property);
            }
        }
        var profile=HudProfileStore.Parse(data.ToJsonString());
        JsonNode normalized=JsonNode.Parse(HudProfileStore.Serialize(profile))!;
        foreach(Field field in _fields.Where(f=>f.Definition.Id.StartsWith("hud/",StringComparison.Ordinal)))
        {
            string path=field.Definition.Id[3..];var requested=At(data,path);var actual=At(normalized,path);
            if(!path.EndsWith("/overrideProperties",StringComparison.Ordinal)&&requested!=null&&!JsonNode.DeepEquals(requested,actual))
                throw new FormatException(field.Definition.Label+" is outside its allowed range or format.");
        }
        bool legacyChanged=values["legacy.CrosshairStyle"]!=values["$legacyStyle"]
            ||values["legacy.CrosshairSize"]!=values["$legacySize"];
        bool explicitHud=values.TryGetValue("$hudEdited",out string? edited)&&bool.Parse(edited);
        if(legacyChanged&&!explicitHud)profile.Crosshair=CrosshairProfile.FromLegacy(Enum.Parse<CrosshairStyle>(values["legacy.CrosshairStyle"]),Enum.Parse<CrosshairSize>(values["legacy.CrosshairSize"]));
        return profile;
    }
    internal HudProfile DraftHud(IReadOnlyDictionary<string,string> values)=>BuildHud(values);
    internal IReadOnlyDictionary<string,string> StageHud(IReadOnlyDictionary<string,string> values,HudProfile profile)
    {
        profile=profile.DeepClone();var result=new Dictionary<string,string>(values,StringComparer.Ordinal);
        var data=JsonNode.Parse(HudProfileStore.Serialize(profile))!;result["$hud"]=data.ToJsonString();
        result["$hudEdited"]="true";
        foreach(Field field in _fields.Where(f=>f.Definition.Id.StartsWith("hud/",StringComparison.Ordinal)))
        {
            string path=field.Definition.Id[3..];JsonNode? value=At(data,path);
            result[field.Definition.Id]=path.EndsWith("/overrideProperties",StringComparison.Ordinal)?OverrideText(value):LeafText((JsonValue)(value??At(_hudTemplate,path))!);
        }
        foreach(string path in OverrideTargets())result["hudOverride."+path]=At(data,path)!=null?"true":"false";
        return result;
    }
    private static string LeafText(JsonValue value)=>value.GetValueKind()==JsonValueKind.String?value.GetValue<string>():value.ToJsonString();
    private static IEnumerable<(string Path,JsonValue Value)> Leaves(JsonNode node,string path)
    {
        if(path.EndsWith("/overrideProperties",StringComparison.Ordinal))yield break;
        if(node is JsonValue value){yield return(path,value);yield break;}
        if(node is JsonObject obj)foreach(var property in obj)
            if(property.Value!=null)foreach(var leaf in Leaves(property.Value,path+"/"+property.Key.Replace("~","~0").Replace("/","~1")))yield return leaf;
        if(node is JsonArray array)for(int i=0;i<array.Count;i++)
            if(array[i]!=null)foreach(var leaf in Leaves(array[i]!,path+"/"+i))yield return leaf;
    }
    private static JsonNode? At(JsonNode data,string path)
    {
        JsonNode? current=data;
        foreach(string key in path.Split('/').Skip(1))
        {
            if(current==null)return null;
            current=current is JsonArray array?array[int.Parse(key,Invariant)]:current[key.Replace("~1","/").Replace("~0","~")];
        }
        return current;
    }
    private static void Put(JsonNode data,string path,JsonNode? value)
    {
        int split=path.LastIndexOf('/');JsonNode parent=split==0?data:At(data,path[..split])!;
        string key=path[(split+1)..].Replace("~1","/").Replace("~0","~");
        if(parent is JsonArray array)array[int.Parse(key,Invariant)]=value;else parent[key]=value;
    }
}
#endif
