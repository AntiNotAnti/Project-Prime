namespace MphRead.Mods.Render.Hud;

public static class HudPalettes
{
    public static readonly string[] Names={"Default","Blue / amber","High contrast"};
    public static void Apply(HudProfile p,int index)
    {
        foreach(var meter in new[] { p.Health,p.Ammo })
        {
            meter.FullColor=index==0 ? "#3DD951" : index==1 ? "#56B4E9" : "#FFFFFF";
            meter.WarningColor=index==0 ? "#FFAD19" : "#F0E442";
            meter.DangerColor=index==0 ? "#F22D2D" : index==1 ? "#D55E00" : "#FF00FF";
        }
        if(index!=0)
        {
            p.Crosshair.HealthColor=false; p.Crosshair.Color=index==1 ? "#56B4E9" : "#FFFFFF";
            p.Crosshair.Outline=2; p.Crosshair.OutlineColor="#000000";
        }
        if(index==2) p.ReduceTransparency=true;
        p.Mode=HudMode.Custom;
    }
}
