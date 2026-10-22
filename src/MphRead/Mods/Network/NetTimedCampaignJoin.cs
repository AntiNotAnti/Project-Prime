using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MphRead.Mods.Network;

// Process-owned opt-in evidence. Nothing in this file participates in admission,
// grants, emission, damage or replay. Values survive an empty-room world reset.
internal readonly record struct TimedJoinOutcome(uint ShotId, ushort ClaimId, byte Result,
    byte VictimSlot, ushort VictimGeneration, ushort VictimLife, ushort DamageApplied,
    ushort HealthAfter, ushort HalfturretHealthAfter, uint DamageSequence, CombatAckFlags Flags)
{
    internal static TimedJoinOutcome From(in CombatAckEntry value) => new(value.ShotId, value.ClaimId,
        value.Result, value.VictimSlot, value.VictimGeneration, value.VictimLife, value.DamageApplied,
        value.HealthAfter, value.HalfturretHealthAfter, value.DamageSequence, value.Flags);
}
internal readonly record struct TimedJoinCertificate(ulong Ordinal, uint ObservedFrame,
    TimedShotCertificate Shot, TimedShotRetention Retention);
internal readonly record struct TimedJoinComponent(ulong Ordinal, TimedNativeCapability Value,
    bool PlayerSource, bool AcceptedPlayback);
internal readonly record struct TimedJoinClaim(ulong Ordinal, uint ObservedFrame,
    ShotKey Key, HitClaimPacket Claim);
internal readonly record struct TimedJoinTerminal(ulong Ordinal, uint ObservedFrame,
    int ShooterSlot, ShotKey? Key, TimedJoinOutcome Outcome);
internal enum TimedJoinLane { Native, Admitted, Correction }
internal readonly record struct TimedJoinPayment(ulong Ordinal, uint ReservationFrame,
    uint PaymentFrame, ShotPaymentKey Key, byte Weapon, TimedJoinLane Lane, ushort ClaimId,
    TimedJoinOutcome Outcome, int ActualTurretDamage, bool Correction, NativeImpactWitness? Impact);
internal readonly record struct TimedJoinContinuous(ulong Ordinal, uint ReservationFrame,
    uint PaymentFrame, ContinuousPaymentKey Key, TimedJoinOutcome Outcome,
    int ActualTurretDamage, int HealBody, int HealTurret);

// Pages allocate only on observed demand, never at normal-game startup. A full
// page/row/byte budget drops evidence explicitly, without overwriting old facts.
internal sealed class TimedJoinValues<T> where T : struct
{
    internal const int PageSize = 1024;
    private readonly List<T[]> _pages = new();
    private readonly int _limit;
    private readonly Func<int, bool> _reserve;
    internal int Count { get; private set; }
    internal long Observed { get; private set; }
    internal long Dropped { get; private set; }
    internal TimedJoinValues(int limit, Func<int, bool> reserve) { _limit = limit; _reserve = reserve; }
    internal void Add(in T value)
    {
        Observed++;
        if (Count == _limit) { Dropped++; return; }
        int page = Count / PageSize, offset = Count % PageSize;
        if (offset == 0)
        {
            int size = Math.Min(PageSize, _limit - Count);
            if (!_reserve(checked(size * Unsafe.SizeOf<T>()))) { Dropped++; return; }
            _pages.Add(new T[size]);
        }
        _pages[page][offset] = value; Count++;
    }
    internal IEnumerable<T> Rows()
    { for (int i = 0; i < Count; i++) yield return _pages[i / PageSize][i % PageSize]; }
    internal object Budget() => new { Observed, Count, Dropped, RowLimit = _limit, PageSize,
        RowBytes = Unsafe.SizeOf<T>(), Pages = _pages.Count };
}

internal sealed class TimedJoinRecorder
{
    internal const long MaximumBytes = 256L * 1024 * 1024;
    private ulong _ordinal;
    internal long AllocatedBytes { get; private set; }
    internal readonly TimedJoinValues<TimedJoinCertificate> Certificates;
    internal readonly TimedJoinValues<TimedJoinComponent> Components;
    internal readonly TimedJoinValues<TimedJoinClaim> Claims;
    internal readonly TimedJoinValues<TimedJoinTerminal> Terminals;
    internal readonly TimedJoinValues<TimedJoinPayment> Payments;
    internal readonly TimedJoinValues<TimedJoinContinuous> Continuous;
    internal TimedJoinRecorder(int rows)
    {
        Certificates = new(rows, Reserve); Components = new(rows, Reserve); Claims = new(rows, Reserve);
        Terminals = new(rows, Reserve); Payments = new(rows, Reserve); Continuous = new(rows, Reserve);
    }
    private bool Reserve(int bytes)
    { if (AllocatedBytes + bytes > MaximumBytes) return false; AllocatedBytes += bytes; return true; }
    internal ulong Next() => ++_ordinal;
    internal object Capture() => new { Schema = 1,
        Scope = "actual authority value observations; no impact inference or gameplay authorization",
        MaximumBytes, AllocatedBytes, LastOrdinal = _ordinal,
        Budgets = new { Certificates = Certificates.Budget(), Components = Components.Budget(),
            Claims = Claims.Budget(), Terminals = Terminals.Budget(), Payments = Payments.Budget(), Continuous = Continuous.Budget() },
        Certificates = ProjectCertificates(), Components = Components.Rows(),
        Claims = ProjectClaims(), Terminals = Terminals.Rows(), Payments = ProjectPayments(), Continuous = Continuous.Rows() };
    private IEnumerable<object> ProjectCertificates()
    {
        foreach (var row in Certificates.Rows())
        {
            var shot = row.Shot;
            var fire = shot.Fire;
            var damage = shot.Damage;
            byte[] firePacket = new byte[FireEvent.Size]; fire.Write(firePacket);
            yield return new { row.Ordinal, row.ObservedFrame, row.Retention, Shot = new {
                shot.Key, shot.Room, shot.AuthorityFrame, shot.Lane, shot.Source, shot.LedgerAnswer,
                shot.Knowledge, shot.Funding, shot.Unavailable, shot.Components, shot.CaptureMode,
                Fire = new { fire.ShotId, fire.SourceFrame, fire.SourceLife, fire.AckFrame, fire.AckSubFrame,
                    fire.Weapon, fire.Charge, fire.Kind, fire.ContinuousPhase, fire.PoseFlags,
                    Payload = Convert.ToHexString(firePacket) },
                Damage = new { damage.Body, damage.Head, damage.Splash, damage.Reach, damage.Curve,
                    damage.Balanced, damage.Effects, damage.DirectOnlyEffects, damage.Present } } };
        }
    }
    private IEnumerable<object> ProjectPayments()
    {
        foreach (var row in Payments.Rows())
            yield return new { row.Ordinal, row.ReservationFrame, row.PaymentFrame, row.Key, row.Weapon,
                row.Lane, row.ClaimId, row.Outcome, row.ActualTurretDamage, row.Correction, Impact = ProjectImpact(row.Impact) };
    }
    private static object? ProjectImpact(NativeImpactWitness? source)
    {
        if (source is not { } value) return null;
        return new { value.Payment, value.Weapon, value.Frame,
            Point = new { value.Point.X, value.Point.Y, value.Point.Z }, value.NativeChannel, value.NativeHead,
            Origin = new { value.Origin.X, value.Origin.Y, value.Origin.Z },
            SegmentStart = new { value.SegmentStart.X, value.SegmentStart.Y, value.SegmentStart.Z },
            SegmentEnd = new { value.SegmentEnd.X, value.SegmentEnd.Y, value.SegmentEnd.Z },
            value.Direct, value.OrdinaryDirect, value.Completed, Outcome = TimedJoinOutcome.From(value.Outcome) };
    }
    private IEnumerable<object> ProjectClaims()
    {
        foreach (var row in Claims.Rows())
        {
            var claim = row.Claim;
            byte[] payload = new byte[HitClaimPacket.Size]; claim.Write(payload);
            yield return new { row.Ordinal, row.ObservedFrame, row.Key,
                claim.ClaimId, claim.MatchId, claim.AuthorityEpoch, claim.ShooterGeneration, claim.ShooterLifeId,
                claim.ShotId, claim.ComponentId, claim.Frame, claim.AckFrame, claim.LaunchFrame,
                claim.VictimSlot, claim.VictimGeneration, claim.VictimLifeId, claim.Beam, claim.Damage, claim.Flags,
                Payload = Convert.ToHexString(payload),
                ClaimedBodyPoint = new { X = float.IsFinite(claim.HitPoint.X) ? (float?)claim.HitPoint.X : null,
                    Y = float.IsFinite(claim.HitPoint.Y) ? (float?)claim.HitPoint.Y : null,
                    Z = float.IsFinite(claim.HitPoint.Z) ? (float?)claim.HitPoint.Z : null } };
        }
    }
}

internal static class NetTimedCampaignJoin
{
    private readonly record struct Request(int Slot, ushort Id, ShotKey Key);
    [ThreadStatic] private static Request? _request;
    internal readonly struct ClaimScope : IDisposable
    {
        private readonly Request? _prior;
        private readonly bool _active;
        private ClaimScope(Request value) { _prior = _request; _request = value; _active = true; }
        public void Dispose() { if (_active) _request = _prior; }
        internal static ClaimScope Enter(int slot, ushort id, ShotKey key) => new(new(slot, id, key));
    }
    private static TimedJoinRecorder? _recorder;
    private static TimedJoinRecorder Recorder => _recorder ??= new(NetCampaignEvidence.EvidenceRowLimit);
    private static bool Enabled => NetCampaignEvidence.Enabled && NetSession.Active && NetSession.IsAuthority;
    internal static void Certificate(in TimedShotCertificate shot, TimedShotRetention result)
    { if (Enabled) { var r = Recorder; r.Certificates.Add(new(r.Next(), NetSession.NetFrame, shot, result)); } }
    internal static void Component(in TimedNativeCapability value, bool playerSource, bool accepted)
    { if (Enabled) { var r = Recorder; r.Components.Add(new(r.Next(), value, playerSource, accepted)); } }
    internal static ClaimScope Claim(int slot, in HitClaimPacket claim)
    {
        if (!Enabled) return default;
        var key = NetShotProof.Key(slot, claim);
        var r = Recorder; r.Claims.Add(new(r.Next(), NetSession.NetFrame, key, claim));
        return ClaimScope.Enter(slot, claim.ClaimId, key);
    }
    internal static bool TryRequestKey(int slot, ushort id, out ShotKey key)
    {
        key = _request.GetValueOrDefault().Key;
        return _request is { } request && request.Slot == slot && request.Id == id;
    }
    internal static void Terminal(int slot, ShotKey? key, in CombatAckEntry outcome)
    {
        if (!Enabled) return;
        NetCampaignEvidence.Timeline("terminal-decided", key ?? default,
            victim: outcome.VictimSlot, victimGeneration: outcome.VictimGeneration,
            victimLife: outcome.VictimLife, claimId: outcome.ClaimId,
            damageEventId: outcome.DamageSequence, value: outcome.DamageApplied, result: outcome.Result);
        var r = Recorder;
        r.Terminals.Add(new(r.Next(), NetSession.NetFrame, slot, key, TimedJoinOutcome.From(outcome)));
    }
    internal static void Payment(in ShotPaymentKey key, byte weapon, uint reservationFrame,
        in CombatAckEntry outcome, int turretDamage, ushort claimId = 0, bool correction = false,
        NativeImpactWitness? impact = null)
    {
        if (!Enabled) return;
        var r = Recorder;
        r.Payments.Add(new(r.Next(), reservationFrame, NetSession.NetFrame, key, weapon,
            correction ? TimedJoinLane.Correction : claimId != 0 ? TimedJoinLane.Admitted : TimedJoinLane.Native, claimId,
            TimedJoinOutcome.From(outcome), Math.Max(0, turretDamage), correction, impact));
    }
    internal static void Continuous(in ContinuousPaymentKey key, uint reservationFrame,
        in CombatAckEntry outcome, int turretDamage, int healBody, int healTurret)
    {
        if (!Enabled) return;
        var r = Recorder; r.Continuous.Add(new(r.Next(), reservationFrame, NetSession.NetFrame, key,
            TimedJoinOutcome.From(outcome), Math.Max(0, turretDamage), Math.Max(0, healBody), Math.Max(0, healTurret)));
    }
    internal static object Capture() => _recorder?.Capture() ?? new { Schema = 1,
        Scope = "no authority join observations", MaximumBytes = TimedJoinRecorder.MaximumBytes,
        AllocatedBytes = 0, LastOrdinal = 0 };
}
