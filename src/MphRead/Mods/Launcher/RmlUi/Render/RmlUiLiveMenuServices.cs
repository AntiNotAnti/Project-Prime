#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        var players = new RmlUiLobbyPlayer[Math.Clamp((int)roster.Count, 0, 8)];
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

    public async Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token)
    {
        LobbyCreationOutcome outcome = await LobbyCreationService.CreateAsync(request, token)
            .ConfigureAwait(false);
        return new(outcome.Joined, outcome.Plan, outcome.Error);
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

    public bool NextHunter()
    {
        if (!NetSession.IsInLobby || NetSession.LobbyCommandPending
            || NetSession.ServerSession is not { } session
            || NetSession.LocalSlot < 0) return false;
        var allowed = HunterRules.Pool(session.Match.LowTier);
        int index = -1;
        for (int i = 0; i < allowed.Count; i++)
            if (allowed[i] == NetSession.LocalHunter) { index = i; break; }
        NetSession.LocalHunter = allowed[(index + 1) % allowed.Count];
        LauncherPrefs.LastHunter = NetSession.LocalHunter;
        LauncherPrefs.Save();
        NetSession.SendIdentify();
        return true;
    }

    public bool NextSuit()
    {
        if (!NetSession.IsInLobby || NetSession.LobbyCommandPending
            || NetSession.LocalSlot < 0) return false;
        NetSession.LocalColor = (NetSession.LocalColor + 1) % 4;
        LauncherPrefs.LastColor = NetSession.LocalColor;
        LauncherPrefs.Save();
        NetSession.SendIdentify();
        return true;
    }

    public RmlUiHunterDraft? ReadHunterDraft()
    {
        if (!NetSession.Active || !NetSession.IsInLobby
            || NetSession.ServerSession is not { } session || NetSession.LocalSlot < 0)
            return null;
        return new RmlUiHunterDraft(
            session.MatchId, session.AuthorityEpoch,
            NetSession.LocalHunter, (byte)NetSession.LocalColor,
            HunterRules.Pool(session.Match.LowTier).ToArray(), NetSession.LobbyCommandPending);
    }

    public bool ApplyHunterDraft(Hunter hunter, byte suit, ushort matchId, ulong authorityEpoch)
    {
        RmlUiHunterDraft? current = ReadHunterDraft();
        if (!current.HasValue || current.Value.CommandPending
            || current.Value.MatchId != matchId
            || current.Value.AuthorityEpoch != authorityEpoch
            || suit > 3 || !current.Value.Allowed.Contains(hunter))
            return false;

        NetSession.LocalHunter = hunter;
        NetSession.LocalColor = suit;
        LauncherPrefs.LastHunter = hunter;
        LauncherPrefs.LastColor = suit;
        LauncherPrefs.Save();
        NetSession.SendIdentify();
        // SendIdentify requests the identity; it does not prove server admission.
        return true;
    }

    public bool OpenLobbyDetails() => Shell.RmlUiShowLegacyLobby();

    public RmlUiRuleDraft? ReadRules()
    {
        if (!NetSession.Active || !NetSession.IsInLobby
            || NetSession.ServerSession is not { } session) return null;
        return new(session.MatchId, session.AuthorityEpoch, session.Revision,
            session.Match, session.RuleFlags);
    }

    public IReadOnlyList<string> BuiltInArenaKeys() => Shell.RmlUiBuiltInRooms();

    public IReadOnlyList<string> InstalledCommunityArenaKeys()
        => MphRead.Mods.MapGen.CustomRooms.Definitions
            .Where(map => MphRead.Mods.MapGen.CustomRooms.Installed.TryGet(map.Name, out var installed)
                && MphRead.Mods.MapGen.CustomRooms.Installed.HasExact(installed.Identity))
            .Select(map => map.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool SubmitRules(RmlUiRuleDraft draft, out string error)
    {
        error = "";
        if (!NetSession.CanEditLobby || NetSession.LobbyCommandPending
            || NetSession.ServerSession is not { } live
            || live.MatchId != draft.MatchId || live.AuthorityEpoch != draft.AuthorityEpoch
            || live.Revision != draft.SessionRevision)
        {
            error = "Server session changed or rule edit is unavailable.";
            return false;
        }
        if (LobbyRules.ValidateDefinition(draft.Match, out error) != LobbyResultCode.Ok)
            return false;
        // Preserve authority-created map package identity and all other
        // session fields. Only match/rules are updated by this command.
        SessionStatePacket updated = live;
        updated.Match = draft.Match;
        updated.RuleFlags = draft.Flags;
        if (!NetSession.SendLobbyCommand(LobbyCommandType.UpdateMatch, configuration: updated))
        {
            error = "Server cannot accept another command yet.";
            return false;
        }
        return true;
    }

    public void Quit() => Shell.RequestQuit();
}
#endif
