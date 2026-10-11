using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class BombEffectBirthTests
{
    private static int _checks;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo Time = typeof(Scene).GetField("_elapsedTime", Private)!;
    private static readonly FieldInfo Frame = typeof(Scene).GetField("_effectFrame", Private)!;
    private static readonly MethodInfo Effects = typeof(Scene).GetMethod("ProcessEffects", Private)!;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); Console.WriteLine("BOMB EFFECT PASS " + label); }
    private static void AdvanceClock(Scene scene)
    {
        Time.SetValue(scene, (float)Time.GetValue(scene)! + 1f / 60);
        Frame.SetValue(scene, (ulong)Frame.GetValue(scene)! + 1);
    }
    private static void ProcessEffects(Scene scene) => Effects.Invoke(scene, new[] { Frame.GetValue(scene) });
    private static int Particles(BombEntity bomb) => bomb.Effect?.Elements.Sum(e => e.Particles.Count) ?? 0;
    private static BombEntity Ghost(PassiveReplayScene replay, BombType kind)
    {
        var match = replay.State.Match!.Value;
        var scene = replay.Scene;
        scene.Bombs.Begin(match.MatchId, match.AuthorityEpoch);
        var key = new ShotKey(match.AuthorityEpoch, match.MatchId, 0, 1, 1, 1);
        var state = new BombState(key, kind, 0, scene.Players.Items[0].Position,
            Vector3.UnitZ, Vector3.UnitY, Vector3.Zero, 80, 0, 0, 0, 0);
        return BombEntity.SpawnReplica(scene.Players.Items[0], state, scene)!;
    }
    private static BombEntity RestoredGhost(PassiveReplayScene replay)
    {
        foreach (var bomb in replay.Scene.GetBombEntities()) if (bomb.ModReplica) return bomb;
        throw new InvalidOperationException("Restored canonical ghost is missing.");
    }
    internal static int Run(string replayPath)
    {
        Headless.Enter();
        try
        {
            foreach (var kind in new[] { BombType.MorphBall, BombType.Lockjaw })
            {
                foreach (bool beforeClock in new[] { false, true })
                {
                    using var replay = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
                    var ghost = Ghost(replay, kind);
                    Check(ghost.Effect == null, $"{kind} bootstrap birth has no premature zero-age emitter");
                    if (beforeClock) AdvanceClock(replay.Scene);
                    ghost.Process(); ProcessEffects(replay.Scene);
                    int particles = Particles(ghost);
                    Check(particles > 0, $"{kind} {(beforeClock ? "next-clock" : "same-step")} native effects phase emits body");
                    var effect = ghost.Effect;
                    AdvanceClock(replay.Scene); ghost.Process(); ProcessEffects(replay.Scene);
                    Check(ReferenceEquals(effect, ghost.Effect) && Particles(ghost) == particles,
                        $"{kind} following step retains body without duplicate birth");
                }
                using var source = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
                var pending = Ghost(source, kind);
                using var pendingCheckpoint = ReplayWorldCheckpoint.Capture(source);
                using var restored = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
                pendingCheckpoint.Restore(restored);
                var ghostRestored = RestoredGhost(restored);
                Check(restored.Scene.Bombs.Canonical && ghostRestored.Effect == null,
                    $"{kind} pre-emission world restore retains canonical deferred birth");
                AdvanceClock(restored.Scene); ghostRestored.Process(); ProcessEffects(restored.Scene);
                int restoredParticles = Particles(ghostRestored);
                Check(restoredParticles > 0, $"{kind} restored pending ghost emits at first simulation effects phase");
                using var emittedCheckpoint = ReplayWorldCheckpoint.Capture(restored);
                using var continued = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
                emittedCheckpoint.Restore(continued);
                var existing = RestoredGhost(continued);
                Check(Particles(existing) == restoredParticles, $"{kind} emitted body particles survive world restore");
                var existingEffect = existing.Effect;
                AdvanceClock(continued.Scene); existing.Process(); ProcessEffects(continued.Scene);
                Check(ReferenceEquals(existingEffect, existing.Effect) && Particles(existing) == restoredParticles,
                    $"{kind} post-emission restore does not emit duplicate body");
            }
            Console.WriteLine($"PASS: {_checks} bomb effect birth/checkpoint assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
