using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher;
using MphRead.Mods.Render.Characters;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Text;
using System.Diagnostics;
using MphRead.Mods.Launcher.RmlUi.Host;

// Owned runtime acceptance is separate from the authoring-only source fixture.
// The tests-first admission check intentionally runs before any owned media I/O.
internal static class CharacterDroneRuntimeOwnedProbe
{
    internal static int Run(string source,bool probeBurst=false)
    {
        int checks=0;string? failed=null,error=null;var milestones=new Milestones();object? runtime=null;
        string core=typeof(Scene).Assembly.Location,tool=typeof(CharacterDroneRuntimeOwnedProbe).Assembly.Location;
        string coreBefore=HashFile(core),toolBefore=HashFile(tool);
        void Check(bool condition,string why)
        {if(!condition){failed=why;throw new InvalidDataException(why);}checks++;}
        try
        {
            if(probeBurst)
            {
                var pose=typeof(Scene).Assembly.GetType("MphRead.Mods.Render.Characters.CharacterSourceDronePose");
                Check(pose!=null,"source drone burst pose contract exists before any owned-source/native-asset IO");
                var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
                var required=pose!.GetProperty("RequiredOrdinals",flags)?.GetValue(null)??pose.GetField("RequiredOrdinals",flags)?.GetValue(null);
                Check(required is IEnumerable<int> ordinals&&ordinals.SequenceEqual(new[]{13,75,73,74}),"burst pose captures only Ready/unfold/fire/fold closed source ordinals");
                Check(typeof(CharacterSourceDroneOwner).GetProperty("Pose",Hidden)?.PropertyType==pose
                    &&pose.GetProperty("CurrentLocalPose",Hidden|BindingFlags.Public)!=null
                    &&pose.GetMethod("Sample",Hidden|BindingFlags.Public,null,[typeof(int),typeof(int)],null)!=null,
                    "complete captured burst pose and native-state/frame sampling belong to separate source drone owner");
            }
            var appearance=typeof(LaunchPlan).Assembly.GetType("MphRead.Mods.Launcher.LocalDroneAppearance");
            Check(appearance is{IsEnum:true}&&appearance.IsPublic,
                "independent LocalDroneAppearance runtime contract exists before owned-source or native-asset I/O");
            Check(Enum.GetNames(appearance!).SequenceEqual(new[]{"Native","Alpha","Gamma"})
                &&Enum.GetValues(appearance!).Cast<object>().Select(Convert.ToInt32).SequenceEqual(new[]{0,1,2}),
                "local drone presentation identity appends no Hunter/gameplay/wire capability");
            Check(typeof(LaunchPlan).GetProperty("LocalDrone")?.PropertyType==appearance
                &&typeof(LauncherPrefs).GetProperty("LocalDrone",BindingFlags.Public|BindingFlags.Static)?.PropertyType==appearance,
                "detached launch plan and persisted preference carry the exact local drone identity");
            Check(typeof(LauncherHunterChoice).GetProperty("Drone")?.PropertyType==appearance
                &&typeof(LauncherHunterChoice).GetConstructors().Any(c=>c.GetParameters() is var p&&p.Length==4&&p[3].ParameterType==appearance&&p[3].IsOptional),
                "Hunters choice adds optional fourth drone preference without changing existing constructor defaults");
            var rules=typeof(LaunchPlan).Assembly.GetType("MphRead.Mods.Launcher.LocalDroneAppearanceRules");
            Check(rules?.GetMethods(BindingFlags.Public|BindingFlags.Static).Any(m=>m.Name=="TryAdmit")==true
                &&typeof(CharacterSourceDroneSelection).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Any(m=>m.Name=="Load"),
                "separate closed drone admission and same-generation source selection precede actual Scene creation");
            runtime=Smoke(source,Check,milestones);
        }
        catch(Exception ex){error=ex.GetType().Name;Console.Error.WriteLine("OWNED DRONE RUNTIME failure: "+ex);}
        finally
        {
            try{Check(HashFile(core)==coreBefore&&HashFile(tool)==toolBefore,"test-first runtime contract check preserves core/tool bytes");}
            catch(Exception ex){error??=ex.GetType().Name;}
            Console.WriteLine(JsonSerializer.Serialize(new{Pass=error==null,Checks=checks,FailedCheck=failed,ErrorType=error,BurstProbe=probeBurst,
                CoreSha256=coreBefore,ToolSha256=toolBefore,SourceIoAttempted=milestones.SourceIo,SourceSha256Before=milestones.SourceBefore,SourceSha256After=milestones.SourceAfter,
                NativeSha256Before=milestones.NativeBefore,NativeSha256After=milestones.NativeAfter,TemporaryDerivedDataDeleted=milestones.Cleaned,
                Runtime=runtime,NativeAssetIoAttempted=milestones.NativeIo,GraphicsContextActivated=milestones.Context,RenderingActivated=milestones.Rendering,GameplayActivated=milestones.Gameplay,
                Scope=!milestones.SourceIo?"tests-first source-drone runtime contract only, before owned media/native assets/context/gameplay IO":"actual MatchStart/LoadScene/keyboard native vs Alpha/Gamma fixed trace, complete own33rig/1390tri/twoGX source presentation and native laser fallback; separate guard/fractional/lifecycle fixtures; native death and accepted presentation status injections labeled",
                Unqualified=new[]{"physical controls","native menu clicks","Android/macOS","online/replay","source burst/laser clips","opponent combat balance","achieved refresh rate","visual art completeness"}}));
        }
        return error==null?0:1;
    }
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Milestones{internal bool SourceIo,NativeIo,Context,Rendering,Gameplay;internal bool Cleaned=true;internal string? SourceBefore,SourceAfter,NativeBefore,NativeAfter;}
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static object Smoke(string source,Action<bool,string> check,Milestones milestones)
    {
        string temporary=Path.Combine(Path.GetTempPath(),"prime-drone-runtime-"+Guid.NewGuid().ToString("N")),cwd=Environment.CurrentDirectory;
        string files=GameFiles.Root,prefs=LauncherPrefs.Directory;string? data=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA"),native=Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS");
        var drone=LauncherPrefs.LocalDrone;var body=LauncherPrefs.LocalBody;byte slot=Menu.SaveSlot;
        bool hd=RenderOptions.CharacterModelReplacements,bright=RenderOptions.BrightSkins,cosmetics=RenderOptions.ShowCustomCosmetics,force=MapAudit.ForceEveryone;
        var shadows=RenderOptions.Shadows;bool focus=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible;
        milestones.Cleaned=false;milestones.SourceIo=true;string before=HashFile(source),nativeBefore="";milestones.SourceBefore=before;RenderWindow? window=null;var cases=new List<object>();var traces=new List<string[]>();var captures=new List<object>();string? captureRoot=Environment.GetEnvironmentVariable("PRIME_DRONE_TEST_CAPTURE_DIR");
        try
        {
            check(OperatingSystem.IsWindows()&&!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive&&!ReplayCapture.LiveRecordingActive,"actual offline Windows GPU process");
            if(!string.IsNullOrWhiteSpace(captureRoot)){captureRoot=Path.GetFullPath(captureRoot);check(captureRoot.StartsWith(Path.GetFullPath(Path.Combine(cwd,"artifacts"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"optional drone pictures stay only in ignored worktree artifacts");Directory.CreateDirectory(captureRoot);}
            check(before=="3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844"&&native!=null&&Directory.Exists(native),"exact owned PrimeRev2 and existing AMHE1 source before import");milestones.NativeIo=true;nativeBefore=Inventory(native!);milestones.NativeBefore=nativeBefore;
            string user=Path.Combine(temporary,"user");Directory.CreateDirectory(user);File.WriteAllText(Path.Combine(user,"paths.txt"),"0.19.0.0\n"+Ver.AMHE1+"="+native+"\nExport="+Path.Combine(user,"Export")+"\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",user);Directory.SetCurrentDirectory(user);GameFiles.Root=LauncherPrefs.Directory=user;Menu.SaveSlot=0;
            ConsoleSetup.Run();Paths.UpdatePaths();Paths.MphKey=Ver.AMHE1;GraphicsBackendPolicy.Configure("opengl");RenderOptions.CharacterModelReplacements=false;RenderOptions.BrightSkins=false;RenderOptions.ShowCustomCosmetics=true;RenderOptions.Shadows=ShadowQuality.Off;MapAudit.ForceEveryone=true;GamepadManager.ClearAll();GamepadContexts.Focused=true;GamepadContexts.MenuVisible=false;
            string imports=RetroCharacterPreviewExporter.DefaultRoot;check(Path.GetFullPath(imports).StartsWith(Path.GetFullPath(temporary)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"actual default import root isolated");
            RetroCharacterPreviewExporter.ExportFromOwnedGame(source,selectedIds:["trooper-drone-alpha","trooper-drone-gamma"]);
            foreach(string id in new[]{"trooper-drone-alpha","trooper-drone-gamma"})
            {
                var selected=CharacterSourceDroneSelection.Load(id,CancellationToken.None);check(selected.Pack.TryResolvePrivate(Hunter.Weavel,selected.Appearance,CharacterModelPart.AlternateForm,0,out var asset)&&selected.Pack.HasSourceDrone(asset),"actual source selection captures own Pack before Scene: "+id);
                Console.WriteLine(JsonSerializer.Serialize(new{Probe="source-drone-cpu-load",Id=id,selected.Generation}));
            }
            var settings=new MenuSettings{TeamPlay="off",ThirdPersonCamera="on",SpawnProtection="off",MovementModel="Native"};
            CheckPolicies(settings,check);
            window=new RenderWindow(shell:true);window.ClientSize=new(640,480);window.Context.MakeCurrent();milestones.Context=true;GLFW.SwapInterval(0);
            foreach(var appearance in new[]{LocalDroneAppearance.Native,LocalDroneAppearance.Alpha,LocalDroneAppearance.Gamma})
            {
                LauncherPrefs.LocalDrone=appearance;LauncherPrefs.LocalBody=LocalBodyAppearance.Native;var plan=OfflineLaunch.Preview(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Weavel,0,0,0,OfflineCharacterMode.TrooperScoutDronePractice);LauncherPrefs.LocalDrone=LocalDroneAppearance.Native;
                foreach(var key in Used)Key(window.KeyboardState,key,false);
                check(plan.LocalDrone==appearance&&MatchStart.Begin(window,settings,plan),"real detached plan MatchStart/LoadScene binds initialized drone: "+appearance+" "+MatchStart.LastError);milestones.Gameplay=true;
                var scene=window.Scene;var player=scene.Players.Main;var owner=player.ModSourceDrone;var nativeModel=player.ModSourceNativeDrone!;
                check(player.ScoutDroneEnabled&&player.ModCharacterGameplay==CharacterGameplayRules.TrooperDroneV1&&player.ModSourceBody==null&&nativeModel.Name=="PsychoBit"&&player.DroneBeams.Count==40,"actual native practice/model/40beam pool remains authoritative");
                check(appearance==LocalDroneAppearance.Native?owner==null:owner is{Current:true}&&owner.Model.Joints.Count==33&&owner.Model.IndexCount==1390*3&&owner.Model.Segments.Count==2,"source owner is complete and separate from unchanged body owner");
                Console.WriteLine(JsonSerializer.Serialize(new{Probe="source-drone-match-loaded",Appearance=appearance.ToString(),Generation=owner?.Generation}));
                var input=typeof(Scene).GetField("_inputMode",Hidden)!;input.SetValue(scene,Enum.Parse(input.FieldType,"All"));scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
                foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}player.Controls.Shoot.Key=Keys.Enter;player.Controls.Morph.Key=Keys.B;player.Controls.AltAttack.Key=Keys.F;player.Controls.Jump.Key=Keys.Space;player.Controls.Zoom.Key=Keys.Z;player.Controls.MoveUp.Key=player.Controls.RollUp.Key=Keys.W;player.Controls.MouseAim=false;player.Controls.KeyboardAim=false;
                Key(window.KeyboardState,Keys.Enter,true);for(int n=0;n<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);n++)scene.OnSimulationFrame();Key(window.KeyboardState,Keys.Enter,false);scene.CameraSequences.Current?.End();for(int n=0;n<240;n++)scene.OnSimulationFrame();scene.CameraSequences.Current?.End();
                check(player.Health>0&&!player.IsAltForm,"actual native spawn reaches living biped");
                var result=Trace(window,scene,player,owner,appearance,imports,check,captureRoot,captures,()=>milestones.Rendering=true);traces.Add(result.Trace);
                if(traces.Count>1){check(result.Trace.Length==traces[0].Length,"native/source trace lengths match");int mismatch=Enumerable.Range(0,result.Trace.Length).FirstOrDefault(i=>result.Trace[i]!=traces[0][i],-1);check(mismatch<0,"native gameplay/body/beams/laser/animation/RNG exact parity: "+appearance+" first mismatch="+mismatch);}
                cases.Add(new{Appearance=appearance.ToString(),Frames=result.Trace.Length,GameplayTraceSha256=Sha(Encoding.UTF8.GetBytes(string.Join("\n",result.Trace))),result.Metadata});int[] lists=owner?.Model.Segments.Select(s=>s.ListId).ToArray()??[];
                window.EndScene();check(owner==null||!owner.Current&&!owner.Ready&&lists.All(id=>!GL.IsList(id)),"actual EndScene retires complete source drone");
            }
            Lifecycle(window,settings,source,imports,check);
            return new{Cases=cases.ToArray(),Captures=captures.ToArray(),Renderer=GL.GetString(StringName.Renderer)};
        }
        finally
        {
            window?.EndScene();window?.Dispose();Directory.SetCurrentDirectory(cwd);Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",data);string target=Path.GetFullPath(temporary);if(!target.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("prime-drone-runtime-",StringComparison.Ordinal))throw new InvalidDataException("Cleanup escaped disposable root");if(Directory.Exists(target))Directory.Delete(target,true);
            string after=HashFile(source),nativeAfter=nativeBefore.Length==0?"":Inventory(native!);milestones.SourceAfter=after;milestones.NativeAfter=nativeAfter;milestones.Cleaned=!Directory.Exists(target);check(milestones.Cleaned&&before==after&&nativeBefore==nativeAfter,"derived imports deleted and both owned sources unchanged");Console.WriteLine(JsonSerializer.Serialize(new{Probe="source-drone-runtime-cleanup",SourceSha256Before=before,SourceSha256After=after,NativeSha256Before=nativeBefore,NativeSha256After=nativeAfter,TemporaryDerivedDataDeleted=!Directory.Exists(target)}));
            GameFiles.Root=files;LauncherPrefs.Directory=prefs;LauncherPrefs.LocalDrone=drone;LauncherPrefs.LocalBody=body;Menu.SaveSlot=slot;RenderOptions.CharacterModelReplacements=hd;RenderOptions.BrightSkins=bright;RenderOptions.ShowCustomCosmetics=cosmetics;RenderOptions.Shadows=shadows;MapAudit.ForceEveryone=force;GamepadContexts.Focused=focus;GamepadContexts.MenuVisible=menu;
        }
    }
    private static string Inventory(string root){using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var pending=new Stack<string>();pending.Push(root);int files=0;long bytes=0;while(pending.Count>0){string directory=pending.Pop();if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Native inventory alias");foreach(string child in Directory.GetDirectories(directory).Order(StringComparer.Ordinal).Reverse())pending.Push(child);foreach(string file in Directory.GetFiles(directory).Order(StringComparer.Ordinal)){if(++files>65536||(bytes+=new FileInfo(file).Length)>1024L*1024*1024||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Native inventory bound");hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root,file)));hash.AppendData(new byte[]{0});hash.AppendData(Convert.FromHexString(HashFile(file)));}}return Convert.ToHexStringLower(hash.GetHashAndReset());}
    private static readonly Keys[] Used=[Keys.Enter,Keys.B,Keys.F,Keys.Space,Keys.Z,Keys.W];
    private sealed record Stage(string Name,int Steps);
    private static readonly Stage[] Script=[new("biped",20),new("split",91),new("flight",90),new("hover",90),new("sink",60),new("burst",220),new("burst-rest",30),new("laser",135),new("laser-rest",20),new("merge",91),new("death-respawn",420),new("new-life-split",91),new("new-life-flight",60)];
    private sealed record TraceResult(string[] Trace,object Metadata);
    private static TraceResult Trace(RenderWindow window,Scene scene,PlayerEntity player,CharacterSourceDroneOwner? owner,LocalDroneAppearance appearance,string imports,Action<bool,string> check,string? captureRoot,List<object> captures,Action rendered)
    {
        var watch=Stopwatch.StartNew();var native=player.ModSourceNativeDrone!;int sourcePictures=0,nativePictures=0,repeats=0,burstFallback=0,laserFallback=0,laserPictures=0,beamPictures=0,poseChecks=0,maxShots=0;bool died=false,respawn=false,moved=false,guards=false;
        var start=player.Position;var rows=new List<string>();var programs=new HashSet<int>();var schedules=new List<object>();bool captured=false;
        if(owner!=null)
        {
            check(owner.Clip.Ordinal==13&&owner.Clip.ResourceId==0xb45b6445&&owner.Clip.MotionPolicy=="drone-pinned-root-pelvis-v1","exact own derived Ready13 primitive without source attack capability");
            check(Distance(owner.Fit,Matrix4.CreateTranslation(-.05303000249617673f,-1.7403560023522004f,.026181991054304687f)*Matrix4.CreateScale(.3f))<.000002f,"fixed derived Ready0 pivot and positive.3 source scale agree with independent oracle");
            CheckPoseOracle(owner,check);
        }
        using var locked=owner==null?null:new SourceLocks(imports);check(owner==null||locked!.Count>=24,"all imported component files remain exclusively locked through real Step/Draw");int reads=CharacterSourceClip.CapturedFileReads;
        Picture("prime",false);Rng.SetRng1(123456);Rng.SetRng2(654321);scene.Random.SetRng1(123456);scene.Random.SetRng2(654321);
        foreach(var stage in Script)
        {
            if(stage.Name=="death-respawn"){player.TakeDamage(10000,DamageFlags.Death|DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln|DamageFlags.NoSfx,null,null);died=player.Health==0;}
            for(int age=0;age<stage.Steps;age++)
            {
                foreach(var key in Used)Key(window.KeyboardState,key,false);
                if((stage.Name is "split" or "merge" or "new-life-split")&&age==0)Key(window.KeyboardState,Keys.B,true);
                if(stage.Name is "flight" or "new-life-flight")Key(window.KeyboardState,Keys.W,true);
                if(stage.Name=="hover")Key(window.KeyboardState,Keys.Space,true);if(stage.Name=="burst")Key(window.KeyboardState,Keys.F,true);
                if(stage.Name=="laser"&&age==0)Key(window.KeyboardState,Keys.Z,true);if(stage.Name=="death-respawn"&&age%30==0)Key(window.KeyboardState,Keys.Enter,true);
                Step();Picture(stage.Name,true);rows.Add(State(scene,player));moved|=(player.Position-start).Length>1;respawn|=died&&player.Health>0&&player.ModIsInPlay;maxShots=Math.Max(maxShots,player.DroneShots);
                if(owner!=null&&!captured&&captureRoot!=null&&stage.Name=="flight"&&age>=30&&Items(scene).Count==2)
                {string state=State(scene,player),path=Path.Combine(captureRoot,"source-drone-"+appearance.ToString().ToLowerInvariant()+"-ready-flight.png");check(!File.Exists(path)&&ScreenCapture.SaveWindow(scene.Size.X,scene.Size.Y,path),"optional real native-input source Ready flight picture saved");captured=true;captures.Add(new{Appearance=appearance.ToString(),Path=path,Sha256=HashFile(path),DeleteAfterVisualReview=true});check(State(scene,player)==state,"capture readback cannot advance native state");}
                if(owner!=null&&!guards&&stage.Name=="flight"&&Items(scene).Count==2){Guards();guards=true;}
            }
            Console.WriteLine(JsonSerializer.Serialize(new{Probe="source-drone-stage",Appearance=appearance.ToString(),stage.Name,stage.Steps,player.DroneFireState,player.DroneShots,player.DroneLaserFrame,SourcePictures=sourcePictures,NativePictures=nativePictures}));
        }
        check(moved&&died&&respawn&&maxShots==38&&beamPictures>0&&burstFallback>0&&laserFallback>0&&laserPictures>0,"real input earns flight/hover/full38shot burst/visible cached laser/merge/injected death/native respawn");
        check(owner==null||sourcePictures>0&&guards&&poseChecks>0&&programs.Count is >0 and <=2,"both complete own source material batches rendered with actual GPU palettes and guard recovery");
        if(owner!=null)
        {
            foreach(var key in Used)Key(window.KeyboardState,key,false);check(player.IsAltForm&&!player.IsMorphing&&owner.Ready,"fractional fixture follows genuine native new-life split");
            int oldCap=FrameTiming.FrameRateCap;
            try
            {
                foreach(int rate in new[]{90,120,240,540})
                {
                    FrameTiming.FrameRateCap=rate>FrameTiming.MaxCap?FrameTiming.Unlimited:rate;FrameTiming.Reset();long first=owner.CompletedSteps;int total=0,zero=0;
                    for(int p=0;p<rate;p++){int steps=FrameTiming.Advance(1d/rate);total+=steps;if(steps==0)zero++;for(int n=0;n<steps;n++)Step();Picture("fractional-"+rate,true);}
                    check(owner.CompletedSteps-first==total&&zero>0,"fractional pictures advance Ready only by supported completed native steps");schedules.Add(new{Rate=rate,Pictures=rate,CompletedSteps=total,ZeroStepPictures=zero});
                }
            }
            finally{FrameTiming.FrameRateCap=oldCap;FrameTiming.Reset();}
            int[] lists=owner.Model.Segments.Select(s=>s.ListId).ToArray();CharacterModelRuntime.Release(scene);check(!owner.Current&&lists.All(id=>!GL.IsList(id)),"context retirement destroys source geometry and authority");Picture("context-retired",true);check(Items(scene).Count==0&&NativePackets(scene,native)>0,"retired source draws complete native drone without resurrection");
        }
        check(CharacterSourceClip.CapturedFileReads==reads,"no source proof/clip file IO occurs during locked native simulation/GPU pictures");
        return new(rows.ToArray(),new{SourcePictures=sourcePictures,NativePictures=nativePictures,RepeatedDraws=repeats,BurstFallbackPictures=burstFallback,LaserFallbackPictures=laserFallback,NativeLaserPictures=laserPictures,NativeBeamTrailPictures=beamPictures,MaximumShots=maxShots,SourcePoseChecks=poseChecks,ActualShaderPrograms=programs.Count,FractionalSchedules=schedules.ToArray(),TraceGrain="one record after each fixed script completed simulation step and its repeated picture; scene/global RNG, actor+planted body, selected native drone/laser counters and cached origin/end,40 beam lifespan/age/position/direction/flags/ownership columns, native registered animation frames; separate guards/fractional/context pictures counted outside trace"});

        void Step()
        {
            if(watch.Elapsed.TotalMinutes>8)throw new TimeoutException("Bounded actual drone timeline exceeded eight minutes.");long prior=owner?.CompletedSteps??0;long life=(long)typeof(PlayerEntity).GetField("_sourceBodyLife",Hidden)!.GetValue(player)!;scene.OnSimulationFrame();
            if(owner is{Current:true}){bool supported=(bool)typeof(PlayerEntity).GetProperty("SourceDroneStateSupported",Hidden)!.GetValue(player)!;long nowLife=(long)typeof(PlayerEntity).GetField("_sourceBodyLife",Hidden)!.GetValue(player)!;long expected=(life==nowLife?prior:0)+(supported?1:0);check(owner.CompletedSteps==expected&&owner.Ready==supported,"source Ready clock advances once per supported native completed step and resets only for native life");}
        }
        void Guards()
        {
            long clock=owner!.CompletedSteps;string palette=Palette(owner.Model),canonical=State(scene,player);scene.GameState.PauseMenu();try{for(int n=0;n<3;n++){scene.OnSimulationFrame();Picture("pause",true);}check(owner.CompletedSteps==clock&&Palette(owner.Model)==palette,"pause freezes source raw pose/material clock");}finally{scene.GameState.UnpauseMenu();}
            RenderOptions.ShowCustomCosmetics=false;try{Picture("cosmetics-off",true);check(Items(scene).Count==0&&NativePackets(scene,native)>0,"cosmetics-off draws complete native fallback");}finally{RenderOptions.ShowCustomCosmetics=true;}
            RenderOptions.Shadows=ShadowQuality.Low;try{Picture("shadow-guard",true);check(Items(scene).Count==0&&NativePackets(scene,native)>0,"unsupported shadow mode draws complete native fallback");}finally{RenderOptions.Shadows=ShadowQuality.Off;}
            var property=typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;var flags=player.Flags2;property.SetValue(player,flags|PlayerFlags2.Cloaking);try{Picture("cloak",true);check(Items(scene).Count==0,"injected accepted cloak presentation guard refuses source");}finally{property.SetValue(player,flags);}
            var field=typeof(PlayerEntity).GetField("_curAlpha",Hidden)!;float alpha=(float)field.GetValue(player)!;field.SetValue(player,.5f);try{Picture("alpha",true);check(Items(scene).Count==0,"injected fractional native alpha refuses opaque imported source");}finally{field.SetValue(player,alpha);}
            Picture("guard-restored",true);check(Items(scene).Count==2&&owner.CompletedSteps==clock&&Palette(owner.Model)==palette&&State(scene,player)==canonical,"guard restoration preserves canonical native state and exact prepared source pose/clock");
        }
        void Picture(string label,bool repeat)
        {
            long clock=owner?.CompletedSteps??0;string palette=owner==null?"":Palette(owner.Model);scene.OnDrawFrame();Observe();check(scene.OnRenderFrame()&&GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"actual Scene world source/native render is GL-clean: "+label);rendered();
            if(Items(scene).Count>0&&owner!=null)
            {
                foreach(var segment in owner.Model.Segments)check(segment.SourceGx is{} binding&&ReferenceEquals(binding.Owner,scene)&&binding.Program.DrawPass==CharacterSourceGxProgram.Pass.Opaque&&GL.IsProgram(binding.ProgramId)&&binding.Textures.All(GL.IsTexture),"own opaque source material and all original textures are resident in actual Scene");
                // A program may be shared by both material batches. Its uniforms
                // retain the final actual draw, so compare that batch exactly.
                foreach(var group in owner.Model.Segments.GroupBy(s=>s.SourceGx!.ProgramId))if(programs.Add(group.Key))
                {var segment=group.Last();for(int i=0;i<segment.SourceJointPalette!.Count;i++){int location=GL.GetUniformLocation(group.Key,"gx_skin["+i+"]");var actual=new float[16];check(location>=0,"actual GPU source skin slot active");GL.GetUniform(group.Key,location,actual);check(actual.SequenceEqual(segment.SourcePresentationPalette!.Skip(i*16).Take(16)),"actual GPU palette equals final complete source batch matrix");}}
                var rig=owner.Model.SourceRig!;var globals=Globals(rig,owner.Clip.Sample(owner.MaterialSeconds%owner.Clip.Duration));var full=owner.Model.Joints.Select((j,i)=>j.InverseBind*globals[rig.JointNodes[i]]).ToArray();foreach(var segment in owner.Model.Segments)for(int i=0;i<segment.SourceJointPalette!.Count;i++)check(Distance(Matrix(segment.SourceMatrixPalette!,i*16),full[segment.SourceJointPalette[i]])<.000001f,"full33 source Ready skin matches current60Hz owner raw palette");poseChecks++;
            }
            string canonical=State(scene,player);
            if(repeat){scene.OnDrawFrame();Observe();check(scene.OnRenderFrame()&&GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"repeated source/native drone draw is GL-clean");repeats++;check(State(scene,player)==canonical&&(owner?.CompletedSteps??0)==clock&&(owner==null||Palette(owner.Model)==palette),"repeated draw cannot tick native actor/body/beams/laser/animation/RNG or source clock/raw pose");}scene.AfterRenderFrame();
            void Observe()
            {
                var items=Items(scene);int nativeCount=NativePackets(scene,native);check(items.Count is 0 or 2,"both original source material packets or complete native fallback");
                if(items.Count>0){sourcePictures++;check(owner is{Current:true,Ready:true}&&nativeCount==0&&items.All(i=>ReferenceEquals(i.SourceGx!.BodyOwner,owner)&&i.SourceGx.AlternateForm&&!i.ViewModel&&i.SourceGx.Seconds==owner.MaterialSeconds),"own complete source drone replaces exactly native mobile model and carries own supported-step material clock");}else if(nativeCount>0){nativePictures++;if(player.DroneFireState!=0)burstFallback++;if(player.DroneLaserFrame>=0)laserFallback++;}
                if(player.DroneFireState!=0||player.DroneLaserFrame>=0||player.IsMorphing||player.IsUnmorphing)check(items.Count==0,"source never overrides native burst/laser/morph presentation");
                var all=WorldItems(scene).Distinct().ToArray();int binding=(int)typeof(PlayerEntity).GetField("_trailBindingId1",Hidden)!.GetValue(player)!;var lasers=all.Where(i=>i.Type==RenderItemType.TrailSingle&&i.TextureBindingId==binding&&i.Diffuse==new Vector3(1,.15f,.1f)).ToArray();check(lasers.Length==(player.DroneLaserOn?2:0),"native cached laser always draws exact crossed-strip count during source fallback");if(lasers.Length>0){laserPictures++;check(lasers.All(i=>(i.Transform.Row3.Xyz-player.DroneLaserOrigin).LengthSquared<.0000001f),"native laser packets retain cached native origin");}
                var flights=player.DroneBeams.Where(b=>b.Lifespan>0&&ReferenceEquals(b.Owner,player)).ToArray();if(all.Any(i=>i.Type==RenderItemType.TrailSingle&&flights.Any(b=>i.TextureBindingId==(int)typeof(BeamProjectileEntity).GetField("_bindingId",Hidden)!.GetValue(b)!)))beamPictures++;
            }
        }
    }
    private sealed class SourceLocks:IDisposable{private readonly List<FileStream> _files=new();internal int Count=>_files.Count;internal SourceLocks(string root){try{foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))_files.Add(new(file,FileMode.Open,FileAccess.Read,FileShare.None));}catch{Dispose();throw;}}public void Dispose(){foreach(var f in _files)f.Dispose();_files.Clear();}}
    private static List<RenderItem> Items(Scene scene)=>((List<RenderItem>)typeof(Scene).GetField("_sourceBodyItems",Hidden)!.GetValue(scene)!).Where(i=>i.SourceGx?.BodyOwner is CharacterSourceDroneOwner).ToList();
    private static IEnumerable<RenderItem> WorldItems(Scene scene)=>new[]{"_nonDecalItems","_translucentItems","_decalItems"}.SelectMany(n=>(List<RenderItem>)typeof(Scene).GetField(n,Hidden)!.GetValue(scene)!);
    private static int NativePackets(Scene scene,Model native)=>WorldItems(scene).Distinct().Count(i=>i.Type==RenderItemType.Mesh&&native.Meshes.Any(m=>m.ListId==i.ListId));
    private static string State(Scene scene,PlayerEntity player)
    {
        using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);void V(Vector3 v){w.Write(v.X);w.Write(v.Y);w.Write(v.Z);}
        w.Write(scene.FrameCount);w.Write(scene.Random.Rng1);w.Write(scene.Random.Rng2);w.Write(Rng.Rng1);w.Write(Rng.Rng2);w.Write(player.Health);V(player.Position);V(player.Speed);V(player.FacingVector);w.Write((uint)player.Flags1);w.Write((uint)player.Flags2);w.Write(player.IsAltForm);w.Write(player.IsMorphing);w.Write(player.IsUnmorphing);w.Write(player.DroneBody.Health);V(player.DroneBody.Position);
        foreach(int v in new[]{player.DroneFireState,player.DroneFireFrame,player.DroneCooldown,player.DroneShots,player.DroneBursts,player.DroneDamageTotal,player.DroneLaserFrame,player.DroneLaserProcessedFrame,player.DroneLaserCooldown,player.DroneLaserPattern,player.DroneLasers,player.DroneLaserOpportunities,player.DroneLaserHitAttempts,player.DroneLaserSoundCount})w.Write(v);
        w.Write(player.DroneFloorGap);w.Write(player.DroneCap);w.Write(player.DroneLaserDistance);w.Write(player.DroneLaserOn);V(player.DroneLaserOrigin);V(player.DroneLaserEnd);
        foreach(var model in player.GetModels()){w.Write(model.Model.Name);foreach(int frame in model.AnimInfo.Frame)w.Write(frame);}
        foreach(var beam in player.DroneBeams){w.Write(beam.Lifespan);w.Write(beam.Age);V(beam.Position);V(beam.Direction);w.Write((int)beam.Flags);w.Write(ReferenceEquals(beam.Owner,player));}
        return Sha(bytes.ToArray());
    }
    private static string Palette(CharacterWeightedRenderModel model){using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);foreach(var s in model.Segments)foreach(float v in s.SourceMatrixPalette!)w.Write(v);return Sha(bytes.ToArray());}
    private static Matrix4 Matrix(float[] v,int o)=>new(v[o],v[o+1],v[o+2],v[o+3],v[o+4],v[o+5],v[o+6],v[o+7],v[o+8],v[o+9],v[o+10],v[o+11],v[o+12],v[o+13],v[o+14],v[o+15]);
    private static Matrix4[] Globals(CharacterSourceRig rig,IReadOnlyList<Matrix4> pose){var result=new Matrix4[pose.Count];var done=new bool[pose.Count];Matrix4 Resolve(int i){if(!done[i]){int p=rig.Nodes[i].Parent;result[i]=p<0?pose[i]:pose[i]*Resolve(p);done[i]=true;}return result[i];}for(int i=0;i<pose.Count;i++)Resolve(i);return result;}
    private static float Distance(Matrix4 a,Matrix4 b){float d=0;for(int r=0;r<4;r++)for(int c=0;c<4;c++)d=Math.Max(d,Math.Abs(a[r,c]-b[r,c]));return d;}
    private sealed class ChoiceStore(LauncherHunterChoice value):ILauncherHunterChoiceStore{internal LauncherHunterChoice Value=value;public LauncherHunterChoice Capture()=>Value;public void Save(LauncherHunterChoice choice)=>Value=choice;}
    private static void CheckPolicies(MenuSettings settings,Action<bool,string> check)
    {
        check(LocalDroneAppearanceRules.Available&&LocalDroneAppearanceRules.Canonical((LocalDroneAppearance)3)==LocalDroneAppearance.Native&&LocalDroneAppearanceRules.Canonical((LocalDroneAppearance)(-1))==LocalDroneAppearance.Native,"qualified desktop and invalid saved drone identity canonicalization");
        var store=new ChoiceStore(new(Hunter.Weavel,0));
        using(var draft=new LauncherHunterChoiceController(store))
        {
            check(draft.SelectDrone(1)&&draft.Current().Dirty&&draft.Apply()&&store.Value.Drone==LocalDroneAppearance.Alpha,"controller applies explicit Alpha draft");check(draft.SelectDrone(2)&&draft.Current().Dirty,"Gamma draft remains detached");draft.Discard();check(draft.Current().Choice.Drone==LocalDroneAppearance.Alpha&&!draft.Current().Dirty,"Discard restores saved appearance");check(!draft.SelectDrone(-1)&&!draft.SelectDrone(3),"invalid drone controller values refuse");draft.SelectHunter(0);check(draft.Current().Choice.Drone==LocalDroneAppearance.Native&&!draft.SelectDrone(1),"nonWeavel clears incompatible appearance without granting practice");
        }
        using(var reopened=new LauncherHunterChoiceController(store))check(reopened.Current().Choice.Drone==LocalDroneAppearance.Alpha,"new controller reopens saved Alpha");
        check((uint)RmlUiIntentKind.HunterDroneSelect==287&&Enumerable.Range(0,3).All(i=>RmlUiIntentRegistry.IsValid(RmlUiIntentKind.HunterDroneSelect,i))&&!RmlUiIntentRegistry.IsValid(RmlUiIntentKind.HunterDroneSelect,-1)&&!RmlUiIntentRegistry.IsValid(RmlUiIntentKind.HunterDroneSelect,3),"new intent287 retains closed0..2 choices and existing numbering");
        for(int i=0;i<3;i++){var token=new RmlUiDocumentToken(7,11);check(RmlUiIntentRegistry.TryParseLegacy("hunter:drone-select:"+i,token,3,out var intent)&&intent.Kind==RmlUiIntentKind.HunterDroneSelect&&intent.Argument==i&&intent.Document==token&&intent.Sequence==3&&RmlUiIntentRegistry.ToLegacy(intent)=="hunter:drone-select:"+i,"scoped native intent drone action roundtrip");}
        foreach(var appearance in new[]{LocalDroneAppearance.Alpha,LocalDroneAppearance.Gamma})
        {
            LauncherPrefs.LocalDrone=appearance;LauncherPrefs.LocalBody=LocalBodyAppearance.Native;check(LauncherPrefs.TrySave(),"actual isolated preference writer saves drone");LauncherPrefs.LocalDrone=LocalDroneAppearance.Native;LauncherPrefs.Load();check(LauncherPrefs.LocalDrone==appearance,"actual preference load restores additive local_drone field");
            var ordinary=OfflineLaunch.Preview(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Weavel,0,0,0);check(ordinary.LocalDrone==LocalDroneAppearance.Native&&ordinary.OfflineCharacter==OfflineCharacterMode.Native,"saved drone presentation inactive in ordinary native matches");
            var plan=OfflineLaunch.Preview(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Weavel,0,0,0,OfflineCharacterMode.TrooperScoutDronePractice);check(LocalDroneAppearanceRules.TryAdmit(plan,out _),"only exact native-movement zero-bot Weavel practice admits source presentation");
            foreach(var bad in new[]{plan with{Kind=LaunchKind.Online},plan with{Hunter=Hunter.Samus},plan with{Bots=1},plan with{IsPlaytest=false},plan with{Mode=GameMode.Capture},plan with{OfflineCharacter=OfflineCharacterMode.Native},plan with{OfflineCharacter=OfflineCharacterMode.TrooperScoutDroneCombatPractice},plan with{LocalBody=LocalBodyAppearance.TrooperPower},plan with{LocalDrone=(LocalDroneAppearance)3},plan with{MatchRules=plan.MatchRules with{MovementModel=MphRead.Mods.Movement.MovementModel.Momentum}}})check(!LocalDroneAppearanceRules.TryAdmit(bad,out _),"forged unsupported source-drone plan refuses authoritative admission");
            check(!LocalBodyAppearanceRules.TryAdmit(plan with{LocalBody=LocalBodyAppearance.TrooperPower},out _),"existing source-body Native gameplay fence unchanged in practice");
        }
    }
    private static void Lifecycle(RenderWindow window,MenuSettings settings,string source,string imports,Action<bool,string> check)
    {
        LauncherPrefs.LocalDrone=LocalDroneAppearance.Alpha;LauncherPrefs.LocalBody=LocalBodyAppearance.Native;var plan=OfflineLaunch.Preview(settings,"MP3 PROVING GROUND",GameMode.Battle,Hunter.Weavel,0,0,0,OfflineCharacterMode.TrooperScoutDronePractice);LauncherPrefs.LocalDrone=LocalDroneAppearance.Native;
        var state=RetroCharacterPreviewExporter.InspectBundle(imports,"trooper-drone-alpha");string file=state.Files.Single(p=>p.EndsWith(".source.glb",StringComparison.Ordinal));byte[] saved=File.ReadAllBytes(file),changed=saved.ToArray();changed[^1]^=1;
        try{File.WriteAllBytes(file,changed);Refused("corrupt-alpha");}finally{File.WriteAllBytes(file,saved);}
        check(MatchStart.Begin(window,settings,plan),"actual lifecycle begins captured Alpha");var live=window.Scene.Players.Main.ModSourceDrone!;string generation=live.Generation;
        RetroCharacterPreviewExporter.ExportFromOwnedGame(source,selectedIds:["trooper-drone-alpha"]);check(live.Current&&live.Generation==generation,"Update never retargets active generation");
        check(LocalImportBundle.Remove(imports,"trooper-drone-alpha")&&live.Current,"Remove withdraws discovery and preserves captured active owner");window.EndScene();check(!live.Current&&!live.Ready,"actual EndScene retires removed generation");Refused("removed-alpha");
        RetroCharacterPreviewExporter.ExportFromOwnedGame(source,selectedIds:["trooper-drone-alpha"]);check(MatchStart.Begin(window,settings,plan),"actual reinstall/relaunch binds fresh complete owner");var fresh=window.Scene.Players.Main.ModSourceDrone!;check(fresh.Current&&!ReferenceEquals(fresh,live)&&!live.Current,"old owner never resurrects on reinstall");
        var player=window.Scene.Players.Main;int[] lists=fresh.Model.Segments.Select(s=>s.ListId).ToArray();player.ModRetireCharacterGameplay();
        check(!fresh.Current&&!fresh.Ready&&player.ModSourceDrone==null&&player.ModSourceNativeDrone==null&&!player.ScoutDroneEnabled&&player.ModCharacterGameplay==CharacterGameplayRules.Native&&lists.All(id=>!GL.IsList(id)),"actual native gameplay retirement disposes source owner/lists before native drone model removal");window.EndScene();
        check(MatchStart.Begin(window,settings,plan),"fresh actual owner can test declared native rule-loss fixture");player=window.Scene.Players.Main;var lost=player.ModSourceDrone!;int[] lostLists=lost.Model.Segments.Select(s=>s.ListId).ToArray();window.Scene.GameState.EnhancedHunters=true;
        try{check(player.ModCharacterGameplay==CharacterGameplayRules.Native&&player.ModSourceDrone==null&&player.ModSourceNativeDrone==null&&!lost.Current&&!lost.Ready&&lostLists.All(id=>!GL.IsList(id)),"injected accepted incompatible native rule is observed by real gameplay getter and retires both presentation owners");}
        finally{window.Scene.GameState.EnhancedHunters=false;window.EndScene();}
        var native=plan with{LocalDrone=LocalDroneAppearance.Native};check(MatchStart.Begin(window,settings,native)&&window.Scene.Players.Main.ModSourceDrone==null&&window.Scene.Players.Main.ScoutDroneEnabled,"same actual launcher recovers native practice independently of source");window.EndScene();
        var forged=plan with{OfflineCharacter=OfflineCharacterMode.Native};check(!MatchStart.Begin(window,settings,forged)&&!window.HasScene,"actual MatchStart rejects forged ordinary source-drone plan before Scene creation");
        Console.WriteLine(JsonSerializer.Serialize(new{Probe="source-drone-product-lifecycle",CorruptRefused=true,RemovedRefused=true,UpdatePreservedGeneration=true,RetiredNoResurrection=true,DirectNativeRuleRetirement=true,DeclaredIncompatibleNativeRuleRetirement=true,NativeRecovery=true,ForgedPlanRefused=true}));
        void Refused(string label){check(!window.HasScene&&!MatchStart.Begin(window,settings,plan)&&!window.HasScene&&!string.IsNullOrWhiteSpace(MatchStart.LastError),"real source failure refuses before Scene creation: "+label);}
    }
    private static void CheckPoseOracle(CharacterSourceDroneOwner owner,Action<bool,string> check)
    {
        check(owner.Clip.KeyCount==40&&owner.Clip.Duration==1.2999999523162842&&owner.Clip.ContentSha256=="d714b3f01328ad897a82ed67de2f3e20e0922283bd9e453c269939684cc46cb0","derived Ready retains exact original40-key raw ANIM identity/duration");
        using var oracle=JsonDocument.Parse(PoseOracle);
        foreach(var sample in oracle.RootElement.EnumerateArray())
        {
            var pose=owner.Clip.Sample(sample.GetProperty("Time").GetDouble());using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
            foreach(var m in pose.Take(33))for(int r=0;r<4;r++)for(int c=0;c<4;c++)writer.Write((double)m[r,c]);
            check(Sha(bytes.ToArray())==sample.GetProperty("Sha").GetString(),"all33 local matrices exactly match independently measured83fe RootPin/sampler oracle");
        }
    }
    // Frozen metadata-only independent83fe oracle: every40 key, selected interval
    // off-grid points, all11 seam/epsilon probes and completed-step phases.
    private const string PoseOracle="""
[{"Time":0,"Sha":"fef14d6606ee797a1f4ae4d1fcb2c8bac4cafadb6697c1bb1edcc2991ca5597f"},{"Time":0.03333333134651184,"Sha":"20e3b11674814f3d40c4ff4342dad9c46293e9fc5884ba4974db63c8bff32564"},{"Time":0.06666666269302368,"Sha":"d0a1b93548ba90ebbaf88ba55d99b258eea9d1fb8be02ff10b9196cb90a6a317"},{"Time":0.09999999403953552,"Sha":"b4c75181936305e02e7eb0a14c9893cdd9ed148de60bdd40691634f8bfa7f54c"},{"Time":0.13333332538604736,"Sha":"b8cdc4f92399ec2709d85b958bfc9053e0866a2d88edfcda04d3939481d3a6fa"},{"Time":0.1666666567325592,"Sha":"6925c72d2ed584bd255229f684fe7d984bfa96692e6b446461468c1ce72348e0"},{"Time":0.19999998807907104,"Sha":"b34fddf00689c13b1bcd3b1fc21d294ff1ab1480139094dc3fdaf8efd323d8d6"},{"Time":0.23333331942558289,"Sha":"f83e4902e7912d5d09eadddc450c4151a8a30389d7717c4fe2c0699c936d37d6"},{"Time":0.2666666507720947,"Sha":"4d5c6585f64ba6481f18994f66d2ec963f731f2d03605e7a52efe3565ebb81c7"},{"Time":0.29999998211860657,"Sha":"e9bbfa05c2582ad843f785fae44fde825656b45fe6ed8b3ff262290972025f12"},{"Time":0.3333333134651184,"Sha":"8d79176ccff758621b2b75deb11fad0b5d49e9a9310af3a59f0280356ce6e1cf"},{"Time":0.36666664481163025,"Sha":"0c71a938a5a1c15eca842ac25e0c488977421224dcc6ef9b0b730bf1219ed17f"},{"Time":0.3999999761581421,"Sha":"c9fbbd0810258f785f39b2decabe0b3d22dbb2fed6c0924fcfe19c2c94393e7a"},{"Time":0.43333330750465393,"Sha":"a841977aa0dafbb57f501c53737ff3bc92906bfd79758ed02ebd69134af0247c"},{"Time":0.46666663885116577,"Sha":"fe15e345fa9b100853e047ce73bcfdbdba0e564b4c4e0d512577472499bcf573"},{"Time":0.4999999701976776,"Sha":"2003812df49d5f1f39322d4d972465097b58ed3c33b89835d70ad52d637b0380"},{"Time":0.5333333015441895,"Sha":"6586b45629ffa4a88b3ec49f022295c8acedd05eaf4c4cd422048eb432faddd3"},{"Time":0.5666666328907013,"Sha":"34d149e786d4c9a98f0124c7384446735d756efd19181f982e862181930ac83c"},{"Time":0.5999999642372131,"Sha":"bfa03c792623e39fc2825f5ee969b471dc8f045c98bf42faf3fab251173a8db6"},{"Time":0.633333295583725,"Sha":"247213c466be06e0a4986d7370a2e1a634d93f34aa1ec7e541e2c1989c4b4b9f"},{"Time":0.6666666269302368,"Sha":"093905250cfe3e1636671d3bb566b60c11bcbc4055129dbf4b8645ba6f8f1ba1"},{"Time":0.6999999582767487,"Sha":"8fb233ec60d3c9c140022c4b8d79626f0922d71ea67da1368aa8701e92573237"},{"Time":0.7333332896232605,"Sha":"a0372ee21dfa1d8f2d4a6985f3d347bcab33f06fe9146e35b671edc64f3767cf"},{"Time":0.7666666209697723,"Sha":"076d8894ddb4ed97e4922ac73362d6634bf345001f518121637f9f2f875c48ff"},{"Time":0.7999999523162842,"Sha":"d702ddcdafd251ddd716fed98eea6b096985ce432d082eac2a19676cd607150f"},{"Time":0.833333283662796,"Sha":"350d7ac62070f17390f46fda3a2b539f628faa5d426435a5aed6433d5a692246"},{"Time":0.8666666150093079,"Sha":"4aa9bd67e201ff292509bd112124f85fb5ede673c2eca2a10b0744d808f5d381"},{"Time":0.8999999463558197,"Sha":"abe3a548b4ffe371b77455853de374b1a4c637264b150fc97b08faed392e1dfa"},{"Time":0.9333332777023315,"Sha":"3cf79bc686dfd38e9e33f530e43582a7e606c10f84495bcfc12a5a513e7d5489"},{"Time":0.9666666090488434,"Sha":"ab6bcbf06acb9c755f0b6937eceaef1670a0229439f2134002fd324739ab18e3"},{"Time":0.9999999403953552,"Sha":"f9f33e95e0e5b33a583b701a5db025a29f5cccc99d80c218a66ec12b377949c1"},{"Time":1.033333271741867,"Sha":"e3c3c8ea9bf8075cfc2fb4fb1254c59b11618ebd596613f36d7393d4bf19dfe5"},{"Time":1.066666603088379,"Sha":"58fe103493fc48ef24fc223fb30e31e9d4b63ddafada8c492567bd4b4e3de127"},{"Time":1.0999999344348907,"Sha":"b7b6706e39c7edde8980ab2fb0266e86da17c647e257c25708a13c4f2780495e"},{"Time":1.1333332657814026,"Sha":"ee4a789a3017c9e4fcd4448c4239927b155f6b60c117f34cac16c18365560a86"},{"Time":1.1666665971279144,"Sha":"b48b32a3c8f327d06e8f2ec563ae48e7bb09a486fd083b10593450961638296e"},{"Time":1.1999999284744263,"Sha":"255c9dd7d3f458243a6c802afdf63b1529f40f264c3ab32eaa2207325450e400"},{"Time":1.233333259820938,"Sha":"5e04febadcf11674da5c94774614bb1ae5b3237de7884457bd2f6d52a0d9f53a"},{"Time":1.26666659116745,"Sha":"e2cd242cc9a82d589982004d68ff281e8c2b2bb50379718bed90c69691b9d420"},{"Time":1.2999999225139618,"Sha":"e1ac254d2257b678094371c42d06b0c13145d37ff0f2298495c6f0567e24f44b"},{"Time":0.00416666641831398,"Sha":"3875ebed5798c494facd14175e1c8ebdf48ac57dc4ae63ba33e4783e4418f2c2"},{"Time":0.01666666567325592,"Sha":"0a0217693c70ecbf1f107c0fe1a075361a4244ab2f131d6b8c6cf0c6a3671d5d"},{"Time":0.02916666492819786,"Sha":"2e4053672fba05420023a87b1e2332ae41627f586b3bbe1c02b20d5f8bda3c56"},{"Time":0.30416664853692055,"Sha":"a280afc8a7c0f720f0395e961edcbd5fa70d9f3e5830545753fdee3ed13c02e2"},{"Time":0.3166666477918625,"Sha":"7262f94c61ace1cca518fe1edd0e97d5bd0695f5da0f1db47214057f65f2febe"},{"Time":0.32916664704680443,"Sha":"e5ea6d182cf2815943949fd86be731f7f12815d8c2f116291a3fde7bc4786d7e"},{"Time":0.6041666306555271,"Sha":"44b05a438ac60bf2cae0a273b19c3e607f3701a3723fc345e50eb0ff1ff40792"},{"Time":0.616666629910469,"Sha":"d309861a250935bab295d6ad2fd553b4766e5b489f92df992bd652f5c3bedf36"},{"Time":0.629166629165411,"Sha":"ef47338990aafc79e218819fdc461674301f77099698f3b6e9048a5ed36dccf5"},{"Time":0.9041666127741337,"Sha":"e2fe38664423e5f6d1f95a3067f3e6b93f99fc7bcf6b79d97708308a3b9671ae"},{"Time":0.9166666120290756,"Sha":"b1ea8f0d4375302847680742e4960ce6c3d1951ad5ce38e9134f6bd6ecdcd662"},{"Time":0.9291666112840176,"Sha":"f5fe27100d17ede01baf0628f190496bbca15d1bba76c4e81de95b05dcd0d643"},{"Time":1.2041665948927402,"Sha":"e20a2a6262c36e20bd517ed3352daa1114ca1369190f8f55a8d7f7556e70515d"},{"Time":1.2166665941476822,"Sha":"eda4e9b11877fb071905cc81172e1832a86b06ed1a6f7d40b36bdf5050454423"},{"Time":1.2291665934026241,"Sha":"04c26c025badff536cddbc3ca9ca1d69ccf3a362dfb79f7987b28ce742349ba4"},{"Time":0,"Sha":"fef14d6606ee797a1f4ae4d1fcb2c8bac4cafadb6697c1bb1edcc2991ca5597f"},{"Time":1e-8,"Sha":"50a5da8581ef0a02d9d6ad3997881734342c43188219e48a8ec71cbfa8898e94"},{"Time":1e-7,"Sha":"fef14d6606ee797a1f4ae4d1fcb2c8bac4cafadb6697c1bb1edcc2991ca5597f"},{"Time":0.000001,"Sha":"dc48c0d54e75308c84551b3eb95ad7e994b4472ba8a80e9b26416fc4fa0e3da1"},{"Time":1.2999989523162843,"Sha":"77c82f7aa458a0a7eb05156c84b65b8a04ce3a7cc5d42de15c68166dd8e3a173"},{"Time":1.2999999423162842,"Sha":"d3177f52feaf4486c24b1115a4514986296f3b75a17f25eda192481307b07034"},{"Time":0,"Sha":"fef14d6606ee797a1f4ae4d1fcb2c8bac4cafadb6697c1bb1edcc2991ca5597f"},{"Time":9.99999993922529e-9,"Sha":"50a5da8581ef0a02d9d6ad3997881734342c43188219e48a8ec71cbfa8898e94"},{"Time":9.999999999177334e-7,"Sha":"dc48c0d54e75308c84551b3eb95ad7e994b4472ba8a80e9b26416fc4fa0e3da1"},{"Time":0,"Sha":"fef14d6606ee797a1f4ae4d1fcb2c8bac4cafadb6697c1bb1edcc2991ca5597f"},{"Time":0.012333332598209346,"Sha":"2a1361618b3aeb240f585bc269fb91929771f02ab06154080f58cefc19b061a1"},{"Time":0,"Sha":"fef14d6606ee797a1f4ae4d1fcb2c8bac4cafadb6697c1bb1edcc2991ca5597f"},{"Time":0.5,"Sha":"a6d8bb7e82ca3cd37f915bd8ae40b15bcb91b092a3c32678f73e80b400fb96e0"},{"Time":1,"Sha":"12cd41c30008a16305b82fcd0c9c068fb82a855da4772973fbea2001f8d22b7f"},{"Time":0.20000004768371582,"Sha":"727079fbaddb15cd32e1e3de8fbb6ecf9906c6cdfdf4aa649729fc324c7778f6"},{"Time":0.7000000476837158,"Sha":"b2c26652e817ad386ffb45611877e546adf950404c0c0b4e975f0b40457b3b5a"},{"Time":1.2000000476837158,"Sha":"3253d056b0d730916308c343c43b09c7897fd45cfa00b928003a1535aef4faee"},{"Time":0.40000009536743164,"Sha":"12bedd43593029f997c72f87630003b0f5f6677acfdbdaa9d14f753fa3f851b2"},{"Time":0.9000000953674316,"Sha":"3bc8e964df363410e62be223d4e786cb1112ab217de05feb59a9b1690c06e7b7"},{"Time":0.10000014305114746,"Sha":"06ab256c3b742152ee6b1a198ef8b60e3c71dbb6f9c5c21e158cecb624db2af1"},{"Time":0.6000001430511475,"Sha":"0ec50fec85ffae803c4fa362c7f2abdab6911a9d7fe3cfe9b035f4930304426c"},{"Time":1.1000001430511475,"Sha":"eba77cf11aab472688f4a0c4e3838a2d9be88881dd22eeb9f5d381d44d1b465a"},{"Time":0.3000001907348633,"Sha":"8ed9c3de04f7f43eafd47fc366f4ac4c30e83c2740a18dc3a7d8d4305586961a"},{"Time":0.8000001907348633,"Sha":"eca6fcc07116f982893d9fbc242d5112d134f11d4c5e8c275bbfc9e9ba7bce33"},{"Time":2.384185791015625e-7,"Sha":"887a97982dfd0ffd37c3586f30af85efac9bc18244170f887ec4392c2d3f4ab2"},{"Time":0.5000002384185791,"Sha":"862de8e78ad620e18275eb3554b77db916677e7831dbe799d2c142a6a90404f5"},{"Time":1.000000238418579,"Sha":"38cff507ec7f23fd519d5f054113e1e5a0e36bcb8f4b187fe4ccafb0c69a0aec"},{"Time":0.20000028610229492,"Sha":"31f1062fc28d044baa295c0c9baa1b1276e37a328b8edc5fa46a1ac123e37375"}]
""";
    private static string Sha(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}
}
