using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using FruityPrime.Server.Shared;
using MphRead.Mods.Accounts;
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
    private readonly AccountSession? _account = AccountSessions.Current;
    private readonly bool _createLobby;
    private NodeControlClient? _observed;
    private bool _busy;
    private Guid _joiningMatch;
    private ulong _joiningNonce;
    private string? _joinError;
    private bool _createRequested;
    public event EventHandler<LaunchPlan>? Launch;
    public event EventHandler? Closed;
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
        if (NodeSessions.Current is { Connected: true } current) { Observe(current); RenderSession(); }
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
        if (_account?.IsSignedIn != true) { _status.Text = "Sign in through Hunter License to browse and join Nodes."; return; }
        if (NodeSessions.Current is { Connected: true } current) { Observe(current); await current.SendAsync("lobby.list", new LobbyList()); return; }
        _status.Text = "Checking compatible Nodes…";
        var identity = await Task.Run(ContentEnvironment.GetContentIdentity);
        var nodes = await _account.GetNodesAsync(NetHeader.Version, BuildVersion.Display, identity.ContentHash, _lifetime.Token);
        _rows.Children.Clear();
        foreach (var node in nodes)
            _rows.Children.Add(Button($"{node.Name} · {node.Region} · {node.OnlineUsers}/{node.Capacity} · {node.TrustClass}", async () =>
            {
                var session = await NodeSessions.ConnectAsync(_account, node.NodeId, _lifetime.Token);
                Observe(session); await session.SendAsync("lobby.list", new LobbyList()); RenderSession();
            }));
        _status.Text = nodes.Length == 0 ? "No compatible Nodes are online." : "Choose a Node.";
    }
    private void Observe(NodeControlClient? session)
    {
        if (_observed != null) _observed.Changed -= OnChanged;
        _observed = session;
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
        _status.Text = _joinError ?? state.Error ?? (node.Connected ? "Connected to Node" : "Node disconnected. Reconnect to continue.");
        _rows.Children.Add(Button("Disconnect Node", async () => { Observe(null); await NodeSessions.DisconnectAsync(); await Refresh(); }));
        if (!node.Connected)
        {
            _rows.Children.Add(Button("Resume Node session", async () => { Observe(await NodeSessions.ResumeAsync(_lifetime.Token)); RenderSession(); }));
            return;
        }
        if (_joinError != null && state.Handoff is { } retry && !state.MatchEnded)
            _rows.Children.Add(Button("Retry Worker connection", () => node.SendAsync("match.rejoin", new NodeMatchRejoin(retry.MatchId))));
        if (state.Lobby is { } lobby)
        {
            _rows.Children.Add(new TextBlock { Text = $"{lobby.Name} · {lobby.Phase}" });
            foreach (var member in lobby.Members)
                _rows.Children.Add(new TextBlock { Text = $"{member.DisplayName} · {member.Hunter} · {(member.Ready ? "Ready" : "Not ready")}" });
            var self = lobby.Members.FirstOrDefault(x => x.SessionId == state.Session?.SessionId);
            _rows.Children.Add(Button(self?.Ready == true ? "Not ready" : "Ready", () => node.SendAsync("lobby.ready.set", new LobbySetReady(self?.Ready != true, lobby.Revision))));
            if (lobby.OwnerSessionId == state.Session?.SessionId)
            {
                if (lobby.Phase == LobbyPhase.Open)
                {
                    var map = new ComboBox { ItemsSource = _maps, SelectedItem = _maps.Contains(lobby.MapKey) ? lobby.MapKey : _maps.FirstOrDefault() };
                    var mode = new ComboBox { ItemsSource = Enum.GetValues<MatchMode>(), SelectedItem = lobby.Mode };
                    _rows.Children.Add(new TextBlock { Text = "Map and mode" });
                    _rows.Children.Add(map); _rows.Children.Add(mode);
                    _rows.Children.Add(Button("Apply match settings", () => node.SendAsync("lobby.configure", new LobbyConfigure(lobby.Revision,
                        map.SelectedItem as string ?? "", mode.SelectedItem is MatchMode selected ? selected : MatchMode.Battle))));
                    _rows.Children.Add(Button("Start match", () => node.SendAsync("lobby.start", new LobbyStart(lobby.Revision))));
                }
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
                _rows.Children.Add(Button($"{lobbyEntry.Name} · {lobbyEntry.Players}/{lobbyEntry.PlayerLimit}", () => node.SendAsync("lobby.join", new LobbyJoin(lobbyEntry.LobbyId, lobbyEntry.Revision))));
        }
    }

    private string LobbyName()
    {
        string name = (_name.Text ?? "").Trim();
        return name.Length > 0 ? name : "Hunters";
    }
}
