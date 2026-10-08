#if MPHREAD_RMLUI_ANDROID
using System;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Presenters;
using MphRead.Mods.Network;

namespace MphRead.Droid;

internal sealed partial class AndroidRmlUiSession
{
    private LobbySessionController? _lobby;
    private RmlLobbyAdminPresenter? _admin;
    private RmlLobbyRulesEditor? _rules;
    private long _lobbyFrame;
    private readonly LobbyMapPreview _mapPreview = new(new MphRead.Mods.Launcher.RmlUi.Pages.Theatre.TheatreImageCache(decode: AndroidRmlUiImages.Decode));
    private (Guid Lifetime, ushort Match, ulong Epoch, uint Start)? _pendingStart;
    private bool _graphicsSuspended;

    private void InitializeLobby()
    {
        _rules = new(_rooms, (id,value) => Pages.SetText(id,value), (id,value) => Pages.SetBool(id,value),
            (id,value) => Pages.SetField(id,value));
        if (NetSession.Active && NetSession.PersistentLobby) OpenLobby(null);
    }
    private void OpenLobby(LobbyContext? context)
    {
        ClosePresenters(); RetireLobby(stopConnection: false);
        _lobby = new(context);
        _lobby.TransferPumpOwnership(LobbyPumpOwner.Native);
        _lobby.MatchRequested += MatchRequested;
        _lobby.Closed += LobbyClosed;
        _rules!.BindSession(_lobby);
        RmlUiLobbyBindings.Present(Pages, _lobby.Snapshot());
        Pages.ShowBaseline(RmlUiMenuPage.Lobby);
        Console.WriteLine("[rmlui-android] persistent native lobby opened");
    }
    private void PumpLobby()
    {
        if (_lobby is not { } lobby) return;
        lobby.PumpOnce(LobbyPumpOwner.Native, ++_lobbyFrame);
        if (!ReferenceEquals(_lobby, lobby)) return;
        _rules?.Tick(); _admin?.Update();
        RmlUiLobbyBindings.Present(Pages, lobby.Snapshot());
        if(lobby.Snapshot().Match is {} map) _mapPreview.Present(map.RoomKey,Pages.SetText,Pages.SetBool,Pages.Manager.Lifetime(Pages.Document));
    }
    private void MatchRequested(object? sender, LaunchPlan plan)
    {
        if (!ReferenceEquals(sender, _lobby)) return;
        LobbySnapshot state = _lobby!.Snapshot();
        if (!state.Active || state.Closed || !state.ShouldLoadMatch) return;
        _pendingStart = (state.Lifetime, state.MatchId, state.AuthorityEpoch, state.StartGeneration);
        _launch(plan);
    }
    private void LobbyClosed(object? sender, string reason)
    {
        if (!ReferenceEquals(sender, _lobby)) return;
        ClosePresenters(); RetireLobby(stopConnection: false);
        Pages.ShowBaseline(RmlUiMenuPage.Home); _route = RmlUiRouteArgument.Home;
        Pages.SetText("system_status", reason);
    }
    private void RetireLobby(bool stopConnection)
    {
        var lobby = _lobby; _lobby = null; _pendingStart = null;
        _rules?.ResetSession(); _rules?.BindSession(null);
        if (lobby == null) return;
        lobby.MatchRequested -= MatchRequested; lobby.Closed -= LobbyClosed;
        if (stopConnection) lobby.Dispose(); else lobby.Retire();
    }
    private void HandleLobby(RmlUiIntent intent)
    {
        if (_lobby is not { } lobby) return;
        LobbyIntentKind? command = intent.Kind switch
        {
            RmlUiIntentKind.LobbyReady => LobbyIntentKind.ToggleReady, RmlUiIntentKind.LobbyStart => LobbyIntentKind.StartMatch,
            RmlUiIntentKind.LobbyLeave => LobbyIntentKind.Leave, RmlUiIntentKind.LobbyNextHunter => LobbyIntentKind.NextHunter,
            RmlUiIntentKind.LobbyNextSuit => LobbyIntentKind.NextSuit, RmlUiIntentKind.LobbyMapRetry => LobbyIntentKind.RetryMap, _ => null
        };
        if (command is { } kind)
        {
            LobbyActionResult result = lobby.Dispatch(lobby.Intent(kind));
            if (!result.Accepted) Pages.SetText("lobby_status", result.Message);
            return;
        }
        switch (intent.Kind)
        {
            case RmlUiIntentKind.LobbyChatSend:
                var sent=lobby.Dispatch(lobby.Intent(LobbyIntentKind.SendChat) with {Text=Host.ReadField(intent.Document,"lobby_chat_input")});
                if(sent.Accepted)Host.SetField(intent.Document,"lobby_chat_input","");
                Pages.SetText("lobby_chat_status",sent.Accepted?"":sent.Message); break;
            case RmlUiIntentKind.LobbyAdminOpen:
                if (_admin?.Active != true) { _admin?.Dispose(); _admin = new(Host, lobby); _admin.Open(); } break;
            case RmlUiIntentKind.LobbyRulesOpen: _rules?.Open(); break;
            case RmlUiIntentKind.LobbyRulesClose: _rules?.Close(); break;
            case RmlUiIntentKind.LobbyRulesMap: _rules?.CycleMap(); break;
            case RmlUiIntentKind.LobbyRulesMode: _rules?.CycleMode(); break;
            case RmlUiIntentKind.LobbyRulesFormat: _rules?.CycleFormat(); break;
            case RmlUiIntentKind.LobbyRulesToggle: _rules?.Toggle(intent.Argument); break;
            case RmlUiIntentKind.LobbyRulesApply: _rules?.Apply(Pages.ReadField("rules_time"), Pages.ReadField("rules_goal")); break;
            case RmlUiIntentKind.LobbyClassic: Pages.SetText("lobby_status", "THE NATIVE ANDROID LOBBY OWNS THIS SESSION."); break;
        }
    }
    internal void SuspendGraphics()
    {
        if (_graphicsSuspended) return;
        string? stale = null;
        if (_pendingStart is { } expected && _lobby is { } lobby)
        {
            LobbySnapshot state = lobby.Snapshot();
            if (!state.Active || state.Closed || !state.ShouldLoadMatch || state.Lifetime != expected.Lifetime
                || state.MatchId != expected.Match || state.AuthorityEpoch != expected.Epoch || state.StartGeneration != expected.Start)
                stale = "The lobby start changed before Android acquired the match renderer.";
        }
        _lobby?.Suspend(); _lobby?.YieldPumpToGameplay();
        ClosePresenters(); _rules?.ResetSession();
        Pages.Dispose(); Host.Shutdown(); _graphicsSuspended = true;
        if (stale != null) throw new AndroidRmlUiLaunchChangedException(stale);
    }
    internal void ResumeGraphics(string root, int width, int height, float density)
    {
        if (!_graphicsSuspended) return;
        if (!Host.Initialize(width, height, density, root, RmlUiRenderBackend.DrawList))
            throw new InvalidOperationException("The Android RmlUi host could not resume after the match.");
        Host.Input.SetFramebufferScale(1, 1); Host.Input.DiagnosticsEnabled = true;
        Pages = new(Host); RefreshChrome();
        _graphicsSuspended = false; _pendingStart = null;
        if (_lobby is { } lobby && NetSession.Active && NetSession.PersistentLobby)
        {
            lobby.Resume(); _rules?.BindSession(lobby);
            RmlUiLobbyBindings.Present(Pages, lobby.Snapshot());
            if(lobby.Snapshot().Match is {} map) _mapPreview.Present(map.RoomKey,Pages.SetText,Pages.SetBool,Pages.Manager.Lifetime(Pages.Document));
            Pages.ShowBaseline(RmlUiMenuPage.Lobby);
        }
        else { RetireLobby(stopConnection: false); _route = RmlUiRouteArgument.Home; Pages.ShowBaseline(); }
        Console.WriteLine("[rmlui-android] same owner resumed documents and lobby authority");
    }
}

internal sealed class AndroidRmlUiLaunchChangedException(string message) : InvalidOperationException(message);
#endif
