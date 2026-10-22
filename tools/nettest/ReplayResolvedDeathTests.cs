using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ReplayResolvedDeathTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string label) => NetArchitectureTests.Check(ok, label);
    private static FieldInfo Field(Type type, string name) => type.GetField(name, Private)!;
    private static EffectElementEntry[] DeathEffects(Scene scene)
        => ((IEnumerable<EffectElementEntry>)Field(typeof(Scene), "_activeElements").GetValue(scene)!)
            .Where(e => e.EffectId == 216).ToArray();

    private static void Arm(PassiveReplayScene replay, byte slot)
    {
        var poses = replay.Scene.ReplayPoses!;
        // Prepare the real recording before replacing just this frame's outcome.
        poses.PresentResolvedImpacts(replay.Scene);
        uint frame = replay.Session.RecordingFrame;
        Check(replay.State.TryGetPlayer(slot, out var recorded) && recorded.Health > 0,
            "authoritative victim is alive before injected lethal presentation");
        var victim = replay.Scene.Players.Items[slot];
        Field(typeof(PlayerEntity), "_health").SetValue(victim, recorded.Health);
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!
            .SetValue(victim, victim.Flags1 | PlayerFlags1.AltForm);
        var match = replay.State.Match!.Value;
        var fact = new ReplayShotFact(match.MatchId, match.AuthorityEpoch, frame, frame,
            0xF0000001, 1, slot, recorded.SlotGeneration, recorded.LifeId,
            slot, recorded.SlotGeneration, recorded.LifeId, 0,
            ReplayShotFactFlags.Lethal | ReplayShotFactFlags.Direct,
            recorded.Health, 0, 0, 0, victim.Position);
        var facts = (Dictionary<uint, List<ReplayShotFact>>)Field(typeof(ReplayPoseStream), "_resolvedShotFacts").GetValue(poses)!;
        facts[frame] = new List<ReplayShotFact> { fact };
        Field(typeof(ReplayPoseStream), "_impactPrepared").SetValue(poses, null);
        poses.PresentResolvedImpacts(replay.Scene);
    }

    public static int Run(string replayPath)
    {
        MphRead.Mods.Headless.Enter();
        try
        {
            using var linear = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
            using var seeking = new PassiveReplayScene(replayPath, new Vector2i(256, 192));
            byte slot = byte.MaxValue;
            for (int frame = 0; frame < 600 && slot == byte.MaxValue; frame++)
            {
                Check(linear.Step() && seeking.Step(), "recording reaches live victim");
                for (byte i = 0; i < PlayerEntity.SlotCapacity; i++)
                    if (linear.State.TryGetPlayer(i, out var a) && a.Health > 0
                        && seeking.State.TryGetPlayer(i, out var b) && a.Equals(b)) { slot = i; break; }
            }
            Check(slot != byte.MaxValue, "recording supplies matching live authority victim");
            Arm(linear, slot); Arm(seeking, slot);
            var authority = linear.State.CaptureCheckpoint().Bytes.ToArray();
            var seekAuthority = seeking.State.CaptureCheckpoint().Bytes.ToArray();
            int before = DeathEffects(linear.Scene).Length;
            linear.Scene.ReplayPoses!.PresentResolvedDeaths(linear.Scene, seeking: false);
            seeking.Scene.ReplayPoses!.PresentResolvedDeaths(seeking.Scene, seeking: true);
            var effects = DeathEffects(linear.Scene);
            Check(effects.Length > before && DeathEffects(seeking.Scene).Length == effects.Length,
                "seek and linear lethal transition both spawn native deathAlt216 effects");
            uint at = linear.Session.RecordingFrame;
            string hash = linear.Scene.ReplayPresentationHash(at);
            Check(seeking.Scene.ReplayPresentationHash(at) == hash,
                "seek and linear lethal presentation hashes agree");
            linear.Scene.ReplayPoses.PresentResolvedDeaths(linear.Scene, seeking: false);
            seeking.Scene.ReplayPoses.PresentResolvedDeaths(seeking.Scene, seeking: true);
            Check(linear.Scene.ReplayPresentationHash(at) == hash
                && seeking.Scene.ReplayPresentationHash(at) == hash,
                "repeated transition creates no duplicate effects");
            // A later hold can follow another accepted alive snapshot. Exercise
            // that case without advancing unrelated simulation or effect clocks.
            foreach (var replay in new[] { linear, seeking })
            {
                Field(typeof(PlayerEntity), "_health").SetValue(replay.Scene.Players.Items[slot], (ushort)99);
                var lethalFrames = (uint[])Field(typeof(ReplayPoseStream), "_lastResolvedLethalFrame").GetValue(replay.Scene.ReplayPoses)!;
                Check(at > 0, "hold fixture has a prior authority frame");
                lethalFrames[slot] = at - 1;
                replay.Scene.ReplayPoses!.PresentResolvedDeaths(replay.Scene, seeking: true);
            }
            Check(linear.Scene.ReplayPresentationHash(at) == hash
                && seeking.Scene.ReplayPresentationHash(at) == hash
                && DeathEffects(linear.Scene).SequenceEqual(effects),
                "later alive-snapshot hold creates no duplicate death effects");
            Check(linear.State.CaptureCheckpoint().Bytes.SequenceEqual(authority)
                && seeking.State.CaptureCheckpoint().Bytes.SequenceEqual(seekAuthority),
                "presentation never changes recorded authoritative state or payments");
            Console.WriteLine("PASS: resolved death seek/linear effect regression");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
