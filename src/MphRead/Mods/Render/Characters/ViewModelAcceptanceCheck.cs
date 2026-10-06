#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Render.Characters;

/// <summary>Real native gun animation, embedded materials and presentation checks in a bot scene.</summary>
internal static class ViewModelAcceptanceCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static List<RenderItem> Items(Scene scene, string field) =>
        (List<RenderItem>)typeof(Scene).GetField(field, Private)!.GetValue(scene)!;
    internal static int Run(string room, string output, Hunter hunter = Hunter.Samus, bool poseOnly = false,
        bool pbrLitAllStages = false)
    {
        var directory = Path.GetFullPath(output); Directory.CreateDirectory(directory);
        var cases = new List<object>();
        var animations = new HashSet<string>();
        var json = new JsonSerializerOptions { WriteIndented = true };
        GameSettings.Apply(GameState.LoadSettings());
        int oldFov = RenderOptions.FieldOfView, oldCap = FrameTiming.FrameRateCap;
        bool oldAdvanced = RenderOptions.AdvancedMaterials, oldHd = RenderOptions.CharacterModelReplacements;
        bool oldPro = Features.ProHud, oldFixed = Features.FixedCrosshair, oldWeapon = Features.FixedWeapon;
        bool oldBright = RenderOptions.BrightSkins, oldFps = RenderOptions.ShowFps;
        Action? restoreBindings = null;
        Scene? scene = null;
        int submitted = 0, lateFrames = 0, effectFrames = 0; bool fired = false, zoomed = false, muzzleAligned = false;
        int materialOnFrames = 0, materialOffFrames = 0;
        try
        {
            Require(oldHd, "HD character models must be enabled."); DebugLog.Force();
            var settings = DesktopGlContext.Settings(background: true);
            Console.WriteLine("VIEWMODEL ACCEPTANCE GLFW settings ready");
            settings.CurrentMonitor = Monitors.GetPrimaryMonitor().Handle;
            settings.ClientSize = new(1000, 700); settings.StartVisible = true; settings.StartFocused = false;
            settings.Title = "Project Prime Source cannon acceptance";
            Console.WriteLine("VIEWMODEL ACCEPTANCE creating window");
            using var window = new NativeWindow(settings);
            Console.WriteLine("VIEWMODEL ACCEPTANCE window ready");
            using var graphics = new DesktopGraphicsSession(window);
            try
            {
                Features.ProHud = false; Features.FixedCrosshair = true; Features.FixedWeapon = true;
                RenderOptions.BrightSkins = false; RenderOptions.AdvancedMaterials = pbrLitAllStages; RenderOptions.ShowFps = false;
                var keyboard = SyntheticInput.CreateKeyboard();
                var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Private | BindingFlags.Public)!
                    .CreateDelegate<Action<KeyboardState, Keys, bool>>();
                scene = new Scene(window.FramebufferSize, keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
                scene.Random.SetRng1(123456); scene.AddPlayer(hunter); scene.AddPlayer(hunter == Hunter.Kanden ? Hunter.Samus : Hunter.Kanden);
                scene.Players.Items[0].IsBot = false; scene.Players.Items[1].IsBot = true; scene.Players.Items[1].BotLevel = 1;
                scene.AddRoom(room, GameMode.Battle, playerCount: 2); scene.OnLoad(); scene.OnResize();
                scene.GameState.PointGoal = 0; scene.GameState.MatchTime = -1; scene.GameState.SpawnProtection = false;
                scene.GameState.EnhancedHunters = false; scene.GameState.ShadowFreeze = false;
                var target = scene.Players.Main;
                var oldBindings = target.Controls.All.Select(c => (Control:c, c.Type, c.Key, c.MouseButton)).ToArray();
                bool oldMouseAim=target.Controls.MouseAim;
                restoreBindings = () => {
                    foreach (var binding in oldBindings) { binding.Control.Type=binding.Type; binding.Control.Key=binding.Key; binding.Control.MouseButton=binding.MouseButton; }
                    target.Controls.ClearAll(); target.Controls.MouseAim=oldMouseAim;
                };
                var bindings = new (Keybind Control, Keys Key)[] {
                    (target.Controls.Shoot,Keys.F), (target.Controls.MoveUp,Keys.W),
                    (target.Controls.MoveLeft,Keys.A), (target.Controls.MoveRight,Keys.D),
                    (target.Controls.Jump,Keys.Space), (target.Controls.Zoom,Keys.Z) };
                foreach (var control in target.Controls.All) { control.Type=ButtonType.Key; control.Key=Keys.Unknown; }
                foreach (var binding in bindings) { binding.Control.Type=ButtonType.Key; binding.Control.Key=binding.Key; }
                void Press(Keybind key) => setKey(keyboard,key.Key,true);
                void Input()
                {
                    target.Controls.ClearAll(); target.Controls.MouseAim = false;
                    foreach (var binding in bindings) setKey(keyboard,binding.Key,false);
                    object input = typeof(PlayerEntity).GetProperty("Input", Private)!.GetValue(target)!;
                    input.GetType().GetProperty("HasInput")!.SetValue(input, true);
                    target.Health = 999;
                    scene.Players.Items[1].Position = target.Position + new Vector3(8,0,8);
                }
                void Present(string? capture=null)
                {
                    DesktopGraphicsSession.Resize(window); scene.OnDrawFrame(); Require(scene.OnRenderFrame(),"Render stopped.");
                    if (capture != null) Require(ScreenCapture.Save(scene,Path.Combine(directory,capture)),"Capture failed: "+capture);
                    DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                }
                for (int i=0;i<300;i++)
                {
                    // Preserve the normal spawn lifecycle. Setting health
                    // before spawn can bypass initialization; invulnerability
                    // instead prevents the warm-up bot from killing the player.
                    typeof(PlayerEntity).GetField("_spawnInvulnTimer",Private)!
                        .SetValue(target,(ushort)2);
                    scene.OnSimulationFrame(); Present();
                }
                RenderOptions.FieldOfView=78;
                scene.SetFreeCamera(false); Require(target.Health > 0 && target.Hunter == hunter, "Requested main player did not spawn.");
                var gun = (ModelInstance)typeof(PlayerEntity).GetField("_gunModel", Private)!.GetValue(target)!;
                Require(CharacterModelRuntime.TryGetRigid(scene, hunter, CharacterModelPart.ViewModel, gun.Model, out var replacement), "Rigid cannon did not resolve.");
                Require(replacement.Segments.Any(s => s.AlbedoBinding.HasValue), "Expected embedded Source weapon sections.");
                if (hunter == Hunter.Samus) Require(replacement.Segments.Where(s => s.AlbedoBinding.HasValue).Select(s => s.NativeNodeIndex).Distinct().Count() == 8, "Expected eight Samus mechanical cannon sections.");
                Require(replacement.Segments.Where(s => s.AlbedoBinding.HasValue).Select(s => s.AlbedoBinding).Distinct().Count() >= 1, "Cannon uses its atlas or three shared source image sets.");
                Require(replacement.Segments.Where(s => s.AlbedoBinding.HasValue).Select(s => s.MaterialMaps.Normal).Distinct().Count() >= 1
                    && replacement.Segments.Where(s => s.AlbedoBinding.HasValue).All(s => s.MaterialMaps.Normal != 0), "Normal atlas must upload once for every section.");
                var equip = typeof(PlayerEntity).GetMethod("TryEquipWeapon", Private)!;
                void Equip(BeamType beam) { target.EquipInfo.InfiniteAmmo = true; target.AvailableWeapons[beam]=true;
                    Array.Fill((int[])typeof(PlayerEntity).GetField("_ammo",Private)!.GetValue(target)!,999);
                    ((AvailableArray)typeof(PlayerEntity).GetField("_availableCharges",Private)!.GetValue(target)!)[beam]=true; Require((bool)equip.Invoke(target, new object[] {beam, false, false})!, "Weapon equip failed."); }
                void Inspect(string label, bool expectedVisible)
                {
                    var packets = Items(scene,"_nonDecalItems").Concat(Items(scene,"_translucentItems"))
                        .Distinct().Where(p => p.ViewModel && replacement.Segments.Any(s => s.AlbedoBinding.HasValue && s.ListId == p.ListId)).ToArray();
                    int expected = expectedVisible ? replacement.Segments.Count(s => s.AlbedoBinding.HasValue && gun.Model.Nodes[s.NativeNodeIndex].Enabled
                        && gun.Model.NodeParentsEnabled(gun.Model.Nodes[s.NativeNodeIndex])) : 0;
                    Require(packets.Length == expected, $"{label}: {packets.Length}/{expected} replacement sections submitted.");
                    foreach (var segment in replacement.Segments)
                    {
                        var packet = packets.SingleOrDefault(p => p.ListId == segment.ListId); if (packet == null) continue;
                        Require(!packet.WeightedSkinning && packet.Transform == gun.Model.Nodes[segment.NativeNodeIndex].Animation,
                            $"{label}: stale native rigid transform for {segment.NativeNodeIndex}.");
                        if (segment.DoubleSided) Require(packet.CullingMode == CullingMode.Neither, "Source double-sided weapon surface was culled.");
                        Require(packet.HasTexture && packet.TextureBindingId == segment.GetAlbedo(scene,target.Recolor) && packet.TexcoordMatrix == Matrix4.Identity,
                            $"{label}: Source atlas or UV identity lost.");
                        Require(packet.CosmeticMaterial.NormalBinding == (RenderOptions.AdvancedMaterials ? segment.MaterialMaps.Normal : 0)
                            && packet.CosmeticMaterial.SpecularBinding == (RenderOptions.AdvancedMaterials ? segment.MaterialMaps.Specular : 0)
                            && packet.CosmeticMaterial.EmissiveBinding == (RenderOptions.AdvancedMaterials ? segment.MaterialMaps.Emissive : 0),
                            $"{label}: material toggle failed.");
                    }
                    if (expected > 0)
                    {
                        submitted++;
                        if (RenderOptions.AdvancedMaterials) materialOnFrames++; else materialOffFrames++;
                    }
                    if (hunter != Hunter.Samus && label.StartsWith("suit-",StringComparison.Ordinal) && target.Recolor >= 4)
                        Require(replacement.Segments.Any(s=>s.AlbedoBinding.HasValue && s.GetAlbedo(scene,target.Recolor)!=s.AlbedoBinding), "Native team weapon palette did not resolve.");
                }
                var stages = new (string Name, int Count)[] {
                    ("idle",60),("fov-60",60),("fov-120",60),("materials-on",60),("materials-off",60),
                    ("run-strafe",120),("jump",120),("aim-turn",90),("fire",90),("charge",120),
                    ("charge-fov-60",120),("charge-fov-120",120),("missile",120),("missile-charge",120),("imperialist",60),("zoom",90),("unzoom",60),
                    ("affinity",90),("affinity-charge",120),("suit-0",60),("suit-1",60),("suit-2",60),("suit-3",60),("suit-4",60),("suit-5",60) };
                foreach (var stage in poseOnly ? Array.Empty<(string Name,int Count)>() : stages)
                {
                    int before = submitted, beforeEffects = effectFrames;
                    int beforeMaterialsOn = materialOnFrames, beforeMaterialsOff = materialOffFrames;
                    if (stage.Name is "fov-60" or "charge-fov-60") RenderOptions.FieldOfView = 60;
                    if (stage.Name is "fov-120" or "charge-fov-120") RenderOptions.FieldOfView = 120;
                    if (stage.Name == "materials-on") { RenderOptions.FieldOfView = 78; RenderOptions.AdvancedMaterials = true; }
                    if (stage.Name == "materials-off") RenderOptions.AdvancedMaterials = false;
                    if (stage.Name == "missile") { RenderOptions.FieldOfView=78; Equip(BeamType.Missile); }
                    if (stage.Name == "imperialist") Equip(BeamType.Imperialist);
                    if (stage.Name == "affinity") Equip(Weapons.GetAffinityBeam(hunter));
                    // The synthetic zoom key toggles zoom through the normal input path.
                    if (stage.Name == "unzoom") target.UpdateZoom(false);
                    if (stage.Name.StartsWith("suit-",StringComparison.Ordinal))
                    {
                        Equip(BeamType.PowerBeam); target.UpdateZoom(false); target.ModSetAim(Vector3.UnitZ);
                        target.Recolor=int.Parse(stage.Name[5..]); scene.GameState.Teams=false;
                        RenderOptions.FieldOfView=78; RenderOptions.AdvancedMaterials=true;
                    }
                    // Optional material acceptance extends the lit coverage to
                    // every native action. Retain the explicit off stage as a
                    // control; the default diagnostic schedule stays unchanged.
                    if (pbrLitAllStages && stage.Name != "materials-off") RenderOptions.AdvancedMaterials=true;
                    bool stageAdvancedMaterials = RenderOptions.AdvancedMaterials;
                    for (int age=0;age<stage.Count;age++)
                    {
                        Require(RenderOptions.AdvancedMaterials == stageAdvancedMaterials,
                            stage.Name+": material mode changed during the stage.");
                        Input();
                        if (stage.Name == "run-strafe") Press(age<60 ? target.Controls.MoveUp : target.Controls.MoveLeft);
                        if (stage.Name == "jump" && age<12) Press(target.Controls.Jump);
                        if (stage.Name == "aim-turn") target.ModSetAim(new Vector3(MathF.Sin(age*.1f),MathF.Sin(age*.08f),MathF.Cos(age*.1f)).Normalized());
                        if (stage.Name is "fire" or "missile" or "affinity" && age%24<3) Press(target.Controls.Shoot);
                        if (stage.Name == "zoom") Press(target.Controls.Zoom);
                        if (stage.Name is "charge" or "charge-fov-60" or "charge-fov-120" or "missile-charge" or "affinity-charge" && age<95) Press(target.Controls.Shoot);
                        scene.OnSimulationFrame();
                        if (stage.Name.StartsWith("suit-",StringComparison.Ordinal))
                            typeof(PlayerEntity).GetField("_timeSinceDamage",Private)!.SetValue(target,(ushort)255);
                        animations.Add(target.GunAnimation.ToString()); DesktopGraphicsSession.Resize(window); scene.OnDrawFrame();
                        Inspect(stage.Name+"/"+age, true);
                        float authored = (float)typeof(Scene).GetField("_viewModelFov",Private)!.GetValue(scene)!;
                        float world = (float)typeof(Scene).GetField("_cameraFov",Private)!.GetValue(scene)!;
                        Require(MathF.Abs(authored-MathHelper.DegreesToRadians(target.CameraInfo.Fov))<.00001f,
                            "Cannon projection differs from native camera FOV.");
                        Require(MathF.Abs(world-MathHelper.DegreesToRadians(RenderOptions.ScaleCameraFov(target.CameraInfo.Fov)))<.00001f,
                            "World FOV did not apply.");
                        var effectPackets=Items(scene,"_nonDecalItems").Concat(Items(scene,"_translucentItems"))
                            .Distinct().Where(p=>p.ViewModel && p.Type==RenderItemType.Particle).ToArray();
                        if (effectPackets.Length>0) effectFrames++;
                        foreach (var entity in scene.Entities)
                            if (entity is BeamProjectileEntity beam && beam.Owner == target && beam.Lifespan>0) fired=true;
                        zoomed |= target.EquipInfo.Zoomed;
                        Require(scene.OnRenderFrame(),"Render stopped.");
                        if (age == 40 || age == stage.Count-1) Require(ScreenCapture.Save(scene,Path.Combine(directory,stage.Name+$"-{age}.png")),"Capture failed.");
                        DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                    }
                    if (stage.Name.StartsWith("charge",StringComparison.Ordinal)) Require(effectFrames>beforeEffects,"No cannon-projected charge particles observed.");
                    cases.Add(new {stage.Name,stage.Count,submitted=submitted-before,effectFrames=effectFrames-beforeEffects, fov=RenderOptions.FieldOfView,
                        advancedMaterials=stageAdvancedMaterials,materialOnFrames=materialOnFrames-beforeMaterialsOn,materialOffFrames=materialOffFrames-beforeMaterialsOff});
                }
                target.Recolor=0; Equip(BeamType.PowerBeam); target.UpdateZoom(false); target.ModSetAim(Vector3.UnitZ); RenderOptions.FieldOfView = 78;
                for (int i=0;i<60;i++) {
                    Input();
                    // Normal movement input raises a gun lowered by inactivity.
                    // Both captures use the resulting same camera and idle pose.
                    Press(target.Controls.MoveRight);
                    scene.OnSimulationFrame(); Present();
                }
                // Use an explicit reference pose for the geometry comparison.
                // Gameplay animation coverage above remains driven by input.
                typeof(PlayerEntity).GetMethod("SetGunAnimation",Private)!.Invoke(target,
                    new object[] {GunAnimation.Idle,AnimFlags.NoLoop});
                Require(target.GunAnimation == GunAnimation.Idle,"Comparison did not reach native idle.");
                // Compare native and Source in the same live camera without changing persisted settings.
                RenderOptions.CharacterModelReplacements=false; Present("native-fp.png");
                RenderOptions.CharacterModelReplacements=true; Present("source-fp.png");
                Require(target.ModGetFirstPersonGunTransform(out var idleGunTransform), "Idle gun pose unavailable.");
                var inverseIdleGun = idleGunTransform.Inverted();
                File.WriteAllText(Path.Combine(directory,"native-idle-frames.json"),JsonSerializer.Serialize(
                    gun.Model.Nodes.Select(n => {
                        var m=n.Animation*inverseIdleGun;
                        return new { n.Name, matrix=new[] {
                            new[] {m.M11,m.M21,m.M31,m.M41},new[] {m.M12,m.M22,m.M32,m.M42},
                            new[] {m.M13,m.M23,m.M33,m.M43},new[] {m.M14,m.M24,m.M34,m.M44} } };
                    }),json));
                if (poseOnly) { Console.WriteLine("VIEWMODEL NATIVE IDLE POSE EXPORTED " + directory); return 0; }
                foreach (int rate in new[] {90,120,240,540})
                {
                    if (pbrLitAllStages) RenderOptions.AdvancedMaterials=true;
                    bool stageAdvancedMaterials = RenderOptions.AdvancedMaterials;
                    int beforeMaterialsOn = materialOnFrames, beforeMaterialsOff = materialOffFrames;
                    FrameTiming.FrameRateCap=rate>FrameTiming.MaxCap ? FrameTiming.Unlimited : rate; FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); int before=lateFrames;
                    for (int picture=0;picture<rate;picture++)
                    {
                        Require(RenderOptions.AdvancedMaterials == stageAdvancedMaterials,
                            rate+"Hz: material mode changed during the stage.");
                        int steps=FrameTiming.Advance(1.0/rate);
                        for (int step=0;step<steps;step++) { Input(); Press(target.Controls.MoveRight); scene.OnSimulationFrame(); }
                        scene.ModSetLateAim((picture%7-3)*.2f,(picture%5-2)*.1f);
                        DesktopGraphicsSession.Resize(window); scene.OnDrawFrame(); Inspect($"{rate}Hz/{picture}",true);
                        Require(target.ModGetFirstPersonGunTransform(out var transform), "First-person presentation pose unavailable.");
                        Require(target.ModGetFirstPersonEffectTransform(out var effect), "Muzzle presentation pose unavailable.");
                        var muzzle=Vector3.TransformPosition(new Vector3(0,0,Fixed.ToFloat(target.Values.MuzzleOffset)),transform);
                        Require((muzzle-effect.Row3.Xyz).Length<.00001f,"Late-latched muzzle effect detached."); muzzleAligned=true;
                        Require(scene.OnRenderFrame(),"Render stopped.");
                        if (picture==rate-1) Require(ScreenCapture.Save(scene,Path.Combine(directory,$"late-{rate}.png")),"Capture failed.");
                        DesktopGraphicsSession.Present(window); scene.AfterRenderFrame(); lateFrames++;
                    }
                    cases.Add(new {rate,lateFrames=lateFrames-before, simulationSteps=FrameTiming.TotalSteps,
                        advancedMaterials=stageAdvancedMaterials,materialOnFrames=materialOnFrames-beforeMaterialsOn,materialOffFrames=materialOffFrames-beforeMaterialsOff});

                }
                Require(fired && zoomed && muzzleAligned,$"Fire={fired}, zoom={zoomed}, muzzle={muzzleAligned}; required states must occur.");
                Require(animations.Contains("Charging") && animations.Contains("ChargingMissile"),"Charge animations not observed.");
                File.Delete(Path.Combine(directory,"failure.txt"));
                File.WriteAllText(Path.Combine(directory,"acceptance.json"),JsonSerializer.Serialize(new {
                    pass=true, hunter=hunter.ToString(), testedModelSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(replacement.Asset.ModelPath))).ToLowerInvariant(), backend=GraphicsBackendPolicy.Resolved.ToString(), room,submitted,lateFrames,effectFrames,fired,zoomed,muzzleAligned, animations,
                    mobileTextureTier=CharacterModelPack.ForceMobileTierForCheck,textureCompression=ModernGraphicsCompat.PreferredCharacterTextureCompression.ToString(),
                    pbrLitAllStages,materialOnFrames,materialOffFrames,
                    nodes=replacement.Segments.Select(s=>gun.Model.Nodes[s.NativeNodeIndex].Name), cases,
                    scope="Real bot scene and native gun animation. Scripted focus-independent input. Native rigid packet transforms, embedded atlas bindings, material toggles, FOV 60/78/120 and Imperialist zoom. With pbrLitAllStages, all action and high-refresh stages use advanced materials except the explicit materials-off control. Draw scheduling simulated at 90/120/240/540 Hz; not physical display latency measurements. Visual review is separate."
                },json));
                Console.WriteLine($"VIEWMODEL ACCEPTANCE PASS {submitted} frames, {lateFrames} high-refresh pictures: {directory}"); return 0;
            }
            finally { scene?.DoCleanup(); scene?.UnloadGl(); }
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory,"failure.txt"),ex.ToString()); Console.Error.WriteLine("VIEWMODEL ACCEPTANCE FAIL "+ex); return 1; }
        finally
        {
            RenderOptions.FieldOfView=oldFov; RenderOptions.CharacterModelReplacements=oldHd; RenderOptions.AdvancedMaterials=oldAdvanced;
            restoreBindings?.Invoke(); RenderOptions.ShowFps=oldFps;
            RenderOptions.BrightSkins=oldBright; Features.ProHud=oldPro; Features.FixedCrosshair=oldFixed; Features.FixedWeapon=oldWeapon;
            FrameTiming.FrameRateCap=oldCap; FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
        }
    }
}
#endif
