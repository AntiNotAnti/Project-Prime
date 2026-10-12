using System;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    private static PickupReady? _pickupReadiness;
    private static uint _pickupReadinessSequence;
    private static bool _pickupReadinessSent;
    private static double _pickupReadinessStarted;
    internal static void ResetPickupReadiness()
    { _pickupReadiness = null; _pickupReadinessSent = false; }
    private static void MarkPickupSceneReady()
    {
        if (!IsClient || IsAuthority || ServerMatch is not { } match) return;
        if (_pickupReadiness is { } previous && previous.Epoch == match.AuthorityEpoch
            && previous.Match == match.MatchId) return;
        if (_pickupReadinessSequence == uint.MaxValue)
        { LastError = "Pickup readiness identity exhausted."; Stop(); return; }
        _pickupReadiness = new(match.AuthorityEpoch, match.MatchId, 0, ++_pickupReadinessSequence);
        _pickupReadinessSent = false; _pickupReadinessStarted = Clock;
    }
    private static void PumpPickupReadiness()
    {
        if (!IsClient || IsAuthority || _pickupReadiness is not { } ready || _hostEndPoint == null) return;
        if (!MatchesStream(ready.Match, ready.Epoch)) return;
        ushort generation = NetPlayerLifecycle.Generation(LocalSlot);
        if (ready.Generation != generation && generation != 0)
        {
            // A replacement admission needs its own baseline, including a same-
            // match rejoin that did not rebuild the already loaded scene.
            if (ready.Generation != 0)
            {
                if (_pickupReadinessSequence == uint.MaxValue)
                { LastError = "Pickup readiness identity exhausted."; Stop(); return; }
                ready = ready with { Sequence = ++_pickupReadinessSequence };
            }
            ready = ready with { Generation = generation }; _pickupReadiness = ready;
            _pickupReadinessSent = false; _pickupReadinessStarted = Clock;
        }
        if (_pickupReadinessSent) return;
        if (Clock - _pickupReadinessStarted >= NetPickupNetwork.DeadlineSeconds)
        { LastError = "Pickup readiness delivery exceeded its deadline."; Stop(); return; }
        if (!ready.IsValid) return;
        Span<byte> bytes = stackalloc byte[PickupReady.Size]; ready.Write(bytes);
        if (_transport?.TrySendSemantic(_hostEndPoint, PacketType.PickupReady, bytes) == true)
            _pickupReadinessSent = true;
    }
    internal static Func<int>? PickupDeliveryPending;
    internal static int PendingPickupWork
    {
        get
        {
            int count = NetPickupNetwork.PendingCount + (PickupDeliveryPending?.Invoke() ?? 0)
                + (_pickupReadiness.HasValue && !_pickupReadinessSent ? 1 : 0);
            if (IsHost) foreach (var peer in _peers)
                if (peer.Pickups != null && NetPickupNetwork.Current(peer.Pickups)) count += NetPickupNetwork.PeerPending(peer.Pickups);
            return count;
        }
    }
    private static void ConfigurePickups()
    {
        NetPickupNetwork.SendClaim = claim =>
        {
            if (_hostEndPoint == null || _transport == null) return false;
            Span<byte> bytes = stackalloc byte[PickupClaim.Size]; claim.Write(bytes);
            return _transport.TrySendSemantic(_hostEndPoint, PacketType.PickupClaim, bytes);
        };
        NetPickupNetwork.ClientFailure = message => { LastError = message; Stop(); };
        PumpPickupReadiness();
    }
    private static void HandlePickupReady(ReceivedPacket packet)
    {
        var peer = FindPeer(packet.Sender);
        if (!IsHost || peer == null || !PickupReady.TryRead(packet.Payload, out var ready)
            || !MatchesStream(ready.Match, ready.Epoch)
            || ready.Generation != NetPlayerLifecycle.Generation(peer.SlotIndex)
            || ready.Sequence <= peer.PickupReadySequence) return;
        peer.PickupReadySequence = ready.Sequence;
        peer.Pickups = NetPickupNetwork.Join();
    }
    private static void HandlePickupClaim(ReceivedPacket packet)
    {
        if (!IsHost || !WorldIsReady || FreezeGameplay) return;
        var peer = FindPeer(packet.Sender);
        if (peer == null || peer.SlotIndex < 0 || !PickupClaim.TryRead(packet.Payload, out var claim)) return;
        var timing = _transport?.ConnectionStats(peer.EndPoint);
        NetPickupNetwork.Receive(peer.SlotIndex, claim, timing?.RttMilliseconds ?? 100,
            timing?.RttJitterMilliseconds ?? 0);
    }
    private static void HandlePickupFact(ReceivedPacket packet)
    {
        if (!WorldIsReady || _playback || !NetFactBatch.Validate(packet.Type, packet.Payload)) return;
        int stride = NetFactBatch.Stride(packet.Type);
        for (int at = 0; at < packet.Payload.Length; at += stride)
        {
            var bytes = packet.Payload.Slice(at, stride);
            if (packet.Type == PacketType.PickupItem && PickupItemFact.TryRead(bytes, out var item))
                NetPickupNetwork.ReceiveItem(item);
            else if (packet.Type == PacketType.PickupVerdict && PickupVerdict.TryRead(bytes, out var verdict))
                NetPickupNetwork.ReceiveVerdict(verdict);
        }
    }
    private static void PumpHostPickups()
    {
        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (peer.Pickups == null || !NetPickupNetwork.Current(peer.Pickups)) continue;
            if (!NetPickupNetwork.Pump(peer.Pickups, peer.SlotIndex, (type, payload) =>
                _transport?.TrySendSemantic(peer.EndPoint, type, payload) == true))
            {
                _transport?.Send(peer.EndPoint, PacketType.Bye, ReadOnlySpan<byte>.Empty);
                _peers.RemoveAt(i); NetPlayerLifecycle.SetOccupant(peer.SlotIndex, 0); BroadcastHostControl();
            }
        }
    }
}
