using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.Gui;

internal static class HudStudioPreview
{
    public static int ExportRadar(string directory)
    {
        if (!GuiLauncher.EnsureSetup(requireDisplay:false)) return 1;
        Directory.CreateDirectory(directory);
        foreach (var style in Enum.GetValues<HudRadarStyle>())
        {
            var profile=HudProfileDefaults.Create("Project Prime"); HudRadarStyles.Apply(profile,style);
            foreach(var element in profile.Elements.Values) element.Enabled=false;
            var radar=profile.Elements["core.radar"]; radar.Enabled=true; radar.Anchor=HudAnchor.Center;
            radar.OffsetX=radar.OffsetY=0; radar.Scale=2;
            var canvas=new HudStudioCanvas(new HudStudioHistory(profile)) { PreviewWidth=800,PreviewHeight=600,GridSize=0,Selected=-1,RadarOnly=true };
            var window=new Window { Width=800,Height=600,Content=canvas };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var bitmap=new RenderTargetBitmap(new PixelSize(800,600),new Vector(96,96));
                bitmap.Render(canvas); bitmap.Save(Path.Combine(directory,$"radar-{style}.png"),PngBitmapEncoderOptions.Default);
            }
            finally { window.Close(); }
        }
        Console.WriteLine("Radar editor previews saved to " + Path.GetFullPath(directory));
        return 0;
    }
    // Deterministic editor previews, not a substitute for native match screenshots.
    public static int Export(string directory)
    {
        if (!GuiLauncher.EnsureSetup(requireDisplay:false)) return 1;
        Directory.CreateDirectory(directory);
        foreach (string preset in new[] { "Classic", "Project Prime", "Competitive", "Accessibility" })
        foreach (var (width,height) in new[] { (1920,1080),(3440,1440),(1280,720),(1080,1920) })
        {
            var canvas = new HudStudioCanvas(new HudStudioHistory(HudProfileDefaults.Create(preset)))
            { PreviewWidth=width, PreviewHeight=height, GridSize=0, Selected=-1 };
            var window = new Window { Width=width,Height=height,Content=canvas };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var bitmap=new RenderTargetBitmap(new PixelSize(width,height),new Vector(96,96));
                bitmap.Render(canvas);
                bitmap.Save(Path.Combine(directory,$"{preset.Replace(' ','-')}-{width}x{height}.png"));
            }
            finally { window.Close(); }
        }
        Console.WriteLine("HUD editor preview matrix saved to " + Path.GetFullPath(directory));
        return 0;
    }
}
