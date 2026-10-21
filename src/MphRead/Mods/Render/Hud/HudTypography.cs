using System;
using System.Collections.Generic;
namespace MphRead.Mods.Render.Hud;
public sealed class HudTextStyle
{
    public bool Enabled { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool FontOnlyOverride { get; internal set; }
    public bool FitToScreen { get; set; }=true;
    public string Font { get; set; }="font00";
    public float Size { get; set; }=1;
    public string Color { get; set; }="#D9EDF8";
    public bool MatchPalette { get; set; }=true;
    public bool PreserveSemanticColors { get; set; }=true;
    public float Opacity { get; set; }=1;
    public float Outline { get; set; }
    public string OutlineColor { get; set; }="#06111F";
    [System.Text.Json.Serialization.JsonIgnore] public System.Numerics.Vector3 Ink { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] public System.Numerics.Vector3 OutlineInk { get; private set; }
    public void Validate()
    {
        if(HudFonts.Index(Font)<0)Font="font00";
        Size=HudProfile.Clamp(Size,.05f,6,1);Opacity=HudProfile.Clamp(Opacity,0,1,1);
        Outline=HudProfile.Clamp(Outline,0,5,0);Color=HudColor.Normalize(Color);OutlineColor=HudColor.Normalize(OutlineColor);
        Ink=HudColor.Parse(Color);OutlineInk=HudColor.Parse(OutlineColor);
    }
}
public sealed class HudTypography
{
    public static readonly string[] Ids={"core.health","core.ammo","match.score","match.timer","combat.killFeed","combat.kill","combat.assist","combat.notifications","touch.labels","core.weapons","core.radar","combat.opponent","match.objective","system.diagnostics","all.other","ui.body","ui.headings","ui.data","game.replay","match.gameOver"};
    public static readonly string[] Names={"Health","Ammunition","Points / score","Match timer","Killfeed","Kill Notification","Assist Notification","Other combat awards","Touchscreen labels / boost / bombs","Weapon labels","Radar labels","Opponent information","Objectives","Diagnostics","Other HUD lettering / scoreboards / results","Menus, settings and lobby text","Interface headings and titles","Interface numbers and data","Replay / killcam lettering","Game over lettering"};
    public int PaletteDefaultsVersion { get; set; }
    public bool UseGlobalFont { get; set; }
    public string GlobalFont { get; set; }="font00";
    public bool UseGameFont { get; set; }
    public string GameFont { get; set; }="font00";
    public bool UseInterfaceFont { get; set; }
    public string InterfaceFont { get; set; }="font00";
    [System.Text.Json.Serialization.JsonIgnore] private bool _runtimeResolved;
    public string? SharedFontFor(string id) => UseGlobalFont ? GlobalFont
        : id.StartsWith("ui.",StringComparison.Ordinal) ? (UseInterfaceFont?InterfaceFont:null)
        : UseGameFont?GameFont:null;
    public void ResolveForRuntime()
    {
        _runtimeResolved=true;
        foreach(var pair in Styles)
            if(SharedFontFor(pair.Key) is {} font) { pair.Value.FontOnlyOverride=!pair.Value.Enabled;pair.Value.Font=font;pair.Value.Enabled=true; }
    }
    public bool LinkKillAssist { get; set; }
    public Dictionary<string,HudTextStyle> Styles { get; set; }=new();
    public HudTextStyle Get(string id)
    {
        // Game-over inherits the existing general HUD/results style until customized.
        if(id=="match.gameOver" && _runtimeResolved && (!Styles.TryGetValue(id,out var result) || !result.Enabled || result.FontOnlyOverride)
            && Styles.TryGetValue("all.other",out var general) && general.Enabled && !general.FontOnlyOverride) return general;
        if(id=="combat.assist" && LinkKillAssist)id="combat.kill";
        if(_runtimeResolved && (id is "combat.kill" or "combat.assist")
            && (!Styles.TryGetValue(id,out var specific) || !specific.Enabled || specific.FontOnlyOverride)
            && Styles.TryGetValue("combat.notifications",out var shared) && shared.Enabled && !shared.FontOnlyOverride) return shared;
        if(!Styles.TryGetValue(id,out var style))Styles[id]=style=new();
        if(_runtimeResolved && SharedFontFor(id) is {} font) { if(!style.Enabled)style.FontOnlyOverride=true;style.Font=font;style.Enabled=true; }
        return style;
    }
    public void Validate() {
        Styles ??= new();
        if (PaletteDefaultsVersion < 1)
        {
        foreach (var id in new[] { "combat.kill", "combat.assist" })
        {
            var style = Get(id);
            if (!style.Enabled)
            {
                style.Enabled = true; style.MatchPalette = true; style.PreserveSemanticColors = false;
            }
        }
        PaletteDefaultsVersion = 1;
        }
 if(HudFonts.Index(GlobalFont)<0)GlobalFont="font00";if(HudFonts.Index(GameFont)<0)GameFont="font00";if(HudFonts.Index(InterfaceFont)<0)InterfaceFont="font00";Styles??=new();foreach(var id in Ids)Get(id).Validate(); }
}
