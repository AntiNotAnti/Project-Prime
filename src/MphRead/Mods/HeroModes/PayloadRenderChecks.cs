#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
namespace MphRead.Mods.HeroModes;
internal static class PayloadRenderChecks
{
    internal static int Run(string room,string output,bool bots=false)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();Scene? scene=null;
        var old=HudProfiles.CopyCurrent();bool oldBoard=PlayerEntity.ModForceScoreboard;
        void Check(bool okay,string label){if(!okay)throw new InvalidOperationException(label);checks.Add(label);Console.WriteLine("PAYLOAD RENDER PASS "+label);}
        try
        {
            var settings=DesktopGlContext.Settings(background:true);settings.CurrentMonitor=Monitors.GetPrimaryMonitor().Handle;
            settings.ClientSize=new(1200,720);settings.StartVisible=true;settings.StartFocused=false;settings.Title="Hero Shooter / Push the Payload native test";
            using var window=new NativeWindow(settings);using var graphics=new DesktopGraphicsSession(window);
            scene=new Scene(window.FramebufferSize,SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.Players.MaxPlayers=8;scene.Random.SetRng1(81871);
            Hunter[] hunters=[Hunter.Samus,Hunter.Weavel,Hunter.Spire,Hunter.Noxus,Hunter.Trace,Hunter.Kanden,Hunter.Sylux,Hunter.Samus];
            for(int i=0;i<8;i++)scene.AddPlayer(hunters[i],team:i/4);
            foreach(var p in scene.Players.Items){p.IsBot=bots||p.SlotIndex!=0;p.BotLevel=2;}
            scene.AddRoom(room,GameMode.PushThePayload,playerCount:8);scene.OnLoad();scene.OnResize();
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=3;scene.GameState.SpawnProtection=false;
            RenderOptions.ResolutionScale=100;RenderOptions.ShowFps=false;
            var profile=HudProfileDefaults.Create("Project Prime");profile.Mode=HudMode.Custom;HudProfiles.Publish(profile);
            void Present(string? name=null)
            {DesktopGraphicsSession.Resize(window);scene.OnDrawFrame();if(!scene.OnRenderFrame())throw new InvalidOperationException("Renderer stopped");if(name!=null)Check(ScreenCapture.SaveWindow(scene,Path.Combine(output,name+".png")),"Native screenshot "+name);DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();}
            for(int i=0;i<200;i++){scene.OnSimulationFrame();if(i%5==0)Present();}
            Check(scene.GameState.ActivePlayers==8,"Actual eight-player rendered payload scene");
            Check(scene.Payload.Frame is {} frame&&frame.Valid,"Actual payload objective frame valid");
            Present("protected-spawn-and-setup-clock");
            if(bots)
            {
                // All eight use native input-driven AI. No position/health/progress overrides in this branch.
                foreach(var p in scene.Players.Items)p.IsBot=true;
                var samples=new List<object>();long peak=0;int lastRound=0;
                for(int tick=0;tick<72000 && scene.GameState.MatchTime!=0;tick++)
                {
                    scene.OnSimulationFrame();if(tick%60==0){var f=scene.Payload.Frame!.Value;peak=Math.Max(peak,f.Best);lastRound=Math.Max(lastRound,f.Round);samples.Add(new{tick,f.Round,f.Attacker,f.Progress,f.Best,f.Checkpoints,f.Contested,players=scene.Players.Items.Select(p=>new{slot=p.SlotIndex,hp=p.Health,team=p.TeamIndex,x=p.Position.X,y=p.Position.Y,z=p.Position.Z}).ToArray()});}
                    if(tick%600==0)File.WriteAllText(Path.Combine(output,"bot-match-telemetry.json"),JsonSerializer.Serialize(samples));
                    if(tick%30==0)Present();if(tick%9000==0)Present("bot-match-"+tick);
                    if(scene.Payload.Frame?.Finished==true)break;
                }
                Present("bot-match-final");File.WriteAllText(Path.Combine(output,"bot-match-telemetry.json"),JsonSerializer.Serialize(samples));
                Check(peak>0,"Unassisted native bots advanced the payload");Check(lastRound>=1,"Unassisted native match reached the opposing attack round");
                Check(scene.Payload.Frame?.Finished==true,"Unassisted native bot match reached final objective result");
            }
            else
            {
                foreach(var p in scene.Players.Items)p.IsBot=false;
                for(int i=0;i<PayloadRuntime.Rules.PreparationTicks+1;i++)PayloadRuntime.Tick(scene);
                var main=scene.Players.Main;var cart=scene.Payload.Position;var forward=scene.Payload.Forward;
                for(int i=1;i<8;i++)scene.Players.Items[i].ModPlaceAt(cart+new Vector3(0,.1f,20+i*2));
                main.Spawn(cart-forward*8+Vector3.UnitY*.1f,forward,Vector3.UnitY,main.NodeRef,false);
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,(ushort)0);main.ModReleaseSpawnProtection();
                main.CameraInfo.NodeRef=main.NodeRef;
                for(int i=0;i<10;i++){scene.OnSimulationFrame();Present();}
                Present("cargo-and-street");
                var route=scene.Payload.Map!.Route;
                for(int marker=0;marker<3;marker++) {
                    var pose=route.Pose(marker<2?route.Checkpoints[marker].Distance:route.Length);
                    var center=new Vector3(pose.Position.X,pose.Position.Y,pose.Position.Z);
                    var direction=new Vector3(pose.Forward.X,pose.Forward.Y,pose.Forward.Z);
                    main.Spawn(center-direction*14+Vector3.UnitY*.1f,direction,Vector3.UnitY,main.NodeRef,false);
                    for(int tick=0;tick<8;tick++){scene.OnSimulationFrame();Present();}
                    Present("checkpoint-marker-"+(marker+1));
                }
                main.Spawn(cart-forward*8+Vector3.UnitY*.1f,forward,Vector3.UnitY,main.NodeRef,false);
                var moved=profile.DeepClone();moved.Elements["match.timer"].Anchor=HudAnchor.BottomRight;moved.Elements["match.timer"].OffsetX=-300;moved.Elements["match.timer"].OffsetY=-240;
                HudProfiles.Publish(HudProfileStore.Parse(HudProfileStore.Serialize(moved)));
                Check(HudProfiles.Runtime[6].Offset.X==-300,"Saved relocated timer reaches native payload HUD");Present("relocated-payload-clock");
                PlayerEntity.ModForceScoreboard=true;Present("payload-scoreboard");PlayerEntity.ModForceScoreboard=false;
                main.ModPlaceAt(cart+Vector3.UnitY*3.6f);main.Speed=Vector3.Zero;
                for(int i=0;i<180;i++){scene.OnSimulationFrame();if(i%5==0)Present();}
                Check(main.Health>0&&main.Position.Y>1,"Native body lands on physical cargo instead of falling through");
                Check(scene.Payload.Frame!.Value.Progress>0,"Living cargo rider escorts payload through native simulation");
                Check(Vector3.Distance(main.Position,scene.Payload.Position)<6,"Native linked-floor transform carries the rider");Present("riding-moving-payload");
            }
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,scope=bots?"Native eight-bot autonomous Linux softwareGL match; not Windows or internet":"Actual Linux softwareGL native map, cargo, HUD, scoreboard and player collision. Not Windows or internet"},new JsonSerializerOptions{WriteIndented=true}));
            scene.DoCleanup();scene.UnloadGl();scene=null;return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=false,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{PlayerEntity.ModForceScoreboard=oldBoard;HudProfiles.Publish(old);}
    }
}
#endif
