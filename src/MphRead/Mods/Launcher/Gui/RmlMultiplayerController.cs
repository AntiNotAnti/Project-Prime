#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui
{
    internal interface IRmlLobbyQueue : IDisposable
    {
        bool Admitted { get; }
        bool Ended { get; }
        bool SeatAvailable { get; }
        int Position { get; }
        int QueueLength { get; }
        int RemainingOfferSeconds { get; }
        ulong OfferId { get; }
        ulong OfferEpoch { get; }
        string? Error { get; }
        string Status { get; }
        void Poll();
        bool AcceptSeat();
        bool DeclineSeat();
    }

    internal sealed class RmlLobbyQueue : IRmlLobbyQueue
    {
        internal LobbyQueueClient Client { get; }
        internal RmlLobbyQueue(LobbyQueueClient client) => Client = client;
        public bool Admitted => Client.Admitted;
        public bool Ended => Client.Ended;
        public bool SeatAvailable => Client.SeatAvailable;
        public int Position => Client.Position;
        public int QueueLength => Client.QueueLength;
        public int RemainingOfferSeconds => Client.RemainingOfferSeconds;
        public ulong OfferId => Client.Offer?.OfferId ?? 0;
        public ulong OfferEpoch => Client.Offer?.AuthorityEpoch ?? 0;
        public string? Error => Client.Error;
        public string Status => Client.Status;
        public void Poll() => Client.Poll();
        public bool AcceptSeat() => Client.AcceptSeat();
        public bool DeclineSeat() => Client.DeclineSeat();
        public void Dispose() => Client.Dispose();
    }

    internal sealed record RmlMultiplayerQueueSnapshot(long Version, bool Visible, string Status,
        string Position, int OfferSeconds, bool CanJoin, bool CanAccept, bool CanDecline, bool Busy);

    internal interface IRmlMultiplayerServices
    {
        Core.SocialPartyView? CurrentParty { get; }
        ulong AuthorityEpoch { get; }
        object? ConnectionIdentity { get; }
        Task<ServerDiscoveryResult> DiscoverAsync(Action<ServerBrowserEntry> onEntry, CancellationToken token);
        Task<QuickPlaySearchResult> FindBestAsync(int requiredSlots, bool lobbyOnly, CancellationToken token);
        Task<ServerStatus> ProbeAsync(string host, int port, CancellationToken token);
        Task<Core.SocialLeaderAdmissionResult> PrepareReservationAsync(ServerBrowserEntry entry, CancellationToken token);
        Task CancelReservationAsync(string requestId);
        Task<OnlineJoinResult> JoinAsync(string host, int port, string player, Hunter hunter, int suit,
            bool spectate, PartyReservedAdmission? admission, CancellationToken token);
        Task<IRmlLobbyQueue> OpenQueueAsync(string host, int port, CancellationToken token);
        Task<OnlineJoinResult> JoinQueuedAsync(string host, int port, string player, Hunter hunter, int suit,
            IRmlLobbyQueue queue, CancellationToken token);
        void StopOwnedConnection(object? identity);
        void NotePartyTravel();
    }

    internal sealed class RmlMultiplayerServices : IRmlMultiplayerServices
    {
        public Core.SocialPartyView? CurrentParty => Core.SocialMatchmaking.CurrentParty;
        public ulong AuthorityEpoch => NetSession.AuthorityEpoch;
        public object? ConnectionIdentity => NetSession.ConnectionIdentity;
        public Task<ServerDiscoveryResult> DiscoverAsync(Action<ServerBrowserEntry> onEntry, CancellationToken token)
            => ServerBrowserService.DiscoverAsync(onEntry, token);
        public Task<QuickPlaySearchResult> FindBestAsync(int requiredSlots, bool lobbyOnly, CancellationToken token)
            => ServerBrowserService.FindBestAsync(requiredSlots, lobbyOnly, token);
        public Task<ServerStatus> ProbeAsync(string host, int port, CancellationToken token)
            => ServerBrowserService.ProbeAsync(host, port, allowJoinProbe: false, cancellationToken: token);
        public Task<Core.SocialLeaderAdmissionResult> PrepareReservationAsync(ServerBrowserEntry entry, CancellationToken token)
            => Core.SocialMatchmaking.PrepareLeaderReservationAsync(entry, token);
        public async Task CancelReservationAsync(string requestId)
            => await Core.SocialMatchmaking.CancelReservationAsync(requestId).ConfigureAwait(false);
        public Task<OnlineJoinResult> JoinAsync(string host, int port, string player, Hunter hunter, int suit,
            bool spectate, PartyReservedAdmission? admission, CancellationToken token)
            => ServerBrowserService.JoinAsync(host, port, player, hunter, suit, token,
                spectate: spectate, partyAdmission: admission);
        public async Task<IRmlLobbyQueue> OpenQueueAsync(string host, int port, CancellationToken token)
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            IPAddress address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException("The server has no supported address.");
            return new RmlLobbyQueue(new LobbyQueueClient(new IPEndPoint(address, port)));
        }
        public Task<OnlineJoinResult> JoinQueuedAsync(string host, int port, string player, Hunter hunter, int suit,
            IRmlLobbyQueue queue, CancellationToken token)
            => ServerBrowserService.JoinAsync(host, port, player, hunter, suit, token,
                queuedAdmission: ((RmlLobbyQueue)queue).Client);
        public void StopOwnedConnection(object? identity)
        {
            if (identity != null && ReferenceEquals(identity, NetSession.ConnectionIdentity)) NetSession.Stop();
        }
        public void NotePartyTravel() => Core.SocialMatchmaking.NoteQuickPlayTravel();
    }

    /// <summary>
    /// Multiplayer's first native-RmlUi presenter. This layer never owns a
    /// network session or invents lobby authority: it delegates matchmaking,
    /// directory discovery and connection to the same presentation-neutral
    /// services as PlayWorkspace/CreateServerScreen. All UI updates and
    /// completion handoffs are published on Shell's render/UI thread via Tick.
    /// </summary>
    internal sealed class RmlMultiplayerController : IDisposable
    {
        private static readonly GameMode[] Modes =
        {
            GameMode.Battle, GameMode.BattleTeams, GameMode.Survival,
            GameMode.Bounty, GameMode.Capture, GameMode.Nodes
        };
        private readonly ConcurrentQueue<Action> _completions = new();
        private readonly object _completionGate = new();
        private static readonly SemaphoreSlim ConnectionGate = new(1, 1);
        private readonly IRmlMultiplayerServices _services;
        private readonly List<ServerBrowserEntry> _servers = new();
        private string[] _rooms;
        private Core.CommunityHostRequest? _requiredMap;
        private CancellationTokenSource? _operation;
        private enum PendingActivity { None, Browse, Quick, Connect, Create, Queue }
        private PendingActivity _pendingActivity;
        private int _generation;
        private int _roomIndex;
        private int _modeIndex;
        private int _serverKind;
        private bool _visible;
        private bool _busy;
        private bool _dirty;
        private bool _disposed;
        private string _status = "CHOOSE A MULTIPLAYER ACTIVITY";
        private sealed record QueueContext(string Host, int Port, string Player, Hunter Hunter, int Suit,
            ulong AuthorityEpoch, Action<string>? ReportFailure);
        private QueueContext? _queueContext;
        private IRmlLobbyQueue? _queue;
        private string _queueStatus = "";
        private ulong _acceptedQueueOffer;
        private RmlMultiplayerQueueSnapshot _queueView = new(0, false, "", "", 0, false, false, false, false);

        public event Action<LaunchPlan>? Connected;
        public bool Visible => _visible;
        public RmlMultiplayerQueueSnapshot QueueSnapshot => _queueView;

        private readonly Action<string, string> _setText;
        private readonly Action<string, bool> _setBool;
        private readonly Action<string, string> _setField;
        private readonly Func<string, string> _readField;

#if !ANDROID
        public RmlMultiplayerController(IReadOnlyList<string> rooms)
            : this(rooms, RmlUiPrototype.SetMenuText, RmlUiPrototype.SetMenuBool,
                RmlUiPrototype.SetFieldValue, RmlUiPrototype.ReadFieldValue) { }
#endif
        public RmlMultiplayerController(IReadOnlyList<string> rooms, Action<string, string> setText,
            Action<string, bool> setBool, Action<string, string> setField, Func<string, string> readField,
            IRmlMultiplayerServices? services = null)
        {
            _services = services ?? new RmlMultiplayerServices();
            _setText = setText ?? throw new ArgumentNullException(nameof(setText));
            _setBool = setBool ?? throw new ArgumentNullException(nameof(setBool));
            _setField = setField ?? throw new ArgumentNullException(nameof(setField));
            _readField = readField ?? throw new ArgumentNullException(nameof(readField));
            _rooms = rooms.Where(room => !String.IsNullOrWhiteSpace(room))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _modeIndex = Math.Max(0, Array.IndexOf(Modes, LauncherPrefs.LastLobbyMode));
        }

        public void Open(bool quickPlay)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _requiredMap = null;
            _visible = true;
            _roomIndex = 0;
            _serverKind = 0; // Hosted is the default; dedicated is explicit.
            _setField("play_player_name",
                String.IsNullOrWhiteSpace(LauncherPrefs.PlayerName) ? "Player" : LauncherPrefs.PlayerName);
            _setField("play_join_address",
                $"{LauncherPrefs.ServerAddress}:{LauncherPrefs.ServerPort}");
            _setField("play_create_name",
                $"{(String.IsNullOrWhiteSpace(LauncherPrefs.PlayerName) ? "Player" : LauncherPrefs.PlayerName)}'s lobby");
            RefreshStatic();
            if (quickPlay) QuickPlay();
            else Browse();
        }

        public void Cancel()
        {
            RetireQueue();
            _requiredMap = null;
            _visible = false;
            _generation++;
            _operation?.Cancel();
            _operation?.Dispose();
            _operation = null;
            _busy = false;
            _pendingActivity = PendingActivity.None;
        }

        public void Tick()
        {
            int count = 0;
            while (count++ < 32 && _completions.TryDequeue(out Action? complete))
                complete();
            PollQueue();
            RefreshQueueSnapshot();
            if (!_visible || !_dirty)
                return;
            _dirty = false;
            Publish();
        }

        public void Browse()
        {
            if (!_visible) return;
            (int generation, CancellationToken token) = Begin("SEARCHING DIRECTORY", PendingActivity.Browse);
            _servers.Clear();
            _dirty = true;
            _ = DiscoverAsync(generation, token);
        }

        private async Task DiscoverAsync(int generation, CancellationToken token)
        {
            try
            {
                ServerDiscoveryResult result = await _services.DiscoverAsync(entry =>
                    Post(generation, () =>
                    {
                        if (!_visible || !entry.Live) return;
                        if (_servers.Any(row => row.Endpoint.Equals(entry.Endpoint,
                            StringComparison.OrdinalIgnoreCase))) return;
                        _servers.Add(entry);
                        _servers.Sort((a, b) => (a.Status.Latency < 0
                            ? Int32.MaxValue : a.Status.Latency).CompareTo(
                            b.Status.Latency < 0 ? Int32.MaxValue : b.Status.Latency));
                        _dirty = true;
                    }), token);
                Post(generation, () => FinishOperation(
                    result.Live > 0 ? $"{result.Live} LIVE SERVERS" : result.Message));
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Post(generation, () => FinishOperation("DIRECTORY UNAVAILABLE. RETRY.")); }
        }

        public void QuickPlay()
        {
            if (!_visible) return;
            if (_busy && _pendingActivity != PendingActivity.Browse) return;
            Core.SocialPartyView? party = PartyForJoin(spectate: false);
            (int generation, CancellationToken token) = Begin("QUICK PLAY // SEARCHING", PendingActivity.Quick);
            _ = QuickPlayAsync(generation, token, party, LocalPlayer(), LauncherPrefs.LastHunter, LauncherPrefs.LastColor);
        }

        private async Task QuickPlayAsync(int generation, CancellationToken token, Core.SocialPartyView? party,
            string player, Hunter hunter, int suit)
        {
            try
            {
                QuickPlaySearchResult result = await _services.FindBestAsync(
                    party?.MemberCount ?? 1, party != null, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!result.Found)
                {
                    Post(generation, () => FinishOperation(result.Message));
                    return;
                }
                await JoinAsync(generation, token, result.Entry.Listing.Address, result.Entry.Listing.Port,
                    player, hunter, suit, false, result.Entry, party).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Post(generation, () => FinishOperation("QUICK PLAY UNAVAILABLE. RETRY.")); }
        }

        public void JoinSelected(int row, bool spectate = false)
        {
            if (row < 0 || row >= _servers.Count)
            {
                Status("SELECT A LIVE SERVER FIRST");
                return;
            }
            ServerBrowserEntry entry = _servers[row];
            if (!entry.Live || !entry.Compatible)
            {
                Status("SERVER UNAVAILABLE OR INCOMPATIBLE");
                return;
            }
            StartJoin(entry.Listing.Address, entry.Listing.Port, spectate, entry);
        }

        public void JoinEndpoint(string address, bool spectate)
        {
            // Live server rows are actionable before the last directory probe
            // finishes. A join cancels that discovery instead of waiting for
            // the slowest offline listing.
            if (!_visible || (_busy && _pendingActivity != PendingActivity.Browse))
                return;
            if (!ServerBrowserService.TryParseEndpoint(address,
                LauncherPrefs.ServerAddress, LauncherPrefs.ServerPort,
                out string host, out int port))
            {
                Status("ENTER A VALID HOST:PORT");
                return;
            }

            StartJoin(host, port, spectate, null);
        }

        private Core.SocialPartyView? PartyForJoin(bool spectate)
            => !spectate && _services.CurrentParty is { IsLeader: true, MemberCount: > 1 } party ? party : null;

        private void StartJoin(string host, int port, bool spectate, ServerBrowserEntry? entry)
        {
            if (!_visible || (_busy && _pendingActivity != PendingActivity.Browse)) return;
            (int generation, CancellationToken token) = Begin(
                $"CONNECTING TO {host}:{port}", PendingActivity.Connect);
            _ = JoinAsync(generation, token, host, port, LocalPlayer(), LauncherPrefs.LastHunter,
                LauncherPrefs.LastColor, spectate, entry, PartyForJoin(spectate));
        }

        public void JoinSocial(Core.SocialJoinRequest request, Action<string> reportFailure)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(reportFailure);
            if (_disposed || (_busy && _pendingActivity != PendingActivity.Browse))
            {
                request.Dispose();
                reportFailure("Another connection is already in progress. Retry after it finishes.");
                return;
            }
            // Keep the Social document and its drafts alive until Connected is accepted.
            _visible = true;
            _requiredMap = null;
            (int generation, CancellationToken token) = Begin(
                $"CONNECTING TO {request.ServerName}", PendingActivity.Connect);
            _ = JoinAsync(generation, token, request.Host, request.Port,
                String.IsNullOrWhiteSpace(LauncherPrefs.PlayerName) ? "Player" : LauncherPrefs.PlayerName,
                LauncherPrefs.LastHunter, LauncherPrefs.LastColor, false, null, null, request, reportFailure);
        }

        private async Task JoinAsync(int generation, CancellationToken token, string host,
            int port, string player, Hunter hunter, int suit, bool spectate,
            ServerBrowserEntry? entry, Core.SocialPartyView? party,
            Core.SocialJoinRequest? social = null, Action<string>? reportFailure = null,
            IRmlLobbyQueue? queued = null, ulong queueEpoch = 0)
        {
            PartyReservedAdmission? admission = null;
            string reservationId = "";
            bool accepted = false;
            bool ownsGate = false;
            object? connection = null;
            void Fail(string error) => Post(generation, () =>
            {
                if (_queueContext != null) _queueStatus = error;
                FinishOperation(error);
                reportFailure?.Invoke(error);
            });
            try
            {
                await ConnectionGate.WaitAsync(token).ConfigureAwait(false);
                ownsGate = true;
                token.ThrowIfCancellationRequested();
                ulong expectedEpoch = social?.AuthorityEpoch ?? queueEpoch;
                if (social != null) admission = social.TakeAdmission();
                if (party != null)
                {
                    if (_services.CurrentParty != party)
                    {
                        Fail("Your party changed during matchmaking. Refresh Social and retry.");
                        return;
                    }
                    if (entry == null)
                    {
                        ServerStatus status = await _services.ProbeAsync(host, port, token).ConfigureAwait(false);
                        entry = new(new MasterListing { Address = host, Port = port }, status);
                    }
                    if (!ServerBrowserService.CanQuickPlay(entry.Value, party.MemberCount, lobbyOnly: true))
                    {
                        Fail("This lobby cannot reserve enough open slots for your party.");
                        return;
                    }
                    Post(generation, () => Status($"RESERVING {party.MemberCount} PARTY SLOTS"));
                    Core.SocialLeaderAdmissionResult prepared = await _services.PrepareReservationAsync(entry.Value, token).ConfigureAwait(false);
                    admission = prepared.Admission;
                    reservationId = prepared.RequestId;
                    token.ThrowIfCancellationRequested();
                    if (!prepared.Success || admission == null)
                    {
                        Fail(prepared.Error);
                        return;
                    }
                    if (_services.CurrentParty != party)
                    {
                        Fail("Your party changed before connecting. Refresh Social and retry.");
                        return;
                    }
                    expectedEpoch = entry.Value.Status.AuthorityEpoch;
                }
                OnlineJoinResult joined = queued != null
                    ? await _services.JoinQueuedAsync(host, port, player, hunter, suit, queued, token).ConfigureAwait(false)
                    : await _services.JoinAsync(host, port, player, hunter, suit, spectate, admission, token).ConfigureAwait(false);
                if (joined.Joined) connection = _services.ConnectionIdentity;
                token.ThrowIfCancellationRequested();
                if (joined.Joined && expectedEpoch != 0 && _services.AuthorityEpoch != expectedEpoch)
                {
                    _services.StopOwnedConnection(connection);
                    connection = null;
                    Fail("The lobby authority changed. Refresh Social and retry.");
                    return;
                }
                if (!joined.Joined)
                {
                    if (!spectate && party == null && admission == null && queued == null)
                    {
                        ServerStatus status = await _services.ProbeAsync(host, port, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (status.Online && status.Protocol == NetConfig.ProtocolVersion && status.WaitlistSupported
                            && ((status.MaxPlayers > 0 && status.Players + Math.Max(0, status.ReservedSlots) >= status.MaxPlayers)
                                || status.WaitlistCount > 0))
                        {
                            if (expectedEpoch != 0 && status.AuthorityEpoch != expectedEpoch)
                                Fail("The lobby authority changed. Refresh Social and retry.");
                            else Post(generation, () => OfferQueue(new(host, port, player, hunter, suit,
                                expectedEpoch != 0 ? expectedEpoch : status.AuthorityEpoch, reportFailure), status));
                            return;
                        }
                    }
                    Fail(joined.Error); return;
                }
                // The worker transfers cleanup responsibility to the owner-thread completion.
                object? joinedConnection = connection;
                string ownedReservation = reservationId;
                accepted = true;
                Post(generation, () =>
                {
                    RetireQueue();
                    _busy = false;
                    _pendingActivity = PendingActivity.None;
                    _dirty = true;
                    if (party != null) _services.NotePartyTravel();
                    Connected?.Invoke(joined.Plan with
                    {
                        Lobby = new LobbyContext(social?.ServerName ?? $"SESSION // {host}", $"{host}:{port}")
                    });
                }, () =>
                {
                    _services.StopOwnedConnection(joinedConnection);
                    if (ownedReservation.Length != 0) _ = CancelOwnedReservationAsync(ownedReservation);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Fail("Connection service unavailable. Refresh and retry."); }
            finally
            {
                admission?.Dispose();
                queued?.Dispose();
                social?.Dispose();
                if (!accepted)
                {
                    _services.StopOwnedConnection(connection);
                    if (reservationId.Length != 0) await CancelOwnedReservationAsync(reservationId).ConfigureAwait(false);
                }
                if (ownsGate) ConnectionGate.Release();
            }
        }

        private async Task CancelOwnedReservationAsync(string requestId)
        {
            try { await _services.CancelReservationAsync(requestId).ConfigureAwait(false); }
            catch (Exception) { /* The server lease expires if cleanup is unavailable. */ }
        }

        private void OfferQueue(QueueContext context, ServerStatus status)
        {
            RetireQueue();
            _queueContext = context;
            _queueStatus = $"{status.Players}/{status.MaxPlayers} PLAYERS // {status.WaitlistCount} WAITING";
            _busy = false;
            _pendingActivity = PendingActivity.Queue;
            Status("THIS LOBBY IS FULL. JOIN THE WAITLIST FOR A PLAYER SLOT.");
        }

        public void QueueJoin()
        {
            if (!_visible || _busy || _queueContext is not { } context || _queue != null) return;
            (int generation, CancellationToken token) = Begin("CONNECTING TO WAITLIST", PendingActivity.Queue);
            _queueContext = context;
            _queueStatus = "CONNECTING TO WAITLIST";
            _ = OpenQueueAsync(generation, token, context);
        }

        private async Task OpenQueueAsync(int generation, CancellationToken token, QueueContext context)
        {
            IRmlLobbyQueue? queue = null;
            try
            {
                queue = await _services.OpenQueueAsync(context.Host, context.Port, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                IRmlLobbyQueue prepared = queue;
                queue = null;
                Post(generation, () =>
                {
                    _queue = prepared;
                    _queueStatus = prepared.Status;
                    _busy = false;
                    _dirty = true;
                }, prepared.Dispose);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                Post(generation, () =>
                {
                    _queueStatus = "THE WAITLIST COULD NOT CONNECT. RETRY OR LEAVE.";
                    _busy = false;
                    _dirty = true;
                });
            }
            finally { queue?.Dispose(); }
        }

        public void QueueAccept()
        {
            if (!_visible || _busy || _queueContext is not { } context || _queue is not { SeatAvailable: true } queue
                || queue.OfferId == 0 || _acceptedQueueOffer == queue.OfferId) return;
            if (context.AuthorityEpoch != 0 && queue.OfferEpoch != context.AuthorityEpoch)
            {
                QueueLeave("The lobby authority changed. Refresh the server list and retry.");
                return;
            }
            if (queue.AcceptSeat())
            {
                _acceptedQueueOffer = queue.OfferId;
                _queueStatus = "PREPARING YOUR PLAYER SLOT";
                _dirty = true;
            }
        }

        public void QueueDecline()
        {
            if (!_visible || _queue is not { SeatAvailable: true } queue) return;
            queue.DeclineSeat();
            QueueLeave("Player slot declined. Left waitlist.");
        }

        public void QueueLeave() => QueueLeave("Left waitlist.");

        private void QueueLeave(string reason)
        {
            if (_queueContext == null) return;
            Action<string>? report = _queueContext.ReportFailure;
            _generation++;
            _operation?.Cancel(); _operation?.Dispose(); _operation = null;
            RetireQueue();
            _busy = false;
            _pendingActivity = PendingActivity.None;
            Status(reason);
            report?.Invoke(reason);
        }

        private void PollQueue()
        {
            if (!_visible || _queue == null || _queueContext is not { } context) return;
            IRmlLobbyQueue queue = _queue;
            queue.Poll();
            if (queue.Admitted)
            {
                // The admitted client's existing transport and bootstrap transfer to NetLaunch.
                _queue = null;
                ulong epoch = queue.OfferEpoch != 0 ? queue.OfferEpoch : context.AuthorityEpoch;
                (int generation, CancellationToken token) = Begin("PREPARING YOUR PLAYER SLOT", PendingActivity.Connect);
                _queueContext = context;
                _queueStatus = "PREPARING YOUR PLAYER SLOT";
                _ = JoinAsync(generation, token, context.Host, context.Port, context.Player, context.Hunter,
                    context.Suit, false, null, null, reportFailure: context.ReportFailure, queued: queue, queueEpoch: epoch);
                return;
            }
            if (queue.Error != null || queue.Ended)
            {
                _queueStatus = queue.Error ?? queue.Status;
                _queue = null; queue.Dispose();
                _acceptedQueueOffer = 0;
                _busy = false;
                _dirty = true;
                return;
            }
            if (!queue.SeatAvailable || queue.OfferId != _acceptedQueueOffer) _acceptedQueueOffer = 0;
            _queueStatus = _acceptedQueueOffer != 0 ? "PREPARING YOUR PLAYER SLOT" : queue.Status;
        }

        private void RefreshQueueSnapshot()
        {
            bool visible = _queueContext != null;
            bool offer = _queue?.SeatAvailable == true;
            bool accepting = offer && _queue?.OfferId == _acceptedQueueOffer;
            string position = offer ? $"A PLAYER SLOT IS READY. ACCEPT WITHIN {_queue!.RemainingOfferSeconds} SECONDS."
                : _queue?.Position > 0 ? $"POSITION {_queue.Position} OF {_queue.QueueLength}"
                : visible ? "YOUR PLACE IS HELD BRIEFLY IF THE CONNECTION IS INTERRUPTED." : "";
            var next = new RmlMultiplayerQueueSnapshot(0, visible, _queueStatus, position,
                _queue?.RemainingOfferSeconds ?? 0, visible && !_busy && _queue == null,
                offer && !accepting && !_busy, offer && !_busy, _busy || accepting);
            if (next != _queueView with { Version = 0 })
            {
                _queueView = next with { Version = _queueView.Version + 1 };
                _dirty = true;
            }
        }

        private void RetireQueue()
        {
            _queue?.Dispose(); _queue = null;
            _queueContext = null;
            _queueStatus = "";
            _acceptedQueueOffer = 0;
        }

        public void OpenCreate()
        {
            if (!_visible) return;
            if (_queueContext != null) QueueLeave("Left waitlist to configure a new lobby.");
            _requiredMap = null;
            // Browsing is cancellable. Reaching the create form must never
            // wait for a dead directory before the map/host controls work.
            if (_pendingActivity == PendingActivity.Browse
                || _pendingActivity == PendingActivity.Quick)
            {
                _generation++;
                _operation?.Cancel();
                _operation?.Dispose();
                _operation = null;
                _busy = false;
                _pendingActivity = PendingActivity.None;
            }
            Status("CONFIGURE YOUR LOBBY");
        }

        public bool OpenCreateForMap(Core.CommunityHostRequest request)
        {
            if (_busy && _pendingActivity != PendingActivity.Browse && _pendingActivity != PendingActivity.Quick)
            {
                Status("WAIT FOR THE CURRENT CONNECTION BEFORE HOSTING A COMMUNITY MAP.");
                return false;
            }
            if (!request.RoomKey.Equals(request.Identity.RoomKey, StringComparison.OrdinalIgnoreCase)
                || !CustomRooms.Installed.HasExact(request.Identity))
            {
                Status("THE INSTALLED MAP REVISION CHANGED. REINSTALL THE SELECTED COMMUNITY REVISION.");
                return false;
            }
            if (!_rooms.Contains(request.RoomKey, StringComparer.OrdinalIgnoreCase))
                _rooms = _rooms.Append(request.RoomKey).ToArray();
            if (!_visible) Open(quickPlay: false);
            OpenCreate();
            _roomIndex = Array.FindIndex(_rooms, room => room.Equals(request.RoomKey, StringComparison.OrdinalIgnoreCase));
            _requiredMap = request;
            _setBool("play_create_mode", true); _setBool("play_browser_mode", false);
            RefreshStatic();
            return true;
        }

        public bool OpenCreateForRoom(string roomKey)
        {
            string room = roomKey.Trim();
            // Studio IPC supplies a name, so resolve it through the actual installed catalog.
            if (!CustomRooms.Definitions.Any(definition => definition.Name.Equals(room, StringComparison.OrdinalIgnoreCase)))
            {
                Status("THE STUDIO MAP IS NOT INSTALLED. SAVE OR INSTALL IT BEFORE HOSTING.");
                return false;
            }
            if (!_visible)
            {
                _visible = true;
                _serverKind = 0;
            }
            if (_busy && _pendingActivity != PendingActivity.Browse && _pendingActivity != PendingActivity.Quick)
            {
                Status("WAIT FOR THE CURRENT CONNECTION BEFORE HOSTING A STUDIO MAP.");
                return false;
            }
            if (!_rooms.Contains(room, StringComparer.OrdinalIgnoreCase)) _rooms = _rooms.Append(room).ToArray();
            OpenCreate();
            _roomIndex = Array.FindIndex(_rooms, key => key.Equals(room, StringComparison.OrdinalIgnoreCase));
            _requiredMap = CustomRooms.Installed.TryGet(room, out InstalledMapIdentity installed)
                ? new Core.CommunityHostRequest(room, installed.Identity) : null;
            _setBool("play_create_mode", true);
            _setBool("play_browser_mode", false);
            RefreshStatic();
            return true;
        }

        public void NextMap(int direction = 1)
        {
            if (_busy || _rooms.Length == 0) return;
            _requiredMap = null;
            _roomIndex = (_roomIndex + direction + _rooms.Length) % _rooms.Length;
            RefreshStatic();
        }

        public void NextMode()
        {
            if (_busy) return;
            _modeIndex = (_modeIndex + 1) % Modes.Length;
            RefreshStatic();
        }

        public void ToggleHost()
        {
            if (_busy) return;
            _serverKind = (_serverKind + 1) % 2;
            RefreshStatic();
        }

        public void Create(string desiredName, string desiredPlayer)
        {
            if (!_visible || _busy) return;
            if (!GameFiles.Ready || _rooms.Length == 0)
            {
                Status("GAME MAPS ARE NOT READY");
                return;
            }

            string player = desiredPlayer.Trim();
            if (player.Length == 0) player = LocalPlayer();
            if (player.Length > 24) player = player[..24];
            string name = desiredName.Trim();
            if (name.Length == 0) name = $"{player}'s lobby";
            if (name.Length > 48) name = name[..48];
            string map = _rooms[_roomIndex];
            if (_requiredMap is { } exact && !CustomRooms.Installed.HasExact(exact.Identity))
            {
                Status("THE SELECTED MAP REVISION CHANGED BEFORE HOSTING. REINSTALL IT FROM COMMUNITY.");
                return;
            }
            GameMode mode = Modes[_modeIndex];
            bool localHost = _serverKind == 1;
            if (localHost && !LocalServer.Ready)
            {
                Status("LOCAL SERVER BINARY NOT INSTALLED // USE HOSTED");
                return;
            }

            (int generation, CancellationToken token) = Begin(localHost
                ? "STARTING LOCAL SERVER" : "FINDING HOST FOR LOBBY",
                PendingActivity.Create);
            _ = CreateAsync(generation, token, name, player, map, mode, localHost, _requiredMap);
        }

        private async Task CreateAsync(int generation, CancellationToken token,
            string name, string player, string map, GameMode mode, bool localHost, Core.CommunityHostRequest? requiredMap)
        {
            bool ownsGate = false;
            object? connection = null;
            bool accepted = false;
            try
            {
                await ConnectionGate.WaitAsync(token).ConfigureAwait(false);
                ownsGate = true;
                token.ThrowIfCancellationRequested();
                if (requiredMap is { } pin && !CustomRooms.Installed.HasExact(pin.Identity))
                {
                    Post(generation, () => FinishOperation("THE SELECTED MAP REVISION CHANGED BEFORE HOSTING. REINSTALL IT FROM COMMUNITY."));
                    return;
                }
                int timeLimit = mode == GameMode.OneInTheChamber ? 0
                    : Math.Clamp(LauncherPrefs.LastLobbyTimeLimitSeconds, 0, UInt16.MaxValue);
                int goal = mode == LauncherPrefs.LastLobbyMode
                    ? LauncherPrefs.LastLobbyGoal
                    : MatchGoalRules.DefaultValue(mode);
                Hunter hunter = Hunters.Resolve(LauncherPrefs.LastHunter);
                int suit = LauncherPrefs.LastColor;
                var rotation = new List<(string RoomKey, GameMode Mode)> { (map, mode) };

                string server;
                int port;
                Guid owner;
                if (localHost)
                {
                    port = await Task.Run(() => LocalServer.Start(name, rotation,
                        maxPlayers: PlayerEntity.SlotCapacity,
                        timeLimit: timeLimit, pointGoal: (ushort)Math.Clamp(goal, 0, UInt16.MaxValue),
                        masterHost: LauncherPrefs.MasterHost,
                        masterPort: LauncherPrefs.MasterPort,
                        listed: LauncherPrefs.ListHostedGame, cancel: token,
                        lobby: true, requestedPort: NetConfig.DefaultPort), token);
                    if (port < 0)
                    {
                        Post(generation, () => FinishOperation(
                            LocalServer.LastError ?? "LOCAL SERVER COULD NOT START"));
                        return;
                    }
                    server = "127.0.0.1";
                    owner = LocalServer.OwnerToken;
                }
                else
                {
                    var candidates = new ConcurrentBag<HostCandidate>();
                    var found = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    NetMasterClient.FindHosts(LauncherPrefs.MasterHost,
                        LauncherPrefs.MasterPort,
                        onFound: candidate => candidates.Add(candidate),
                        onDone: () => found.TrySetResult(true));
                    await Task.WhenAny(found.Task, Task.Delay(4500, token));
                    token.ThrowIfCancellationRequested();

                    HostCandidate[] available = candidates.Where(h => h.WillHost)
                        .OrderBy(h => h.Latency < 0 ? Int32.MaxValue : h.Latency)
                        .ToArray();
                    if (available.Length == 0)
                    {
                        Post(generation, () => FinishOperation(
                            "NO HOSTED SERVER AVAILABLE // SELECT LOCAL SERVER"));
                        return;
                    }

                    HostedGame created = default;
                    foreach (HostCandidate candidate in available)
                    {
                        token.ThrowIfCancellationRequested();
                        created = await Task.Run(() => NetMasterClient.RequestGame(
                            candidate.Host, candidate.Port, map, mode, timeLimit,
                            goal, PlayerEntity.SlotCapacity, name,
                            rotation: rotation, policy: ServerSessionPolicy.Lobby), token);
                        if (created.Started) break;
                    }

                    if (!created.Started)
                    {
                        string reason = created.Reason.Length > 0 ? created.Reason
                            : "NO HOST COULD CREATE THE LOBBY";
                        Post(generation, () => FinishOperation(reason));
                        return;
                    }
                    server = created.Host;
                    port = created.Port;
                    owner = created.OwnerToken;
                }

                token.ThrowIfCancellationRequested();
                Post(generation, () => Status($"JOINING {server}:{port}"));
                bool connected = await Task.Run(() => NetLaunch.Connect(
                    server, port, player, hunter, color: suit,
                    ownerToken: owner, cancellationToken: token), token);
                if (connected) connection = NetSession.ConnectionIdentity;
                token.ThrowIfCancellationRequested();
                if (!connected)
                {
                    string reason = NetLaunch.LastJoinError;
                    NetSession.Stop();
                    Post(generation, () => FinishOperation(reason));
                    return;
                }

                if (requiredMap is { } exact && (NetSession.ServerSession is not { } session
                    || !session.Match.MapIdentity.Content(session.Match.RoomKey).Matches(exact.Identity)
                    || !CustomRooms.Installed.HasExact(exact.Identity)))
                {
                    Post(generation, () => FinishOperation("THE HOSTED MAP REVISION CHANGED. REINSTALL THE SELECTED COMMUNITY REVISION."));
                    return;
                }

                var plan = new LaunchPlan
                {
                    Kind = LaunchKind.Online,
                    Hunter = hunter, PlayerName = player, RoomKey = "",
                    Mode = mode, Port = port,
                    Lobby = new LobbyContext(name, localHost
                        ? $"Local server // port {port}"
                        : $"{server}:{port}", CreatedLocally: localHost)
                };
                object? joinedConnection = connection;
                accepted = true;
                Post(generation, () =>
                {
                    LauncherPrefs.PlayerName = player;
                    LauncherPrefs.LastLobbyMode = mode;
                    LauncherPrefs.LastLobbyTimeLimitSeconds = timeLimit;
                    LauncherPrefs.LastLobbyGoal = (ushort)Math.Clamp(goal, 0, UInt16.MaxValue);
                    LauncherPrefs.ServerAddress = server;
                    LauncherPrefs.ServerPort = port;
                    LauncherPrefs.LastKind = (int)LaunchKind.Online;
                    LauncherPrefs.Save();
                    _busy = false;
                    _pendingActivity = PendingActivity.None;
                    _dirty = true;
                    Connected?.Invoke(plan);
                }, () => _services.StopOwnedConnection(joinedConnection));
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Post(generation, () => FinishOperation("HOSTING FAILED. RETRY THE SELECTED MAP.")); }
            finally
            {
                if (!accepted) _services.StopOwnedConnection(connection);
                if (ownsGate) ConnectionGate.Release();
            }
        }

        private (int Generation, CancellationToken Token) Begin(
            string message, PendingActivity activity)
        {
            RetireQueue();
            _operation?.Cancel();
            _operation?.Dispose();
            _operation = new CancellationTokenSource();
            _generation++;
            _busy = true;
            _pendingActivity = activity;
            Status(message);
            return (_generation, _operation.Token);
        }

        private void FinishOperation(string message)
        {
            _busy = false;
            _pendingActivity = PendingActivity.None;
            Status(message);
        }

        private void Post(int generation, Action action, Action? discarded = null)
        {
            lock (_completionGate)
            {
                if (_disposed) { discarded?.Invoke(); return; }
                _completions.Enqueue(() =>
                {
                    if (!_disposed && _visible && generation == _generation) action();
                    else discarded?.Invoke();
                });
            }
        }

        private void Status(string message)
        {
            _status = String.IsNullOrWhiteSpace(message)
                ? "READY" : message.Trim().ToUpperInvariant();
            _dirty = true;
        }

        private string LocalPlayer()
        {
            string player = _readField("play_player_name").Trim();
            if (player.Length == 0) player = LauncherPrefs.PlayerName.Trim();
            return player.Length == 0 ? "Player" : player;
        }

        private void RefreshStatic() => _dirty = true;

        private void Publish()
        {
            _setText("play_status", _status);
            _setBool("play_busy", _busy);
            _setText("play_create_map",
                _rooms.Length > 0 ? _rooms[_roomIndex].ToUpperInvariant()
                    : "NO LOCAL MAPS");
            _setText("play_create_mode",
                Modes[_modeIndex].ToString().ToUpperInvariant());
            _setText("play_create_host",
                _serverKind == 0 ? "HOSTED // ONLINE" : "DEDICATED // THIS DEVICE");
            _setText("play_server_count", $"{_servers.Count} LIVE");
            // Do not display "no public sessions" beneath real live rows.
            // The screenshot showed this stale default alongside 2 LIVE.
            _setBool("play_no_servers", _servers.Count == 0);
            for (int i = 0; i < 8; i++)
            {
                bool exists = i < _servers.Count;
                ServerBrowserEntry entry = exists ? _servers[i] : default;
                _setBool($"play_server{i}_present", exists);
                _setText($"play_server{i}_name",
                    exists ? entry.Name.ToUpperInvariant() : "");
                _setText($"play_server{i}_details",
                    exists ? $"{entry.Status.RoomKey} // {entry.Status.Players}/"
                        + $"{entry.Status.MaxPlayers} // "
                        + (entry.Status.Latency >= 0 ? $"{entry.Status.Latency} MS" : "PING --")
                        : "");
            }
        }

        public void Dispose()
        {
            Cancel();
            lock (_completionGate)
            {
                _disposed = true;
                Connected = null;
                while (_completions.TryDequeue(out Action? complete)) complete();
            }
        }
    }
}
#endif
