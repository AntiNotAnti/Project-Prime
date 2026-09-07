using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network
{
    public readonly record struct ClientReconnectRequest(
        ulong Nonce, ulong PreviousConnectionId, uint SessionId);

    /// <summary>
    /// Returns a ticket minted for <see cref="ClientReconnectRequest.Nonce"/>.
    /// The callback must never reuse the ticket from the original admission.
    /// </summary>
    public delegate ValueTask<string> FreshTicketCallback(
        ClientReconnectRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Owns the shell-to-match lifetime of the authoritative client. The socket may
    /// remain alive without a Scene; only the active pump owner may poll it.
    /// </summary>
    public sealed class ClientSessionCoordinator : IDisposable
    {
        private readonly object _gate = new();
        private readonly SessionPump _pump = new();
        private AuthoritativePlay? _session;
        private Scene? _scene;
        private FreshTicketCallback? _freshTicket;
        private ClientSessionState _state;
        private string? _failure;
        private uint _observedTransitionRevision;
        private bool _matchPending;

        public static ClientSessionCoordinator Shared { get; } = new();

        public event Action<ClientSessionState>? StateChanged;
        public event Action<MatchTransitionPacket>? MatchTransitioned;

        public ClientSessionState State { get { lock (_gate) { return _state; } } }
        public string? Failure { get { lock (_gate) { return _failure; } } }
        public AuthoritativePlay? Session { get { lock (_gate) { return _session; } } }
        public Scene? Scene { get { lock (_gate) { return _scene; } } }
        public bool MatchPending { get { lock (_gate) { return _matchPending; } } }
        public bool CanReconnect
        {
            get
            {
                lock (_gate)
                {
                    return _state == ClientSessionState.Failed && _session != null
                        && _session.Client.Failure != "Server disconnected the session.";
                }
            }
        }
        public SessionPumpOwner PumpOwner => _pump.Owner;

        private ClientSessionCoordinator() { }

        public void BeginConnecting()
        {
            lock (_gate)
            {
                if (_session != null)
                    throw new InvalidOperationException("Leave the current server session before connecting to another one.");
                _failure = null;
                _matchPending = false;
            }
            SetState(ClientSessionState.Connecting);
        }

        public void AttachSession(AuthoritativePlay session, FreshTicketCallback? freshTicket = null)
        {
            ArgumentNullException.ThrowIfNull(session);
            lock (_gate)
            {
                if (_session != null && !ReferenceEquals(_session, session))
                    throw new InvalidOperationException("A different client session is already attached.");
                _session = session;
                _freshTicket = freshTicket;
                _failure = null;
                _observedTransitionRevision = session.Client.MatchTransitionRevision;
                _matchPending = session.Client.Accepted.Destination == JoinDestination.Match;
            }
            if (!_pump.TryAcquire(SessionPumpOwner.Shell))
                throw new InvalidOperationException("The match loop still owns session polling.");
            RefreshState(session);
        }

        /// <summary>Poll from a shell timer. Returns false while the match owns polling.</summary>
        public bool Poll()
        {
            AuthoritativePlay? session;
            lock (_gate) { session = _session; }
            if (session == null) return false;
            if (_pump.Owner == SessionPumpOwner.None && !_pump.TryAcquire(SessionPumpOwner.Shell)) return false;
            try
            {
                if (!_pump.TryPoll(SessionPumpOwner.Shell, session.PollSession)) return false;
            }
            catch (Exception) when (session.Client.Failure != null)
            {
                ObserveFailure(session, throwOnFailure: false);
                return false;
            }
            return ObserveAfterPoll(session, throwOnFailure: false);
        }

        public void AttachScene(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            AuthoritativePlay session;
            lock (_gate)
            {
                session = _session ?? throw new InvalidOperationException("No authoritative client session is attached.");
                if (!_matchPending && session.Client.State == NetConnectionState.Lobby)
                    throw new InvalidOperationException("The server has not sent a match transition.");
                if (_scene != null && !ReferenceEquals(_scene, scene))
                    throw new InvalidOperationException("A different match scene is already attached.");
            }
            SessionPumpOwner owner = _pump.Owner;
            bool acquired = owner switch
            {
                SessionPumpOwner.None => _pump.TryAcquire(SessionPumpOwner.Match),
                SessionPumpOwner.Shell => _pump.Transfer(SessionPumpOwner.Shell, SessionPumpOwner.Match),
                SessionPumpOwner.Match => true,
                _ => false
            };
            if (!acquired) throw new InvalidOperationException("Session polling is active on the shell.");
            session.AttachScene(scene);
            lock (_gate)
            {
                _scene = scene;
                _matchPending = false;
            }
            SetState(ClientSessionState.LoadingMatch);
        }

        public void DetachScene(Scene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            AuthoritativePlay? session;
            lock (_gate)
            {
                if (!ReferenceEquals(_scene, scene)) return;
                session = _session;
                _scene = null;
                _matchPending = false;
            }
            _pump.Stop(() => session?.DetachScene(scene));
            if (session == null) return;
            if (session.Client.Failure != null) ObserveFailure(session, throwOnFailure: false);
            else SetState(ClientSessionState.PostMatch);
        }

        /// <summary>Dismiss post-match presentation once the server is in its lobby.</summary>
        public bool ReturnToLobby()
        {
            AuthoritativePlay? session;
            lock (_gate) { session = _session; }
            if (session?.Client.State != NetConnectionState.Lobby) return false;
            SetState(ClientSessionState.Lobby);
            return true;
        }

        public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
        {
            AuthoritativePlay? session;
            FreshTicketCallback? callback;
            lock (_gate)
            {
                if (_state != ClientSessionState.Failed || _session == null
                    || _session.Client.Failure == "Server disconnected the session.") return false;
                session = _session;
                callback = _freshTicket;
                _failure = null;
            }
            SetState(ClientSessionState.Connecting);
            try
            {
                if (session.Client.RequiresFreshTicket)
                {
                    if (callback == null) throw new InvalidOperationException("This session requires a fresh game ticket to reconnect.");
                    ulong nonce = NetConnection.NewIdentity();
                    var request = new ClientReconnectRequest(nonce,
                        session.Client.Connection?.Id ?? 0,
                        session.Client.Connection?.SessionId ?? session.Client.Accepted.SessionId);
                    string ticket = await callback(request, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    session.Client.Reconnect(nonce, ticket);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    session.Client.Reconnect();
                }
                lock (_gate)
                {
                    _observedTransitionRevision = session.Client.MatchTransitionRevision;
                    _matchPending = false;
                }
                if (_pump.Owner == SessionPumpOwner.None) _pump.TryAcquire(SessionPumpOwner.Shell);
                return true;
            }
            catch (Exception error)
            {
                lock (_gate) { _failure = error is OperationCanceledException ? "Reconnection cancelled." : error.Message; }
                SetState(ClientSessionState.Failed);
                return false;
            }
        }

        public void Leave() => TearDown(ClientSessionState.Disconnected, null);

        public void Shutdown() => TearDown(ClientSessionState.Disconnected, null);

        /// <summary>End an unrecoverable client/platform failure and retain its display reason.</summary>
        public void Fail(string reason)
        {
            if (String.IsNullOrWhiteSpace(reason)) reason = "The client session failed.";
            TearDown(ClientSessionState.Failed, reason.Trim());
        }

        internal void PollMatch(AuthoritativePlay session, Scene scene)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_session, session) || !ReferenceEquals(_scene, scene))
                    throw new InvalidOperationException("The match scene does not own this client session.");
            }
            try
            {
                if (!_pump.TryPoll(SessionPumpOwner.Match, session.PollSession))
                    throw new InvalidOperationException("The shell owns session polling.");
            }
            catch (Exception) when (session.Client.Failure != null)
            {
                ObserveFailure(session, throwOnFailure: true);
            }
            ObserveAfterPoll(session, throwOnFailure: true);
        }

        internal bool Owns(AuthoritativePlay session)
        {
            lock (_gate) { return ReferenceEquals(_session, session); }
        }

        internal void Forget(AuthoritativePlay session)
        {
            bool changed;
            lock (_gate)
            {
                if (!ReferenceEquals(_session, session)) return;
                _session = null;
                _scene = null;
                _freshTicket = null;
                _matchPending = false;
                changed = _state != ClientSessionState.Failed && _state != ClientSessionState.Leaving;
            }
            if (_pump.Owner != SessionPumpOwner.None) _pump.Release(_pump.Owner);
            if (changed) SetState(ClientSessionState.Disconnected);
        }

        public void Dispose() => Shutdown();

        private bool ObserveAfterPoll(AuthoritativePlay session, bool throwOnFailure)
        {
            lock (_gate)
            {
                // Teardown clears the attached session before it waits for the
                // active poll. A poll that just finished must not publish stale
                // state over the teardown's final Disconnected/Failed state.
                if (!ReferenceEquals(_session, session)) return false;
            }
            if (session.Client.Failure != null)
            {
                ObserveFailure(session, throwOnFailure);
                return false;
            }
            MatchTransitionPacket? transition = null;
            lock (_gate)
            {
                if (session.Client.MatchTransitionRevision != _observedTransitionRevision)
                {
                    _observedTransitionRevision = session.Client.MatchTransitionRevision;
                    _matchPending = true;
                    transition = session.Client.LastMatchTransition;
                }
            }
            RefreshState(session);
            if (transition.HasValue) MatchTransitioned?.Invoke(transition.Value);
            return true;
        }

        private void ObserveFailure(AuthoritativePlay session, bool throwOnFailure)
        {
            string reason = session.Client.Failure ?? "The server session failed.";
            if (reason == "Server disconnected the session.")
            {
                Fail(reason);
            }
            else
            {
                lock (_gate) { _failure = reason; }
                SetState(ClientSessionState.Failed);
            }
            if (throwOnFailure) throw new ProgramException(reason);
        }

        private void RefreshState(AuthoritativePlay session)
        {
            ClientSessionState current = State;
            ClientSessionState state = session.Client.State switch
            {
                NetConnectionState.Connecting => ClientSessionState.Connecting,
                NetConnectionState.Lobby => Scene != null || current == ClientSessionState.PostMatch
                    ? ClientSessionState.PostMatch : ClientSessionState.Lobby,
                NetConnectionState.Loading => ClientSessionState.LoadingMatch,
                NetConnectionState.Ready or NetConnectionState.Playing => Scene != null
                    ? ClientSessionState.InMatch : current == ClientSessionState.PostMatch
                        ? ClientSessionState.PostMatch : ClientSessionState.LoadingMatch,
                NetConnectionState.Disconnecting => current == ClientSessionState.Failed
                    ? ClientSessionState.Failed : ClientSessionState.Leaving,
                _ => ClientSessionState.Failed
            };
            SetState(state);
        }

        private void TearDown(ClientSessionState finalState, string? failure)
        {
            AuthoritativePlay? session;
            Scene? scene;
            SetState(ClientSessionState.Leaving);
            lock (_gate)
            {
                session = _session ?? AuthoritativePlay.Current;
                scene = _scene;
                _session = null;
                _scene = null;
                _freshTicket = null;
                _matchPending = false;
                _failure = failure;
            }
            _pump.Stop(() =>
            {
                if (scene != null) session?.DetachScene(scene);
                session?.Dispose();
                NetHostSession.Stop();
            });
            SetState(finalState);
        }

        private void SetState(ClientSessionState state)
        {
            Action<ClientSessionState>? changed;
            lock (_gate)
            {
                if (_state == state) return;
                _state = state;
                changed = StateChanged;
            }
            changed?.Invoke(state);
        }
    }
}
