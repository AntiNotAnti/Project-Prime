using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Local presentation-fact injection proves rendering only; it never
// impersonates an accepted damage fact.
internal static class HeadHitRigGlChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState).GetMethod("SetKeyState",Hidden|BindingFlags.Public)!
        .CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly FieldInfo Models=typeof(PlayerEntity).GetField("_bipedModelLods",Hidden)!;
    private static readonly FieldInfo Muzzle=typeof(PlayerEntity).GetField("_muzzlePos",Hidden)!;
    private static readonly FieldInfo Aim=typeof(PlayerEntity).GetField("_aimVec",Hidden)!;
    private static readonly FieldInfo Gun=typeof(PlayerEntity).GetField("_gunVec1",Hidden)!;
    private static readonly FieldInfo Used=typeof(Scene).GetField("_usedRenderItems",Hidden)!;
    private static readonly MethodInfo Compose=typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation",Hidden)!;
    private sealed class ModelState(Model model)
    {
        internal readonly Matrix4[] Animation=model.Nodes.Select(n=>n.Animation).ToArray();
        private readonly Matrix4[] _transform=model.Nodes.Select(n=>n.Transform).ToArray();
        internal readonly float[] Palette=model.MatrixStackValues.ToArray();
        private readonly (Matrix4?,Matrix4?,bool,bool)[] _control=model.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).ToArray();
        internal bool Same(Model m)=>m.Nodes.Select(n=>n.Animation).SequenceEqual(Animation)&&m.Nodes.Select(n=>n.Transform).SequenceEqual(_transform)
            &&m.MatrixStackValues.SequenceEqual(Palette)&&m.Nodes.Select(n=>(n.BeforeTransform,n.AfterTransform,n.AnimIgnoreParent,n.AnimIgnoreChild)).SequenceEqual(_control);
        internal object Differences(Model m)=>new{
            nodes=Enumerable.Range(0,Animation.Length).Where(i=>m.Nodes[i].Animation!=Animation[i]||m.Nodes[i].Transform!=_transform[i]
                ||(m.Nodes[i].BeforeTransform,m.Nodes[i].AfterTransform,m.Nodes[i].AnimIgnoreParent,m.Nodes[i].AnimIgnoreChild)!=_control[i])
                .Select(i=>new{index=i,name=m.Nodes[i].Name,animationChanged=m.Nodes[i].Animation!=Animation[i],beforeAnimation=Animation[i].ToString(),afterAnimation=m.Nodes[i].Animation.ToString(),
                    transformChanged=m.Nodes[i].Transform!=_transform[i],beforeTransform=_transform[i].ToString(),afterTransform=m.Nodes[i].Transform.ToString(),
                    beforeControl=_control[i].ToString(),afterControl=(m.Nodes[i].BeforeTransform,m.Nodes[i].AfterTransform,m.Nodes[i].AnimIgnoreParent,m.Nodes[i].AnimIgnoreChild).ToString()}).ToArray(),
            palette=Enumerable.Range(0,Palette.Length).Where(i=>m.MatrixStackValues[i]!=Palette[i]).Select(i=>new{index=i,before=Palette[i],after=m.MatrixStackValues[i]}).ToArray()};
        internal void Restore(Model m)
        {for(int i=0;i<Animation.Length;i++){m.Nodes[i].Animation=Animation[i];m.Nodes[i].Transform=_transform[i];}
            Array.Copy(Palette,(float[])m.MatrixStackValues,Palette.Length);MatrixPaletteRevision.Changed(m.MatrixStackValues);}
    }
    private static Model[] BodyModels(PlayerEntity player)=>((ModelInstance[])Models.GetValue(player)!).Select(i=>i.Model).ToArray();
    private static float[][] Submitted(Scene scene,Model model)=>((Queue<RenderItem>)Used.GetValue(scene)!)
        .Where(i=>i.Type==RenderItemType.Mesh&&ReferenceEquals(i.MatrixPaletteSource,model.MatrixStackValues)&&!i.ViewModel)
        .Select(i=>i.MatrixStack.Take(i.MatrixStackCount*16).ToArray()).ToArray();
    private static bool SamePackets(float[][] a,float[][] b)=>a.Length==b.Length&&a.Zip(b).All(p=>p.First.SequenceEqual(p.Second));
    private static bool Finite(Matrix4 m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}.All(float.IsFinite);
    internal static int Run(string data,string output)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled;
        bool focused=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible,capturing=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive;
        bool hold=InputSettings.CustomScopeHold;bool stylus=PointerInput.StylusMode,cosmetics=RenderOptions.ShowCustomCosmetics,replacements=RenderOptions.CharacterModelReplacements;
        var quality=RenderOptions.CosmeticQuality;var hud=HudProfiles.CopyCurrent();int cap=FrameTiming.FrameRateCap;
        NativeWindow? window=null;DesktopGraphicsSession? graphics=null;Scene? scene=null;
        var clock=Stopwatch.StartNew();int checks=0;var captures=new List<object>();var scopes=new List<object>();var inputCases=new List<object>();string renderer="",version="";
        void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);Console.WriteLine("HEAD HIT RIG PASS "+label);}
        void Bound(){if(clock.Elapsed.TotalSeconds>600)throw new TimeoutException("Seven-rig head-hit GL fixture exceeded600seconds.");}
        try
        {
            data=Path.GetFullPath(data);output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh offline GL owner, no headless/live/replay ambiguity");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;GamepadManager.ClearAll();GamepadContexts.Focused=true;
            GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;PointerInput.StylusMode=false;PointerDevice.Reset();InputSettings.CustomScopeHold=false;
            RenderOptions.ShowCustomCosmetics=true;RenderOptions.CosmeticQuality=CosmeticEffectQuality.Medium;RenderOptions.CharacterModelReplacements=false;
            var enabledHud=hud.DeepClone();enabledHud.ReduceMotion=false;HudProfiles.Publish(enabledHud);
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;
            settings.Title="Project Prime seven-Hunter surviving-headshot presentation qualification";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            renderer=GL.GetString(StringName.Renderer);version=GL.GetString(StringName.Version);
            foreach(var hunter in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Spire,Hunter.Weavel})
            {
                Bound();FrameTiming.FrameRateCap=144;FrameTiming.Reset();
                var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();
                scene=new(window.FramebufferSize,keyboard,mouse,_=>{},()=>{});scene.AddPlayer(hunter);
                scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
                var inputMode=typeof(Scene).GetField("_inputMode",Hidden)!;inputMode.SetValue(scene,Enum.Parse(inputMode.FieldType,"All"));
                scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;scene.GameState.ThirdPersonCamera=true;
                var player=scene.Players.Main;player.IsBot=false;
                foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
                player.Controls.Shoot.Key=Keys.Enter;player.Controls.Morph.Key=Keys.M;player.Controls.MoveUp.Key=Keys.W;player.Controls.Jump.Key=Keys.Space;
                player.Controls.Zoom.Key=Keys.Z;player.Controls.CustomWeapon.Key=Keys.C;player.Controls.PowerBeam.Key=Keys.P;player.Controls.MouseAim=player.Controls.KeyboardAim=false;
                // Reset would change the victim's presentation generation and cancel
                // the reaction. This helper advances only completed fixed steps.
                void Step(int count=1){for(int i=0;i<count;i++){Bound();FrameTiming.Advance(FrameTiming.StepSeconds);scene.OnSimulationFrame();}}
                void Press(Keys key){Key(keyboard,key,true);Step();Key(keyboard,key,false);Step();}
                Key(keyboard,Keys.Enter,true);for(int i=0;i<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
                Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(180);scene.CameraSequences.Current?.End();
                Check(player.Health>0&&player.ModIsInPlay&&scene.CameraMode==CameraMode.Player,hunter+" actual native player spawned");
                PlayerSpawnEntity? spawn=null;foreach(var entry in scene.GetPlayerSpawnEntities())if(entry.Id==4)spawn=entry;
                if(spawn==null)throw new InvalidOperationException("Authored spawn4 missing.");var position=spawn.Position;CollisionResult floor=default;
                Check(CollisionDetection.CheckBetweenPoints(position+Vector3.UnitY*2,position-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),hunter+" authored spawn has supporting native floor");
                position.Y=floor.Position.Y+.5f;player.Reposition(position,Vector3.UnitZ,scene.Room!.GetNodeRefByPosition(position));player.ModSetAim(Vector3.UnitZ);player.Speed=Vector3.Zero;Step(20);
                Check(scene.OfflineWeapons==null,hunter+" ordinary native victim needs no custom inventory");
                // Native scenes use a declared free picture camera so the face and
                // torso are visible. Actor/source position and aim stay untouched.
                void InspectBody()
                {
                    var target=player.Position+Vector3.UnitY*1.0f;var forward=player.FacingVector.WithY(0).Normalized();var side=Vector3.Cross(Vector3.UnitY,forward).Normalized();
                    var eye=player.Position+forward*2.6f+side*1.1f+Vector3.UnitY*1.3f;var facing=(target-eye).Normalized();var right=Vector3.Cross(facing,Vector3.UnitY).Normalized();
                    scene.SetFreeCamera(true);typeof(Scene).GetField("_cameraPosition",Hidden)!.SetValue(scene,eye);typeof(Scene).GetField("_cameraFacing",Hidden)!.SetValue(scene,facing);
                    typeof(Scene).GetField("_cameraRight",Hidden)!.SetValue(scene,right);typeof(Scene).GetField("_cameraUp",Hidden)!.SetValue(scene,Vector3.Cross(right,facing));
                }
                InspectBody();
                float[][] Picture(string? label,bool preserve=true)
                {
                    Bound();
                    if(preserve)
                    {
                        // Native PlayerDraw composes a new animation pose after
                        // fixed steps or fractional-alpha changes. Establish that
                        // exact native baseline at the same source time BEFORE
                        // measuring the additive scope's restoration.
                        var nativeModels=BodyModels(player);var nativeBefore=nativeModels.Select(m=>new ModelState(m)).ToArray();
                        bool show=RenderOptions.ShowCustomCosmetics;RenderOptions.ShowCustomCosmetics=false;
                        try{Picture(null,preserve:false);}finally{RenderOptions.ShowCustomCosmetics=show;}
                        for(int i=0;i<nativeModels.Length;i++)if(!nativeBefore[i].Same(nativeModels[i]))
                            Console.WriteLine("HEAD HIT NATIVE BASELINE "+JsonSerializer.Serialize(new{hunter=hunter.ToString(),label,frame=scene.FrameCount,alpha=FrameTiming.PresentationAlpha,lod=i,diff=nativeBefore[i].Differences(nativeModels[i])}));
                    }
                    var models=BodyModels(player);var state=models.Select(m=>new ModelState(m)).ToArray();
                    ulong frame=scene.FrameCount;var rng=(scene.Random.Rng1,scene.Random.Rng2);var pos=player.Position;var velocity=player.Speed;
                    var hp=player.Health;var ammo=player.ModAmmo;var available=Enumerable.Range(0,9).Select(i=>player.AvailableWeapons[(BeamType)i]).ToArray();
                    var native=player.CurrentWeapon;var muzzle=Muzzle.GetValue(player);var aim=Aim.GetValue(player);var gun=Gun.GetValue(player);
                    var pose=scene.ModHeadHitPose(player);long generation=FrameTiming.PresentationGeneration;var custom=scene.CustomWeaponHud(player);
                    var runtime=scene.OfflineWeapons;WeaponStateSnapshot resources=default;bool selected=false;
                    bool ownsResource=runtime!=null&&runtime.TryPeek(player,out resources,out selected);ulong payments=runtime?.AcceptedShots??0;
                    scene.OnDrawFrame();var submitted=Submitted(scene,models[0]);
                    if(!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing)Check(submitted.Length>0,hunter+" actual PlayerDraw submits native body palette");
                    Check(scene.OnRenderFrame(),hunter+" actual GL renderer produces picture");
                    if(label!=null)
                    {
                        string path=Path.Combine(output,hunter+"-"+label+".png");Check(ScreenCapture.SaveWindow(scene,path),hunter+" capture saved "+label);
                        captures.Add(new{hunter=hunter.ToString(),label,path,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),frame,alpha=FrameTiming.PresentationAlpha,amount=pose.Amount,
                            cameraOnly="Declared front/side free presentation camera; actor/source untouched",nativeBodyPackets=submitted.Length,
                            position=player.Position.ToString(),velocity=player.Speed.ToString(),standing=player.Flags1.TestFlag(PlayerFlags1.Standing),
                            forwardInput=player.Controls.MoveUp.IsDown,jumpInput=player.Controls.Jump.IsDown,customOptic=custom.Optic});
                    }
                    DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                    Check(scene.FrameCount==frame&&(scene.Random.Rng1,scene.Random.Rng2)==rng&&player.Position==pos&&player.Speed==velocity&&player.Health==hp&&player.ModAmmo==ammo
                        &&player.CurrentWeapon==native&&Enumerable.Range(0,9).All(i=>player.AvailableWeapons[(BeamType)i]==available[i])&&scene.CustomWeaponHud(player)==custom,
                        hunter+" draw preserves HP/ammo/inventory/custom snapshot/position/velocity/RNG/fixed clock");
                    Check(runtime==null||runtime.AcceptedShots==payments&&runtime.TryPeek(player,out var afterResources,out bool afterSelected)==ownsResource
                        &&afterResources==resources&&afterSelected==selected,hunter+" draw preserves direct custom inventory/payment snapshot when present");
                    Check(Equals(Muzzle.GetValue(player),muzzle)&&Equals(Aim.GetValue(player),aim)&&Equals(Gun.GetValue(player),gun),hunter+" draw preserves canonical muzzle/aim/gun");
                    Check(scene.ModHeadHitPose(player)==pose&&FrameTiming.PresentationGeneration==generation,hunter+" repeated picture never advances or rebinds reaction");
                    if(preserve)
                    {
                        for(int i=0;i<models.Length;i++)if(!state[i].Same(models[i]))
                            Console.WriteLine("HEAD HIT RESTORE DIFFERENCE "+JsonSerializer.Serialize(new{hunter=hunter.ToString(),label,frame=scene.FrameCount,alpha=FrameTiming.PresentationAlpha,lod=i,diff=state[i].Differences(models[i])}));
                        Check(models.Select((m,i)=>state[i].Same(m)).All(v=>v),hunter+" all canonical19-node matrices/transforms/controls/palettes restored against same-frame native baseline");
                    }
                    Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,hunter+" GL no errors");return submitted;
                }
                Picture(null,preserve:false);Picture("tp-before");Check(scene.ModHeadHitPose(player).Amount==0,hunter+" no event no overlay");
                var beforeFact=(player.Health,player.ModAmmo,player.Position,player.Speed,scene.Random.Rng1,scene.Random.Rng2);
                scene.ModAcceptLocalHeadHit(player,Vector3.UnitZ);Check((player.Health,player.ModAmmo,player.Position,player.Speed,scene.Random.Rng1,scene.Random.Rng2)==beforeFact,
                    hunter+" declared local visual fact injection never performs damage/payment/RNG");
                Step(5);Check(scene.ModHeadHitPose(player).Amount>.8f,hunter+" five actual fixed steps reach bounded reaction peak");
                var peak=Picture("tp-peak");
                RenderOptions.ShowCustomCosmetics=false;var sameFrameControl=Picture("tp-peak-disabled-same-frame");RenderOptions.ShowCustomCosmetics=true;
                Check(!SamePackets(peak,sameFrameControl),hunter+" actual submitted body palette changes from overlay, independent of animation time");
                if(hunter==Hunter.Spire)
                {
                    var body=BodyModels(player)[0];int spine=body.GetNodeIndexByName("Spine_2"),head=body.GetNodeIndexByName("Head_1");int slot=body.NodeMatrixIds.ToList().IndexOf(spine);
                    Check(slot>=0&&!body.NodeMatrixIds.Contains(head)&&peak.Length==sameFrameControl.Length
                        &&peak.Zip(sameFrameControl).Any(p=>!p.First.Skip(slot*16).Take(16).SequenceEqual(p.Second.Skip(slot*16).Take(16))),
                        "Spire actual GL body packets change weighted Spine_2 even though native Head_1 is unweighted");
                }
                // Explicit fourteen LOD scope seams complement actual LOD0
                // PlayerDraw pictures; LOD1 is not falsely called a live LOD switch.
                for(int lod=0;lod<2;lod++)
                {
                    var model=BodyModels(player)[lod];var saved=new ModelState(model);
                    try
                    {
                        var body=PlayerEntity.ModBuildBipedDrawTransform(player.Position,player.FacingVector,player.FacingVector.WithY(0),Metadata.HunterScales[hunter],Fixed.ToFloat(player.Values.MinPickupHeight),out float pitch);
                        Compose.Invoke(player,new object?[]{model,model.GetNodeByName("Spine_1")!,body,pitch,null,null});var canonical=new ModelState(model);
                        int head=model.GetNodeIndexByName("Head_1"),shoot=model.GetNodeIndexByName("R_elbow"),spine=model.GetNodeIndexByName("Spine_2");
                        int[] legs=Enumerable.Range(0,model.Nodes.Count).Where(i=>model.Nodes[i].Name.Contains("hip",StringComparison.OrdinalIgnoreCase)||model.Nodes[i].Name.Contains("knee",StringComparison.OrdinalIgnoreCase)||model.Nodes[i].Name.Contains("foot",StringComparison.OrdinalIgnoreCase)).ToArray();
                        using(HeadHitPoseOverlay.Begin(model,scene.ModHeadHitPose(player)))
                        {
                            Check(model.Nodes[head].Animation!=canonical.Animation[head]&&model.Nodes.All(n=>Finite(n.Animation))&&model.MatrixStackValues.All(float.IsFinite),$"{hunter} LOD{lod} composed native model has finite head reaction");
                            Check(model.Nodes[shoot].Animation==canonical.Animation[shoot]&&legs.Length>0&&legs.All(i=>model.Nodes[i].Animation==canonical.Animation[i]),$"{hunter} LOD{lod} shooting anchor and legs exact inside body overlay");
                            if(hunter==Hunter.Spire){int slot=model.NodeMatrixIds.ToList().IndexOf(spine);Check(slot>=0&&!model.MatrixStackValues.Skip(slot*16).Take(16).SequenceEqual(canonical.Palette.Skip(slot*16).Take(16)),$"Spire LOD{lod} weighted torso palette changes");}
                        }
                        Check(canonical.Same(model),$"{hunter} LOD{lod} scope restores every node and full palette");
                        scopes.Add(new{hunter=hunter.ToString(),lod,nodes=model.Nodes.Count,scope="Explicit native composition/scope seam in actual GL scene; LOD1 not an actual distance switch",restored=true});
                    }
                    finally{saved.Restore(model);}
                }
                long reactionGeneration=FrameTiming.PresentationGeneration;
                // Increasing virtual fractions never reset the generation or run
                // simulation; repeated pictures may sample but cannot advance age.
                double previous=FrameTiming.Alpha;
                foreach(double alpha in new[]{.25d,.5d,.9d})
                {if(alpha>previous){FrameTiming.Advance((alpha-previous)*FrameTiming.StepSeconds);previous=FrameTiming.Alpha;}Picture(null);Picture(null);}
                Check(FrameTiming.PresentationGeneration==reactionGeneration,hunter+" virtual high-refresh pictures preserve victim timeline");
                Step(24);Check(scene.ModHeadHitPose(player).Amount==0,hunter+" actual fixed steps fully settle reaction");Picture("tp-settled");
                // Real keyboard motion first; the free camera is restored only
                // after fixed-step input has earned displacement/airborne state.
                scene.SetFreeCamera(false);Step(2);var walkStart=player.Position;
                Key(keyboard,Keys.W,true);Step(6);scene.ModAcceptLocalHeadHit(player,Vector3.UnitZ);Step(5);
                Check(player.Controls.MoveUp.IsDown&&(player.Position-walkStart).WithY(0).LengthSquared>.01f
                    &&player.Speed.WithY(0).LengthSquared>.00001f&&scene.ModHeadHitPose(player).Amount>.8f,
                    hunter+" actual held W earns horizontal movement before moving reaction picture");
                inputCases.Add(new{hunter=hunter.ToString(),kind="keyboard-moving",start=walkStart.ToString(),position=player.Position.ToString(),velocity=player.Speed.ToString(),forwardDown=player.Controls.MoveUp.IsDown});
                InspectBody();Picture("tp-keyboard-moving-peak");scene.SetFreeCamera(false);Key(keyboard,Keys.W,false);Step(24);
                for(int i=0;i<120&&!player.Flags1.TestFlag(PlayerFlags1.Standing);i++)Step();
                Check(player.Flags1.TestFlag(PlayerFlags1.Standing),hunter+" jump begins on genuine supported ground");
                var jumpStart=player.Position;Key(keyboard,Keys.Space,true);Step();
                bool jumpAccepted=player.Controls.Jump.IsPressed&&player.Speed.Y>.01f;
                float launchSpeed=player.Speed.Y;Key(keyboard,Keys.Space,false);scene.ModAcceptLocalHeadHit(player,Vector3.UnitZ);Step(4);
                Check(jumpAccepted&&!player.Flags1.TestFlag(PlayerFlags1.Standing)&&player.Position.Y>jumpStart.Y+.05f
                    &&player.Speed.Y>.01f&&scene.ModHeadHitPose(player).Amount>.5f,
                    hunter+" actual Space edge earns ascending airborne state before jumping reaction picture");
                inputCases.Add(new{hunter=hunter.ToString(),kind="keyboard-jump",start=jumpStart.ToString(),position=player.Position.ToString(),velocity=player.Speed.ToString(),jumpAccepted,launchSpeed,standing=player.Flags1.TestFlag(PlayerFlags1.Standing)});
                InspectBody();Picture("tp-keyboard-jump-peak");scene.SetFreeCamera(false);Step(30);
                for(int i=0;i<120&&!player.Flags1.TestFlag(PlayerFlags1.Standing);i++)Step();
                Check(player.Flags1.TestFlag(PlayerFlags1.Standing)&&scene.ModHeadHitPose(player).Amount==0,hunter+" actual jump lands and reaction settles");
                if(hunter==Hunter.Samus)
                {
                    // Declared developer grant establishes only the presentation
                    // target's inventory; actual Zoom input establishes scope.
                    scene.GameState.ThirdPersonCamera=false;var rifle=scene.EnableDeveloperRifle(true);
                    Check(rifle.Grant(player),"Samus declared developer grant establishes M7 scoped-target presentation fixture");Step(20);Press(Keys.Z);Step(40);
                    Check(rifle.TryPeek(player,out var scoped,out bool chosen)&&chosen&&scoped.Optic==1&&scoped.Magazine==4&&scoped.Reserve==16
                        &&scene.CustomWeaponHud(player).Optic==1&&player.ModCustomScopeBlend>.99f,
                        "Samus actual Zoom input establishes2.5x custom scope without Fire or payment");
                    scene.GameState.ThirdPersonCamera=true;scene.ModAcceptLocalHeadHit(player,Vector3.UnitZ);Step(5);
                    Check(scene.CustomWeaponHud(player).Optic==1&&scene.ModHeadHitPose(player).Amount>.8f,"Samus scoped M7 victim retains canonical optic during TP reaction");
                    InspectBody();var scopedPeak=Picture("tp-m7-scoped-head-hit-peak");
                    RenderOptions.ShowCustomCosmetics=false;var scopedControl=Picture(null);RenderOptions.ShowCustomCosmetics=true;
                    Check(!SamePackets(scopedPeak,scopedControl),"Samus actual TP body remains visibly reacted while M7 owner is scoped");
                    inputCases.Add(new{hunter="Samus",kind="developer-M7-normal-Zoom-scoped-target",optic=1,scope="Actual Zoom input; developer ownership only, no public acquisition or accepted damage claim"});
                    scene.SetFreeCamera(false);scene.GameState.ThirdPersonCamera=false;Press(Keys.Z);Step(45);Press(Keys.P);Step(20);
                    Check(rifle.TryPeek(player,out var returned,out bool customSelected)&&!customSelected&&returned.Magazine==4&&returned.Reserve==16,
                        "Samus actual native selection leaves scoped-target fixture with no debit");scene.GameState.ThirdPersonCamera=true;
                }
                void Suppressed(string label,Action begin,Action end)
                {begin();scene.ModAcceptLocalHeadHit(player,Vector3.UnitX);Step();Check(scene.ModHeadHitPose(player).Amount==0,hunter+" "+label+" refuses/cancels reaction");Picture(null,preserve:false);end();Step();Check(scene.ModHeadHitPose(player).Amount==0,hunter+" "+label+" restoration cannot replay old event");}
                Suppressed("reduced motion",()=>{var p=enabledHud.DeepClone();p.ReduceMotion=true;HudProfiles.Publish(p);},()=>HudProfiles.Publish(enabledHud));
                Suppressed("quality off",()=>RenderOptions.CosmeticQuality=CosmeticEffectQuality.Off,()=>RenderOptions.CosmeticQuality=CosmeticEffectQuality.Medium);
                Suppressed("frozen victim",()=>player.ModSetFrozen(true),()=>player.ModSetFrozen(false));
                // Actual morph input supplies a real transition instead of writing
                // arbitrary form flags; this is eligibility coverage, not damage.
                scene.SetFreeCamera(false);scene.GameState.ThirdPersonCamera=true;Press(Keys.M);Check(player.IsMorphing||player.IsAltForm,hunter+" actual Morph input enters transition/form");scene.ModAcceptLocalHeadHit(player,Vector3.UnitX);
                Check(scene.ModHeadHitPose(player).Amount==0,hunter+" morphing/alternate form suppresses reaction");Step(100);
                Check(player.IsAltForm,hunter+" actual native form transition completes");scene.ModAcceptLocalHeadHit(player,Vector3.UnitZ);
                Check(scene.ModHeadHitPose(player).Amount==0,hunter+" stable alternate form refuses overlay");Press(Keys.M);Step(100);
                Check(!player.IsAltForm&&!player.IsMorphing&&!player.IsUnmorphing&&scene.ModHeadHitPose(player).Amount==0,hunter+" native return cannot resurrect suppressed reaction");
                scene.DoCleanup();scene.UnloadGl();scene=null;
            }
            string assembly=typeof(Scene).Assembly.Location;
            File.WriteAllText(Path.Combine(output,"head-hit-rig-gl-results.json"),JsonSerializer.Serialize(new{checks,renderer,version,seconds=clock.Elapsed.TotalSeconds,captures,scopes,inputCases,
                assemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly))),
                scope="Seven actual native Hunter scenes/PlayerDraw/GL with declared local visual-fact injection. No accepted damage/contact/network claim. Four standing captures each: before/peak/same-frame-disabled/settled, plus actual keyboard-earned movement and ascending-jump pictures for every Hunter. Samus scoped-target TP uses declared developer M7 ownership and normal Zoom input. Fourteen explicit native LOD-composition scopes. Virtual alpha sampling does not claim achieved refresh Hz. Character replacements disabled: HD/Weighted4 and Android/device acceptance remain unverified."},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} seven-rig native head-hit GL checks; captures={captures.Count}; LODscopes={scopes.Count}.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("HEAD HIT RIG GL FAIL "+error);return 1;}
        finally
        {
            if(scene!=null){scene.DoCleanup();scene.UnloadGl();}graphics?.Dispose();window?.Dispose();
            MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;GamepadContexts.Focused=focused;GamepadContexts.MenuVisible=menu;GamepadContexts.Capturing=capturing;GamepadContexts.TextEntryActive=text;
            InputSettings.CustomScopeHold=hold;PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();RenderOptions.ShowCustomCosmetics=cosmetics;RenderOptions.CosmeticQuality=quality;
            RenderOptions.CharacterModelReplacements=replacements;HudProfiles.Publish(hud);FrameTiming.FrameRateCap=cap;FrameTiming.Reset();Directory.SetCurrentDirectory(cwd);
        }
    }
}
