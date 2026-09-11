using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Network;

/// <summary>One match's bounded asynchronous admission queue. Signature work never runs on its tick owner.</summary>
public sealed class WorkerTicketAuthority : IServerTicketAuthority
{
    private const int Capacity = 64;
    private const int AdmissionKeyCapacity = 64;
    private readonly WorkerAdmissionVerifier _verifier;
    private readonly MatchSpec _spec;
    private readonly Channel<(IPEndPoint Endpoint, JoinPacket Join)> _requests = Channel.CreateBounded<(IPEndPoint, JoinPacket)>(Capacity);
    private readonly ConcurrentQueue<ValidatedTicketJoin> _results = new();
    private readonly Dictionary<(IPEndPoint, ulong), JoinPacket> _pending = new();
    private readonly Dictionary<(IPEndPoint, ulong), (JoinPacket Join, TicketIdentity Identity)> _accepted = new();
    private readonly Dictionary<Guid, AdmissionKeyLease> _admissionKeys = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private int _disposed;
    private long _validationFaults;
    public long ValidationFaults => Interlocked.Read(ref _validationFaults);
    public Guid ServerId { get; }
    public Guid SessionId { get; }
    public bool RequireTickets => true;
    public WorkerTicketAuthority(MatchSpec spec, MatchPlacement placement, string keyId, string publicKey)
    {
        _spec = spec; _placement = placement;
        _verifier = new WorkerAdmissionVerifier(spec, placement);
        try { _verifier.UpdatePublicKey(keyId, publicKey); }
        catch { _verifier.Dispose(); throw; }
        ServerId = placement.WorkerId.Value;
        SessionId = placement.WorkerIncarnation;
        _worker = Task.Run(WorkAsync);
    }
    public bool RetirePublicKey(string keyId) => _verifier.RetirePublicKey(keyId);
    public void UpdatePublicKey(string keyId, string publicKey) => _verifier.UpdatePublicKey(keyId, publicKey);

    /// <summary>
    /// Installs a control-plane admission secret on the match owner. This is
    /// intentionally lane-owned: callers must invoke it through the match's
    /// simulation lane and never retain a reference to the stored bytes.
    /// </summary>
    public bool TryInstallAdmissionKey(InstallAdmissionKey command, out string reason)
    {
        ExpireAdmissionKeys(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (command.NodeId != _spec.NodeId || command.NodeIncarnation != _spec.NodeIncarnation
            || command.MatchId != _spec.MatchId || command.WireMatchId != _placement.WireMatchId
            || command.WorkerId != _placement.WorkerId || command.WorkerIncarnation != _placement.WorkerIncarnation)
            return Reject(out reason, "admission_scope");
        RosterSeat? seat = _spec.Roster.FirstOrDefault(candidate => candidate.SeatId == command.SeatId
            && candidate.Role != SeatRole.Bot);
        if (seat == null) return Reject(out reason, "admission_seat");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (command.ExpiresAt <= now || command.ExpiresAt > now + 300)
            return Reject(out reason, "admission_expired");
        byte[] key;
        try { key = AdmissionKeyRules.Decode(command.AdmissionKey); }
        catch (ArgumentException) { return Reject(out reason, "admission_key"); }
        if (_admissionKeys.TryGetValue(command.AdmissionId, out AdmissionKeyLease existing))
        {
            bool same = existing.TicketId == command.TicketId && existing.NodeSessionId == command.NodeSessionId
                && existing.SeatId == command.SeatId && existing.JoinNonce == command.JoinNonce
                && existing.ExpiresAt == command.ExpiresAt
                && CryptographicOperations.FixedTimeEquals(existing.Key, key);
            CryptographicOperations.ZeroMemory(key);
            if (!same) return Reject(out reason, "admission_reuse");
            reason = "";
            return true;
        }
        if (_admissionKeys.Count >= AdmissionKeyCapacity) return Reject(out reason, "admission_capacity");
        _admissionKeys.Add(command.AdmissionId, new(command.TicketId, command.NodeSessionId, command.MatchId,
            command.WireMatchId, command.SeatId, command.JoinNonce, command.ExpiresAt, key));
        reason = "";
        return true;
    }

    /// <summary>Test/diagnostic seam that returns a copy, never the owned key buffer.</summary>
    public bool TryGetAdmissionKey(Guid admissionId, out byte[] key)
    {
        ExpireAdmissionKeys(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (_admissionKeys.TryGetValue(admissionId, out AdmissionKeyLease lease))
        {
            key = lease.Key.ToArray();
            return true;
        }
        key = Array.Empty<byte>();
        return false;
    }

    public bool ValidateAdmissionJoin(Guid admissionId, in JoinPacket join)
    {
        ExpireAdmissionKeys(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return admissionId != Guid.Empty && admissionId == join.AdmissionId
            && _admissionKeys.TryGetValue(admissionId, out AdmissionKeyLease lease)
            && lease.MatchId == _spec.MatchId && lease.WireMatchId == _placement.WireMatchId
            && lease.JoinNonce == join.Nonce && !String.IsNullOrEmpty(join.Ticket);
    }

    public bool ValidateAdmissionIdentity(Guid admissionId, in JoinPacket join, in TicketIdentity identity)
    {
        ExpireAdmissionKeys(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return ValidateAdmissionJoin(admissionId, join)
            && _admissionKeys.TryGetValue(admissionId, out AdmissionKeyLease lease)
            && identity.WorkerAdmission && identity.TicketId == lease.TicketId
            && identity.ReservedSeat == lease.SeatId
            && identity.NodeSessionId == lease.NodeSessionId;
    }
    public bool Submit(IPEndPoint endpoint, in JoinPacket join)
    {
        // Called exclusively by the owning match; retransmission cannot change endpoint or any claim.
        if (Volatile.Read(ref _disposed) != 0) return false;
        var key = (endpoint, join.Nonce);
        if (_pending.TryGetValue(key, out JoinPacket pending)) return pending == join;
        if (_pending.Count >= Capacity) return false;
        var copy = new IPEndPoint(endpoint.Address, endpoint.Port);
        _pending.Add((copy, join.Nonce), join);
        if (_requests.Writer.TryWrite((copy, join))) return true;
        _pending.Remove(key);
        return false;
    }
    public bool TryRead(out ValidatedTicketJoin result)
    {
        ExpireAdmissionKeys(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (!_results.TryDequeue(out result)) return false;
        _pending.Remove((result.Endpoint, result.Join.Nonce));
        return true;
    }
    private async Task WorkAsync()
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(_stop.Token))
            {
                TicketIdentity? identity = null;
                try
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    long seconds = now.ToUnixTimeSeconds();
                    foreach (var expired in _accepted.Where(pair => pair.Value.Identity.ExpiresAt <= seconds).Select(pair => pair.Key).ToArray())
                        _accepted.Remove(expired);
                    var retryKey = (request.Endpoint, request.Join.Nonce);
                    if (_accepted.TryGetValue(retryKey, out var accepted))
                    {
                        if (accepted.Join == request.Join) identity = accepted.Identity;
                    }
                    else if (_accepted.Count < 4096 && _verifier.TryConsume(request.Join.Ticket, request.Join, now, out WorkerAdmissionClaims? claims))
                    {
                    identity = new TicketIdentity(claims!.PlayerId, claims.TicketId, claims.ExpiresAt,
                        GuestSessionId: claims.GuestSessionId, ReservedSeat: claims.SeatId, WorkerAdmission: true,
                            ReservedTeam: _spec.Roster.First(seat => seat.SeatId == claims.SeatId).Team,
                            NodeSessionId: claims.NodeSessionId);
                        _accepted.Add(retryKey, (request.Join, identity.Value));
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    identity = null;
                    Interlocked.Increment(ref _validationFaults);
                }
                _results.Enqueue(new(request.Endpoint, request.Join, identity));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        RetireAdmissionKeys();
        _stop.Cancel(); _requests.Writer.TryComplete();
        _ = _worker.ContinueWith(_ => { _verifier.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
    }

    private readonly record struct AdmissionKeyLease(Guid TicketId, Guid NodeSessionId, MatchId MatchId,
        WireMatchId WireMatchId, byte SeatId, ulong JoinNonce, long ExpiresAt, byte[] Key);

    private void ExpireAdmissionKeys(long now)
    {
        foreach (Guid id in _admissionKeys.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
        {
            AdmissionKeyLease lease = _admissionKeys[id];
            CryptographicOperations.ZeroMemory(lease.Key);
            _admissionKeys.Remove(id);
        }
    }

    private void RetireAdmissionKeys()
    {
        foreach (AdmissionKeyLease lease in _admissionKeys.Values)
            CryptographicOperations.ZeroMemory(lease.Key);
        _admissionKeys.Clear();
    }

    private static bool Reject(out string reason, string value)
    {
        reason = value;
        return false;
    }

    private readonly MatchPlacement _placement;
}
