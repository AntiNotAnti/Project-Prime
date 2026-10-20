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

// Real public offline pickup and native GL submission. The declared setup places
// the actor at an authored native spawn and uses a declared free-camera vantage at the item.
// Collection then uses synthetic W input and the ordinary player item phase.
// This is procedural-proxy acceptance, not final rifle art or a device test.
internal static class StockPickupGlChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
        .CreateDelegate<Action<KeyboardState, Keys, bool>>();

    private static readonly Action<MouseState,Vector2> MousePosition=typeof(MouseState)
        .GetProperty("Position",Hidden|BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState,Vector2>>();

    internal static int Run(string data, string outdir,bool wheel=false,bool acquireWhileHeld=false)
    {
        string cwd = Directory.GetCurrentDirectory();
        bool force = MapAudit.ForceEveryone, script = NetTestScript.Enabled;
        bool focused = GamepadContexts.Focused, menu = GamepadContexts.MenuVisible;
        bool capturing = GamepadContexts.Capturing, text = GamepadContexts.TextEntryActive;
        bool stylus = PointerInput.StylusMode;
        var oldWheelBind=PadBindings.Get(PadAction.WeaponWheel);bool oldSouthpaw=GamepadOptions.Southpaw;
        float oldThreshold=GamepadOptions.WheelThreshold;int[] oldOrder=(int[])GamepadOptions.WheelOrder.Clone();
        bool oldZoneWanted=StylusZone.Wanted;float oldZoneLeft=StylusZone.Left,oldZoneTop=StylusZone.Top,oldZoneWidth=StylusZone.Width;
        Scene? scene = null;
        NativeWindow? window = null;
        DesktopGraphicsSession? graphics = null;
        int checks = 0;
        var watch = Stopwatch.StartNew();
        var captures = new List<object>();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++; Console.WriteLine("PICKUP GL PASS " + label);
        }
        void Bound()
        {
            if (watch.Elapsed.TotalSeconds > 120) throw new TimeoutException("Pickup GL fixture exceeded120seconds");
        }
        try
        {
            data = Path.GetFullPath(data); outdir = Path.GetFullPath(outdir);
            Directory.CreateDirectory(outdir);
            Directory.SetCurrentDirectory(data); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Check(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive,
                "fresh process owns ordinary offline GL without entering Headless");
            MapAudit.ForceEveryone = true; NetTestScript.Enabled = false;
            GamepadManager.ClearAll(); GamepadContexts.Focused = true;
            GamepadContexts.MenuVisible = GamepadContexts.Capturing = GamepadContexts.TextEntryActive = false;
            PointerInput.StylusMode = false; PointerDevice.Reset();

            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(960, 720); settings.StartVisible = true;
            settings.Title = "Project Prime public world pickup proxy acceptance";
            window = new NativeWindow(settings); graphics = new DesktopGraphicsSession(window);
            GLFW.SwapInterval(0);
            string renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version);
            var keyboard = SyntheticInput.CreateKeyboard();var mouse=SyntheticInput.CreateMouse();
            scene = new(window.FramebufferSize, keyboard, mouse, _ => { }, () => { });
            scene.AddPlayer(Hunter.Samus);
            scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 1, nodeLayerMask: 0);
            scene.OnLoad(); scene.OnResize();
            var mode = typeof(Scene).GetField("_inputMode", Hidden)!;
            mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            var player = scene.Players.Main; player.IsBot = false;
            foreach (var button in player.Controls.All) { button.Type = ButtonType.Key; button.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.MoveUp.Key = Keys.W;
            player.Controls.MouseAim = player.Controls.KeyboardAim = false;
            player.Controls.WeaponMenu.Key=Keys.Tab;player.Controls.PowerBeam.Key=Keys.P;player.Controls.CustomWeapon.Key=Keys.C;
            var runtime = scene.EnableOfflineRifleAvailability(true);
            var ledger = runtime.Pickups ?? throw new InvalidOperationException("Public runtime has no pickup ledger.");
            void Step(int count = 1)
            { for (int i = 0; i < count; i++) { Bound(); scene.OnSimulationFrame(); } }
            Key(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) Step();
            Key(keyboard, Keys.Enter, false); scene.CameraSequences.Current?.End(); Step(180);
            scene.CameraSequences.Current?.End();
            Check(player.Health > 0 && player.ModIsInPlay && scene.CameraMode == CameraMode.Player,
                "actual native player spawns into the first-person renderer");
            Check(ledger.Layout.Count == 1 && ledger.Revision == 0 && !runtime.TryPeek(player, out _, out _),
                "public activation starts empty with one unconsumed world station");

            // Same extracted native approach as StockPickupSceneChecks; no item,
            // custom ownership, clock, funding or resource state is injected.
            PlayerSpawnEntity? spawn = null;
            foreach (var item in scene.GetPlayerSpawnEntities()) if (item.Id == 4) spawn = item;
            if (spawn == null) throw new InvalidOperationException("Authored native approach spawn4 missing.");
            var start = spawn.Position;
            CollisionResult floor = default;
            Check(CollisionDetection.CheckBetweenPoints(start + Vector3.UnitY * 2, start - Vector3.UnitY * 6,
                TestFlags.Players, scene, ref floor), "native approach has extracted world floor collision");
            start.Y = floor.Position.Y;
            var center = ledger.Layout.Placements[0].Position;
            var walk = (center - start).WithY(0).Normalized();
            player.Reposition(start.AddY(.5f), walk, scene.Room!.GetNodeRefByPosition(start));
            player.ModSetAim(walk); player.Speed = Vector3.Zero; Step(15);
            // Presentation-only camera seam: aim setup does not alter the actor,
            // contacts, resources or simulation clock. Restore Player mode before walking.
            scene.SetFreeCamera(true);
            var camera = center + new Vector3(2,1.5f,-3);
            var facing = (center-camera).Normalized();
            var right = Vector3.Cross(facing,Vector3.UnitY).Normalized();
            void CameraField(string name,Vector3 value)=>typeof(Scene).GetField(name,Hidden)!.SetValue(scene,value);
            CameraField("_cameraPosition",camera);CameraField("_cameraFacing",facing);
            CameraField("_cameraRight",right);CameraField("_cameraUp",Vector3.Cross(right,facing));
            Check(ledger.TryState(0, out var available) && available.Available && ledger.Revision == 0
                && !runtime.TryPeek(player, out _, out _) && (player.Position - center).WithY(0).Length > 2,
                "camera setup remains outside pickup contact with no custom ownership");

            var used = typeof(Scene).GetField("_usedRenderItems", Hidden)!;
            int ProxyBoxes()
            {
                int count = 0;
                foreach (var item in (Queue<RenderItem>)used.GetValue(scene)!)
                {
                    if (item.Type != RenderItemType.Box || item.ItemCount != 8) continue;
                    if (item.OverrideColor == new Vector4(.12f, .8f, .95f, 1)
                        || item.OverrideColor == new Vector4(.45f, .65f, .75f, 1)
                        || item.OverrideColor == new Vector4(.4f, 1, .9f, 1)) count++;
                }
                return count;
            }
            void Draw(string? name, int expectedBoxes)
            {
                Bound();
                ulong frame = scene.FrameCount, revision = ledger.Revision, shots = runtime.AcceptedShots;
                uint world = ledger.AuthorityFrame;
                int health = player.Health; var position = player.Position; var ammo = player.ModAmmo;
                bool owned = runtime.TryPeek(player, out var state, out bool selected);
                ledger.TryState(0, out var itemBefore);
                scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "actual world renderer produces a picture");
                Check(ProxyBoxes() == expectedBoxes, "renderer owns the expected procedural pickup box submissions");
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "native pickup world draw has no GL error");
                if (name != null)
                {
                    string composite = Path.Combine(outdir, name + "-window.png");
                    string worldFile = Path.Combine(outdir, name + "-world.png");
                    Check(ScreenCapture.SaveWindow(scene, composite) && ScreenCapture.Save(scene, worldFile),
                        "real pre-swap composite and scene-target PNGs captured");
                    captures.Add(new { name, composite, compositeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(composite))).ToLowerInvariant(),
                        worldFile, worldSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(worldFile))).ToLowerInvariant(),
                        frame, camera = scene.CameraPosition.ToString(), center = center.ToString(), expectedBoxes });
                }
                DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                bool ownedAfter = runtime.TryPeek(player, out var after, out bool selectedAfter);
                ledger.TryState(0, out var itemAfter);
                Check(scene.FrameCount == frame && ledger.AuthorityFrame == world && ledger.Revision == revision
                    && itemAfter == itemBefore && ledger.PendingOutcomes == 0 && runtime.AcceptedShots == shots
                    && player.Health == health && player.Position == position && player.ModAmmo == ammo
                    && ownedAfter == owned && after == state && selectedAfter == selected,
                    "draw-only picture cannot advance contact, resources, ownership, actor or simulation");
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "capture/present finishes without a GL error");
            }
            if(wheel)
            {
                scene.SetFreeCamera(false);Key(keyboard,Keys.Tab,true);Step(2);
                Check(player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)&&player.ModCustomWheelCount==0
                    &&!runtime.TryPeek(player,out _,out _),"unowned public life retains native six-entry wheel without custom allocation or grant");
                Draw("wheel-unowned-native-six",3);
                Key(keyboard,Keys.Tab,false);Step(2);scene.SetFreeCamera(true);
                CameraField("_cameraPosition",camera);CameraField("_cameraFacing",facing);
                CameraField("_cameraRight",right);CameraField("_cameraUp",Vector3.Cross(right,facing));
            }
            Draw("before-contact", 3);
            // Repeated pictures exercise Scene's pooled point-array recycling
            // without stepping the item cooldown or simulating a pickup.
            for (int i = 0; i < 12; i++) Draw(null, 3);
            int healthBefore = player.Health;
            scene.SetFreeCamera(false);
            if(acquireWhileHeld)
            {
                Key(keyboard,Keys.Tab,true);Step(2);
                Check(player.ModCustomWheelCount==0&&ledger.Revision==0,"native wheel hold begins before actual acquisition");
            }
            player.ModSetAim(walk); Key(keyboard, Keys.W, true);
            int walkingSteps = 0;
            for (; walkingSteps < 120 && ledger.Revision == 0; walkingSteps++) Step();
            Key(keyboard, Keys.W, false);
            Check(walkingSteps > 0 && walkingSteps < 120 && ledger.Revision == 1 && player.Health == healthBefore,
                "actual W input and native item contact consume the station exactly once");
            Step(2);
            if(acquireWhileHeld)
            {
                Check(runtime.TryPeek(player,out _,out _)&&player.ModCustomWheelCount==0,
                    "actual mid-hold pickup preserves the existing native targets until release");
                Draw("wheel-acquired-during-native-hold",0);
                Key(keyboard,Keys.Tab,false);Step(2);
                Check(runtime.TryPeek(player,out _,out bool heldReturn)&&!heldReturn,
                    "the existing native hold still releases through its native selection path after acquisition");
                Key(keyboard,Keys.C,true);Step();Key(keyboard,Keys.C,false);Step(2);
            }
            Check(runtime.TryPeek(player, out var funded, out bool equipped) && equipped
                && funded.Magazine == 4 && funded.Reserve == 16 && runtime.AcceptedShots == 0,
                "next real input phase owns and auto-selects four/sixteen without firing");
            Draw("after-contact", 0);
            if(wheel)
            {
                Key(keyboard,Keys.Tab,true);Step(2);
                Check(player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)&&player.ModCustomWheelCount==8
                    &&player.ModCustomWheelSelection==6,"actual acquired gameplay wheel has six native entries, tagged M7 and native return");
                Draw("wheel-acquired-m7",0);
                var firstBounds=player.ModCustomWheelPixelBounds(6);
                Check(firstBounds.Width>0&&firstBounds.Height>0&&firstBounds.Contains(firstBounds.Left+firstBounds.Width/2,firstBounds.Top+firstBounds.Height/2),
                    "drawn acquired M7 box exposes the shared absolute pixel bounds");
                MousePosition(mouse,mouse.Position+new Vector2(0,scene.Size.Y/10f*1.1f));Step();
                Check(player.ModCustomWheelSelection==7,"actual native mouse delta drags from M7 to its native return entry");
                Draw("wheel-native-return",0);
                Key(keyboard,Keys.Tab,false);Key(keyboard,Keys.Enter,true);Step();
                Check(runtime.TryPeek(player,out var afterReturn,out bool selectedReturn)&&!selectedReturn
                    &&player.CurrentWeapon==BeamType.PowerBeam&&afterReturn.Magazine==4&&afterReturn.Reserve==16&&runtime.AcceptedShots==0,
                    "actual wheel release unequips to the same native fallback and suppresses simultaneous custom Fire");
                int nativeFlights=0;foreach(var beam in scene.GetBeamProjectileEntities())
                    if(beam.Owner==player&&beam.Lifespan>0&&beam.ModCustomShot==null)nativeFlights++;
                Check(nativeFlights==0,"wheel return cannot also emit a native fallback shot in the same sample");
                Key(keyboard,Keys.Enter,false);Step(2);
                Key(keyboard,Keys.Tab,true);Step(2);
                Check(player.ModCustomWheelCount==8&&player.ModCustomWheelSelection==7,"next hold anchors the actual held native return");
                MousePosition(mouse,mouse.Position-new Vector2(0,scene.Size.Y/10f*1.1f));Step();
                Check(player.ModCustomWheelSelection==6,"actual reverse mouse drag reaches acquired M7");
                Key(keyboard,Keys.Tab,false);Step(2);
                Check(runtime.TryPeek(player,out var afterEquip,out bool selectedEquip)&&selectedEquip
                    &&afterEquip.Magazine==4&&afterEquip.Reserve==16&&runtime.AcceptedShots==0,
                    "ordinary mouse wheel release equips acquired M7 without granting, refilling or firing");
                Draw("wheel-returned-m7",0);
                // Actual production gamepad polling/actions from a named
                // synthetic device; authority/state resources remain untouched.
                PadBindings.Set(PadAction.WeaponWheel,GamepadButtons.RightThumb);
                GamepadOptions.Southpaw=false;GamepadOptions.WheelThreshold=.45f;
                Array.Copy(new[]{5,3,1,4,2,0},GamepadOptions.WheelOrder,6);
                void Pad(GamepadButtons buttons,float x=0,float y=0)
                {GamepadManager.UpdateDevice("cw-wheel-gl",new MphRead.Mods.Input.GamepadState{Buttons=buttons,RightX=x,RightY=y,Name="Synthetic wheel acceptance"},mapped:true);}
                Pad(GamepadButtons.None);GamepadManager.SelectDevice("cw-wheel-gl");Step(2);
                Pad(GamepadButtons.RightThumb);Step(2);
                Check(GamepadInput.WheelHeld&&player.ModCustomWheelCount==8&&player.ModCustomWheelSelection==-1,
                    "actual synthetic controller hold preserves neutral-stick cancellation");
                var heldModel=(CustomWeaponWheel)typeof(PlayerEntity).GetField("_customWheel",Hidden)!.GetValue(player)!;
                Check(heldModel[0]==new CustomCycleTarget(false,BeamType.ShockCoil)&&GamepadOptions.WheelOrder.Length==6,
                    "acquired controller layout copies the saved six-entry permutation before appending M7");
                Array.Reverse(GamepadOptions.WheelOrder);
                Check(heldModel[0].Native==BeamType.ShockCoil,"changing settings cannot move frozen targets during a wheel hold");
                Draw("wheel-pad-neutral",0);
                Pad(GamepadButtons.None);Step(2);
                Check(runtime.TryPeek(player,out _,out bool afterNeutral)&&afterNeutral&&!player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen),
                    "neutral controller release cancels and retains M7 rather than selecting native fallback");
                GamepadManager.ClearAll();Step(2);Key(keyboard,Keys.P,true);Step();Key(keyboard,Keys.P,false);Step(2);
                Check(runtime.TryPeek(player,out _,out bool nativeForPad)&&!nativeForPad,"ordinary binding prepares actual native fallback for controller M7 selection");
                Pad(GamepadButtons.None);GamepadManager.SelectDevice("cw-wheel-gl");Step(2);
                Pad(GamepadButtons.RightThumb);Step(2);
                float angle=6.5f*MathF.Tau/player.ModCustomWheelCount;
                Pad(GamepadButtons.RightThumb,MathF.Sin(angle),MathF.Cos(angle));Step(2);
                Check(player.ModCustomWheelSelection==6,"actual synthetic controller stick resolves the drawn tagged M7 entry");
                Draw("wheel-pad-m7",0);
                Pad(GamepadButtons.None);Step(2);
                Check(runtime.TryPeek(player,out _,out bool fromPad)&&fromPad&&runtime.AcceptedShots==0,
                    "actual controller release equips acquired M7 through ordinary custom input");
                GamepadManager.ClearAll();Step(2);Key(keyboard,Keys.P,true);Step();Key(keyboard,Keys.P,false);Step(2);
                PointerInput.StylusMode=true;StylusZone.Enabled=true;PointerDevice.Reset();
                StylusZone.AspectCorrection=scene.Size.X/(float)scene.Size.Y;StylusZone.SetRect(.2f,.2f,.6f);
                void Pen(float x,float y,bool down)
                {PointerDevice.Update(new(PointerDeviceType.Pen,1,x,y,down,down,true,down?1:0),scene.Size.X,scene.Size.Y,acceptsInput:true);}
                float selX=(StylusZone.Left+232/256f*StylusZone.Width)*scene.Size.X;
                float selY=(StylusZone.Top+43/192f*StylusZone.Height)*scene.Size.Y;
                Pen(selX,selY,false);Step(2);Pen(selX,selY,true);Step(2);
                Check(player.ModCustomWheelCount==8&&player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen),
                    "actual synthetic stylus SEL contact opens acquired wheel within its zone");
                var penBounds=player.ModCustomWheelPixelBounds(6);
                float penX=penBounds.Left+penBounds.Width/2,penY=penBounds.Top+penBounds.Height/2;
                Pen(penX,penY,true);Step(2);
                Check(player.ModCustomWheelSelection==6&&penBounds.Contains(penX,penY),
                    "actual absolute stylus pointer selects the same M7 rectangle drawn by the HUD");
                Draw("wheel-stylus-m7",0);
                Pen(penX,penY,false);Step();Pen(penX,penY,false);Step(2);
                Check(runtime.TryPeek(player,out _,out bool fromPen)&&fromPen&&!player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen),
                    "debounced actual pen release equips M7 through canonical input without granting resources");
                PointerInput.StylusMode=false;PointerDevice.Reset();Step(2);
                Key(keyboard,Keys.P,true);Step();Key(keyboard,Keys.P,false);Step(2);
                Key(keyboard,Keys.Tab,true);Step(2);
                MousePosition(mouse,mouse.Position-new Vector2(0,scene.Size.Y/10f*1.1f));Step();
                Check(player.ModCustomWheelSelection==6,"dry-release fixture first highlights an eligible acquired M7");
                // Declared canonical-state transition seam: model the authority
                // exhausting ammo after the HUD sample but before release.
                var canonical=runtime.State(player)!;
                typeof(WeaponRuntimeState).GetField("_magazine",Hidden)!.SetValue(canonical,0);
                typeof(WeaponRuntimeState).GetField("_reserve",Hidden)!.SetValue(canonical,0);
                Key(keyboard,Keys.Tab,false);Step(2);
                Check(runtime.TryPeek(player,out var emptyAfter,out bool dryEquipped)&&!dryEquipped
                    &&emptyAfter.Magazine==0&&emptyAfter.Reserve==0&&runtime.AcceptedShots==0,
                    "release refreshes current typed ammo and refuses a formerly highlighted dry M7");
            }
            File.WriteAllText(Path.Combine(outdir, "pickup-gl-results.json"), JsonSerializer.Serialize(new {
                renderer, version, checks, seconds = watch.Elapsed.TotalSeconds, walkingSteps, captures,wheel,acquireWhileHeld,
                scope = "Actual offline public pickup/native GL; synthetic keyboard, declared native-spawn setup and presentation-only free-camera vantage. Procedural proxy only; no final CW06 art, Android or physical-device qualification."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS {checks} public world pickup GL checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("PICKUP GL FAIL " + error); return 1; }
        finally
        {
            scene?.DoCleanup(); graphics?.Dispose(); window?.Dispose();
            MapAudit.ForceEveryone = force; NetTestScript.Enabled = script;
            GamepadContexts.Focused = focused; GamepadContexts.MenuVisible = menu;
            GamepadContexts.Capturing = capturing; GamepadContexts.TextEntryActive = text;
            PointerInput.StylusMode = stylus; PointerDevice.Reset(); GamepadManager.ClearAll();
            PadBindings.Set(PadAction.WeaponWheel,oldWheelBind);GamepadOptions.Southpaw=oldSouthpaw;
            GamepadOptions.WheelThreshold=oldThreshold;Array.Copy(oldOrder,GamepadOptions.WheelOrder,6);
            StylusZone.Enabled=oldZoneWanted;StylusZone.SetRect(oldZoneLeft,oldZoneTop,oldZoneWidth);
            Directory.SetCurrentDirectory(cwd);
        }
    }
}
