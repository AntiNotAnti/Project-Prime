using System;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    private static NetClientJoinOwnership? _partyReservationOwner;
    private static PartyReservationRequestPacket? _partyReservationRequest;
    private static PartyReservationStatePacket? _partyReservationState;
    private static double _partyReservationExpires;
    private static WeakReference<object>? _partyReservationAccountWitness;
    internal static bool BeginNativePartyReservation(PartyReservationRequestPacket request, object accountWitness)
    {
        lock (ClientLifecycleGate)
        {
            if (accountWitness == null || !IsClient || !PersistentLobby || _playback || LocalSlot < 0 || _transport == null || _hostEndPoint == null
                || request.ClientId != ClientId || request.Purpose != PartyReservationPurpose.LeaderCreate
                || ServerSession is not { Phase: SessionPhase.Lobby } current || current.MatchId != request.MatchId
                || current.AuthorityEpoch != request.AuthorityEpoch || !request.Validate()) return false;
            _partyReservationOwner = new(); _partyReservationOwner.Capture();
            _partyReservationRequest = request with { CareerTicket = "" }; _partyReservationState = null;
            _partyReservationAccountWitness = new(accountWitness);
            byte[] payload = new byte[request.EncodedSize]; request.Write(payload);
            _transport.Send(_hostEndPoint, PacketType.PartyReservationRequest, payload); return true;
        }
    }
    private static void ReceiveNativePartyReservation(ReceivedPacket packet)
    {
        lock (ClientLifecycleGate)
        {
            if (_playback || _partyReservationOwner?.IsCurrent != true || _partyReservationRequest is not { } request
                || !PartyReservationStatePacket.TryRead(packet.Payload, out var state)
                || state.RequestId != request.RequestId || state.OperationNonce != request.OperationNonce
                || state.PartyRevision != request.PartyRevision || state.TravelId != request.TravelId
                || state.TravelRevision != request.TravelRevision || state.MatchId != request.MatchId
                || state.AuthorityEpoch != request.AuthorityEpoch
                || _partyReservationState is { } old && state.Revision <= old.Revision) return;
            _partyReservationState = state;
            _partyReservationExpires = Clock + state.RemainingTicks / (double)Render.FrameTiming.SimulationHz;
        }
    }
    private static bool PartyAccountWitness(object? expected)
    {
        if (expected == null || _partyReservationAccountWitness == null) return false;
        return _partyReservationAccountWitness.TryGetTarget(out var witness) && ReferenceEquals(witness, expected);
    }
    internal static PartyReservationStatePacket? CaptureNativePartyReservation(string request, object? accountWitness)
    {
        lock (ClientLifecycleGate) return PartyAccountWitness(accountWitness) && _partyReservationOwner?.IsCurrent == true
            && _partyReservationState is { } state && state.RequestId == request
            && ServerSession is { Phase: SessionPhase.Lobby } session && session.MatchId == state.MatchId
            && session.AuthorityEpoch == state.AuthorityEpoch ? state : null;
    }
    internal static int NativePartyReservationSeconds
    { get { lock (ClientLifecycleGate) return Math.Max(0, (int)Math.Ceiling(_partyReservationExpires - Clock)); } }
    internal static bool CancelNativePartyReservation(string request, object? accountWitness)
    {
        lock (ClientLifecycleGate)
        {
            if (!PartyAccountWitness(accountWitness) || _partyReservationOwner?.IsCurrent != true || _partyReservationState is not { } state
                || state.RequestId != request || _transport == null || _hostEndPoint == null) return false;
            var action = new PartyReservationActionPacket(1, PartyReservationWireAction.CancelGroup,
                state.OperationNonce, state.Revision, request, state.ReservationId, state.MemberLeaseNonce,
                state.MatchId, state.AuthorityEpoch);
            byte[] payload = new byte[action.EncodedSize]; action.Write(payload);
            _transport.Send(_hostEndPoint, PacketType.PartyReservationAction, payload); return true;
        }
    }
    private static void ClearNativePartyReservation()
    {
        _partyReservationOwner = null; _partyReservationRequest = null; _partyReservationState = null;
        _partyReservationAccountWitness = null; _partyReservationExpires = 0;
    }
}
