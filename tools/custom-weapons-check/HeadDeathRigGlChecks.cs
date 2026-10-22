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

// Actual offline lethal-head native rig/catalog acceptance fixture.
// Real emitter/collision/TakeDamage supplies the accepted lethal head cause.
// Setup HP, positions, developer shooter ownership and camera are explicit.
// Native bodies only in this fixture; modern replacement is explicitly disabled.
internal static class HeadDeathRigGlChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!
        .CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly FieldInfo Models=typeof(PlayerEntity).GetField("_bipedModelLods",Hidden)!;
    private static readonly FieldInfo Used=typeof(Scene).GetField("_usedRenderItems",Hidden)!;
    private static readonly FieldInfo Muzzle=typeof(PlayerEntity).GetField("_muzzlePos",Hidden)!;
    private static readonly FieldInfo Aim=typeof(PlayerEntity).GetField("_aimVec",Hidden)!;
    private static readonly FieldInfo Gun=typeof(PlayerEntity).GetField("_gunVec1",Hidden)!;
    private static Model[] BodyModels(PlayerEntity p)=>((ModelInstance[])Models.GetValue(p)!).Select(i=>i.Model).ToArray();
    private sealed class ModelState(Model m)
    {
        internal readonly Matrix4[] Animation=m.Nodes.Select(n=>n.Animation).ToArray(),_transform=m.Nodes.Select(n=>n.Transform).ToArray();
        private readonly float[] _palette=m.MatrixStackValues.ToArray();
        private readonly (Matrix4?,Matrix4?,bool,bool)[] _control=m.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).ToArray();
        internal bool Same(Model m)=>m.Nodes.Select(n=>n.Animation).SequenceEqual(Animation)&&m.Nodes.Select(n=>n.Transform).SequenceEqual(_transform)
            &&m.MatrixStackValues.SequenceEqual(_palette)&&m.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).SequenceEqual(_control);
        internal void Restore(Model m)
        {
            for(int i=0;i<Animation.Length;i++){m.Nodes[i].Animation=Animation[i];m.Nodes[i].Transform=_transform[i];
                (m.Nodes[i].BeforeTransform,m.Nodes[i].AfterTransform,m.Nodes[i].AnimIgnoreParent,m.Nodes[i].AnimIgnoreChild)=_control[i];}
            Array.Copy(_palette,(float[])m.MatrixStackValues,_palette.Length);MatrixPaletteRevision.Changed(m.MatrixStackValues);
        }
    }
    // Both actual scene-owned native models are composed using the current
    // dead-body world transform, which already includes the catalog root pose.
    // LOD1 preparation is explicit; this does not pretend to be a distance switch.
    private static void ProbeScopes(PlayerEntity victim,Scene scene,DeathPresentationDefinition definition,HeadDeathPose pose,Action<bool,string> check,List<object> results)
    {
        var position=victim.Position;var speed=victim.Speed;var aim=Aim.GetValue(victim);var muzzle=Muzzle.GetValue(victim);var gun=Gun.GetValue(victim);
        var rng=(scene.Random.Rng1,scene.Random.Rng2);var beforePose=scene.ModHeadDeathPose(victim,definition);
        var compose=typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation",Hidden)!;
        check(scene.ModHeadDeathAnchor(victim,out var anchor,out var facing),"scope has immutable accepted-life death anchor");
        var world=PlayerEntity.ModBuildBipedDrawTransform(anchor,facing,new Vector3(victim.Field70,0,victim.Field74),
            Metadata.HunterScales[victim.Hunter],Fixed.ToFloat(victim.Values.MinPickupHeight),out float pitch);
        if(DeathPresentationRuntime.Visible(victim.CosmeticDeathState,definition,scene.ElapsedTime))
            world=DeathPresentationRuntime.Pose(definition,victim.CosmeticDeathState.Progress(scene.ElapsedTime,definition))*world;
        for(int lod=0;lod<2;lod++)
        {
            var model=BodyModels(victim)[lod];var original=new ModelState(model);
            try
            {
                compose.Invoke(victim,new object?[]{model,model.GetNodeByName("Spine_1")!,world,pitch,null,null});
                var canonical=new ModelState(model);
                check(model.Nodes.Count==19&&HeadDeathPoseOverlay.Prewarm(model),$"{victim.Hunter}/{definition.Key}/LOD{lod}: actual19-node rig prewarms");
                int[] left=[model.GetNodeIndexByName("L_hip"),model.GetNodeIndexByName("L_knee"),model.GetNodeIndexByName("L_ankle")];
                int[] right=[model.GetNodeIndexByName("R_hip"),model.GetNodeIndexByName("R_knee"),model.GetNodeIndexByName("R_ankle")];
                check(left.Concat(right).All(i=>i>=0),"native joint-name mapping resolves both complete legs");
                var profile=CorpseRigProfile.For(model);
                check(profile.ProceduralSupported&&!profile.PhysicalQualified&&profile.Hunter==victim.Hunter&&profile.Lod==lod,
                    $"{victim.Hunter}/LOD{lod}: strict native layout supports procedural fallback only");
                check(profile.Head==model.GetNodeIndexByName("Head_1")&&profile.Spine==model.GetNodeIndexByName("Spine_2")
                    &&profile.LeftHip==left[0]&&profile.LeftKnee==left[1]&&profile.LeftAnkle==left[2]
                    &&profile.RightHip==right[0]&&profile.RightKnee==right[1]&&profile.RightAnkle==right[2],
                    "profile maps actual named head/spine/legs without assumed node IDs");
                int expectedPalette=victim.Hunter switch{Hunter.Kanden=>16,Hunter.Noxus=>14,Hunter.Spire=>13,_=>17};
                check(profile.PaletteCount==expectedPalette&&profile.PaletteCount==model.NodeMatrixIds.Count,
                    "profile preserves hunter-specific palette count distinct from19 node count");
                check(profile.WeightedHead==model.NodeMatrixIds.Contains(profile.Head)&&profile.WeightedHead==(victim.Hunter!=Hunter.Spire),
                    "Spire unweighted head and six weighted native heads remain explicit profile facts");
                check(model.Nodes[profile.LeftKnee].ParentIndex==profile.LeftHip&&model.Nodes[profile.LeftAnkle].ParentIndex==profile.LeftKnee
                    &&model.Nodes[profile.RightKnee].ParentIndex==profile.RightHip&&model.Nodes[profile.RightAnkle].ParentIndex==profile.RightKnee,
                    "profile leg ancestry follows both actual native chains");
                check(ReferenceEquals(profile,CorpseRigProfile.For(model)),"warmed profile lookup returns the same immutable model-owned profile");
                if(definition.WireId==0&&pose.Head>0)
                {RejectUnknownProfiles(model,pose,check);RejectIncompleteLodPair(victim,lod,check);}

                if(victim.Hunter is Hunter.Trace or Hunter.Noxus)check(model.GetNodeIndexByName("Pelvis")==-1,"Trace/Noxus accepted without a fabricated Pelvis node");
                var feet=new[]{model.Nodes[left[2]].Animation.Row3.Xyz,model.Nodes[right[2]].Animation.Row3.Xyz};
                var hips=(model.Nodes[left[0]].Animation.Row3.Xyz+model.Nodes[right[0]].Animation.Row3.Xyz)*.5f;
                float[] lengths=left.Take(2).Select((_,i)=>(model.Nodes[left[i+1]].Animation.Row3.Xyz-model.Nodes[left[i]].Animation.Row3.Xyz).Length)
                    .Concat(right.Take(2).Select((_,i)=>(model.Nodes[right[i+1]].Animation.Row3.Xyz-model.Nodes[right[i]].Animation.Row3.Xyz).Length)).ToArray();
                float maximumLengthError=0,hipDrop=0;bool neutral=pose.Head==0&&pose.Collapse==0;
                using(HeadDeathPoseOverlay.Begin(model,pose))
                {
                    check(model.Nodes.All(n=>Finite(n.Animation))&&model.MatrixStackValues.All(float.IsFinite),"composed corpse nodes and weighted palette stay finite");
                    check(model.Nodes[left[2]].Animation.Row3.Xyz==feet[0]&&model.Nodes[right[2]].Animation.Row3.Xyz==feet[1],"both ankle world targets remain EXACT after catalog-root composition");
                    float[] actual=left.Take(2).Select((_,i)=>(model.Nodes[left[i+1]].Animation.Row3.Xyz-model.Nodes[left[i]].Animation.Row3.Xyz).Length)
                        .Concat(right.Take(2).Select((_,i)=>(model.Nodes[right[i+1]].Animation.Row3.Xyz-model.Nodes[right[i]].Animation.Row3.Xyz).Length)).ToArray();
                    maximumLengthError=actual.Zip(lengths).Max(p=>MathF.Abs(p.First-p.Second));
                    check(maximumLengthError<.001f,"analytic knee solve preserves both measured leg segment lengths");
                    var lowered=(model.Nodes[left[0]].Animation.Row3.Xyz+model.Nodes[right[0]].Animation.Row3.Xyz)*.5f;hipDrop=hips.Y-lowered.Y;
                    check(hipDrop>=-.0001f&&hipDrop<=.4501f&&hipDrop<=MathF.Min(lengths[0]+lengths[1],lengths[2]+lengths[3])*.5f+.0001f,"hip drop respects actual shorter leg and .45m cap");
                    if(neutral)check(canonical.Same(model),"ZERO motion is exact identity for Cryo/explicit neutral pose, including knee pole");
                    else
                    {
                        check(!canonical.Same(model),"nonzero accepted pose actually changes composed native body/palette");
                        if(pose.Collapse>.01f)check(hipDrop>0,"accepted collapse lowers actual hip midpoint");
                    }
                    var active=new ModelState(model);using(HeadDeathPoseOverlay.Begin(model,pose))check(active.Same(model),"nested shared-model corpse scope cannot overwrite outer borrow");
                }
                check(canonical.Same(model),"all19 nodes/transforms/control flags and complete palette restore EXACTLY");
                results.Add(new{hunter=victim.Hunter.ToString(),definition=definition.Key,lod,pose.Head,pose.Collapse,neutral,maximumLengthError,hipDrop,
                    leftFoot=feet[0].ToString(),rightFoot=feet[1].ToString(),scope="Native composition/scope seam after existing catalog root transform; LOD1 is not a live distance switch"});
            }
            finally{original.Restore(model);}
            check(original.Same(model),"LOD seam restores the pre-probe scene-owned native model");
        }
        check(victim.Position==position&&victim.Speed==speed&&Equals(Aim.GetValue(victim),aim)&&Equals(Muzzle.GetValue(victim),muzzle)&&Equals(Gun.GetValue(victim),gun)
            &&(scene.Random.Rng1,scene.Random.Rng2)==rng&&scene.ModHeadDeathPose(victim,definition)==beforePose,"LOD scope probe cannot alter canonical source/physics/RNG/pose clock");
    }
    // Declared malformed-asset seam: only fresh node/palette-owning model copies
    // are changed. Shared native source nodes/raw assets/palette lists stay intact.
    private static int _negativeLayouts,_incompletePairs;
    private static void RejectUnknownProfiles(Model original,HeadDeathPose pose,Action<bool,string> check)
    {
        var originalState=new ModelState(original);var palette=original.NodeMatrixIds.ToArray();
        foreach(string failure in new[]{"name","duplicate-palette","out-of-range-palette"})
        {
            var clone=original.CreateSceneCopy();
            if(failure=="name")typeof(Model).GetField("<Name>k__BackingField",Hidden)!.SetValue(clone,original.Name+"-unsupported");
            else
            {
                var altered=clone.NodeMatrixIds.ToArray();
                if(failure=="duplicate-palette")altered[0]=altered[1];else altered[0]=clone.Nodes.Count;
                typeof(Model).GetField("<NodeMatrixIds>k__BackingField",Hidden)!.SetValue(clone,altered);
            }
            var before=new ModelState(clone);var profile=CorpseRigProfile.For(clone);
            check(!profile.ProceduralSupported&&!profile.PhysicalQualified&&!HeadDeathPoseOverlay.Prewarm(clone),
                "unsupported cloned model layout refuses prewarm: "+failure);
            using(HeadDeathPoseOverlay.Begin(clone,pose))check(before.Same(clone),"unsupported cloned layout Begin is exact NOP: "+failure);
            check(before.Same(clone),"unsupported cloned layout scope cannot rewrite nodes/palette: "+failure);
        }
        check(originalState.Same(original)&&original.NodeMatrixIds.SequenceEqual(palette)&&CorpseRigProfile.For(original).ProceduralSupported,
            "malformed independent clones cannot poison or mutate actual shared native model profile");
        _negativeLayouts++;
    }
    private static void RejectIncompleteLodPair(PlayerEntity victim,int lod,Action<bool,string> check)
    {
        var instances=(ModelInstance[])Models.GetValue(victim)!;
        var native=instances.Select(i=>i.Model).ToArray();var before=native.Select(m=>new ModelState(m)).ToArray();
        var ready=typeof(PlayerEntity).GetField("_headDeathRigReady",Hidden)!;
        var visible=typeof(PlayerEntity).GetProperty("ModClassicHeadDeathVisible",Hidden)!;
        victim.ModPrewarmHeadDeathPose();
        check((bool)ready.GetValue(victim)!&&(bool)visible.GetValue(victim)!,
            "actual Classic peak starts with a fully supported visible native LOD pair");
        var unsupported=native[lod].CreateSceneCopy();
        typeof(Model).GetField("<Name>k__BackingField",Hidden)!.SetValue(unsupported,native[lod].Name+"-unsupported-pair");
        try
        {
            instances[lod].SetModel(unsupported);victim.ModPrewarmHeadDeathPose();
            check(!(bool)ready.GetValue(victim)!&&!(bool)visible.GetValue(victim)!,
                "one unsupported LOD"+lod+" disables pair readiness and Classic retained-body visibility");
            check(native.Select((m,i)=>before[i].Same(m)).All(same=>same),
                "failed pair prewarm changes no original LOD canonical node or palette");
        }
        finally
        {instances[lod].SetModel(native[lod]);victim.ModPrewarmHeadDeathPose();}
        check((bool)ready.GetValue(victim)!&&(bool)visible.GetValue(victim)!
            &&instances.Select((i,n)=>ReferenceEquals(i.Model,native[n])&&before[n].Same(native[n])).All(same=>same),
            "restored actual LOD pair rewarms and preserves all original composed matrices/palettes");
        _incompletePairs++;
    }
    private static bool Finite(Matrix4 m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}.All(float.IsFinite);
    private static object PoseReceipt(HeadDeathPose pose)=>new{pose.Active,pose.Head,pose.Collapse,pose.Alpha,pose.Side,direction=pose.Direction.ToString()};
    internal static int Run(string data,string output)
    {
        _negativeLayouts=_incompletePairs=0;
        int code=Run(data,output,ProbeScopes);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"profile-negative-results.json"),JsonSerializer.Serialize(new
        {negativeLayoutCohorts=_negativeLayouts,incompleteLodPairs=_incompletePairs,expected=14,code},new JsonSerializerOptions{WriteIndented=true}));
        if(code==0&&(_negativeLayouts!=14||_incompletePairs!=14))
            throw new InvalidOperationException("Negative layout/LOD tests must execute for all fourteen native profiles.");
        return code;
    }
    internal delegate void ScopeProbe(PlayerEntity victim,Scene scene,DeathPresentationDefinition definition,HeadDeathPose pose,Action<bool,string> check,List<object> results);
    internal static int Run(string data,string output,ScopeProbe scopeProbe)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled;
        bool cosmetics=RenderOptions.ShowCustomCosmetics,hd=RenderOptions.CharacterModelReplacements;
        var quality=RenderOptions.CosmeticQuality;var hud=HudProfiles.CopyCurrent();int cap=FrameTiming.FrameRateCap;
        var oldOverride=CosmeticDebug.Override;var oldFixed=CosmeticDebug.FixedTime;
        NativeWindow? window=null;DesktopGraphicsSession? graphics=null;Scene? scene=null;
        var watch=Stopwatch.StartNew();int checks=0;var captures=new List<object>();var cases=new List<object>();var scopes=new List<object>();
        void Check(bool good,string label){checks++;if(!good)throw new InvalidOperationException(label);Console.WriteLine("HEAD DEATH GL PASS "+label);}
        void Bound(){if(watch.Elapsed.TotalSeconds>600)throw new TimeoutException("Death GL matrix exceeded600seconds.");}
        try
        {
            ArgumentNullException.ThrowIfNull(scopeProbe);
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh actual offline GL process");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;RenderOptions.ShowCustomCosmetics=true;RenderOptions.CosmeticQuality=CosmeticEffectQuality.Medium;
            RenderOptions.CharacterModelReplacements=false;var enabled=hud.DeepClone();enabled.ReduceMotion=false;HudProfiles.Publish(enabled);
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;settings.Title="CW08 accepted lethal-head native rig qualification";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            var renderer=GL.GetString(StringName.Renderer);var version=GL.GetString(StringName.Version);
            Check(CosmeticCatalog.DeathPresentations.Count==8,"catalog is Classic plus seven nonzero effects");
            foreach(var hunter in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Spire,Hunter.Weavel})
            foreach(var definition in CosmeticCatalog.DeathPresentations)
            {
                Bound();FrameTiming.FrameRateCap=144;FrameTiming.Reset();
                CosmeticDebug.Apply(["-cosmetic","clear","-cosmetic","death",definition.Key]);
                var keyboard=SyntheticInput.CreateKeyboard();scene=new(window.FramebufferSize,keyboard,SyntheticInput.CreateMouse(),_=>{},()=>{});
                scene.AddPlayer(hunter);scene.AddPlayer(Hunter.Samus);scene.Players.PlayerCount=2;
                foreach(var actor in scene.Players.Items)actor.IsBot=false;
                scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:2,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
                scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
                var victim=scene.Players.Items[0];var shooter=scene.Players.Items[1];
                void Step(int count=1){for(int i=0;i<count;i++){Bound();FrameTiming.Advance(FrameTiming.StepSeconds);NetTestScript.Rest(shooter,true);scene.OnSimulationFrame();}}
                Key(keyboard,Keys.Enter,true);Step(240);Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(10);
                foreach(var actor in scene.Players.Items)foreach(var b in actor.Controls.All){b.Type=ButtonType.Key;b.Key=Keys.Unknown;}
                Check(victim.Health>0&&shooter.Health>0,hunter+"/"+definition.Key+" genuine two-actor native spawn");
                var runtime=scene.EnableDeveloperRifle(true);Check(runtime.Grant(shooter),"declared shooter-only developer M7 ownership");Step();
                Check(!runtime.TryPeek(victim,out _,out _),"victim owns no custom weapon and remains eligible");
                // Keep the victim at a genuine authored grounded spawn. Only the
                // source is placed to establish a verified unobstructed shot ray.
                PlayerSpawnEntity? spawn=null;foreach(var s in scene.GetPlayerSpawnEntities())if(s.Id==4)spawn=s;
                if(spawn==null)throw new InvalidOperationException("Authored spawn4 absent");
                var end=spawn.Position;CollisionResult floor=default;
                Check(CollisionDetection.CheckBetweenPoints(end+Vector3.UnitY*2,end-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),"authored victim floor exists");
                end.Y=floor.Position.Y+.5f;victim.Reposition(end,Vector3.UnitZ,scene.Room!.GetNodeRefByPosition(end));victim.Speed=Vector3.Zero;
                float head=Fixed.ToFloat(victim.Values.MaxPickupHeight)-.15f;Vector3 from=default;bool ray=false;
                foreach(var d in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitZ})
                {
                    var a=end-d*5;CollisionResult sourceFloor=default;
                    if(!CollisionDetection.CheckBetweenPoints(a+Vector3.UnitY*2,a-Vector3.UnitY*6,TestFlags.Players,scene,ref sourceFloor))continue;
                    a.Y=sourceFloor.Position.Y+.5f;
                    if(!BeamProjectileEntity.TryBeamObstacle(a+new Vector3(0,.4f,0),end+Vector3.UnitY*head,scene,out _,out _))
                    {from=a;ray=true;break;}
                }
                Check(ray,"declared source placement has genuine floor and unobstructed native head ray");
                shooter.ModPlaceAt(from);shooter.Speed=Vector3.Zero;victim.Health=100;
                typeof(PlayerEntity).GetField("_spawnInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);
                typeof(PlayerEntity).GetField("_damageInvulnTimer",Hidden)!.SetValue(victim,(ushort)0);
                Step(); // actual alive observation/life and selected cosmetic latch
                int priorDeaths=scene.GameState.Deaths[victim.SlotIndex],priorKills=scene.GameState.Kills[shooter.SlotIndex];
                int priorNative=scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist];
                Check(runtime.Optic(shooter,1,1)==WeaponActionResult.Accepted,"canonical precision optic");
                Check(runtime.Fire(shooter,2,victim.Position+Vector3.UnitY*head-(shooter.Position+new Vector3(0,.4f,0)))==WeaponActionResult.Accepted,"actual canonical M7 emitter");
                for(int i=0;i<6&&victim.Health>0;i++)Step();
                Check(victim.Health==0&&runtime.LastImpact is {Headshot:true,Lethal:true},"real collision/TakeDamage establishes lethal head cause");
                Check(scene.GameState.Deaths[victim.SlotIndex]==priorDeaths+1&&scene.GameState.Kills[shooter.SlotIndex]==priorKills+1
                    &&scene.GameState.BeamKills[shooter.SlotIndex,(int)BeamType.Imperialist]==priorNative,"one ordinary kill/death; native Imperialist ledger unchanged");
                Check(runtime.AcceptedShots==1&&runtime.TryPeek(shooter,out var funded,out _)&&funded.Magazine==3,"one accepted shot and magazine payment");
                Step();Check(victim.CosmeticDeathState.Active&&victim.CosmeticDeathState.Key==definition.Key,"native death observer latches requested catalog key");
                uint deadFrame=(uint)scene.FrameCount;float start=victim.CosmeticDeathState.StartTime;
                Check(scene.ModHeadDeathAnchor(victim,out var anchor,out var facing),"accepted-life visual death anchor available");
                scene.SetFreeCamera(true);var target=anchor+Vector3.UnitY*.8f;var eye=anchor+new Vector3(1.2f,1.5f,3.1f);var look=(target-eye).Normalized();var right=Vector3.Cross(look,Vector3.UnitY).Normalized();
                typeof(Scene).GetField("_cameraPosition",Hidden)!.SetValue(scene,eye);typeof(Scene).GetField("_cameraFacing",Hidden)!.SetValue(scene,look);
                typeof(Scene).GetField("_cameraRight",Hidden)!.SetValue(scene,right);typeof(Scene).GetField("_cameraUp",Hidden)!.SetValue(scene,Vector3.Cross(right,look));
                void AtAge(int ticks){while(unchecked((uint)scene.FrameCount-deadFrame)<ticks)Step();}
                RenderItem[] BodyItems()=>((Queue<RenderItem>)Used.GetValue(scene)!).Where(i=>i.Type==RenderItemType.Mesh&&!i.ViewModel
                    &&BodyModels(victim).Any(m=>ReferenceEquals(i.MatrixPaletteSource,m.MatrixStackValues))).ToArray();
                int Picture(string label,bool shouldHaveBody,bool capture=true,bool checkMatrices=true)
                {
                    // Same-Hunter actors share native model/palette sources.
                    // Hide only the source actor for this picture so its live
                    // body cannot masquerade as the expired victim's packets.
                    typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2),Hidden|BindingFlags.Public)!
                        .SetValue(shooter,shooter.Flags2|PlayerFlags2.HideModel);
                    // Native dead pose/root composition first settles at this
                    // exact source time; scope seams independently test restore.
                    scene.OnDrawFrame();Check(scene.OnRenderFrame(),"native prime render");DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                    var models=BodyModels(victim);var native=models.Select(m=>new ModelState(m)).ToArray();
                    Check(shooter.Health>0&&runtime.State(shooter)!=null,"source remains alive on its genuine authored floor");
                    var state=runtime.State(shooter)!.Snapshot();var health=(victim.Health,shooter.Health);var positions=(victim.Position,shooter.Position);var speeds=(victim.Speed,shooter.Speed);
                    var counters=(scene.GameState.Kills[shooter.SlotIndex],scene.GameState.Deaths[victim.SlotIndex],scene.GameState.Points[shooter.SlotIndex]);var respawn=victim.RespawnTimer;
                    var rng=(scene.Random.Rng1,scene.Random.Rng2);ulong frame=scene.FrameCount;var muzzle=Muzzle.GetValue(victim);var aim=Aim.GetValue(victim);var gun=Gun.GetValue(victim);
                    var pose=scene.ModHeadDeathPose(victim,definition);var cosmetic=(victim.CosmeticDeathState.Key,victim.CosmeticDeathState.StartTime,victim.CosmeticDeathState.Position,victim.CosmeticDeathState.Facing);
                    scene.OnDrawFrame();var items=BodyItems();Check((items.Length>0)==shouldHaveBody,"actual body packet visibility "+label);
                    Check(items.All(i=>!i.WeightedSkinning),"native-only draw evidence; no HD/Weighted4 claim");
                    if(definition.WireId!=0&&items.Length>0)Check(items.All(i=>i.Cosmetics.Effect==(int)definition.SurfaceEffect),"existing catalog surface wins "+label);
                    Check(scene.OnRenderFrame(),"real native GL corpse picture");
                    if(capture){string path=Path.Combine(output,$"{hunter}-{definition.WireId}-{label}.png");Check(ScreenCapture.SaveWindow(scene,path),"capture saved "+label);
                        captures.Add(new{hunter=hunter.ToString(),definition=definition.Key,label,path,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),frame,pose=PoseReceipt(pose),
                            progress=victim.CosmeticDeathState.Progress(scene.ElapsedTime,definition),bodyPackets=items.Length,alpha=items.Select(i=>i.Alpha).ToArray()});}
                    DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                    Check(runtime.State(shooter)!.Snapshot()==state&&runtime.AcceptedShots==1&&(victim.Health,shooter.Health)==health&&(victim.Position,shooter.Position)==positions
                        &&(victim.Speed,shooter.Speed)==speeds&&(scene.Random.Rng1,scene.Random.Rng2)==rng&&scene.FrameCount==frame&&victim.RespawnTimer==respawn
                        &&(scene.GameState.Kills[shooter.SlotIndex],scene.GameState.Deaths[victim.SlotIndex],scene.GameState.Points[shooter.SlotIndex])==counters,"draw cannot pay damage/death/shot/score/resources or advance physics/RNG/clocks");
                    Check(Equals(Muzzle.GetValue(victim),muzzle)&&Equals(Aim.GetValue(victim),aim)&&Equals(Gun.GetValue(victim),gun)&&scene.ModHeadDeathPose(victim,definition)==pose
                        &&(victim.CosmeticDeathState.Key,victim.CosmeticDeathState.StartTime,victim.CosmeticDeathState.Position,victim.CosmeticDeathState.Facing)==cosmetic,"draw preserves source anchors, corpse latch and pose clock");
                    if(checkMatrices)Check(models.Select((m,i)=>native[i].Same(m)).All(v=>v),"all19 nodes/transforms/control flags/full palettes restore across actual corpse draw");
                    Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"native corpse GL no errors");return items.Length;
                }
                AtAge(4);var peak=scene.ModHeadDeathPose(victim,definition);Check(peak.Active,"accepted lethal-head reaction is active after actual dead state");
                if(definition.PoseStyle==DeathPoseStyle.ShatterFreeze)Check(peak.Head==0&&peak.Collapse==0,"Cryo preserves frozen skeleton");
                Picture("head-kick",true);scopeProbe(victim,scene,definition,peak,Check,scopes);
                AtAge(12);Picture("collapse",true);scopeProbe(victim,scene,definition,scene.ModHeadDeathPose(victim,definition),Check,scopes);
                if(definition.WireId==0){AtAge(34);Check(scene.ModHeadDeathPose(victim,definition).Alpha<1,"Classic short corpse fades before42ticks");Picture("classic-fade",true);
                    AtAge(44);Check(!scene.ModHeadDeathPose(victim,definition).Active,"Classic42tick presentation retires");Picture("classic-ended",false);}
                else
                {
                    // Settled knees must NOT recover to an upright corpse while
                    // the selected catalog body remains visibly present.
                    float expectedCollapse=definition.WireId switch{2=>0,4=>.25f,6=>.15f,_=>1};
                    void HeldCollapse(string stage)
                    {
                        var held=scene.ModHeadDeathPose(victim,definition);
                        Check(held.Active&&held.Head==0&&MathF.Abs(held.Collapse-expectedCollapse)<.00001f&&held.Alpha==1,
                            "catalog holds settled collapse with zero helmet kick and native body alpha "+stage);
                        Check(DeathPresentationRuntime.Visible(victim.CosmeticDeathState,definition,scene.ElapsedTime)
                            &&victim.CosmeticDeathState.Progress(scene.ElapsedTime,definition)<definition.HideBodyAt,
                            "existing catalog still owns a visible body "+stage);
                        Picture(stage,true);scopeProbe(victim,scene,definition,held,Check,scopes);
                    }
                    AtAge(24);HeldCollapse("catalog-held-settle");
                    AtAge(48);HeldCollapse("catalog-held-after42");
                    while(victim.CosmeticDeathState.Progress(scene.ElapsedTime,definition)<definition.HideBodyAt+.001f)Step();
                    Picture("catalog-hidden",false);while(scene.ElapsedTime-start<definition.Duration+.02f)Step();Picture("catalog-ended",false);
                }
                Check(victim.Health==0&&scene.GameState.Deaths[victim.SlotIndex]==priorDeaths+1,"presentation cannot respawn victim or duplicate death");
                int oldLife=scene.GameState.SpawnOrdinals[victim.SlotIndex];victim.Spawn(victim.Position,Vector3.UnitZ,Vector3.UnitY,victim.NodeRef,respawn:true);Step();
                Check(scene.GameState.SpawnOrdinals[victim.SlotIndex]>oldLife&&!scene.ModHeadDeathPose(victim,definition).Active,"genuine new life clears corpse presentation");
                cases.Add(new{hunter=hunter.ToString(),definition=definition.Key,peak=PoseReceipt(peak),deadFrame,anchor=anchor.ToString(),facing=facing.ToString(),oneDamagePayment=true,nativeOnly=true});
                scene.DoCleanup();scene.UnloadGl();scene=null;
            }
            File.WriteAllText(Path.Combine(output,"head-death-rig-gl-results.json"),JsonSerializer.Serialize(new{checks,cases,captures,scopes,seconds=watch.Elapsed.TotalSeconds,renderer,version,
                scope="Native7Hunters×8catalog choices; actual M7 emitter/collision/normal death. Nonzero collapse held at24 and48ticks (Cryo0,Void.25,Float.15,others1), Classic42tick expiry unchanged; catalog visibility remains independent. Explicit source placement/HP/developer ownership. No replicated/native-shot-fact/HD/Weighted4/audio audibility/physics/physical-Hz claim. Explicit preparedLOD0/1 scope probes test exact ankles, measured segment lengths, bounded hip drop and all19-node/palette restoration; LOD1 is not a live distance switch."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally
        {
            if(scene!=null){scene.DoCleanup();scene.UnloadGl();}graphics?.Dispose();window?.Dispose();
            MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;RenderOptions.ShowCustomCosmetics=cosmetics;RenderOptions.CosmeticQuality=quality;RenderOptions.CharacterModelReplacements=hd;
            typeof(CosmeticDebug).GetProperty("Override",BindingFlags.Public|BindingFlags.Static)!.SetValue(null,oldOverride);
            typeof(CosmeticDebug).GetProperty("FixedTime",BindingFlags.Public|BindingFlags.Static)!.SetValue(null,oldFixed);
            HudProfiles.Publish(hud);FrameTiming.FrameRateCap=cap;FrameTiming.Reset();Directory.SetCurrentDirectory(cwd);
        }
    }
}
