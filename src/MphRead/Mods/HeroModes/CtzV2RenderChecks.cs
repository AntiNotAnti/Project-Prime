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
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.HeroModes;

internal static class CtzV2RenderChecks
{
    internal static int Run(string room,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var checks=new List<string>();Scene? scene=null;
        var old=HudProfiles.CopyCurrent();
        void Require(bool okay,string text){if(!okay)throw new InvalidOperationException(text);checks.Add(text);Console.WriteLine("CTZ2 RENDER PASS "+text);}
        try
        {
            var settings=DesktopGlContext.Settings(background:true);
            settings.CurrentMonitor=Monitors.GetPrimaryMonitor().Handle;
            settings.ClientSize=new(1200,720);settings.StartVisible=true;settings.StartFocused=false;
            settings.Title="Capture the Zone v2 native verification";
            using var window=new NativeWindow(settings);
            using var graphics=new DesktopGraphicsSession(window);
            var keyboard=SyntheticInput.CreateKeyboard();
            scene=new Scene(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.Random.SetRng1(12345);scene.AddPlayer(Hunter.Noxus);scene.AddPlayer(Hunter.Samus);scene.AddPlayer(Hunter.Trace);
            foreach(var p in scene.Players.Items){p.IsBot=false;p.BotLevel=0;}
            scene.AddRoom(room,GameMode.CaptureTheZone,playerCount:3);scene.OnLoad();scene.OnResize();
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=0;scene.GameState.SpawnProtection=false;
            scene.GameState.EnhancedHunters=false;scene.GameState.ShadowFreeze=false;
            RenderOptions.ResolutionScale=100;RenderOptions.ShowFps=false;
            var main=scene.Players.Main;var ally=scene.Players.Items[1];var enemy=scene.Players.Items[2];
            main.TeamIndex=ally.TeamIndex=0;enemy.TeamIndex=1;
            var profile=HudProfileDefaults.Create("Project Prime");profile.Mode=HudMode.Custom;
            HudProfiles.Publish(profile);
            void Present(string? file=null)
            {
                DesktopGraphicsSession.Resize(window);scene.OnDrawFrame();RequireOnceRender(scene.OnRenderFrame());
                if(file!=null)Require(ScreenCapture.SaveWindow(scene,Path.Combine(output,file)),"Native screenshot "+file);
                DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
            }
            static void RequireOnceRender(bool okay){if(!okay)throw new InvalidOperationException("Native render stopped");}
            var invuln=typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!;
            for(int i=0;i<180;i++){invuln.SetValue(main,(ushort)2);scene.OnSimulationFrame();if(i%3==0)Present();}
            Require(main.Health>0 && main.Hunter==Hunter.Noxus,"Main Noxus spawned in actual CTZ scene");
            scene.CaptureZone.Apply(new(CtzPhase.Active,1,0,-1,0,720,1500,0,0,0,0,false));
            void Place(PlayerEntity p,Vector3 pos,Vector3 facing)
            {p.Spawn(pos,facing,Vector3.UnitY,p.NodeRef,respawn:false);invuln.SetValue(p,(ushort)0);p.ModReleaseSpawnProtection();}
            Place(main,new(-43,.1f,14),Vector3.UnitX);Place(ally,new(-29,.1f,14),Vector3.UnitX);Place(enemy,new(30,.1f,14),-Vector3.UnitX);
            for(int i=0;i<12;i++){scene.OnSimulationFrame();Present();}
            Present("spawn-room-friendly-door.png");
            Place(main,new(0,.1f,-12),Vector3.UnitZ);Place(ally,new(0,.1f,-5),-Vector3.UnitZ);Place(enemy,new(20,.1f,18),-Vector3.UnitZ);
            ally.Health=100;ally.ModStartCtzHealingAura();main.ModFocusCtzTeammate(ally);
            main.ModStartCtzFreezeCooldown();
            for(int i=0;i<12;i++){scene.OnSimulationFrame();Present();}
            Require(main.ModCtzShowingHealedAlly,"Offline healed teammate target panel is visible without network generation IDs");
            Present("hud-default-ally-healing.png");
            var moved=profile.DeepClone();
            moved.Elements["match.score"].OffsetY=360;
            moved.Elements["match.objective"].OffsetY=420;
            moved.Elements["match.rounds"].OffsetY=475;
            moved.Elements["combat.abilityCooldown"].Anchor=HudAnchor.TopCenter;moved.Elements["combat.abilityCooldown"].OffsetY=585;
            moved.Elements["combat.opponent"].Anchor=HudAnchor.TopRight;moved.Elements["combat.opponent"].OffsetX=-800;moved.Elements["combat.opponent"].OffsetY=60;
            HudProfiles.Publish(moved);Present("hud-relocated-ally-healing.png");
            Require(HudProfiles.Runtime[5].Offset.Y==360 && HudProfiles.Runtime[Array.IndexOf(HudProfileDefaults.ElementIds, "match.rounds")].Offset.Y==475,"Moved score and round controls reach native runtime");
            scene.CaptureZone.Apply(scene.CaptureZone.State with{Unlock=180});
            moved.Elements["match.timer"].OffsetY=650;HudProfiles.Publish(moved);Present("hud-relocated-timer.png");
            main.TakeDamage(100000,DamageFlags.Death|DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,null);
            moved.Elements["match.respawn"].Anchor=HudAnchor.TopCenter;moved.Elements["match.respawn"].OffsetY=300;
            HudProfiles.Publish(moved);Present("hud-relocated-respawn.png");
            Require(main.RespawnTimer==300,"Noxus native five-second respawn displayed");
            File.WriteAllText(Path.Combine(output,"render-results.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,
                size="1200x720 (5:3)",scope="Actual native Linux software OpenGL renders, no Windows hardware or live network match claim"},new JsonSerializerOptions{WriteIndented=true}));
            scene.DoCleanup();scene.UnloadGl();scene=null;
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"render-results.json"),JsonSerializer.Serialize(new{passed=false,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{HudProfiles.Publish(old);}
    }
}
#endif
