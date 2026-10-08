#if MPHREAD_RMLUI_POC
using System;
using System.IO;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
namespace MphRead.Mods.Launcher.RmlUi.Presenters;

internal static class RmlSplashPage
{
    internal static void Open(RmlUiHost host,RmlUiLauncherPages pages,TheatreImageCache? images=null)
    {
        pages.Suspend();
        var document=pages.Manager.OpenPage(new("splash","pages/start/splash.rml","splash_continue"));
        bool artwork=false;
        try
        {
            var assembly=typeof(RmlSplashPage).Assembly;
            using var stream=assembly.GetManifestResourceStream("ProjectPrime.Assets.Backgrounds.launcher-bg.png")
                ?? assembly.GetManifestResourceStream("MphRead.Assets.Backgrounds.launcher-bg.png");
            if(stream!=null)
            {
                string directory=Path.Combine(LauncherPrefs.Directory,"rmlui-thumbnail-cache");
                Directory.CreateDirectory(directory);
                string source=Path.Combine(directory,"splash-"+assembly.ManifestModule.ModuleVersionId+".png");
                if(!File.Exists(source)){using var output=File.Create(source);stream.CopyTo(output);}
                host.SetText(document,"image:splash_art",(images??new(maximumDimension:2048)).Load(source,default));
                artwork=true;
            }
        }
        catch(Exception error){DebugLog.Line("splash",error.Message);}
        host.SetBool(document,"visible:splash_art",artwork);
        host.SetBool(document,"visible:splash_fallback",!artwork);
        Layout(host,pages);
    }
    internal static void Layout(RmlUiHost host,RmlUiLauncherPages pages)
    {
        if(pages.Manager.PageKey!="splash")return;
        float width=host.FramebufferWidth,height=host.FramebufferHeight;
        float scale=Math.Min(width/1672f,height/941f),w=1672*scale,h=941*scale;
        host.SetText(pages.Manager.Page,"rect:splash_art",FormattableString.Invariant($"{(width-w)/2},{(height-h)/2},{w},{h}"));
    }
}
#endif
