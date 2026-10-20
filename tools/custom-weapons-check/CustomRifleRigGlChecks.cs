using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Explicit developer grants establish presentation fixtures, never public
// ownership or network combat evidence. Actual native PlayerDraw/input/GL is
// exercised; the separately labelled LOD1 path uses native node preparation.
internal static class CustomRifleRigGlChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly FieldInfo Used=typeof(Scene).GetField("_usedRenderItems",Hidden)!;
    private static readonly FieldInfo Muzzle=typeof(PlayerEntity).GetField("_muzzlePos",Hidden)!;
    private static readonly FieldInfo Aim=typeof(PlayerEntity).GetField("_aimVec",Hidden)!;
    private static readonly FieldInfo Gun=typeof(PlayerEntity).GetField("_gunVec1",Hidden)!;
    private static readonly FieldInfo Models=typeof(PlayerEntity).GetField("_bipedModelLods",Hidden)!;
    private static readonly FieldInfo ShootNodes=typeof(PlayerEntity).GetField("_shootNodes",Hidden)!;
    private static readonly MethodInfo DrawRifle=typeof(PlayerEntity).GetMethod("ModDrawCustomRifle",Hidden)!;
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static bool Finite(Matrix4 m)=>Finite(m.Row0.Xyz)&&Finite(m.Row1.Xyz)&&Finite(m.Row2.Xyz)&&Finite(m.Row3.Xyz)
        &&float.IsFinite(m.M14)&&float.IsFinite(m.M24)&&float.IsFinite(m.M34)&&float.IsFinite(m.M44);
    private static Matrix4[] Bones(PlayerEntity player)=>((ModelInstance[])Models.GetValue(player)!)
        .SelectMany(instance=>instance.Model.Nodes.Select(node=>node.Animation)).ToArray();
    private static int HeldBoxes(Scene scene,CustomRifleModel? model,bool firstPerson,bool fallback=false)
    {
        int count=0;
        foreach(var item in (Queue<RenderItem>)Used.GetValue(scene)!)
        {
            if(item.Type!=RenderItemType.Box||item.ItemCount!=8||item.ViewModel!=firstPerson)continue;
            var ink=item.OverrideColor;if(!ink.HasValue)continue;var color=ink.Value;
            if(fallback)
            {
                if(color.Xyz==new Vector3(.12f,.8f,.95f)||color.Xyz==new Vector3(.45f,.65f,.75f)
                    ||color.Xyz==new Vector3(.4f,1,.9f))count++;
            }
            else if(model!=null)for(byte material=0;material<6;material++)
                if(color.Xyz==model.Color(material).Xyz){count++;break;}
        }
        return count;
    }
    internal static int Run(string data,string output)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled;
        bool focused=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible,capturing=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive;
        bool stylus=PointerInput.StylusMode,hold=InputSettings.CustomScopeHold;int cap=FrameTiming.FrameRateCap;
        NativeWindow? window=null;DesktopGraphicsSession? graphics=null;Scene? scene=null;
        int checks=0;var watch=Stopwatch.StartNew();var captures=new List<object>();var rigs=new List<object>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("RIFLE RIG GL PASS "+label);}
        void Bound(){if(watch.Elapsed.TotalSeconds>600)throw new TimeoutException("Seven-rig GL fixture exceeded600seconds.");}
        try
        {
            data=Path.GetFullPath(data);output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh offline process owns real native GL, without headless or replay");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;GamepadManager.ClearAll();
            GamepadContexts.Focused=true;GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;
            PointerInput.StylusMode=false;PointerDevice.Reset();InputSettings.CustomScopeHold=false;
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);settings.StartVisible=true;
            settings.Title="Project Prime seven-Hunter original arm-mounted rifle qualification";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            string renderer=GL.GetString(StringName.Renderer),version=GL.GetString(StringName.Version);
            foreach(var hunter in new[]{Hunter.Samus,Hunter.Kanden,Hunter.Trace,Hunter.Sylux,Hunter.Noxus,Hunter.Spire,Hunter.Weavel})
            {
                Bound();FrameTiming.Reset();var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();
                scene=new(window.FramebufferSize,keyboard,mouse,_=>{},()=>{});scene.AddPlayer(hunter);
                scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);scene.OnLoad();scene.OnResize();
                var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(scene,Enum.Parse(mode.FieldType,"All"));
                scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;scene.GameState.ThirdPersonCamera=false;
                var player=scene.Players.Main;player.IsBot=false;
                foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
                player.Controls.Shoot.Key=Keys.Enter;player.Controls.CustomWeapon.Key=Keys.C;player.Controls.PowerBeam.Key=Keys.P;
                player.Controls.CustomOptic.Key=Keys.O;player.Controls.CustomReload.Key=Keys.R;player.Controls.Zoom.Key=Keys.Z;
                player.Controls.MouseAim=player.Controls.KeyboardAim=false;
                void Step(int count=1){FrameTiming.Reset();for(int i=0;i<count;i++){Bound();scene.OnSimulationFrame();}}
                void Press(Keys key){Key(keyboard,key,true);Step();Key(keyboard,key,false);Step();}
                Key(keyboard,Keys.Enter,true);for(int i=0;i<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
                Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(180);scene.CameraSequences.Current?.End();
                Check(player.Health>0&&player.ModIsInPlay&&scene.CameraMode==CameraMode.Player,hunter+" actual native player reaches first-person gameplay");
                PlayerSpawnEntity? spawn=null;foreach(var item in scene.GetPlayerSpawnEntities())if(item.Id==4)spawn=item;
                if(spawn==null)throw new InvalidOperationException("Authored native spawn4 missing.");
                var start=spawn.Position;CollisionResult floor=default;
                Check(CollisionDetection.CheckBetweenPoints(start+Vector3.UnitY*2,start-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),hunter+" native spawn4 has a real supporting floor");
                start.Y=floor.Position.Y+.5f;player.Reposition(start,Vector3.UnitZ,scene.Room!.GetNodeRefByPosition(start));
                player.ModSetAim(Vector3.UnitZ);player.Speed=Vector3.Zero;
                var runtime=scene.EnableDeveloperRifle(true);Check(runtime.Grant(player),hunter+" declared developer grant establishes presentation-only owner");Step(20);
                var model=scene.CustomRifleModel??throw new InvalidOperationException("Model was not prewarmed.");
                var audio=(CustomRifleAudio?)typeof(Scene).GetField("_customRifleAudio",Hidden)!.GetValue(scene);
                Check(audio==null||audio.VoiceCount==8,hunter+" original PCM audio is either unavailable with native fallback or prewarmed with exactly8 bounded voices");
                int audioVoices=audio?.VoiceCount??0;
                Check(runtime.Pickups==null&&runtime.TryPeek(player,out var initial,out bool selected)&&selected&&initial.Magazine==4&&initial.Reserve==16,
                    hunter+" developer fixture is selected4/16 without pretending to collect a public pickup");
                void Picture(string? label,bool firstPerson,int expected,bool preserve=true,bool fallback=false,Action? direct=null)
                {
                    Bound();var state=runtime.TryPeek(player,out var before,out bool chosen);var hud=scene.CustomWeaponHud(player);
                    ulong frame=scene.FrameCount,shots=runtime.AcceptedShots;var rng=(scene.Random.Rng1,scene.Random.Rng2);
                    var position=player.Position;var speed=player.Speed;var health=player.Health;var ammo=player.ModAmmo;
                    var muzzle=Muzzle.GetValue(player);var aim=Aim.GetValue(player);var gun=Gun.GetValue(player);
                    var bones=Bones(player);var pose=scene.CustomRiflePose(player);float fov=player.CameraInfo.Fov;
                    scene.OnDrawFrame();direct?.Invoke();
                    int actual=HeldBoxes(scene,model,firstPerson,fallback);
                    Check(actual==expected,$"{hunter} {label??"repeat"}: actual {(firstPerson?"FP":"TP")} module geometry {actual}/{expected}");
                    Check(scene.OnRenderFrame(),hunter+" actual native renderer produces held-rifle picture");
                    if(label!=null)
                    {
                        string path=Path.Combine(output,hunter+"-"+label+".png");Check(ScreenCapture.SaveWindow(scene,path),hunter+" capture saved before native swap");
                        captures.Add(new{hunter=hunter.ToString(),label,path,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                            firstPerson,expectedBoxes=expected,frame,alpha=FrameTiming.PresentationAlpha,scopeBlend=player.ModCustomScopeBlend,pose,
                            cameraMode=scene.CameraMode.ToString(),camera=scene.CameraPosition.ToString()});
                    }
                    DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                    Check(runtime.TryPeek(player,out var after,out bool chosenAfter)==state&&after==before&&chosenAfter==chosen
                        &&scene.CustomWeaponHud(player)==hud&&runtime.AcceptedShots==shots&&scene.FrameCount==frame
                        &&(scene.Random.Rng1,scene.Random.Rng2)==rng&&player.Position==position&&player.Speed==speed
                        &&player.Health==health&&player.ModAmmo==ammo&&player.CameraInfo.Fov==fov&&scene.CustomRiflePose(player)==pose,
                        hunter+" picture preserves canonical resources, pose clock, simulation, RNG, actor, ammo and FOV");
                    Check(Equals(Muzzle.GetValue(player),muzzle)&&Equals(Aim.GetValue(player),aim)&&Equals(Gun.GetValue(player),gun),
                        hunter+" picture preserves canonical muzzle/aim/gun vectors");
                    if(preserve)Check(Bones(player).SequenceEqual(bones),hunter+" picture restores both canonical native body skeletons");
                    Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,hunter+" held module GL has no errors");
                }
                // First draw initializes native node transforms. Subsequent
                // pictures must leave their canonical state byte-for-byte equal.
                Picture(null,true,16,preserve:false);Picture("fp-idle-lod0",true,16);
                Check(player.ModTryCustomRifleTransform(true,0,out var fp,out var fpMuzzle)&&Finite(fp)&&Finite(fpMuzzle)
                    &&(Matrix.Vec3MultMtx4(model.Muzzle,fp)-fpMuzzle).LengthSquared<1e-9f,hunter+" first-person API returns finite mount and consistent visual socket");
                foreach(double alpha in new[]{0d,.25d,.5d,.9d})
                {
                    FrameTiming.FrameRateCap=144;FrameTiming.Reset();FrameTiming.Advance(FrameTiming.StepSeconds*alpha);
                    Picture(null,true,16);
                }
                FrameTiming.Reset();scene.GameState.ThirdPersonCamera=true;
                Picture(null,false,16,preserve:false);Picture("tp-idle-lod0",false,16);
                Matrix4 tp=default;Vector3 tpMuzzle=default;
                Check(scene.ModThirdPersonCameraActive&&player.ModTryCustomRifleTransform(false,0,out tp,out tpMuzzle)&&Finite(tp)&&Finite(tpMuzzle),
                    hunter+" actual chase camera and LOD0 native elbow provide a finite world mount");
                foreach(double alpha in new[]{0d,.5d,.9d})
                {FrameTiming.FrameRateCap=144;FrameTiming.Reset();FrameTiming.Advance(FrameTiming.StepSeconds*alpha);Picture(null,false,16);}
                FrameTiming.Reset();
                // Declared presentation-only camera views expose the forearm
                // join. Native player position, aim and muzzle never move.
                var anglePosition=player.Position;var angleMuzzle=Muzzle.GetValue(player);var angleAim=Aim.GetValue(player);
                var forward=player.FacingVector.WithY(0).Normalized();var side=Vector3.Cross(Vector3.UnitY,forward).Normalized();
                if(Vector3.Dot(tpMuzzle-player.Position,side)<0)side=-side;
                var target=player.Position+Vector3.UnitY*.95f;
                scene.SetFreeCamera(true);
                void InspectAngle(string label,Vector3 eye)
                {
                    var direction=(target-eye).Normalized();var right=Vector3.Cross(direction,Vector3.UnitY).Normalized();
                    typeof(Scene).GetField("_cameraPosition",Hidden)!.SetValue(scene,eye);
                    typeof(Scene).GetField("_cameraFacing",Hidden)!.SetValue(scene,direction);
                    typeof(Scene).GetField("_cameraRight",Hidden)!.SetValue(scene,right);
                    typeof(Scene).GetField("_cameraUp",Hidden)!.SetValue(scene,Vector3.Cross(right,direction));
                    Picture(label,false,16);
                }
                InspectAngle("tp-weapon-side-idle-lod0",player.Position+side*2.5f+forward*.15f+Vector3.UnitY*1.25f);
                InspectAngle("tp-front-idle-lod0",player.Position+forward*2.6f+side*.55f+Vector3.UnitY*1.25f);
                scene.SetFreeCamera(false);
                Check(player.Position==anglePosition&&Equals(Muzzle.GetValue(player),angleMuzzle)&&Equals(Aim.GetValue(player),angleAim),
                    hunter+" side/front presentation camera setup preserves actual actor position, aim and canonical muzzle");
                var instances=(ModelInstance[])Models.GetValue(player)!;var lod1=instances[1].Model;
                var body=PlayerEntity.ModBuildBipedDrawTransform(player.Position,player.FacingVector,player.FacingVector.WithY(0),
                    Metadata.HunterScales[hunter],Fixed.ToFloat(player.Values.MinPickupHeight),out float pitch);
                typeof(PlayerEntity).GetMethod("UpdateHunterBipedPresentation",Hidden)!.Invoke(player,
                    new object?[]{lod1,lod1.GetNodeByName("Spine_1")!,body,pitch,null,null});
                var shoot=(Node?[])ShootNodes.GetValue(player)!;
                Matrix4 tp1=default;Vector3 tip1=default;
                Check(shoot[0]?.Name=="R_elbow"&&shoot[1]?.Name=="R_elbow"
                    &&player.ModTryCustomRifleTransform(false,1,out tp1,out tip1)&&Finite(tp1)&&Finite(tip1),
                    hunter+" declared native LOD1 preparation resolves actual R_elbow and finite visual muzzle");
                Picture("tp-lod1-direct-native-node-seam",false,22,preserve:false,
                    direct:()=>DrawRifle.Invoke(player,new object[]{false,1,1f}));
                scene.GameState.ThirdPersonCamera=false;
                Picture("fp-lod1-direct-render-seam",true,22,preserve:false,
                    direct:()=>{scene.BeginViewModelItems();try{DrawRifle.Invoke(player,new object[]{true,1,1f});}finally{scene.EndViewModelItems();}});
                // Restore the actual first-person draw branch after the explicit
                // TP node/LOD seams. No native skeleton or resource is edited.
                scene.GameState.ThirdPersonCamera=false;Picture(null,true,16,preserve:false);
                void LensDiagnostic(string stage)
                {
                    var currentHud=scene.CustomWeaponHud(player);runtime.TryPeek(player,out var canonical,out bool chosen);
                    Console.WriteLine($"RIFLE RIG OPTIC {hunter} {stage}: hud={currentHud.Optic} canonical={canonical.Optic} selected={chosen} ready={currentHud.Ready} visible={currentHud.Visible} phase={canonical.Phase} blend={player.ModCustomScopeBlend:R} fov={player.CameraInfo.Fov:R} nativeZoom={player.EquipInfo.Zoomed} zoomKey={player.Controls.Zoom.Key} fixedFrame={scene.FrameCount}");
                }
                // Zoom activates the remembered optic; O only changes preference
                // while unscoped and changes2.5x/4x while already scoped.
                Press(Keys.Z);Step(40);LensDiagnostic("zoom-enter-after40");
                Check(scene.CustomWeaponHud(player).Optic==1&&player.ModCustomScopeBlend>.99f,hunter+" actual Zoom input establishes2.5x scope blend");
                Picture("fp-scope25-hidden",true,0);
                scene.GameState.ThirdPersonCamera=true;Picture(null,false,16,preserve:false);Picture("tp-scope25-visible",false,16);
                scene.GameState.ThirdPersonCamera=false;Press(Keys.O);Step(40);LensDiagnostic("optic-cycle-after40");
                Check(scene.CustomWeaponHud(player).Optic==2&&player.ModCustomScopeBlend>.99f,hunter+" actual second optic input establishes4x scope blend");
                Picture("fp-scope4-hidden",true,0,preserve:false);Press(Keys.Z);Step(45);LensDiagnostic("zoom-exit-after45");
                Check(scene.CustomWeaponHud(player).Optic==0&&player.ModCustomScopeBlend==0,hunter+" optic return clears custom lens and raises module");
                Picture(null,true,16,preserve:false);
                object? firstStepAlignment=null;
                Key(keyboard,Keys.Enter,true);Step();
                if(hunter==Hunter.Samus)
                {
                    Picture("fp-fire-first-step-muzzle-alignment",true,16,preserve:false);
                    BeamProjectileEntity? flight=null;foreach(var beam in scene.GetBeamProjectileEntities())
                        if(ReferenceEquals(beam.Owner,player)&&beam.ModCustomShot!=null&&beam.Lifespan>0){flight=beam;break;}
                    Check(player.ModTryCustomRifleTransform(true,0,out _,out var visualTip)&&Finite(visualTip)&&flight!=null,
                        "Samus representative first-fire picture has an actual visual muzzle and live canonical custom flight");
                    var canonicalPositions=(flight!.SpawnPosition,flight.BackPosition,flight.Position);
                    var nativeTip=Muzzle.GetValue(player);var sourceAim=Aim.GetValue(player);Vector3 trailStart=default;
                    Check(runtime.TryPeek(player,out var liveState,out _)&&player.ModTryCustomRifleTrailStart(liveState.Owner,liveState.Weapon,out trailStart)
                        &&Finite(trailStart)&&(trailStart-scene.ModWorldSpaceViewModelPoint(visualTip)).LengthSquared<1e-9f,
                        "Samus first-fire trail start requires exact current owner/weapon and the same prepared visual muzzle/FOV conversion");
                    var view=(Matrix4)typeof(Scene).GetField("_viewMatrix",Hidden)!.GetValue(scene)!;
                    var worldProjection=(Matrix4)typeof(Scene).GetField("_perspectiveMatrix",Hidden)!.GetValue(scene)!;
                    var weaponProjection=(Matrix4)typeof(Scene).GetField("_viewModelPerspectiveMatrix",Hidden)!.GetValue(scene)!;
                    float weaponDepth=Matrix.ProjectPosition(visualTip,view,weaponProjection,out var weaponScreen);
                    float worldDepth=Matrix.ProjectPosition(trailStart,view,worldProjection,out var worldScreen);
                    var delta=weaponScreen-worldScreen;float pixelError=MathF.Sqrt(delta.X*delta.X*scene.Size.X*scene.Size.X+delta.Y*delta.Y*scene.Size.Y*scene.Size.Y);
                    Check(weaponDepth>0&&worldDepth>0&&float.IsFinite(pixelError)&&pixelError<=.25f,
                        "Samus world-space trail start projects within a quarter pixel of the displayed viewmodel muzzle");
                    var owner=liveState.Owner;var foreignLife=new WeaponOwnerContext(owner.MatchId,owner.AuthorityEpoch,owner.Slot,owner.Generation,
                        owner.Life==ushort.MaxValue?(ushort)1:(ushort)(owner.Life+1));
                    Check(!player.ModTryCustomRifleTrailStart(foreignLife,liveState.Weapon,out _)
                        &&!player.ModTryCustomRifleTrailStart(owner,WeaponIdentity.Native(BeamType.PowerBeam),out _),
                        "visual trail attachment refuses another life and native weapon identity");
                    Check((flight.SpawnPosition,flight.BackPosition,flight.Position)==canonicalPositions
                        &&Equals(Muzzle.GetValue(player),nativeTip)&&Equals(Aim.GetValue(player),sourceAim),
                        "visual trail projection reads preserve canonical projectile spawn/back/front and player muzzle/aim");
                    firstStepAlignment=new{visualMuzzle=visualTip.ToString(),worldTrailStart=trailStart.ToString(),pixelError,
                        weaponScreen=weaponScreen.ToString(),worldScreen=worldScreen.ToString(),nativeMuzzle=nativeTip!.ToString(),
                        canonicalSpawn=flight.SpawnPosition.ToString(),segmentBack=flight.BackPosition.ToString(),segmentFront=flight.Position.ToString(),
                        scope="Read-only positions after actual first Fire step; visual trail start matches projected viewmodel muzzle without changing canonical flight."};
                    Console.WriteLine("RIFLE RIG MUZZLE "+JsonSerializer.Serialize(firstStepAlignment));
                }
                Key(keyboard,Keys.Enter,false);Step();
                Check(runtime.AcceptedShots==1&&runtime.TryPeek(player,out var fired,out _)&&fired.Magazine==3,
                    hunter+" actual synthetic Fire emits and debits one developer-fixture round");
                Picture("fp-fire",true,16,preserve:false);Step(18);
                Check(scene.CustomRiflePose(player).Bolt>.8f,hunter+" fixed-step bolt animation follows the accepted shot");Picture("fp-bolt",true,16);
                Step(80);Press(Keys.R);Step(50);
                Check(runtime.TryPeek(player,out var loading,out _)&&loading.Phase==CustomWeaponPhase.Reloading&&scene.CustomRiflePose(player).Magazine>.9f,
                    hunter+" actual Reload input drives canonical reload and visual magazine travel");Picture("fp-reload",true,16,preserve:false);
                Step(150);Press(Keys.P);Picture(null,true,0,preserve:false);
                Check(!player.ModTryCustomRifleTransform(true,0,out _,out _)&&!player.ModTryCustomRifleTransform(false,0,out _,out _)
                    &&runtime.TryPeek(player,out var native,out bool custom)&&!custom&&native.Magazine==4&&native.Reserve==15,
                    hunter+" actual native fallback selection removes module without losing custom inventory");
                Press(Keys.C);Step(20);Picture(null,true,16,preserve:false);Picture("fp-native-return-reselected",true,16);
                typeof(Scene).GetMethod("ReleaseCustomRifleModel",Hidden)!.Invoke(scene,null);
                Picture("fp-missing-asset-original-fallback",true,3,preserve:false,fallback:true);
                Check(scene.CustomRifleModel==null,hunter+" pure held draw cannot reload a missing model");Step();
                Check(scene.CustomRifleModel!=null,hunter+" next actual fixed step rewarms asset after fallback");
                var currentAudio=(CustomRifleAudio?)typeof(Scene).GetField("_customRifleAudio",Hidden)!.GetValue(scene);
                Check(currentAudio==null||currentAudio.VoiceCount==8,
                    hunter+" current post-rewarm audio owner is unavailable or owns exactly8 bounded voices");
                int rewarmAudioVoices=currentAudio?.VoiceCount??0;
                rigs.Add(new{hunter=hunter.ToString(),firstPerson=fp.ToString(),firstPersonMuzzle=fpMuzzle.ToString(),thirdPerson=tp.ToString(),
                    thirdPersonMuzzle=tpMuzzle.ToString(),lod1Muzzle=tip1.ToString(),shots=runtime.AcceptedShots,audioVoices,rewarmAudioVoices,firstStepAlignment,
                    audioScope="Runtime voice availability only; no physical audibility claim."});
                scene.DoCleanup();scene.UnloadGl();Check(scene.CustomRifleModel==null&&(audio==null||audio.VoiceCount==0)
                    &&(currentAudio==null||currentAudio.VoiceCount==0)
                    &&typeof(Scene).GetField("_customRifleAudio",Hidden)!.GetValue(scene)==null,
                    hunter+" cleanup and actual visual release drop model ownership and retire both original and current post-rewarm PCM voices");scene=null;
            }
            File.WriteAllText(Path.Combine(output,"rifle-rig-gl-results.json"),JsonSerializer.Serialize(new{checks,renderer,version,
                seconds=watch.Elapsed.TotalSeconds,rigs,captures,
                scope="Seven actual offline native Hunter scenes/GL, explicit developer grant, original arm-mounted support module. FP/TP LOD0 use actual PlayerDraw; extra weapon-side/front views use only the declared free presentation camera, and Samus first-step Fire records visual/canonical muzzle/segment coordinates without modifying them. LOD1 captures deliberately add6 boxes using prepared native node/render seams alongside normal16. Synthetic input fires/reloads/scopes/selects native. Virtual presentation-alpha sampling does not claim achieved refresh Hz; no public pickup/network combat, physical controller or Android device qualification."
            },new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} seven-rig held-rifle GL checks.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("RIFLE RIG GL FAIL "+error);return 1;}
        finally
        {
            if(scene!=null){scene.DoCleanup();scene.UnloadGl();}graphics?.Dispose();window?.Dispose();
            MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;GamepadContexts.Focused=focused;GamepadContexts.MenuVisible=menu;
            GamepadContexts.Capturing=capturing;GamepadContexts.TextEntryActive=text;InputSettings.CustomScopeHold=hold;
            PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();FrameTiming.FrameRateCap=cap;FrameTiming.Reset();
            Directory.SetCurrentDirectory(cwd);
        }
    }
}
