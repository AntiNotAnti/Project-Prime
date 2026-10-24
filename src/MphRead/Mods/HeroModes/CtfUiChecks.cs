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
using MphRead.Mods.Launcher.Gui;
using ProjectPrime.CaptureZone;
using ProjectPrime.CaptureZone.Ui;

namespace MphRead.Mods.HeroModes;
public static class CtfUiChecks
{
    public static int Run(string output)
    {
        var checks=new List<string>();
        void Check(bool okay,string text){if(!okay)throw new InvalidOperationException(text);checks.Add(text);}
        try
        {
            Directory.CreateDirectory(output);
            AppBuilder.Configure<CtfTestApplication>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
            var roster=new CaptureZoneRoster(4);Guid local=HeroRosterBridge.SlotId(0);
            for(int t=0;t<2;t++)for(int p=0;p<4;p++)
            {var id=HeroRosterBridge.SlotId(t*4+p);roster.Join(id,$"PLAYER {t*4+p+1}",t);roster.Select(id,(ZoneHunter)p);roster.Ready(id,true);}
            var view=new CaptureZoneLobbyView(roster.Snapshot,local,h=>roster.Select(local,h).Message,"CAPTURE THE FLAG",
                h=>HeroRules.WeaponDetails(HeroRules.ToGame(h)));
            var window=new Window{Width=1280,Height=720,Content=view};window.Show();
            void Drain(){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
            Drain();Check(view.Cards.Count==7,"Actual shared CTF view contains seven hunter cards");
            view.Cards[ZoneHunter.Noxus].Focus();Drain();
            Check(view.FocusedHunter==ZoneHunter.Noxus,"Keyboard focus selects actual character detail panel");
            view.Cards[ZoneHunter.Noxus].RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Check(view.StatusText.Contains("Already selected by your teammate"),"Real button preserves occupied reservation");
            view.Cards[ZoneHunter.Weavel].RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Check(roster.Snapshot().Slots.Single(p=>p.PlayerId==local).Hunter==ZoneHunter.Weavel,"Real button routes to shared selection command");
            foreach(var size in new[]{new PixelSize(1280,720),new PixelSize(1366,768),new PixelSize(1200,720)})
            {
                window.Width=size.Width;window.Height=size.Height;Drain();
                Check(view.Bounds.Width==size.Width && view.Bounds.Height==size.Height,$"CTF view arranged at {size.Width}x{size.Height}");
                using var image=new RenderTargetBitmap(size,new Avalonia.Vector(96,96));image.Render(view);
                image.Save(Path.Combine(output,$"ctf-shared-view-{size.Width}x{size.Height}.png"));
            }
            // Inspect the real Settings Controls page, not a copied README description.
            var settings=new SettingsView(new MenuSettings(),shell:true);window.Content=settings;
            settings.ShowSection("Controls",1);Drain();
            var section=typeof(SettingsView).GetMethod("SectionPage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
                .Invoke(settings,new object[]{"Controls"}) as Control;
            Check(section!=null && section.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==CtfRules.ZoomDescription),
                "Actual Controls page contains contextual Zoom/ADS pickup/throw description");
            window.Close();
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,
                actual_shared_view_rendered=true,live_lobby_network_test=false,hardware_controller_tested=false,native_gameplay_tested=false},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
    }
    public sealed class CtfTestApplication:Application
    {public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=ThemeVariant.Dark;}}
}
#endif
