#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.HeroModes;

internal static class CtzV3RenderChecks
{
    internal static int Run(string room,string output,bool dsLayout=false,int renderWidth=1200,int renderHeight=720)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        var checks=new List<string>();Scene? scene=null;
        var original=HudProfiles.CopyCurrent();bool oldBoard=PlayerEntity.ModForceScoreboard;
        bool oldDs=DsLayout.Enabled, oldStylus=PointerInput.StylusMode;
        string oldAspect=DsLayout.ScreenAspect;
        void Require(bool okay,string text){if(!okay)throw new InvalidOperationException(text);checks.Add(text);Console.WriteLine("CTZ5 RENDER PASS "+text);}
        try
        {
            if (dsLayout) { DsLayout.Enabled=true; DsLayout.ScreenAspect="5:3"; PointerInput.StylusMode=true; }
            var settings=DesktopGlContext.Settings(background:true);
            settings.CurrentMonitor=Monitors.GetPrimaryMonitor().Handle;
            settings.ClientSize=new(renderWidth,renderHeight);settings.StartVisible=true;settings.StartFocused=false;
            settings.Title="Capture the Zone v5 native verification";
            using var window=new NativeWindow(settings);
            using var graphics=new DesktopGraphicsSession(window);
            scene=new Scene(window.FramebufferSize,SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.Players.MaxPlayers=8;
            scene.Random.SetRng1(12345);
            Hunter[] hunters=[Hunter.Noxus,Hunter.Samus,Hunter.Weavel,Hunter.Spire,Hunter.Trace,Hunter.Kanden,Hunter.Sylux,Hunter.Noxus];
            for(int i=0;i<hunters.Length;i++)scene.AddPlayer(hunters[i],team:i/4);
            foreach(var p in scene.Players.Items){p.IsBot=p.SlotIndex!=0;p.BotLevel=0;}
            scene.AddRoom(room,GameMode.CaptureTheZone,playerCount:8);scene.OnLoad();scene.OnResize();
            if (dsLayout) Require(scene.UseDsLayout && DsLayout.ScreenAspect=="5:3", "Actual dual-screen 5:3 stylus render path enabled");
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=0;scene.GameState.SpawnProtection=false;
            scene.GameState.EnhancedHunters=false;scene.GameState.ShadowFreeze=false;
            RenderOptions.ResolutionScale=100;RenderOptions.ShowFps=false;
            var profile=HudProfileDefaults.Create("Project Prime");profile.Mode=HudMode.Custom;
            profile.Elements["combat.opponent"].Anchor=HudAnchor.TopRight;
            profile.Elements["combat.opponent"].OffsetX=-960;profile.Elements["combat.opponent"].OffsetY=360;
            HudProfiles.Publish(profile);
            void Present(string? file=null)
            {
                DesktopGraphicsSession.Resize(window);scene.OnDrawFrame();
                if(!scene.OnRenderFrame())throw new InvalidOperationException("Native renderer stopped");
                if(file!=null)Require(ScreenCapture.SaveWindow(scene,Path.Combine(output,file)),"Full native HUD screenshot "+file);
                DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
            }
            var invuln=typeof(PlayerEntity).GetField("_spawnInvulnTimer",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var resolve=typeof(BeamProjectileEntity).GetMethod("ResolveCollision",BindingFlags.Instance|BindingFlags.NonPublic)!;
            for(int i=0;i<200;i++){scene.OnSimulationFrame();if(i%4==0)Present();}
            Require(scene.GameState.ActivePlayers==8,$"Eight-player scene available for the actual scoreboard (active={scene.GameState.ActivePlayers}, created={scene.Players.PlayersCreated})");
            foreach(var p in scene.Players.Items)p.IsBot=false;
            for(int i=0;i<8;i++)
            {
                var p=scene.Players.Items[i];p.TeamIndex=i/4;
                p.Spawn(new Vector3(i<4?-22:22,.1f,15+(i%4)*2),Vector3.UnitZ,Vector3.UnitY,p.NodeRef,false);
                invuln.SetValue(p,(ushort)0);p.ModReleaseSpawnProtection();
                scene.GameState.Nicknames[i]=hunters[i].ToString()+" "+(i+1);
            }
            var ally=scene.Players.Items[1];
            void Main(int slot)
            {
                // Separate controlled camera subjects so an earlier subject does not overlap the next camera.
                for(int i=0;i<8;i++) if(i!=slot && i!=ally.SlotIndex)
                    scene.Players.Items[i].ModPlaceAt(new Vector3(i<4?-22:22,.1f,15+(i%4)*2));
                scene.Players.MainPlayerIndex=slot;var p=scene.Players.Main;
                if(!p.HudReady)p.SetUpHud();
                p.Spawn(new Vector3(0,.1f,-12),Vector3.UnitZ,Vector3.UnitY,p.NodeRef,false);
                p.CameraInfo.NodeRef=p.NodeRef;invuln.SetValue(p,(ushort)0);p.ModReleaseSpawnProtection();
                ally.ModPlaceAt(new Vector3(0,.1f,-5));ally.Speed=Vector3.Zero;ally.Health=100;
                for(int i=0;i<6;i++){scene.OnSimulationFrame();Present();}
            }
            void Hit(BeamProjectileEntity b)
            {
                var point=ally.Position+Vector3.UnitY*.5f;b.Age=0;b.SpawnPosition=point-Vector3.UnitZ*5;b.Position=point;
                var collision=new CollisionResult{Position=point,Plane=new Vector4(0,0,-1,-point.Z),Distance=.5f};
                resolve.Invoke(b,new object?[]{collision,ally,false,.5f,false});
            }
            BeamProjectileEntity[] Fire(PlayerEntity p,bool charged=false)
            {
                foreach(var b in p.EquipInfo.Beams)b.Lifespan=0;
                p.EquipInfo.ChargeLevel=(ushort)(charged?p.EquipInfo.Weapon.FullCharge*2:0);
                NetFireEvents.Begin(p);NetUnlagged.BeginShot(p);
                try{BeamProjectileEntity.Spawn(p,p.EquipInfo,p.Position+Vector3.UnitY,Vector3.UnitZ,BeamSpawnFlags.NoMuzzle,p.NodeRef,scene);}
                finally{NetUnlagged.EndShot(p);}
                return p.EquipInfo.Beams.Where(b=>b.Owner==p && b.Lifespan>0).OrderByDescending(b=>b.Age).ToArray();
            }
            Main(0);
            scene.CaptureZone.Apply(new(CtzPhase.Active,2,0,-1,0,2700,3660,1,0,0,0,false,3720));
            Present("gameplay-clock-after-unlock-no-score-overlay.png");
            var moved=profile.DeepClone();
            moved.Elements["match.timer"].Anchor=HudAnchor.BottomRight;
            moved.Elements["match.timer"].OffsetX=-240;moved.Elements["match.timer"].OffsetY=-240;
            var saved=HudProfileStore.Parse(HudProfileStore.Serialize(moved));HudProfiles.Publish(saved);
            Require(HudProfiles.Runtime[6].Offset.X==-240 && HudProfiles.Runtime[6].Offset.Y==-240,"Saved relocated match.timer layout reaches native HUD");
            Present("gameplay-relocated-clock-after-reload.png");
            Hit(Fire(scene.Players.Main)[0]);
            Require(ally.Health==109 && scene.Players.Main.ModCtzShowingHealedAlly,"Noxus native hit preserves the teammate-health panel");
            Present("noxus-healed-teammate.png");
            Main(2);var burst=Fire(scene.Players.Main);
            Require(burst.Length==3,"Weavel native emitter creates a three-projectile burst");
            foreach(var b in burst)Hit(b);
            Require(ally.Health==112 && scene.Players.Main.ModCtzShowingHealedAlly,"Weavel three projectile impacts heal12HP and reveal ally health");
            Present("weavel-three-heals-teammate-health.png");
            Main(3);Hit(Fire(scene.Players.Main,true)[0]);
            Require(scene.Players.Main.ModCtzShowingHealedAlly && ally.ModCtzAuraRemaining==300,"Spire native charged impact reveals ally health and starts aura");
            for(int i=0;i<60;i++){scene.OnSimulationFrame();if(i%4==0)Present();}
            Require(ally.Health==104 && scene.Players.Main.ModCtzShowingHealedAlly,"Spire first aura pulse shows104HP");
            Present("spire-healing-aura-first-pulse.png");
            for(int i=0;i<240;i++){scene.OnSimulationFrame();if(i%4==0)Present();}
            Require(ally.Health==120 && scene.Players.Main.ModCtzShowingHealedAlly,"Spire panel stays visible through all five aura pulses");
            Present("spire-healing-aura-twenty-hp.png");
            PlayerEntity.ModForceScoreboard=true;
            CtzScoreboardEvidence.Capture=new();
            Present("scoreboard-live-healing-eight-players.png");
            Require(scene.GameState.CtzStats.Get(2).Done>=12 && scene.GameState.CtzStats.Get(3).Done>=20,"Scoreboard consumes actual native projectile/aura healing totals");
            Require(CtzScoreboardEvidence.Capture.All(b=>b.Left>=b.CellLeft-.01f && b.Left+b.Width<=b.CellLeft+b.CellWidth+.01f && b.Height<=b.CellHeight+.01f),"Actual scoreboard text stays inside column and row bounds");
            File.WriteAllText(Path.Combine(output,"scoreboard-live-text-bounds.json"),JsonSerializer.Serialize(CtzScoreboardEvidence.Capture));
            var realStats=scene.GameState.CtzStats.Capture();
            var realNames=(string[])scene.GameState.Nicknames.Clone();
            var realKills=(int[])scene.GameState.Kills.Clone();var realDeaths=(int[])scene.GameState.Deaths.Clone();
            var stressRows=realStats.Select(r=>r with{Done=int.MaxValue,Received=1234567890,Taken=2000000000}).ToArray();
            scene.GameState.CtzStats.Apply(stressRows);
            for(int slot=0;slot<8;slot++){scene.GameState.Nicknames[slot]="LONG_PLAYER_NAME_"+slot+"_MMMMMMMMMMMM";scene.GameState.Kills[slot]=123456;scene.GameState.Deaths[slot]=654321;NetSession.SlotPing[slot]=987;}
            CtzScoreboardEvidence.ForcePing=true;CtzScoreboardEvidence.Capture.Clear();
            Present("scoreboard-stress-eight-players-ping-large-totals.png");
            Require(CtzScoreboardEvidence.Capture.All(b=>b.Left>=b.CellLeft-.01f && b.Left+b.Width<=b.CellLeft+b.CellWidth+.01f && b.Height<=b.CellHeight+.01f && b.Top+b.Height<=192),"Eight players, ping, long names and ten-digit totals remain bounded");
            File.WriteAllText(Path.Combine(output,"scoreboard-stress-text-bounds.json"),JsonSerializer.Serialize(CtzScoreboardEvidence.Capture));
            var enlarged=HudProfiles.CopyCurrent();enlarged.TextScale=3;HudProfiles.Publish(enlarged);
            CtzScoreboardEvidence.Capture.Clear();Present("scoreboard-large-hud-font-scale.png");
            Require(CtzScoreboardEvidence.Capture.All(b=>b.Left>=b.CellLeft-.01f && b.Left+b.Width<=b.CellLeft+b.CellWidth+.01f && b.Height<=b.CellHeight+.01f),"Large saved HUD text scaling cannot overlap scoreboard cells");
            var outlined=HudProfiles.CopyCurrent();var lettering=outlined.Typography.Get("all.other");
            lettering.Enabled=true;lettering.Font="font03";lettering.Size=3;lettering.Outline=5;
            HudProfiles.Publish(outlined);CtzScoreboardEvidence.Capture.Clear();
            Present("scoreboard-custom-font-max-outline.png");
            Require(CtzScoreboardEvidence.Capture.All(b=>b.Left>=b.CellLeft-.01f && b.Left+b.Width<=b.CellLeft+b.CellWidth+.01f && b.Height<=b.CellHeight+.01f),"Custom font and maximum saved outline remain inside their cells");
            File.WriteAllText(Path.Combine(output,"scoreboard-outlined-text-bounds.json"),JsonSerializer.Serialize(CtzScoreboardEvidence.Capture));
            HudProfiles.Publish(saved);scene.GameState.CtzStats.Apply(realStats);
            realNames.CopyTo(scene.GameState.Nicknames,0);realKills.CopyTo(scene.GameState.Kills,0);realDeaths.CopyTo(scene.GameState.Deaths,0);
            CtzScoreboardEvidence.ForcePing=false;CtzScoreboardEvidence.Capture=null;
            PlayerEntity.ModForceScoreboard=false;
            scene.CaptureZone.Apply(scene.CaptureZone.State with{Phase=CtzPhase.Active,Round=3,Unlock=600,RoundElapsedTicks=0});
            scene.Players.Main.ModZoneRoundRespawn(); // Match the actual round-transition lifecycle, not only its snapshot.
            Require(!scene.Players.Main.ModCtzShowingHealedAlly,"Round respawn clears the previous ally-healing feedback");
            scene.Players.Main.CameraInfo.NodeRef=scene.Players.Main.NodeRef;
            Present("round-start-banner-0-seconds.png");
            scene.CaptureZone.Apply(scene.CaptureZone.State with{Unlock=420,RoundElapsedTicks=180});
            Require(!CtzPresentation.ShowRoundStartBanner(scene.CaptureZone.State),"Mode banner disappears at exactly3seconds");
            Present("round-start-banner-ended-3-seconds.png");
            File.WriteAllText(Path.Combine(output,"render-results.json"),JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,
                size=$"{renderWidth}x{renderHeight}", dualScreenStylus5by3=dsLayout,scope="Actual Linux softwareOpenGL full-window captures with native impact callbacks. No native Windows or internet gameplay claim."},new JsonSerializerOptions{WriteIndented=true}));
            scene.DoCleanup();scene.UnloadGl();scene=null;return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"render-results.json"),JsonSerializer.Serialize(new{passed=false,checks,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{CtzScoreboardEvidence.Capture=null;CtzScoreboardEvidence.ForcePing=false;PlayerEntity.ModForceScoreboard=oldBoard;HudProfiles.Publish(original);
            DsLayout.Enabled=oldDs;DsLayout.ScreenAspect=oldAspect;PointerInput.StylusMode=oldStylus;}
    }
}
#endif
