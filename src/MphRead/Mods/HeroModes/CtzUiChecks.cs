#if MPHREAD_AVALONIA && !ANDROID
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using MphRead.Mods.Launcher;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Launcher.Gui;
using ProjectPrime.CaptureZone;
using ProjectPrime.CaptureZone.Ui;

namespace MphRead.Mods.HeroModes;
public static class CtzUiChecks
{
    public static int Run(string output)
    {
        var checks=new List<string>();
        void Check(bool okay,string text){if(!okay)throw new InvalidOperationException(text);checks.Add(text);}
        try
        {
            Directory.CreateDirectory(output);
            AppBuilder.Configure<CtzTestApplication>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
            var roster=new CaptureZoneRoster(4);Guid local=HeroRosterBridge.SlotId(0);
            for(int t=0;t<2;t++)for(int p=0;p<4;p++)
            {var id=HeroRosterBridge.SlotId(t*4+p);roster.Join(id,$"PLAYER {t*4+p+1}",t);roster.Select(id,(ZoneHunter)p);roster.Ready(id,true);}
            var view=new CaptureZoneLobbyView(roster.Snapshot,local,h=>roster.Select(local,h).Message,"CAPTURE THE ZONE",
                h=>HeroRules.WeaponDetails(HeroRules.ToGame(h)));
            var window=new Window{Width=1280,Height=720,Content=view};window.Show();
            void Drain(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
            Drain();Check(view.Cards.Count==7,"Actual shared CTZ view contains seven hunter cards");
            view.Cards[ZoneHunter.Noxus].Focus();Drain();
            Check(view.FocusedHunter==ZoneHunter.Noxus,"Keyboard focus selects actual character detail panel");
            view.Cards[ZoneHunter.Noxus].RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Check(view.StatusText.Contains("Already selected by your teammate"),"Real button preserves occupied reservation");
            view.Cards[ZoneHunter.Weavel].RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Check(roster.Snapshot().Slots.Single(p=>p.PlayerId==local).Hunter==ZoneHunter.Weavel,"Real button routes to shared selection command");
            foreach(var size in new[]{new PixelSize(1280,720),new PixelSize(1366,768),new PixelSize(1200,720)})
            {
                window.Width=size.Width;window.Height=size.Height;Drain();
                Check(view.Bounds.Width==size.Width && view.Bounds.Height==size.Height,$"CTZ view arranged at {size.Width}x{size.Height}");
                using var image=new RenderTargetBitmap(size,new Avalonia.Vector(96,96));image.Render(view);
                image.Save(Path.Combine(output,$"ctz-shared-view-{size.Width}x{size.Height}.png"));
            }
            window.Close();
            var hp=MphRead.Mods.Render.Hud.HudProfileDefaults.Create("Project Prime");
            var history=new HudStudioHistory(hp);
            var canvas=new HudStudioCanvas(history){PreviewWidth=1200,PreviewHeight=720,GridSize=0,Selected=10,
                Scenario=MphRead.Mods.Render.Hud.HudPreviewScenario.CaptureZone};
            var editor=new Window{Width=1200,Height=720,Content=canvas};editor.Show();editor.UpdateLayout();Dispatcher.UIThread.RunJobs();
            var before=canvas.ElementBounds(10);
            history.Edit(p=>p.Elements["match.objective"].OffsetX+=160);canvas.Refresh();editor.UpdateLayout();Dispatcher.UIThread.RunJobs();
            var after=canvas.ElementBounds(10);
            Check(Math.Abs(after.X-before.X-100)<.1,"CTZ objective editor bounds track160 logical-unit move without changing legacy offsets");
            Check(canvas.ElementBounds(14).Width>0 && canvas.ElementBounds(15).Width>0 && canvas.ElementBounds(16).Width>0,
                "Round respawn and cooldown controls have selectable editor bounds");
            Check(!canvas.ElementInPreview(5) && !canvas.ElementInPreview(14),"CTZ editor omits scoreboard-only score and round-win overlays");
            var timerBefore=canvas.ElementBounds(6);
            history.Edit(p=>{p.Elements["match.timer"].OffsetX+=160;p.Elements["match.timer"].OffsetY+=240;});
            canvas.Refresh();editor.UpdateLayout();Dispatcher.UIThread.RunJobs();
            var timerAfter=canvas.ElementBounds(6);
            Check(Math.Abs(timerAfter.X-timerBefore.X-100)<.1 && Math.Abs(timerAfter.Y-timerBefore.Y-150)<.1,"Clock editor bounds move by exact saved logical offsets");
            var reloaded=HudProfileStore.Parse(HudProfileStore.Serialize(history.Draft));
            Check(reloaded.Elements["match.timer"].OffsetX==history.Draft.Elements["match.timer"].OffsetX && reloaded.Elements["match.timer"].OffsetY==history.Draft.Elements["match.timer"].OffsetY,"Relocated clock survives serialize and reload");
            using(var image=new RenderTargetBitmap(new PixelSize(1200,720),new Avalonia.Vector(96,96)))
            {image.Render(canvas);image.Save(Path.Combine(output,"ctz-hud-editor-1200x720.png"));}
            editor.Close();
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,
                actual_shared_view_rendered=true,live_lobby_network_test=false,hardware_controller_tested=false,native_gameplay_tested=false},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
    }
    public sealed class CtzTestApplication:Application
    {public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=ThemeVariant.Dark;}}
}
#endif
