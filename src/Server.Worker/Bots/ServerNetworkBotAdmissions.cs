using System;
using System.Net;

namespace MphRead.Mods.Network;

public sealed partial class ServerNetwork
{
    // One pending human owns one retirement request. No socket or player slot is
    // allocated until normal safe retirement has released objectives.
    private sealed class PendingBotJoin(IPEndPoint endpoint, JoinPacket join, TicketIdentity? identity,
        int slot, uint matchId, double now)
    {
        public readonly IPEndPoint Endpoint = endpoint;
        public readonly JoinPacket Join = join;
        public readonly TicketIdentity? Identity = identity;
        public readonly int Slot = slot;
        public readonly uint MatchId = matchId;
        public readonly double Started = now;
        public double LastRetry = now;
    }
    private readonly PendingBotJoin?[] _botJoins = new PendingBotJoin[8];
    public Action<int>? CancelBotClaim { get; set; }
    public bool HasPendingBotAdmission(int slot) => _botJoins[slot] != null;
    private byte CountRosterBots()
    {
        byte count = 0;
        for (int slot = 0; slot < _capacity; slot++)
            if (_peers[slot] == null && BotRosterEntry?.Invoke(slot) != null) count++;
        return count;
    }
    private bool RetryBotAdmission(IPEndPoint endpoint, in JoinPacket join)
    {
        foreach (PendingBotJoin? pending in _botJoins)
        {
            if (pending == null || !pending.Endpoint.Equals(endpoint)) continue;
            if (pending.Join.Equals(join))
            {
                pending.LastRetry = _now;
                SendJoinPending(endpoint, join.Nonce);
            }
            else Refuse(endpoint, join.Nonce, "Another admission is pending.");
            return true;
        }
        return false;
    }
    private bool QueueBotAdmission(IPEndPoint endpoint, in JoinPacket join, TicketIdentity? identity, int slot)
    {
        _botJoins[slot] = new(endpoint, join, identity, slot, MatchId, _now);
        SendJoinPending(endpoint, join.Nonce);
        return true;
    }
    private void ProcessBotAdmissions()
    {
        for (int slot = 0; slot < _botJoins.Length; slot++)
        {
            PendingBotJoin? pending = _botJoins[slot];
            if (pending == null) continue;
            if (AdmissionClosed || pending.MatchId != MatchId || _now - pending.Started >= 30
                || _now - pending.LastRetry > NetConfig.TimeoutSeconds
                || pending.Identity is { } identity && identity.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                _botJoins[slot] = null;
                CancelBotClaim?.Invoke(slot);
                Refuse(pending.Endpoint, pending.Join.Nonce, "Pending admission expired; please retry.");
                continue;
            }
            if (_peers[slot] != null || HasReconnectReservation(slot)) continue;
            if (CanClaimPlayerSlot?.Invoke(slot) != true) continue;
            _botJoins[slot] = null;
            // This identity was already verified; never consume its one-use ticket twice.
            Admit(pending.Endpoint, pending.Join, pending.Identity);
        }
    }
    private void SendJoinPending(IPEndPoint endpoint, ulong nonce)
    {
        Span<byte> packet = stackalloc byte[NetHeader.Size + JoinPendingPacket.Size];
        new NetHeader(NetMessageType.JoinPending, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(packet);
        new JoinPendingPacket(nonce).Write(packet[NetHeader.Size..]);
        _transport.SendDatagram(endpoint, packet);
    }
}
