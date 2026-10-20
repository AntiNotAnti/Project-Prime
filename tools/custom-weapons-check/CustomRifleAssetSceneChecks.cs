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

// Actual offline scene/native GL, synthetic keyboard and declared native-spawn
// setup. This qualifies the original pickup asset/cache, not rifle rig animation
// or physical devices. Headless is entered last, after all GL owners retire.
internal static class CustomRifleAssetSceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly FieldInfo Attempted=typeof(Scene).GetField("_customRifleModelAttempted",Hidden)!;
    private static readonly FieldInfo Used=typeof(Scene).GetField("_usedRenderItems",Hidden)!;
    private static readonly FieldInfo Muzzle=typeof(PlayerEntity).GetField("_muzzlePos",Hidden)!;
    private static readonly FieldInfo Gun=typeof(PlayerEntity).GetField("_gunVec1",Hidden)!;
    private static readonly FieldInfo Aim=typeof(PlayerEntity).GetField("_aimVec",Hidden)!;
    private static bool Tried(Scene scene)=>(bool)Attempted.GetValue(scene)!;

    internal static int Run(string data,string output)
    {
        string cwd=Directory.GetCurrentDirectory();
        bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled;
        bool focused=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible;
        bool capturing=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive,stylus=PointerInput.StylusMode;
        NativeWindow? window=null;DesktopGraphicsSession? graphics=null;Scene? scene=null;
        int checks=0;var watch=Stopwatch.StartNew();var captures=new List<object>();
        void Check(bool value,string label)
        {if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("RIFLE ASSET GL PASS "+label);}
        void Bound(){if(watch.Elapsed.TotalSeconds>150)throw new TimeoutException("Rifle asset GL fixture exceeded150seconds.");}
        try
        {
            data=Path.GetFullPath(data);output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            Directory.SetCurrentDirectory(data);Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!Headless.Active&&!NetSession.Active&&!DemoPlayback.IsActive,"fresh process starts ordinary offline native GL");
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;GamepadManager.ClearAll();
            GamepadContexts.Focused=true;GamepadContexts.MenuVisible=GamepadContexts.Capturing=GamepadContexts.TextEntryActive=false;
            PointerInput.StylusMode=false;PointerDevice.Reset();
            var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(960,720);
            settings.StartVisible=true;settings.Title="Project Prime original Riftline M7 asset qualification";
            window=new NativeWindow(settings);graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
            string renderer=GL.GetString(StringName.Renderer),version=GL.GetString(StringName.Version);
            var keyboard=SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();
            scene=new(window.FramebufferSize,keyboard,mouse,_=>{},()=>{});
            scene.AddPlayer(Hunter.Samus);scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);
            scene.OnLoad();scene.OnResize();
            var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(scene,Enum.Parse(mode.FieldType,"All"));
            scene.GameState.MatchTime=-1;scene.GameState.PointGoal=10000;
            var player=scene.Players.Main;player.IsBot=false;
            foreach(var button in player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            player.Controls.Shoot.Key=Keys.Enter;player.Controls.MoveUp.Key=Keys.W;
            player.Controls.MouseAim=player.Controls.KeyboardAim=false;
            void Step(int count=1){for(int i=0;i<count;i++){Bound();scene.OnSimulationFrame();}}
            Key(keyboard,Keys.Enter,true);
            for(int i=0;i<600&&!player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Key(keyboard,Keys.Enter,false);scene.CameraSequences.Current?.End();Step(180);scene.CameraSequences.Current?.End();
            Check(player.Health>0&&player.ModIsInPlay&&scene.CameraMode==CameraMode.Player,"native player spawns without a custom runtime");
            Check(scene.OfflineWeapons==null&&scene.CustomRifleModel==null&&!Tried(scene),"native-only load and fixed steps never attempt original rifle asset loading");
            int Boxes(CustomRifleModel? model,bool fallback=false)
            {
                int count=0;
                foreach(var item in (Queue<RenderItem>)Used.GetValue(scene)!)
                {
                    if(item.Type!=RenderItemType.Box||item.ItemCount!=8)continue;
                    if(fallback)
                    {
                        if(item.OverrideColor==new Vector4(.12f,.8f,.95f,1)||item.OverrideColor==new Vector4(.45f,.65f,.75f,1)
                            ||item.OverrideColor==new Vector4(.4f,1,.9f,1))count++;
                    }
                    else if(model!=null)for(byte material=0;material<6;material++)
                        if(item.OverrideColor==model.Color(material)){count++;break;}
                }
                return count;
            }
            // Declared direct presentation seam: a missing cache submits original
            // fallback only; drawing cannot turn into an implicit resource load.
            scene.OnDrawFrame();int fallbackBefore=Boxes(null,true);
            CustomPickupPresentation.Draw(scene,player.Position+Vector3.UnitY);
            Check(Boxes(null,true)-fallbackBefore==3&&scene.CustomRifleModel==null&&!Tried(scene),
                "direct missing-cache pickup submission emits exactly3 fallback boxes without creating or attempting a model");
            Check(scene.OnRenderFrame(),"native-only picture plus declared fallback submits through real GL");
            DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
            Check(scene.CustomRifleModel==null&&!Tried(scene),"render completion also cannot prewarm a native-only scene");

            var runtime=scene.EnableOfflineRifleAvailability(true);var ledger=runtime.Pickups!;
            Check(scene.CustomRifleModel==null&&!Tried(scene),"public runtime construction does not hide an asset load inside activation");
            Step();var model=scene.CustomRifleModel;
            Check(model!=null&&Tried(scene)&&model.Lod(0).Length==16,"actual fixed step prewarms the verified16-box LOD0 model before its first draw");
            Check(ledger.Layout.Count==1&&ledger.Revision==0&&!runtime.TryPeek(player,out _,out _),"asset prewarm grants no inventory or pickup consumption");
            // Declared cache-retirement seam, with live public runtime and GL
            // targets intact: even an enabled draw must remain load-free.
            typeof(Scene).GetMethod("ReleaseCustomRifleModel",Hidden)!.Invoke(scene,null);
            scene.OnDrawFrame();Check(Boxes(null,true)==3&&scene.CustomRifleModel==null&&!Tried(scene),
                "enabled scene with a retired cache draws exactly3 original fallback boxes without attempting an asset load");
            Check(scene.OnRenderFrame(),"enabled missing-cache fallback renders through actual native GL");
            DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
            Check(scene.CustomRifleModel==null&&!Tried(scene),"enabled pure draw and render completion leave missing model untouched");
            Step();model=scene.CustomRifleModel;
            Check(model!=null&&Tried(scene)&&model.Lod(0).Length==16,"only the following actual fixed step restores the verified original model");
            PlayerSpawnEntity? spawn=null;foreach(var item in scene.GetPlayerSpawnEntities())if(item.Id==4)spawn=item;
            if(spawn==null)throw new InvalidOperationException("Authored native spawn4 missing.");
            var start=spawn.Position;CollisionResult floor=default;
            Check(CollisionDetection.CheckBetweenPoints(start+Vector3.UnitY*2,start-Vector3.UnitY*6,TestFlags.Players,scene,ref floor),
                "declared native spawn4 approach resolves actual room floor");
            start.Y=floor.Position.Y;var center=ledger.Layout.Placements[0].Position;
            var walk=(center-start).WithY(0).Normalized();
            player.Reposition(start.AddY(.5f),walk,scene.Room!.GetNodeRefByPosition(start));player.ModSetAim(walk);player.Speed=Vector3.Zero;Step(15);
            scene.SetFreeCamera(true);var camera=center+new Vector3(2,1.5f,-3);var facing=(center-camera).Normalized();
            var right=Vector3.Cross(facing,Vector3.UnitY).Normalized();
            void Camera(string name,Vector3 value)=>typeof(Scene).GetField(name,Hidden)!.SetValue(scene,value);
            Camera("_cameraPosition",camera);Camera("_cameraFacing",facing);Camera("_cameraRight",right);Camera("_cameraUp",Vector3.Cross(right,facing));
            Check(ledger.TryState(0,out var available)&&available.Available&&ledger.Revision==0&&!runtime.TryPeek(player,out _,out _),
                "presentation camera leaves actual authored item available and life empty");
            void Draw(string? name,int expected)
            {
                Bound();ulong frame=scene.FrameCount,revision=ledger.Revision,shots=runtime.AcceptedShots;uint world=ledger.AuthorityFrame;
                var position=player.Position;var speed=player.Speed;var ammo=player.ModAmmo;int health=player.Health;
                var rng=(scene.Random.Rng1,scene.Random.Rng2);var match=(scene.GameState.MatchState,scene.GameState.MatchTime);
                var muzzle=Muzzle.GetValue(player);var gun=Gun.GetValue(player);var aim=Aim.GetValue(player);
                var hud=scene.CustomWeaponHud(player);bool owned=runtime.TryPeek(player,out var state,out bool selected);ledger.TryState(0,out var item);
                scene.OnDrawFrame();Check(Boxes(model)==expected&&Boxes(null,true)==0,"shared pickup submits exactly"+expected+" original LOD0 boxes and no fallback");
                Check(scene.OnRenderFrame(),"original pickup picture renders through actual native GL");
                if(name!=null)
                {
                    string composite=Path.Combine(output,name+"-window.png"),worldFile=Path.Combine(output,name+"-world.png");
                    Check(ScreenCapture.SaveWindow(scene,composite)&&ScreenCapture.Save(scene,worldFile),"actual original-pickup window and scene-target captures saved before swap");
                    captures.Add(new{name,composite,worldFile,compositeSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(composite))),
                        worldSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(worldFile))),frame,expectedBoxes=expected});
                }
                DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                bool ownedAfter=runtime.TryPeek(player,out var after,out bool selectedAfter);ledger.TryState(0,out var itemAfter);
                Check(ReferenceEquals(scene.CustomRifleModel,model)&&Tried(scene),"repeated draw borrows the same prewarmed scene model");
                Check(scene.FrameCount==frame&&ledger.AuthorityFrame==world&&ledger.Revision==revision&&itemAfter==item&&ledger.PendingOutcomes==0
                    &&runtime.AcceptedShots==shots&&player.Position==position&&player.Speed==speed&&player.ModAmmo==ammo&&player.Health==health
                    &&ownedAfter==owned&&after==state&&selectedAfter==selected&&scene.CustomWeaponHud(player)==hud
                    &&(scene.Random.Rng1,scene.Random.Rng2)==rng&&(scene.GameState.MatchState,scene.GameState.MatchTime)==match,
                    "draw-only pictures preserve clock, world item, resources, game state, actor, native ammo and both gameplay RNG streams");
                Check(Equals(Muzzle.GetValue(player),muzzle)&&Equals(Gun.GetValue(player),gun)&&Equals(Aim.GetValue(player),aim),
                    "draw preserves exact canonical muzzle, gun vector and aim vector");
                Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"original asset draw/capture/present has no GL error");
            }
            Draw("original-m7-before-contact",16);for(int i=0;i<12;i++)Draw(null,16);
            scene.OnDrawFrame();int beforeLod=Boxes(model);
            CustomRiflePresentation.Draw(scene,model!,Matrix4.CreateTranslation(center+Vector3.UnitY),1,1);
            Check(model!.Lod(1).Length==6&&Boxes(model)-beforeLod==6,
                "declared direct LOD1 presentation seam submits exactly6 original boxes through the same renderer");
            Check(scene.OnRenderFrame(),"native GL renders the original six-box LOD1 submission");
            DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
            Check(GL.GetError()==OpenTK.Graphics.OpenGL.ErrorCode.NoError,"original LOD1 draw finishes without GL error");
            int beforeHealth=player.Health;scene.SetFreeCamera(false);player.ModSetAim(walk);Key(keyboard,Keys.W,true);
            int walkingSteps=0;for(;walkingSteps<120&&ledger.Revision==0;walkingSteps++)Step();Key(keyboard,Keys.W,false);Step(2);
            Check(walkingSteps>0&&walkingSteps<120&&ledger.Revision==1&&player.Health==beforeHealth
                &&runtime.TryPeek(player,out var funded,out _)&&funded.Magazine==4&&funded.Reserve==16&&runtime.AcceptedShots==0,
                "actual synthetic W/native item phase consumes pictured original station and funds4/16 without firing");
            scene.SetFreeCamera(true);Camera("_cameraPosition",camera);Camera("_cameraFacing",facing);Camera("_cameraRight",right);Camera("_cameraUp",Vector3.Cross(right,facing));
            Draw("original-m7-after-contact",0);for(int i=0;i<4;i++)Draw(null,0);
            scene.UnloadGl();Check(scene.CustomRifleModel==null&&!Tried(scene),"actual visual teardown drops model ownership and attempted flag");
            // CPU-only actual fixed step after visual teardown. No render is
            // attempted against the released world targets or display lists.
            Step();Check(scene.CustomRifleModel is {} renewed&&!ReferenceEquals(renewed,model)&&Tried(scene),
                "next actual fixed step rewarms a distinct verified model after scene visual retirement");
            scene.DoCleanup();Check(scene.CustomRifleModel==null&&!Tried(scene),"actual scene cleanup releases the rewarm and custom runtime");scene=null;
            graphics.Dispose();graphics=null;window.Dispose();window=null;

            // Headless.Enter is a one-way fresh-process mode. All GL owners have
            // retired above; this final scene never creates a graphics context.
            Headless.Enter();var hk=SyntheticInput.CreateKeyboard();var hm=SyntheticInput.CreateMouse();
            scene=new(new Vector2i(256,192),hk,hm,_=>{},()=>{});scene.AddPlayer(Hunter.Samus);
            scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);scene.OnLoad();
            scene.EnableOfflineRifleAvailability(true);scene.OnSimulationFrame();scene.UpdateCustomWeaponPresentation();
            Check(scene.CustomRifleModel==null&&!Tried(scene)&&CustomRifleModel.LoadBuiltin()==null,
                "actual headless public scene and direct builtin guard never attempt or retain a presentation model");
            scene.DoCleanup();scene.UnloadGl();scene=null;
            File.WriteAllText(Path.Combine(output,"rifle-assets-gl-results.json"),JsonSerializer.Serialize(new{
                checks,renderer,version,seconds=watch.Elapsed.TotalSeconds,walkingSteps,captures,
                modelResource=CustomRifleModel.Resource,modelSha256=CustomRifleModel.Digest,
                scope="Actual offline public/native GL, original verified pickup mesh, synthetic W/native contact, declared native spawn4 and free-camera vantage. Actual fixed-step prewarm and visual teardown/rewarm; final headless CPU scene. No rifle rig/animation, online combat, achieved refresh rate or physical Android/device qualification."
            },new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS {checks} original rifle asset scene/GL checks.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("RIFLE ASSET GL FAIL "+error);return 1;}
        finally
        {
            if(scene!=null){scene.DoCleanup();scene.UnloadGl();}graphics?.Dispose();window?.Dispose();
            MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;GamepadContexts.Focused=focused;GamepadContexts.MenuVisible=menu;
            GamepadContexts.Capturing=capturing;GamepadContexts.TextEntryActive=text;PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();
            Directory.SetCurrentDirectory(cwd);
        }
    }
}
