using System.Diagnostics;
using System.Reflection;
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

// First actual accepted-M7 physical runtime smoke. Authored positioning/HP and
// shooter developer ownership are setup; cause, capture, activation and stepping
// use production simulation and draw. No direct Track/backend spawn injection.
internal static class PhysicalCorpseRigGlChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly FieldInfo Models=typeof(PlayerEntity).GetField("_bipedModelLods",Hidden)!;
    private static readonly FieldInfo Used=typeof(Scene).GetField("_usedRenderItems",Hidden)!;
    private static Model[] BodyModels(PlayerEntity p)=>((ModelInstance[])Models.GetValue(p)!).Select(i=>i.Model).ToArray();
    private sealed class ModelState(Model m)
    {
        private readonly Matrix4[] _animation=m.Nodes.Select(n=>n.Animation).ToArray(),_transform=m.Nodes.Select(n=>n.Transform).ToArray();
        private readonly float[] _palette=m.MatrixStackValues.ToArray();
        private readonly (Matrix4?,Matrix4?,bool,bool)[] _control=m.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).ToArray();
        internal bool Same(Model m)=>m.Nodes.Select(n=>n.Animation).SequenceEqual(_animation)&&m.Nodes.Select(n=>n.Transform).SequenceEqual(_transform)
            &&m.MatrixStackValues.SequenceEqual(_palette)&&m.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).SequenceEqual(_control);
    }
    internal static int Run(string data,string output)=>RunCase(data,output,Hunter.Samus,"smoke",false);
    internal static int RunMatrix(string data,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);var cases=new List<object>();var watch=Stopwatch.StartNew();int failed=0;
        void Save(bool complete)=>File.WriteAllText(Path.Combine(output,"matrix-results.json"),JsonSerializer.Serialize(new{cases,failed,complete,seconds=watch.Elapsed.TotalSeconds,
            scope="Actual7 native accepted M7 deaths; six physical and Spire fallback, actual near/far/near native LOD selector, picture0/12/24/35, explicit feature/catalog/source-fence negative cases; no lifecycle stress/network/device/art approval."},new JsonSerializerOptions{WriteIndented=true}));
        foreach(var hunter in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Spire,Hunter.Weavel})
        {
            string path=Path.Combine(output,hunter.ToString());int code=RunCase(data,path,hunter,"physical",true);cases.Add(new{hunter=hunter.ToString(),scenario="physical",code,path});if(code!=0)failed++;Save(false);
        }
        foreach(string scenario in new[]{"feature-off","quality-off","hd-enabled","nonzero-catalog","teleport-fence","translation-fence","active-fence"})
        {
            string path=Path.Combine(output,scenario);int code=RunCase(data,path,Hunter.Samus,scenario,true);cases.Add(new{hunter="Samus",scenario,code,path});if(code!=0)failed++;Save(false);
        }
        Save(true);return failed==0?0:1;
    }
    private static int RunCase(string data,string output,Hunter hunter,string scenario,bool matrix)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);string cwd=Directory.GetCurrentDirectory();
        bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled,physical=PhysicalCorpseOptions.Enabled,cosmetics=RenderOptions.ShowCustomCosmetics,hd=RenderOptions.CharacterModelReplacements;
        bool maxDetail=Features.MaxPlayerDetail;var quality=RenderOptions.CosmeticQuality;var hud=HudProfiles.CopyCurrent();int cap=FrameTiming.FrameRateCap;var oldOverride=CosmeticDebug.Override;var oldFixed=CosmeticDebug.FixedTime;
        Scene? scene=null;NativeWindow? window=null;DesktopGraphicsSession? graphics=null;int checks=0;string? failure=null;
        var watch=Stopwatch.StartNew();var history=new List<object>();var captures=new List<object>();var staticCandidates=new List<object>();
        void Check(bool good,string label){checks++;Console.WriteLine($"PHYSICAL CORPSE {(good?"PASS":"FAIL")} {label}");if(!good)throw new InvalidOperationException(label);}
        void Bound(){if(watch.Elapsed.TotalSeconds>180)throw new TimeoutException("Physical smoke exceeded180seconds");}
        void Record(string stage){history.Add(new{stage,frame=scene!.FrameCount,diagnostics=scene.ModPhysicalCorpseDiagnostics});Console.WriteLine($"PHYSICAL DIAGNOSTIC {stage} {scene.ModPhysicalCorpseDiagnostics}");}
        try
        {
            Console.WriteLine($"PHYSICAL CASE {hunter} {scenario}");Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh actual offline GL process");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;PhysicalCorpseOptions.Enabled=scenario!="feature-off";RenderOptions.ShowCustomCosmetics=true;Features.MaxPlayerDetail=!matrix;
            RenderOptions.CosmeticQuality=scenario=="quality-off"?CosmeticEffectQuality.Off:CosmeticEffectQuality.Medium;RenderOptions.CharacterModelReplacements=scenario=="hd-enabled";
            var enabled=hud.DeepClone();enabled.ReduceMotion=false;HudProfiles.Publish(enabled);var definition=CosmeticCatalog.DeathPresentations.First(d=>scenario=="nonzero-catalog"?d.WireId!=0:d.WireId==0);CosmeticDebug.Apply(["-cosmetic","clear","-cosmetic","death",definition.Key]);
            FrameTiming.FrameRateCap=144;FrameTiming.Reset();
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="CW10e genuine M7 physical corpse smoke";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            var keyboard=SyntheticInput.CreateKeyboard();scene=new(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
            bool remoteVictim=matrix&&scenario!="nonzero-catalog"; // Offline debug cosmetics apply only to the local actor.
            scene.AddPlayer(remoteVictim?Hunter.Samus:hunter);scene.AddPlayer(remoteVictim?hunter:Hunter.Samus);scene.Players.PlayerCount=2;foreach(var p in scene.Players.Items)p.IsBot=false;
            scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();scene.OnResize();scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var victim=scene.Players.Items[remoteVictim?1:0];var shooter=scene.Players.Items[remoteVictim?0:1];
            void Step(int count=1){for(int i=0;i<count;i++){Bound();FrameTiming.Advance(FrameTiming.StepSeconds);NetTestScript.Rest(shooter,true);scene.OnSimulationFrame();}}
            void Draw(Action? beforePresent=null){Bound();var before=scene.ModPhysicalCorpseDiagnostics.FixedSteps;scene.OnDrawFrame();Check(scene.OnRenderFrame(),"actual native GL render");beforePresent?.Invoke();DesktopGraphicsSession.Present(window!);scene.AfterRenderFrame();Check(scene.ModPhysicalCorpseDiagnostics.FixedSteps==before,"drawing advances zero physical fixed steps");}
            Key(keyboard,Keys.Enter,true);Step(240);Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(10);
            foreach(var actor in scene.Players.Items)foreach(var b in actor.Controls.All){b.Type=ButtonType.Key;b.Key=Keys.Unknown;}
            Check(victim.Health>0&&shooter.Health>0,"genuine native two-actor spawn");
            var runtime=scene.EnableDeveloperRifle(true);Check(runtime.Grant(shooter),"declared shooter-only developer M7 ownership");Step();
            Check(!runtime.TryPeek(victim,out _,out _),"victim owns no custom weapon");
            Check(StockRiftlinePickups.TryPosition(scene.Room!.Meta.Name,out var fixedPoint),"authored Proving Ground stock center");
            var end=new Vector3(Fixed.ToFloat(fixedPoint.X),Fixed.ToFloat(fixedPoint.Y),Fixed.ToFloat(fixedPoint.Z));CollisionResult floor=default;
            Check(CollisionDetection.CheckBetweenPoints(end+Vector3.UnitY*2,end-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),"genuine authored-center native floor");end.Y=floor.Position.Y+.5f;
            victim.Reposition(end,Vector3.UnitZ,scene.Room.GetNodeRefByPosition(end));victim.Speed=Vector3.Zero;
            foreach(float extent in new[]{4f,3f,2f}){bool accepted=CorpseStaticSnapshot.TryCapture(scene,CorpseMatrixConversion.Copy(end),extent,out var snapshot);staticCandidates.Add(new{extent,accepted,triangles=snapshot?.World.Triangles.Length});}
            float head=Fixed.ToFloat(victim.Values.MaxPickupHeight)-.15f;Vector3 from=default;bool ray=false;
            foreach(var d in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ})
            {var a=end-d*5;CollisionResult sourceFloor=default;if(!CollisionDetection.CheckBetweenPoints(a+Vector3.UnitY*2,a-Vector3.UnitY*6,TestFlags.Players,scene,ref sourceFloor))continue;a.Y=sourceFloor.Position.Y+.5f;
                if(!BeamProjectileEntity.TryBeamObstacle(a+new Vector3(0,.4f,0),end+Vector3.UnitY*head,scene,out _,out _)){from=a;ray=true;break;}}
            Check(ray,"source has genuine floor and unobstructed native head ray");shooter.ModPlaceAt(from);shooter.Speed=Vector3.Zero;victim.Health=100;
            typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);Step();
            int deaths=scene.GameState.Deaths[victim.SlotIndex],kills=scene.GameState.Kills[shooter.SlotIndex],native=scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist];
            Check(runtime.Optic(shooter,1,1)==WeaponActionResult.Accepted,"canonical precision optic");
            Check(runtime.Fire(shooter,2,victim.Position+Vector3.UnitY*head-(shooter.Position+new Vector3(0,.4f,0)))==WeaponActionResult.Accepted,"actual canonical M7 emitter");
            for(int i=0;i<6&&victim.Health>0;i++)Step();
            Check(victim.Health==0&&runtime.LastImpact is {Headshot:true,Lethal:true},"real M7 collision/TakeDamage supplies lethal head cause");
            Check(scene.GameState.Deaths[victim.SlotIndex]==deaths+1&&scene.GameState.Kills[shooter.SlotIndex]==kills+1&&scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist]==native
                &&runtime.AcceptedShots==1&&runtime.State(shooter)!.Snapshot().Magazine==3,"one normal death/kill/M7 payment; native ledger unchanged");
            Step();Check(victim.CosmeticDeathState.Key==definition.Key,"requested catalog death is selected");
            bool hasAnchor=scene.ModHeadDeathAnchor(victim,out var anchor,out _);if(scenario=="quality-off")anchor=victim.Position;else Check(hasAnchor,"accepted-life corpse anchor");scene.SetFreeCamera(true);
            var eye=anchor+new Vector3(1.2f,1.5f,3.1f);var look=(anchor+Vector3.UnitY*.8f-eye).Normalized();var right=Vector3.Cross(look,Vector3.UnitY).Normalized();
            typeof(Scene).GetField("_cameraPosition",Hidden)!.SetValue(scene,eye);typeof(Scene).GetField("_cameraFacing",Hidden)!.SetValue(scene,look);
            typeof(Scene).GetField("_cameraRight",Hidden)!.SetValue(scene,right);typeof(Scene).GetField("_cameraUp",Hidden)!.SetValue(scene,Vector3.Cross(right,look));
            if(matrix)scene.Players.Main.CameraInfo.Position=victim.Position+Vector3.UnitZ*2;
            Record("accepted-before-picture");Draw();Record("capture-picture");Step();Record("next-fixed-activation");Draw();Record("physical-picture");
            bool expectedPhysical=hunter!=Hunter.Spire&&scenario is "physical" or "smoke" or "teleport-fence" or "translation-fence" or "active-fence";
            void Capture(string label)
            {
                Draw(()=>
                {
                    string path=Path.Combine(output,$"{hunter}-{label}.png");Check(ScreenCapture.SaveWindow(scene,path),"actual native picture before Present "+label);
                    captures.Add(new{path,label,frame=scene.FrameCount,diagnostics=scene.ModPhysicalCorpseDiagnostics,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),captureBeforePresent=true});
                });
            }
            if(!expectedPhysical)
            {
                Check(scene.ModPhysicalCorpseDiagnostics.Created==0&&scene.ModPhysicalCorpseDiagnostics.Draws==0&&scene.ModPhysicalCorpseDiagnostics.Active==0,"explicit unsupported/gated case remains fallback without physical allocation");
                if(hunter==Hunter.Spire)Check(scene.ModPhysicalCorpseDiagnostics.Refused>0,"Spire unweighted-head physical refusal is explicit");Capture("fallback");
                if(scenario is "feature-off" or "quality-off" or "hd-enabled")
                {
                    PhysicalCorpseOptions.Enabled=true;RenderOptions.CosmeticQuality=CosmeticEffectQuality.Medium;RenderOptions.CharacterModelReplacements=false;
                    Step();Draw();Step();Draw();Check(scene.ModPhysicalCorpseDiagnostics.Created==0&&scene.ModPhysicalCorpseDiagnostics.Queued==0,"reenabling does not restart an already observed current death cause");
                }
                scene.DoCleanup();scene.UnloadGl();Record("fallback-released");Check(scene.ModPhysicalCorpseDiagnostics.Active==0&&scene.ModPhysicalCorpseDiagnostics.PoolBytes==0,"fallback releases cleanly");scene=null;return 0;
            }
            Check(scene.ModPhysicalCorpseDiagnostics.Created>0&&scene.ModPhysicalCorpseDiagnostics.Active==1&&scene.ModPhysicalCorpseDiagnostics.Bodies==15,"actual Scene created one fifteen-body physical corpse");
            Check(scene.ModPhysicalCorpseDiagnostics.Draws>0,"actual native GPU submission uses physical palette");
            Capture("physics-step0");Draw();var models=BodyModels(victim);var baseline=models.Select(m=>new ModelState(m)).ToArray();var state=runtime.State(shooter)!.Snapshot();
            var health=(victim.Health,shooter.Health);var pos=(victim.Position,shooter.Position);var speed=(victim.Speed,shooter.Speed);var rng=(scene.Random.Rng1,scene.Random.Rng2);var frame=scene.FrameCount;
            var sourceFields=new[]{"_muzzlePos","_aimVec","_gunVec1"}.Select(n=>typeof(PlayerEntity).GetField(n,Hidden)!).ToArray();var source=sourceFields.Select(f=>f.GetValue(victim)).ToArray();
            var beforeDraw=scene.ModPhysicalCorpseDiagnostics;for(int i=0;i<8;i++)Draw();
            Check(scene.ModPhysicalCorpseDiagnostics.FixedSteps==beforeDraw.FixedSteps&&scene.ModPhysicalCorpseDiagnostics.Created==beforeDraw.Created,"repeated pictures neither step nor respawn physical world");
            Check(models.Select((m,i)=>baseline[i].Same(m)).All(v=>v),"all19 nodes/transforms/controls/full shared palettes restore after actual draws");
            Check(scene.FrameCount==frame&&(scene.Random.Rng1,scene.Random.Rng2)==rng&&runtime.State(shooter)!.Snapshot()==state&&runtime.AcceptedShots==1&&(victim.Health,shooter.Health)==health
                &&(victim.Position,shooter.Position)==pos&&(victim.Speed,shooter.Speed)==speed&&sourceFields.Select(f=>f.GetValue(victim)).SequenceEqual(source),"draw preserves canonical clocks/RNG/payment/health/body/muzzle/aim");
            // Actual public Player.Draw appends actor ranges to the current scene
            // packet queue. Source remains visible and shares the Samus Model.
            scene.OnDrawFrame();var queue=(Queue<RenderItem>)Used.GetValue(scene)!;int count=queue.Count;
            if(matrix)scene.Players.Main.CameraInfo.Position=victim.Position+Vector3.UnitZ*2;victim.Draw();
            var victimPackets=queue.Skip(count).Where(p=>p.Type==RenderItemType.Mesh&&!p.ViewModel).ToArray();var values=victimPackets.Select(p=>p.MatrixStack.Take(p.MatrixStackCount*16).ToArray()).ToArray();
            var secondPackets=Array.Empty<RenderItem>();var secondValues=Array.Empty<float[]>();
            if(matrix)
            {
                Check(!victim.Flags2.TestFlag(PlayerFlags2.Lod1),"same-submission first victim range uses actual near LOD0");int farStart=queue.Count;
                scene.Players.Main.CameraInfo.Position=victim.Position+Vector3.UnitZ*4;victim.Draw();
                secondPackets=queue.Skip(farStart).Where(p=>p.Type==RenderItemType.Mesh&&!p.ViewModel).ToArray();secondValues=secondPackets.Select(p=>p.MatrixStack.Take(p.MatrixStackCount*16).ToArray()).ToArray();
                Check(victim.Flags2.TestFlag(PlayerFlags2.Lod1)&&secondPackets.Length>0,"same-submission second victim range uses actual far LOD1");
                Check(victimPackets.Select((p,i)=>p.MatrixStack.Take(p.MatrixStackCount*16).SequenceEqual(values[i])).All(v=>v),"earlier LOD0 palette copies survive later actual LOD1 draw");
                Check(victimPackets.All(a=>secondPackets.All(b=>!ReferenceEquals(a.MatrixStack,b.MatrixStack))),"both LOD packet ranges own independent copied palette storage");
            }
            int sourceStart=queue.Count;shooter.Draw();var sourcePackets=queue.Skip(sourceStart).Where(p=>p.Type==RenderItemType.Mesh&&!p.ViewModel).ToArray();
            Check(victimPackets.Length>0&&sourcePackets.Length>0,"explicit actual dead/live actor packet boundaries exist");
            Check(victimPackets.Select((p,i)=>p.MatrixStack.Take(p.MatrixStackCount*16).SequenceEqual(values[i])).All(v=>v)
                &&secondPackets.Select((p,i)=>p.MatrixStack.Take(p.MatrixStackCount*16).SequenceEqual(secondValues[i])).All(v=>v),"earlier physical GPU packet matrix copies survive later live actor draw");
            Check(victimPackets.Concat(secondPackets).All(a=>sourcePackets.All(b=>!ReferenceEquals(a.MatrixStack,b.MatrixStack))),"dead/live packets own distinct copied palette storage");
            Check(scene.OnRenderFrame(),"real actor packet render");string image=Path.Combine(output,$"{hunter}-physical-packets.png");Check(ScreenCapture.SaveWindow(scene,image),"actual physical smoke PNG");
            captures.Add(new{path=image,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(image))),victimPackets=victimPackets.Length,sourcePackets=sourcePackets.Length,secondLodPackets=secondPackets.Length});DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
            if(matrix)
            {
                foreach(int lod in new[]{0,1,0})
                {
                    scene.Players.Main.CameraInfo.Position=victim.Position+Vector3.UnitZ*(lod==0?2:4);Draw();
                    Check(victim.Flags2.TestFlag(PlayerFlags2.Lod1)==(lod==1),"actual distance selector chooses native LOD"+lod);
                    var lodModels=BodyModels(victim);var saved=lodModels.Select(m=>new ModelState(m)).ToArray();var d=scene.ModPhysicalCorpseDiagnostics;Draw();
                    Check(lodModels.Select((m,i)=>saved[i].Same(m)).All(v=>v),"both native LOD matrices/palettes restore exactly after actual selection");
                    Check(scene.ModPhysicalCorpseDiagnostics.Created==d.Created&&scene.ModPhysicalCorpseDiagnostics.FixedSteps==d.FixedSteps&&scene.ModPhysicalCorpseDiagnostics.Draws>d.Draws,"LOD switch reuses physical world and submits existing body pose");Capture("lod"+lod+"-draw"+scene.ModPhysicalCorpseDiagnostics.Draws);
                }
            }
            Step(12);Draw();Capture("physics-step12");Record("twelve-simulated-physical-steps");Check(scene.ModPhysicalCorpseDiagnostics.FixedSteps>beforeDraw.FixedSteps&&scene.ModPhysicalCorpseDiagnostics.Faults==0,"only genuine fixed simulation advances finite physical runtime");
            if(matrix)
            {
                Step(12);Draw();Capture("physics-step24");Record("twentyfour-simulated-physical-steps");Step(11);Draw();Capture("physics-step35");Record("thirtyfive-simulated-physical-steps");
                Check(scene.ModPhysicalCorpseDiagnostics.FixedSteps==35&&scene.ModPhysicalCorpseDiagnostics.Active==1&&scene.ModPhysicalCorpseDiagnostics.Faults==0,"supported native corpse remains physical through35 actual steps");
                if(scenario.EndsWith("-fence"))
                {
                    ulong created=scene.ModPhysicalCorpseDiagnostics.Created;var chunk=scene.Room!.RoomCollision.First(c=>c.Active);
                    var translation=chunk.Translation;bool active=chunk.Active;
                    try
                    {
                        if(scenario=="teleport-fence")victim.ModPlaceAt(victim.Position+Vector3.UnitX*5);
                        else if(scenario=="translation-fence")chunk.Translation+=Vector3.UnitX*.01f;
                        else chunk.Active=false;
                        Step();Draw();Record(scenario);Check(scene.ModPhysicalCorpseDiagnostics.Active==0&&scene.ModPhysicalCorpseDiagnostics.PoolBytes==0,"changed source/lifecycle retires copied physical world: "+scenario);
                    }
                    finally{chunk.Translation=translation;chunk.Active=active;}
                    Step();Draw();Check(scene.ModPhysicalCorpseDiagnostics.Created==created&&scene.ModPhysicalCorpseDiagnostics.Queued==0,"restored source cannot restart an already retired death cause");
                }
            }
            Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"native physical corpse GL has no errors");
            int life=scene.GameState.SpawnOrdinals[victim.SlotIndex];victim.Spawn(victim.Position,Vector3.UnitZ,Vector3.UnitY,victim.NodeRef,respawn:true);Step();Record("respawn");
            Check(scene.GameState.SpawnOrdinals[victim.SlotIndex]>life&&scene.ModPhysicalCorpseDiagnostics.Active==0&&scene.ModPhysicalCorpseDiagnostics.Bodies==0,"genuine new life retires physical corpse");
            scene.DoCleanup();scene.UnloadGl();Record("released");Check(scene.ModPhysicalCorpseDiagnostics.Active==0&&scene.ModPhysicalCorpseDiagnostics.Bodies==0&&scene.ModPhysicalCorpseDiagnostics.PoolBytes==0,"actual Scene release frees physical world/pool");scene=null;
            return 0;
        }
        catch(Exception e){failure=e.ToString();Console.Error.WriteLine(e);if(scene!=null)Record("failure");return 1;}
        finally
        {
            File.WriteAllText(Path.Combine(output,"physical-smoke-results.json"),JsonSerializer.Serialize(new{hunter=hunter.ToString(),scenario,checks,failure,elapsedSeconds=watch.Elapsed.TotalSeconds,history,captures,staticCandidates,
                gameSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),scope="Actual native accepted M7 death; explicit shooter developer ownership/HP/native floor and camera placement. Matrix cases exercise actual near/far/near selector, physical frames0/12/24/35 or declared gate/fallback. No100-cycle stress/network/device/art approval; HD-enabled case only negative physical admission."},new JsonSerializerOptions{WriteIndented=true}));
            if(scene!=null){scene.DoCleanup();scene.UnloadGl();}graphics?.Dispose();window?.Dispose();PhysicalCorpseOptions.Enabled=physical;MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;
            Features.MaxPlayerDetail=maxDetail;RenderOptions.ShowCustomCosmetics=cosmetics;RenderOptions.CosmeticQuality=quality;RenderOptions.CharacterModelReplacements=hd;HudProfiles.Publish(hud);FrameTiming.FrameRateCap=cap;FrameTiming.Reset();
            typeof(CosmeticDebug).GetProperty("Override",BindingFlags.Public|BindingFlags.Static)!.SetValue(null,oldOverride);typeof(CosmeticDebug).GetProperty("FixedTime",BindingFlags.Public|BindingFlags.Static)!.SetValue(null,oldFixed);Directory.SetCurrentDirectory(cwd);
        }
    }
}
