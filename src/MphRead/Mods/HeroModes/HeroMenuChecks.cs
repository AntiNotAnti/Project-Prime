#if MPHREAD_AVALONIA && !ANDROID
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Headless;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
namespace MphRead.Mods.HeroModes;
internal static class HeroMenuChecks
{
    internal static int Run(string output)
    {
        var checks=new List<string>();int old=LauncherPrefs.LastHeroObjective;var oldMode=LauncherPrefs.LastLobbyMode;
        void Check(bool okay,string label){if(!okay)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("MENU PASS "+label);}
        try
        {
            Directory.CreateDirectory(output);
            AppBuilder.Configure<CtzUiChecks.CtzTestApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().SetupWithoutStarting();
            ChoiceRow Field(object target,string name)=>(ChoiceRow)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
            var offline=new OfflineWorkspace(new MenuSettings(),Array.Empty<string>(),new PrimeOverlayHost());
            var mode=Field(offline,"_mode");var objective=Field(offline,"_heroObjective");
            var window=new Window{Width=1366,Height=768,Content=offline};window.Show();
            void Draw(string name){window.UpdateLayout();Dispatcher.UIThread.RunJobs();window.UpdateLayout();using var image=new RenderTargetBitmap(new PixelSize((int)window.Width,(int)window.Height),new Avalonia.Vector(96,96));image.Render((Visual)window.Content!);image.Save(Path.Combine(output,name+".png"));}
            mode.Index=MatchTypeCatalog.BaseModeIndex(GameMode.Battle);Check(!objective.IsVisible,"Offline objective submenu hidden outside Hero Shooter");
            mode.Index=MatchTypeCatalog.BaseModeIndex(GameMode.CaptureTheZone);objective.Index=0;
            Check(objective.IsVisible&&mode.Value=="Hero Shooter"&&objective.Value=="Capture the Zone","Offline actual controls expose Hero Shooter then Capture the Zone");Draw("offline-hero-shooter-capture-the-zone");
            objective.Index=1;
            var selected=(GameMode)offline.GetType().GetMethod("SelectedMode",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(offline,null)!;
            Check(selected==GameMode.PushThePayload && LauncherPrefs.LastHeroObjective==1,"Offline child selection resolves payload and updates preference");Draw("offline-hero-shooter-push-the-payload");
            mode.Index=0;Check(!objective.IsVisible,"Leaving family hides child row");mode.Index=MatchTypeCatalog.BaseModeIndex(GameMode.PushThePayload);Check(objective.Index==1,"Returning to family preserves objective");
            window.Content=null;
            LauncherPrefs.LastLobbyMode=GameMode.PushThePayload;
            var create=new CreateServerScreen(Array.Empty<string>(),discoverHosts:false);window.Content=create;
            var createMode=Field(create,"_mode");var createObjective=Field(create,"_heroObjective");
            Check(createMode.Value=="Hero Shooter" && createObjective.IsVisible&&createObjective.Value=="Push the Payload","Create-server restores parent and concrete objective");Draw("create-server-hero-shooter-push-the-payload");
            createObjective.Index=0;Check(createObjective.Value=="Capture the Zone","Create-server child switch works");
            window.Content=null;window.Close();
            Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,scope="Actual Avalonia offline and create-server controls and screenshots. No native Windows or hardware-controller claim."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(JsonSerializer.Serialize(new{passed=false,count=checks.Count,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{LauncherPrefs.LastHeroObjective=old;LauncherPrefs.LastLobbyMode=oldMode;}
    }
}
#endif
