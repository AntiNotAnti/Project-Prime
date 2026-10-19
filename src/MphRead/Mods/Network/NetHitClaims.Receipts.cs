using System;

namespace MphRead.Mods.Network;

internal enum ClaimReceiptRetirement
{
    Delivered, TimedOut, CapacityEvicted, LifeRetired, OccupantRetired, MatchRetired, SessionRetired
}

public static partial class NetHitClaims
{
    private static long _receiptsCreated;
    private static long _authorityPendingCancelled;
    private static readonly long[] ReceiptRetirements = new long[7];

    // Receipt-only accounting. It never pays damage, changes prediction, or
    // turns a source admission/accepted claim into an authoritative hit.
    private static void RetireReceipt(ref Outgoing entry, ClaimReceiptRetirement reason)
    {
        if (!entry.Live) return;
        entry.Live = false;
        ReceiptRetirements[(int)reason]++;
        NetCampaignEvidence.Timeline("receipt-retired", new ShotKey(entry.AuthorityEpoch,
            entry.MatchId, entry.ShooterSlot, entry.ShooterGeneration, entry.ShooterLifeId, entry.ShotId),
            entry.ComponentId, (entry.Flags & HitClaimPacket.FlagContinuousTick) != 0 ? entry.Frame : 0,
            NetShotProof.Kind(entry.Flags), entry.VictimSlot, entry.VictimGeneration, entry.VictimLifeId,
            entry.Id, ackFrame: entry.AckFrame, weapon: entry.Beam, result: (int)reason);
    }

    private static void RetireReceipts(ClaimReceiptRetirement reason)
    {
        for (int i = 0; i < _outbox.Length; i++) RetireReceipt(ref _outbox[i], reason);
    }

    private static void RecordPendingCancellation(ClaimReceiptRetirement reason)
    {
        foreach (ref readonly var entry in _pending.AsSpan())
        {
            if (!entry.Live) continue;
            _authorityPendingCancelled++;
            NetCampaignEvidence.Timeline("claim-cancelled", new ShotKey(entry.AuthorityEpoch,
                entry.MatchId, entry.ShooterSlot, entry.ShooterGeneration, entry.ShooterLifeId, entry.ShotId),
                entry.ComponentId, entry.ContinuousPhase, NetShotProof.Kind(entry.Flags), entry.VictimSlot,
                entry.VictimGeneration, entry.VictimLifeId, entry.Id, weapon: entry.Beam, result: (int)reason);
        }
    }

    internal static object CaptureReceiptAccounting() => new
    {
        Created = _receiptsCreated,
        Delivered = ReceiptRetirements[(int)ClaimReceiptRetirement.Delivered],
        TimedOut = ReceiptRetirements[(int)ClaimReceiptRetirement.TimedOut],
        CapacityEvicted = ReceiptRetirements[(int)ClaimReceiptRetirement.CapacityEvicted],
        LifeRetired = ReceiptRetirements[(int)ClaimReceiptRetirement.LifeRetired],
        OccupantRetired = ReceiptRetirements[(int)ClaimReceiptRetirement.OccupantRetired],
        MatchRetired = ReceiptRetirements[(int)ClaimReceiptRetirement.MatchRetired],
        SessionRetired = ReceiptRetirements[(int)ClaimReceiptRetirement.SessionRetired],
        Outstanding = OutgoingReceipts,
        AuthorityPendingCancelled = _authorityPendingCancelled,
        Scope = "Process lifetime client receipts; delivered/refused outcomes are not damage payments; constructed test outbox injection is not declaration activity"
    };

    private static CombatAckEntry RequestOutcome(in HitClaimPacket claim) => new()
    {
        ShotId = claim.ShotId, VictimSlot = claim.VictimSlot,
        VictimGeneration = claim.VictimGeneration, VictimLife = claim.VictimLifeId
    };

    private static bool ReceiptReusable(int slot, int at) => _seenIds[slot, at] == 0
        || _seenResults[slot, at] != ResultPending
            && unchecked(NetSession.NetFrame - _seenTerminalFrames[slot, at]) > DeliveryMaxAge;

    private static void AnswerRequest(int slot, in HitClaimPacket claim, byte result,
        bool remember = true, CombatAckEntry? outcome = null)
    {
        int at = SeenIndex(slot, claim.ClaimId);
        var key = NetShotProof.Key(slot, claim);
        var payment = NetShotProof.PaymentKey(slot, claim);
        if (_seenIds[slot, at] == claim.ClaimId && claim.ClaimId != 0)
        {
            bool exact = _seenKeys[slot, at] == key && _seenPaymentKeys[slot, at] == payment
                && _seenBeams[slot, at] == (BeamType)claim.Beam;
            if (exact || !ReceiptReusable(slot, at))
            {
                Answer(slot, claim.ClaimId, result, remember: exact && remember,
                    outcome: exact ? outcome : RequestOutcome(claim));
                return;
            }
        }
        if (claim.ClaimId == 0)
        { Answer(slot, 0, result, remember: false, outcome: RequestOutcome(claim)); return; }
        if (!ReceiptReusable(slot, at) || ReceiptAdmissionBlocked(slot))
        { RefuseReceiptCapacity(slot, claim); return; }
        _seenKeys[slot, at] = key; _seenPaymentKeys[slot, at] = payment;
        _seenBeams[slot, at] = (BeamType)claim.Beam;
        _seenOutcomes[slot, at] = RequestOutcome(claim);
        Remember(slot, claim.ClaimId, ResultPending);
        Answer(slot, claim.ClaimId, result, outcome: outcome);
    }

    private static bool ReceiptAdmissionBlocked(int slot)
    {
        if (!_receiptUnsafe[slot]) return false;
        if (unchecked((int)(NetSession.NetFrame - _receiptUnsafeUntil[slot])) > 0)
        { _receiptUnsafe[slot] = false; return false; }
        return true;
    }

    private static void RefuseReceiptCapacity(int slot, in HitClaimPacket claim)
    {
        // A refusal which cannot fit must never become a later rescue on retry.
        // Require a complete quiet delivery window before admitting new IDs.
        // Exact retained receipts are replayed before this gate and stay usable.
        _receiptUnsafe[slot] = true;
        _receiptUnsafeUntil[slot] = unchecked(NetSession.NetFrame + DeliveryMaxAge);
        ClaimsCapacityRefused++;
        Answer(slot, claim.ClaimId, HitVerdictPacket.ResultClaimCapacity,
            remember: false, outcome: RequestOutcome(claim));
    }
}
