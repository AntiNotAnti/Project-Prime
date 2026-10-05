#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render.Characters;

/// <summary>Real simulation/render acceptance with scene-owned input, unaffected by desktop focus.</summary>
internal static class CharacterAcceptanceCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void Set(object obj, string field, object value) =>
        obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static List<RenderItem> Items(Scene scene, string field) =>
        (List<RenderItem>)typeof(Scene).GetField(field, Private)!.GetValue(scene)!;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static int Run(string room, string output)
    {
        string directory = Path.GetFullPath(output);
        Directory.CreateDirectory(directory);
        bool oldBright = RenderOptions.BrightSkins, oldForce = MapAudit.ForceEveryone;
        bool oldDetail = Features.MaxPlayerDetail;
        var oldStyle = RenderOptions.BrightSkinStyle;
        Scene? scene = null, preview = null;
        var frames = new List<object>();
        var failures = new List<string>();
        var cases = new List<object>();
        try
        {
            GameSettings.Apply(GameState.LoadSettings());
            Require(RenderOptions.CharacterModelReplacements, "HD character models must already be enabled.");
            DebugLog.Force();
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(800, 600);
            settings.StartVisible = true;
            settings.StartFocused = false;
            settings.Title = "Project Prime Samus acceptance";
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            try
            {
            Vector2i size = window.FramebufferSize;
            RenderOptions.ShowFps = false;
            RenderOptions.BrightSkins = false;
            Features.MaxPlayerDetail = true;
            MapAudit.ForceEveryone = true;
            Console.WriteLine($"CHARACTER ACCEPTANCE backend={GraphicsBackendPolicy.Resolved} room={room} "
                + "focus-independent scene input; every simulation step rendered; LOD0 forced");
            preview = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            preview.SideScene = true;
            preview.OnLoad(); preview.OnResize();
            Preview(preview, window, directory, "launcher-before", failures);
            Scene.LauncherPreview = false; Scene.PreviewWanted = false;

            scene = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            scene.Random.SetRng1(123456);
            scene.AddPlayer(Hunter.Kanden);
            scene.AddPlayer(Hunter.Samus);
            scene.Players.Items[0].IsBot = true;
            scene.Players.Items[0].BotLevel = 1;
            scene.Players.Items[1].IsBot = false;
            scene.AddRoom(room, GameMode.Battle, playerCount: 2);
            scene.OnLoad(); scene.OnResize();
            scene.GameState.PointGoal = 0; scene.GameState.MatchTime = -1;
            scene.GameState.SpawnProtection = false;
            scene.GameState.EnhancedHunters = false;
            scene.GameState.ShadowFreeze = false;
            var target = scene.Players.Items[1];
            target.IsBot = false;
            target.Controls.MouseAim = false;
            for (int i = 0; i < 300; i++) Step(scene, window);
            Require(target.Health > 0, "Samus did not spawn.");
            scene.SetFreeCamera(true);

            var stages = new (string Name, int Count)[]
            {
                ("idle",60), ("run",120), ("strafe",120), ("jump-fall-land",180),
                ("aim-turn",120), ("fire",90), ("damage",60), ("freeze",90), ("thaw",60),
                ("morph-move",120), ("unmorph",120), ("bright-skin",90),
                ("team-orange",60), ("team-green",60), ("double-damage",90), ("death-respawn",300)
            };
            bool airborne = false, falling = false, landed = false, alt = false, morphing = false, unmorphing = false;
            bool frozen = false, died = false, respawned = false, fired = false, doubled = false;
            var start = target.Position;
            float runTravel = 0, strafeTravel = 0, altTravel = 0;
            int submittedFrames = 0;
            CharacterWeightedRenderModel? weighted = null;
            int shoulder = -1;
            foreach (var stage in stages)
            {
                int submitted = 0, missing = 0;
                for (int age = 0; age < stage.Count; age++)
                {
                    target.Controls.ClearAll();
                    object input = typeof(PlayerEntity).GetProperty("Input", Private)!.GetValue(target)!;
                    input.GetType().GetProperty("HasInput")!.SetValue(input, true);
                    // Keep ordinary bot damage from killing the subject outside the death case.
                    if (stage.Name != "death-respawn") target.Health = 999;
                    if (age == 0) Configure(stage.Name, scene, target);
                    switch (stage.Name)
                    {
                        case "run": Hold(age < 60 ? target.Controls.MoveUp : target.Controls.MoveDown); break;
                        case "strafe": Hold(age < 60 ? target.Controls.MoveLeft : target.Controls.MoveRight); break;
                        case "jump-fall-land": if (age < 12) Hold(target.Controls.Jump, age == 0); break;
                        case "aim-turn":
                            float yaw = age * MathF.PI / 30;
                            target.ModSetAim(new Vector3(MathF.Sin(yaw), MathF.Sin(age * MathF.PI / 40) * 2, MathF.Cos(yaw)).Normalized());
                            break;
                        case "fire": Hold(target.Controls.Shoot, age % 20 == 0); break;
                        case "morph-move":
                            if (age == 0) Hold(target.Controls.Morph, true);
                            if (age >= 55) Hold(age < 85 ? target.Controls.RollRight : target.Controls.RolltLeft);
                            break;
                        case "unmorph": if (age == 0) Hold(target.Controls.Morph, true); break;
                    }
                    Vector3 previous = target.Position;
                    // Keep the real AI opponent out of the inspection camera. It still
                    // simulates/fires, but cannot stand inside the subject's silhouette.
                    scene.Players.Items[0].Position = target.Position + new Vector3(8,0,8);
                    scene.OnSimulationFrame();
                    if (stage.Name == "run") runTravel += (target.Position - previous).Length;
                    if (stage.Name == "strafe") strafeTravel += (target.Position - previous).Length;
                    if (stage.Name == "jump-fall-land")
                    {
                        if (!target.Flags1.TestFlag(PlayerFlags1.Grounded)) airborne = true;
                        if (!target.Flags1.TestFlag(PlayerFlags1.Grounded) && target.Speed.Y < 0) falling = true;
                        if (airborne && target.Flags1.TestFlag(PlayerFlags1.Grounded)) landed = true;
                    }
                    if (stage.Name == "morph-move")
                    {
                        morphing |= target.IsMorphing; alt |= target.IsAltForm;
                        if (target.IsAltForm) altTravel += (target.Position - previous).Length;
                    }
                    if (stage.Name == "unmorph") unmorphing |= target.IsUnmorphing;
                    if (stage.Name == "freeze") frozen |= target.ModFrozen;
                    if (stage.Name == "fire")
                        foreach (var entity in scene.Entities)
                            if (entity is BeamProjectileEntity beam && beam.Owner == target && beam.Lifespan > 0) fired = true;
                    if (stage.Name == "double-damage") doubled |= target.DoubleDamage;
                    if (stage.Name == "death-respawn")
                    {
                        died |= target.Health == 0;
                        if (died && target.Health > 0) respawned = true;
                    }
                    Follow(scene, target);
                    DesktopGraphicsSession.Resize(window);
                    scene.OnDrawFrame();
                    var packets = Items(scene, "_nonDecalItems").Concat(Items(scene, "_translucentItems"))
                        .Where(item => item.WeightedSkinning).ToArray();
                    bool eligible = target.Health > 0 && !target.IsAltForm
                        && target.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson)
                        && !target.Flags2.TestFlag(PlayerFlags2.HideModel);
                    if (eligible)
                    {
                        if (packets.Length < 4) { missing++; failures.Add($"{stage.Name}/{age}: biped drawn without four Weighted4 packets"); }
                        else
                        {
                            submitted++; submittedFrames++;
                            if (weighted == null)
                            {
                                Require(CharacterModelRuntime.TryGetWeighted(scene, Hunter.Samus, CharacterModelPart.Biped,
                                    target.BipedModel2.Model, out weighted), "Installed Samus weighted model did not resolve.");
                                shoulder = weighted.Joints.ToList().FindIndex(j => target.BipedModel2.Model.Nodes[j.NativeNodeIndex].Name == "L_varias2_SDK");
                                Require(shoulder >= 0, "Shoulder joint missing.");
                            }
                            Matrix4 expected = weighted.Joints[shoulder].InverseBind
                                * target.BipedModel2.Model.Nodes[weighted.Joints[shoulder].NativeNodeIndex].Animation;
                            var actual = new Matrix4(packets[0].MatrixStack[shoulder*16],packets[0].MatrixStack[shoulder*16+1],packets[0].MatrixStack[shoulder*16+2],packets[0].MatrixStack[shoulder*16+3],
                                packets[0].MatrixStack[shoulder*16+4],packets[0].MatrixStack[shoulder*16+5],packets[0].MatrixStack[shoulder*16+6],packets[0].MatrixStack[shoulder*16+7],
                                packets[0].MatrixStack[shoulder*16+8],packets[0].MatrixStack[shoulder*16+9],packets[0].MatrixStack[shoulder*16+10],packets[0].MatrixStack[shoulder*16+11],
                                packets[0].MatrixStack[shoulder*16+12],packets[0].MatrixStack[shoulder*16+13],packets[0].MatrixStack[shoulder*16+14],packets[0].MatrixStack[shoulder*16+15]);
                            if (actual != expected || packets.Any(p => p.MatrixStack.Take(p.MatrixStackCount*16).Any(v => !float.IsFinite(v))))
                                failures.Add($"{stage.Name}/{age}: stale or invalid shoulder palette");
                        }
                    }
                    Require(scene.OnRenderFrame(), "Render stopped.");
                    if (age == 0 || age == 15 || age == 40 || age == stage.Count-1)
                        ScreenCapture.Save(scene, Path.Combine(directory, $"{stage.Name}-{age:D3}.png"));
                    frames.Add(new { stage = stage.Name, age, frame = scene.FrameCount, health = target.Health,
                        alt = target.IsAltForm, morph = target.IsMorphing, unmorph = target.IsUnmorphing,
                        frozen = target.ModFrozen, doubleDamage = target.DoubleDamage,
                        legAnimation = typeof(PlayerEntity).GetProperty("Biped1Anim", Private)!.GetValue(target)!.ToString(),
                        torsoAnimation = typeof(PlayerEntity).GetProperty("Biped2Anim", Private)!.GetValue(target)!.ToString(),
                        weightedPackets = packets.Length, eligible, x=target.Position.X,y=target.Position.Y,z=target.Position.Z });
                    DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                }
                cases.Add(new { stage = stage.Name, frames = stage.Count, weightedFrames = submitted, missingWeightedFrames = missing });
                Console.WriteLine($"CHARACTER ACCEPTANCE {stage.Name}: {submitted} weighted frames, {missing} missing");
            }
            Require(runTravel > .5f && strafeTravel > .5f, "Run/strafe controls did not move Samus.");
            Require(airborne && falling && landed, "Jump/fall/land not observed.");
            Require(fired && frozen && doubled, "Fire/freeze/double-damage states not observed.");
            Require(morphing && alt && unmorphing && !target.IsAltForm && altTravel > .2f, "Morph/move/unmorph incomplete.");
            Require(died && respawned, "Death/respawn incomplete.");
            scene.DoCleanup(); scene.UnloadGl(); scene = null;
            Preview(preview, window, directory, "launcher-after", failures);
            File.WriteAllText(Path.Combine(directory, "acceptance.json"), JsonSerializer.Serialize(new
            {
                backend = GraphicsBackendPolicy.Resolved.ToString(), room, submittedFrames, runTravel, strafeTravel,
                airborne, falling, landed, fired, frozen, doubled, morphing, alt, altTravel, unmorphing, died, respawned,
                cases, failures, scope = "Real scene simulation/render with one AI bot and scripted Samus controls. Damage, freeze and double damage are injected. LOD0 forced. Visual capture review is separate from packet assertions."
            }, Json));
            File.WriteAllText(Path.Combine(directory, "frames.json"), JsonSerializer.Serialize(frames, Json));
            Console.WriteLine($"CHARACTER ACCEPTANCE {(failures.Count == 0 ? "PASS" : "FAIL")} {directory}");
            return failures.Count == 0 ? 0 : 1;
            }
            finally
            {
                scene?.DoCleanup(); scene?.UnloadGl();
                preview?.UnloadGl();
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames,Json));
            Console.Error.WriteLine("CHARACTER ACCEPTANCE FAIL " + error); return 1;
        }
        finally
        {
            // Scene cleanup must happen while the graphics context is alive; normal path above does so.
            RenderOptions.BrightSkins = oldBright; RenderOptions.BrightSkinStyle = oldStyle;
            Features.MaxPlayerDetail = oldDetail; MapAudit.ForceEveryone = oldForce;
        }
    }

    private static void Hold(Keybind key, bool press = false) { key.IsDown = true; key.IsPressed = press; }
    private static void Configure(string name, Scene scene, PlayerEntity target)
    {
        switch (name)
        {
            case "damage": target.TakeDamage(10, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, scene.Players.Items[0]); break;
            case "freeze": target.ModSetFrozen(true); break;
            case "thaw": target.ModSetFrozen(false); break;
            case "bright-skin": RenderOptions.BrightSkins = true; RenderOptions.BrightSkinStyle = PlayerSkinStyle.Textured; break;
            case "team-orange": case "team-green":
                scene.GameState.Teams = true; target.TeamIndex = name == "team-orange" ? 0 : 1; TeamVisuals.Apply(target); break;
            case "double-damage": Set(target, "_doubleDmgTimer", (ushort)180); break;
            case "death-respawn": target.TakeDamage(10000, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, scene.Players.Items[0]); break;
        }
    }
    private static void Follow(Scene scene, PlayerEntity target)
    {
        Vector3 center = target.Position + new Vector3(0,.8f,0);
        Vector3 facing = new Vector3(-2,-1,-4).Normalized();
        Set(scene,"_cameraPosition",center + new Vector3(2,1,4));
        Set(scene,"_cameraFacing",facing); Set(scene,"_cameraUp",Vector3.UnitY);
        Set(scene,"_cameraFov",MathHelper.DegreesToRadians(55));
    }
    private static void Step(Scene scene, NativeWindow window)
    {
        scene.OnSimulationFrame(); DesktopGraphicsSession.Resize(window);
        scene.OnDrawFrame(); scene.OnRenderFrame(); DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
    }
    private static void Preview(Scene scene, NativeWindow window, string directory, string label, List<string> failures)
    {
        Scene.LauncherPreview = true; Scene.LauncherHunter = Hunter.Samus; Scene.LauncherSuit = 0;
        Scene.PreviewWanted = true; Scene.PreviewLeft = .1f; Scene.PreviewTop = .1f;
        Scene.PreviewRight = .9f; Scene.PreviewBottom = .9f;
        int drawn = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            // Advance explicitly; do not let accelerated diagnostics depend on wall-clock preview pacing.
            scene.ModStepPreview();
            if (scene.ModDrawPreviewAlone(window.FramebufferSize)) drawn++;
        }
        if (drawn < 60) failures.Add($"{label}: {drawn}/60 launcher preview frames drawn");
        ScreenCapture.SaveWindow(scene,Path.Combine(directory,label+".png"));
        Console.WriteLine($"CHARACTER ACCEPTANCE {label}: {drawn}/60 drawn");
    }
}
#endif
