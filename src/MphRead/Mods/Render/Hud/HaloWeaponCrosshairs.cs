namespace MphRead.Mods.Render.Hud;

/// <summary>Optional presentation set. Existing profiles remain intact when it is selected.</summary>
public static class HaloWeaponCrosshairs
{
    public const string Theme = "Halo weapons";
    public static CrosshairProfile Create(int weapon, CrosshairProfile controls, bool zoom = false)
    {
        var p = new CrosshairProfile {
            Inner=false, HealthColor=false, Color="#78DBFF", Outline=1, Thickness=1.5f,
            Dot=true, DotSize=2, DotShape=CrosshairDotShape.Circle,
            Motion=controls.Motion, Scale=controls.Scale, Opacity=controls.Opacity,
            Enabled=controls.Enabled, WidthScale=controls.WidthScale, HeightScale=controls.HeightScale,
            ShotScale=1.08f, ChargeScale=1.15f, ZoomScale=1,
            MinimumVisibility=controls.MinimumVisibility
        };
        switch (weapon)
        {
            case 0: p.Ring=true; p.Radius=7; p.RingThickness=1.3f; break; // Battle Rifle
            case 1: p.Ring=true; p.Radius=8; p.RingThickness=1.5f; p.TStyle=true; p.Inner=true; p.LengthX=4; p.LengthY=3; p.Gap=10; p.Color="#88ED95"; break;
            case 2: p.Brackets=true; p.BracketCorner=12; p.BracketLength=5; p.BracketThickness=1.8f; p.DotSize=1.5f; p.Color="#FFCF74"; break; // SPNKr
            case 3: p.Ring=true; p.Radius=10; p.RingThickness=2; p.WidthScale*=1.25f; p.HeightScale*=.75f; p.Color="#FFAB74"; break;
            case 4: p.Inner=true; p.LengthX=p.LengthY=zoom?4:3; p.Gap=zoom?6:4; p.Thickness=1; p.DotSize=1.5f; p.Color="#B2F1FF"; p.ChargeScale=1; p.ShotScale=1; break;
            case 5: p.Brackets=true; p.BracketCorner=9; p.BracketLength=5; p.BracketThickness=1.3f; p.Rotation=45; p.Color="#EEA4FF"; break;
            case 6: p.Ring=true; p.Radius=11; p.RingThickness=2; p.DotSize=3; p.Color="#BEF16B"; break;
            case 7: p.Inner=true; p.LengthX=10; p.LengthY=4; p.Gap=2; p.Thickness=1; p.Dot=false; p.Color="#FFE7A2"; break;
            case 8: p.Inner=true; p.TStyle=true; p.LengthX=6; p.LengthY=10; p.Gap=8; p.Ring=true; p.Radius=5; p.RingThickness=1; p.Color="#FF7777"; break;
        }
        p.Validate(); return p;
    }
}
