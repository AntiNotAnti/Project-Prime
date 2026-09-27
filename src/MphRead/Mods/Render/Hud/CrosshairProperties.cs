using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace MphRead.Mods.Render.Hud;

/// <summary>Editor/load-time metadata. Never used in a drawing or simulation loop.</summary>
public sealed record HudPropertyDescriptor(string Id,string Name,string Category,Type Type,float Min,float Max,float Step,PropertyInfo Property)
{
    public object? Get(CrosshairProfile profile) => Property.GetValue(profile);
    public void Set(CrosshairProfile profile,object value)
    {
        Property.SetValue(profile,value);
        if (profile.OverrideProperties != null && !profile.OverrideProperties.Contains(Id))
            profile.OverrideProperties=profile.OverrideProperties.Append(Id).ToArray();
    }
    public void Reset(CrosshairProfile profile,CrosshairProfile defaults)
    {
        Property.SetValue(profile,Property.GetValue(defaults));
        if (profile.OverrideProperties!=null) profile.OverrideProperties=profile.OverrideProperties.Where(id=>id!=Id).ToArray();
    }
}
public static class CrosshairProperties
{
    public static readonly string[] PartNames={nameof(CrosshairProfile.DotStyle),nameof(CrosshairProfile.InnerStyle),nameof(CrosshairProfile.OuterStyle),nameof(CrosshairProfile.RingStyle),nameof(CrosshairProfile.BracketStyle)};
    public static void MarkPart(CrosshairProfile profile,string id)
    { if(profile.OverrideProperties!=null && !profile.OverrideProperties.Contains(id)) profile.OverrideProperties=profile.OverrideProperties.Append(id).ToArray(); }
    public static readonly HudPropertyDescriptor[] All = typeof(CrosshairProfile).GetProperties()
        .Where(p=>p.PropertyType==typeof(bool)||p.PropertyType==typeof(float)||p.PropertyType==typeof(int)||p.PropertyType==typeof(string)||p.PropertyType.IsEnum)
        .Select(p=>
        {
            string category=p.Name.StartsWith("Dot") ? "Dot" : p.Name.StartsWith("Outer") ? "Outer" : p.Name.StartsWith("Bracket") ? "Brackets"
                : p.Name.Contains("Ring") || p.Name is "Radius" or "Segments" ? "Ring" : p.Name.StartsWith("Outline") ? "Outline"
                : p.Name is "Inner" or "LengthX" or "LengthY" or "Gap" or "Thickness" or "TStyle" ? "Inner" : "General";
            float min=p.Name is "Scale" or "DotSize" or "Thickness" or "RingThickness" or "BracketThickness" ? .1f : p.Name=="Segments" ? 12 : 0;
            float max=p.Name.Contains("Opacity") ? 1 : p.Name=="Scale" ? 8 : p.Name=="Segments" ? 128 : p.Name.Contains("Thickness") || p.Name=="Outline" ? 20 : 100;
            string name=System.Text.RegularExpressions.Regex.Replace(p.Name,"([a-z])([A-Z])","$1 $2");
            return new HudPropertyDescriptor(p.Name,name,category,p.PropertyType,min,max,max<=8 ? .1f : 1,p);
        }).ToArray();

    public static CrosshairProfile Resolve(CrosshairProfile defaults,CrosshairProfile? custom)
    {
        if(custom==null) return defaults;
        if(custom.OverrideProperties==null) return custom;
        var root=JsonSerializer.SerializeToNode(defaults)!.AsObject();
        var overrides=JsonSerializer.SerializeToNode(custom)!.AsObject();
        foreach(string id in custom.OverrideProperties)
            if(Array.Exists(All,p=>p.Id==id) || PartNames.Contains(id)) root[id]=overrides[id]?.DeepClone();
        root[nameof(CrosshairProfile.OverrideProperties)]=null;
        var result=root.Deserialize<CrosshairProfile>()!; result.Validate(); return result;
    }
}

public sealed class CrosshairPartStyle
{
    public bool CustomColor { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public float Opacity { get; set; } = 1;
    public float Outline { get; set; } = -1;
    internal void Validate()
    { Color=HudColor.Normalize(Color); Opacity=HudProfile.Clamp(Opacity,0,1,1); Outline=HudProfile.Clamp(Outline,-1,20,-1); }
}
