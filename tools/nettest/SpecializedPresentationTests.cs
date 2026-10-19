using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Actual Kanden chain selectors with completed, actor-local matrices and no assets or GL.</summary>
internal static class SpecializedPresentationTests
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo Frame = typeof(Scene).GetField("_frameCount", Hidden)!;
    private static readonly FieldInfo Segments = typeof(PlayerEntity).GetField("_kandenSegMtx", Hidden)!;
    private static readonly PropertyInfo Flags = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!;
    private static readonly PropertyInfo HunterProperty = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Hunter))!;
    private static int _checks, _failures;
    private static void Check(bool condition, string label)
    {
        _checks++; if (!condition) _failures++;
        Console.WriteLine($"[specialized-presentation] {(condition ? "PASS" : "FAIL")} {label}");
    }
    private static bool Near(Vector3 a, Vector3 b) => (a - b).Length < .0001f;
    private static void Tick(Scene scene, ulong frame) => Frame.SetValue(scene, frame);
    private static Matrix4[] Matrices(PlayerEntity actor) => (Matrix4[])Segments.GetValue(actor)!;
    private static Vector3 Local(int index, bool second) => new(second ? .4f : 0, index * .1f, -index * .3f + (second ? .2f : 0));
    private static void Pose(PlayerEntity actor, float x, bool second)
    {
        actor.Position = new Vector3(x, 0, 0);
        Matrix4[] segments = Matrices(actor);
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = Matrix4.CreateRotationY(second ? MathF.PI / 2 : 0);
            segments[i].Row3.Xyz = actor.Position + Local(i, second);
        }
    }
    private static PlayerEntity Pair(Scene scene, int slot = 1)
    {
        var actor = scene.Players.Items[slot];
        HunterProperty.SetValue(actor, Hunter.Kanden);
        actor.Health = 100; actor.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
        Flags.SetValue(actor, PlayerFlags1.AltForm);
        typeof(PlayerEntity).GetField("_frozenTimer", Hidden)!.SetValue(actor, (ushort)0);
        Tick(scene, 0); Pose(actor, 0, false); actor.ModResetDrawState(); actor.ModCaptureDrawState();
        Tick(scene, 1); Pose(actor, 2, true); actor.ModCaptureDrawState(); Tick(scene, 2);
        return actor;
    }
    private static void Current(PlayerEntity actor, string label)
    {
        var canonical = Matrices(actor).ToArray(); var transform = actor.Transform;
        for (int i = 0; i < canonical.Length; i++)
        {
            Matrix4 actual = actor.ModGetKandenSegmentDrawPose(i, actor.Position, out bool sampled);
            Check(!sampled && Near(actual.Row3.Xyz, canonical[i].Row3.Xyz)
                && Near(actual.Row2.Xyz, canonical[i].Row2.Xyz), label + $" segment {i}");
        }
        Check(canonical.SequenceEqual(Matrices(actor)) && transform == actor.Transform,
            "canonical fallback leaves player transform and chain matrices unchanged");
    }
    private static void Sampling(Scene scene)
    {
        foreach (int slot in new[] { 1, 0 })
        {
            var actor = Pair(scene, slot);
            var canonical = Matrices(actor).ToArray(); var position = actor.Position; var transform = actor.Transform;
            uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
            actor.ModGetDrawActorPose(out Vector3 drawPosition, out _);
            Check(Near(drawPosition, slot == 0 ? new Vector3(2, 0, 0) : Vector3.UnitX),
                "specialized animation preserves the existing local/current or other-actor/interpolated base-position policy");
            for (int repeat = 0; repeat < 5; repeat++)
            for (int i = 0; i < canonical.Length; i++)
            {
                Matrix4 actual = actor.ModGetKandenSegmentDrawPose(i, drawPosition, out bool sampled);
                Vector3 expectedLocal = Vector3.Lerp(Local(i, false), Local(i, true), .5f);
                Check(sampled && Near(actual.Row3.Xyz, drawPosition + expectedLocal),
                    $"actor {slot} segment {i} samples relative chain translation at the same display fraction");
                Check(Near(actual.Row2.Xyz, Matrix4.CreateRotationY(MathF.PI / 4).Row2.Xyz)
                    && MathF.Abs(actual.Row2.Xyz.Length - 1) < .0001f,
                    $"actor {slot} segment {i} samples orthogonal shortest-arc chain orientation");
            }
            Check(canonical.SequenceEqual(Matrices(actor)) && actor.Position == position && actor.Transform == transform
                && scene.Random.Rng1 == rng1 && scene.Random.Rng2 == rng2,
                "repeated specialized samples leave simulation chain matrices, actor pose and both RNG streams exact");
        }
        var sixty = Pair(scene); FrameTiming.FrameRateCap = 60;
        Current(sixty, "60 Hz retains canonical specialized animation");
        FrameTiming.FrameRateCap = 120;
    }
    private static void Boundaries(Scene scene)
    {
        foreach (var item in new (string Label, Action<PlayerEntity> Edit)[]
        {
            ("form boundary", actor => Flags.SetValue(actor, actor.Flags1 | PlayerFlags1.Morphing)),
            ("hunter boundary", actor => HunterProperty.SetValue(actor, Hunter.Spire)),
            ("death boundary", actor => actor.Health = 0),
            ("despawn boundary", actor => actor.LoadFlags = LoadFlags.Active),
            ("frozen canonical policy", actor => typeof(PlayerEntity).GetField("_frozenTimer", Hidden)!.SetValue(actor, (ushort)1)),
            ("uncaptured actor translation", actor => actor.Position += Vector3.UnitX * .01f)
        })
        {
            var actor = Pair(scene); item.Edit(actor); Current(actor, item.Label + " rejects specialized carry immediately");
        }
        var edited = Pair(scene); Matrices(edited)[2].Row3.Xyz += Vector3.UnitY;
        Matrix4 changed = edited.ModGetKandenSegmentDrawPose(2, edited.Position, out bool sampled);
        Check(!sampled && changed == Matrices(edited)[2], "uncaptured local segment edit selects exact current matrix");
        var pooled = Pair(scene); pooled.ModResetDrawState(); Current(pooled, "pool/lifetime reset clears segment carry");
        var missing = Pair(scene); Tick(scene, 3); Current(missing, "missing completed capture expires specialized carry");
        var focus = Pair(scene); FrameTiming.Reset(); FrameTiming.Advance(1d / 120);
        Current(focus, "focus clock reset rejects specialized carry");
        Tick(scene, 2); Pose(focus, 3, false); focus.ModCaptureDrawState(); Tick(scene, 3);
        Current(focus, "first post-reset capture rebases chain shape");
        Tick(scene, 3); Pose(focus, 4, true); focus.ModCaptureDrawState(); Tick(scene, 4);
        Matrix4 recovered = focus.ModGetKandenSegmentDrawPose(0, focus.Position, out sampled);
        Check(sampled && Near(recovered.Row3.Xyz, focus.Position + new Vector3(.2f, 0, .1f)),
            "second consecutive post-reset capture resumes local chain sampling");
    }
    private static void RotationHistories(Scene scene)
    {
        var model = typeof(PlayerEntity).GetField("_modelTransform", Hidden)!;
        var spireFacing = typeof(PlayerEntity).GetField("_spireAltFacing", Hidden)!;
        var spireUp = typeof(PlayerEntity).GetField("_spireAltUp", Hidden)!;
        var attackRotation = typeof(PlayerEntity).GetMethod("SpireAttackDrawRotation", Hidden)!;
        Matrix4 Attack(PlayerEntity actor) => (Matrix4)attackRotation.Invoke(actor, null)!;
        PlayerEntity RotationPair()
        {
            var actor = scene.Players.Items[1];
            HunterProperty.SetValue(actor, Hunter.Spire);
            actor.Health = 100; actor.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
            Flags.SetValue(actor, PlayerFlags1.AltForm);
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!.SetValue(actor, PlayerFlags2.None);
            typeof(PlayerEntity).GetField("_frozenTimer", Hidden)!.SetValue(actor, (ushort)0);
            Tick(scene, 0); actor.Position = Vector3.Zero; model.SetValue(actor, Matrix4.Identity);
            spireFacing.SetValue(actor, Vector3.UnitZ); spireUp.SetValue(actor, Vector3.UnitY);
            actor.ModResetDrawState(); actor.ModCaptureDrawState();
            Tick(scene, 1); actor.Position = new Vector3(2, 0, 0);
            model.SetValue(actor, Matrix4.CreateRotationY(MathF.PI / 2));
            spireFacing.SetValue(actor, Vector3.UnitX); actor.ModCaptureDrawState(); Tick(scene, 2);
            return actor;
        }
        var actor = RotationPair();
        Matrix4 canonicalModel = (Matrix4)model.GetValue(actor)!;
        Matrix4 canonicalAttack = EntityBase.GetTransformMatrix(Vector3.UnitX, Vector3.UnitY);
        uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
        for (int repeat = 0; repeat < 5; repeat++)
        {
            Matrix4 draw = actor.ModGetAlternateFormDrawPose(Vector3.UnitX, out bool sampled);
            Check(sampled && Near(draw.Row3.Xyz, Vector3.UnitX)
                && Near(draw.Row2.Xyz, Matrix4.CreateRotationY(MathF.PI / 4).Row2.Xyz),
                "alternate-form rotation samples completed orientations independently of its selected actor translation");
            Check(Near(Attack(actor).Row2.Xyz,
                EntityBase.GetTransformMatrix(new Vector3(1, 0, 1).Normalized(), Vector3.UnitY).Row2.Xyz),
                "Spire attack basis samples its own completed orientation at the same display fraction");
        }
        Check((Matrix4)model.GetValue(actor)! == canonicalModel
            && (Vector3)spireFacing.GetValue(actor)! == Vector3.UnitX && (Vector3)spireUp.GetValue(actor)! == Vector3.UnitY
            && scene.Random.Rng1 == rng1 && scene.Random.Rng2 == rng2,
            "alternate and attack orientation reads preserve canonical basis, model matrix and RNG");
        foreach (var item in new (string Label, Action<PlayerEntity> Edit)[]
        {
            ("form", value => Flags.SetValue(value, value.Flags1 | PlayerFlags1.Morphing)),
            ("Hunter", value => HunterProperty.SetValue(value, Hunter.Noxus)),
            ("death", value => value.Health = 0),
            ("frozen", value => typeof(PlayerEntity).GetField("_frozenTimer", Hidden)!.SetValue(value, (ushort)1)),
            ("missing capture", _ => Tick(scene, 3)),
            ("focus generation", _ => { FrameTiming.Reset(); FrameTiming.Advance(1d / 120); })
        })
        {
            actor = RotationPair(); item.Edit(actor);
            Matrix4 draw = actor.ModGetAlternateFormDrawPose(actor.Position, out bool sampled);
            Check(!sampled && Near(draw.Row2.Xyz, canonicalModel.Row2.Xyz) && Attack(actor) == canonicalAttack,
                item.Label + " fence rejects alternate and Spire attack rotation history together");
        }
        actor = RotationPair(); spireFacing.SetValue(actor, -Vector3.UnitZ);
        Check(Attack(actor) == EntityBase.GetTransformMatrix(-Vector3.UnitZ, Vector3.UnitY),
            "uncaptured Spire attack-basis edit rejects its stale orientation history");
        actor = RotationPair(); model.SetValue(actor, Matrix4.CreateRotationY(-MathF.PI / 2));
        var edited = actor.ModGetAlternateFormDrawPose(actor.Position, out bool editedSample);
        Check(!editedSample && Near(edited.Row2.Xyz, Matrix4.CreateRotationY(-MathF.PI / 2).Row2.Xyz),
            "uncaptured alternate-form rotation edit rejects its stale history");
        actor = RotationPair(); FrameTiming.FrameRateCap = 60;
        var sixty = actor.ModGetAlternateFormDrawPose(actor.Position, out bool sixtySample);
        Check(!sixtySample && Near(sixty.Row2.Xyz, canonicalModel.Row2.Xyz) && Attack(actor) == canonicalAttack,
            "60 Hz alternate and Spire attack rotation remain exact current-state presentation");
        FrameTiming.FrameRateCap = 120;
        actor = RotationPair();
        var flags2 = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags2))!;
        flags2.SetValue(actor, actor.Flags2 | PlayerFlags2.AltAttack);
        Check(Attack(actor) == canonicalAttack, "Spire attack entry flag fences its old orientation before another capture");
        Tick(scene, 2); actor.ModCaptureDrawState(); Tick(scene, 3);
        Check(Attack(actor) == canonicalAttack, "first completed Spire attack-entry capture rebases its independent orientation history");
    }
    private static void ExactFallbackAndDrawRestore(Scene scene)
    {
        var actor = Pair(scene, 0);
        actor.Position = new Vector3(100_000_000, -100_000_000, 100_000_000);
        var canonical = Matrices(actor);
        for (int i = 0; i < canonical.Length; i++)
        {
            canonical[i] = Matrix4.CreateRotationY(.13f * (i + 1));
            canonical[i].Row3.Xyz = new Vector3(.25f + i, 1.125f, -2.375f);
        }
        actor.ModResetDrawState();
        for (int i = 0; i < canonical.Length; i++)
        {
            Check(actor.ModGetKandenSegmentDrawPose(i, actor.Position, out bool sampled) == canonical[i] && !sampled,
                "zero-delta Kanden fallback preserves every canonical matrix bit despite large actor/local cancellation");
            Matrix4 expected = canonical[i]; expected.Row3.Xyz += new Vector3(32, 0, 0);
            Check(actor.ModGetKandenSegmentDrawPose(i, actor.Position + new Vector3(32, 0, 0), out sampled) == expected && !sampled,
                "fallback Kanden attachment adds only the selected actor translation delta");
        }

        // Exercise PlayerEntity.Draw's actual specialized submission/finally
        // with an empty five-node model. Alpha zero avoids asset replacement
        // lookup; node submission and canonical cleanup still execute.
        var nodes = new RawNode[5];
        for (int i = 0; i < nodes.Length; i++)
        {
            object raw = default(RawNode);
            typeof(RawNode).GetField(nameof(RawNode.Name))!.SetValue(raw, new byte[64]);
            typeof(RawNode).GetField(nameof(RawNode.ParentId))!.SetValue(raw, (short)-1);
            typeof(RawNode).GetField(nameof(RawNode.ChildId))!.SetValue(raw, (short)-1);
            typeof(RawNode).GetField(nameof(RawNode.NextId))!.SetValue(raw, (short)-1);
            nodes[i] = (RawNode)raw;
        }
        var model = new Model("specialized-content-free", false, default, nodes, Array.Empty<RawMesh>(),
            Array.Empty<RawMaterial>(), Array.Empty<DisplayList>(), Array.Empty<IReadOnlyList<RenderInstruction>>(),
            new AnimationResults(), Array.Empty<Matrix4>(), Array.Empty<Recolor>(), Array.Empty<int>(),
            Array.Empty<Vector3Fx>(), Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>());
        actor = Pair(scene, 0);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!.SetValue(actor, Metadata.PlayerValues[(int)Hunter.Kanden]);
        var lods = (ModelInstance[])typeof(PlayerEntity).GetField("_bipedModelLods", Hidden)!.GetValue(actor)!;
        lods[0] = new ModelInstance(model); lods[1] = new ModelInstance(model);
        typeof(PlayerEntity).GetField("_bipedModel1", Hidden)!.SetValue(actor, new ModelInstance(model));
        typeof(PlayerEntity).GetField("_bipedModel2", Hidden)!.SetValue(actor, new ModelInstance(model));
        typeof(PlayerEntity).GetField("_altModel", Hidden)!.SetValue(actor, new ModelInstance(model));
        typeof(PlayerEntity).GetField("_curAlpha", Hidden)!.SetValue(actor, 0f);
        var modelField = typeof(PlayerEntity).GetField("_modelTransform", Hidden)!;
        Matrix4 canonicalModel = (Matrix4)modelField.GetValue(actor)!;
        var saved = Matrices(actor).ToArray();
        bool priorCosmetics = MphRead.Mods.RenderOptions.ShowCustomCosmetics;
        try
        {
            MphRead.Mods.RenderOptions.ShowCustomCosmetics = false;
            for (int draw = 0; draw < 3; draw++)
            {
                actor.Draw();
                Check(model.Nodes.Select(node => node.Animation).SequenceEqual(saved)
                    && Matrices(actor).SequenceEqual(saved) && (Matrix4)modelField.GetValue(actor)! == canonicalModel,
                    "actual Kanden Player.Draw restores canonical chain nodes, simulation matrices and model basis after each fractional picture");
            }
        }
        finally { MphRead.Mods.RenderOptions.ShowCustomCosmetics = priorCosmetics; }
    }
    internal static int Run()
    {
        _checks = _failures = 0;
        var priorGame = GameState.Current; var priorPlayers = PlayerEntity.LegacyRegistry; var priorRandom = Rng.Current;
        int priorCap = FrameTiming.FrameRateCap;
        try
        {
            FrameTiming.FrameRateCap = 120; FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); FrameTiming.Advance(1d / 120);
            var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            scene.SetFreeCamera(false); scene.Players.MainPlayerIndex = 0;
            Sampling(scene); Boundaries(scene); RotationHistories(scene); ExactFallbackAndDrawRestore(scene);
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A",
                "specialized draw history preserves the frozen replay-world field contract");
        }
        catch (Exception exception) { Check(false, exception.ToString()); }
        finally
        {
            GameState.Current = priorGame; PlayerEntity.LegacyRegistry = priorPlayers; Rng.Current = priorRandom;
            FrameTiming.FrameRateCap = priorCap; FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
        }
        Console.WriteLine($"[specialized-presentation] {_checks - _failures}/{_checks} checks passed.");
        return _failures == 0 ? 0 : 1;
    }
}
