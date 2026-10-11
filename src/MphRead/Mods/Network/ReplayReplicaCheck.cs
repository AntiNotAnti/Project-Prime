using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
#if !ANDROID
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;
#endif

namespace MphRead.Mods.Network
{
    internal static class ReplayReplicaCheck
    {
        internal static int Run(string path, string? screenshots = null)
        {
#if !ANDROID
            NativeWindow? window = null;
            if (screenshots != null)
            {
                var settings = Render.DesktopGlContext.Settings(background: true);
                settings.ClientSize = new Vector2i(640, 480);
                window = new NativeWindow(settings);
                using var graphics = new MphRead.Mods.Render.DesktopGraphicsSession(window);
            }
            else
#endif
                Headless.Enter();
            NetSession.StartPlayback();
            var live = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            live.GameState.Points[2] = 731;
            live.Random.SetRng1(54321); live.Random.SetRng2(98765);
            NetPlayerBridge.ShootPressAge[2] = 23;
            var foregroundPlayer = PlayerEntity.Players[0];
            var foregroundAudio = global::MphRead.Sound.Sfx.Instance;
            string before = Sentinel(live);
            PassiveReplayScene? restored = null;
            try
            {
                Vector2i size = screenshots != null ? new(640, 480) : new(256, 192);
                using var first = new PassiveReplayScene(path, size);
                using var second = new PassiveReplayScene(path, size);
                AssertNetworkSlotsConstructed(first);
                AssertNetworkSlotsConstructed(second);
                ReplayAssetChecks.Run(first, second, path, size);
                var hashes = new Queue<(string Gameplay, string Presentation, ReplayReplicaCheckpoint Decoder)>();
                var drawnCheckpointFrames = new HashSet<uint>();
                var referenceFrames = new Dictionary<uint, (string Gameplay, string Presentation)>();
                Replay.ReplayWorldCheckpoint? clipCheckpoint = null;
                int steps = 0, projectileFrames = 0, modelRestores = 0, worldRestores = 0, continuedFrames = 0;
                bool projectileCheckpoint = false;
                while (first.Step())
                {
                    if (restored != null)
                    {
                        bool advanced = restored.Step();
                        CheckWorld(first, restored, "continuation", advanced);
                        continuedFrames++;
                    }
                    bool captureProjectile = false;
                    if (!projectileCheckpoint)
                        foreach (EntityBase entity in first.Scene.Entities)
                            if (entity is BeamProjectileEntity or BombEntity) { captureProjectile = true; break; }
                    if (captureProjectile || first.Session.CurrentFrame > 0 && first.Session.CurrentFrame % 300 == 0)
                    {
                        projectileCheckpoint |= captureProjectile;
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        var checkpoint = Replay.ReplayWorldCheckpoint.Capture(first);
                        using (var reference = Replay.ReplayWorldCheckpoint.Capture(first, boundAccessors: false))
                            if (!checkpoint.Bytes.SequenceEqual(reference.Bytes))
                                throw new InvalidDataException("Bound checkpoint capture changed serialized bytes.");
                        if (checkpoint.Frame == 900) clipCheckpoint = checkpoint;
                        double captureMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
                        restored?.Dispose(); restored = new PassiveReplayScene(path, size);
                        checkpoint.Restore(restored);
                        double restoreMs = timer.Elapsed.TotalMilliseconds;
                        CheckWorld(first, restored, "restore", advanced: true);
                        worldRestores++;
                        if (screenshots != null)
                        {
                            drawnCheckpointFrames.Add(checkpoint.Frame);
                            string linearImage = Path.Combine(screenshots, $"linear-{checkpoint.Frame}.png");
                            string restoredImage = Path.Combine(screenshots, $"restored-{checkpoint.Frame}.png");
                            Draw(first, linearImage); Draw(restored, restoredImage);
                            if (!File.ReadAllBytes(linearImage).SequenceEqual(File.ReadAllBytes(restoredImage)))
                                throw new InvalidDataException($"Detached world picture differs at frame {checkpoint.Frame}.");
                        }
                        Console.WriteLine($"[replayreplica] restored frame {checkpoint.Frame}: {checkpoint.Bytes.Length} bytes; capture {captureMs:F2} ms, create/restore {restoreMs:F2} ms");
                        if (checkpoint != clipCheckpoint) checkpoint.Dispose();
                    }
                    hashes.Enqueue((ReplayStateHash.Compute(first.Scene, first.Session.CurrentFrame),
                        first.Scene.ReplayPresentationHash(first.Session.CurrentFrame), first.State.CaptureCheckpoint()));
                    referenceFrames[first.Session.CurrentFrame] = (ReplayStateHash.Compute(first.Scene, first.Session.CurrentFrame),
                        first.Scene.ReplayPresentationHash(first.Session.CurrentFrame));
                    foreach (EntityBase entity in first.Scene.Entities)
                        if (entity is BeamProjectileEntity or BombEntity) { projectileFrames++; break; }
                    if (++steps % 17 == 0 || first.Session.AtEnd)
                    {
                        while (hashes.TryDequeue(out var expected))
                        {
                            if (!second.Step()) throw new InvalidDataException("Interleaved replay ended early.");
                            if (screenshots != null && drawnCheckpointFrames.Contains(second.Session.CurrentFrame))
                                Draw(second, Path.Combine(screenshots, "interleaved-checkpoint.png"));
                            if (ReplayStateHash.Compute(second.Scene, second.Session.CurrentFrame) != expected.Gameplay)
                                throw new InvalidDataException($"Interleaved replica worlds differ at frame {second.Session.CurrentFrame}.");
                            if (second.Scene.ReplayPresentationHash(second.Session.CurrentFrame) != expected.Presentation)
                                throw new InvalidDataException($"Interleaved replica animation/effects differ at frame {second.Session.CurrentFrame}.");
                            var decoded = new ReplayReplicaState(); decoded.RestoreCheckpoint(expected.Decoder);
                            if (!decoded.CaptureCheckpoint().Bytes.SequenceEqual(second.State.CaptureCheckpoint().Bytes))
                                throw new InvalidDataException($"Detached decoder differs at frame {second.Session.CurrentFrame}.");
                        }
                        foreach (EntityBase entity in second.Scene.Entities)
                            foreach (var model in entity.ReplayModels)
                            {
                                var captured = Replay.ReplayModelCheckpoint.Capture(model);
                                model.AnimInfo.Frame[0] += 7; model.Active = !model.Active;
                                captured.Restore(model);
                                if (!Replay.ReplayModelCheckpoint.Capture(model).Bytes.SequenceEqual(captured.Bytes))
                                    throw new InvalidDataException("Animation checkpoint did not restore its exact values.");
                                modelRestores++;
                            }
                        if (screenshots != null && steps % 170 == 0)
                        {
                            Draw(first, Path.Combine(screenshots, "first.png"));
                            Draw(second, Path.Combine(screenshots, "second.png"));
                            if (restored != null) Draw(restored, Path.Combine(screenshots, "restored.png"));
                        }
                    }
                    if (Sentinel(live) != before || !ReferenceEquals(GameState.Current, live.GameState)
                        || !ReferenceEquals(PlayerEntity.Players[0], foregroundPlayer)
                        || !ReferenceEquals(global::MphRead.Sound.Sfx.Instance, foregroundAudio))
                        throw new InvalidDataException($"Replica changed foreground state at frame {first.Session.CurrentFrame}.");
                }
                if (screenshots != null) Draw(second, Path.Combine(screenshots, "before-dispose.png"));
                first.Dispose();
                if (screenshots != null)
                {
                    Draw(second, Path.Combine(screenshots, "after-dispose.png"));
                    if (!File.ReadAllBytes(Path.Combine(screenshots, "before-dispose.png"))
                        .SequenceEqual(File.ReadAllBytes(Path.Combine(screenshots, "after-dispose.png"))))
                        throw new InvalidOperationException("Disposing a replica changed another scene's picture.");
                }
                second.Dispose();
                using (clipCheckpoint)
                    ReplayReplicaSeekChecks.Run(path, size, referenceFrames, clipCheckpoint, comparePresentation: screenshots == null);
                if (Sentinel(live) != before || !ReferenceEquals(global::MphRead.Sound.Sfx.Instance, foregroundAudio))
                    throw new InvalidDataException("Replica teardown changed foreground state.");
                if (steps == 0) throw new InvalidDataException("No replica frames were simulated.");
                Console.WriteLine($"[replayreplica] PASS: {steps} gameplay/presentation hashes and decoder restores, {modelRestores} animation restores, {worldRestores} detached worlds and {continuedFrames} continuation frames, {projectileFrames} frames with projectiles; interleaved scenes and teardown preserve foreground state.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine($"[replayreplica] FAIL: {ex}"); return 1; }
            finally
            {
                restored?.Dispose();
                live.DoCleanup(); NetSession.Stop();
#if !ANDROID
                window?.Dispose();
#endif
            }
        }

        // Failure-only diagnostics: no reflection or checkpoint work on the successful path.
        private static void CheckWorld(PassiveReplayScene expected, PassiveReplayScene actual, string operation, bool advanced)
        {
            uint frame = expected.Session.CurrentFrame;
            string gameplay = ReplayStateHash.Compute(expected.Scene, frame);
            string actualGameplay = ReplayStateHash.Compute(actual.Scene, actual.Session.CurrentFrame);
            string presentation = expected.Scene.ReplayPresentationHash(frame);
            string actualPresentation = actual.Scene.ReplayPresentationHash(actual.Session.CurrentFrame);
            if (advanced && actual.Session.CurrentFrame == frame && gameplay == actualGameplay && presentation == actualPresentation) return;
            string detail;
            try { detail = DescribeDifferences(expected, actual); }
            catch (Exception ex) { detail = $"diagnostic failed: {ex.GetType().Name}: {ex.Message}"; }
            throw new InvalidDataException($"Detached world {operation} differs: expectedFrame={frame}, actualFrame={actual.Session.CurrentFrame}, advanced={advanced}; "
                + $"gameplay expected={gameplay} actual={actualGameplay}; presentation expected={presentation} actual={actualPresentation}; {detail}");
        }

        private static string DescribeDifferences(PassiveReplayScene expected, PassiveReplayScene actual)
        {
            var text = new StringBuilder();
            int differences = 0, visited = 0;
            void Compare(object? left, object? right, string path, int depth)
            {
                if (differences >= 24 || ++visited > 12000 || ReferenceEquals(left, right)) return;
                void Difference(string values) { text.Append(path).Append(": ").Append(values.Length > 240 ? values[..240] + "..." : values).Append("; "); differences++; }
                if (left == null || right == null) { Difference($"{left?.GetType().Name ?? "null"} -> {right?.GetType().Name ?? "null"}"); return; }
                Type type = left.GetType();
                if (type != right.GetType()) { Difference($"type {type.Name} -> {right.GetType().Name}"); return; }
                if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string))
                {
                    if (!left.Equals(right)) Difference($"{left} -> {right}");
                    return;
                }
                if (left is Array a && right is Array b)
                {
                    if (a.Length != b.Length) Difference($"length {a.Length} -> {b.Length}");
                    if (a.Rank == 1 && b.Rank == 1)
                        for (int i = 0; i < Math.Min(64, Math.Min(a.Length, b.Length)); i++) Compare(a.GetValue(i), b.GetValue(i), $"{path}[{i}]", depth + 1);
                    return;
                }
                // References are never traversed: entity identity is useful without following cycles.
                if (depth > 0 && !type.IsValueType)
                {
                    if (left is EntityBase le && right is EntityBase re && (le.Type != re.Type || le.Id != re.Id))
                        Difference($"entity {le.Type}/{le.Id} -> {re.Type}/{re.Id}");
                    return;
                }
                if (depth > 4) return;
                for (Type? current = type; current != null; current = current.BaseType)
                    foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.Name))
                        Compare(field.GetValue(left), field.GetValue(right), path + "." + field.Name, depth + 1);
            }
            Compare(expected.Scene.GameState, actual.Scene.GameState, "gameState", 0);
            Compare(expected.Scene.Random, actual.Scene.Random, "random", 0);
            var leftList = new List<EntityBase>();
            foreach (EntityBase entity in expected.Scene.Entities) leftList.Add(entity);
            var rightList = new List<EntityBase>();
            foreach (EntityBase entity in actual.Scene.Entities) rightList.Add(entity);
            var leftEntities = leftList.ToArray();
            var rightEntities = rightList.ToArray();
            if (leftEntities.Length != rightEntities.Length) text.Append($"entityCount {leftEntities.Length} -> {rightEntities.Length}; ");
            for (int i = 0; i < Math.Min(leftEntities.Length, rightEntities.Length) && differences < 24; i++)
            {
                Compare(leftEntities[i], rightEntities[i], $"entity[{i}]({leftEntities[i].Type}/{leftEntities[i].Id})", 0);
                var lm = leftEntities[i].ReplayModels; var rm = rightEntities[i].ReplayModels;
                if (lm.Count != rm.Count) text.Append($"entity[{i}].modelCount {lm.Count} -> {rm.Count}; ");
                for (int j = 0; j < Math.Min(lm.Count, rm.Count) && differences < 24; j++)
                {
                    Compare(lm[j], rm[j], $"entity[{i}].model[{j}]", 0);
                    Compare(lm[j].AnimInfo, rm[j].AnimInfo, $"entity[{i}].model[{j}].AnimInfo", 0);
                }
            }
            using var lc = Replay.ReplayWorldCheckpoint.Capture(expected);
            using var rc = Replay.ReplayWorldCheckpoint.Capture(actual);
            int offset = 0, length = Math.Min(lc.Bytes.Length, rc.Bytes.Length);
            while (offset < length && lc.Bytes[offset] == rc.Bytes[offset]) offset++;
            text.Append($"checkpoint lengths={lc.Bytes.Length}/{rc.Bytes.Length}, firstByteDifference={(offset < length || lc.Bytes.Length != rc.Bytes.Length ? offset : -1)}; fieldDifferences={differences}, inspected={visited} (bounded)");
            return text.ToString();
        }

        private static void AssertNetworkSlotsConstructed(PassiveReplayScene replay)
        {
            if (replay.Scene.Players.MaxPlayers != PlayerEntity.SlotCapacity
                || replay.Scene.Players.PlayersCreated != PlayerEntity.SlotCapacity)
                throw new InvalidDataException("Replay did not construct the full network slot capacity.");
            for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
            {
                PlayerEntity player = replay.Scene.Players.Items[slot];
                bool registered = false;
                foreach (EntityBase entity in replay.Scene.Entities)
                {
                    if (ReferenceEquals(entity, player))
                    {
                        registered = true;
                        break;
                    }
                }
                if (!player.LoadFlags.TestFlag(LoadFlags.SlotActive)
                    || !registered
                    || player.Halfturret == null)
                    throw new InvalidDataException($"Replay slot {slot} was not fully registered before playback.");
                if (replay.State.Occupant(slot).IsBot && player.IsBot)
                    throw new InvalidDataException($"Replay slot {slot} incorrectly re-enabled recorded bot AI.");
            }
        }

        private static void Draw(PassiveReplayScene replay, string path)
        {
#if !ANDROID
            var scene = replay.Scene;
            PlayerEntity player = scene.Players.Items[0];
            scene.SetReplicaCamera(player.Position + new Vector3(0, 1.6f, 0),
                player.Position + new Vector3(0, 1.6f, 0) + player.FacingVector, 78);
            string before = ReplayStateHash.Compute(scene, replay.Session.CurrentFrame);
            scene.OnDrawFrame();
            if (!scene.OnRenderFrame()) throw new InvalidOperationException("Replica renderer stopped.");
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Replica draw produced a GL error.");
            if (ReplayStateHash.Compute(scene, replay.Session.CurrentFrame) != before)
                throw new InvalidOperationException("Replica drawing advanced gameplay state.");
            if (!ScreenCapture.Save(scene, path)) throw new IOException("Replica picture was empty.");
#endif
        }

        private static string Sentinel(Scene live) => string.Join('|', ReplayStateHash.Compute(live, 0),
            live.Random.Rng1, live.Random.Rng2, NetSession.Active, NetSession.Role, NetSession.LocalSlot,
            NetSession.CurrentMatchId, NetSession.AuthorityEpoch, NetSession.NetFrame,
            NetSession.SnapshotsReceived, NetSession.SnapshotsSent, NetSession.IntentsReceived,
            NetPlayerBridge.ShootPressAge[2], string.Join(',', NetDamage.PlayerChecks),
            string.Join(',', NetDamage.PlayerOverlaps), NetDamage.BombSpawnCalls,
            NetDamage.ShockCoilSpawned, NetHealthSync.RegisteredSpawns.Count);
    }
}
