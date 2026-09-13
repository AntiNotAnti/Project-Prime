using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const int Capacity = MultiplayerLimits.MaxHandoffQueue;
    private const int AdmissionKeyCapacity = MultiplayerLimits.MaxAdmissionLeasesPerMatch;
    // Admission owners are a bounded match-lifetime set.  Keeping the last
    // accepted generation after a lease is retired/expired prevents delayed
    // control messages from reopening an older lease without retaining keys.
    private const int GenerationHistoryCapacity = MultiplayerLimits.MaxHandoffQueue;
    private readonly WorkerAdmissionVerifier _verifier;
    private readonly MatchSpec _spec;
    private readonly Channel<(IPEndPoint Endpoint, JoinPacket Join)> _requests = Channel.CreateBounded<(IPEndPoint, JoinPacket)>(Capacity);
    private readonly ConcurrentQueue<ValidatedTicketJoin> _results = new();
    private readonly Dictionary<(IPEndPoint, ulong), JoinPacket> _pending = new();
    private readonly Dictionary<(IPEndPoint, ulong), (JoinPacket Join, TicketIdentity Identity)> _accepted = new();
    private readonly Dictionary<Guid, AdmissionKeyLease> _admissionKeys = new();
    private readonly Dictionary<AdmissionOwner, Guid> _admissionByOwner = new();
    private readonly Dictionary<AdmissionOwner, HandoffGeneration> _generationHighWater = new();
    private readonly Dictionary<Guid, RetiredAdmission> _retiredAdmissions = new();
    private readonly Guid[] _expiredAdmissionIds = new Guid[AdmissionKeyCapacity];
    private readonly (IPEndPoint Endpoint, ulong Nonce)[] _expiredAccepted = new (IPEndPoint, ulong)[4096];
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
        => TryInstallAdmissionKey(command, out reason, out _);

    /// <summary>Installs a lease and reports the exact superseded admission.</summary>
    public bool TryInstallAdmissionKey(InstallAdmissionKey command, out string reason,
        out Guid supersededAdmissionId)
    {
        supersededAdmissionId = Guid.Empty;
        long nowTimestamp = Stopwatch.GetTimestamp();
        ExpireAdmissionKeys(nowTimestamp);
        if (command.NodeId != _spec.NodeId || command.NodeIncarnation != _spec.NodeIncarnation
            || command.MatchId != _spec.MatchId || command.WireMatchId != _placement.WireMatchId
            || command.WorkerId != _placement.WorkerId || command.WorkerIncarnation != _placement.WorkerIncarnation)
            return Reject(out reason, "admission_scope");
        try { command.HandoffGeneration.Validate(); }
        catch (ArgumentException) { return Reject(out reason, "admission_generation"); }
        RosterSeat? seat = _spec.Roster.FirstOrDefault(candidate => candidate.SeatId == command.SeatId
            && candidate.Role != SeatRole.Bot);
        if (seat == null) return Reject(out reason, "admission_seat");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (command.ExpiresAt <= now || command.ExpiresAt > now + 300)
            return Reject(out reason, "admission_expired");
        byte[] key;
        try { key = AdmissionKeyRules.Decode(command.AdmissionKey); }
        catch (ArgumentException) { return Reject(out reason, "admission_key"); }
        HandoffGeneration generation = command.HandoffGeneration;
        if (_admissionKeys.TryGetValue(command.AdmissionId, out AdmissionKeyLease existing))
        {
            bool same = existing.TicketId == command.TicketId && existing.NodeSessionId == command.NodeSessionId
                && existing.SeatId == command.SeatId && existing.JoinNonce == command.JoinNonce
                && existing.ExpiresAt == command.ExpiresAt && existing.HandoffGeneration == generation
                && CryptographicOperations.FixedTimeEquals(existing.Key, key);
            CryptographicOperations.ZeroMemory(key);
            if (!same) return Reject(out reason, "admission_reuse");
            reason = "";
            return true;
        }
        AdmissionOwner owner = new(command.MatchId, command.NodeSessionId, command.SeatId);
        if (_generationHighWater.TryGetValue(owner, out HandoffGeneration highWater)
            && generation.Value <= highWater.Value)
        {
            CryptographicOperations.ZeroMemory(key);
            return Reject(out reason, "admission_stale_generation");
        }

        AdmissionKeyLease current = default;
        bool hasCurrent = _admissionByOwner.TryGetValue(owner, out Guid currentId)
            && _admissionKeys.TryGetValue(currentId, out current);
        if (!hasCurrent && !_generationHighWater.ContainsKey(owner)
            && _generationHighWater.Count >= GenerationHistoryCapacity)
        {
            CryptographicOperations.ZeroMemory(key);
            return Reject(out reason, "admission_generation_capacity");
        }
        if (hasCurrent)
        {
            // A current lease occupies one slot, so a newer generation can
            // replace it even when the bounded key table is otherwise full.
            if (generation.Value <= current.HandoffGeneration.Value)
            {
                CryptographicOperations.ZeroMemory(key);
                return Reject(out reason, "admission_stale_generation");
            }
        }
        else if (_admissionKeys.Count >= AdmissionKeyCapacity)
        {
            CryptographicOperations.ZeroMemory(key);
            return Reject(out reason, "admission_capacity");
        }
        long remainingSeconds = command.ExpiresAt - now;
        long deadline;
        try
        {
            long durationTicks = checked(remainingSeconds * Stopwatch.Frequency);
            deadline = checked(nowTimestamp + Math.Max(1, durationTicks));
        }
        catch (OverflowException)
        {
            CryptographicOperations.ZeroMemory(key);
            return Reject(out reason, "admission_expired");
        }
        if (hasCurrent)
        {
            _admissionKeys.Remove(currentId);
            _admissionByOwner.Remove(owner);
            RememberRetired(currentId, owner, current.HandoffGeneration);
            CryptographicOperations.ZeroMemory(current.Key);
            supersededAdmissionId = currentId;
        }
        try
        {
            _admissionKeys.Add(command.AdmissionId, new(command.TicketId, command.NodeSessionId, command.MatchId,
                command.WireMatchId, command.SeatId, command.JoinNonce, command.ExpiresAt, deadline, generation, key));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        _admissionByOwner[owner] = command.AdmissionId;
        _generationHighWater[owner] = generation;
        reason = "";
        return true;
    }

    /// <summary>Retires only the exact admission tuple named by the Node.</summary>
    public bool TryRetireAdmission(RetireAdmission command, out string reason)
    {
        reason = "";
        if (command.MatchId != _spec.MatchId || command.WorkerId != _placement.WorkerId
            || command.WorkerIncarnation != _placement.WorkerIncarnation)
            return Reject(out reason, "admission_scope");
        AdmissionOwner owner = new(command.MatchId, command.NodeSessionId, command.SeatId);
        if (!_admissionByOwner.TryGetValue(owner, out Guid currentId))
        {
            return _retiredAdmissions.TryGetValue(command.AdmissionId, out RetiredAdmission retired)
                && retired.Owner == owner && retired.Generation == command.HandoffGeneration;
        }
        if (!_admissionKeys.TryGetValue(currentId, out AdmissionKeyLease lease))
        {
            _admissionByOwner.Remove(owner);
            return _retiredAdmissions.TryGetValue(command.AdmissionId, out RetiredAdmission retired)
                && retired.Owner == owner && retired.Generation == command.HandoffGeneration;
        }
        if (currentId != command.AdmissionId || lease.HandoffGeneration != command.HandoffGeneration)
            return Reject(out reason, "admission_stale_generation");
        _admissionByOwner.Remove(owner);
        _admissionKeys.Remove(currentId);
        RememberRetired(currentId, owner, lease.HandoffGeneration);
        CryptographicOperations.ZeroMemory(lease.Key);
        return true;
    }

    /// <summary>Test/diagnostic seam that returns a copy, never the owned key buffer.</summary>
    public bool TryGetAdmissionKey(Guid admissionId, out byte[] key)
    {
        ExpireAdmissionKeys(Stopwatch.GetTimestamp());
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
        ExpireAdmissionKeys(Stopwatch.GetTimestamp());
        return admissionId != Guid.Empty && admissionId == join.AdmissionId
            && _admissionKeys.TryGetValue(admissionId, out AdmissionKeyLease lease)
            && lease.MatchId == _spec.MatchId && lease.WireMatchId == _placement.WireMatchId
            && lease.JoinNonce == join.Nonce && !String.IsNullOrEmpty(join.Ticket);
    }

    public bool ValidateAdmissionIdentity(Guid admissionId, in JoinPacket join, in TicketIdentity identity)
    {
        if (!identity.WorkerAdmission) return false;
        try { identity.HandoffGeneration.Validate(); }
        catch (ArgumentException) { return false; }
        return ValidateAdmissionJoin(admissionId, join)
            && _admissionKeys.TryGetValue(admissionId, out AdmissionKeyLease lease)
            && identity.TicketId == lease.TicketId
            && identity.ReservedSeat == lease.SeatId
            && identity.NodeSessionId == lease.NodeSessionId
            && identity.HandoffGeneration == lease.HandoffGeneration;
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
        ExpireAdmissionKeys(Stopwatch.GetTimestamp());
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
                    int expiredAcceptedCount = 0;
                    foreach (var pair in _accepted)
                    {
                        if (pair.Value.Identity.ExpiresAt <= seconds
                            && expiredAcceptedCount < _expiredAccepted.Length)
                            _expiredAccepted[expiredAcceptedCount++] = pair.Key;
                    }
                    for (int i = 0; i < expiredAcceptedCount; i++)
                        _accepted.Remove(_expiredAccepted[i]);
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
                            NodeSessionId: claims.NodeSessionId, HandoffGeneration: claims.HandoffGeneration);
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

    private readonly record struct AdmissionOwner(MatchId MatchId, Guid NodeSessionId, byte SeatId);
    private readonly record struct RetiredAdmission(AdmissionOwner Owner, HandoffGeneration Generation);
    private readonly record struct AdmissionKeyLease(Guid TicketId, Guid NodeSessionId, MatchId MatchId,
        WireMatchId WireMatchId, byte SeatId, ulong JoinNonce, long ExpiresAt,
        long ExpiresAtTimestamp, HandoffGeneration HandoffGeneration, byte[] Key);

    private void ExpireAdmissionKeys(long nowTimestamp)
    {
        int expiredCount = 0;
        foreach (var pair in _admissionKeys)
        {
            if (pair.Value.ExpiresAtTimestamp <= nowTimestamp
                && expiredCount < _expiredAdmissionIds.Length)
                _expiredAdmissionIds[expiredCount++] = pair.Key;
        }
        for (int i = 0; i < expiredCount; i++)
        {
            Guid id = _expiredAdmissionIds[i];
            AdmissionKeyLease lease = _admissionKeys[id];
            CryptographicOperations.ZeroMemory(lease.Key);
            _admissionKeys.Remove(id);
            AdmissionOwner owner = new(lease.MatchId, lease.NodeSessionId, lease.SeatId);
            _admissionByOwner.Remove(owner);
            RememberRetired(id, owner, lease.HandoffGeneration);
        }
    }

    private void RetireAdmissionKeys()
    {
        foreach (AdmissionKeyLease lease in _admissionKeys.Values)
            CryptographicOperations.ZeroMemory(lease.Key);
        _admissionKeys.Clear();
        _admissionByOwner.Clear();
        _generationHighWater.Clear();
        _retiredAdmissions.Clear();
    }

    private void RememberRetired(Guid admissionId, AdmissionOwner owner, HandoffGeneration generation)
    {
        if (_retiredAdmissions.Count >= GenerationHistoryCapacity
            && !_retiredAdmissions.ContainsKey(admissionId))
            _retiredAdmissions.Remove(_retiredAdmissions.Keys.First());
        _retiredAdmissions[admissionId] = new(owner, generation);
    }

    private static bool Reject(out string reason, string value)
    {
        reason = value;
        return false;
    }

    private readonly MatchPlacement _placement;
}
