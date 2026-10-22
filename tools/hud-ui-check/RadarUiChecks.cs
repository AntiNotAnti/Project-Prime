using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Render.Hud;

internal static class RadarUiChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string message) { checks++; if(!condition) throw new Exception("Radar UI: "+message); }
        var view=new HudStudioView(HudProfileDefaults.Create("Project Prime"),_=>{});
        var window=new Window { Width=1280,Height=720,Content=view };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=view.GetVisualDescendants().OfType<HudStudioCanvas>().Single();
            var chooser=view.GetVisualDescendants().OfType<ComboBox>().First(c=>c.ItemsSource==HudProfileDefaults.ElementIds);
            chooser.SelectedIndex=4; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var original=canvas.History.Capture();
            foreach(var style in Enum.GetValues<HudRadarStyle>())
            {
                var preset=view.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.PlaceholderText=="Apply radar preset");
                preset.SelectedIndex=(int)style; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Check(canvas.History.Draft.Radar.Style==style,"select preset "+style);
                Check(canvas.History.Draft.Elements["core.radar"].OffsetX==-141.849f,"preset preserves anchor offset");
                double width=canvas.ElementBounds(4).Width;
                canvas.History.Edit(p=>p.Radar.RadiusScale=2); canvas.Refresh();
                Check(canvas.ElementBounds(4).Width>width*1.5,"dynamic bounds "+style);
                canvas.History.Undo(); Check(Math.Abs(canvas.ElementBounds(4).Width-width)<.01,"undo bounds");
                canvas.History.Redo(); Check(canvas.ElementBounds(4).Width>width*1.5,"redo bounds");
                canvas.History.Edit(p=> {p.Radar.Orientation=HudRadarOrientation.NorthUp;p.Radar.Objectives=true;p.Radar.Elevation=HudRadarElevationMode.Chevron;p.Radar.TrailSamples=4;});
                var beforeReset=canvas.History.Capture();
                canvas.History.Edit(p=>p.ResetElement("core.radar"));
                Check(canvas.History.Draft.Radar.Style==HudRadarStyle.Basic && canvas.History.Draft.Radar.TrailSamples==0,"full reset");
                canvas.History.Undo(); Check(canvas.History.Capture()==beforeReset,"reset undo restores every property");
            }
            canvas.History.Edit(p=> {p.Radar.RadiusScale=2;p.Elements["core.radar"].Anchor=HudAnchor.Center;p.Elements["core.radar"].OffsetX=0;p.Elements["core.radar"].OffsetY=0;});
            canvas.Refresh();
            var point=canvas.TranslatePoint(canvas.ElementBounds(4).Center,window)!.Value;
            float start=canvas.History.Draft.Elements["core.radar"].OffsetX;
            window.MouseDown(point,MouseButton.Left);window.MouseMove(point+new Vector(35,0));window.MouseUp(point+new Vector(35,0),MouseButton.Left);
            Check(canvas.Selected==4 && canvas.History.Draft.Elements["core.radar"].OffsetX!=start,"drag large radar");
            canvas.History.Undo(); Check(canvas.History.Draft.Elements["core.radar"].OffsetX==start,"drag undo");
            window.Width=420;window.Height=900;canvas.PreviewWidth=1080;canvas.PreviewHeight=1920;
            window.UpdateLayout();Dispatcher.UIThread.RunJobs();
            Check(canvas.ElementBounds(4).Width>0 && canvas.Bounds.Height>0,"portrait bounds and narrow inspector");
        }
        finally { window.Close(); }
        return checks;
    }
}
