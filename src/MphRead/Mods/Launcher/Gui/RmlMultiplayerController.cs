#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
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
        private readonly List<ServerBrowserEntry> _servers = new();
        private readonly string[] _rooms;
        private CancellationTokenSource? _operation;
        private enum PendingActivity { None, Browse, Quick, Connect, Create }
        private PendingActivity _pendingActivity;
        private int _generation;
        private int _roomIndex;
        private int _modeIndex;
        private int _serverKind;
        private bool _visible;
        private bool _busy;
        private bool _dirty;
        private string _status = "CHOOSE A MULTIPLAYER ACTIVITY";

        public event Action<LaunchPlan>? Connected;
        public bool Visible => _visible;

        public RmlMultiplayerController(IReadOnlyList<string> rooms)
        {
            _rooms = rooms.Where(room => !String.IsNullOrWhiteSpace(room))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _modeIndex = Math.Max(0, Array.IndexOf(Modes, LauncherPrefs.LastLobbyMode));
        }

        public void Open(bool quickPlay)
        {
            _visible = true;
            _roomIndex = 0;
            _serverKind = 0; // Hosted is the default; dedicated is explicit.
            RmlUiPrototype.SetFieldValue("play_player_name",
                String.IsNullOrWhiteSpace(LauncherPrefs.PlayerName) ? "Player" : LauncherPrefs.PlayerName);
            RmlUiPrototype.SetFieldValue("play_join_address",
                $"{LauncherPrefs.ServerAddress}:{LauncherPrefs.ServerPort}");
            RmlUiPrototype.SetFieldValue("play_create_name",
                $"{(String.IsNullOrWhiteSpace(LauncherPrefs.PlayerName) ? "Player" : LauncherPrefs.PlayerName)}'s lobby");
            RefreshStatic();
            if (quickPlay) QuickPlay();
            else Browse();
        }

        public void Cancel()
        {
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
                ServerDiscoveryResult result = await ServerBrowserService.DiscoverAsync(entry =>
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
            catch (Exception ex) { Post(generation, () => FinishOperation(ex.Message)); }
        }

        public void QuickPlay()
        {
            if (!_visible) return;
            (int generation, CancellationToken token) = Begin("QUICK PLAY // SEARCHING", PendingActivity.Quick);
            _ = QuickPlayAsync(generation, token);
        }

        private async Task QuickPlayAsync(int generation, CancellationToken token)
        {
            try
            {
                QuickPlaySearchResult result = await ServerBrowserService.FindBestAsync(token);
                if (!result.Found)
                {
                    Post(generation, () => FinishOperation(result.Message));
                    return;
                }
                Post(generation, () =>
                {
                    FinishOperation($"MATCH FOUND // {result.Entry.Name}");
                    JoinEndpoint(result.Entry.Endpoint, false);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Post(generation, () => FinishOperation(ex.Message)); }
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
            JoinEndpoint(entry.Endpoint, spectate);
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

            string player = LocalPlayer();
            (int generation, CancellationToken token) = Begin(
                $"CONNECTING TO {host}:{port}", PendingActivity.Connect);
            _ = JoinAsync(generation, token, host, port, player, spectate);
        }

        private async Task JoinAsync(int generation, CancellationToken token, string host,
            int port, string player, bool spectate)
        {
            try
            {
                OnlineJoinResult joined = await ServerBrowserService.JoinAsync(
                    host, port, player, LauncherPrefs.LastHunter,
                    LauncherPrefs.LastColor, token, spectate: spectate);
                Post(generation, () =>
                {
                    if (!joined.Joined)
                    {
                        FinishOperation(joined.Error);
                        return;
                    }
                    _busy = false;
                    _pendingActivity = PendingActivity.None;
                    _dirty = true;
                    Connected?.Invoke(joined.Plan with
                    {
                        Lobby = new LobbyContext($"SESSION // {host}", $"{host}:{port}")
                    });
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Post(generation, () => FinishOperation(ex.Message)); }
        }

        public void OpenCreate()
        {
            if (!_visible) return;
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

        public void NextMap(int direction = 1)
        {
            if (_busy || _rooms.Length == 0) return;
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

        public void Create(string desiredName)
        {
            if (!_visible || _busy) return;
            if (!GameFiles.Ready || _rooms.Length == 0)
            {
                Status("GAME MAPS ARE NOT READY");
                return;
            }

            string player = LocalPlayer();
            string name = desiredName.Trim();
            if (name.Length == 0) name = $"{player}'s lobby";
            if (name.Length > 48) name = name[..48];
            string map = _rooms[_roomIndex];
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
            _ = CreateAsync(generation, token, name, player, map, mode, localHost);
        }

        private async Task CreateAsync(int generation, CancellationToken token,
            string name, string player, string map, GameMode mode, bool localHost)
        {
            try
            {
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
                if (!connected)
                {
                    string reason = NetLaunch.LastJoinError;
                    NetSession.Stop();
                    Post(generation, () => FinishOperation(reason));
                    return;
                }

                LauncherPrefs.PlayerName = player;
                LauncherPrefs.LastLobbyMode = mode;
                LauncherPrefs.LastLobbyTimeLimitSeconds = timeLimit;
                LauncherPrefs.LastLobbyGoal = (ushort)Math.Clamp(goal, 0, UInt16.MaxValue);
                LauncherPrefs.ServerAddress = server;
                LauncherPrefs.ServerPort = port;
                LauncherPrefs.LastKind = (int)LaunchKind.Online;
                LauncherPrefs.Save();

                var plan = new LaunchPlan
                {
                    Kind = LaunchKind.Online,
                    Hunter = hunter, PlayerName = player, RoomKey = "",
                    Mode = mode, Port = port,
                    Lobby = new LobbyContext(name, localHost
                        ? $"Local server // port {port}"
                        : $"{server}:{port}", CreatedLocally: localHost)
                };
                Post(generation, () =>
                {
                    _busy = false;
                    _pendingActivity = PendingActivity.None;
                    _dirty = true;
                    Connected?.Invoke(plan);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Post(generation, () => FinishOperation(ex.Message)); }
        }

        private (int Generation, CancellationToken Token) Begin(
            string message, PendingActivity activity)
        {
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

        private void Post(int generation, Action action)
        {
            _completions.Enqueue(() =>
            {
                if (_visible && generation == _generation)
                    action();
            });
        }

        private void Status(string message)
        {
            _status = String.IsNullOrWhiteSpace(message)
                ? "READY" : message.Trim().ToUpperInvariant();
            _dirty = true;
        }

        private static string LocalPlayer()
        {
            string player = RmlUiPrototype.ReadFieldValue("play_player_name").Trim();
            if (player.Length == 0) player = LauncherPrefs.PlayerName.Trim();
            return player.Length == 0 ? "Player" : player;
        }

        private void RefreshStatic() => _dirty = true;

        private void Publish()
        {
            RmlUiPrototype.SetMenuText("play_status", _status);
            RmlUiPrototype.SetMenuBool("play_busy", _busy);
            RmlUiPrototype.SetMenuText("play_create_map",
                _rooms.Length > 0 ? _rooms[_roomIndex].ToUpperInvariant()
                    : "NO LOCAL MAPS");
            RmlUiPrototype.SetMenuText("play_create_mode",
                Modes[_modeIndex].ToString().ToUpperInvariant());
            RmlUiPrototype.SetMenuText("play_create_host",
                _serverKind == 0 ? "HOSTED // ONLINE" : "DEDICATED // THIS DEVICE");
            RmlUiPrototype.SetMenuText("play_server_count", $"{_servers.Count} LIVE");
            for (int i = 0; i < 8; i++)
            {
                bool exists = i < _servers.Count;
                ServerBrowserEntry entry = exists ? _servers[i] : default;
                RmlUiPrototype.SetMenuBool($"play_server{i}_present", exists);
                RmlUiPrototype.SetMenuText($"play_server{i}_name",
                    exists ? entry.Name.ToUpperInvariant() : "");
                RmlUiPrototype.SetMenuText($"play_server{i}_details",
                    exists ? $"{entry.Status.RoomKey} // {entry.Status.Players}/"
                        + $"{entry.Status.MaxPlayers} // "
                        + (entry.Status.Latency >= 0 ? $"{entry.Status.Latency} MS" : "PING --")
                        : "");
            }
        }

        public void Dispose()
        {
            Cancel();
            Connected = null;
            while (_completions.TryDequeue(out _)) { }
        }
    }
}
#endif
