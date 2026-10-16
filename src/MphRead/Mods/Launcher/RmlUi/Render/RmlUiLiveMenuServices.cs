#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Launcher.Gui;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Uses the exact same authoritative services and StartScreen lobby coordinator
/// as the current client. No RmlUi-owned NetSession.Pump or session mutation
/// occurs on a worker completion.
/// </summary>
internal sealed class RmlUiLiveMenuServices : IRmlUiMenuServices
{
    public HubSnapshot Hub() => HubState.Capture();

    public string BuildVersion()
        => MphRead.Mods.Update.BuildVersion.Current?.ToString(3) ?? "LOCAL";

    public RmlUiLobbyView Lobby()
    {
        if (!NetSession.Active || !NetSession.PersistentLobby
            || NetSession.ServerSession is not { } session
            || session.Phase is not (SessionPhase.Lobby or SessionPhase.Starting))
            return new(false, false, false, false, false, false, false,
                "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());

        var roster = NetSession.LobbyRoster();
        var players = new RmlUiLobbyPlayer[Math.Clamp(roster.Count, 0, 8)];
        for (int i = 0; i < players.Length; i++)
        {
            byte slot = roster.Slots[i];
            players[i] = new(
                roster.Names[i] ?? "",
                ((Hunter)roster.Hunters[i]).ToString().ToUpperInvariant(),
                roster.LobbyReady[i],
                slot == NetSession.LocalSlot,
                roster.IsBot(i));
        }
        bool inLobby = NetSession.IsInLobby;
        bool owner = NetSession.LocalIsLobbyOwner;
        bool localReady = NetSession.LocalSlot >= 0 && NetSession.LocalSlot < NetSession.SlotLobbyReady.Length
            && NetSession.SlotLobbyReady[NetSession.LocalSlot];
        bool canReady = inLobby && session.RequireReady
            && NetSession.LocalSlot >= 0 && !NetSession.LobbyCommandPending;
        bool canStart = inLobby && NetSession.CanEditLobby && !NetSession.LobbyCommandPending
            && LobbyRules.Validate(session.Match, roster, session.RequireReady, out _) == LobbyResultCode.Ok;
        string status = NetSession.LobbyMessage.Length != 0 ? NetSession.LobbyMessage
            : !NetSession.RequiredMapReady ? NetSession.MapPreparationMessage
            : session.Phase == SessionPhase.Starting
                ? "STARTING MATCH // WAITING FOR SERVER"
                : "WAITING FOR PLAYERS";

        return new(true, session.Phase == SessionPhase.Starting, owner, session.RequireReady,
            localReady, canReady, canStart, "MULTIPLAYER LOBBY",
            session.Match.RoomKey, session.Match.Mode.ToString().ToUpperInvariant(),
            session.Match.Format.ToString().ToUpperInvariant(), status,
            session.MaxPlayers, players);
    }

    public async Task<RmlUiDirectory> Browse(CancellationToken token)
    {
        var rows = new ConcurrentBag<RmlUiServerCandidate>();
        ServerDiscoveryResult result = await ServerBrowserService.DiscoverAsync(
            entry =>
            {
                if (entry.Live && entry.Compatible)
                    rows.Add(ToCandidate(entry));
            }, token).ConfigureAwait(false);
        return new(result.DirectoryAnswered, result.Message, rows.ToArray());
    }

    public async Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token)
    {
        QuickPlaySearchResult result = await ServerBrowserService.FindBestAsync(token).ConfigureAwait(false);
        return result.Found ? ToCandidate(result.Entry) : null;
    }

    private static RmlUiServerCandidate ToCandidate(ServerBrowserEntry entry)
        => new(entry.Name, entry.Endpoint, entry.Status.Players,
            Math.Max(1, entry.Status.MaxPlayers), entry.Status.Latency, entry.Compatible);

    public async Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token)
    {
        if (!GameFiles.Ready)
            return new(false, default, "Configure the required game files before joining.");
        if (!ServerBrowserService.TryParseEndpoint(endpoint, LauncherPrefs.ServerAddress,
            LauncherPrefs.ServerPort, out string host, out int port))
            return new(false, default, "Enter a valid server host or host:port.");

        HubSnapshot hub = HubState.Capture();
        string name = String.IsNullOrWhiteSpace(playerName) ? hub.PlayerName : playerName.Trim();
        OnlineJoinResult admitted = await ServerBrowserService.JoinAsync(host, port, name,
            hub.DisplayHunter, hub.Suit, token).ConfigureAwait(false);
        return new(admitted.Joined, admitted.Plan, admitted.Error);
    }

    public bool AcceptJoin(LaunchPlan plan) => Shell.RmlUiAcceptJoin(plan);
    public bool OpenCreateLobby() => Shell.RmlUiOpenCreateLobby();
    public bool NavigateTo(RmlUiRouteArgument route) => Shell.RmlUiNavigate(route);
    public bool OpenStudio() => Shell.RmlUiNavigateStudio();
    public bool LeaveLobby() => Shell.RmlUiLeaveLobby();

    public bool SetReady()
    {
        RmlUiLobbyView state = Lobby();
        return state.Active && state.CanReady
            && NetSession.SendLobbyCommand(LobbyCommandType.SetReady, ready: !state.LocalReady);
    }

    public bool StartMatch()
    {
        RmlUiLobbyView state = Lobby();
        return state.Active && state.CanStart
            && NetSession.SendLobbyCommand(LobbyCommandType.StartMatch);
    }

    public bool NextHunter() => Shell.RmlUiShowLegacyLobby();
    public bool NextSuit() => Shell.RmlUiShowLegacyLobby();
    public bool OpenLobbyDetails() => Shell.RmlUiShowLegacyLobby();

    public void Quit() => Shell.RequestQuit();
}
#endif
