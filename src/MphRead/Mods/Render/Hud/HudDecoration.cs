using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace MphRead.Mods.Render.Hud;
public sealed class HudDecorationStyle
{
    public bool MatchPalette { get; set; }=true;
    public string Color { get; set; }="#A2D3F0";
    public float Opacity { get; set; }=1;
    public float LineScale { get; set; }=1;
    [JsonIgnore] public System.Numerics.Vector3 Ink { get; private set; }
    public void Validate(string accent)
    {Color=HudColor.Normalize(Color);Opacity=HudProfile.Clamp(Opacity,0,1,1);LineScale=HudProfile.Clamp(LineScale,.1f,5,1);Ink=HudColor.Parse(MatchPalette?accent:Color);}
}
public sealed class HudDecorations
{
    public static readonly string[] Ids={"borders","background","controls","radar","weapons","points","health","boost"};
    public static readonly string[] Names={"Screen borders","Bottom background","Touchscreen controls","Radar frame","Weapon artwork","Points decoration","Health decoration","Boost / bombs"};
    public Dictionary<string,HudDecorationStyle> Styles {get;set;}=new();
    public HudDecorationStyle Get(string id){if(!Styles.TryGetValue(id,out var s))Styles[id]=s=new();return s;}
    public void Validate(string accent){Styles??=new();foreach(var id in Ids)Get(id).Validate(accent);}
}
