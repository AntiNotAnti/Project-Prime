using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual replica actor and ROM-backed viewmodel, with rendering disabled.
internal static class ReplayOwnerSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidDataException(message); _checks++; }
    private static FieldInfo Field(string name) => typeof(PlayerEntity).GetField(name, Hidden)!;
    public static int Run(string profileDirectory, string sourceReplayPath)
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            _checks = 0;
            string source = Path.GetFullPath(sourceReplayPath);
            Directory.SetCurrentDirectory(Path.GetFullPath(profileDirectory));
            Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
            using var replay = new PassiveReplayScene(source, new Vector2i(256, 192));
            PlayerEntity? player = null;
            for (int i = 0; i < 600 && player == null; i++)
            {
                Check(replay.Step(), "replay advances to a live replica viewmodel");
                var main = replay.Scene.Players.Main;
                if (main.Health > 0 && Field("_gunModel").GetValue(main) is ModelInstance) player = main;
            }
            Check(player != null, "source replay supplies a live main player");
            var actor = player!; var instance = (ModelInstance)Field("_gunModel").GetValue(actor)!; var model = instance.Model;
            Check(model.Nodes.Count > 0 && model.Materials.Count > 0, "real assets supply viewmodel nodes and materials");
            var begin = typeof(PlayerEntity).GetMethod("ModBeginOwnerGunDraw", Hidden)!;
            var update = typeof(EntityBase).GetMethod("UpdateTransforms", Hidden, null,
                new[] { typeof(ModelInstance), typeof(Matrix4), typeof(int) }, null)!;
            var originalVisual = actor.ModCaptureOwnerVisual();
            var animation = originalVisual.Animation;
            if (animation.A.Count > 1)
                animation = animation with { A = animation.A with { Frame = (animation.A.Frame + 1) % animation.A.Count } };
            else if (animation.B.Count > 1)
                animation = animation with { B = animation.B with { Frame = (animation.B.Frame + 1) % animation.B.Count } };
            int nodeFrame = animation.Node.Slot == 0 ? animation.A.Frame : animation.B.Frame;
            animation = animation with { From = nodeFrame, To = nodeFrame, Fraction = 0 };
            var alteredVisual = originalVisual with { Alpha = originalVisual.Alpha > .5f ? .25f : .75f,
                Recolor = (byte)((originalVisual.Recolor + 1) % 4), Animation = animation };
            Check(actor.ModSetOwnerVisual(alteredVisual), "real replica accepts compatible owner visual override");
            var nodes = model.Nodes.Select(n => (n.Transform, n.Animation)).ToArray();
            var materials = model.Materials.Select(m => (m.CurrentAlpha, m.CurrentDiffuse, m.CurrentAmbient, m.CurrentSpecular,
                m.CurrentTextureId, m.CurrentPaletteId, m.TextureBindingId, m.RenderMode)).ToArray();
            var palette = model.MatrixStackValues.ToArray(); var canonicalAnimation = ReplayOwnerAnimation.Capture(instance);
            float alpha = (float)Field("_curAlpha").GetValue(actor)!; int recolor = actor.Recolor; int health = actor.Health;
            byte[] authority = replay.State.CaptureCheckpoint().Bytes.ToArray();
            for (int pass = 0; pass < 2; pass++)
            {
                bool injected = false;
                try
                {
                    using var scope = (IDisposable)begin.Invoke(actor, new object[] { Matrix4.CreateTranslation(19, 23, 29) })!;
                    Check((bool)Field("_ownerGunDrawActive").GetValue(actor)! && actor.Recolor == alteredVisual.Recolor
                        && (float)Field("_curAlpha").GetValue(actor)! == alteredVisual.Alpha,
                        "owner scope installs animation/alpha/recolor only during draw");
                    update.Invoke(actor, new object[] { instance, Matrix4.CreateTranslation(19, 23, 29), actor.Recolor });
                    Check(!nodes.SequenceEqual(model.Nodes.Select(n => (n.Transform, n.Animation))),
                        "actual owner draw transform changes real model matrices before restoration");
                    // Exercise every saved dynamic field, including cache values the headless renderer skips.
                    foreach (var material in model.Materials)
                    { material.CurrentAlpha = .123f; material.CurrentDiffuse = Vector3.One * .123f;
                        material.CurrentAmbient = Vector3.One * .234f; material.CurrentSpecular = Vector3.One * .345f;
                        material.CurrentTextureId = 123; material.CurrentPaletteId = 234; material.TextureBindingId = 345;
                        material.RenderMode = material.RenderMode == RenderMode.Normal ? RenderMode.Translucent : RenderMode.Normal; }
                    if (pass == 1) { injected = true; throw new InvalidOperationException("injected owner draw failure"); }
                }
                catch (InvalidOperationException error) when (injected && error.Message == "injected owner draw failure") { }
                Check(nodes.SequenceEqual(model.Nodes.Select(n => (n.Transform, n.Animation))), "scope restores every node transform and animation exactly");
                Check(materials.SequenceEqual(model.Materials.Select(m => (m.CurrentAlpha, m.CurrentDiffuse, m.CurrentAmbient, m.CurrentSpecular,
                    m.CurrentTextureId, m.CurrentPaletteId, m.TextureBindingId, m.RenderMode))), "scope restores all dynamic material and binding values exactly");
                Check(palette.SequenceEqual(model.MatrixStackValues), "scope restores exact pre-draw matrix palette");
                Check(canonicalAnimation == ReplayOwnerAnimation.Capture(instance) && actor.Recolor == recolor
                    && (float)Field("_curAlpha").GetValue(actor)! == alpha && !(bool)Field("_ownerGunDrawActive").GetValue(actor)!,
                    "scope restores canonical animation, recolor, alpha and active flag");
                Check(actor.Health == health && replay.State.CaptureCheckpoint().Bytes.SequenceEqual(authority),
                    "successful and failed owner draws preserve authoritative health and replica checkpoint bytes");
            }
            actor.ModSetOwnerVisual(default);
            Console.WriteLine($"PASS: {_checks} ROM-backed owner draw restoration assertions; headless actual replica, no GL image claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
    }
}
