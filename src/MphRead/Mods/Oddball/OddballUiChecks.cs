#if MPHREAD_AVALONIA
using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
namespace MphRead.Mods.Launcher.Gui;
internal static class OddballUiChecks
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS "+message);}
    internal static int Run(string directory)
    {
        try{
            if(!GuiLauncher.EnsureSetup())throw new InvalidOperationException("No Avalonia backend");
            Directory.CreateDirectory(directory);
            Dispatcher.UIThread.Invoke(()=>{
                var settings=new MenuSettings{RoomKey="MP1 SANCTORUS",Fiesta="on",PointGoal="999"};
                var view=new OfflineWorkspace(settings,new[]{"MP1 SANCTORUS",Oddball.OddballMatch.RoomKey},new PrimeOverlayHost());
                T Field<T>(string name)=>(T)typeof(OfflineWorkspace).GetField(name,Fields)!.GetValue(view)!;
                int index=Array.FindIndex(MatchTypeCatalog.OfflineGameTypes,x=>x.Free==GameMode.Oddball || x.Team==GameMode.Oddball);
                Check(index>=0,"Oddball exists in offline selector");
                Field<ChoiceRow>("_mode").Index=index;
                Check(Field<string>("_room")=="MP1 SANCTORUS","selecting Oddball preserves selected arena");
                Check(Field<ChoiceRow>("_bots").Index==7,"selecting Oddball defaults to seven bots");
                LaunchPlan? launched=null;view.Launched+=(_,p)=>launched=p;
                var start=Field<PrimeButton>("_start");
                var click=(EventHandler?)typeof(HubNavButton).GetField("Click",Fields)!.GetValue(start);
                click!.Invoke(start,EventArgs.Empty);
                Check(launched is {Mode:GameMode.Oddball,Bots:7},"real start-button handler emits eight-player Oddball launch");
                Check(launched!.Value.RoomKey=="MP1 SANCTORUS" && !launched.Value.MatchRules.Fiesta && launched.Value.MatchRules.PointGoal==100,"unrelated Fiesta/score preferences do not leak into Oddball");
                Check(LobbyRules.ValidateDefinition(launched.Value.MatchRules,out _)!=LobbyResultCode.Ok,"network admission rejects offline-only Oddball");
                Check(OfflineRematch.TryPlan(launched.Value,Oddball.OddballMatch.RoomKey,out var next) && next.Mode==GameMode.Oddball && next.RoomKey==Oddball.OddballMatch.RoomKey && next.MatchRules.RoomKey==next.RoomKey,"rematch changes maps without losing Oddball or disagreeing rules");
                Check(UiCapture.Capture(view,Path.Combine(directory,"oddball-offline-1366x768.png"),new Size(1366,768)),"actual offline layout renders");
                var profile=Render.Hud.HudProfileDefaults.Create("Halo");
                profile.Elements["match.timer"].OffsetX=123;profile.AccentColor="#F0A030";
                foreach(var id in new[]{"match.respawn","match.oddballAction","match.oddballEvents","match.oddballScoreboard","match.oddballStatus"})
                {
                    Check(profile.Elements.ContainsKey(id),id+" has independent layout");
                    Check(Array.IndexOf(Render.Hud.HudTypography.Ids,id)>=0,id+" has Gameplay typography entry");
                    Check(!profile.Typography.Get(id).Enabled,id+" defaults to shared HUD palette");
                    profile.Elements[id].OffsetX=31;profile.Elements[id].Scale=1.25f;
                }
                var editor=new HudStudioView(profile,_=>{});
                var elementChoice=(Avalonia.Controls.ComboBox)typeof(HudStudioView).GetField("_elements",Fields)!.GetValue(editor)!;
                elementChoice.SelectedIndex=Array.IndexOf(Render.Hud.HudProfileDefaults.ElementIds,"match.oddballScoreboard");
                Check((string)typeof(HudStudioView).GetField("_category",Fields)!.GetValue(editor)! == "Gameplay","Oddball editor is in Gameplay");
                Check(UiCapture.Capture(editor,Path.Combine(directory,"oddball-gameplay-editor.png"),new Size(1366,768)),"actual Oddball HUD editor renders without index errors");
                var json=System.Text.Json.JsonSerializer.Serialize(profile);
                var loaded=System.Text.Json.JsonSerializer.Deserialize<Render.Hud.HudProfile>(json)!;loaded.Validate();
                Check(loaded.Elements["match.timer"].OffsetX==123 && loaded.AccentColor=="#F0A030","HUD migration preserves existing timer and color");
                foreach(var id in new[]{"match.respawn","match.oddballAction","match.oddballEvents","match.oddballScoreboard","match.oddballStatus"})
                    Check(loaded.Elements[id].OffsetX==31 && loaded.Elements[id].Scale==1.25f,id+" layout survives save/reload");

            });
            Console.WriteLine($"ODDBALL UI CHECKS PASS: {checks}");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);Console.WriteLine("ODDBALL UI CHECKS FAILED");return 1;}
    }
}
#endif
