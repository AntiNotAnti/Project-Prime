using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Death;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Genuine production projectile/canonical damage causes and ordinary native
// Spawn. Only floor/HP/invulnerability/pose setup and magazine-only refill are
// declared fixture seams; no accepted death, Track, physical spawn or clock injection.
internal static class PhysicalCorpseRigLifecycleChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly FieldInfo Magazine=typeof(WeaponRuntimeState).GetField("_magazine",Hidden)!;
    private static readonly FieldInfo SpawnInvulnerability=typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!;
    private static readonly FieldInfo DamageInvulnerability=typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!;
    private static readonly FieldInfo[] SourceFields=new[]{"_muzzlePos","_aimVec","_gunVec1"}.Select(n=>typeof(PlayerEntity).GetField(n,Hidden)!).ToArray();
    private sealed record WeakPair(int Scene,int Cycle,WeakReference Backend,WeakReference Data);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakPair CaptureWeak(Scene scene,int victimSlot,int sceneIndex,int cycle)
    {
        var slots=(Array)typeof(Scene).GetField("_physicalCorpses",Hidden)!.GetValue(scene)!;
        object slot=slots.GetValue(victimSlot)??throw new InvalidOperationException("Expected active physical slot for weak capture");
        object backend=slot.GetType().GetField("Backend",Hidden)!.GetValue(slot)??throw new InvalidOperationException("Expected active backend for weak capture");
        object data=slot.GetType().GetField("Data",Hidden)!.GetValue(slot)??throw new InvalidOperationException("Expected active capture data for weak capture");
        return new(sceneIndex,cycle,new WeakReference(backend),new WeakReference(data));
    }
    private sealed record Measurement(string Phase,string Operation,int Scene,int Cycle,double Microseconds,long ManagedBytes);
    private sealed record CycleResult(int Scene,int Cycle,int VictimLife,ulong Frame,long ActivePoolBytes,PhysicalCorpseDiagnostics Active,PhysicalCorpseDiagnostics AfterRespawn);
    private static object Distribution(IEnumerable<Measurement> values)
    {
        var all=values.ToArray();var times=all.Select(x=>x.Microseconds).Order().ToArray();var bytes=all.Select(x=>x.ManagedBytes).Order().ToArray();
        double At(double p)=>times.Length==0?0:times[(int)Math.Clamp(Math.Ceiling(times.Length*p)-1,0,times.Length-1)];
        long BytesAt(double p)=>bytes.Length==0?0:bytes[(int)Math.Clamp(Math.Ceiling(bytes.Length*p)-1,0,bytes.Length-1)];
        return new{samples=all.Length,p50Us=At(.5),p95Us=At(.95),p99Us=At(.99),maxUs=At(1),managedBytes=new{total=all.Sum(x=>x.ManagedBytes),p50=BytesAt(.5),p95=BytesAt(.95),p99=BytesAt(.99),maximum=BytesAt(1)}};
    }
    internal static int Run(string data,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);string cwd=Directory.GetCurrentDirectory();
        bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled,physical=PhysicalCorpseOptions.Enabled,cosmetics=RenderOptions.ShowCustomCosmetics,hd=RenderOptions.CharacterModelReplacements,detail=Features.MaxPlayerDetail;
        var quality=RenderOptions.CosmeticQuality;var hud=HudProfiles.CopyCurrent();int cap=FrameTiming.FrameRateCap;var oldOverride=CosmeticDebug.Override;var oldFixed=CosmeticDebug.FixedTime;
        Scene? scene=null;NativeWindow? window=null;DesktopGraphicsSession? graphics=null;int checks=0,acceptedDeaths=0,nativeRespawns=0,primaryCycles=0,cleanups=0;string? failure=null;
        var weak=new List<WeakPair>();bool retentionCollected=false;var rows=new List<CycleResult>();var measurements=new List<Measurement>();var sceneResults=new List<object>();var watch=Stopwatch.StartNew();long maximumPool=0;
        void Check(bool pass,string label){checks++;if(!pass)throw new InvalidOperationException("check="+checks+" "+label);}
        void Bound(){if(watch.Elapsed.TotalSeconds>600)throw new TimeoutException("Native physical lifecycle exceeded600seconds");}
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh actual offline GL process");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;PhysicalCorpseOptions.Enabled=true;RenderOptions.ShowCustomCosmetics=true;RenderOptions.CharacterModelReplacements=false;Features.MaxPlayerDetail=false;
            RenderOptions.CosmeticQuality=CosmeticEffectQuality.Medium;var enabled=hud.DeepClone();enabled.ReduceMotion=false;HudProfiles.Publish(enabled);
            var classic=CosmeticCatalog.DeathPresentations.Single(d=>d.WireId==0);CosmeticDebug.Apply(["-cosmetic","clear","-cosmetic","death",classic.Key]);
            FrameTiming.FrameRateCap=144;FrameTiming.Reset();var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(640,480);settings.StartVisible=true;settings.Title="CW10e genuine M7 corpse lifecycle";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            for(int sceneIndex=0;sceneIndex<8;sceneIndex++)
            {
                Bound();var keyboard=SyntheticInput.CreateKeyboard();scene=new(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
                scene.AddPlayer(Hunter.Samus);scene.AddPlayer(Hunter.Samus);scene.Players.PlayerCount=2;foreach(var p in scene.Players.Items)p.IsBot=false;
                scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();scene.OnResize();scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
                var shooter=scene.Players.Items[0];var victim=scene.Players.Items[1];
                void Step(int count=1,string? phase=null,int cycle=-1)
                {
                    for(int i=0;i<count;i++)
                    {
                        Bound();FrameTiming.Advance(FrameTiming.StepSeconds);NetTestScript.Rest(shooter,true);
                        long allocated=GC.GetAllocatedBytesForCurrentThread(),begin=Stopwatch.GetTimestamp();scene.OnSimulationFrame();long elapsed=Stopwatch.GetTimestamp()-begin;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                        if(phase!=null)measurements.Add(new(phase,"whole-OnSimulationFrame",sceneIndex,cycle,elapsed*1_000_000d/Stopwatch.Frequency,allocated));
                    }
                }
                void Draw(string? phase=null,int cycle=-1)
                {
                    Bound();ulong beforeSteps=scene.ModPhysicalCorpseDiagnostics.FixedSteps;
                    long allocated=GC.GetAllocatedBytesForCurrentThread(),begin=Stopwatch.GetTimestamp();scene.OnDrawFrame();bool rendered=scene.OnRenderFrame();DesktopGraphicsSession.Present(window!);scene.AfterRenderFrame();long elapsed=Stopwatch.GetTimestamp()-begin;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                    if(phase!=null)measurements.Add(new(phase,"whole-draw-render-present",sceneIndex,cycle,elapsed*1_000_000d/Stopwatch.Frequency,allocated));
                    Check(rendered&&scene.ModPhysicalCorpseDiagnostics.FixedSteps==beforeSteps,"actual GL draw never advances physics");
                }
                Key(keyboard,Keys.Enter,true);Step(240);Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(10);
                foreach(var p in scene.Players.Items)foreach(var b in p.Controls.All){b.Type=ButtonType.Key;b.Key=Keys.Unknown;}
                Check(shooter.Health>0&&victim.Health>0,"native two-actor spawn scene="+sceneIndex);
                var runtime=scene.EnableDeveloperRifle(true);Check(runtime.Grant(shooter),"declared shooter-only developer ownership");Step();
                Check(!runtime.TryPeek(victim,out _,out _),"victim has no custom ownership");Check(runtime.Optic(shooter,1,1)==WeaponActionResult.Accepted,"canonical precision optic");
                Check(StockRiftlinePickups.TryPosition(scene.Room!.Meta.Name,out var fixedPoint),"authored Proving Ground landmark");
                var end=new Vector3(Fixed.ToFloat(fixedPoint.X),Fixed.ToFloat(fixedPoint.Y),Fixed.ToFloat(fixedPoint.Z));CollisionResult floor=default;
                Check(CollisionDetection.CheckBetweenPoints(end+Vector3.UnitY*2,end-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),"real native floor near authored center");end.Y=floor.Position.Y+.5f;
                float head=Fixed.ToFloat(victim.Values.MaxPickupHeight)-.15f;Vector3 from=default;bool ray=false;
                foreach(var d in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ})
                {
                    var at=end-d*5;CollisionResult sourceFloor=default;
                    if(!CollisionDetection.CheckBetweenPoints(at+Vector3.UnitY*2,at-Vector3.UnitY*6,TestFlags.Players,scene,ref sourceFloor))continue;at.Y=sourceFloor.Position.Y+.5f;
                    if(!BeamProjectileEntity.TryBeamObstacle(at+new Vector3(0,.4f,0),end+Vector3.UnitY*head,scene,out _,out _)){from=at;ray=true;break;}
                }
                Check(ray,"native floor and actual clear head ray");uint nextShot=2,nextOptic=2;
                void WaitForCadence()
                {
                    int waited=0;
                    while(unchecked((int)((uint)scene.FrameCount-runtime.State(shooter)!.Snapshot().FireReadyTick))<0)
                    {Step();if(++waited>90)throw new InvalidOperationException("Native cadence did not mature within90realsteps");}
                    Check(shooter.Health>0&&victim.Health>0,"both actual actors stay alive before next accepted shot");
                }
                void Camera()
                {
                    Check(scene.ModHeadDeathAnchor(victim,out var anchor,out _),"accepted current-life corpse anchor");scene.SetFreeCamera(true);
                    var eye=anchor+new Vector3(1.2f,1.5f,3.1f);var look=(anchor+Vector3.UnitY*.8f-eye).Normalized();var right=Vector3.Cross(look,Vector3.UnitY).Normalized();
                    typeof(Scene).GetField("_cameraPosition",Hidden)!.SetValue(scene,eye);typeof(Scene).GetField("_cameraFacing",Hidden)!.SetValue(scene,look);typeof(Scene).GetField("_cameraRight",Hidden)!.SetValue(scene,right);typeof(Scene).GetField("_cameraUp",Hidden)!.SetValue(scene,Vector3.Cross(right,look));
                    scene.Players.Main.CameraInfo.Position=victim.Position+Vector3.UnitZ*2;
                }
                void Kill(bool expectPhysical,int cycle)
                {
                    scene.SetFreeCamera(false);WaitForCadence();victim.Reposition(end,Vector3.UnitZ,scene.Room.GetNodeRefByPosition(end));victim.Speed=Vector3.Zero;shooter.ModPlaceAt(from);shooter.Speed=Vector3.Zero;
                    victim.Health=100;SpawnInvulnerability.SetValue(victim,(ushort)0);DamageInvulnerability.SetValue(victim,(ushort)0);Step();
                    Check(runtime.Optic(shooter,nextOptic++,1)==WeaponActionResult.Accepted,"fresh canonical optic after observer-camera return");
                    var state=runtime.State(shooter)!;var beforeRefill=state.Snapshot();Magazine.SetValue(state,4);
                    Check(state.Snapshot()==(beforeRefill with{Magazine=4}),"declared magazine-only refill retains owner/source/deadlines/optic/request history");
                    int deaths=scene.GameState.Deaths[victim.SlotIndex],kills=scene.GameState.Kills[shooter.SlotIndex],native=scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist];ulong accepted=runtime.AcceptedShots;
                    Check(runtime.Fire(shooter,nextShot++,victim.Position+Vector3.UnitY*head-(shooter.Position+new Vector3(0,.4f,0)))==WeaponActionResult.Accepted,"real M7 emitter accepted scene="+sceneIndex+" cycle="+cycle);
                    for(int i=0;i<6&&victim.Health>0;i++)Step();
                    Check(victim.Health==0&&runtime.LastImpact is {Headshot:true,Lethal:true},"actual collision/TakeDamage commits lethal M7 head cause");
                    Check(scene.GameState.Deaths[victim.SlotIndex]==deaths+1&&scene.GameState.Kills[shooter.SlotIndex]==kills+1&&scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist]==native&&runtime.AcceptedShots==accepted+1&&runtime.State(shooter)!.Snapshot().Magazine==3,
                        "one canonical death/kill/M7 payment with native beam ledger unchanged");acceptedDeaths++;
                    Step();Check(victim.CosmeticDeathState.Key==classic.Key,"Classic presentation remains selected");Camera();
                    var before=scene.ModPhysicalCorpseDiagnostics;Draw();Step();Draw();var active=scene.ModPhysicalCorpseDiagnostics;
                    Check(expectPhysical?active.Created==before.Created+1&&active.Active==1&&active.Bodies==15&&active.PoolBytes>0:active.Created==0&&active.Active==0&&active.PoolBytes==0,"actual source activates exactly expected physical world");
                }
                void NativeRespawn()
                {
                    int oldLife=scene.GameState.SpawnOrdinals[victim.SlotIndex];victim.Spawn(end,Vector3.UnitZ,Vector3.UnitY,scene.Room.GetNodeRefByPosition(end),respawn:true);Step();nativeRespawns++;
                    var retired=scene.ModPhysicalCorpseDiagnostics;Check(scene.GameState.SpawnOrdinals[victim.SlotIndex]>oldLife&&victim.Health>0&&retired.Active==0&&retired.Queued==0&&retired.Bodies==0&&retired.PoolBytes==0,"ordinary native new-life spawn retires exact old corpse and releases arena");
                }
                if(sceneIndex==0)
                {
                    PhysicalCorpseOptions.Enabled=false;Kill(false,-1);Step(4);Draw();Draw();Step(24,"featureOff",-1);for(int j=0;j<24;j++)Draw("featureOff",-1);
                    Check(scene.ModPhysicalCorpseDiagnostics.Created==0&&scene.ModPhysicalCorpseDiagnostics.PoolBytes==0,"featureOff actual accepted-death baseline never creates physics");NativeRespawn();PhysicalCorpseOptions.Enabled=true;
                }
                for(int cycle=0;cycle<16;cycle++)
                {
                    Kill(true,cycle);Step(3);Step(12,"active",cycle);Draw();Draw();
                    var beforeDraw=runtime.State(shooter)!.Snapshot();var health=(victim.Health,shooter.Health);var position=(victim.Position,shooter.Position);var speed=(victim.Speed,shooter.Speed);var random=(scene.Random.Rng1,scene.Random.Rng2);ulong frame=scene.FrameCount,shots=runtime.AcceptedShots;
                    int deaths=scene.GameState.Deaths[victim.SlotIndex],kills=scene.GameState.Kills[shooter.SlotIndex];var source=SourceFields.SelectMany(field=>new[]{field.GetValue(victim),field.GetValue(shooter)}).ToArray();var before=scene.ModPhysicalCorpseDiagnostics;
                    for(int j=0;j<3;j++)Draw("active",cycle);
                    var active=scene.ModPhysicalCorpseDiagnostics;
                    Check(active.Active==1&&active.Bodies==15&&active.Faults==0&&active.FixedSteps==before.FixedSteps&&active.Created==before.Created,"warmed picture sampling reuses current physical world without stepping/spawn/fault");
                    Check(scene.FrameCount==frame&&(scene.Random.Rng1,scene.Random.Rng2)==random&&runtime.State(shooter)!.Snapshot()==beforeDraw&&runtime.AcceptedShots==shots&&(victim.Health,shooter.Health)==health&&(victim.Position,shooter.Position)==position&&(victim.Speed,shooter.Speed)==speed
                        &&scene.GameState.Deaths[victim.SlotIndex]==deaths&&scene.GameState.Kills[shooter.SlotIndex]==kills&&SourceFields.SelectMany(field=>new[]{field.GetValue(victim),field.GetValue(shooter)}).SequenceEqual(source),"independent repeated draws preserve clocks/RNG/HP/resources/source vectors/native counters");
                    int life=scene.GameState.SpawnOrdinals[victim.SlotIndex];maximumPool=Math.Max(maximumPool,active.PoolBytes);weak.Add(CaptureWeak(scene,victim.SlotIndex,sceneIndex,cycle));NativeRespawn();rows.Add(new(sceneIndex,cycle,life,frame,active.PoolBytes,active,scene.ModPhysicalCorpseDiagnostics));primaryCycles++;
                }
                // An additional genuine accepted death leaves a live world for
                // Scene.DoCleanup, rather than only testing an already-empty scene.
                Kill(true,16);Step(3);Draw();var live=scene.ModPhysicalCorpseDiagnostics;Check(live.Active==1&&live.Bodies==15&&live.PoolBytes>0,"cleanup begins with actually active owned native corpse");
                weak.Add(CaptureWeak(scene,victim.SlotIndex,sceneIndex,16));
                scene.DoCleanup();var clean=scene.ModPhysicalCorpseDiagnostics;Check(clean.Active==0&&clean.Queued==0&&clean.Bodies==0&&clean.PoolBytes==0&&clean.Faults==0&&clean.Retired==clean.Created,"active-world Scene.DoCleanup releases every backend/arena without fault");scene.UnloadGl();cleanups++;
                sceneResults.Add(new{scene=sceneIndex,live,clean,frame=scene.FrameCount,primaryCycles=16});Console.WriteLine($"LIFECYCLE SCENE PASS {sceneIndex} cycles={primaryCycles} deaths={acceptedDeaths} respawns={nativeRespawns} peakPool={maximumPool}");scene=null;
            }
            Check(primaryCycles==128&&acceptedDeaths==137&&nativeRespawns==129&&cleanups==8,"128 genuine cycles plus one featureOff baseline and eight live-world cleanup deaths");
            long warmMaximum=rows.Take(16).Max(x=>x.ActivePoolBytes),lateMaximum=rows.Skip(16).Max(x=>x.ActivePoolBytes);
            Check(lateMaximum<=warmMaximum,"active per-world pool reaches first16cycle maximum and never grows across later lives/scene reloads");
            Check(weak.Count==136,"weak witnesses cover128normal corpses plus8active cleanup corpses, excluding featureOff");
            // Deliberately outside timing/allocation samples. The no-inline
            // capture helper never returns a strong backend/Data reference.
            GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,blocking:true,compacting:true);GC.WaitForPendingFinalizers();GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,blocking:true,compacting:true);retentionCollected=true;
            foreach(var pair in weak)Check(!pair.Backend.IsAlive&&!pair.Data.IsAlive,"disposed backend and copied native capture are collectible scene="+pair.Scene+" cycle="+pair.Cycle);
            Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"lifecycle native GL has no errors");
            Console.WriteLine($"PASS {checks} physical lifecycle checks;128death/respawncycles;8activecleanups;pool warm={warmMaximum} late={lateMaximum}");return 0;
        }
        catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);return 1;}
        finally
        {
            var report=new{checks,failure,seconds=watch.Elapsed.TotalSeconds,primaryCycles,acceptedDeaths,nativeRespawns,activeWorldCleanups=cleanups,maximumActivePoolBytes=maximumPool,cycles=rows,scenes=sceneResults,
                managedRetention=new{retentionCollected,witnessPairs=weak.Count,retained=weak.Select(w=>new{w.Scene,w.Cycle,backendAlive=w.Backend.IsAlive,dataAlive=w.Data.IsAlive}).ToArray()},
                performance=new{scope="Whole production OnSimulationFrame and whole draw/render/present, including all native engine/GL overhead; no isolated StepHeadDeathPresentation claim. State hashing, input-rest setup, record allocation and result serialization are outside timed regions. Allocation is actual measured managed bytes, not a zero-allocation guarantee.",
                    groups=measurements.GroupBy(x=>new{x.Phase,x.Operation}).Select(g=>new{g.Key.Phase,g.Key.Operation,distribution=Distribution(g)}).ToArray(),raw=measurements},
                gameSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),leafSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(MphRead.CorpsePhysics.CorpsePose).Assembly.Location))),
                scope="Actual native offline Samus accepted M7 head deaths, ordinary Player.Spawn respawns and scene cleanup/reload; declared shooter developer ownership, magazine-only refill retaining real cooldown,100HP/invulnerability/native-floor and camera positioning. No accepted-cause/physical-spawn injection, clock warp, networking/device/native-art approval."};
            File.WriteAllText(Path.Combine(output,"lifecycle-results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            if(scene!=null){scene.DoCleanup();scene.UnloadGl();}graphics?.Dispose();window?.Dispose();PhysicalCorpseOptions.Enabled=physical;MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;Features.MaxPlayerDetail=detail;RenderOptions.ShowCustomCosmetics=cosmetics;RenderOptions.CosmeticQuality=quality;RenderOptions.CharacterModelReplacements=hd;HudProfiles.Publish(hud);FrameTiming.FrameRateCap=cap;FrameTiming.Reset();
            typeof(CosmeticDebug).GetProperty("Override",BindingFlags.Public|BindingFlags.Static)!.SetValue(null,oldOverride);typeof(CosmeticDebug).GetProperty("FixedTime",BindingFlags.Public|BindingFlags.Static)!.SetValue(null,oldFixed);Directory.SetCurrentDirectory(cwd);
        }
    }
}
