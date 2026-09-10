using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Network;

/// <summary>One match's bounded asynchronous admission queue. Signature work never runs on its tick owner.</summary>
public sealed class WorkerTicketAuthority : IServerTicketAuthority
{
    private const int Capacity = 64;
    private readonly WorkerAdmissionVerifier _verifier;
    private readonly MatchSpec _spec;
    private readonly Channel<(IPEndPoint Endpoint, JoinPacket Join)> _requests = Channel.CreateBounded<(IPEndPoint, JoinPacket)>(Capacity);
    private readonly ConcurrentQueue<ValidatedTicketJoin> _results = new();
    private readonly Dictionary<(IPEndPoint, ulong), JoinPacket> _pending = new();
    private readonly Dictionary<(IPEndPoint, ulong), (JoinPacket Join, TicketIdentity Identity)> _accepted = new();
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
        _spec = spec;
        _verifier = new WorkerAdmissionVerifier(spec, placement);
        try { _verifier.UpdatePublicKey(keyId, publicKey); }
        catch { _verifier.Dispose(); throw; }
        ServerId = placement.WorkerId.Value;
        SessionId = placement.WorkerIncarnation;
        _worker = Task.Run(WorkAsync);
    }
    public bool RetirePublicKey(string keyId) => _verifier.RetirePublicKey(keyId);
    public void UpdatePublicKey(string keyId, string publicKey) => _verifier.UpdatePublicKey(keyId, publicKey);
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
                            ReservedTeam: _spec.Roster.First(seat => seat.SeatId == claims.SeatId).Team);
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
        _stop.Cancel(); _requests.Writer.TryComplete();
        _ = _worker.ContinueWith(_ => { _verifier.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
    }
}
