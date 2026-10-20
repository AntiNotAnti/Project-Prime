using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum CombatFastMode : byte { Off, Shadow, Paid }
internal enum FastPaymentKnowledge : byte { Missing, Reserved, Paid }
internal readonly record struct FastPaymentFact(FastPaymentKnowledge Knowledge,
    bool WeaponMatches, CombatAckEntry Outcome);
internal enum FastDecisionState : byte { LegacyRequired, PendingDependencies, VerifiedPaid, VerifiedNative, Reject }
internal enum FastDecisionReason : byte
{
    FamilyUnsupported, MissingSource, SourceUnavailable, GeometryUnavailable, HeadMismatch,
    WallMismatch, OrderingDeferred, ReservationPending, PaidDuplicate, NativeDamage,
    NativeBlocked, DamageUnavailable, InvalidIdentity
}
internal readonly record struct CombatFastDecision(FastDecisionState State, FastDecisionReason Reason,
    FastNativeDamageResult Damage = default, CombatAckEntry Paid = default)
{
    internal bool Eligible => State is FastDecisionState.VerifiedPaid or FastDecisionState.VerifiedNative;
    internal bool WouldPay => State == FastDecisionState.VerifiedNative
        && Damage.Status == FastDamageAvailability.Available;
    internal bool WouldReject => State == FastDecisionState.Reject;
}
internal enum FastProbeStage : byte { ClaimArrival, ClaimReady, NativePreDamage }
internal readonly record struct FastClaimShape(ushort Id, byte Weapon, byte Flags, ushort Damage,
    uint Frame, uint AckFrame, uint LaunchFrame, Vector3 Point, Vector3 Direction)
{
    internal static FastClaimShape From(in HitClaimPacket claim) => new(claim.ClaimId, claim.Beam,
        claim.Flags, claim.Damage, claim.Frame, claim.AckFrame, claim.LaunchFrame, claim.HitPoint, claim.Direction);
}
internal readonly record struct FastNativeEvidence(bool SourceVerified, bool GeometryVerified,
    FastPaymentKnowledge PriorPayment, TimedShotCertificate Source,
    FastNativeDamageGates SourceGates, FastNativeDamageGates VictimGates,
    TimedVictimDamageProfile Profile, NativeImpactWitness Impact, bool TransientEmission);
internal readonly record struct CombatFastRow(ShotPaymentKey Key, FastProbeStage Stage,
    uint LegalFrame, uint GeometryFrame, byte Weapon, CombatFastDecision Decision,
    int Attempts, bool Compared, bool Agrees, int ActualDebit, int ActualHealth,
    int Payments, int Terminals, FastClaimShape Request = default, bool Corrected = false, int PreHealth = 0,
    CombatAckEntry ActualOutcome = default, int TerminalDisagreements = 0, FastNativeEvidence NativeEvidence = default,
    bool PaidBypass = false);

// Pure decisions over separately authenticated facts. A paid receipt is not a
// new legal-hit certificate. Fresh claims never bypass the existing ordering
// barrier: only the native pre-damage execution seam proves its current turn.
internal static class CombatFastDecisionFunction
{
    internal static CombatFastDecision Receipt(in HitClaimPacket claim, in FastPaymentFact paid,
        in ShotProofDecision proof)
    {
        if (claim.ClaimId == 0 || claim.ShotId == 0 || claim.ComponentId == 0 || claim.VictimSlot >= 8)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.InvalidIdentity);
        if (!CombatFamilyPolicies.PaidReceiptFamily(claim.Beam)
            || claim.ComponentId != 1 || NetShotProof.Kind(claim.Flags) != 1
            || (claim.Flags & HitClaimPacket.FlagContinuousTick) != 0)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.FamilyUnsupported);
        if (paid.Knowledge == FastPaymentKnowledge.Reserved)
            return new(FastDecisionState.PendingDependencies, FastDecisionReason.ReservationPending);
        if (paid.Knowledge == FastPaymentKnowledge.Paid && paid.WeaponMatches)
        {
            bool head = (claim.Flags & HitClaimPacket.FlagHeadshot) != 0;
            if (head != paid.Outcome.Flags.HasFlag(CombatAckFlags.Headshot))
                return new(FastDecisionState.LegacyRequired, FastDecisionReason.HeadMismatch);
            return new(FastDecisionState.VerifiedPaid, FastDecisionReason.PaidDuplicate, Paid: paid.Outcome);
        }
        if (proof.Reason == ShotProofReason.WrongHeadshot)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.HeadMismatch);
        if (proof.Reason is ShotProofReason.BlockedMuzzle or ShotProofReason.BlockedWorld)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.WallMismatch);
        if (proof.Reason is ShotProofReason.UnknownShot or ShotProofReason.ShotNotFired)
            return new(FastDecisionState.PendingDependencies, FastDecisionReason.MissingSource);
        if (proof.Source != ShotProofReason.Verified)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.SourceUnavailable);
        if (proof.Geometry != ShotProofReason.Verified)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.GeometryUnavailable);
        return new(FastDecisionState.LegacyRequired, FastDecisionReason.OrderingDeferred);
    }
    internal static CombatFastDecision Native(in FastPaymentFact paid, bool source,
        bool geometry, in FastNativeDamageResult damage)
    {
        if (paid.Knowledge != FastPaymentKnowledge.Missing)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.ReservationPending);
        if (!source) return new(FastDecisionState.PendingDependencies, FastDecisionReason.MissingSource);
        if (!geometry) return new(FastDecisionState.LegacyRequired, FastDecisionReason.GeometryUnavailable);
        if (damage.Status == FastDamageAvailability.Unavailable)
            return new(FastDecisionState.LegacyRequired, FastDecisionReason.DamageUnavailable, damage);
        return new(damage.Status == FastDamageAvailability.Blocked
            ? FastDecisionState.Reject : FastDecisionState.VerifiedNative,
            damage.Status == FastDamageAvailability.Blocked
                ? FastDecisionReason.NativeBlocked : FastDecisionReason.NativeDamage, damage);
    }
}

// Fixed process-owned evidence only. No damage, reservation, replay simulation,
// controller, packet, source acceptance, world restore, or gameplay clock writes.
// Cells never recycle: an overflowing run is explicitly incomplete at export.
internal static class NetCombatFastPath
{
    internal const int PerShooter = 1024;
    internal static CombatFastMode Mode { get; set; } =
        Environment.GetEnvironmentVariable("PRIME_COMBAT_FASTPATH") switch {
            "shadow" => CombatFastMode.Shadow, "paid" => CombatFastMode.Paid, _ => CombatFastMode.Off };
    internal static bool ShadowEnabled => Mode is CombatFastMode.Shadow or CombatFastMode.Paid;
    internal static bool PaidEnabled => Mode == CombatFastMode.Paid;
    private static bool Active => ShadowEnabled && NetHitClaims.Arbitrating;
    private static CombatFastRow[,]? _rows;
    private static CombatFastRow[,] Rows => _rows ??= new CombatFastRow[8, PerShooter];
    private static readonly int[] Counts = new int[8];
    internal static long Overflow, Invalid, UnjoinedTerminal;
    internal static long PaidReceipts;
    [ThreadStatic] private static (int Slot, ushort Id, ShotPaymentKey Key, FastClaimShape Shape)? _request;
    [ThreadStatic] private static TimedShotCertificate? _emitting;
    [ThreadStatic] private static bool _emissionBound;
    internal readonly struct SourceScope : IDisposable
    {
        private readonly bool _active;
        private readonly TimedShotCertificate? _prior;
        private readonly bool _priorBound;
        internal SourceScope(TimedShotCertificate? attempt)
        {
            _active = Active; _prior = _active ? _emitting : null; _priorBound = _emissionBound;
            if (_active) { _emitting = attempt; _emissionBound = false; }
        }
        public void Dispose() { if (_active) { _emitting = _prior; _emissionBound = _priorBound; } }
    }
    internal static SourceScope Source(TimedShotCertificate? attempt) => new(attempt);
    internal static void Emitted(in TimedShotEmission emission)
    {
        if (!Active || _emitting is not { } attempt) return;
        if (_emissionBound) { _emitting = null; return; }
        _emissionBound = true;
        _emitting = !emission.Conflict && emission.HasSpend && emission.Components == 1
            && emission.Key == attempt.Key && emission.Damage.Present && emission.Damage.FirstSegmentKnown
            && emission.Cost == attempt.Source.Cost && emission.AmmoType == attempt.Source.AmmoType
            && NetFireEvents.PendingAccepted(attempt.Key, attempt.Fire)
            ? attempt with { Components = emission.Components, Damage = emission.Damage } : null;
    }
    internal readonly struct RequestScope : IDisposable
    {
        private readonly bool _active;
        private readonly (int Slot, ushort Id, ShotPaymentKey Key, FastClaimShape Shape)? _prior;
        internal RequestScope(int slot, in HitClaimPacket claim)
        {
            _active = Active; _prior = _active ? _request : null;
            if (_active) { _request = (slot, claim.ClaimId, NetShotProof.PaymentKey(slot, claim), FastClaimShape.From(claim)); ObserveClaim(slot, claim); }
        }
        public void Dispose() { if (_active) _request = _prior; }
    }
    internal static RequestScope Request(int slot, in HitClaimPacket claim) => new(slot, claim);
    internal static bool TryRequest(int slot, ushort id, out ShotPaymentKey key)
    {
        key = default;
        if (!Active || _request is not { } value || value.Slot != slot || value.Id != id) return false;
        key = value.Key; return true;
    }
    private static bool Valid(in ShotPaymentKey key) => (uint)key.Shot.ShooterSlot < 8
        && key.Shot.AuthorityEpoch != 0 && key.Shot.MatchId != 0 && key.Shot.Generation != 0
        && key.Shot.LifeId != 0 && key.Shot.ShotId != 0 && key.Component != 0
        && key.Victim < 8 && key.Generation != 0 && key.Life != 0;
    private static int Find(in ShotPaymentKey key, FastProbeStage stage, FastClaimShape shape = default)
    {
        if (!Valid(key)) return -1;
        for (int i = 0; i < Counts[key.Shot.ShooterSlot]; i++)
            if (Rows[key.Shot.ShooterSlot, i].Key == key && Rows[key.Shot.ShooterSlot, i].Stage == stage
                && Rows[key.Shot.ShooterSlot, i].Request == shape) return i;
        return -1;
    }
    private static void Add(in CombatFastRow row)
    {
        if (!Valid(row.Key)) { Invalid++; return; }
        int slot = row.Key.Shot.ShooterSlot, at = Find(row.Key, row.Stage, row.Request);
        if (at >= 0) { Rows[slot, at] = Rows[slot, at] with { Attempts = Rows[slot, at].Attempts + 1 }; return; }
        if (Counts[slot] == PerShooter) { Overflow++; return; }
        Rows[slot, Counts[slot]++] = row;
    }
    private static CombatFastDecision ObserveClaim(int slot, in HitClaimPacket claim, bool ready = false)
    {
        if (!Active) return default;
        var key = NetShotProof.PaymentKey(slot, claim);
        if (!Valid(key)) { Invalid++; return default; }
        var paid = NetCombatPayments.Inspect(key, claim.Beam);
        var proof = paid.Knowledge == FastPaymentKnowledge.Paid ? default : NetShotProof.Evaluate(slot, claim);
        var decision = CombatFastDecisionFunction.Receipt(claim, paid, proof);
        if (!ready && decision.Eligible)
            decision = new(FastDecisionState.LegacyRequired, FastDecisionReason.OrderingDeferred);
        Add(new(key, ready ? FastProbeStage.ClaimReady : FastProbeStage.ClaimArrival, NetSession.NetFrame, claim.AckFrame, claim.Beam,
            decision, 1, false, false, 0, 0, 0, 0, FastClaimShape.From(claim)));
        return decision;
    }
    internal static CombatFastDecision ClaimReady(int slot, in HitClaimPacket claim) => ObserveClaim(slot, claim, ready: true);
    internal static void PaidReceipt(int slot, in HitClaimPacket claim)
    {
        PaidReceipts++;
        int at = Find(NetShotProof.PaymentKey(slot, claim), FastProbeStage.ClaimReady, FastClaimShape.From(claim));
        if (at >= 0) Rows[slot, at] = Rows[slot, at] with { PaidBypass = true };
    }
    internal static void NativeBefore(PlayerEntity victim, BeamProjectileEntity beam, in NativeImpactWitness witness)
    {
        if (!Active) return;
        var key = witness.Payment;
        CombatFastDecision decision;
        FastNativeEvidence evidence = default;
        uint geometryFrame = 0;
        if (!witness.OrdinaryDirect || !CombatFamilyPolicies.NativeContactShadowFamily(witness.Weapon)
            || beam.Owner is not PlayerEntity owner)
            decision = new(FastDecisionState.LegacyRequired, FastDecisionReason.FamilyUnsupported);
        else
        {
            bool found = NetTimedShotProof.TryGet(key.Shot, out var shot);
            bool transientEmission = false;
            if (!found && _emissionBound && _emitting is { } transient && transient.Key == key.Shot
                && NetFireEvents.PendingAccepted(transient.Key, transient.Fire)) { shot = transient; found = true; transientEmission = true; }
            bool source = found && shot.TrustedSource
                && shot.Unavailable == TimedShotUnavailable.None && shot.DoubleHintAgrees == true
                && (shot.Knowledge == TimedGrantKnowledge.NotGranted || shot.AuthorizedDouble)
                && shot.Fire.Weapon == witness.Weapon && shot.Components == 1
                && !shot.Fire.HasEnhancedContext && !shot.Damage.Balanced && shot.Damage.Effects == 0
                && shot.Damage.DirectOnlyEffects == 0 && shot.Damage.Present
                && NetPlayerLifecycle.CurrentProjectile(beam);
            geometryFrame = shot.Damage.FirstCollisionFrame;
            // Native collision, not the claimant's point/head/damage, supplies
            // the exact selected body and complete obstacle/competitor ordering.
            // Restrict the certificate to its original complete native segment.
            bool geometry = shot.Damage.FirstSegmentKnown
                && witness.Origin == shot.Damage.Origin && witness.SegmentStart == shot.Damage.Origin
                && witness.SegmentEnd == shot.Damage.Origin + shot.Damage.NativeVelocity;
            var sourceGates = owner.ModCaptureFastDamageGates(witness.Frame);
            var victimGates = victim.ModCaptureFastDamageGates(witness.Frame);
            var damage = victim.ModCaptureFastDamageProfile(witness.Frame, out var profile)
                ? NetCombatFastDamage.Evaluate(key, sourceGates, victimGates, profile,
                    witness.NativeChannel, witness.NativeHead) : default;
            var paid = NetCombatPayments.Inspect(key, witness.Weapon);
            evidence = new(source, geometry, paid.Knowledge, shot, sourceGates, victimGates,
                profile, witness, transientEmission);
            decision = CombatFastDecisionFunction.Native(paid, source, geometry, damage);
        }
        Add(new(key, FastProbeStage.NativePreDamage, witness.Frame, geometryFrame, witness.Weapon,
            decision, 1, false, false, 0, 0, 0, 0, PreHealth: victim.Health, NativeEvidence: evidence));
    }
    internal static void NativeAfter(PlayerEntity victim, BeamProjectileEntity beam, int before)
    {
        if (!Active || !NetNativeImpactWitness.TryCapture(out var witness)) return;
        int at = Find(witness.Payment, FastProbeStage.NativePreDamage);
        if (at < 0) return;
        ref var row = ref Rows[witness.Payment.Shot.ShooterSlot, at];
        if (row.Compared) return; // A duplicate contact cannot replace the first observation.
        int debit = Math.Max(0, before - victim.Health);
        bool compare = row.Decision.WouldPay || row.Decision.WouldReject;
        bool agrees = compare && debit == row.Decision.Damage.HealthDebit
            && victim.Health == before - (int)row.Decision.Damage.HealthDebit
            && (!row.Decision.WouldPay || row.Payments == 1)
            && (!row.Decision.WouldReject || row.Payments == 0);
        row = row with { Compared = compare, Agrees = agrees, ActualDebit = debit, ActualHealth = victim.Health };
    }
    internal static void Payment(in ShotPaymentKey key, in CombatAckEntry outcome)
    {
        if (!Active) return;
        int at = Find(key, FastProbeStage.NativePreDamage);
        if (at >= 0) { ref var row = ref Rows[key.Shot.ShooterSlot, at]; row = row with { Payments = row.Payments + 1, ActualOutcome = outcome }; }
    }
    internal static void Terminal(in ShotPaymentKey key, in CombatAckEntry answer)
    {
        if (!Active) return;
        int at = -1;
        // The active request owns its exact shape, even when a ClaimId was
        // re-used with different flags, damage, times, point or impulse.
        if (_request is { } request && request.Key == key && request.Id == answer.ClaimId)
        {
            at = Find(key, FastProbeStage.ClaimReady, request.Shape);
            if (at < 0) at = Find(key, FastProbeStage.ClaimArrival, request.Shape);
        }
        else if (Valid(key))
        {
            // Deferred settlement has no active datagram. Join its unique
            // ClaimId only; ambiguity never borrows a different request proof.
            for (int i = 0; i < Counts[key.Shot.ShooterSlot]; i++)
                if (Rows[key.Shot.ShooterSlot,i].Key == key && Rows[key.Shot.ShooterSlot,i].Request.Id == answer.ClaimId
                    && Rows[key.Shot.ShooterSlot,i].Stage == FastProbeStage.ClaimReady)
                { if (at >= 0) { at = -1; break; } at = i; }
        }
        if (at < 0) { UnjoinedTerminal++; return; }
        ref var row = ref Rows[key.Shot.ShooterSlot, at];
        bool compare = row.Decision.State == FastDecisionState.VerifiedPaid;
        var paid = row.Decision.Paid;
        bool agrees = compare && answer.Accepted && answer.ShotId == key.Shot.ShotId
            && answer.VictimSlot == key.Victim && answer.VictimGeneration == key.Generation
            && answer.VictimLife == key.Life && answer.DamageApplied == paid.DamageApplied
            && answer.HealthAfter == paid.HealthAfter && answer.Flags == paid.Flags
            && answer.HalfturretHealthAfter == paid.HalfturretHealthAfter
            && answer.DamageSequence == paid.DamageSequence;
        row = row with { ActualOutcome = answer, TerminalDisagreements = row.TerminalDisagreements + (compare && !agrees ? 1 : 0), ActualDebit = answer.DamageApplied, ActualHealth = answer.HealthAfter, Terminals = row.Terminals + 1, Compared = row.Compared || compare,
            Agrees = row.Compared ? row.Agrees && agrees : agrees };
    }
    internal static void Correct(in ShotPaymentKey key)
    {
        if (!Active || !Valid(key)) return;
        for (int i = 0; i < Counts[key.Shot.ShooterSlot]; i++)
            if (Rows[key.Shot.ShooterSlot,i].Key == key)
                Rows[key.Shot.ShooterSlot,i] = Rows[key.Shot.ShooterSlot,i] with { Corrected = true };
    }
    internal static CombatFastRow[] CaptureRows()
    {
        int count = 0; for (int slot = 0; slot < 8; slot++) count += Counts[slot];
        var values = new CombatFastRow[count]; int next = 0;
        for (int slot = 0; slot < 8; slot++) for (int i = 0; i < Counts[slot]; i++) values[next++] = Rows[slot, i];
        return values;
    }
    private static object Vector(in Vector3 value) => new {
        X = float.IsFinite(value.X) ? (float?)value.X : null,
        Y = float.IsFinite(value.Y) ? (float?)value.Y : null,
        Z = float.IsFinite(value.Z) ? (float?)value.Z : null
    };
    private static object ExportNative(in FastNativeEvidence value) => new {
        value.SourceVerified, value.GeometryVerified, value.PriorPayment, value.TransientEmission,
        value.SourceGates, value.VictimGates, value.Profile,
        Source = new { value.Source.Key, value.Source.Room, value.Source.AuthorityFrame, value.Source.Lane, value.Source.TrustedSource,
            value.Source.Source, value.Source.Unavailable, value.Source.Knowledge, value.Source.Funding,
            value.Source.DoubleHintAgrees, value.Source.AuthorizedDouble, value.Source.Components,
            Fire = new { value.Source.Fire.ShotId, value.Source.Fire.SourceLife,
                value.Source.Fire.SourceFrame, value.Source.Fire.AckFrame, value.Source.Fire.AckSubFrame,
                value.Source.Fire.Kind, value.Source.Fire.Weapon, value.Source.Fire.Charge,
                value.Source.Fire.ContinuousPhase, value.Source.Fire.PoseFlags,
                Origin = Vector(value.Source.Fire.Origin), Direction = Vector(value.Source.Fire.Direction),
                Aim = Vector(value.Source.Fire.Aim), View = Vector(value.Source.Fire.View),
                Reticle = new { value.Source.Fire.Reticle.X, value.Source.Fire.Reticle.Y } },
            Damage = new { value.Source.Damage.Present, value.Source.Damage.FirstSegmentKnown,
                value.Source.Damage.FirstCollisionFrame, value.Source.Damage.Body, value.Source.Damage.Head,
                value.Source.Damage.Splash, value.Source.Damage.Radius, value.Source.Damage.Speed,
                value.Source.Damage.Reach, value.Source.Damage.Curve, value.Source.Damage.Balanced,
                value.Source.Damage.Effects, value.Source.Damage.DirectOnlyEffects,
                Origin = Vector(value.Source.Damage.Origin), NativeVelocity = Vector(value.Source.Damage.NativeVelocity) }
        },
        Impact = new { value.Impact.Payment, value.Impact.Weapon, value.Impact.Frame,
            value.Impact.NativeChannel, value.Impact.NativeHead, value.Impact.Direct, value.Impact.OrdinaryDirect,
            Point = Vector(value.Impact.Point), Origin = Vector(value.Impact.Origin),
            SegmentStart = Vector(value.Impact.SegmentStart), SegmentEnd = Vector(value.Impact.SegmentEnd) }
    };
    private static object ExportOutcome(in CombatAckEntry outcome) => new {
        outcome.ShotId, outcome.ClaimId, outcome.Result, outcome.VictimSlot, outcome.VictimGeneration,
        outcome.VictimLife, outcome.DamageApplied, outcome.HealthAfter, outcome.HalfturretHealthAfter,
        outcome.DamageSequence, Flags = (byte)outcome.Flags
    };
    internal static object Capture()
    {
        var rows = CaptureRows();
        long eligible=0,pay=0,reject=0,missing=0,head=0,wall=0,ordering=0,unsupported=0,doublePay=0,trade=0,compared=0,disagree=0,uncompared=0,corrected=0;
        foreach (var row in rows)
        {
            var d = row.Decision;
            if (row.Corrected) { corrected++; continue; } // Explicitly outside the certified subset.
            if (d.Eligible) eligible++; if (d.WouldPay) pay++; if (d.WouldReject) reject++;
            if (d.Reason == FastDecisionReason.MissingSource) missing++;
            if (d.Reason == FastDecisionReason.HeadMismatch) head++;
            if (d.Reason == FastDecisionReason.WallMismatch) wall++;
            if (d.Reason == FastDecisionReason.OrderingDeferred) ordering++;
            if (d.Reason == FastDecisionReason.FamilyUnsupported) unsupported++;
            if (d.WouldPay && row.Payments > 1) doublePay++;
            if (row.Compared) { compared++; if (!row.Agrees) disagree++; }
            else if (d.Eligible || d.WouldReject) uncompared++;
            if (d.WouldPay && row.Compared && d.Damage.Lethal != (row.ActualHealth == 0)) trade++;
        }
        var export = Array.ConvertAll(rows, row => new {
            row.Key, row.Stage, row.LegalFrame, row.GeometryFrame, row.Weapon,
            Decision = new { row.Decision.State, row.Decision.Reason, row.Decision.Damage,
                Paid = ExportOutcome(row.Decision.Paid) },
            row.Attempts, row.Compared, row.Agrees, row.ActualDebit, row.ActualHealth,
            row.Payments, row.Terminals, row.Corrected, row.PreHealth, row.TerminalDisagreements, row.PaidBypass,
            NativeEvidence = ExportNative(row.NativeEvidence),
            ActualOutcome = ExportOutcome(row.ActualOutcome),
            Request = new { row.Request.Id, row.Request.Weapon, row.Request.Flags, row.Request.Damage,
                row.Request.Frame, row.Request.AckFrame, row.Request.LaunchFrame,
                Point = Vector(row.Request.Point), Direction = Vector(row.Request.Direction) }
        });
        return new { Schema=1, Mode, PerShooter, Rows=export, Overflow, Invalid, UnjoinedTerminal, PaidReceipts,
            fastEligible=eligible, fastWouldPay=pay, fastWouldReject=reject, missingSource=missing,
            headMismatch=head, wallMismatch=wall, orderingDeferred=ordering, familyUnsupported=unsupported,
            wouldDoublePay=doublePay, wouldAlterTrade=trade, Compared=compared, Disagreements=disagree, Uncompared=uncompared, CorrectedExcluded=corrected,
            Scope="Shadow observes; Paid returns only exact already-paid primary direct receipts before redundant validation. No new payment, fresh-claim early settlement or native enforcement. Native pre-damage legal frame is distinct from retained geometric frame. Missing/overflow/uncompared observations cannot certify parity." };
    }
    // Only explicit test setup resets process evidence. Gameplay lifecycle must
    // never erase unresolved joins or turn an overflow into an apparent PASS.
    internal static void ResetEvidenceForTests() { Array.Clear(Rows); Array.Clear(Counts); Overflow=Invalid=UnjoinedTerminal=PaidReceipts=0; _request=null; _emitting=null; _emissionBound=false; }
}
