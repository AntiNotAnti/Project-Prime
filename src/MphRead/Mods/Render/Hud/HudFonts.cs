using System;
using System.IO;
namespace MphRead.Mods.Render.Hud;
public static class HudFonts
{
    public sealed record Entry(string Id,string Name,string Family,string Category,bool Italic,int Weight);
    public const int AtlasWidth=1792, AtlasHeight=672, CellSize=112, PaddingX=24, PaddingY=16;
    public static readonly Entry[] All = {
        new("font00","Rajdhani SemiBold","Rajdhani","Modern / Classic",false,600),
        new("font01","Rajdhani Bold","Rajdhani","Modern / Classic",false,700),
        new("font02","JetBrains Mono Regular","JetBrains Mono","Modern / Classic",false,400),
        new("font03","JetBrains Mono Bold","JetBrains Mono","Modern / Classic",false,700),
        new("font04","DejaVu Sans","DejaVu Sans","Modern / Classic",false,400),
        new("font05","DejaVu Sans Bold","DejaVu Sans","Modern / Classic",false,700),
        new("font06","DejaVu Sans Condensed","DejaVu Sans Condensed","Modern / Classic",false,400),
        new("font07","DejaVu Sans Mono","DejaVu Sans Mono","Modern / Classic",false,400),
        new("font08","DejaVu Serif","DejaVu Serif","Modern / Classic",false,400),
        new("font09","Liberation Sans","Liberation Sans","Modern / Classic",false,400),
        new("font10","Liberation Sans Narrow","Liberation Sans Narrow","Modern / Classic",false,400),
        new("font11","Liberation Mono","Liberation Mono","Modern / Classic",false,400),
        new("font12","Liberation Serif","Liberation Serif","Modern / Classic",false,400),
        new("font13","Carlito","Carlito","Modern / Classic",false,400),
        new("font14","Caladea","Caladea","Modern / Classic",false,400),
        new("font15","Noto Sans Regular","Noto Sans","Modern / Classic",false,400),
        new("font16","Linux Biolinum G Regular","Linux Biolinum G","Modern / Classic",false,400),
        new("font17","Linux Libertine G Regular","Linux Libertine G","Modern / Classic",false,400),
        new("font18","Rubik Italic","Rubik","Italic",true,400),
        new("font19","Rubik Bold Italic","Rubik","Italic",true,700),
        new("font20","Liberation Sans Narrow Italic","Liberation Sans Narrow","Italic",true,400),
        new("font21","Liberation Mono Italic","Liberation Mono","Italic",true,400),
        new("font22","Caladea Italic","Caladea","Italic",true,400),
        new("font23","Linux Libertine G Italic","Linux Libertine G","Italic",true,400),
        new("font24","Lobster Regular","Lobster","Script / Calligraphy",false,400),
        new("font25","Great Vibes Regular","Great Vibes","Script / Calligraphy",false,400),
        new("font26","Allura Regular","Allura","Script / Calligraphy",false,400),
        new("font27","Pacifico Regular","Pacifico","Script / Calligraphy",false,400),
        new("font28","Cinzel Decorative Regular","Cinzel Decorative","Decorative serif",false,400),
        new("font29","UnifrakturMaguntia","UnifrakturMaguntia","Gothic / Blackletter",false,400),
        new("font30","Black Ops One Regular","Black Ops One","Decorative display",false,400),
        new("font31","Monoton","Monoton","Decorative display",false,400),
        new("font32","Bungee Shade Regular","Bungee Shade","Decorative display",false,400),
        new("font33","Audiowide Regular","Audiowide","Technical",false,400)
    };
    public static int Index(string? id) => Array.FindIndex(All,f=>f.Id==id);
    public sealed record Atlas(float[] Advances, byte[] Alpha);
    private static readonly Atlas?[] Cache=new Atlas?[All.Length];
    public static Atlas Get(string id)
    {
        int index=Math.Max(0,Index(id));
        if(Cache[index] is {} cached)return cached;
        return Cache[index] = HudAssetReader.ReadAtlas(
            "MphRead.Assets.HudFonts." + All[index].Id + ".bin", AtlasWidth, AtlasHeight);
    }
}
