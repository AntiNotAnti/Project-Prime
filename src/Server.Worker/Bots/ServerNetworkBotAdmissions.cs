using System;
using System.Net;
using System.Security.Cryptography;

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
                SendJoinPending(endpoint, join);
            }
            else Refuse(endpoint, join.Nonce, "Another admission is pending.", join.AdmissionId);
            return true;
        }
        return false;
    }
    private bool QueueBotAdmission(IPEndPoint endpoint, in JoinPacket join, TicketIdentity? identity, int slot)
    {
        _botJoins[slot] = new(endpoint, join, identity, slot, MatchId, _now);
        SendJoinPending(endpoint, join);
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
                Refuse(pending.Endpoint, pending.Join.Nonce, "Pending admission expired; please retry.", pending.Join.AdmissionId);
                continue;
            }
            if (_peers[slot] != null || HasReconnectReservation(slot)) continue;
            if (CanClaimPlayerSlot?.Invoke(slot) != true) continue;
            _botJoins[slot] = null;
            // This identity was already verified; never consume its one-use ticket twice.
            byte[] key = Array.Empty<byte>();
            if (UdpAuthenticationEnabled && (TicketAuthority is not { } authority
                || !authority.TryGetAdmissionKey(pending.Join.AdmissionId, out key))) continue;
            try { Admit(pending.Endpoint, pending.Join, pending.Identity,
                UdpAuthenticationEnabled ? key : ReadOnlySpan<byte>.Empty); }
            finally { if (key.Length != 0) CryptographicOperations.ZeroMemory(key); }
        }
    }
    private void SendJoinPending(IPEndPoint endpoint, in JoinPacket join)
    {
        byte[] key = Array.Empty<byte>();
        if (UdpAuthenticationEnabled && (TicketAuthority is not { } authority
            || !authority.TryGetAdmissionKey(join.AdmissionId, out key))) return;
        try
        {
            Span<byte> packet = stackalloc byte[NetConfig.MaxPacketSize];
            Span<byte> payload = packet[NetHeader.Size..(NetHeader.Size + JoinPendingPacket.Size)];
            new JoinPendingPacket(join.Nonce).Write(payload);
            NetHeader header = new(NetMessageType.JoinPending, NetHeaderFlags.Unsequenced, 0, 0, 0, 0);
            int length;
            if (UdpAuthenticationEnabled)
                length = NetAuthentication.Sign(key, NetAuthDirection.ServerToClient, header, payload, packet);
            else { header.Write(packet); length = NetHeader.Size + payload.Length; }
            _transport.SendDatagram(endpoint, packet[..length]);
        }
        finally { if (key.Length != 0) CryptographicOperations.ZeroMemory(key); }
    }
}
