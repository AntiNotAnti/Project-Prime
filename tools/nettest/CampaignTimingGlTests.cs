using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Campaign;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Clock=MphRead.Mods.Campaign.CampaignTimeRecords;

namespace MphRead.NetTest;

// Opt-in native Renderer clock acceptance. Only hashes/counts leave this tool:
// no screenshots, source media, fixture-save files, or campaign save writes.
internal static class CampaignTimingGlTests
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Failure:Exception{internal Failure(string label):base(label){}}
    private sealed record Manifest(int Files,long Bytes,string Sha256);
    private static int checks;
    private static readonly Stopwatch deadline=new();
    private static void Check(bool value,string label){checks++;if(!value)throw new Failure(label);}
    private static void Bound(){if(deadline.Elapsed>TimeSpan.FromSeconds(120))throw new Failure("Native timing GL exceeded120-second scene budget");}
    internal static int Run(string[] args)
    {
        if(args.Length!=2||!Directory.Exists(args[1])){Console.Error.WriteLine("Usage: --campaign-timing-gl OWNED_AMHE1_ROOT");return 2;}
        string assets=Path.GetFullPath(args[1]),root=Path.Combine(Path.GetTempPath(),"prime-campaign-timing-gl-"+Guid.NewGuid().ToString("N"));
        string priorDirectory=Directory.GetCurrentDirectory();string? priorData=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        byte priorSlot=Menu.SaveSlot;SaveWhen priorSave=Menu.NeededSave;bool priorSkip=Cheats.SkipPlanetIntros,priorFocus=GamepadContexts.Focused;
        int priorCap=FrameTiming.FrameRateCap;string priorKey=Paths.MphKey,priorAssets=Paths.FileSystem;
        Manifest? before=null,after=null;string stage="Initialize",status="FAIL",renderer="",version="",failureType="",failureLabel="",failureHash="";
        string binaryBefore=HashFile(typeof(Scene).Assembly.Location),binaryAfter=binaryBefore;bool savesAbsent=true;var cases=new List<object>();
        checks=0;
        try
        {
            Directory.CreateDirectory(root);Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",Path.Combine(root,"data"));Directory.SetCurrentDirectory(root);
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh ordinary offline GL process required");
            Paths.MphKey="AMHE1";Paths.SetPath(Paths.MphKey,assets);
            stage="HashOwnedBefore";before=HashOwnedRoot(assets);
            Menu.SaveSlot=0;Menu.NeededSave=SaveWhen.Never;Cheats.SkipPlanetIntros=true;GamepadContexts.Focused=true;GamepadManager.ClearAll();PointerDevice.Reset();
            stage="CreateNativeContext";deadline.Restart();
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new Vector2i(640,480);settings.StartVisible=true;settings.StartFocused=false;
            settings.Title="Project Prime native campaign clock diagnostic";
            using(var window=new NativeWindow(settings))
            using(var graphics=new DesktopGraphicsSession(window))
            {
                GLFW.SwapInterval(0);renderer=GL.GetString(StringName.Renderer);version=GL.GetString(StringName.Version);
                RunScene(window,cases,label=>stage=label);
            }
            stage="SaveAbsence";savesAbsent=!Directory.Exists(Path.Combine(root,"Savedata"));Check(savesAbsent,"slot0/Never native lifecycle created no save directory");
            stage="HashOwnedAfter";after=HashOwnedRoot(assets);Check(before==after,"entire owned root path/length/hash inventory unchanged");
            binaryAfter=HashFile(typeof(Scene).Assembly.Location);Check(binaryBefore==binaryAfter,"native core binary unchanged during diagnostic");
            status="PASS";return 0;
        }
        catch(Exception error)
        {failureType=error.GetType().Name;failureHash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(error.Message)));if(error is Failure)failureLabel=error.Message;return 1;}
        finally
        {
            if(before!=null&&after==null){try{after=HashOwnedRoot(assets);}catch(Exception error){failureType+="/"+error.GetType().Name;}}
            try{binaryAfter=HashFile(typeof(Scene).Assembly.Location);}catch(Exception error){failureType+="/"+error.GetType().Name;}
            Menu.SaveSlot=priorSlot;Menu.NeededSave=priorSave;Cheats.SkipPlanetIntros=priorSkip;GamepadContexts.Focused=priorFocus;
            FrameTiming.FrameRateCap=priorCap;FrameTiming.Reset();PointerDevice.Reset();GamepadManager.ClearAll();Read.ClearCache();
            Paths.MphKey=priorKey;Paths.SetPath(priorKey,priorAssets);
            Directory.SetCurrentDirectory(priorDirectory);Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",priorData);
            if(Directory.Exists(root))Directory.Delete(root,true);
            Console.WriteLine(JsonSerializer.Serialize(new{Schema=1,Status=status,Stage=stage,checks,FailureType=failureType,FailureLabel=failureLabel,FailureMessageSha256=failureHash,
                renderer,version,Width=640,Height=480,BinarySha256Before=binaryBefore,BinarySha256After=binaryAfter,OwnedBefore=before,OwnedAfter=after,
                TemporaryContextDeleted=!Directory.Exists(root),SaveDirectoryAbsent=savesAbsent,MediaCaptured=false,SourcePayloadsWritten=false,cases,
                Scope="Actual owned room45 native Renderer timing hook and GL; virtual picture cadences, synthetic keyboard pause, explicit dialog/state/Mode predicate controls. No achieved displayHz, physical input, movie playback, replica, replay, network, custom-room or full multiplayer-world qualification."
            },new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private static void RunScene(NativeWindow window,List<object> cases,Action<string> stage)
    {
        var keyboard=SyntheticInput.CreateKeyboard();var setKey=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
        Scene? scene=null;GameMode priorMode=GameMode.SinglePlayer;MatchState priorState=MatchState.InProgress;
        try
        {
            stage("LoadOwnedNativeScene");Bound();
            scene=new Scene(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            scene.GameState.StartNewSave();scene.AddPlayer(Hunter.Samus);scene.AddRoom("UNIT2_LAND",GameMode.SinglePlayer,playerCount:1);
            MphRead.Sound.Sfx.Load(scene);scene.OnLoad();scene.OnResize();var player=scene.Players.Main;
            foreach(var control in player.Controls.All){control.Type=ButtonType.Key;control.Key=Keys.Unknown;}
            player.Controls.Shoot.Key=Keys.Enter;player.Controls.Pause.Key=Keys.Tab;player.Controls.MouseAim=false;player.Controls.KeyboardAim=false;
            setKey(keyboard,Keys.Enter,true);int spawn=0;while(spawn<240&&!player.LoadFlags.TestFlag(LoadFlags.Spawned)){Step();spawn++;}
            setKey(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();for(int i=0;i<90;i++)Step();scene.CameraSequences.Current?.End();
            priorMode=scene.GameState.Mode;priorState=scene.GameState.MatchState;
            var stats=scene.GameState.StorySave.Stats;var record=stats.CampaignTime;
            Check(scene.RoomId==45&&scene.GameState.SinglePlayer&&scene.CameraMode==CameraMode.Player&&player.LoadFlags.TestFlag(LoadFlags.Spawned),"actual local native room45 player settled");
            Check(record is {Version:1,PlayTicks:>=0,GoreaFirstTicks:0,GoreaSecondTicks:0},"fresh factory record retained through real spawn/warmup");
            Check(scene.GameState.MatchState==MatchState.InProgress&&!scene.GameState.MenuPause&&!scene.GameState.DialogPause,"settled native scene is eligible before timing baseline");
            Check(!scene.MoviePlaying,"no movie playing during clock assertions");
            stage("DrawOnly");long before=Ticks();ulong simulationFrame=scene.FrameCount;for(int i=0;i<20;i++)Draw();
            Check(Ticks()==before&&scene.FrameCount==simulationFrame,"twenty draw-only pictures advance no timing or simulation frames");
            cases.Add(new{Kind="DrawOnly",Pictures=20,FixedSteps=0,PlayTickDelta=Ticks()-before});
            foreach(int hz in new[]{60,120,144})
            {
                stage("VirtualCadence"+hz);FrameTiming.Reset();FrameTiming.ResetDiagnostics();FrameTiming.FrameRateCap=hz;
                int ticks=0,pictures=0;before=Ticks();simulationFrame=scene.FrameCount;
                while(ticks<120)
                {
                    Bound();int due=FrameTiming.Advance(1d/hz);
                    for(int step=0;step<due&&ticks<120;step++)
                    {
                        long priorTicks=Ticks();ulong priorFrame=scene.FrameCount;scene.OnSimulationFrame();ticks++;
                        Check(scene.FrameCount==priorFrame+1&&Ticks()==priorTicks+1,"one actual simulation step advances exactly one60Hz campaign tick");
                    }
                    long beforeDraw=Ticks();Draw();pictures++;Check(Ticks()==beforeDraw,"native world/HUD draw never records an extra gameplay tick");
                    Check(pictures<400,"virtual cadence picture count bounded");
                }
                Check(Ticks()-before==120&&scene.FrameCount-simulationFrame==120&&FrameTiming.DroppedSteps==0&&FrameTiming.Stalls==0,"each virtual cadence preserves same120 fixed clock steps");
                Check(ReferenceEquals(stats,scene.GameState.StorySave.Stats)&&ReferenceEquals(record,stats.CampaignTime)&&record!.GoreaFirstTicks==0&&record.GoreaSecondTicks==0,"cadence retained original live record and unchanged unrelated Gorea phase counters");
                cases.Add(new{Kind="Cadence",VirtualHostHz=hz,FixedSteps=ticks,Pictures=pictures,PlayTickDelta=Ticks()-before,DroppedSteps=FrameTiming.DroppedSteps,Stalls=FrameTiming.Stalls});
            }
            stage("NativeMenuPause");TogglePause();Check(scene.GameState.MenuPause,"synthetic native pause binding created actual menu/HUD owner");
            before=Ticks();for(int i=0;i<6;i++)Step();Check(Ticks()==before,"native menu pause excludes six actual simulation calls from clock");
            cases.Add(new{Kind="NativeMenuPause",FixedSteps=6,PlayTickDelta=Ticks()-before});TogglePause();Check(!scene.GameState.MenuPause,"native menu pause exited through same binding");
            stage("DialogPause");scene.GameState.PauseDialog();scene.GameState.ApplyPause();Check(scene.GameState.DialogPause,"explicit dialog pause applied by existing owner");
            before=Ticks();for(int i=0;i<6;i++)Step();Check(Ticks()==before,"applied dialog pause excludes six actual simulation calls from clock");
            cases.Add(new{Kind="DialogPause",FixedSteps=6,PlayTickDelta=Ticks()-before});scene.GameState.UnpauseDialog();scene.GameState.ApplyPause();Check(!scene.GameState.DialogPause,"dialog pause released");
            stage("NotInProgress");scene.GameState.MatchState=MatchState.Disconnected;before=Ticks();for(int i=0;i<3;i++)Step();Check(Ticks()==before,"Disconnected match-state predicate excludes actual steps");
            cases.Add(new{Kind="NotInProgress",FixedSteps=3,PlayTickDelta=Ticks()-before});scene.GameState.MatchState=priorState;
            stage("MultiplayerPredicate");scene.GameState.Mode=GameMode.Battle;before=Ticks();Step();Check(Ticks()==before,"temporary multiplayer Mode predicate excludes actual step");
            cases.Add(new{Kind="MultiplayerPredicateOnly",FixedSteps=1,PlayTickDelta=Ticks()-before,MatchStateAfterPredicate=scene.GameState.MatchState});
            scene.GameState.Mode=priorMode;scene.GameState.MatchState=priorState;
            stage("EligibleResume");before=Ticks();Step();Check(Ticks()==before+1,"restoring ordinary native eligibility resumes one exact tick");
            cases.Add(new{Kind="EligibleResume",FixedSteps=1,PlayTickDelta=Ticks()-before,SpawnSteps=spawn});
            long Ticks()=>Clock.Capture(stats).PlayTicks??throw new Failure("live timing unexpectedly became unknown");
            void Step(){Bound();scene.OnSimulationFrame();Draw();}
            void Draw(){Bound();scene.OnDrawFrame();Check(scene.OnRenderFrame(),"actual native scene render active");DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();}
            void TogglePause(){setKey(keyboard,Keys.Tab,true);Step();setKey(keyboard,Keys.Tab,false);Step();}
        }
        catch
        {
            if(scene!=null)cases.Add(new{Kind="FailureState",scene.RoomId,scene.FrameCount,scene.GameState.Mode,scene.GameState.MatchState,
                scene.GameState.MenuPause,scene.GameState.DialogPause,scene.MoviePlaying,scene.CameraMode,
                Timing=Clock.Capture(scene.GameState.StorySave.Stats)});
            throw;
        }
        finally
        {
            if(scene!=null)
            {
                scene.GameState.Mode=priorMode;scene.GameState.MatchState=priorState;
                if(scene.GameState.MenuPause){scene.GameState.UnpauseMenu();scene.Players.Main.EndMenuPauseHud();}
                scene.GameState.UnpauseDialog();scene.GameState.ApplyPause();Menu.SaveSlot=0;Menu.NeededSave=SaveWhen.Never;
                MomentumSceneTests.Release(scene);
            }
        }
    }
    private static Manifest HashOwnedRoot(string root)
    {
        for(DirectoryInfo? directory=new(root);directory!=null;directory=directory.Parent)
            if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new Failure("owned source ancestry must not contain symlinks");
        var files=new List<string>();long total=0;
        int entries=0;
        void Walk(string directory,int depth=0)
        {
            if(depth>64)throw new Failure("owned source directory depth exceeds bound");
            foreach(string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if(++entries>65536)throw new Failure("owned source entry inventory exceeds bound");
                var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new Failure("owned source entry must not be a symlink");
                if((attributes&FileAttributes.Directory)!=0)Walk(path,depth+1);
                else{if(files.Count>=65536)throw new Failure("owned root exceeds bounded file inventory");long length=new FileInfo(path).Length;total=checked(total+length);if(total>2L*1024*1024*1024)throw new Failure("owned root exceeds2GiB hash budget");files.Add(path);}
            }
        }
        Walk(root);Check(files.Count>0,"owned source inventory nonempty");files.Sort(StringComparer.Ordinal);using var manifest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(string path in files)
        {string relative=Path.GetRelativePath(root,path).Replace('\\','/');string row=relative+'\0'+new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture)+'\0'+HashFile(path)+'\n';manifest.AppendData(Encoding.UTF8.GetBytes(row));}
        return new(files.Count,total,Convert.ToHexStringLower(manifest.GetHashAndReset()));
    }
    private static string HashFile(string path)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);long length=input.Length;if(length>2L*1024*1024*1024)throw new Failure("file exceeds bounded streaming hash extent");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[65536];long remaining=length;
        while(remaining>0){int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(read==0)throw new Failure("source hash input truncated");hash.AppendData(buffer,0,read);remaining-=read;}
        if(input.ReadByte()!=-1||input.Length!=length)throw new Failure("source hash input extent changed");return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
