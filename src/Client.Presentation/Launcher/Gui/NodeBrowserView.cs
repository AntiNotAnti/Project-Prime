using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Accounts;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Compatibility presentation for the classic launcher. Discovery, Node
/// admission, lobby commands, reconnect, map preparation, and Worker handoff
/// all belong to <see cref="PlayController"/> and its
/// <see cref="MphRead.Mods.Network.ClientOnlineRuntime"/>. This view only
/// renders the immutable controller snapshot and forwards user actions.
/// </summary>
internal sealed class NodeBrowserView : UserControl
{
    private readonly PlayController _play;
    private readonly bool _createLobby;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _name = new() { Text = LauncherPrefs.PlayerName, Watermark = "Lobby name" };
    private bool _busy;
    private bool _createRequested;
    private int _disposed;

    public event EventHandler<LaunchPlan>? Launch;
    public event EventHandler? Closed;

    public NodeBrowserView(PlayController play, bool createLobby = false)
    {
        _play = play ?? throw new ArgumentNullException(nameof(play));
        _createLobby = createLobby;
        var root = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Game Servers", FontSize = 24 });
        root.Children.Add(_status);
        root.Children.Add(Button("Refresh Servers", Refresh));
        root.Children.Add(Button("Back", () =>
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }));
        root.Children.Add(_rows);
        Content = new ScrollViewer { Content = root };

        _play.Changed += ControllerChanged;
        _play.Launch += ControllerLaunch;
        _play.SetHandoffEnabled(true);
        DetachedFromVisualTree += Detached;
        Render();
        if (_play.State.Node?.Session == null)
            _ = Run(Refresh);
    }

    private Avalonia.Controls.Button Button(string label, Func<Task> action)
    {
        var button = new Avalonia.Controls.Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        button.Click += async (_, _) => await Run(action);
        return button;
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy || Volatile.Read(ref _disposed) != 0) return;
        _busy = true;
        try { await action().ConfigureAwait(true); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                "Could not complete the server request. Try again.");
        }
        finally { _busy = false; }
    }

    private async Task Refresh()
    {
        if (_play.State.Node?.Session != null)
        {
            await _play.RefreshLobbiesAsync(_lifetime.Token).ConfigureAwait(true);
            return;
        }
        _createRequested = false;
        await _play.RefreshNodesAsync(_lifetime.Token).ConfigureAwait(true);
    }

    private void ControllerChanged(object? sender, EventArgs args)
        => Dispatcher.UIThread.Post(Render);

    private void ControllerLaunch(object? sender, LaunchPlan plan)
        => Launch?.Invoke(this, plan);

    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _play.SetHandoffEnabled(false);
        _play.Changed -= ControllerChanged;
        _play.Launch -= ControllerLaunch;
    }

    private void Render()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        PlayState state = _play.State;
        _rows.Children.Clear();
        _status.Text = state.Message;

        if (state.Node?.Session is not { } session)
        {
            RenderDirectory(state);
            return;
        }

        _status.Text = state.Node.Error is { Length: > 0 } error
            ? PrimeRoutePresentation.PlayerFacingNetworkError(error,
                "Connection issue. Reconnect and try again.")
            : state.Message;
        _rows.Children.Add(new TextBlock
        {
            Text = session.GuestSessionId is not null
                ? $"Guest session · {session.DisplayName}"
                : $"Account session · {session.DisplayName}"
        });
        _rows.Children.Add(Button("Disconnect Server", async () =>
        {
            _createRequested = false;
            await _play.DisconnectAsync().ConfigureAwait(true);
        }));

        if (state.Lobby is { } lobby)
            RenderLobby(state, lobby, session);
        else
            RenderLobbies(state);
    }

    private void RenderDirectory(PlayState state)
    {
        _rows.Children.Add(new TextBlock
        {
            Text = state.Nodes.Count == 0
                ? "No compatible servers are online."
                : "Choose a server."
        });
        foreach (NodeListing node in state.Nodes)
        {
            NodeListing selected = node;
            _rows.Children.Add(Button(
                $"{node.Name} · {node.Region} · {node.OnlineUsers}/{node.Capacity} · {node.TrustClass}",
                async () =>
                {
                    if (!await _play.ConnectNodeAsync(selected, _lifetime.Token).ConfigureAwait(true))
                        return;
                    await _play.RefreshLobbiesAsync(_lifetime.Token).ConfigureAwait(true);
                }));
        }
    }

    private void RenderLobbies(PlayState state)
    {
        if (_createLobby && !_createRequested && state.Lobbies != null)
        {
            _createRequested = true;
            _ = Run(() => _play.CreateLobbyAsync(LobbyName(), _lifetime.Token));
        }

        _rows.Children.Add(_name);
        _rows.Children.Add(Button("Create public lobby",
            () => _play.CreateLobbyAsync(LobbyName(), _lifetime.Token)));
        foreach (LobbyListEntry entry in state.Lobbies?.Lobbies ?? [])
        {
            int humanPlayerLimit = Math.Max(0, entry.PlayerLimit - entry.BotCount);
            LobbyListEntry selected = entry;
            _rows.Children.Add(Button(
                $"{entry.Name} · Players {entry.Players}/{humanPlayerLimit} · Waitlist {entry.WaitlistCount}",
                () => _play.JoinLobbyAsync(selected.LobbyId, selected.Revision,
                    cancellationToken: _lifetime.Token)));
            if (entry.Observers < entry.ObserverLimit)
                _rows.Children.Add(Button(
                    $"Spectate {entry.Name} · {entry.Observers}/{entry.ObserverLimit}",
                    () => _play.JoinObserverAsync(selected.LobbyId, selected.Revision,
                        _lifetime.Token)));
            if (entry.Players >= humanPlayerLimit)
                _rows.Children.Add(Button($"Waitlist for {entry.Name}",
                    () => _play.JoinWaitlistAsync(selected.LobbyId, selected.Revision,
                        cancellationToken: _lifetime.Token)));
        }
    }

    private void RenderLobby(PlayState state, LobbySnapshot lobby, NodeSessionSnapshot session)
    {
        _rows.Children.Add(new TextBlock { Text = $"{lobby.Name} · {lobby.Phase}" });
        int players = lobby.Members.Count(member => !member.Observer);
        int observers = lobby.Members.Count(member => member.Observer);
        int humanPlayerLimit = Math.Max(0, lobby.PlayerLimit - lobby.BotCount);
        _rows.Children.Add(new TextBlock
        {
            Text = $"Players {players}/{humanPlayerLimit} · Spectators {observers}/{lobby.ObserverLimit} · Waitlist {lobby.Waitlist?.Count ?? 0}"
        });
        foreach (LobbyMember member in lobby.Members)
        {
            string identity = member.GuestSessionId is not null
                ? $"Guest · {member.DisplayName}"
                : $"Account · {member.DisplayName}";
            _rows.Children.Add(new TextBlock
            {
                Text = $"{identity} · {member.Hunter} · {(member.Ready ? "Ready" : "Not ready")}"
            });
        }

        LobbyWaitlistSnapshot? waitlist = lobby.Waitlist;
        if (waitlist is { Entries.Length: > 0 })
        {
            _rows.Children.Add(new TextBlock { Text = "Ordered waitlist" });
            foreach (LobbyQueueEntrySummary entry in waitlist.Entries)
                _rows.Children.Add(new TextBlock { Text = $"#{entry.Position} {entry.DisplayName} · {entry.State}" });
        }

        LobbyMember? self = lobby.Members.FirstOrDefault(x => x.SessionId == session.SessionId);
        if (self is not null)
            _rows.Children.Add(Button(self.Ready ? "Not ready" : "Ready",
                () => _play.SetReadyAsync(!self.Ready, _lifetime.Token)));
        if (waitlist?.IsSelfQueued == true)
        {
            if (waitlist.SelfOffer is { } offer)
            {
                _rows.Children.Add(new TextBlock { Text = $"PLAYER SLOT AVAILABLE · expires {offer.ExpiresAt.LocalDateTime:t}" });
                _rows.Children.Add(Button("Accept player slot", () => _play.AcceptWaitlistAsync(
                    lobby.LobbyId, lobby.Revision, offer.OfferId, _lifetime.Token)));
                _rows.Children.Add(Button("Decline slot", () => _play.DeclineWaitlistAsync(
                    lobby.LobbyId, lobby.Revision, offer.OfferId, _lifetime.Token)));
            }
            _rows.Children.Add(Button("Cancel waitlist", () => _play.LeaveWaitlistAsync(
                lobby.LobbyId, lobby.Revision, _lifetime.Token)));
        }
        else if (self is { Observer: true })
        {
            _rows.Children.Add(Button("Join player waitlist", () => _play.JoinWaitlistAsync(
                lobby.LobbyId, lobby.Revision, cancellationToken: _lifetime.Token)));
        }

        if (lobby.OwnerSessionId == session.SessionId)
        {
            if (lobby.Phase == LobbyPhase.Open && _play.AvailableMaps is { Count: > 0 } maps)
            {
                var map = new ComboBox
                {
                    ItemsSource = maps,
                    SelectedItem = maps.Contains(lobby.MapKey, StringComparer.Ordinal)
                        ? lobby.MapKey : maps[0]
                };
                var mode = new ComboBox { ItemsSource = Enum.GetValues<MatchMode>(), SelectedItem = lobby.Mode };
                _rows.Children.Add(new TextBlock { Text = "Map and mode" });
                _rows.Children.Add(map);
                _rows.Children.Add(mode);
                _rows.Children.Add(Button("Apply match settings", () => _play.ConfigureLobbyAsync(
                    map.SelectedItem as string ?? "", mode.SelectedItem is MatchMode selected
                        ? selected : MatchMode.Battle, lobby.BotCount, (LobbyRulesOptions?)null,
                    _lifetime.Token)));
                _rows.Children.Add(Button("Start match", () => _play.StartMatchAsync(_lifetime.Token)));
            }
            else if (lobby.Phase == LobbyPhase.Open)
                _rows.Children.Add(new TextBlock { Text = _play.MapCatalogMessage });
            if (lobby.Phase == LobbyPhase.PostMatch)
            {
                _rows.Children.Add(Button("Rematch", () => _play.RematchAsync(_lifetime.Token)));
                _rows.Children.Add(Button("Return to open lobby", () => _play.ReturnToLobbyAsync(_lifetime.Token)));
            }
        }

        _rows.Children.Add(Button("Leave lobby", () => _play.LeaveLobbyAsync(_lifetime.Token)));
        if (state.Node is { Handoff: not null, MatchEnded: false })
            _rows.Children.Add(Button("Retry Match Connection", async () =>
                await _play.RetryHandoffAsync(_lifetime.Token).ConfigureAwait(true)));
    }

    private string LobbyName()
    {
        string name = (_name.Text ?? "").Trim();
        return name.Length > 0 ? name : "Hunters";
    }
}
