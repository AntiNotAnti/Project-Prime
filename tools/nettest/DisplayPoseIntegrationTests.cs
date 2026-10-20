using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>Content-free integration with the actual EntityBase draw owner.
/// No OnLoad, asset files, native window, GL, audio device, or network session.</summary>
internal static class DisplayPoseIntegrationTests
{
    private static readonly FieldInfo Frame = typeof(Scene).GetField("_frameCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo Flags = typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!;

    private sealed class ProbeEntity : EntityBase
    {
        internal ProbeEntity(Scene scene) : base(EntityType.Object, scene) { }
        internal void Transfer(Scene scene) => _scene = scene;
    }

    private static void Tick(Scene scene, ulong tick) => Frame.SetValue(scene, tick);
    private static bool Near(Vector3 actual, float x) => (actual - new Vector3(x, 0, 0)).LengthSquared < .0000001f;
    private static Scene NewScene() => new(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(),
        SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);

    private static void BipedTransformChecks(Action<bool, string> check)
    {
        // Independent expression of the pre-CPP-03 biped body basis. The
        // factorized helper must preserve exact canonical matrix/angle math.
        foreach (Vector3 facing in new[] { Vector3.UnitZ, Vector3.UnitX,
            new Vector3(.3f, .4f, .5f).Normalized(), Vector3.UnitY })
        foreach (float scale in new[] { .75f, 1f, 1.125f })
        {
            Vector3 position = new(2, 3, 4), fallback = Vector3.UnitZ;
            float bottom = .375f;
            float limit = Fixed.ToFloat(2896), cos = MathF.Sqrt(1 - facing.Y * facing.Y), sin = facing.Y;
            if (MathF.Abs(facing.Y) > limit) { cos = limit; sin = facing.Y <= 0 ? -limit : limit; }
            float pitch = MathF.Atan2(sin, cos);
            Vector3 lateral = new(facing.X, 0, facing.Z);
            if (lateral.LengthSquared < .000001f) lateral = fallback;
            lateral = lateral.Normalized();
            Vector3 right = new(lateral.Z, 0, -lateral.X);
            Matrix4 expected = Matrix4.Identity;
            expected.Row0.Xyz = -right; expected.Row1.Xyz = Vector3.Cross(lateral, right);
            expected.Row2.Xyz = -lateral; expected.Row3.Xyz = position;
            expected.Row3.Y += bottom + bottom * (1 - scale);
            expected.Row0.Xyz *= scale; expected.Row1.Xyz *= scale; expected.Row2.Xyz *= scale;
            Matrix4 actual = PlayerEntity.ModBuildBipedDrawTransform(position, facing, fallback, scale, bottom, out float angle);
            check(actual == expected && angle == pitch, "factorized body matrix/pitch preserves canonical biped arithmetic exactly");
        }
    }

    private static void CheckpointChecks(Scene scene, Action<bool, string> check)
    {
        const string contract = "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A";
        check(ReplayWorldCheckpoint.ComputeContract(stable: true) == contract
            && ReplayCheckpointAccessors.Contract == contract, "historical world schema and generated accessor fingerprint unchanged");
        var fields = (Dictionary<Type, FieldInfo[]>)typeof(ReplayWorldCheckpoint)
            .GetField("Fields", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var types = (Dictionary<string, Type>)typeof(ReplayWorldCheckpoint)
            .GetField("Types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        check(fields.Values.SelectMany(value => value).All(field => field.Name != "_drawPose")
            && !types.ContainsValue(typeof(DisplayPoseHistory)) && !types.ContainsValue(typeof(DisplayPoseLifetime)),
            "new scene/room/lifetime draw state is absent from the archival field and type closure");
        var legacy = new[] { "_drawPrevious", "_drawCurrent", "_drawStateValid" }
            .Select(name => typeof(EntityBase).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
        check(legacy.All(field => fields[typeof(EntityBase)].Contains(field))
            && legacy[0].FieldType == typeof(Matrix4) && legacy[1].FieldType == typeof(Matrix4)
            && legacy[2].FieldType == typeof(bool), "legacy canonical slots retain exact names, types and schema positions");
        var actor = new PlayerEntity(0, scene) { Health = 100, LoadFlags = LoadFlags.Active | LoadFlags.Spawned };
        Tick(scene, 0); actor.ModResetDrawState();
        Matrix4 expectedPrevious = Matrix4.Identity, expectedCurrent = Matrix4.Identity;
        using var archival = new MemoryStream();
        using var archivalWriter = new BinaryWriter(archival);
        void RecordLegacy()
        {
            check((Matrix4)legacy[0].GetValue(actor)! == expectedPrevious
                && (Matrix4)legacy[1].GetValue(actor)! == expectedCurrent
                && (bool)legacy[2].GetValue(actor)!, "archival slots follow the original canonical capture rules");
            foreach (var field in legacy)
                ReplayWorldCheckpoint.WriteValue(archivalWriter, field.FieldType, field.GetValue(actor), _ => -1);
        }
        RecordLegacy();
        foreach (var item in new (ulong Tick, float X)[] { (1, 1), (1, 1.25f), (4, 1.5f), (5, 2), (6, 2.5f), (7, 9), (8, 8.5f) })
        {
            Tick(scene, item.Tick); actor.Position = new(item.X, 0, 0);
            if (item.Tick == 5) Flags.SetValue(actor, PlayerFlags1.Morphing);
            if (item.Tick == 6) actor.Health = 0;
            if ((actor.Position - expectedCurrent.Row3.Xyz).LengthSquared > 16f)
                expectedPrevious = expectedCurrent = actor.Transform;
            else { expectedPrevious = expectedCurrent; expectedCurrent = actor.Transform; }
            actor.ModCaptureDrawState();
            // Drawing may query carry repeatedly; none of it changes the
            // bytes saved by the historical schema, even across a boundary.
            _ = actor.ReplayDrawTransform; _ = actor.SimulationDrawPosition;
            RecordLegacy();
        }
        string digest = Convert.ToHexString(SHA256.HashData(archival.ToArray())).ToLowerInvariant();
        // Independent little-endian Matrix4/bool fixture of the pre-CPP-03
        // reset/capture rules. No current codec computes this expected value.
        check(digest == "64a16aac610f36c686ae6055fdfde3d2c94819fd1e1cddd7457cabf9138ae4f1",
            "legacy canonical snapshot byte hash unchanged across duplicate/gap/form/death/teleport ticks");

        var references = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        int Reference(object? value)
        {
            if (value == null) return -1;
            if (!references.TryGetValue(value, out int id)) references.Add(value, id = references.Count);
            return id;
        }
        byte[] Block(PlayerEntity player, bool bound)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            if (bound) check(ReplayCheckpointAccessors.Write(writer, player, Reference), "production bound player writer available");
            else foreach (var field in fields[typeof(PlayerEntity)])
                ReplayWorldCheckpoint.WriteValue(writer, field.FieldType, field.GetValue(player), Reference);
            return stream.ToArray();
        }
        byte[] block = Block(actor, bound: true);
        check(block.SequenceEqual(Block(actor, bound: false)), "bound and reflection archived player bytes match exactly");
        var objects = references.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray();
        object? Resolve(int id, Type type) => id == -1 ? null : objects[id];
        var restored = new PlayerEntity(0, scene);
        var readValue = typeof(ReplayWorldCheckpoint).GetMethod("ReadValue", BindingFlags.Static | BindingFlags.NonPublic)!;
        using (var stream = new MemoryStream(block, writable: false))
        using (var reader = new BinaryReader(stream))
        {
            Func<int, Type, object?> resolve = Resolve;
            foreach (var field in fields[typeof(PlayerEntity)])
                field.SetValue(restored, readValue.Invoke(null, new object?[] { reader, field.FieldType, resolve }));
            check(stream.Position == stream.Length, "historical player field block restores without trailing data");
        }
        check(block.SequenceEqual(Block(restored, bound: true)), "historical player checkpoint field block roundtrips byte-for-byte");
        restored.Health = 100;
        Tick(scene, 9); // legacy slots would blend 9 -> 8.5 here if read by drawing
        check(restored.ReplayDrawTransform == restored.Transform,
            "restored archival slots cannot become a second render history");
        Console.WriteLine($"Display pose archival schema {contract}; legacy snapshot SHA-256 {digest}; bound/reflection field-block roundtrip exact.");
    }

    internal static int Run()
    {
        Headless.Enter();
        var oldState = GameState.Current;
        var oldPlayers = PlayerEntity.LegacyRegistry;
        var oldRandom = Rng.Current;
        int oldCap = FrameTiming.FrameRateCap;
        Scene? scene = null, other = null;
        int checks = 0;
        void Check(bool ok, string message)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("DISPLAY POSE INTEGRATION " + message);
        }
        try
        {
            FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); FrameTiming.FrameRateCap = 120;
            FrameTiming.Advance(1d / 120); // live high-refresh clock, exactly half a fixed step
            scene = NewScene();
            var entity = new ProbeEntity(scene);
            scene.AddEntity(entity);
            Tick(scene, 1); entity.Position = Vector3.UnitX; entity.ModCaptureDrawState(); Tick(scene, 2);
            Check(Near(entity.ReplayDrawTransform.Row3.Xyz, .5f)
                && Near(entity.SimulationDrawPosition, .5f), "existing capture-before-increment hook samples matching translation and transform");
            Matrix4 canonical = entity.Transform;
            for (int i = 0; i < 20; i++)
                Check(Near(entity.ReplayDrawTransform.Row3.Xyz, .5f) && entity.Transform == canonical,
                    "repeated actual draw reads leave canonical state unchanged");
            entity.Position = new Vector3(1.1f, 0, 0);
            Check(entity.ReplayDrawTransform == entity.Transform, "uncaptured small transform edits fall back immediately");
            entity.Position = Vector3.UnitX;
            Tick(scene, 3);
            Check(entity.ReplayDrawTransform == entity.Transform, "skipped existing capture expires carry");

            Tick(scene, 0); entity.Position = Vector3.Zero; entity.ModResetDrawState();
            Tick(scene, 1); entity.Position = Vector3.UnitX; entity.ModCaptureDrawState(); Tick(scene, 2);
            FrameTiming.Reset(); FrameTiming.Advance(1d / 120);
            Check(entity.ReplayDrawTransform == entity.Transform, "focus/session clock reset fences actual entity history");
            Tick(scene, 2); entity.Position = new Vector3(2, 0, 0); entity.ModCaptureDrawState(); Tick(scene, 3);
            Check(entity.ReplayDrawTransform == entity.Transform, "first post-reset capture rebases history");
            Tick(scene, 3); entity.Position = new Vector3(3, 0, 0); entity.ModCaptureDrawState(); Tick(scene, 4);
            Check(Near(entity.ReplayDrawTransform.Row3.Xyz, 2.5f), "consecutive post-reset actual tick resumes sampling");

            Tick(scene, 3); entity.ModCaptureDrawState();
            Check(entity.ReplayDrawTransform == entity.Transform, "paused repeated capture stays on canonical tick");
            Tick(scene, 4); entity.Position = new Vector3(10, 0, 0); entity.ModCaptureDrawState(); Tick(scene, 5);
            Check(entity.ReplayDrawTransform == entity.Transform, "teleport rebases actual entity without ghosting");
            scene.RemoveEntity(entity);
            bool found = false;
            foreach (var active in scene.Entities) found |= ReferenceEquals(active, entity);
            Check(!found, "removed entity cannot enter existing scene draw enumeration");

            Tick(scene, 0); entity.Position = Vector3.Zero; entity.ModResetDrawState();
            Tick(scene, 1); entity.Position = Vector3.UnitX; entity.ModCaptureDrawState(); Tick(scene, 2);
            other = NewScene(); Tick(other, 2); entity.Transfer(other);
            Check(entity.ReplayDrawTransform == entity.Transform, "different real scene generation cannot reuse carry");

            // Exercise the actual PlayerEntity policy rather than a substitute
            // override: local first-person stays current while an offline
            // non-main actor uses completed-state interpolation.
            Tick(scene, 0);
            scene.Players.MainPlayerIndex = 0;
            scene.SetFreeCamera(false);
            var local = scene.Players.Items[0];
            var remote = scene.Players.Items[1];
            foreach (var player in new[] { local, remote })
            {
                player.LoadFlags = LoadFlags.Active | LoadFlags.Spawned;
                player.Health = 100;
                player.Position = Vector3.Zero;
                player.ModResetDrawState();
            }
            Tick(scene, 1);
            foreach (var player in new[] { local, remote })
            { player.Position = Vector3.UnitX; player.ModCaptureDrawState(); }
            Tick(scene, 2);
            Check(local.ReplayDrawTransform == local.Transform, "actual local first-person bypass adds no interpolation latency");
            Check(Near(remote.ReplayDrawTransform.Row3.Xyz, .5f), "actual non-main offline actor retains prior draw policy");
            local.ModGetDrawActorPose(out Vector3 localBody, out _);
            remote.ModGetDrawActorPose(out Vector3 remoteBody, out Vector3 remoteFacing);
            Check(localBody == local.Position && Near(remoteBody, .5f)
                && remoteFacing == Vector3.UnitZ,
                "Draw-used actor selector shares the offline body/alt/shadow pose while local first-person stays current");

            // The same real local actor is visible in the host-enabled third-
            // person match. The 60Hz body used to jump ahead of its interpolated
            // camera during fast movement. Exercise the actual Draw selector,
            // not a stand-in lerp: it must consume the same captured 60Hz pose
            // as other entities while first-person keeps its immediate policy.
            typeof(Scene).GetProperty(nameof(Scene.ModThirdPersonCameraActive),
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(scene, true);
            Matrix4 originalWorld = local.Transform;
            for (int i = 0; i < 4; i++)
            {
                local.ModGetDrawActorPose(out Vector3 smoothBody,
                    out Vector3 smoothFacing);
                Check(Near(smoothBody, .5f) && smoothFacing == Vector3.UnitZ
                    && local.Position == Vector3.UnitX && local.Transform == originalWorld,
                    "high-refresh local third-person body samples captured pose without mutating gameplay");
            }
            FrameTiming.FrameRateCap = 240;
            local.ModGetDrawActorPose(out Vector3 at240, out _);
            Check(Near(at240, .5f),
                "240Hz third-person local body shares same phase as 120Hz camera");
            FrameTiming.FrameRateCap = 60;
            local.ModGetDrawActorPose(out Vector3 at60, out _);
            Check(at60 == local.Position,
                "60Hz third-person local body stays immediate without added latency");
            FrameTiming.FrameRateCap = 120;
            PlayerFlags1 liveFlags = local.Flags1;
            Flags.SetValue(local, liveFlags | PlayerFlags1.Morphing);
            local.ModGetDrawActorPose(out Vector3 morphBody, out _);
            Check(morphBody == local.Position, "morph never smears local biped history into alt form");
            Flags.SetValue(local, liveFlags);
            local.ModResetDrawState();
            local.ModGetDrawActorPose(out Vector3 freshLife, out _);
            Check(freshLife == local.Position, "spawn reset fences stale third-person body history");
            scene.ModResetThirdPersonSpawnCamera(local);
            local.ModGetDrawActorPose(out Vector3 restoredFirst, out _);
            Check(restoredFirst == local.Position
                && !scene.ModThirdPersonCameraActive,
                "third-person spawn reset returns to canonical until next eligible draw");
            FrameTiming.FrameRateCap = 60;
            remote.ModGetDrawActorPose(out Vector3 sixtyBody, out Vector3 sixtyFacing);
            Check(sixtyBody == remote.Position && sixtyFacing == remote.FacingVector,
                "60 Hz Draw-used actor selector preserves exact canonical position/facing without extra restore work");
            FrameTiming.FrameRateCap = 120;
            PlayerFlags1 flags = remote.Flags1;
            Flags.SetValue(remote, flags | PlayerFlags1.Morphing);
            Check(remote.ReplayDrawTransform == remote.Transform, "actual morph flag fences carry before capture");
            remote.ModGetDrawActorPose(out Vector3 changedFormBody, out _);
            Check(changedFormBody == remote.Position, "Draw-used actor selector cannot carry an old form");
            remote.ModCaptureDrawState(); Tick(scene, 3);
            Check(remote.ReplayDrawTransform == remote.Transform, "actual new form capture does not blend old form");
            remote.Health = 0;
            Check(remote.ReplayDrawTransform == remote.Transform, "actual dead actor cannot carry its prior live pose");
            remote.Health = 100; remote.LoadFlags &= ~LoadFlags.Spawned;
            Check(remote.ReplayDrawTransform == remote.Transform, "actual despawned actor cannot carry its prior spawned pose");
            Flags.SetValue(remote, flags);
            BipedTransformChecks(Check);
            CheckpointChecks(scene, Check);
            Console.WriteLine($"DISPLAY POSE INTEGRATION PASS {checks} checks; content-free actual scene/entity/Draw-used actor selector hooks, no full Draw/3D/device acceptance.");
            return 0;
        }
        finally
        {
            other?.DoCleanup(); scene?.DoCleanup();
            GameState.Current = oldState; PlayerEntity.LegacyRegistry = oldPlayers; Rng.Current = oldRandom;
            FrameTiming.FrameRateCap = oldCap; FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
        }
    }
}
