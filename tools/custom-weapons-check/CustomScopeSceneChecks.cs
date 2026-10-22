using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Real local native simulation/public input. Headless scope and native GL
// cadence are separate fresh-process modes. Input is synthetic, not a device test.
internal static class CustomScopeSceneChecks
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Action<KeyboardState,Keys,bool> Key=typeof(KeyboardState)
        .GetMethod("SetKeyState",Hidden|BindingFlags.Public)!.CreateDelegate<Action<KeyboardState,Keys,bool>>();
    private static readonly Action<MouseState,Vector2> MousePosition=typeof(MouseState)
        .GetProperty("Position",Hidden|BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();
    private static int _checks;
    private static readonly Stopwatch Deadline=new();
    private static void Check(bool condition,string label)
    {if(!condition)throw new InvalidOperationException(label);_checks++;Console.WriteLine("CUSTOM SCOPE PASS "+label);}
    private static void Bound(){if(Deadline.Elapsed.TotalSeconds>120)throw new TimeoutException("Custom scope fixture exceeded120seconds");}
    private sealed class Fixture : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly PlayerEntity Player;
        internal readonly KeyboardState Keyboard=SyntheticInput.CreateKeyboard();
        internal readonly MouseState Mouse=SyntheticInput.CreateMouse();
        internal readonly OfflineCustomWeaponRuntime Runtime;
        private readonly NativeWindow? _window;
        internal Fixture(NativeWindow? window=null)
        {
            _window=window;Rng.SetRng1(8128);Rng.SetRng2(0x8128a5a5);
            Scene=new(window?.FramebufferSize??new Vector2i(256,192),Keyboard,Mouse,_=>{},()=>{});
            Scene.AddPlayer(Hunter.Samus);Scene.AddRoom("MP3 PROVING GROUND",GameMode.Battle,playerCount:1,nodeLayerMask:0);
            Scene.OnLoad();if(window!=null)Scene.OnResize();
            var mode=typeof(Scene).GetField("_inputMode",Hidden)!;mode.SetValue(Scene,Enum.Parse(mode.FieldType,"All"));
            Scene.GameState.MatchTime=-1;Scene.GameState.PointGoal=10000;
            Player=Scene.Players.Main;Player.IsBot=false;
            foreach(var button in Player.Controls.All){button.Type=ButtonType.Key;button.Key=Keys.Unknown;}
            Player.Controls.Shoot.Key=Keys.Enter;Player.Controls.Zoom.Key=Keys.Z;Player.Controls.CustomWeapon.Key=Keys.C;
            Player.Controls.CustomOptic.Key=Keys.O;Player.Controls.CustomReload.Key=Keys.R;Player.Controls.PowerBeam.Key=Keys.D1;
            Player.Controls.MouseAim=false;Player.Controls.KeyboardAim=false;
            Key(Keyboard,Keys.Enter,true);
            for(int i=0;i<600&&!Player.LoadFlags.TestFlag(LoadFlags.Spawned);i++)Step();
            Release();Scene.CameraSequences.Current?.End();Step(180);Scene.CameraSequences.Current?.End();
            Console.WriteLine($"SCOPE FIXTURE native spawn: inPlay={Player.ModIsInPlay}, HP={Player.Health}, camera={Scene.CameraMode}, headless={Headless.Active}, main={Player.IsMainPlayer}, frame={Scene.FrameCount}");
            // Native Headless selects Roam and skips native HUD texture initialization.
            // The GL fixture separately requires the native first-person Player camera.
            Check(Player.ModIsInPlay&&Player.Health>0&&ReferenceEquals(Player,Scene.Players.Main)
                &&Scene.CameraMode==(Headless.Active?CameraMode.Roam:CameraMode.Player)&&!NetSession.Active,
                "real local native player reaches expected native camera before custom grant");
            Runtime=Scene.EnableDeveloperRifle(true);Check(Runtime.Grant(Player),"explicit offline developer grant binds actual local scope owner");
            Step(2);
        }
        internal void Release(){foreach(var key in new[]{Keys.Enter,Keys.Z,Keys.C,Keys.O,Keys.R,Keys.D1})Key(Keyboard,key,false);}
        internal void Step(int count=1)
        {for(int i=0;i<count;i++){Bound();Scene.OnSimulationFrame();if(_window!=null)Draw();}}
        internal void Press(Keys key){Release();Step();Key(Keyboard,key,true);Step();}
        internal WeaponStateSnapshot State=>Runtime.TryPeek(Player,out var state,out _)
            ?state:throw new InvalidOperationException("Scope fixture has no current read-only custom state");
        internal byte[]? ReticlePixels;
        internal void Draw(string? capture=null,bool sampleReticle=false)
        {
            Bound();var before=Scene.CustomWeaponHud(Player);ulong frame=Scene.FrameCount;
            var state=Runtime==null?default:State;float fov=Player.CameraInfo.Fov;
            Scene.OnDrawFrame();Check(Scene.OnRenderFrame(),"actual native world/HUD render remains active");
            if(capture!=null)Check(ScreenCapture.SaveWindow(Scene,capture),
                "actual visible-window world plus HUD composite captured before swap");
            if(sampleReticle)
            {
                var pixels=Scene.ReadWindowBuffer(out int width,out int height)
                    ??throw new InvalidOperationException("Visible reticle sample has no native composite");
                const int side=128;ReticlePixels=new byte[side*side*3];
                for(int y=0;y<side;y++)Array.Copy(pixels,((height/2-side/2+y)*width+width/2-side/2)*3,
                    ReticlePixels,y*side*3,side*3);
            }
            DesktopGraphicsSession.Present(_window!);Scene.AfterRenderFrame();
            if(Runtime!=null)Check(Scene.FrameCount==frame&&State==state&&Scene.CustomWeaponHud(Player)==before&&Player.CameraInfo.Fov==fov,
                "draw-only picture cannot advance custom resources, fixed HUD snapshot or simulation FOV");
        }
        public void Dispose(){Scene.DoCleanup();}
    }
    internal static int Run(string data,bool gl=false,string? output=null)
    {
        string directory=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,hold=InputSettings.CustomScopeHold;
        bool stylus=PointerInput.StylusMode,focused=GamepadContexts.Focused,menu=GamepadContexts.MenuVisible;
        bool capturing=GamepadContexts.Capturing,text=GamepadContexts.TextEntryActive;
        float sensitivity=InputSettings.MouseSensitivity,two=InputSettings.CustomScope25Sensitivity,four=InputSettings.CustomScope4Sensitivity;
        int userFov=RenderOptions.FieldOfView,cap=FrameTiming.FrameRateCap;var hud=HudProfiles.CopyCurrent();
        _checks=0;Deadline.Restart();
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Check(!NetSession.Active&&!DemoPlayback.IsActive,"fresh ordinary non-replay process owns scope fixture");
            if(!gl)Headless.Enter();else Check(!Headless.Active,"GL fixture never enters irreversible Headless mode");
            MapAudit.ForceEveryone=true;PointerInput.StylusMode=false;PointerDevice.Reset();GamepadManager.ClearAll();
            GamepadContexts.Focused=true;GamepadContexts.MenuVisible=false;GamepadContexts.Capturing=false;GamepadContexts.TextEntryActive=false;
            InputSettings.MouseSensitivity=1;InputSettings.CustomScopeHold=false;
            InputSettings.CustomScope25Sensitivity=.7f;InputSettings.CustomScope4Sensitivity=1.3f;
            if(gl)RunGl(output);else{using var fixture=new Fixture();ScopeAndHud(fixture);}
            Console.WriteLine($"PASS {_checks} custom scope {(gl?"native GL virtual cadence":"headless local scene")} checks.");
            Console.WriteLine("SCOPE: actual offline local public controls, custom canonical state, fixed HUD/cache and optics. Synthetic keyboard/mouse; no physical controller/Android, online source or achieved display-Hz claim.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("CUSTOM SCOPE FAIL "+error);return 1;}
        finally
        {
            NetSession.Stop();MapAudit.ForceEveryone=force;InputSettings.CustomScopeHold=hold;
            InputSettings.CustomScope25Sensitivity=two;InputSettings.CustomScope4Sensitivity=four;InputSettings.MouseSensitivity=sensitivity;
            PointerInput.StylusMode=stylus;PointerDevice.Reset();GamepadManager.ClearAll();
            GamepadContexts.Focused=focused;GamepadContexts.MenuVisible=menu;GamepadContexts.Capturing=capturing;GamepadContexts.TextEntryActive=text;
            RenderOptions.FieldOfView=userFov;HudProfiles.Publish(hud);FrameTiming.FrameRateCap=cap;FrameTiming.Reset();
            Directory.SetCurrentDirectory(directory);
        }
    }
    private static void ScopeAndHud(Fixture f)
    {
        var p=f.Player;var nativeAmmo=p.ModAmmo;float normal=Fixed.ToFloat(p.Values.NormalFov)*2;
        var initial=f.Scene.CustomWeaponHud(p);
        var profile=HudProfiles.CopyCurrent();profile.Mode=HudMode.Custom;
        profile.Elements["core.ammo"].Visibility=HudVisibility.AmmoNotFull;HudProfiles.Publish(profile);
        bool AmmoVisible()=>(bool)typeof(PlayerEntity).GetMethod("HudElementVisible",Hidden)!.Invoke(p,new object[]{2})!;
        Check(!AmmoVisible(),"actual AmmoNotFull HUD condition hides a full custom magazine rather than reading native fallback ammunition");
        Check(initial.Visible&&initial.Ready&&initial.State==f.State&&initial.MagazineCapacity==4&&initial.Optic==0,
            "fixed HUD snapshot exposes canonical initial magazine/reserve without a native slot");
        f.Press(Keys.Z);f.Release();f.Step(40);
        VerifyLens(f,1,2500,.7f,normal);
        VerifyAimPaths(f,normal);
        f.Press(Keys.O);f.Release();f.Step(40);VerifyLens(f,2,4000,1.3f,normal);
        foreach(int baseFov in new[]{60,78,90,120})
        {
            RenderOptions.FieldOfView=baseFov;
            float projected=RenderOptions.ScaleCameraFov(p.CameraInfo.Fov);
            double ratio=Math.Tan(baseFov*Math.PI/360)/Math.Tan(projected*Math.PI/360);
            Check(Math.Abs(ratio-4)<.0001&&RenderOptions.FieldOfView==baseFov,
                "actual settled scope preserves user-selected world FOV and true4x projection ratio");
        }
        f.Press(Keys.Enter);f.Release();f.Step();
        Check(f.Runtime.AcceptedShots==1&&f.State.Magazine==3&&f.Scene.CustomWeaponHud(p).State.Magazine==3,
            "public scoped fire pays real canonical magazine and cached HUD shows actual spend");
        Check(AmmoVisible(),"actual AmmoNotFull HUD condition shows spent custom magazine even when native fallback uses no ammunition");
        f.Press(Keys.Enter);f.Release();f.Step();
        Check(f.Runtime.AcceptedShots==1&&f.State.Magazine==3,
            "real cooldown-denied input cannot manufacture a HUD resource payment");
        f.Press(Keys.R);f.Release();
        var reload=f.State;var cached=f.Scene.CustomWeaponHud(p);ulong frame=f.Scene.FrameCount;float fov=p.CameraInfo.Fov;
        Check(reload.Phase==CustomWeaponPhase.Reloading&&reload.Optic==0&&cached.State==reload&&cached.Optic==0,
            "actual public reload clears scope and exposes canonical reload phase");
        bool unchanged=true;for(int i=0;i<100000;i++)unchanged&=f.Scene.CustomWeaponHud(p)==cached;
        Check(unchanged&&f.State==reload&&f.Scene.FrameCount==frame&&p.CameraInfo.Fov==fov,
            "100000 read-only HUD samples cannot finish reload, move source time or step scope easing");
        f.Step(138);
        Check(f.State.Magazine==4&&f.State.Reserve==15&&f.State.Phase!=CustomWeaponPhase.Reloading,
            "only actual fixed simulation completes138-tick reload and transfers the one spent round");
        Check(!AmmoVisible(),"actual AmmoNotFull HUD condition follows refilled magazine without borrowing depleted custom reserve");
        InputSettings.CustomScopeHold=true;f.Press(Keys.Z);f.Step(40);
        Check(f.State.Optic==2&&p.ModCustomScopeActive,"physical hold scope uses remembered4x lens through ordinary input route");
        Key(f.Keyboard,Keys.O,true);f.Step();Key(f.Keyboard,Keys.O,false);f.Step(40);VerifyLens(f,1,2500,.7f,normal);
        f.Release();f.Step(40);
        Check(f.State.Optic==0&&!p.ModCustomScopeActive&&Math.Abs(p.CameraInfo.Fov-normal)<.001,
            "releasing physical hold scope returns actual camera to hip FOV without native zoom");
        Check(p.ModAmmo==nativeAmmo&&!p.EquipInfo.Zoomed&&p.EquipInfo.ChargeLevel==0,
            "custom optics, fire and reload leave native ammunition, charge and scope untouched");
        f.Press(Keys.Z);f.Step(40);RoleBoundary(f,normal);p.Health=0;f.Scene.UpdateCustomWeaponPresentation();
        Check(!p.ModCustomScopeActive&&!p.ModCustomScopeSelected&&!f.Scene.CustomWeaponHud(p).Ready
            &&Math.Abs(p.CameraInfo.Fov-normal)<.001,
            "actual death invalidates readable custom HUD resources and clears old lens immediately");
        var oldOwner=initial.State.Owner;f.Step();
        p.Spawn(p.Position,Vector3.UnitZ,Vector3.UnitY,p.NodeRef,respawn:true);f.Runtime.Step();f.Release();f.Step(2);
        var fresh=f.Scene.CustomWeaponHud(p);
        Check(fresh.Ready&&fresh.State.Owner.Life!=oldOwner.Life&&fresh.State.Magazine==4&&fresh.Optic==0
            &&!p.ModCustomScopeActive&&Math.Abs(p.CameraInfo.Fov-normal)<.001,
            "real native respawn publishes a fresh owner magazine and cannot revive old-life scope or HUD state");
    }
    // Explicit role-policy seam only: no socket/replay/source certificate is fabricated.
    private sealed class ReplicaServices(ISceneServices original):ISceneServices
    {
        public bool IsReplica=>true;
        public bool MayEndOnScore=>original.MayEndOnScore;
        public bool ShouldLeaveAfterMatch=>original.ShouldLeaveAfterMatch;
        public bool SuppressDamage=>original.SuppressDamage;
        public bool AllowsPresentationSideEffects=>original.AllowsPresentationSideEffects;
        public IPlayerReplicationHost PlayerReplication=>original.PlayerReplication;
        public bool DisablePowerups=>original.DisablePowerups;
        public MphRead.Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile=>original.NetworkWorldProfile;
        public bool ReplicatesHealthSpawns=>original.ReplicatesHealthSpawns;
        public bool TryGetHealthSpawn(short id,out HealthSpawnState state)=>original.TryGetHealthSpawn(id,out state);
    }
    private static void RoleBoundary(Fixture f,float normal)
    {
        var role=typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        var authority=typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!;
        var services=typeof(Scene).GetProperty(nameof(Scene.Services))!;
        var originalRole=NetSession.Role;bool originalAuthority=NetSession.IsAuthority;var originalServices=f.Scene.Services;
        foreach(bool replica in new[]{false,true})
        {
            f.Scene.UpdateCustomWeaponPresentation();f.Step(40);
            Check(f.Player.ModCustomScopeActive&&f.Scene.CustomWeaponHud(f.Player).Visible,
                "real scoped owner has a populated HUD and lens before declared role boundary");
            var state=f.State;ulong frame=f.Scene.FrameCount;
            try
            {
                if(replica)services.SetValue(f.Scene,new ReplicaServices(originalServices));
                else{role.SetValue(null,NetRole.Server);authority.SetValue(null,true);}
                f.Scene.UpdateCustomWeaponPresentation();
                Check(f.Scene.CustomWeaponHud(f.Player)==default&&!f.Player.ModCustomScopeActive
                    &&!f.Player.ModCustomScopeSelected&&Math.Abs(f.Player.CameraInfo.Fov-normal)<.001
                    &&f.Scene.FrameCount==frame,
                    "declared "+(replica?"replica":"authority")+" role boundary clears cached custom HUD/lens without stepping simulation");
            }
            finally {role.SetValue(null,originalRole);authority.SetValue(null,originalAuthority);services.SetValue(f.Scene,originalServices);}
            Check(f.State==state,"declared role cleanup cannot debit or advance canonical custom resources");
        }
        f.Scene.UpdateCustomWeaponPresentation();f.Step(40);
        Check(f.Player.ModCustomScopeActive&&f.Scene.CustomWeaponHud(f.Player).Visible,
            "restored local presentation derives its lens and HUD afresh from unchanged canonical state");
    }
    private static void VerifyLens(Fixture f,byte optic,int milli,float sensitivity,float normal)
    {
        var hud=f.Scene.CustomWeaponHud(f.Player);
        Check(f.State.Optic==optic&&hud.Optic==optic&&hud.State==f.State&&hud.Visible&&hud.Ready,
            "public scope input updates readonly HUD from actual canonical optic and resource snapshot");
        Check(f.Player.ModCustomScopeSelected&&f.Player.ModCustomScopeActive&&!f.Player.EquipInfo.Zoomed
            &&Math.Abs(f.Player.CameraInfo.Fov-CustomScopeMath.TargetFov(normal,milli))<.001
            &&Math.Abs(f.Player.ModCustomScopeBlend-1)<.001&&Math.Abs(f.Player.ModCustomScopeSensitivity-sensitivity)<.001,
            "actual fixed-step camera settles to custom lens with independent custom sensitivity and no native zoom");
    }
    private static void VerifyAimPaths(Fixture f,float normal)
    {
        var p=f.Player;float expected=CustomScopeMath.AimScale(normal,p.CameraInfo.Fov)*p.ModCustomScopeSensitivity;
        var before=p.ModGunVector;ulong frame=f.Scene.FrameCount;
        object[] args={1d,4f,0f,0f,0f,0f,0f};
        typeof(PlayerEntity).GetMethod("ModRenderAimDelta",Hidden)!.Invoke(p,args);
        float render=(float)args[5];
        Check(Math.Abs(render+expected)<.00001&&p.ModGunVector==before&&f.Scene.FrameCount==frame,
            "actual late-render mouse helper uses same custom optical/sensitivity scale without mutating aim");
        p.Controls.MouseAim=true;MousePosition(f.Mouse,f.Mouse.Position+new Vector2(4,0));f.Step();p.Controls.MouseAim=false;
        var after=p.ModGunVector;
        double yaw=(Math.Atan2(after.X,after.Z)-Math.Atan2(before.X,before.Z))*180/Math.PI;
        yaw=(yaw+540)%360-180;
        Check(Math.Abs(yaw-render)<.005,
            "real fixed-step mouse aim and native late-render helper agree on custom scoped yaw scale");
        Check(Vector3.Dot(p.CameraInfo.Facing.Normalized(),p.ModGunVector.Normalized())>.999999f,
            "actual scoped native camera faces the physical gun ray after same-step aim input");
    }
    private readonly record struct Tick(ulong Frame,WeaponStateSnapshot State,CustomWeaponHudState Hud,float Fov,float Sensitivity);
    private static List<string> CaptureProfiles(Fixture f,int hz,string lens,string? directory)
    {
        var captures=new List<string>();
        foreach(var mode in new[]{HudMode.Classic,HudMode.ProjectPrime,HudMode.Custom})
        {
            var profile=HudProfileDefaults.Create(mode==HudMode.Classic?"Classic":"Project Prime");
            profile.Mode=mode;HudProfiles.Publish(profile);
            if(mode==HudMode.Custom)VerifyBaseAmmoPanelBounds(f);
            string? file=directory==null?null:Path.Combine(directory,$"scope{lens}x-{hz}-{mode}.png");
            f.Draw(file);if(file!=null)captures.Add(file);
        }
        return captures;
    }
    private static void VerifyBaseAmmoPanelBounds(Fixture f)
    {
        // Declared typed-panel footprint in native HUD units. Test the actual
        // profile transform against the viewport; this is not pixel/text OCR.
        float aspect=f.Scene.Size.Y/192f*(256f/f.Scene.Size.X);
        float left=256-60*aspect,top=170;
        using var layout=f.Scene.PushHudElement(2,left,top);
        float Read(string field)=>(float)typeof(Scene).GetField(field,Hidden)!.GetValue(f.Scene)!;
        float scale=Read("_profileHudScale"),x=Read("_profileHudX"),y=Read("_profileHudY");
        float x0=(left*scale+x)*f.Scene.Size.X/256f;
        float x1=((left+58*aspect)*scale+x)*f.Scene.Size.X/256f;
        float y0=(top*scale+y)*f.Scene.Size.Y/192f;
        float y1=((top+20)*scale+y)*f.Scene.Size.Y/192f;
        Console.WriteLine($"SCOPE DEFAULT AMMO PANEL BOUNDS {x0:F3},{y0:F3}..{x1:F3},{y1:F3} viewport={f.Scene.Size.X}x{f.Scene.Size.Y}");
        Check(x0>=0&&x1<=f.Scene.Size.X&&y0>=0&&y1<=f.Scene.Size.Y&&x1>x0&&y1>y0,
            "actual base Custom profile transform keeps declared58x20 typed ammo panel within visible viewport");
    }
    private static object CaptureActualFirePulse(NativeWindow window,string? directory)
    {
        // Separate fresh scene after all cadence traces: public input accepts a
        // real local shot. Repeated pictures do not inject or age feedback.
        InputSettings.CustomScopeHold=false;RenderOptions.FieldOfView=78;
        using var f=new Fixture(window);
        f.Press(Keys.Z);f.Release();f.Step(40);
        f.Press(Keys.Enter);f.Release();
        var pulse=f.Scene.CustomFeedback(f.Player);var state=f.State;ulong frame=f.Scene.FrameCount;
        Check(f.Runtime.AcceptedShots==1&&state.Magazine==3&&pulse.FirePulse>0,
            "separate native GL scene has real public-input accepted shot and live bounded fire pulse");
        var captures=new List<string>();byte[]? pulsing=null;
        foreach(var mode in new[]{HudMode.Classic,HudMode.ProjectPrime,HudMode.Custom})
        {
            var profile=HudProfileDefaults.Create(mode==HudMode.Classic?"Classic":"Project Prime");
            profile.Mode=mode;profile.ReduceMotion=false;HudProfiles.Publish(profile);
            if(mode!=HudMode.Classic)Check(Features.CustomCrosshair,
                "default Pro/Custom generic crosshair path remains selected during actual fire feedback");
            string? file=directory==null?null:Path.Combine(directory,$"fire-pulse-{mode}.png");
            f.Draw(file,sampleReticle:mode==HudMode.Custom);if(file!=null)captures.Add(file);
            if(mode==HudMode.Custom)pulsing=f.ReticlePixels;
            Check(f.Scene.CustomFeedback(f.Player)==pulse&&f.State==state&&f.Scene.FrameCount==frame,
                "actual fire-pulse HUD picture cannot age feedback or advance canonical resources");
        }
        var reduced=HudProfileDefaults.Create("Project Prime");reduced.Mode=HudMode.Custom;
        reduced.ReduceMotion=true;HudProfiles.Publish(reduced);
        string? reducedFile=directory==null?null:Path.Combine(directory,"fire-pulse-Custom-reduced-motion.png");
        f.Draw(reducedFile,sampleReticle:true);if(reducedFile!=null)captures.Add(reducedFile);
        Check(f.Scene.CustomFeedback(f.Player)==pulse&&f.State==state&&f.Scene.FrameCount==frame,
            "reduced-motion picture suppresses presentation without erasing or aging accepted fire feedback");
        Check(pulsing!=null&&f.ReticlePixels!=null&&!pulsing.AsSpan().SequenceEqual(f.ReticlePixels),
            "actual defaultCustom reticle pixels differ when bounded fire pulse is suppressed by reduced motion");
        return new{acceptedShots=f.Runtime.AcceptedShots,frame,pulse.FirePulse,captures};
    }
    private static void RunGl(string? output)
    {
        string? directory=output==null?null:Path.GetFullPath(output);if(directory!=null)Directory.CreateDirectory(directory);
        var settings=DesktopGlContext.Settings(background:true);settings.ClientSize=new(640,480);settings.StartVisible=true;settings.Title="Project Prime custom scope cadence acceptance";
        using var window=new NativeWindow(settings);using var graphics=new DesktopGraphicsSession(window);GLFW.SwapInterval(0);
        string renderer=GL.GetString(StringName.Renderer),version=GL.GetString(StringName.Version);var rows=new List<object>();Tick[]? reference=null;
        foreach(int hz in new[]{60,120,240})
        {
            InputSettings.CustomScopeHold=false;RenderOptions.FieldOfView=78;
            using var f=new Fixture(window);
            f.Press(Keys.Z);f.Release();f.Step(40);
            foreach(int baseFov in new[]{60,78,90,120})
            {
                RenderOptions.FieldOfView=baseFov;f.Draw();
                double projected=2*Math.Atan(1/f.Scene.PerspectiveMatrix.M22)*180/Math.PI;
                double ratio=Math.Tan(baseFov*Math.PI/360)/Math.Tan(projected*Math.PI/360);
                Check(Math.Abs(ratio-2.5)<.001,"actual native renderer perspective matrix retains2.5x lens ratio across all user FOV settings");
            }
            var firstCaptures=CaptureProfiles(f,hz,"25",directory);
            f.Press(Keys.Z);f.Release();f.Step(40);RenderOptions.FieldOfView=78;
            FrameTiming.Reset();FrameTiming.ResetDiagnostics();FrameTiming.FrameRateCap=hz;
            var ticks=new Tick[240];int simulated=0,pictures=0;
            while(simulated<ticks.Length)
            {
                Bound();int due=FrameTiming.Advance(1d/hz);
                for(int step=0;step<due&&simulated<ticks.Length;step++)
                {
                    int tick=simulated;
                    Key(f.Keyboard,Keys.Z,tick is 0 or 170);Key(f.Keyboard,Keys.Enter,tick==20);
                    Key(f.Keyboard,Keys.R,tick==25);Key(f.Keyboard,Keys.O,tick==165);
                    f.Scene.OnSimulationFrame();
                    ticks[simulated++]=new(f.Scene.FrameCount,f.State,f.Scene.CustomWeaponHud(f.Player),f.Player.CameraInfo.Fov,f.Player.ModCustomScopeSensitivity);
                }
                f.Draw();pictures++;
            }
            if(reference==null)reference=ticks;
            else Check(reference.AsSpan().SequenceEqual(ticks),"actual240 fixed state/HUD/FOV samples agree under60/120/240Hz rendered host schedules");
            Check(FrameTiming.DroppedSteps==0&&FrameTiming.Stalls==0&&f.Runtime.AcceptedShots==1
                &&f.State.Magazine==4&&f.State.Reserve==15&&f.State.Optic==2,
                "rendered cadence preserves physical fire count, reload completion and final4x optic");
            foreach(int baseFov in new[]{60,78,90,120})
            {
                RenderOptions.FieldOfView=baseFov;f.Draw();
                double projected=2*Math.Atan(1/f.Scene.PerspectiveMatrix.M22)*180/Math.PI;
                double ratio=Math.Tan(baseFov*Math.PI/360)/Math.Tan(projected*Math.PI/360);
                Check(Math.Abs(ratio-4)<.001,"actual native renderer perspective matrix retains4x lens ratio across all user FOV settings");
            }
            var captures=CaptureProfiles(f,hz,"4",directory);captures.AddRange(firstCaptures);
            rows.Add(new{virtualHostHz=hz,fixedSteps=simulated,pictures,FrameTiming.DroppedSteps,FrameTiming.Stalls,
                acceptedShots=f.Runtime.AcceptedShots,finalFov=f.Player.CameraInfo.Fov,captures});
        }
        var firePulse=CaptureActualFirePulse(window,directory);
        if(directory!=null)File.WriteAllText(Path.Combine(directory,"scope-gl-results.json"),JsonSerializer.Serialize(new{renderer,version,checks=_checks,rows,firePulse},new JsonSerializerOptions{WriteIndented=true}));
    }
}
