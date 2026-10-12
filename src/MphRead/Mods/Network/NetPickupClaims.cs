using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>The geometry actually used at the authority's native contact phase.
/// Remote movement remains client reported; this is not independent movement proof.</summary>
internal readonly record struct PickupContactPose(ShotKey Life, uint SourceFrame, uint AuthorityFrame,
    Vector3 Position, Vector3 SpherePosition, float Radius, float MinHeight, float MaxHeight,
    bool Alt, bool Alive, bool CanPickup)
{
    internal bool IsValid => PickupWire.ValidPosition(Position) && PickupWire.ValidPosition(SpherePosition)
        && float.IsFinite(Radius) && Radius > 0 && Radius <= 8
        && float.IsFinite(MinHeight) && float.IsFinite(MaxHeight)
        && MinHeight >= -128 && MaxHeight <= 128 && MinHeight <= MaxHeight;
    internal bool Reaches(Vector3 item, Vector3 position)
    {
        var delta = item - (Alt ? position + (SpherePosition - Position) : position);
        float radius = Radius + .45f;
        return Alt ? delta.LengthSquared < radius * radius
            : delta.X * delta.X + delta.Z * delta.Z < radius * radius
                && delta.Y >= MinHeight && delta.Y <= MaxHeight;
    }
}

internal readonly record struct PickupContactProof(PickupClaim Claim, PickupContactPose Pose,
    PickupItemFact Item, PickupVerdict Verdict, int Window);

internal delegate TimedGrantResult CommitPickup(in PickupClaim claim, uint decisionFrame,
    out TimedGrantRecord? grant);

/// <summary>Bounded contact proof and immutable terminal decisions. Each slot can
/// admit 256 distinct claims per room. Exhaustion permanently closes that slot's
/// claim admission until the room changes, returning a deterministic Capacity
/// terminal; it never evicts an old verdict or makes an old ID grantable again.
/// Native pickups remain available. Delivery has a separate finite deadline.</summary>
internal sealed class NetPickupClaims
{
    internal const int HistoryFrames = 128, ClaimsPerSlot = 256;
    internal const float PositionTolerance = .75f;
    private sealed record Contact(PickupContactPose Pose, PickupItemFact[] Items);
    private readonly record struct ContactKey(ShotKey Life, uint Source);
    private readonly record struct SourceClock(ShotKey Life, uint Source, uint Authority);
    private readonly Dictionary<ContactKey, Contact> _history = new();
    private readonly Queue<ContactKey>[] _order = new Queue<ContactKey>[8];
    private readonly SourceClock[] _clocks = new SourceClock[8];
    private readonly Dictionary<PickupClaimKey, PickupVerdict> _terminals = new();
    private readonly int[] _counts = new int[8];
    private readonly ulong _epoch;
    private readonly ushort _match;
    private readonly uint _room;
    private readonly NetTimedGrants _grants;
    private readonly Action<PickupContactProof>? _proved;
    internal int TerminalCount => _terminals.Count;
    internal bool TryTerminal(PickupClaimKey key, out PickupVerdict verdict) => _terminals.TryGetValue(key, out verdict);
    internal int ObservationCount => _history.Count;
    internal bool HasObservation(ShotKey life, uint source) => _history.ContainsKey(new(life, source));
    internal bool HasFollowingObservation(ShotKey life, uint source)
        => HasObservation(life, unchecked(source + 1)) || HasObservation(life, unchecked(source + 2));
    internal long CapacityRefused { get; private set; }

    internal NetPickupClaims(ulong epoch, ushort match, uint room, NetTimedGrants grants,
        Action<PickupContactProof>? proved = null)
    {
        _epoch = epoch; _match = match; _room = room; _grants = grants; _proved = proved;
        for (int i = 0; i < 8; i++) _order[i] = new();
    }
    private bool Current(ShotKey life) => life.AuthorityEpoch == _epoch && life.MatchId == _match
        && (uint)life.ShooterSlot < 8 && life.Generation != 0 && life.LifeId != 0 && life.ShotId == 0;

    // Caller supplies an immutable value snapshot BEFORE native consumption.
    // Repeated application of an old input never moves its historical contact.
    internal bool Observe(PickupContactPose pose, ReadOnlySpan<PickupItemFact> items)
    {
        if (!Current(pose.Life) || !pose.IsValid || items.Length > NetTimedGrants.SpawnCapacity) return false;
        var key = new ContactKey(pose.Life, pose.SourceFrame);
        if (_history.ContainsKey(key)) return false;
        int slot = pose.Life.ShooterSlot;
        ref var clock = ref _clocks[slot];
        var candidateClock = clock.Life == pose.Life ? clock
            : new SourceClock(pose.Life, pose.SourceFrame, pose.AuthorityFrame);
        long sourceElapsed = unchecked((int)(pose.SourceFrame - candidateClock.Source));
        long authorityElapsed = unchecked((int)(pose.AuthorityFrame - candidateClock.Authority));
        if (sourceElapsed > authorityElapsed + 32 || sourceElapsed < authorityElapsed - HistoryFrames) return false;
        for (int i = 0; i < items.Length; i++)
        {
            var fact = items[i];
            if (!fact.IsValid || fact.Spawn.Epoch != _epoch || fact.Spawn.Match != _match
                || fact.Spawn.Room != _room || fact.AuthorityFrame != pose.AuthorityFrame) return false;
            for (int j = 0; j < i; j++) if (items[j].Spawn == fact.Spawn) return false;
        }
        clock = candidateClock;
        var queue = _order[slot];
        if (queue.Count == HistoryFrames) _history.Remove(queue.Dequeue());
        _history.Add(key, new(pose, items.ToArray())); queue.Enqueue(key);
        return true;
    }

    internal static int HistoryWindow(double rttMilliseconds, double jitterMilliseconds)
    {
        if (!double.IsFinite(rttMilliseconds) || rttMilliseconds < 0
            || !double.IsFinite(jitterMilliseconds) || jitterMilliseconds < 0) return 12;
        // Full RTT plus two jitter margins and six simulation steps, capped below
        // the retained sample count. No client-provided timestamp widens this.
        return (int)Math.Clamp(Math.Ceiling((Math.Min(rttMilliseconds, 2000)
            + 2 * Math.Min(jitterMilliseconds, 1000)) * .06) + 6, 12, HistoryFrames - 8);
    }

    internal bool CanProveContact(in PickupClaim claim, uint now, double rtt, double jitter)
    { ContactFor(claim, now, HistoryWindow(rtt, jitter), out bool proved); return proved; }

    private Contact? ContactFor(in PickupClaim claim, uint now, int window, out bool proved)
    {
        proved = false;
        Contact? fallback = null;
        // Owner contact occurs AFTER input/movement. Reported authority poses
        // come from PRE-step intents, normally sent every two ticks. The next
        // one or two actual source samples can therefore carry the contact
        // pose. Never interpolate, widen native reach, or use the claim itself
        // as an observation; both positions must reach the captured item.
        for (uint offset = 0; offset <= 2; offset++)
        {
            if (!_history.TryGetValue(new(claim.Key.LifeKey, unchecked(claim.SourceFrame + offset)), out var contact)) continue;
            fallback ??= contact;
            var pose = contact.Pose;
            int age = unchecked((int)(now - pose.AuthorityFrame));
            if (age < 0 || age > window || !pose.Alive || !pose.CanPickup
                || (claim.Position - pose.Position).LengthSquared > PositionTolerance * PositionTolerance) continue;
            foreach (var item in contact.Items)
                if (item.Spawn == claim.Spawn && item.Type == claim.Type && item.Available
                    && pose.Reaches(item.Position, pose.Position) && pose.Reaches(item.Position, claim.Position))
                { proved = true; return contact; }
        }
        return fallback;
    }

    internal PickupVerdict Judge(in PickupClaim claim, int senderSlot, ShotKey currentLife, bool alive,
        uint now, double rttMilliseconds, double jitterMilliseconds, CommitPickup commit)
    {
        var request = claim;
        PickupVerdict Refuse(PickupResult result, uint proof = 0)
            => new(request, result, now, proof, 0, 0, 0);
        // Endpoint ownership is checked before cache access: a spoofed sender may
        // neither spend another slot's quota nor obtain its cached grant response.
        if ((uint)senderSlot >= 8 || claim.Key.Slot != senderSlot || !claim.IsValid)
            return Refuse(PickupResult.Invalid);
        if (_terminals.TryGetValue(claim.Key, out var cached)) return cached;
        if (claim.Key.Epoch != _epoch || claim.Key.Match != _match || claim.Spawn.Room != _room)
            return Refuse(PickupResult.Replayed);
        if (_counts[senderSlot] == ClaimsPerSlot)
        {
            CapacityRefused++;
            // No decision clock is invented: this permanent refusal is identical
            // on every retry, for the same refused request.
            return new(request, PickupResult.Capacity, 0, 0, 0, 0, 0);
        }
        _counts[senderSlot]++; // Reserve terminal storage BEFORE any native callback.
        PickupVerdict result;
        PickupContactProof? proof = null;
        if (!Current(currentLife) || claim.Key.LifeKey != currentLife || !alive)
            result = Refuse(PickupResult.Invalid);
        else if (ContactFor(claim, now, HistoryWindow(rttMilliseconds, jitterMilliseconds), out _) is not { } contact)
            result = Refuse(PickupResult.HistoryUnavailable);
        else
        {
            var pose = contact.Pose;
            int age = unchecked((int)(now - pose.AuthorityFrame));
            if (age < 0 || age > HistoryWindow(rttMilliseconds, jitterMilliseconds))
                result = Refuse(PickupResult.TooOld, pose.AuthorityFrame);
            else if (!pose.Alive || !pose.CanPickup)
                result = Refuse(PickupResult.Invalid, pose.AuthorityFrame);
            else if ((claim.Position - pose.Position).LengthSquared > PositionTolerance * PositionTolerance)
                result = Refuse(PickupResult.TooFar, pose.AuthorityFrame);
            else
            {
                PickupItemFact? item = null;
                foreach (var fact in contact.Items) if (fact.Spawn == claim.Spawn) { item = fact; break; }
                if (item is not PickupItemFact observed || observed.Type != claim.Type)
                    result = Refuse(PickupResult.Gone, pose.AuthorityFrame);
                else if (!observed.Available)
                    result = Refuse(PickupResult.Taken, pose.AuthorityFrame);
                else if (!pose.Reaches(observed.Position, pose.Position) || !pose.Reaches(observed.Position, claim.Position))
                    result = Refuse(PickupResult.TooFar, pose.AuthorityFrame);
                else
                {
                    // A native contact may have won before this packet arrived.
                    // Acknowledge that exact grant without applying or renewing it.
                    TimedGrantRecord? grant = null;
                    bool occupied = false;
                    foreach (var previous in _grants.History)
                    {
                        if (previous.Spawn != claim.Spawn) continue;
                        occupied = true;
                        if (previous.Life == currentLife && previous.Type == claim.Type) grant = previous;
                        break;
                    }
                    TimedGrantResult admission = grant != null ? TimedGrantResult.Granted
                        : occupied ? TimedGrantResult.Taken : commit(claim, now, out grant);
                    result = admission == TimedGrantResult.Granted && grant != null
                        ? new(request, PickupResult.Grant, now, pose.AuthorityFrame, grant.Sequence,
                            grant.ContactFrame, grant.ExpiresAtFrame)
                        : Refuse(admission switch
                        {
                            TimedGrantResult.Taken => PickupResult.Taken,
                            TimedGrantResult.Gone => PickupResult.Gone,
                            TimedGrantResult.Capacity => PickupResult.Capacity,
                            _ => PickupResult.Invalid
                        }, pose.AuthorityFrame);
                    if (result.Result == PickupResult.Grant)
                        proof = new(request, pose, observed, result, HistoryWindow(rttMilliseconds, jitterMilliseconds));
                }
            }
        }
        _terminals.Add(claim.Key, result);
        if (proof is PickupContactProof accepted) _proved?.Invoke(accepted);
        return result;
    }
}
