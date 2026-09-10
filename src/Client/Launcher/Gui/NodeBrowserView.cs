using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Node connection and lobby remain alive when this view closes for gameplay.</summary>
internal sealed class NodeBrowserView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly string[] _maps;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _name = new() { Text = LauncherPrefs.PlayerName, Watermark = "Lobby name" };
    private readonly AccountSession? _account = ResolveAccountSession();
    private readonly bool _createLobby;
    private NodeControlClient? _observed;
    private bool _busy;
    private Guid _joiningMatch;
    private ulong _joiningNonce;
    private string? _joinError;
    private bool _createRequested;
    private string[]? _hostedMapKeys;
    public event EventHandler<LaunchPlan>? Launch;
    public event EventHandler? Closed;

    private static AccountSession? ResolveAccountSession()
    {
        if (AccountSessions.Current is { } current)
            return current;

        string address = LauncherPrefs.BackendAddress?.Trim() ?? "";
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? backend)
            || !AccountSession.IsAllowedBackend(backend))
            return null;

        // Configure a signed-out session from the saved Backend origin. Guest
        // admission is anonymous, but Node discovery still has to use the
        // Backend directory.
        return AccountSessions.Configure(backend);
    }
    public NodeBrowserView(System.Collections.Generic.IReadOnlyList<string> maps, bool createLobby = false)
    {
        _maps = maps.ToArray();
        _createLobby = createLobby;
        var root = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Server Nodes", FontSize = 24 });
        root.Children.Add(_status);
        root.Children.Add(Button("Refresh Nodes", Refresh));
        root.Children.Add(Button("Back", () => { Closed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }));
        root.Children.Add(_rows);
        Content = new ScrollViewer { Content = root };
        DetachedFromVisualTree += (_, _) => { _lifetime.Cancel(); Observe(null); };
        if (_account != null && NodeSessions.Current is { Connected: true } current) { Observe(current); RenderSession(); }
        else _ = Run(Refresh);
    }
    private Avalonia.Controls.Button Button(string label, Func<Task> action)
    {
        var button = new Avalonia.Controls.Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => await Run(action);
        return button;
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception e) { _status.Text = e.Message; }
        finally { _busy = false; }
    }
    private async Task Refresh()
    {
        if (_account == null)
        {
            _status.Text = "Set the Backend URL in Hunter License, then choose Guest access.";
            return;
        }
        if (NodeSessions.Current is { Connected: true } current) { Observe(current); await current.SendAsync("lobby.list", new LobbyList()); return; }
        _hostedMapKeys = null;
        _status.Text = _account.IsSignedIn
            ? "Checking compatible Nodes…"
            : "Guest access: checking compatible Nodes…";
        // Map binaries are derived from the install's extracted game files.
        // Prepare them before hashing the content sent to the Backend; doing
        // this only at match launch lets discovery advertise a hash that the
        // Worker cannot actually use and makes a compatible Node disappear.
        var prepared = await Task.Run(() =>
        {
            IReadOnlyList<string> failures = MapPreparation.GenerateMissing();
            return (failures, identity: ContentEnvironment.GetContentIdentity());
        });
        if (prepared.failures.Count != 0)
        {
            _rows.Children.Clear();
            _status.Text = "Custom map preparation failed: " + prepared.failures[0];
            return;
        }
        var identity = prepared.identity;
        var nodes = await _account.GetNodesAsync(NetHeader.Version, BuildVersion.Display, identity.ContentHash, _lifetime.Token);
        _rows.Children.Clear();
        foreach (var node in nodes)
            _rows.Children.Add(Button($"{node.Name} · {node.Region} · {node.OnlineUsers}/{node.Capacity} · {node.TrustClass}", async () =>
            {
                var session = await NodeSessions.ConnectAsync(_account, node, _lifetime.Token);
                Observe(session); await session.SendAsync("lobby.list", new LobbyList()); RenderSession();
            }));
        _status.Text = nodes.Length == 0
            ? (_account.IsSignedIn ? "No compatible Nodes are online." : "Guest access: no compatible Nodes are online.")
            : (_account.IsSignedIn ? "Choose a Node." : "Guest access: choose a Node.");
    }
    private void Observe(NodeControlClient? session)
    {
        if (_observed != null) _observed.Changed -= OnChanged;
        _observed = session;
        _hostedMapKeys = session?.AdvertisedMapKeys;
        if (session != null) session.Changed += OnChanged;
    }
    private void OnChanged() => Dispatcher.UIThread.Post(RenderSession);
    private async Task JoinWorker(NodeControlClient node, NodeMatchHandoff handoff)
    {
        try
        {
            _joinError = null;
            _status.Text = "Joining Worker match…";
            if (!await NetLaunch.JoinWorkerAsync(handoff, node.Session!.DisplayName, _lifetime.Token))
                throw new InvalidOperationException(NetLaunch.LastJoinError);
            node.MarkGameplayJoined(handoff.MatchId);
            Launch?.Invoke(this, new LaunchPlan { Kind = LaunchKind.Online, Hunter = handoff.Hunter,
                PlayerName = node.Session.DisplayName, RoomKey = "", Mode = GameMode.Battle, Port = handoff.Port });
        }
        catch (Exception e) { _joinError = e.Message; RenderSession(); }
    }
    private void RenderSession()
    {
        var node = _observed;
        if (node == null) return;
        var state = node.State;
        if (state.Handoff is { } handoff && !state.MatchEnded && (handoff.MatchId != _joiningMatch || handoff.Nonce != _joiningNonce) && state.JoinedMatchId != handoff.MatchId)
        {
            _joiningMatch = handoff.MatchId; _joiningNonce = handoff.Nonce;
            _ = JoinWorker(node, handoff);
        }
        _rows.Children.Clear();
        bool guest = state.Session?.GuestSessionId is not null;
        _status.Text = _joinError ?? state.Error ?? (node.Connected
            ? (guest ? "Connected to Node with Guest access (not an authenticated account)." : "Connected to Node")
            : "Node disconnected. Reconnect to continue.");
        _rows.Children.Add(Button("Disconnect Node", async () => { Observe(null); _hostedMapKeys = null; await NodeSessions.DisconnectAsync(); await Refresh(); }));
        if (!node.Connected)
        {
            _rows.Children.Add(Button("Resume Node session", async () => { Observe(await NodeSessions.ResumeAsync(_lifetime.Token)); RenderSession(); }));
            return;
        }
        if (_joinError != null && state.Handoff is { } retry && !state.MatchEnded)
            _rows.Children.Add(Button("Retry Worker connection", () => node.SendAsync("match.rejoin", new NodeMatchRejoin(retry.MatchId))));
        if (state.Session is { } session)
            _rows.Children.Add(new TextBlock
            {
                Text = session.GuestSessionId is not null
                    ? $"Guest session · {session.DisplayName}"
                    : $"Account session · {session.DisplayName}"
            });
        if (state.Lobby is { } lobby)
        {
            _rows.Children.Add(new TextBlock { Text = $"{lobby.Name} · {lobby.Phase}" });
            LobbyWaitlistSnapshot? waitlist = lobby.Waitlist;
            int players = lobby.Members.Count(member => !member.Observer);
            int observers = lobby.Members.Count(member => member.Observer);
            int humanPlayerLimit = Math.Max(0, lobby.PlayerLimit - lobby.BotCount);
            _rows.Children.Add(new TextBlock { Text = $"Players {players}/{humanPlayerLimit} · Spectators {observers}/{lobby.ObserverLimit} · Waitlist {waitlist?.Count ?? 0}" });
            foreach (var member in lobby.Members)
            {
                string identity = member.GuestSessionId is not null
                    ? $"Guest · {member.DisplayName}"
                    : $"Account · {member.DisplayName}";
                _rows.Children.Add(new TextBlock { Text = $"{identity} · {member.Hunter} · {(member.Ready ? "Ready" : "Not ready")}" });
            }
            if (waitlist is { Entries.Length: > 0 })
            {
                _rows.Children.Add(new TextBlock { Text = "Ordered waitlist" });
                foreach (var entry in waitlist.Entries)
                    _rows.Children.Add(new TextBlock { Text = $"#{entry.Position} {entry.DisplayName} · {entry.State}" });
            }
            var self = lobby.Members.FirstOrDefault(x => x.SessionId == state.Session?.SessionId);
            if (self is not null)
                _rows.Children.Add(Button(self.Ready ? "Not ready" : "Ready", () => node.SendAsync("lobby.ready.set", new LobbySetReady(!self.Ready, lobby.Revision))));
            if (waitlist?.IsSelfQueued == true)
            {
                if (waitlist.SelfOffer is { } offer)
                {
                    _rows.Children.Add(new TextBlock { Text = $"PLAYER SLOT AVAILABLE · expires {offer.ExpiresAt.LocalDateTime:t}" });
                    _rows.Children.Add(Button("Accept player slot", () => node.SendAsync("lobby.queue.accept",
                        new LobbyQueueAccept(lobby.LobbyId, lobby.Revision, offer.OfferId))));
                    _rows.Children.Add(Button("Decline slot", () => node.SendAsync("lobby.queue.decline",
                        new LobbyQueueDecline(lobby.LobbyId, lobby.Revision, offer.OfferId))));
                }
                _rows.Children.Add(Button("Cancel waitlist", () => node.SendAsync("lobby.queue.leave",
                    new LobbyQueueLeave(lobby.LobbyId, lobby.Revision))));
            }
            else if (self is { Observer: true })
            {
                _rows.Children.Add(Button("Join player waitlist", () => node.SendAsync("lobby.queue.join",
                    new LobbyQueueJoin(lobby.LobbyId, lobby.Revision))));
            }
            if (lobby.OwnerSessionId == state.Session?.SessionId)
            {
                if (lobby.Phase == LobbyPhase.Open && HostedMaps() is { Count: > 0 } maps)
                {
                    var map = new ComboBox { ItemsSource = maps, SelectedItem = maps.Contains(lobby.MapKey, StringComparer.Ordinal) ? lobby.MapKey : maps[0] };
                    var mode = new ComboBox { ItemsSource = Enum.GetValues<MatchMode>(), SelectedItem = lobby.Mode };
                    _rows.Children.Add(new TextBlock { Text = "Map and mode" });
                    _rows.Children.Add(map); _rows.Children.Add(mode);
                    _rows.Children.Add(Button("Apply match settings", () => node.SendAsync("lobby.configure", new LobbyConfigure(lobby.Revision,
                        map.SelectedItem as string ?? "", mode.SelectedItem is MatchMode selected ? selected : MatchMode.Battle))));
                    _rows.Children.Add(Button("Start match", () => node.SendAsync("lobby.start", new LobbyStart(lobby.Revision))));
                }
                else if (lobby.Phase == LobbyPhase.Open)
                    _rows.Children.Add(new TextBlock { Text = MapCatalogMessage() });
                if (lobby.Phase == LobbyPhase.PostMatch)
                {
                    _rows.Children.Add(Button("Rematch", () => node.SendAsync("lobby.rematch", new LobbyRematch(lobby.Revision))));
                    _rows.Children.Add(Button("Return to open lobby", () => node.SendAsync("lobby.return", new LobbyReturn(lobby.Revision))));
                }
            }
            _rows.Children.Add(Button("Leave lobby", () => node.SendAsync("lobby.leave", new LobbyLeave(lobby.Revision))));
        }
        else
        {
            _rows.Children.Add(_name);
            if (_createLobby && !_createRequested && state.Lobbies != null)
            {
                _createRequested = true;
                _ = node.SendAsync("lobby.create", new LobbyCreate(LobbyName(), LobbyVisibility.Public));
            }
            _rows.Children.Add(Button("Create public lobby", () =>
                node.SendAsync("lobby.create", new LobbyCreate(LobbyName(), LobbyVisibility.Public))));
            foreach (var lobbyEntry in state.Lobbies?.Lobbies ?? [])
            {
                int humanPlayerLimit = Math.Max(0, lobbyEntry.PlayerLimit - lobbyEntry.BotCount);
                _rows.Children.Add(Button($"{lobbyEntry.Name} · Players {lobbyEntry.Players}/{humanPlayerLimit} · Waitlist {lobbyEntry.WaitlistCount}",
                    () => node.SendAsync("lobby.join", new LobbyJoin(lobbyEntry.LobbyId, lobbyEntry.Revision))));
                if (lobbyEntry.Observers < lobbyEntry.ObserverLimit)
                    _rows.Children.Add(Button($"Spectate {lobbyEntry.Name} · {lobbyEntry.Observers}/{lobbyEntry.ObserverLimit}",
                        () => node.SendAsync("lobby.join", new LobbyJoin(lobbyEntry.LobbyId, lobbyEntry.Revision, true))));
                if (lobbyEntry.Players >= humanPlayerLimit)
                    _rows.Children.Add(Button($"Waitlist for {lobbyEntry.Name}", () => node.SendAsync("lobby.queue.join",
                        new LobbyQueueJoin(lobbyEntry.LobbyId, lobbyEntry.Revision))));
            }
        }
    }

    private string LobbyName()
    {
        string name = (_name.Text ?? "").Trim();
        return name.Length > 0 ? name : "Hunters";
    }

    private IReadOnlyList<string> HostedMaps()
    {
        if (_hostedMapKeys is null || _hostedMapKeys.Length == 0) return Array.Empty<string>();
        var hosted = _hostedMapKeys.ToHashSet(StringComparer.Ordinal);
        return _maps.Where(hosted.Contains).Distinct(StringComparer.Ordinal)
            .OrderBy(map => map, StringComparer.Ordinal).ToArray();
    }

    private string MapCatalogMessage()
    {
        if (_hostedMapKeys is null)
            return "This Node did not advertise its hosted map catalog; map selection is unavailable.";
        if (_hostedMapKeys.Length == 0)
            return "This Node advertises no hosted maps.";
        return "This Node's hosted maps are not installed locally.";
    }
}
