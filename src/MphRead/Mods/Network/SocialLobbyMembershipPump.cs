using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

// Opaque witness carries the existing peer/slot/connection/match/phase generation.
// The reporter never decodes a ticket or derives a Prime ID from UDP identity.
internal sealed record SocialLobbyMembershipIdentity(object Witness, ulong Epoch, uint ClientId,
    string Ticket, bool Eligible);

/// <summary>Bounded control-owner scheduler. HTTP runs only on workers.</summary>
internal sealed class SocialLobbyMembershipPump : IDisposable
{
    private sealed class State
    {
        internal SocialLobbyMembershipIdentity Identity = null!;
        internal Task<SocialLobbyMembershipReceipt>? Work;
        internal SocialLobbyMembershipIdentity? Sent;
        internal bool Present, SentLeave;
        internal int SentRefresh;
        internal double Next;
        internal double AbsentSince = double.NaN;
    }
    private readonly Func<SocialLobbyMembershipReport, CancellationToken, Task<SocialLobbyMembershipReceipt>> _send;
    private readonly Action<SocialLobbyMembershipIdentity,Guid>? _verified;
    private readonly Dictionary<(ulong, uint, string), State> _states = new();
    // Snapshot keys/states before processing asynchronous completions, just
    // like ToArray did, but without allocating a new array each poll.
    private readonly List<KeyValuePair<(ulong, uint, string), State>> _iteration = new(16);
    private readonly CancellationTokenSource _stop = new();
    private long _sequence;
    private int _refreshGeneration;
    internal long Sequence => _sequence;
    private bool _disposed;
    internal SocialLobbyMembershipPump(Func<SocialLobbyMembershipReport, CancellationToken,
        Task<SocialLobbyMembershipReceipt>> send, long initialSequence = 0,
        Action<SocialLobbyMembershipIdentity,Guid>? verified = null)
        => (_send, _sequence, _verified) = (send, initialSequence, verified);

    internal void Update(IReadOnlyList<SocialLobbyMembershipIdentity> identities, double now)
    {
        if (_disposed) return;
        foreach (State state in _states.Values) state.Present = false;
        foreach (var identity in identities)
        {
            var key = (identity.Epoch, identity.ClientId, identity.Ticket);
            if (!_states.TryGetValue(key, out State? state))
            {
                // Churn cannot create an unbounded HTTP backlog. Unreported old
                // memberships expire naturally; current live verification still applies.
                if (_states.Count >= 64) continue;
                _states.Add(key, state = new());
            }
            state.Identity = identity;
            state.Present = true;
            state.AbsentSince = double.NaN;
        }
        int running = 0;
        foreach (State state in _states.Values)
            if (state.Work is { IsCompleted: false }) running++;
        _iteration.Clear();
        foreach (var pair in _states) _iteration.Add(pair);
        foreach (var pair in _iteration)
        {
            State state = pair.Value;
            if (!state.Present && double.IsNaN(state.AbsentSince)) state.AbsentSince = now;
            if (state.Work is { IsCompleted: true } completed)
            {
                bool verified = completed.IsCompletedSuccessfully && completed.Result.Verified;
                state.Work = null;
                if (verified && !state.SentLeave && state.Present && state.Sent == state.Identity)
                    _verified?.Invoke(state.Identity, completed.Result.PlayerId);
                if (state.SentLeave && !state.Present && verified)
                { ++_refreshGeneration; _states.Remove(pair.Key); continue; }
                state.Next = now + (verified ? 20 : 5);
            }
            if (state.Work != null || running >= 8) continue;
            if (!state.Present && now - state.AbsentSince >= 90)
            { _states.Remove(pair.Key); continue; }
            // Refreshing a ticket for the same UDP client serializes old leave
            // before the new heartbeat. A leave also refreshes other current
            // identities promptly (the same account may use two clients).
            if (state.Present && _states.Any(other => other.Key.Item1 == pair.Key.Item1
                && other.Key.Item2 == pair.Key.Item2 && other.Key != pair.Key && !other.Value.Present)) continue;
            bool changed = state.Sent != state.Identity || state.SentLeave == state.Present
                || state.Present && state.SentRefresh != _refreshGeneration;
            if (!changed && now < state.Next) continue;
            if (_sequence == long.MaxValue) continue; // never wrap a sequence floor
            var identity = state.Identity;
            bool leave = !state.Present;
            var report = new SocialLobbyMembershipReport(identity.Epoch, identity.ClientId,
                identity.Ticket, ++_sequence, identity.Eligible, leave);
            state.Sent = identity; state.SentLeave = leave; state.SentRefresh = _refreshGeneration;
            state.Work = Task.Run(async () =>
            {
                try { return await _send(report, _stop.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                { return default; }
            });
            ++running;
        }
        _iteration.Clear(); // Do not retain expired identities/tickets between polls.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        // Shutdown does not wait on HTTPS. Backend membership has a 60 s TTL.
        _states.Clear();
        _iteration.Clear();
    }
}
