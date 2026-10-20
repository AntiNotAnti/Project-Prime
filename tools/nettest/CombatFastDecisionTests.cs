using System;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Constructed detached facts and diagnostic Request/Terminal hooks only. No
// accepted source, collision, native damage, UDP, payment or parity is certified.
internal static class CombatFastDecisionTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Fast decision: " + label);
        _checks++;
    }
    private static HitClaimPacket Claim(uint shot = 19) => new()
    {
        AuthorityEpoch = 11, MatchId = 7, ShooterGeneration = 2, ShooterLifeId = 3,
        ShotId = shot, ComponentId = 1, ClaimId = (ushort)shot,
        VictimSlot = 1, VictimGeneration = 4, VictimLifeId = 5,
        Beam = (byte)BeamType.Imperialist, Flags = HitClaimPacket.FlagDirect,
        Frame = 99, AckFrame = 90, LaunchFrame = 90, Damage = 12
    };
    private static ShotProofDecision Proof(ShotProofReason reason = ShotProofReason.Verified,
        ShotProofReason source = ShotProofReason.Verified, ShotProofReason geometry = ShotProofReason.Verified)
        => new(ShotProofStatus.Unavailable, reason, ShotProofFamily.Straight, source, geometry);
    private static void ReceiptExpected(HitClaimPacket claim, FastPaymentFact paid, ShotProofDecision proof,
        FastDecisionState state, FastDecisionReason reason, string label)
    {
        var original = claim;
        var actual = CombatFastDecisionFunction.Receipt(claim, paid, proof);
        Check(actual.State == state && actual.Reason == reason && !actual.WouldPay
            && !actual.WouldReject && claim.Equals(original), label);
    }
    private static void Receipts()
    {
        var claim = Claim(); var proof = Proof();
        var outcome = new CombatAckEntry { ShotId = claim.ShotId, ClaimId = claim.ClaimId,
            VictimSlot = claim.VictimSlot, VictimGeneration = claim.VictimGeneration,
            VictimLife = claim.VictimLifeId, Result = (byte)CombatAckResult.Applied,
            DamageApplied = 12, HealthAfter = 88, DamageSequence = 31, Flags = CombatAckFlags.OutcomePresent };
        var paid = new FastPaymentFact(FastPaymentKnowledge.Paid, true, outcome);
        foreach (var weapon in new[] { BeamType.PowerBeam, BeamType.Imperialist })
        {
            claim.Beam = (byte)weapon;
            var actual = CombatFastDecisionFunction.Receipt(claim, paid, proof);
            Check(actual.State == FastDecisionState.VerifiedPaid && actual.Eligible
                && !actual.WouldPay && !actual.WouldReject && actual.Paid.Equals(outcome),
                "qualified paid duplicate retains immutable outcome and never predicts another payment/" + weapon);
        }
        var head = claim; head.Flags |= HitClaimPacket.FlagHeadshot;
        ReceiptExpected(head, paid, proof, FastDecisionState.LegacyRequired, FastDecisionReason.HeadMismatch,
            "body payment cannot be elevated to a claimed head payment");
        var headOutcome = outcome; headOutcome.Flags |= CombatAckFlags.Headshot;
        var headPaid = paid with { Outcome = headOutcome };
        Check(CombatFastDecisionFunction.Receipt(head, headPaid, proof).Paid.Equals(headOutcome),
            "matching paid head fact is retained exactly");
        ReceiptExpected(claim, headPaid, proof, FastDecisionState.LegacyRequired, FastDecisionReason.HeadMismatch,
            "head payment cannot silently become a body receipt");
        foreach (var invalid in new[] { claim with { ShotId = 0 }, claim with { ComponentId = 0 }, claim with { VictimSlot = 8 } })
            ReceiptExpected(invalid, paid, proof, FastDecisionState.LegacyRequired, FastDecisionReason.InvalidIdentity,
                "missing detached claim identity cannot become eligible");
        foreach (var unsupported in new[] { claim with { Beam = (byte)BeamType.Missile },
            claim with { ComponentId = 2 }, claim with { Flags = 0 },
            claim with { Flags = HitClaimPacket.FlagDirect | HitClaimPacket.FlagHalfturret },
            claim with { Flags = HitClaimPacket.FlagDirect | HitClaimPacket.FlagContinuousTick } })
            ReceiptExpected(unsupported, paid, proof, FastDecisionState.LegacyRequired, FastDecisionReason.FamilyUnsupported,
                "unsupported family cannot borrow a paid narrow-family fact");
        ReceiptExpected(claim, new(FastPaymentKnowledge.Reserved, true, default), proof,
            FastDecisionState.PendingDependencies, FastDecisionReason.ReservationPending, "reservation is not payment");
        ReceiptExpected(claim, paid with { WeaponMatches = false }, proof,
            FastDecisionState.LegacyRequired, FastDecisionReason.OrderingDeferred, "wrong-weapon paid fact cannot certify a duplicate");
        foreach (var reason in new[] { ShotProofReason.UnknownShot, ShotProofReason.ShotNotFired })
            ReceiptExpected(claim, default, Proof(reason), FastDecisionState.PendingDependencies,
                FastDecisionReason.MissingSource, "missing or unemitted source remains pending");
        ReceiptExpected(claim, default, Proof(ShotProofReason.WrongHeadshot), FastDecisionState.LegacyRequired,
            FastDecisionReason.HeadMismatch, "unpaid head mismatch retains legacy handling");
        foreach (var reason in new[] { ShotProofReason.BlockedMuzzle, ShotProofReason.BlockedWorld })
            ReceiptExpected(claim, default, Proof(reason), FastDecisionState.LegacyRequired,
                FastDecisionReason.WallMismatch, "known wall mismatch cannot authorize a fresh claim");
        ReceiptExpected(claim, default, Proof(source: ShotProofReason.Ammunition), FastDecisionState.LegacyRequired,
            FastDecisionReason.SourceUnavailable, "unverified source resources retain legacy handling");
        ReceiptExpected(claim, default, Proof(geometry: ShotProofReason.HistoryUnavailable), FastDecisionState.LegacyRequired,
            FastDecisionReason.GeometryUnavailable, "missing world history remains unavailable");
        ReceiptExpected(claim, default, proof, FastDecisionState.LegacyRequired,
            FastDecisionReason.OrderingDeferred, "even complete detached claim proof cannot bypass native ordering");
    }
    private static void NativeFacts()
    {
        var available = new FastNativeDamageResult(FastDamageAvailability.Available,
            FastDamageReason.NativeDamage, 12, 9, true, false);
        var actual = CombatFastDecisionFunction.Native(default, true, true, available);
        Check(actual.State == FastDecisionState.VerifiedNative && actual.Reason == FastDecisionReason.NativeDamage
            && actual.Eligible && actual.WouldPay && !actual.WouldReject && actual.Damage == available,
            "separately supplied complete native facts preserve the nominal/debit/lethal decision");
        var blocked = new FastNativeDamageResult(FastDamageAvailability.Blocked, FastDamageReason.SpawnProtected);
        actual = CombatFastDecisionFunction.Native(default, true, true, blocked);
        Check(actual.State == FastDecisionState.Reject && actual.WouldReject && !actual.WouldPay
            && !actual.Eligible && actual.Reason == FastDecisionReason.NativeBlocked && actual.Damage == blocked,
            "verified native block is a reject with no predicted payment");
        actual = CombatFastDecisionFunction.Native(default, true, true, default);
        Check(actual.State == FastDecisionState.LegacyRequired && actual.Reason == FastDecisionReason.DamageUnavailable
            && !actual.WouldPay && !actual.WouldReject, "unknown native damage cannot become a free or blocked hit");
        actual = CombatFastDecisionFunction.Native(default, false, true, available);
        Check(actual.State == FastDecisionState.PendingDependencies && actual.Reason == FastDecisionReason.MissingSource
            && !actual.WouldPay, "available arithmetic cannot replace source proof");
        actual = CombatFastDecisionFunction.Native(default, true, false, available);
        Check(actual.State == FastDecisionState.LegacyRequired && actual.Reason == FastDecisionReason.GeometryUnavailable
            && !actual.WouldPay, "available arithmetic cannot replace collision proof");
        foreach (var knowledge in new[] { FastPaymentKnowledge.Reserved, FastPaymentKnowledge.Paid })
        {
            actual = CombatFastDecisionFunction.Native(new(knowledge, true, default), true, true, available);
            Check(actual.State == FastDecisionState.LegacyRequired && actual.Reason == FastDecisionReason.ReservationPending
                && !actual.Eligible && !actual.WouldPay && !actual.WouldReject,
                "reserved or already paid native component cannot predict a second payment/" + knowledge);
        }
    }
    private static void PaidReadyLifecycle()
    {
        // Explicit constructed ledger outcome, not native HP damage or admission.
        var claim = Claim(73); var key = NetShotProof.PaymentKey(0, claim);
        var outcome = new CombatAckEntry { ShotId = claim.ShotId, ClaimId = claim.ClaimId,
            VictimSlot = claim.VictimSlot, VictimGeneration = claim.VictimGeneration,
            VictimLife = claim.VictimLifeId, Result = (byte)CombatAckResult.Applied,
            DamageApplied = 12, HealthAfter = 88, DamageSequence = 31, Flags = CombatAckFlags.OutcomePresent };
        using (var reservation = NetCombatPayments.Begin(key, claim.Beam))
        {
            Check(reservation.Allowed, "explicit constructed ledger fixture reserves its complete payment key");
            reservation.Commit(outcome);
        }
        Check(NetCombatPayments.TryGet(key, claim.Beam, out var retained) && retained.Equals(outcome),
            "production ledger retains the declared constructed outcome");
        long payments = NetCombatPayments.Payments;
        using (NetCombatFastPath.Request(0, claim)) { }
        NetCombatFastPath.ClaimReady(0, claim);
        var rows = NetCombatFastPath.CaptureRows();
        var arrival = rows.Single(row => row.Key == key && row.Stage == FastProbeStage.ClaimArrival);
        var ready = rows.Single(row => row.Key == key && row.Stage == FastProbeStage.ClaimReady);
        Check(!arrival.Decision.Eligible && !arrival.Decision.WouldPay
            && arrival.Decision.Reason == FastDecisionReason.OrderingDeferred,
            "raw arrival cannot use even a paid outcome before its ready seam");
        Check(ready.Decision.State == FastDecisionState.VerifiedPaid && ready.Decision.Paid.Equals(outcome)
            && !ready.Decision.WouldPay && !ready.Compared,
            "declared ready seam observes exact constructed paid receipt without predicting another payment");
        var answer = outcome; answer.Result = (byte)CombatAckResult.AlreadyResolved;
        NetCombatFastPath.Terminal(key, answer); NetCombatFastPath.Terminal(key, answer);
        ready = NetCombatFastPath.CaptureRows().Single(row => row.Key == key && row.Stage == FastProbeStage.ClaimReady);
        Check(ready.Compared && ready.Agrees && ready.Terminals == 2 && ready.ActualDebit == 12
            && ready.ActualHealth == 88 && NetCombatPayments.Payments == payments,
            "matching repeated terminal joins compare the ready row without a new ledger payment");
        var forged = answer; forged.DamageSequence++;
        NetCombatFastPath.Terminal(key, forged); NetCombatFastPath.Terminal(key, answer);
        ready = NetCombatFastPath.CaptureRows().Single(row => row.Key == key && row.Stage == FastProbeStage.ClaimReady);
        Check(ready.Compared && !ready.Agrees && ready.Terminals == 4
            && NetCombatPayments.TryGet(key, claim.Beam, out retained) && retained.Equals(outcome),
            "a mismatched terminal remains disagreement and cannot mutate the retained paid outcome");
        Check(!NetCombatFastPath.CaptureRows().Single(row => row.Key == key && row.Stage == FastProbeStage.ClaimArrival).Compared,
            "terminal ready comparison cannot fabricate arrival parity");
        NetCombatPayments.Reset(); NetCombatFastPath.ResetEvidenceForTests();
    }
    private static void EvidenceLifecycle()
    {
        NetSession.StartServerAuthority(_ => { }, () => { });
        NetHitClaims.Enabled = true; NetCombatFastPath.ResetEvidenceForTests();
        NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 7, AuthorityEpoch = 11 }, false);
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 11;
        roster.Revision = 1; roster.Count = 2; roster.Slots[0] = 0; roster.Generations[0] = 2;
        roster.Slots[1] = 1; roster.Generations[1] = 4; NetSession.ApplyRoster(roster);
        NetCombatFastPath.Mode = CombatFastMode.Shadow;
        PaidReadyLifecycle();
        var outer = Claim(); var key = NetShotProof.PaymentKey(0, outer);
        NetCombatFastPath.Mode = CombatFastMode.Off;
        using (NetCombatFastPath.Request(0, outer))
            Check(!NetCombatFastPath.TryRequest(0, outer.ClaimId, out _), "Off cannot expose a diagnostic request");
        NetCombatFastPath.Terminal(key, default); NetCombatFastPath.Payment(key, default);
        Check(NetCombatFastPath.CaptureRows().Length == 0 && NetCombatFastPath.UnjoinedTerminal == 0,
            "Off retains no request/payment/terminal observations");
        NetCombatFastPath.Mode = CombatFastMode.Shadow;
        using (NetCombatFastPath.Request(0, outer))
        {
            Check(NetCombatFastPath.TryRequest(0, outer.ClaimId, out var current) && current == key,
                "Shadow scope carries its exact constructed payment key");
            var inner = outer; inner.VictimLifeId++;
            using (NetCombatFastPath.Request(0, inner))
                Check(NetCombatFastPath.TryRequest(0, inner.ClaimId, out current)
                    && current == NetShotProof.PaymentKey(0, inner) && current != key,
                    "same numeric claim ID cannot erase distinct victim-life ownership");
            Check(NetCombatFastPath.TryRequest(0, outer.ClaimId, out current) && current == key,
                "nested request disposal restores outer identity");
        }
        Check(!NetCombatFastPath.TryRequest(0, outer.ClaimId, out _), "request identity does not leak outside scope");
        using (NetCombatFastPath.Request(0, outer)) { }
        var rows = NetCombatFastPath.CaptureRows();
        Check(rows.Length == 2 && rows.Single(row => row.Key == key).Attempts == 2,
            "retry increments exact row attempts while a distinct life remains independent");
        rows[0] = default;
        Check(NetCombatFastPath.CaptureRows().All(row => row.Key.Shot.ShotId != 0), "row capture is detached from retained evidence");
        NetCombatFastPath.Terminal(key with { Component = 99 }, default);
        Check(NetCombatFastPath.UnjoinedTerminal == 1 && NetCombatFastPath.CaptureRows().All(row => row.Terminals == 0),
            "unmatched component terminal cannot attach to a same-shot row");
        int count = NetCombatFastPath.CaptureRows().Length;
        NetHitClaims.Reset();
        Check(NetCombatFastPath.CaptureRows().Length == count && NetCombatFastPath.UnjoinedTerminal == 1,
            "gameplay ledger reset cannot erase process shadow joins");

        NetCombatFastPath.ResetEvidenceForTests();
        for (uint i = 0; i < NetCombatFastPath.PerShooter; i++)
            using (NetCombatFastPath.Request(0, Claim(1000 + i))) { }
        var first = Claim(1000);
        using (NetCombatFastPath.Request(0, first)) { }
        using (NetCombatFastPath.Request(0, Claim(1000 + NetCombatFastPath.PerShooter))) { }
        rows = NetCombatFastPath.CaptureRows();
        Check(rows.Length == NetCombatFastPath.PerShooter && NetCombatFastPath.Overflow == 1
            && rows.Single(row => row.Key == NetShotProof.PaymentKey(0, first)).Attempts == 2,
            "full per-shooter storage retains exact retry and refuses new row without recycling");
        var invalid = first; invalid.ComponentId = 0;
        using (NetCombatFastPath.Request(0, invalid)) { }
        Check(NetCombatFastPath.Invalid == 1 && NetCombatFastPath.CaptureRows().Length == rows.Length,
            "invalid identity is counted without occupying or recycling a cell");
        NetSession.Stop();
        Check(NetCombatFastPath.CaptureRows().Length == rows.Length && NetCombatFastPath.Overflow == 1,
            "session teardown cannot erase overflow or retained observations");
    }
    internal static int Run()
    {
        var mode = NetCombatFastPath.Mode; bool claims = NetHitClaims.Enabled; _checks = 0;
        try
        {
            Receipts(); NativeFacts(); EvidenceLifecycle();
            Console.WriteLine($"PASS {_checks} constructed fast-decision checks; detached facts and bounded diagnostic hooks only, no UDP/native parity/payment qualification.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            NetSession.Stop(); NetCombatFastPath.ResetEvidenceForTests();
            NetCombatFastPath.Mode = mode; NetHitClaims.Enabled = claims;
        }
    }
}
