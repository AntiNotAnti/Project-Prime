#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.Mods.Oddball;
internal static class OddballRenderChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool ok,string s){if(!ok)throw new InvalidOperationException(s);Console.WriteLine("PASS "+s);}
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);Scene? scene=null;
        try{
            GameSettings.Apply(GameState.LoadSettings());
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(1024,768);settings.StartVisible=true;settings.StartFocused=false;
            using var window=new NativeWindow(settings);using var graphics=new DesktopGraphicsSession(window);
            try{
                var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();
                var setMouse=typeof(MouseState).GetProperty("Position")!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();
                scene=new Scene(window.FramebufferSize,keyboard,mouse,_=>{},()=>{});
                scene.Players.MaxPlayers=8;
                var checkHunter=Enum.TryParse<Hunter>(Environment.GetEnvironmentVariable("ODDBALL_CHECK_HUNTER"),out var requestedHunter)?requestedHunter:Hunter.Samus;
                for(int i=0;i<8;i++)scene.AddPlayer(i==0?checkHunter:(Hunter)(i%7),team:i%2);
                for(int i=0;i<8;i++){scene.Players.Items[i].IsBot=i!=0;scene.Players.Items[i].BotLevel=1;}
                scene.AddRoom(OddballMatch.RoomKey,GameMode.Oddball,playerCount:8);scene.OnLoad();scene.OnResize();
                if(Environment.GetEnvironmentVariable("ODDBALL_AUDIO_FIXTURE")=="1")
                {
                    Check(OddballAudio.LoadedCount==10,"all ten synthetic MP3 decoder fixtures preloaded (not Halo voice verification)");
                    OddballAudio.Queue(scene,"oddball");OddballAudio.Tick(scene);
                    Check(OddballAudio.PlayedCount==1,"voice channel dispatches preloaded cue");
                    OddballAudio.Queue(scene,"ball_taken");OddballAudio.Queue(scene,"ball_taken");
                    Check(OddballAudio.QueuedCount==1,"duplicate pickup audio is coalesced");
                    OddballAudio.Queue(scene,"game_over",true);
                    Check(OddballAudio.QueuedCount==1,"match-end cue replaces stale queued chatter");
                    OddballAudio.Tick(scene);Check(OddballAudio.PlayedCount==2,"priority cue dispatched");
                    System.Threading.Thread.Sleep(250);
                }
                scene.GameState.SpawnProtection=false;scene.GameState.EnhancedHunters=false;scene.GameState.Fiesta=false;
                scene.GameState.Oddball=new OddballMatch();
                var main=scene.Players.Main;
                var setKey=typeof(KeyboardState).GetMethod("SetKeyState",Private|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
                main.Controls.Zoom.Type=ButtonType.Key;main.Controls.Zoom.Key=Keys.Z;
                main.Controls.Shoot.Type=ButtonType.Key;main.Controls.Shoot.Key=Keys.F;
                main.Controls.MouseAim=false;
                object input=typeof(PlayerEntity).GetProperty("Input",Private)!.GetValue(main)!;
                input.GetType().GetProperty("HasInput")!.SetValue(input,true);
                void Draw(string? name=null){DesktopGraphicsSession.Resize(window);scene.OnDrawFrame();Check(scene.OnRenderFrame(),"frame renders" + (name==null?"":" "+name));if(name!=null){Check(ScreenCapture.SaveWindow(scene,Path.Combine(output,name+".png")),"HUD capture "+name);ScreenCapture.Save(scene,Path.Combine(output,name+"-world.png"));}DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();}
                // Native bot navigation, weapons, respawns and skull handling. No teleporting bots.
                for(int i=0;i<9000;i++){
                    scene.OnSimulationFrame();
                    if(i%600==0){Console.WriteLine($"BOT FRAME {i} score {scene.GameState.Oddball!.Score(0)}:{scene.GameState.Oddball.Score(1)}");Draw();}
                }
                int pickups=0;for(int i=1;i<8;i++)pickups+=scene.GameState.ObjectivePickups[i];
                Check(pickups>0,"bots acquire objective through native navigation");
                Check(scene.GameState.Oddball!.HoldTicks[1]+scene.GameState.Oddball.HoldTicks[2]+scene.GameState.Oddball.HoldTicks[3]+scene.GameState.Oddball.HoldTicks[4]+scene.GameState.Oddball.HoldTicks[5]+scene.GameState.Oddball.HoldTicks[6]+scene.GameState.Oddball.HoldTicks[7]>0,"bots score by carrying");
                Draw("01-bot-gameplay");
                // Controlled input / render checks after the independent bot run.
                OctolithFlagEntity? flag=null;foreach(var item in scene.GetOctolithFlagEntities()){flag=item;break;}Check(flag!=null,"one skull entity present");
                for(int i=1;i<8;i++){scene.Players.Items[i].IsBot=false;scene.Players.Items[i].Position=new Vector3(100+i*10,30,100);}
                scene.GameState.Oddball=new OddballMatch();for(int i=0;i<180;i++)scene.GameState.Oddball.Step(-1,-1);flag!.OddballReset();
                main.Spawn(flag.BasePosition,Vector3.UnitZ,Vector3.UnitY,main.NodeRef,true);main.ModSetAim(Vector3.UnitZ);
                setKey(keyboard,Keys.Z,false);scene.OnSimulationFrame();setKey(keyboard,Keys.Z,true);scene.OnSimulationFrame();setKey(keyboard,Keys.Z,false);scene.OnSimulationFrame();
                Check(main.ModOddballCarrying,"normal remappable Zoom input picks up skull");
                for(int i=0;i<60;i++)scene.OnSimulationFrame();Draw("02-carry-first-person");
                int points=scene.GameState.Oddball.Score(0);Check(points>=1,"human carry earns possession point");
                main.Controls.MoveUp.Type=ButtonType.Key;main.Controls.MoveUp.Key=Keys.W;
                main.Controls.MoveRight.Type=ButtonType.Key;main.Controls.MoveRight.Key=Keys.D;
                main.Controls.Jump.Type=ButtonType.Key;main.Controls.Jump.Key=Keys.Space;
                int carryClock=scene.GameState.Oddball.RemainingTicks;
                Vector3 moveStart=main.Position;
                setKey(keyboard,Keys.W,true);
                for(int i=0;i<12;i++){scene.OnSimulationFrame();Console.WriteLine($"CARRY INPUT {i}: key={keyboard.IsKeyDown(Keys.W)} bound={main.Controls.MoveUp.Key}/{main.Controls.MoveUp.Type} down={main.Controls.MoveUp.IsDown} input={input.GetType().GetProperty("HasInput")!.GetValue(input)} focus={GamepadContexts.Focused}/{scene.HardwareInputReady} pause={PauseMenu.Open}/{scene.GameState.MenuPause} position={main.Position} speed={main.Speed}");}
                setKey(keyboard,Keys.W,false);scene.OnSimulationFrame();
                Console.WriteLine($"CARRIER MOVE {moveStart} -> {main.Position}");
                Check(main.ModOddballCarrying && (main.Position-moveStart).Length>.1f,"carrier retains normal W movement");
                moveStart=main.Position;setKey(keyboard,Keys.D,true);
                for(int i=0;i<12;i++)scene.OnSimulationFrame();
                setKey(keyboard,Keys.D,false);scene.OnSimulationFrame();
                Check(main.ModOddballCarrying && (main.Position-moveStart).Length>.1f,"carrier can strafe");
                main.Controls.MouseAim=true;
                foreach(var device in new[]{PointerDeviceType.Mouse,PointerDeviceType.Pen,PointerDeviceType.Touch})
                {
                    PointerDevice.Reset();PointerInput.StylusMode=device!=PointerDeviceType.Mouse;StylusZone.Enabled=false;
                    Vector3 aimBefore=main.ModGunVector;
                    for(int i=0;i<8;i++)
                    {
                        setMouse(mouse,new(200+i*8,200+i*4));
                        PointerDevice.Update(new(device,1,200+i*8,200+i*4,true,true,true),1024,768);
                        scene.OnSimulationFrame();
                        if(i==7)Console.WriteLine($"POINTER SAMPLE {device}: "+input.GetType().GetProperty("MouseDeltaX")!.GetValue(input)+" / "+input.GetType().GetProperty("MouseDeltaY")!.GetValue(input));
                    }
                    Console.WriteLine($"CARRIER AIM {device}: {aimBefore} -> {main.ModGunVector}");
                    Check(main.ModOddballCarrying && (main.ModGunVector-aimBefore).Length>.01f,$"carrier {device} look updates through production input");
                }
                PointerDevice.Reset();PointerInput.StylusMode=false;main.Controls.MouseAim=false;main.ModSetAim(Vector3.UnitZ);
                float jumpY=main.Position.Y;setKey(keyboard,Keys.Space,true);
                for(int i=0;i<5;i++)scene.OnSimulationFrame();setKey(keyboard,Keys.Space,false);scene.OnSimulationFrame();
                Check(main.ModOddballCarrying && main.Position.Y>jumpY+.1f,"carrier can jump");
                for(int i=0;i<60;i++)scene.OnSimulationFrame();
                Check(scene.GameState.Oddball.RemainingTicks==carryClock,"carrier movement/aim/jump do not unpause clock");
                var swings=new System.Collections.Generic.List<int>();
                int lastSwing=main.ModOddballSwingCount;int projectilesBefore=0;
                foreach(var beam in scene.GetBeamProjectileEntities())if(beam.Lifespan>0)projectilesBefore++;
                setKey(keyboard,Keys.F,true);setKey(keyboard,Keys.W,true);moveStart=main.Position;
                for(int i=0;i<210;i++)
                {
                    scene.OnSimulationFrame();
                    if(main.ModOddballSwingCount!=lastSwing){swings.Add(i);lastSwing=main.ModOddballSwingCount;}
                }
                setKey(keyboard,Keys.F,false);setKey(keyboard,Keys.W,false);scene.OnSimulationFrame();
                Console.WriteLine("CARRIER SWINGS "+string.Join(",",swings));
                Check(swings.Count==6,"holding Fire repeats six swings across 210 simulation ticks");
                for(int i=1;i<swings.Count;i++)Check(swings[i]-swings[i-1]==39,"repeat melee interval is exactly 39 ticks / 0.65 seconds");
                Check(main.ModOddballCarrying && (main.Position-moveStart).Length>.1f,"moving remains available during repeated attacks");
                int projectilesAfter=0;foreach(var beam in scene.GetBeamProjectileEntities())if(beam.Lifespan>0)projectilesAfter++;
                Check(projectilesAfter<=projectilesBefore,"carrier Fire does not leak ordinary projectiles");
                for(int i=0;i<20;i++)scene.OnSimulationFrame();
                // Regression: preserve user-style unbound movement keys and exercise the
                // actual controller projection while drawing every gameplay frame.
                main.Controls.MoveUp.Key=Keys.Unknown; main.Controls.MoveRight.Key=Keys.Unknown;
                PadBindings.Set(PadAction.Zoom,GamepadButtons.RightBumper);
                PadBindings.Set(PadAction.Shoot,GamepadButtons.LeftBumper);
                PadBindings.Set(PadAction.NextWeapon,GamepadButtons.None);
                PadBindings.Set(PadAction.PrevWeapon,GamepadButtons.None);
                PadBindings.Set(PadAction.WeaponWheel,GamepadButtons.None);
                GamepadContexts.Focused=true;
                GamepadManager.UpdateDevice("oddball-carrier",new MphRead.Mods.Input.GamepadState{Connected=true},mapped:true);
                for(int i=0;i<3;i++)scene.OnSimulationFrame();
                main.Position=flag.BasePosition; main.ModSetAim(Vector3.UnitZ);
                moveStart=main.Position; Vector3 padAimBefore=main.ModGunVector;
                for(int i=0;i<24;i++){
                    GamepadManager.UpdateDevice("oddball-carrier",new MphRead.Mods.Input.GamepadState{Connected=true,LeftY=.7f,RightX=.4f,Buttons=GamepadButtons.LeftBumper},mapped:true);
                    scene.OnSimulationFrame();Draw();
                }
                Console.WriteLine($"PAD CARRY POSITION {moveStart} -> {main.Position}; aim {padAimBefore} -> {main.ModGunVector}");
                Check(main.ModOddballCarrying && (new Vector2(main.Position.X-moveStart.X,main.Position.Z-moveStart.Z)).Length>.3f,"carrier controller movement with unbound keyboard and per-frame rendering");
                Check((main.ModGunVector-padAimBefore).Length>.03f,"carrier controller aiming while holding melee");
                // Re-read the supplied profile through the real settings loader.
                // The path is an explicit local-test input, never bundled or saved back.
                if (Environment.GetEnvironmentVariable("ODDBALL_INPUT_PROFILE") is string profilePath)
                {
                    string previousDirectory=Launcher.LauncherPrefs.Directory;
                    try
                    {
                        Launcher.LauncherPrefs.Directory=Path.GetDirectoryName(profilePath)!;
                        InputSettings.Load();InputSettings.Apply(main.Controls);
                        Check(main.Controls.MoveUp.Key==Keys.Unknown && main.Controls.Shoot.Key==Keys.Unknown,"actual supplied unbound keyboard profile is active");
                        main.Controls.MouseAim=true;
                        DsLayout.Enabled=true;DsLayout.ScreenAspect="5:3";PointerInput.StylusMode=true;scene.OnResize();
                        flag.OddballReset();main.Spawn(flag.BasePosition,Vector3.UnitZ,Vector3.UnitY,main.NodeRef,true);
                        PointerDevice.Reset();
                        for(int i=0;i<4;i++){
                            GamepadManager.UpdateDevice("oddball-carrier",new MphRead.Mods.Input.GamepadState{Connected=true},mapped:true);
                            scene.OnSimulationFrame();Draw();
                        }
                        GamepadManager.UpdateDevice("oddball-carrier",new MphRead.Mods.Input.GamepadState{Connected=true,Buttons=GamepadButtons.RightBumper},mapped:true);
                        scene.OnSimulationFrame();Draw();
                        Check(main.ModOddballCarrying,"actual profile Right Bumper pickup");
                        // Keep the source profile's stylus settings; feed pen samples to
                        // the centre of its aiming zone (not the weapon-select controls).
                        var zone=(X:StylusZone.Left*1024f,Y:StylusZone.Top*768f,Width:StylusZone.Width*1024f,Height:StylusZone.Height*768f);
                        Console.WriteLine("USER PROFILE AIM REGION "+zone);
                        moveStart=main.Position;Vector3 actualAimBefore=main.ModGunVector;int actualSwings=main.ModOddballSwingCount;
                        for(int i=0;i<24;i++)
                        {
                            GamepadManager.UpdateDevice("oddball-carrier",new MphRead.Mods.Input.GamepadState{Connected=true,LeftY=.65f,Buttons=GamepadButtons.LeftBumper},mapped:true);
                            float x=zone.X+zone.Width*.45f+i*1.5f,y=zone.Y+zone.Height*.4f+i*.5f;
                            setMouse(mouse,new(x,y));PointerDevice.Update(new(PointerDeviceType.Pen,1,x,y,true,true,true),1024,768);
                            scene.OnSimulationFrame();Draw();
                        }
                        Console.WriteLine($"ACTUAL PROFILE CARRY {moveStart} -> {main.Position}; aim {actualAimBefore} -> {main.ModGunVector}");
                        Check(main.ModOddballCarrying && (main.Position-moveStart).Length>.2f,"actual profile controller movement with stylus and repeated melee");
                        Check((main.ModGunVector-actualAimBefore).Length>.01f,"actual profile pen aim while carrying in stacked 5:3");
                        Check(main.ModOddballSwingCount>actualSwings,"actual profile Left Bumper melee repeats without discarding movement");
                        Draw("04d-actual-profile-carrier");
                    }
                    finally {Launcher.LauncherPrefs.Directory=previousDirectory;}
                    PointerDevice.Reset();PointerInput.StylusMode=false;DsLayout.Enabled=false;scene.OnResize();
                    main.Controls.Zoom.Type=ButtonType.Key;main.Controls.Zoom.Key=Keys.Z;
                    main.Controls.Shoot.Type=ButtonType.Key;main.Controls.Shoot.Key=Keys.F;main.Controls.MouseAim=false;
                }
                GamepadManager.RemoveDevice("oddball-carrier");
                for(int i=0;i<20;i++)scene.OnSimulationFrame();
                // Return to the authored center so melee/scoreboard checks use a known clear floor.
                main.Position=flag.BasePosition;main.ModSetAim(Vector3.UnitZ);
                for(int i=0;i<12;i++)scene.OnSimulationFrame();
                var victim=scene.Players.Items[1];victim.Controls.ClearAll();
                victim.Spawn(main.Position+Vector3.UnitZ*1.4f,-Vector3.UnitZ,Vector3.UnitY,main.NodeRef,true);
                victim.ModSetAim(-Vector3.UnitZ);main.ModSetAim(Vector3.UnitZ);
                int beforeMelee=victim.Health;
                setKey(keyboard,Keys.F,true);scene.OnSimulationFrame();setKey(keyboard,Keys.F,false);scene.OnSimulationFrame();Draw("03-melee");
                Console.WriteLine($"MELEE fixture health {beforeMelee}->{victim.Health}, maximum {victim.HealthMax}, distance {(victim.Position-main.Position).Length}, aim {main.ModGunVector}, enemy aim {victim.ModGunVector}");
                Check(victim.Health>0 && victim.Health<beforeMelee,"normal FIRE melee damages frontal enemy without one-shotting");
                for(int i=0;i<PlayerEntity.OddballMeleeIntervalTicks;i++)scene.OnSimulationFrame();
                victim.Position=main.Position+Vector3.UnitZ*1.4f;victim.ModSetAim(-Vector3.UnitZ);main.ModSetAim(Vector3.UnitZ);
                setKey(keyboard,Keys.F,true);scene.OnSimulationFrame();setKey(keyboard,Keys.F,false);scene.OnSimulationFrame();
                Check(victim.Health==0,"second legal frontal melee defeats enemy");
                var savedProfile=Render.Hud.HudProfiles.CopyCurrent();
                var themed=Render.Hud.HudProfileDefaults.Create("Halo");themed.AccentColor="#62BFE4";
                Render.Hud.HudProfiles.Publish(themed);
                PlayerEntity.ModForceScoreboard=true;Draw("04-scoreboard");PlayerEntity.ModForceScoreboard=false;
                themed.Elements["match.oddballStatus"].OffsetX=140;
                themed.Elements["match.timer"].OffsetX=110;
                Render.Hud.HudProfiles.Publish(themed);Draw("04a-edited-hud-palette");
                themed.Elements["match.oddballStatus"].OffsetX=22.5f;themed.Elements["match.timer"].OffsetX=0;
                Render.Hud.HudProfiles.Publish(themed);
                // Continuous rendering catches pooled-packet lifetime regressions, not just a first frame.
                for(int i=0;i<24;i++){scene.OnSimulationFrame();Draw();}
                DsLayout.Enabled=true;DsLayout.ScreenAspect="5:3";PointerInput.StylusMode=true;scene.OnResize();
                Draw("04b-stacked-5x3");
                PlayerEntity.ModForceScoreboard=true;Draw("04c-stacked-scoreboard");PlayerEntity.ModForceScoreboard=false;
                PointerInput.StylusMode=false;DsLayout.Enabled=false;scene.OnResize();
                setKey(keyboard,Keys.Z,true);scene.OnSimulationFrame();setKey(keyboard,Keys.Z,false);scene.OnSimulationFrame();Check(!main.ModOddballCarrying,"normal Zoom input drops skull");
                for(int i=0;i<20;i++)scene.OnSimulationFrame();Draw("05-dropped");
                main.TakeDamage(1000,DamageFlags.IgnoreInvuln,null,victim);
                scene.OnSimulationFrame();Check(main.Health==0 && main.ModOddballRespawnText.Length>0,"dead carrier has native respawn text");
                Draw("05a-respawn-gameplay");PlayerEntity.ModForceScoreboard=true;Draw("05b-respawn-scoreboard");PlayerEntity.ModForceScoreboard=false;
                for(int i=0;i<610;i++)scene.OnSimulationFrame();Check(main.Health>0,"respawn completes without a carrier input lock");
                scene.GameState.Oddball.ScoreTicks[0]=5999;for(int i=0;i<32;i++)scene.OnSimulationFrame();main.Position=flag.Position.AddY(-1);Check(flag.OddballTryPickup(main,false),"recover skull for round-boundary check");scene.OnSimulationFrame();Check(scene.GameState.Oddball.RoundsWon[0]==1,"native round victory");Draw("06-round-win");
                for(int i=0;i<485;i++)scene.OnSimulationFrame();Check(scene.GameState.Oddball.RoundNumber==2 && main.Health>0,"native next round respawns player");Draw("07-next-round");
                main.Position=flag.BasePosition;main.ModSetAim(Vector3.UnitZ);
                scene.GameState.Oddball.ScoreTicks[0]=5999;
                Check(flag.OddballTryPickup(main,false),"second-round objective pickup");scene.OnSimulationFrame();
                Check(scene.GameState.Oddball.Winner==0 && scene.GameState.MatchState==MatchState.GameOver,"native 2-round match completion");
                Draw("08-match-won");
                for(int i=0;i<1200 && scene.GameState.MatchState!=MatchState.Ending;i++)scene.OnSimulationFrame();
                Check(scene.GameState.MatchState==MatchState.Ending,"results countdown progresses after Oddball clock stops");
                Draw("09-final-results");
                File.WriteAllText(Path.Combine(output,"PASS.txt"),$"Native offline 8-player world: 9000 ticks, {pickups} bot pickups. Production key-input pickup/drop, carrier movement/strafe/jump and Mouse/Pen/Touch look, six held-Fire swings exactly 39 ticks apart with movement and no weapon leakage, first-person skull, CTF-style scoreboard, 5:3 stacked display, round transition, two-round native match completion and final results (near-threshold fixtures). No online or Windows hardware claim.");
                Console.WriteLine("ODDBALL RENDER CHECKS PASS");return 0;
            }finally{scene?.DoCleanup();scene?.UnloadGl();}
        }catch(Exception e){Console.Error.WriteLine(e);Console.WriteLine("ODDBALL RENDER CHECKS FAILED");return 1;}
    }
}
#endif
