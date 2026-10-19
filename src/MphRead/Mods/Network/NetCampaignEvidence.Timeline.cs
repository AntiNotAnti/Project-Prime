using System;
using System.Diagnostics;

namespace MphRead.Mods.Network;

// Local, opt-in observations only. These values never authorize a shot, pay
// damage, advance a clock, enter a packet or become replay simulation input.
internal readonly record struct ShotTimelineRow(string Stage, ShotKey Key,
    uint Component, uint Phase, byte Kind, byte Victim, ushort VictimGeneration,
    ushort VictimLife, ushort ClaimId, uint DamageEventId, uint SourceFrame,
    uint AckFrame, byte AckSubFrame, uint LocalFrame, uint AuthorityFrame,
    long Timestamp, byte Weapon, int Value, int Result);

internal sealed class ShotTimelineBuffer
{
    private readonly ShotTimelineRow[] _rows;
    private int _count;
    internal long Overflow { get; private set; }
    internal long UnkeyedRows { get; private set; }
    internal ShotTimelineBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _rows = new ShotTimelineRow[capacity];
    }
    internal void Add(in ShotTimelineRow row)
    {
        var key = row.Key;
        if (key.AuthorityEpoch == 0 || key.MatchId == 0 || (uint)key.ShooterSlot >= 8
            || key.Generation == 0 || key.LifeId == 0 || key.ShotId == 0)
        { UnkeyedRows++; return; }
        if (_count == _rows.Length) { Overflow++; return; }
        _rows[_count++] = row;
    }
    // Detached export is report-time work; routine observation never grows storage.
    internal ShotTimelineRow[] Capture() => _rows.AsSpan(0, _count).ToArray();
}

internal static partial class NetCampaignEvidence
{
    internal static object? PresentationSchedule;
    private static ShotTimelineBuffer? _shotTimeline;
    private static readonly bool TimelineRequested = Environment.GetEnvironmentVariable("PRIME_NCX_TIMELINE") == "1";
    internal static bool TimelineEnabled => Enabled && TimelineRequested;

    internal static void Timeline(string stage, ShotKey key, uint component = 0,
        uint phase = 0, byte kind = 0, byte victim = 255, ushort victimGeneration = 0,
        ushort victimLife = 0, ushort claimId = 0, uint damageEventId = 0,
        uint sourceFrame = 0, uint ackFrame = 0, byte ackSubFrame = 0,
        byte weapon = 255, int value = 0, int result = -1)
    {
        if (!TimelineEnabled) return;
        _shotTimeline ??= new ShotTimelineBuffer(EvidenceRowLimit);
        _shotTimeline.Add(new(stage, key, component, phase, kind, victim, victimGeneration,
            victimLife, claimId, damageEventId, sourceFrame, ackFrame, ackSubFrame,
            NetSession.NetFrame, NetSession.IsAuthority ? NetSession.NetFrame : NetSession.LastSnapshotFrame,
            Stopwatch.GetTimestamp(), weapon, value, result));
    }

    internal static void TimelineFire(string stage, ShotKey key, in FireEvent fire)
    {
        if (!TimelineEnabled) return;
        Timeline(stage, key, phase: fire.Kind == FireEventKind.ContinuousTick ? fire.ContinuousPhase : 0, sourceFrame: fire.SourceFrame,
            ackFrame: fire.AckFrame, ackSubFrame: fire.AckSubFrame, weapon: fire.Weapon);
    }

    internal static void TimelineIntentReceived(int slot, in IntentPacket intent)
    {
        if (!TimelineEnabled) return;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var fire = intent.FireEvents[i];
            TimelineFire("intent-received", new ShotKey(intent.AuthorityEpoch, intent.MatchId,
                slot, intent.SlotGeneration, fire.SourceLife != 0 ? fire.SourceLife : intent.LifeId, fire.ShotId), fire);
        }
    }

    internal static void TimelineClaim(string stage, int slot, in HitClaimPacket claim)
    {
        if (!TimelineEnabled) return;
        var payment = NetShotProof.PaymentKey(slot, claim);
        Timeline(stage, payment.Shot, payment.Component,
            phase: (claim.Flags & HitClaimPacket.FlagContinuousTick) != 0 ? claim.Frame : 0,
            kind: payment.Kind, victim: payment.Victim, victimGeneration: payment.Generation,
            victimLife: payment.Life, claimId: claim.ClaimId, ackFrame: claim.AckFrame,
            weapon: claim.Beam, value: claim.Damage);
    }

    internal static void TimelinePayment(string stage, in ShotPaymentKey payment,
        byte weapon, uint phase = 0, in CombatAckEntry outcome = default, int result = -1, ushort claimId = 0)
    {
        if (!TimelineEnabled) return;
        Timeline(stage, payment.Shot, payment.Component, phase, payment.Kind, payment.Victim,
            payment.Generation, payment.Life, claimId != 0 ? claimId : outcome.ClaimId, outcome.DamageSequence,
            weapon: weapon, value: outcome.DamageApplied, result: result);
    }

    internal static void TimelineContinuous(string stage, in ContinuousPaymentKey payment,
        in CombatAckEntry outcome, int result = -1, ushort claimId = 0)
    {
        if (!TimelineEnabled) return;
        // The ledger's OwnerTick.ShotId is a phase, not the emitted shot ID.
        var shot = payment.OwnerTick with { ShotId = outcome.ShotId };
        Timeline(stage, shot, phase: payment.OwnerTick.ShotId,
            kind: payment.Flags == ContinuousPaymentKey.Turret ? (byte)5 : (byte)1,
            victim: (byte)payment.Victim.Slot, victimGeneration: payment.Victim.Generation,
            victimLife: payment.Victim.LifeId, claimId: claimId != 0 ? claimId : outcome.ClaimId,
            damageEventId: outcome.DamageSequence, weapon: (byte)BeamType.ShockCoil,
            value: outcome.DamageApplied, result: result >= 0 ? result
                : stage == "payment-committed" ? outcome.Result : -1);
    }

    internal static object CaptureShotTimeline() => new
    {
        Schema = 1,
        Enabled = TimelineEnabled,
        Clock = new { Domain = "ProcessStopwatch", Frequency = Stopwatch.Frequency },
        Rows = _shotTimeline?.Capture() ?? Array.Empty<ShotTimelineRow>(),
        Overflow = _shotTimeline?.Overflow ?? 0,
        UnkeyedRows = _shotTimeline?.UnkeyedRows ?? 0,
        Complete = TimelineEnabled && (_shotTimeline?.Overflow ?? 0) == 0 && (_shotTimeline?.UnkeyedRows ?? 0) == 0,
        Scope = "Process-local monotonic samples; LocalFrame is this process simulation frame; AuthorityFrame is local authority tick or latest received snapshot, not a clock offset. SourceFrame/AckFrame/AckSubFrame retain authored metadata when known (zero otherwise). intent-offered is transport scheduling, not socket transmission. source-admitted is not emission or payment; native-contact is not payment. Value is claimed/channel/applied body damage according to stage, not a kill. render-submitted is not visible pixels. Cross-process times require causal alignment with uncertainty."
    };
}
