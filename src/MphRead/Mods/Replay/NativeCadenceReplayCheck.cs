using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using MphRead.Entities;
using OpenTK.Mathematics;
using MphRead.Mods.Physics;

namespace MphRead.Mods.Replay;

internal static class NativeCadenceReplayCheck
{
    internal static int Run(string directory)
    {
        Headless.Enter();
        string source = Path.Combine(directory, "v134-source.ppdemo");
        using var historical = ReplayWorldCheckpoint.FromBytes(File.ReadAllBytes(Path.Combine(directory, "v134-world.ppwc")));
        static void Require(bool ok, string detail)
        { if (!ok) throw new InvalidDataException(detail); }
        static NativeCadenceCheckpoint State(Scene scene) => new(scene.NativeMovementMode,
            scene.Players.Items.Select(p => p.NativeCadenceJumpPending).ToArray(),
            scene.Players.Items.Select(p => p.NativeCadenceFirePending).ToArray());

        foreach (int phase in new[] { 0, 1 })
        {
            using var linear = new PassiveReplayScene(source, new(256, 192));
            historical.Restore(linear);
            while ((int)(linear.Scene.FrameCount & 1) != phase)
                Require(linear.Step(), "No fixture frame for cadence phase.");
            linear.Scene.NativeMovementMode = NativeMovementMode.DiagnosticCadence60;
            var player = linear.Scene.Players.Items[0];
            if (phase == 1)
            {
                Require(!NativeKernelDiagnostic.BeginBiped(linear.Scene.NativeMovementMode,
                    ref player.NativeCadenceJumpPending, linear.Scene.FrameCount - 1, true, out _),
                    "First-half edge was not buffered.");
                Require(!NativeKernelDiagnostic.BeginBiped(linear.Scene.NativeMovementMode,
                    ref player.NativeCadenceFirePending, linear.Scene.FrameCount - 1, true, out _),
                    "First-half fire edge was not buffered.");
            }
            player.EquipInfo.Weapon = linear.Scene.WeaponRules[(int)BeamType.PowerBeam];
            player.EquipInfo.ChargeLevel = 0;
            Require(BeamProjectileEntity.Spawn(player, player.EquipInfo, player.Position + Vector3.UnitY,
                Vector3.UnitZ, BeamSpawnFlags.NoMuzzle, player.NodeRef, linear.Scene) != BeamResultFlags.NoSpawn,
                "Could not establish native projectile checkpoint fixture.");
            using var checkpoint = ReplayWorldCheckpoint.Capture(linear);
            using var fallback = ReplayWorldCheckpoint.Capture(linear, boundAccessors: false);
            Require(checkpoint.Bytes.SequenceEqual(fallback.Bytes), "Bound cadence capture differs from reflection capture.");
            Require(BitConverter.ToUInt16(checkpoint.Bytes.Slice(4, 2)) == 4, "Native checkpoint must use version 4.");
            byte[] corrupt = checkpoint.Bytes.ToArray();
            corrupt[corrupt.Length - State(linear.Scene).Encode().Length + 1] = 255;
            using (var invalid = ReplayWorldCheckpoint.FromBytes(corrupt))
            using (var unpublished = new PassiveReplayScene(source, new(256, 192)))
            {
                bool rejected = false;
                try { invalid.Restore(unpublished); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "Unknown cadence policy was accepted by the full world reader.");
            }
            using var serialized = ReplayWorldCheckpoint.FromBytes(checkpoint.Bytes);
            using var restored = new PassiveReplayScene(source, new(256, 192));
            using var unrelated = new PassiveReplayScene(source, new(256, 192));
            serialized.Restore(restored);
            Require(restored.Scene.FrameCount == linear.Scene.FrameCount, "Scene phase was not restored.");
            Require(State(restored.Scene).Encode().SequenceEqual(State(linear.Scene).Encode()), "Cadence state was not restored.");
            Require(unrelated.Scene.NativeMovementMode == NativeMovementMode.Legacy60,
                "Restoring one scene changed another scene's policy.");
            Require(ReplayStateHash.Compute(restored.Scene, restored.Session.CurrentFrame)
                == ReplayStateHash.Compute(linear.Scene, linear.Session.CurrentFrame), "Initial restored gameplay differs.");
            // Explicitly verify the queued edge independently of snapshot-owned puppet positions.
            bool restoredPending = restored.Scene.Players.Items[0].NativeCadenceJumpPending;
            bool boundary = NativeKernelDiagnostic.BeginBiped(restored.Scene.NativeMovementMode,
                ref restoredPending, restored.Scene.FrameCount, false, out bool jump);
            Require(boundary == (phase == 1) && jump == (phase == 1), "Restored edge changes next boundary decision.");
            for (int step = 0; step < 20; step++)
            {
                Require(linear.Step() && restored.Step(), "Continuation ended early.");
                if (phase == 1 && step == 0)
                    Require(!linear.Scene.Players.Items[0].NativeCadenceJumpPending
                        && !restored.Scene.Players.Items[0].NativeCadenceJumpPending
                        && !linear.Scene.Players.Items[0].NativeCadenceFirePending
                        && !restored.Scene.Players.Items[0].NativeCadenceFirePending,
                        "The resumed player did not consume the buffered edge.");
                Require(linear.Scene.FrameCount == restored.Scene.FrameCount
                    && State(linear.Scene).Encode().SequenceEqual(State(restored.Scene).Encode()), "Cadence continuation differs.");
                Require(ReplayStateHash.Compute(restored.Scene, restored.Session.CurrentFrame)
                    == ReplayStateHash.Compute(linear.Scene, linear.Session.CurrentFrame), "Gameplay continuation differs.");
                Require(linear.Scene.ReplayPresentationHash(linear.Session.CurrentFrame)
                    == restored.Scene.ReplayPresentationHash(restored.Session.CurrentFrame), "Presentation continuation differs.");
                var linearBeams = linear.Scene.Players.Items[0].EquipInfo.Beams;
                var restoredBeams = restored.Scene.Players.Items[0].EquipInfo.Beams;
                Require(linearBeams.Length == restoredBeams.Length, "Projectile pool size differs after restore.");
                for (int index = 0; index < linearBeams.Length; index++)
                {
                    var a = linearBeams[index]; var b = restoredBeams[index];
                    Require(a.Position == b.Position && a.BackPosition == b.BackPosition && a.Velocity == b.Velocity
                        && a.Age == b.Age && a.Lifespan == b.Lifespan && a.Flags == b.Flags,
                        "Native projectile continuation differs.");
                    foreach (double alpha in new[] { 0d, .25, .5, .75, 1d })
                        Require(a.ModNativePowerDrawPosition(alpha) == b.ModNativePowerDrawPosition(alpha),
                            "Native projectile draw continuation differs.");
                }
                Require(unrelated.Scene.NativeMovementMode == NativeMovementMode.Legacy60,
                    "Stepping a native scene changed the unrelated scene.");
            }
            Console.WriteLine($"[cadence-replay] phase {phase}: v4 state/edge restored; 20 continuation frames and projectile trajectories/draw poses match; scene isolation PASS.");
        }
        using var legacy = new PassiveReplayScene(source, new(256, 192));
        legacy.Scene.NativeMovementMode = NativeMovementMode.DiagnosticCadence60;
        legacy.Scene.Players.Items[0].NativeCadenceJumpPending = true;
        legacy.Scene.Players.Items[0].NativeCadenceFirePending = true;
        historical.Restore(legacy);
        Require(legacy.Scene.NativeMovementMode == NativeMovementMode.Legacy60
            && legacy.Scene.Players.Items.All(p => !p.NativeCadenceJumpPending && !p.NativeCadenceFirePending), "Historical restore did not select legacy policy.");
        using var current = ReplayWorldCheckpoint.Capture(legacy);
        Require(BitConverter.ToUInt16(current.Bytes.Slice(4, 2)) == 3, "Legacy worlds must retain v3 output.");
        using var legacyRestored = new PassiveReplayScene(source, new(256, 192));
        current.Restore(legacyRestored);
        Require(ReplayStateHash.Compute(legacy.Scene, legacy.Session.CurrentFrame)
            == ReplayStateHash.Compute(legacyRestored.Scene, legacyRestored.Session.CurrentFrame), "Current v3 restore differs.");
        var teleported = legacyRestored.Scene.Players.Items[0];
        legacyRestored.Scene.NativeMovementMode = NativeMovementMode.DiagnosticCadence60;
        teleported.NativeCadenceJumpPending = true;
        teleported.NativeCadenceFirePending = true;
        teleported.Teleport(teleported.Position + new OpenTK.Mathematics.Vector3(.25f, 0, 0), teleported.FacingVector, teleported.NodeRef);
        Require(!teleported.NativeCadenceJumpPending && !teleported.NativeCadenceFirePending && teleported.PrevPosition == teleported.Position
            && teleported.ModNativeCadenceDrawOffset(2) == OpenTK.Mathematics.Vector3.Zero,
            "Short teleport retained a pending edge or presentation sweep.");
        Console.WriteLine("[cadence-replay] historical policy reset, current v3 restore and teleport reset PASS.");
        return 0;
    }
}
