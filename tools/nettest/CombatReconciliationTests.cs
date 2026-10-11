using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class CombatReconciliationTests
{
    private static int _checks;
    private static PlayerEntity _victim = null!;
    private static PlayerEntity _shooter = null!;
    private static PlayerEntity[] Players => PlayerEntity._players;

    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    public static int Run()
    {
        var savedPlayers = (PlayerEntity[])Players.Clone();
        try
        {
            UnrelatedEventCannotSettlePrediction();
            OutOfOrderAcknowledgements();
            AckFirstSuppressesOnlyExactEvent();
            EventFirstStillRequiresExactAck();
            DeadVictimRejectRetiresDebit();
            WrongIdentityDoesNotSettle();
            EventSequenceWrap();
            TimeoutReleasesHealthFloor();
            NewPredictionsCannotExtendOldDebit();
            DisabledPredictionStillSettlesReceipt();
            LethalPredictionAndAckWaitForAuthorityDeath();
            Console.WriteLine($"PASS: {_checks} combat reconciliation assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            NetSession.Stop();
            Array.Copy(savedPlayers, Players, Players.Length);
        }
    }

    private static void Prepare(ushort eventId = 0)
    {
        HealthShotTests.Session();
        NetHitPrediction.Enabled = true;
        _victim = HealthShotTests.Player(1);
        _shooter = HealthShotTests.Player(0);
        Players[0] = _shooter; Players[1] = _victim;
        var state = HealthShotTests.State(); state.DamageEventId = eventId;
        NetSession.RemoteStates[1] = state;
    }

    private static HitClaimPacket Predict(uint shotId, uint damage = 20)
    {
        DamageFlags flags = 0;
        NetHitPrediction.NoteHit(_victim, _shooter, ref flags, ref damage, BeamType.Missile,
            launchFrame: 1, shotId: shotId, shotKey: ShotKey.For(0, shotId));
        _victim.Health = NetHitPrediction.HealthFor(1, NetSession.RemoteStates[1].Health);
        byte[] bytes = new byte[4096];
        Check(NetHitClaims.Compose(bytes) > 0, "prediction emits a claim");
        int count = bytes[0];
        for (int i = 0; i < count; i++)
        {
            var claim = HitClaimPacket.Read(bytes.AsSpan(1 + i * HitClaimPacket.Size));
            if (claim.ShotId == shotId) return claim;
        }
        throw new InvalidOperationException("composed claims omit requested shot");
    }

    private static CombatAckEntry Ack(HitClaimPacket claim, ushort sequence = 1,
        ushort health = 79, CombatAckResult result = CombatAckResult.AlreadyResolved) => new()
    {
        ClaimId = claim.ClaimId, ShotId = claim.ShotId, VictimSlot = claim.VictimSlot,
        VictimGeneration = claim.VictimGeneration, VictimLife = claim.VictimLifeId,
        Result = (byte)result, DamageSequence = sequence, DamageApplied = 20,
        HealthAfter = health, Flags = CombatAckFlags.OutcomePresent
    };

    private static void Deliver(CombatAckEntry ack)
    {
        byte[] bytes = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
        HitVerdictPacket.Write(bytes, new[] { ack }, 51, 4, 9, 2);
        NetHitClaims.ApplyVerdicts(bytes);
    }

    private static bool Event(ushort sequence, ushort generation = 10, ushort life = 7)
    {
        MethodInfo? method = typeof(NetHitPrediction).GetMethod("ConfirmEvent",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(int), typeof(ushort), typeof(ushort), typeof(ushort) }, null);
        // This fallback makes the old FIFO implementation fail behaviorally before
        // the production API lands, while keeping the test project buildable.
        return method == null ? NetHitPrediction.Confirm(1)
            : (bool)method.Invoke(null, new object[] { 1, generation, life, sequence })!;
    }

    private static void UnrelatedEventCannotSettlePrediction()
    {
        Prepare(); var claim = Predict(101);
        long confirmed = NetHitPrediction.Confirmed;
        Check(!Event(8), "unrelated authoritative event must retain its feedback");
        Check(NetHitPrediction.Confirmed == confirmed && NetHitPrediction.HealthFor(1, 99) == 79,
            "unrelated event cannot retire a pending claim or debit");
        Deliver(Ack(claim, result: CombatAckResult.RejectedGeometry));
        Check(NetHitPrediction.Confirmed == confirmed && NetHitPrediction.Denied == 1
            && NetHitPrediction.HealthFor(1, 99) == 99, "exact reject releases its debit");
    }

    private static void OutOfOrderAcknowledgements()
    {
        Prepare(); var first = Predict(201); var second = Predict(202);
        Check(_victim.Health == 59, "two independent immediate prediction debits");
        Deliver(Ack(second, 2, 79));
        Check(NetHitPrediction.Confirmed == 1 && NetHitPrediction.HealthFor(1, 99) == 59,
            "second ACK preserves the first independent debit");
        Check(!Event(1) && Event(2), "out-of-order ACK maps only its own authoritative event");
        Deliver(Ack(first, result: CombatAckResult.RejectedDamage));
        Check(NetHitPrediction.Confirmed == 1 && NetHitPrediction.Denied == 1
            && NetHitPrediction.HealthFor(1, 99) == 79, "first rejection releases only the first debit");
    }

    private static void AckFirstSuppressesOnlyExactEvent()
    {
        Prepare(); var claim = Predict(301); var ack = Ack(claim, 11);
        Deliver(ack);
        Check(!Event(10), "ACK credit cannot absorb an unrelated event");
        Check(Event(11), "ACK names the exact event whose feedback was predicted");
        Check(!Event(12), "exact receipt cannot suppress a subsequent unrelated event");
        long confirmed = NetHitPrediction.Confirmed;
        Deliver(ack);
        Check(NetHitPrediction.Confirmed == confirmed && NetHitPrediction.HealthFor(1, 99) == 79,
            "duplicate ACK cannot settle twice or alter health twice");
    }

    private static void EventFirstStillRequiresExactAck()
    {
        Prepare(); var claim = Predict(401);
        Check(!Event(4) && NetHitPrediction.Confirmed == 0,
            "event arriving before identity receipt cannot FIFO-confirm a claim");
        var state = NetSession.RemoteStates[1]; state.DamageEventId = 4; state.Health = 79;
        NetSession.RemoteStates[1] = state;
        Deliver(Ack(claim, 4));
        Check(NetHitPrediction.Confirmed == 1 && NetHitPrediction.HealthFor(1, 79) == 79,
            "later exact ACK settles without double subtracting canonical health");
    }

    private static void DeadVictimRejectRetiresDebit()
    {
        Prepare(); var claim = Predict(501); _victim.Health = 0;
        Deliver(Ack(claim, result: CombatAckResult.RejectedDeadVictim));
        Check(_victim.Health == 0 && NetHitPrediction.Denied == 1,
            "reject settles while victim is dead and cannot resurrect it");
        Check(NetHitPrediction.HealthFor(1, 99) == 99,
            "dead-victim reject still retires the presentation debit");
    }

    private static void WrongIdentityDoesNotSettle()
    {
        Prepare(); var claim = Predict(601); var ack = Ack(claim);
        ack.ShotId++; Deliver(ack);
        Check(NetHitPrediction.Confirmed == 0 && NetHitPrediction.HealthFor(1, 99) == 79,
            "wrong shot ID cannot settle prediction");
        ack = Ack(claim); ack.VictimLife++; Deliver(ack);
        Check(NetHitPrediction.Confirmed == 0, "wrong victim life cannot settle prediction");
        Deliver(Ack(claim));
        Check(!Event(1, generation: 11) && !Event(1, life: 8),
            "wrong event occupant or life cannot consume exact feedback receipt");
        Check(Event(1), "valid identity remains available after invalid event attempts");
    }

    private static void EventSequenceWrap()
    {
        Prepare(ushort.MaxValue - 1); var first = Predict(701);
        Deliver(Ack(first, ushort.MaxValue, 79));
        Check(Event(ushort.MaxValue), "exact feedback identity works before sequence wrap");
        var second = Predict(702); Deliver(Ack(second, 1, 59));
        Check(!Event(2) && Event(1), "wrapped event sequence retains exact identity");
        Check(NetHitPrediction.HealthFor(1, 99) == 59, "wrapped newer ACK advances presentation health");
    }

    private static void TimeoutReleasesHealthFloor()
    {
        Prepare(); Predict(801);
        uint now = NetSession.NetFrame;
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now + 300);
        NetHitPrediction.Tick();
        Check(NetHitPrediction.HealthFor(1, 99) == 99 && NetHitPrediction.Denied == 1,
            "missing receipt cannot hold prediction debit or health floor indefinitely");
    }

    private static void LethalPredictionAndAckWaitForAuthorityDeath()
    {
        Prepare(); var claim = Predict(901, 150);
        Check(_victim.Health == 1, "lethal remote prediction clamps immediate health to one");
        var ack = Ack(claim, 1, 0); ack.DamageApplied = 99;
        ack.Flags |= CombatAckFlags.Lethal; Deliver(ack);
        Check(_victim.Health == 1 && NetHitPrediction.HealthFor(1, 99) == 1,
            "accepted lethal ACK cannot own remote death");
        Check(NetSession.RemoteStates[1].Health == 99 && NetDamage.Sequence(1) == 0,
            "presentation reconciliation does not mutate canonical snapshot or damage ledger");
        var state = NetSession.RemoteStates[1]; state.Health = 0; state.DamageEventId = 1;
        NetSession.RemoteStates[1] = state;
        Check(NetHitPrediction.HealthFor(1, 0) == 0, "authority death remains decisive");
    }

    private static void NewPredictionsCannotExtendOldDebit()
    {
        Prepare(); var old = Predict(850, 60); Predict(851, 10);
        Check(_victim.Health == 29, "old and new predictions initially share independent debits");
        Deliver(Ack(old, result: CombatAckResult.RejectedGeometry));
        Check(NetHitPrediction.HealthFor(1, 99) == 89,
            "new prediction cannot preserve the rejected older prediction's low floor");
        Predict(852, 10);
        Check(NetHitPrediction.HealthFor(1, 99) == 79,
            "continued prediction retains only its current independent debits");

        Prepare(); Predict(860, 60);
        typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, NetSession.NetFrame + 300);
        Predict(861, 10); NetHitPrediction.Tick();
        Check(NetHitPrediction.HealthFor(1, 99) == 89 && NetHitPrediction.Denied == 1,
            "fresh predictions cannot extend an expired old debit or its health floor");
    }

    private static void DisabledPredictionStillSettlesReceipt()
    {
        Prepare(); var claim = Predict(870);
        try
        {
            NetHitPrediction.Enabled = false;
            Deliver(Ack(claim, result: CombatAckResult.RejectedDamage));
            Check(NetHitPrediction.Denied == 1, "receipt settles while presentation prediction is disabled");
        }
        finally { NetHitPrediction.Enabled = true; }
        Check(NetHitPrediction.HealthFor(1, 99) == 99,
            "reenabling prediction cannot restore a debit settled while disabled");
    }
}
