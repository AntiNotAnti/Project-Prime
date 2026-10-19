using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Constructed actors and packet-only delivery through production Declare,
// ApplyVerdicts and retirement paths. This is not authenticated UDP evidence.
internal static class ClaimReceiptAccountingTests
{
    private static readonly string[] Reasons = { "Delivered", "TimedOut", "CapacityEvicted", "LifeRetired",
        "OccupantRetired", "MatchRetired", "SessionRetired" };
    private static int _checks;
    private static PlayerEntity _shooter = null!, _victim = null!;
    private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static Array Outbox => (Array)typeof(NetHitClaims).GetField("_outbox", StaticPrivate)!.GetValue(null)!;
    private static T Field<T>(object entry, string name) => (T)entry.GetType().GetField(name)!.GetValue(entry)!;
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("Claim receipts: " + label); _checks++; }
    private static void Frame(uint value) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private static void Tick() => typeof(NetHitClaims).GetMethod("TickOutbox", StaticPrivate)!.Invoke(null, null);
    private static void SetField(int index, string name, object value)
    {
        object entry = Outbox.GetValue(index)!;
        entry.GetType().GetField(name)!.SetValue(entry, value);
        Outbox.SetValue(entry, index);
    }
    private static Dictionary<string, long> Capture()
    {
        object snapshot = NetHitClaims.CaptureReceiptAccounting();
        Check(!string.IsNullOrWhiteSpace((string?)snapshot.GetType().GetProperty("Scope")!.GetValue(snapshot)),
            "accounting declares its observation scope");
        return Reasons.Append("Created").Append("Outstanding").ToDictionary(name => name,
            name => Convert.ToInt64(snapshot.GetType().GetProperty(name)!.GetValue(snapshot)));
    }
    private static void Expect(Dictionary<string, long> before, long created, long outstanding,
        params (string Reason, long Count)[] retired)
    {
        var after = Capture();
        Check(after["Created"] - before["Created"] == created, "Created process-lifetime delta");
        Check(after["Outstanding"] - before["Outstanding"] == outstanding, "Outstanding delta");
        long total = 0;
        foreach (string reason in Reasons)
        {
            long delta = after[reason] - before[reason];
            Check(delta == retired.Where(row => row.Reason == reason).Sum(row => row.Count), reason + " delta is exact");
            total += delta;
        }
        Check(created == total + outstanding, "every created receipt remains outstanding or retires exactly once");
    }
    private static Dictionary<string, long> Prepare()
    {
        HealthShotTests.Session(); NetHitClaims.Reset(); NetHitPrediction.Reset();
        NetHitClaims.Enabled = true;
        _shooter = HealthShotTests.Player(0); _victim = HealthShotTests.Player(1);
        PlayerEntity._players[0] = _shooter; PlayerEntity._players[1] = _victim;
        Frame(100);
        var before = Capture();
        Check(before["Outstanding"] == 0, "fixture starts with an empty bounded outbox");
        return before;
    }
    private static HitClaimPacket Declare(uint shot = 19, uint component = 1, bool direct = true, uint phase = 0)
    {
        var key = ShotKey.For(0, shot);
        ushort id = NetHitClaims.Declare(_victim, _shooter, phase != 0 ? BeamType.ShockCoil : BeamType.Missile,
            12, 0, false, new(10, 2, 3), 90, Vector3.UnitZ, continuousPhase: phase,
            direct: direct, shotId: shot, shotKey: key, componentId: component);
        Check(id != 0, "production Declare creates a named receipt");
        foreach (object entry in Outbox)
        {
            if (!Field<bool>(entry, "Live") || Field<ushort>(entry, "Id") != id) continue;
            Check(Field<byte>(entry, "ShooterSlot") == key.ShooterSlot, "stored shooter slot is explicit");
            var result = new HitClaimPacket {
                ClaimId = id, AuthorityEpoch = Field<ulong>(entry, "AuthorityEpoch"), MatchId = Field<ushort>(entry, "MatchId"),
                ShooterGeneration = Field<ushort>(entry, "ShooterGeneration"), ShooterLifeId = Field<ushort>(entry, "ShooterLifeId"),
                VictimSlot = Field<byte>(entry, "VictimSlot"), VictimGeneration = Field<ushort>(entry, "VictimGeneration"),
                VictimLifeId = Field<ushort>(entry, "VictimLifeId"), ShotId = Field<uint>(entry, "ShotId"),
                ComponentId = Field<uint>(entry, "ComponentId"), Flags = Field<byte>(entry, "Flags"),
                Frame = Field<uint>(entry, "Frame"), Beam = Field<byte>(entry, "Beam") };
            Check(NetShotProof.Key(0, result) == key && result.ComponentId == component,
                "actual outbox retains the complete supplied shot lifecycle and component");
            return result;
        }
        throw new InvalidOperationException("Declared receipt is missing from outbox.");
    }
    private static CombatAckEntry Ack(HitClaimPacket claim) => new() {
        ClaimId = claim.ClaimId, ShotId = claim.ShotId, VictimSlot = claim.VictimSlot,
        VictimGeneration = claim.VictimGeneration, VictimLife = claim.VictimLifeId,
        Result = (byte)CombatAckResult.AlreadyResolved, DamageApplied = 12, HealthAfter = 87,
        DamageSequence = (uint)NetDamage.Sequence(1) + 1, Flags = CombatAckFlags.OutcomePresent };
    private static void Deliver(HitClaimPacket claim, CombatAckEntry? outcome = null)
    {
        byte[] bytes = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
        HitVerdictPacket.Write(bytes, new[] { outcome ?? Ack(claim) }, claim.MatchId, claim.AuthorityEpoch,
            claim.ShooterGeneration, claim.ShooterLifeId);
        NetHitClaims.ApplyVerdicts(bytes);
    }
    private static void DeliveredOnce()
    {
        var before = Prepare(); var claim = Declare();
        Expect(before, 1, 1);
        var wrong = Ack(claim); wrong.ShotId++;
        Deliver(claim, wrong); Expect(before, 1, 1);
        wrong = Ack(claim); wrong.VictimLife++;
        Deliver(claim, wrong); Expect(before, 1, 1);
        var wrongStream = claim; wrongStream.AuthorityEpoch++;
        Deliver(wrongStream); Expect(before, 1, 1);
        wrongStream = claim; wrongStream.MatchId++;
        Deliver(wrongStream); Expect(before, 1, 1);
        var wrongOwner = claim; wrongOwner.ShooterGeneration++;
        Deliver(wrongOwner); Expect(before, 1, 1);
        Deliver(claim); Deliver(claim);
        Expect(before, 1, 0, ("Delivered", 1));
    }
    private static void TimeoutOnce()
    {
        var before = Prepare(); var claim = Declare();
        SetField(0, "Age", NetHitClaims.DeliveryMaxAge - 1);
        Tick(); Expect(before, 1, 1);
        Tick(); Tick(); Deliver(claim);
        Expect(before, 1, 0, ("TimedOut", 1));
    }
    private static void CapacityEviction()
    {
        var before = Prepare(); int capacity = Outbox.Length;
        var oldest = Declare(1);
        SetField(0, "Age", 100);
        for (int i = 1; i <= capacity; i++) Declare((uint)(i + 1));
        Expect(before, capacity + 1, capacity, ("CapacityEvicted", 1));
        Deliver(oldest);
        Expect(before, capacity + 1, capacity, ("CapacityEvicted", 1));
        NetHitClaims.Reset(); NetHitClaims.Reset();
        Expect(before, capacity + 1, 0, ("CapacityEvicted", 1), ("SessionRetired", capacity));
    }
    private static void LifecycleRetirement(bool shooter)
    {
        var before = Prepare(); var claim = Declare(); int slot = shooter ? 0 : 1;
        var next = HealthShotTests.State((ushort)(NetPlayerLifecycle.Get(slot) + 1), 87);
        next.SlotIndex = (byte)slot; next.SlotGeneration = NetPlayerLifecycle.Generation(slot);
        Check(NetPlayerLifecycle.AcceptState(next, 101), "actual lifecycle tracker accepts constructed next-life state");
        NetHitClaims.ForgetSlot(slot, preserveFlights: true);
        NetHitClaims.ForgetSlot(slot, preserveFlights: true);
        var body = HealthShotTests.Player(slot, 87); PlayerEntity._players[slot] = body;
        NetSession.RemoteStates[slot] = next;
        uint sequence = NetDamage.Sequence(slot);
        var lethal = Ack(claim); lethal.Flags |= CombatAckFlags.Lethal; lethal.HealthAfter = 0;
        Deliver(claim, lethal); Deliver(claim, lethal);
        Check(body.Health == 87 && NetDamage.Sequence(slot) == sequence,
            "late old-life kill ACK cannot mutate replacement body or damage sequence");
        Expect(before, 1, 0, ("LifeRetired", 1));
    }
    private static void OtherRetirements()
    {
        var before = Prepare(); var claim = Declare();
        NetPlayerLifecycle.SetOccupant(1, (ushort)(NetPlayerLifecycle.Generation(1) + 1));
        NetHitClaims.ForgetSlot(1); Deliver(claim);
        Expect(before, 1, 0, ("OccupantRetired", 1));
        before = Prepare(); claim = Declare();
        NetHitClaims.ForgetPending(); NetHitClaims.ForgetPending(); Deliver(claim);
        Expect(before, 1, 0, ("MatchRetired", 1));
        before = Prepare(); claim = Declare();
        NetHitClaims.Reset(); NetHitClaims.Reset(); Deliver(claim);
        Expect(before, 1, 0, ("SessionRetired", 1));
    }
    private static void ComponentAndPhaseIsolation()
    {
        var before = Prepare();
        typeof(NetHitClaims).GetField("_nextId", StaticPrivate)!.SetValue(null, ushort.MaxValue);
        var direct = Declare(uint.MaxValue, 1);
        var splash = Declare(uint.MaxValue, 1, direct: false);
        var child = Declare(uint.MaxValue, 2);
        var wrapped = Declare(1, 1);
        Check(direct.ClaimId == ushort.MaxValue && splash.ClaimId == 1 && child.ClaimId == 2,
            "claim ID wraps past zero without aliasing active receipts");
        Check(NetShotProof.PaymentKey(0, direct) != NetShotProof.PaymentKey(0, splash)
            && NetShotProof.PaymentKey(0, direct) != NetShotProof.PaymentKey(0, child),
            "canonical payment identity distinguishes direct, splash and child components");
        Check(NetShotProof.Key(0, direct) != NetShotProof.Key(0, wrapped),
            "uint maximum and wrapped nonzero ShotId remain distinct exact source identities");
        Deliver(wrapped); Expect(before, 4, 3, ("Delivered", 1));
        Deliver(splash); Deliver(splash); Expect(before, 4, 2, ("Delivered", 2));
        Deliver(child); Deliver(direct); Expect(before, 4, 0, ("Delivered", 4));
        before = Prepare();
        var first = Declare(1, phase: uint.MaxValue);
        var second = Declare(1, phase: 1);
        Check(first.Frame == uint.MaxValue && second.Frame == 1
            && (first.Flags & HitClaimPacket.FlagContinuousTick) != 0
            && (second.Flags & HitClaimPacket.FlagContinuousTick) != 0,
            "continuous phase remains distinct from shared emitted ShotId across uint wrap");
        var firstKey = NetShotProof.PaymentKey(0, first);
        var secondKey = NetShotProof.PaymentKey(0, second);
        Check(firstKey.Phase == uint.MaxValue && secondKey.Phase == 1 && firstKey != secondKey
            && (firstKey with { Phase = secondKey.Phase }) == secondKey,
            "same emitted shot, component and victim differ only by canonical continuous phase");
        var ordinaryFirst = first; var ordinarySecond = second;
        ordinaryFirst.Flags = (byte)(ordinaryFirst.Flags & ~HitClaimPacket.FlagContinuousTick);
        ordinarySecond.Flags = (byte)(ordinarySecond.Flags & ~HitClaimPacket.FlagContinuousTick);
        Check(NetShotProof.PaymentKey(0, ordinaryFirst) == NetShotProof.PaymentKey(0, ordinarySecond)
            && NetShotProof.PaymentKey(0, ordinaryFirst).Phase == 0,
            "ordinary frame metadata cannot split a non-continuous payment identity");
        Deliver(first); Deliver(first); Expect(before, 2, 1, ("Delivered", 1));
        Deliver(second); Expect(before, 2, 0, ("Delivered", 2));
    }
    internal static int Run()
    {
        var saved = (PlayerEntity[])PlayerEntity._players.Clone();
        bool enabled = NetHitClaims.Enabled;
        try
        {
            DeliveredOnce(); TimeoutOnce(); CapacityEviction();
            LifecycleRetirement(shooter: false); LifecycleRetirement(shooter: true);
            OtherRetirements(); ComponentAndPhaseIsolation();
            Console.WriteLine($"PASS constructed packet-only receipt accounting ({_checks} checks); no UDP, collision or payment qualification.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally { NetSession.Stop(); NetHitClaims.Enabled = enabled; Array.Copy(saved, PlayerEntity._players, saved.Length); }
    }
}
