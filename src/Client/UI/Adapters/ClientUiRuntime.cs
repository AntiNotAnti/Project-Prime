using System;
using System.Collections.Generic;
using Avalonia.Threading;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.Screens;
using MphRead.Mods.UI.State;

namespace MphRead.Mods.UI.Adapters;

/// <summary>
/// One platform-neutral composition root for the persistent shell. Desktop and Android keep the
/// same instance alive while their match surface is attached and detached.
/// </summary>
public sealed class ClientUiRuntime : IDisposable
{
    private readonly ClientSessionCoordinator _coordinator;
    private readonly LauncherUiServices? _productionServices;
    private bool _disposed;

    public ClientUiRuntime(MenuSettings settings, IReadOnlyList<string> rooms, bool isAndroid,
        UiScreenServices? services = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(rooms);
        _coordinator = ClientSessionCoordinator.Shared;
        State = new AppShellState();
        if (services is null)
        {
            _productionServices = new LauncherUiServices(this, settings, rooms, isAndroid);
            services = _productionServices.Services;
        }
        Services = services;
        Shell = new AppShellView(State, new UiScreenFactory(State.Router, Services));
        _coordinator.StateChanged += CoordinatorStateChanged;
        _coordinator.MatchTransitioned += MatchTransitioned;
        UpdateSession(_coordinator.State);
    }

    public event EventHandler<LaunchPlan>? LaunchRequested;
    public AppShellView Shell { get; }
    public AppShellState State { get; }
    public UiScreenServices Services { get; }
    public ClientSessionCoordinator Coordinator => _coordinator;
    public bool MatchIsRequested { get; private set; }
    public bool MatchIsActive { get; private set; }

    public void PollShell()
    {
        if (!MatchIsRequested && !MatchIsActive) _coordinator.Poll();
        _productionServices?.Lobby.Refresh();
    }

    public void MatchStarted()
    {
        MatchIsRequested = false;
        MatchIsActive = true;
    }

    public void MatchLaunchCancelled()
    {
        MatchIsRequested = false;
        MatchIsActive = false;
        UpdateSession(_coordinator.State);
    }

    /// <summary>Called after a renderer detaches. The session and shell stay alive.</summary>
    public void MatchCompleted(bool replay = false)
    {
        MatchIsRequested = false;
        MatchIsActive = false;
        if (replay)
        {
            State.Router.Replace(UiRoute.Replays);
            return;
        }
        UpdateSession(_coordinator.State);
        State.Router.Replace(_coordinator.State == ClientSessionState.PostMatch
            ? UiRoute.PostMatch
            : _coordinator.State == ClientSessionState.Lobby ? UiRoute.Lobby : UiRoute.Home);
    }

    public bool GoBack() => State.Router.GoBack();

    internal void Joined(bool hosted)
    {
        if (_coordinator.MatchPending)
        {
            RequestMatch(hosted ? LaunchKind.Host : LaunchKind.Online);
            return;
        }
        State.Router.Replace(UiRoute.Lobby);
    }

    internal void RequestReplay(string path)
    {
        Request(new LaunchPlan
        {
            Kind = LaunchKind.Demo,
            Hunter = Hunter.Samus,
            DemoPath = path,
            RoomKey = string.Empty,
            PlayerName = LauncherPrefs.PlayerName
        });
    }

    internal void LeaveSession()
    {
        _coordinator.Leave();
        State.Router.Reset(UiRoute.Home);
    }

    private void MatchTransitioned(MatchTransitionPacket transition)
        => PostUi(() => RequestMatch(NetHostSession.Running ? LaunchKind.Host : LaunchKind.Online));

    private void RequestMatch(LaunchKind kind)
    {
        AuthoritativePlay? session = _coordinator.Session;
        if (session is null) return;
        MatchRules rules = session.Client.Accepted.Rules;
        Request(new LaunchPlan
        {
            Kind = kind,
            Hunter = Hunters.Resolve(LauncherPrefs.LastHunter),
            RoomKey = rules.RoomKey,
            Mode = rules.Mode.ToLegacyMode(),
            Bots = LauncherPrefs.Bots,
            BotLevel = LauncherPrefs.BotLevel,
            Port = kind == LaunchKind.Host ? LauncherPrefs.HostPort : LauncherPrefs.ServerPort,
            PlayerName = LauncherPrefs.PlayerName
        });
    }

    private void Request(LaunchPlan plan)
    {
        if (MatchIsRequested) return;
        MatchIsRequested = true;
        LaunchRequested?.Invoke(this, plan);
    }

    private void CoordinatorStateChanged(ClientSessionState state)
        => PostUi(() => UpdateSession(state));

    private void UpdateSession(ClientSessionState state)
    {
        State.Session.Phase = state switch
        {
            ClientSessionState.Connecting => SessionPhase.Connecting,
            ClientSessionState.Lobby => SessionPhase.Lobby,
            ClientSessionState.LoadingMatch or ClientSessionState.InMatch => SessionPhase.Match,
            ClientSessionState.PostMatch => SessionPhase.PostMatch,
            ClientSessionState.Failed => SessionPhase.Failed,
            _ => SessionPhase.Offline
        };
        State.Session.StatusLabel = state switch
        {
            ClientSessionState.LoadingMatch => "Loading match",
            ClientSessionState.InMatch => "In match",
            ClientSessionState.PostMatch => "Results",
            ClientSessionState.Failed => "Connection failed",
            _ => state.ToString()
        };
        State.Session.Detail = _coordinator.Failure;
        if (state == ClientSessionState.PostMatch && !MatchIsRequested)
            State.Router.Replace(UiRoute.PostMatch);
        else if (state == ClientSessionState.Lobby
            && State.Router.CurrentRoute is UiRoute.PostMatch)
            State.Router.Replace(UiRoute.Lobby);
    }

    private static void PostUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.StateChanged -= CoordinatorStateChanged;
        _coordinator.MatchTransitioned -= MatchTransitioned;
        _productionServices?.Dispose();
        State.Dispose();
    }
}
