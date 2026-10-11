using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Real scene weapon rows supply native charge thresholds; only physical controls author input evidence.
internal static class ShotInputProofTests
{
    private static readonly PropertyInfo AuthorityFrame = typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!;
    private static void Frame(uint frame) => AuthorityFrame.SetValue(null, frame);
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine($"INPUT PROOF PASS {label}");
    }
    private static ShotKey Seed(PlayerEntity player, uint birth)
    {
        NetShotInputProof.Reset(); NetShotSourceJournal.Reset(); Frame(birth);
        var source = new ShotProofSource(0x1FF, 0x1FF, 10000, 10000, false, Hunter.Samus, false, false);
        NetShotSourceJournal.Seed(player, source); NetShotInputProof.Seed(player);
        var key = ShotKey.For(player.SlotIndex, 7);
        Check(NetShotSourceJournal.TryState(key, birth, out var state) && state == source,
            "source journal starts from independent authority seed");
        return key;
    }
    private static IntentPacket Carrier(ShotKey key, uint frame) => new()
    {
        MatchId = key.MatchId, AuthorityEpoch = key.AuthorityEpoch, SlotGeneration = key.Generation,
        LifeId = key.LifeId, Frame = frame, Aim = Vector3.UnitZ, Target = NetTargetIdentity.None
    };
    private static void Observe(ShotKey key, in IntentPacket carrier)
    {
        NetShotSourceJournal.Observe(key.ShooterSlot, carrier);
        NetShotInputProof.Observe(key.ShooterSlot, carrier);
    }
    private static IntentPacket Feed(ShotKey key, uint start, int length, Func<int, byte> sample,
        int missing = -1, bool advanceAuthority = true)
    {
        var sender = new NetShotInputSender();
        IntentPacket carrier = default;
        for (int offset = 0; offset < length; offset++)
        {
            uint frame = unchecked(start + (uint)offset);
            if (advanceAuthority) Frame(frame);
            if (offset != missing) sender.Record(key, frame, sample(offset));
            carrier = Carrier(key, frame);
            sender.Fill(frame, key, ref carrier);
            Observe(key, carrier);
        }
        return carrier;
    }
    private static byte Missile(bool shoot, bool eligible = true)
        => ShotInputHistory.Sample((byte)BeamType.Missile, shoot, false, eligible);
    private static byte Scoped(int offset) => ScopedSample(offset, 3);
    private static byte ScopedSample(int offset, int fireOffset) => ShotInputHistory.Sample(
        (byte)(offset == 0 ? BeamType.PowerBeam : BeamType.Imperialist), offset == fireOffset, offset == 1, true);
    private static FireEvent Fire(ShotKey key, uint source, byte charge = 0,
        BeamType weapon = BeamType.Missile, FireEventKind kind = FireEventKind.ReleaseFire, bool scoped = false)
        => new(key.ShotId, source, source, 0, kind, (byte)weapon, charge,
            scoped ? FireEvent.ScopedStateBit : weapon == BeamType.Missile ? FireEvent.AffinityStateBit : 0,
            SourceLife: key.LifeId);
    private static void Decision(PlayerEntity player, ShotKey key, FireEvent fire, bool charged,
        ShotProofReason expected, string label)
    {
        var actual = NetShotInputProof.Inspect(player, key, fire, charged);
        Check(actual == expected, $"{label}: expected {expected}, got {actual}");
    }

    public static int Run(PlayerEntity player)
    {
        uint previousFrame = NetSession.NetFrame; var previousMode = NetShotProof.Mode;
        try
        {
            NetShotProof.Mode = ShotProofMode.Shadow;
            Check(NetHitClaims.Arbitrating && !player.IsBot && player.SlotIndex != NetSession.LocalSlot,
                "real authority scene uses remote nonbot input validator path");
            Check(player.Hunter == Hunter.Samus && Weapons.GetAffinityBeam(Hunter.Samus) == BeamType.Missile,
                "asset fixture uses native Samus Missile affinity row");
            Check(player.OwningScene.WeaponRules[(int)BeamType.Imperialist].Flags.TestFlag(WeaponFlags.CanZoom),
                "native Imperialist row permits physical zoom toggle");
            var missile = player.OwningScene.WeaponRules[(int)BeamType.Missile + 9];
            int full = missile.FullCharge * 2;
            Check(missile.Flags.TestFlag(WeaponFlags.CanCharge) && full > 8 && full < 255
                && full + 2 < NetShotSourceJournal.ReorderFrames, "native charged Missile fits retained source history");
            byte charge = (byte)full;
            const uint start = 1000;
            uint release = start + (uint)full + 1;

            var key = Seed(player, start);
            var carrier = Feed(key, start, full + 2, i => Missile(i > 0 && i <= full));
            Check(carrier.ShotInputCount == 32 && NetShotSourceJournal.TryState(key, release, out var state)
                && state.Hunter == Hunter.Samus && state.Ammo1 == 10000,
                "repeated raw32 windows and source journal retain native release inputs");
            var fire = Fire(key, release, charge);
            Decision(player, key, fire, true, ShotProofReason.Verified, "contiguous native full-charge hold then physical release");
            Decision(player, key, fire with { Charge = (byte)(charge - 1) }, true,
                ShotProofReason.InvalidCharge, "release cannot report a different charge");
            Decision(player, key, fire with { Kind = FireEventKind.PressFire }, true,
                ShotProofReason.InvalidCharge, "charged tier requires native release event kind");

            key = Seed(player, start);
            Feed(key, start, full + 2, i => Missile(i > 0));
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.InvalidCharge,
                "release event cannot replace an actually held trigger");
            key = Seed(player, start); Frame(release);
            carrier = Carrier(key, release); carrier.ChargeLevel = charge;
            carrier.Buttons = IntentButtons.ZoomedState;
            carrier.HasFireEvents = true; carrier.FireEventCount = 1;
            carrier.FireEvents[0] = Fire(key, release, charge);
            Observe(key, carrier);
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.SourceInputUnavailable,
                "reported charge and buttons alone never author held input proof");
            key = Seed(player, start);
            Feed(key, start, full + 2, i => Missile(i > 0 && i <= full), missing: full - 4);
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.SourceInputUnavailable,
                "missing held sample prevents full-charge reconstruction");
            key = Seed(player, start);
            carrier = Feed(key, start, full + 2, i => Missile(i > 0 && i <= full));
            carrier.ShotInputs[5] ^= ShotInputHistory.ShootBit; Observe(key, carrier);
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.SourceInputUnavailable,
                "conflicting recovered raw sample cannot be accepted as held time");
            key = Seed(player, start);
            Feed(key with { LifeId = (ushort)(key.LifeId + 1) }, start, full + 2,
                i => Missile(i > 0 && i <= full));
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.SourceInputUnavailable,
                "different-life carriers cannot fund current-life charge");
            key = Seed(player, start);
            Feed(key, start, full + 2, i => Missile(i > 0 && i <= full), advanceAuthority: false);
            Frame(start + 1);
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.InvalidCharge,
                "fresh spawn cannot backfill more charged time than authority life elapsed");

            uint delayedBirth = start + (uint)(full / 2);
            key = Seed(player, delayedBirth); Frame(release + (uint)full);
            var delayedSender = new NetShotInputSender();
            for (int offset = 0; offset < full + 2; offset++)
            {
                uint source = start + (uint)offset;
                delayedSender.Record(key, source, Missile(offset > 0 && offset <= full));
                if (source < delayedBirth) continue;
                carrier = Carrier(key, source); delayedSender.Fill(source, key, ref carrier);
                Observe(key, carrier);
            }
            Check(NetSession.NetFrame - delayedBirth >= charge,
                "delayed prelife history reaches charge age guard without extending provable life input");
            Decision(player, key, Fire(key, release, charge), true, ShotProofReason.SourceInputUnavailable,
                "delayed recovered prelife held samples cannot fund a current-life full charge");

            key = Seed(player, start); Feed(key, start, 4, Scoped);
            var scopedFire = Fire(key, start + 3, weapon: BeamType.Imperialist,
                kind: FireEventKind.PressFire, scoped: true);
            Decision(player, key, scopedFire, false, ShotProofReason.Verified,
                "physical zoom rising edge after observed weapon reset proves scope");
            Decision(player, key, scopedFire with { ContinuousPhase = 0 }, false, ShotProofReason.ModifierMismatch,
                "observed scope cannot be hidden by fabricated unscoped event");
            key = Seed(player, start); Feed(key, start, 4, i => ScopedSample(i, -1));
            Decision(player, key, scopedFire, false, ShotProofReason.InvalidCharge,
                "real scope history cannot substitute for a missing physical fire trigger");
            key = Seed(player, start);
            Feed(key, start, 4, i => (byte)(Scoped(i) | (i == 2 ? ShotInputHistory.ShootBit : 0)));
            Decision(player, key, scopedFire, false, ShotProofReason.InvalidCharge,
                "held trigger cannot fabricate a new physical press edge");
            key = Seed(player, start);
            Feed(key, start, 4, i => ShotInputHistory.Sample(
                (byte)(i == 0 ? BeamType.PowerBeam : BeamType.Imperialist), i == 3, false, true));
            Decision(player, key, scopedFire, false, ShotProofReason.ModifierMismatch,
                "scope flag cannot substitute for a physical zoom toggle");
            key = Seed(player, start); Feed(key, start, 4, Scoped, missing: 1);
            Decision(player, key, scopedFire, false, ShotProofReason.SourceInputUnavailable,
                "missing zoom transition leaves scope unavailable");
            key = Seed(player, start);
            Feed(key, start, 4, i => ShotInputHistory.Sample((byte)BeamType.Imperialist, i == 3, i == 0, true));
            Decision(player, key, scopedFire, false, ShotProofReason.SourceInputUnavailable,
                "initial unknown Imperialist state cannot fabricate a scope reset");
            key = Seed(player, start);
            int longRun = NetShotInputProof.Capacity + 80;
            Feed(key, start, longRun, i => ScopedSample(i, longRun - 1));
            Decision(player, key, scopedFire with { SourceFrame = start + (uint)longRun - 1 }, false,
                ShotProofReason.Verified, "observed scope survives raw ring compaction without reported scope state");

            uint wrapStart = uint.MaxValue - (uint)(full / 2);
            key = Seed(player, wrapStart);
            Feed(key, wrapStart, full + 2, i => Missile(i > 0 && i <= full));
            Decision(player, key, Fire(key, unchecked(wrapStart + (uint)full + 1), charge), true,
                ShotProofReason.Verified, "native charge and authority age survive uint frame wrap");
            key = Seed(player, uint.MaxValue - 1); Feed(key, uint.MaxValue - 1, 4, Scoped);
            Decision(player, key, Fire(key, 1, weapon: BeamType.Imperialist, kind: FireEventKind.PressFire, scoped: true),
                false, ShotProofReason.Verified, "physical scope transition survives uint frame wrap");
            Console.WriteLine("shot input proof asset tests passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            Frame(previousFrame); NetShotInputProof.Reset(); NetShotSourceJournal.Reset(); NetShotProof.Mode = previousMode;
        }
    }
}
