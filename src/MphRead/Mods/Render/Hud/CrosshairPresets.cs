using System;
using System.Text;
using System.Text.Json.Nodes;
namespace MphRead.Mods.Render.Hud;

public static class CrosshairPresets
{
    public static readonly string[] Names = { "Cross", "Dot", "Cross + dot", "Circle", "Brackets", "Prime", "Precision", "Duel", "Imperialist", "Minimal Dot", "Ring Dot", "T-Cross", "Heavy" };
    public static CrosshairProfile Create(int index)
    {
        if (index < 5) return CrosshairProfile.FromLegacy((CrosshairStyle)Math.Clamp(index,0,4),CrosshairSize.Medium);
        var p=new CrosshairProfile { HealthColor=false, Color="#00FFFF", Outline=1 };
        switch(index)
        {
            case 5: p.Dot=true; p.DotSize=2; p.Gap=5; break;
            case 6: p.LengthX=p.LengthY=5; p.Thickness=1; p.Gap=3; break;
            case 7: p.LengthX=p.LengthY=6; p.Thickness=2; p.Gap=4; break;
            case 8: p.Inner=false; p.Dot=true; p.DotSize=2; p.Color="#FF4040"; break;
            case 9: p.Inner=false; p.Dot=true; p.DotSize=2; p.Outline=0; break;
            case 10: p.Inner=false; p.Dot=true; p.Ring=true; p.DotSize=2; break;
            case 11: p.TStyle=true; p.Thickness=2; break;
            case 12: p.Thickness=5; p.LengthX=p.LengthY=12; p.Outline=2; break;
        }
        return p;
    }
    public static string Share(CrosshairProfile crosshair)
    {
        var p=new HudProfile { Crosshair=crosshair }; p.Validate();
        string json=JsonNode.Parse(HudProfileStore.Serialize(p))!["crosshair"]!.ToJsonString();
        return "PPCH1:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
    public static CrosshairProfile Import(string code)
    {
        if (code.Length>16384 || !code.StartsWith("PPCH1:",StringComparison.Ordinal)) throw new FormatException("Expected a PPCH1 crosshair code.");
        string json=Encoding.UTF8.GetString(Convert.FromBase64String(code[6..]));
        var node=JsonNode.Parse(json,documentOptions:new System.Text.Json.JsonDocumentOptions { MaxDepth=8 });
        if (node is not JsonObject) throw new FormatException("Invalid crosshair code.");
        return HudProfileStore.Parse(new JsonObject { ["crosshair"]=node }.ToJsonString()).Crosshair;
    }
}
