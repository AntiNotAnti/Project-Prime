#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Training;
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
        // Both the native nameplates and real engine-preview formation use
        // one presentation order: local/front, then other network slots.
        // Never infer roster identity from a visible display name.
        int rosterCount = Math.Clamp((int)roster.Count, 0, 8);
        var players = Enumerable.Range(0, rosterCount)
            .OrderByDescending(i => roster.Slots[i] == NetSession.LocalSlot)
            .ThenBy(i => roster.Slots[i])
            .Select(i => new RmlUiLobbyPlayer(
                roster.Names[i] ?? "",
                ((Hunter)roster.Hunters[i]).ToString().ToUpperInvariant(),
                roster.LobbyReady[i],
                roster.Slots[i] == NetSession.LocalSlot,
                roster.IsBot(i))).ToArray();
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

    public Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
    {
        string[] rooms = Shell.RmlUiBuiltInRooms().ToArray();
        if (!GameFiles.Ready) return Task.FromResult("CONFIGURE GAME FILES BEFORE OFFLINE PLAY");
        if (!request.ValidateForm(rooms)) return Task.FromResult("INVALID OFFLINE MAP OR MATCH CHOICE");
        // Entity/room capability checks can touch the map registry and must
        // never block the native compositor's GL owner thread.
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!MapModeCapabilities.Supports(request.RoomKey, request.Mode,
                    MatchWorldProfile.Resolve(request.Bots + 1), out string reason,
                    request.Bots + 1, allowLocalSource: true))
                return String.IsNullOrWhiteSpace(reason)
                    ? "THAT ARENA CANNOT RUN THE SELECTED MATCH" : reason;
            token.ThrowIfCancellationRequested();
            return "";
        }, token);
    }

    public bool LaunchOffline(RmlUiOfflineRequest request)
        => Shell.RmlUiLaunchOffline(request);

    public AimTrainerDefinition TrainingDefaults() => LauncherPrefs.Training.Sanitize();

    public bool LaunchTraining(AimTrainerDefinition drill, Hunter hunter, int suit)
        => Shell.RmlUiLaunchTraining(drill, hunter, suit);

    public RmlUiAdventureSlot[] ReadAdventureSlots()
    {
        var result = new RmlUiAdventureSlot[AdventureSave.SlotCount];
        for (byte slot = 1; slot <= AdventureSave.SlotCount; slot++)
        {
            AdventureSave.SlotInfo info = AdventureSave.Read(slot);
            string fingerprint = AdventureSave.ContentFingerprint(slot);
            result[slot - 1] = new(slot, info.Used,
                info.Unreadable || fingerprint == "UNAVAILABLE",
                info.Area ?? "", info.Octoliths, info.Health,
                info.HealthMax, fingerprint);
        }
        return result;
    }

    public bool LaunchAdventure(RmlUiAdventureSlot expected, bool newGame, Hunter hunter)
    {
        if (expected.Slot is < 1 or > AdventureSave.SlotCount || expected.Unreadable
            || (!newGame && !expected.Used)) return false;
        // A confirmation binds to exact bytes, not only a reused slot number
        // or an old displayed area/health string.
        RmlUiAdventureSlot[] now = ReadAdventureSlots();
        if (now[expected.Slot - 1] != expected) return false;
        return Shell.RmlUiLaunchAdventure(expected, newGame, hunter);
    }

    public Task<RmlUiReplayEntry[]> ReadTheatreAsync(CancellationToken token)
        => ReplayStorageJobs.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return DemoLibrary.List().Select(demo =>
            {
                var info = new FileInfo(demo.Path);
                bool interrupted = demo.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
                bool playable = !interrupted && demo.Compatibility == ReplayOpenResult.Success;
                return new RmlUiReplayEntry(
                    demo.Path, demo.DisplayName, DemoLibrary.Describe(demo),
                    demo.Room, demo.Recorded.Ticks, demo.Bytes,
                    info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                    demo.Favorite, demo.Metadata?.Type == ReplayType.Clip,
                    interrupted || demo.Compatibility == ReplayOpenResult.Truncated,
                    playable);
            }).ToArray();
        }, token);

    private static bool ExactReplay(RmlUiReplayEntry entry)
    {
        if (String.IsNullOrWhiteSpace(entry.Path)) return false;
        string file, folder, library;
        try
        {
            file = Path.GetFullPath(entry.Path);
            folder = Path.GetDirectoryName(file) ?? "";
            library = Path.GetFullPath(DemoLibrary.Directory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException) { return false; }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!String.Equals(folder, library, comparison)
            || (!file.EndsWith(DemoFile.Extension, comparison)
                && !file.EndsWith(DemoFile.LegacyExtension, comparison)))
            return false;
        var info = new FileInfo(file);
        return info.Exists && info.Length == entry.Bytes
            && info.LastWriteTimeUtc.Ticks == entry.LastWriteTicks;
    }

    public Task<string> ValidateReplayAsync(RmlUiReplayEntry entry, CancellationToken token)
        => ReplayStorageJobs.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!entry.Playable || !ExactReplay(entry))
                return "REPLAY CHANGED, IS INTERRUPTED, OR IS OUTSIDE THE LOCAL LIBRARY";
            ReplayLaunchValidator.Result result = ReplayLaunchValidator.Probe(entry.Path);
            token.ThrowIfCancellationRequested();
            return result.Ok ? "" : result.Error ?? "REPLAY FAILED VALIDATION";
        }, token);

    public bool LaunchReplay(RmlUiReplayEntry entry)
        => entry.Playable && ExactReplay(entry) && Shell.RmlUiLaunchReplay(entry);

    public bool ToggleReplayFavorite(RmlUiReplayEntry entry)
    {
        if (!ExactReplay(entry) || !entry.Playable) return false;
        try
        {
            DemoLibrary.ToggleFavorite(entry.Path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    public RmlUiLobbyChrome ReadLobbyChrome()
    {
        if (!NetSession.IsInLobby || NetSession.ServerSession is not { } session)
            return RmlUiLobbyChrome.Empty;
        MatchDefinition match = session.Match;
        string time = match.TimeLimitSeconds == 0 ? "NO TIME LIMIT"
            : (match.TimeLimitSeconds / 60) + ":" + (match.TimeLimitSeconds % 60).ToString("00");
        string score = match.PointGoal.ToString() +
            (MatchGoalRules.UsesLives(match.Mode) ? " SPARE LIVES"
                : MatchGoalRules.UsesTimeTarget(match.Mode) ? " SEC TO CONTROL"
                : " POINT GOAL");
        string history = String.Join("\n", MphRead.Mods.Chat.NetChat.History.TakeLast(12));
        if (history.Length == 0) history = "NO MESSAGES";
        string status = NetSession.LobbyCommandPending ? "WAITING FOR SERVER"
            : "CHAT SENDS THROUGH THE CURRENT LOBBY";
        string field = !NetSession.RequiredMapReady ? NetSession.MapPreparationMessage
            : session.Phase == SessionPhase.Starting ? "STARTING MATCH" : "MAP READY";
        return new(time, score, history, status, field,
            String.IsNullOrWhiteSpace(NetSession.MapPreparationMessage)
                ? "MAP: " + match.RoomKey + " // PREVIEW NOT AVAILABLE"
                : NetSession.MapPreparationMessage,
            session.Phase == SessionPhase.Starting ? "STARTING" : "",
            session.Phase == SessionPhase.Starting
                ? NetSession.LobbyMessage : "");
    }

    public bool SendLobbyChat(string message)
    {
        if (!NetSession.Active || !NetSession.IsInLobby
            || NetSession.LocalSlot < 0 || NetSession.LobbyCommandPending
            || String.IsNullOrWhiteSpace(message))
            return false;
        string trimmed = message.Trim();
        if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > ChatPacket.MaxTextBytes)
            return false;
        MphRead.Mods.Chat.NetChat.Send(trimmed);
        // A send is a request; authoritative receipt arrives in NetChat.History.
        return true;
    }

    public bool CycleLobbyTeam(int presentationIndex)
    {
        RmlUiAdminSnapshot? roster = ReadAdmin();
        RmlUiAdminPlayer? player = RmlUiLobbyDisplayOrder.Resolve(roster, presentationIndex);
        if (roster == null || !RmlUiLobbyDisplayOrder.CanChangeTeam(roster, player))
            return false;
        RmlUiAdminPlayer actor = player!.Value;
        return SubmitAdmin(new RmlUiAdminRequest(RmlUiAdminOperation.SetTeam,
            roster.MatchId, roster.AuthorityEpoch, roster.SessionRevision,
            actor.Slot, actor.Generation, Team: RmlUiLobbyDisplayOrder.NextTeam(roster, actor)));
    }

    public RmlUiAdminSnapshot? ReadAdmin()
    {
        if (!NetSession.Active || !NetSession.IsInLobby
            || NetSession.ServerSession is not { } session)
            return null;
        RosterPacket roster = NetSession.LobbyRoster();
        if (roster.MatchId != session.MatchId
            || roster.AuthorityEpoch != session.AuthorityEpoch)
            return null;
        int count = Math.Clamp((int)roster.Count, 0, 8);
        var players = new RmlUiAdminPlayer[count];
        for (int i = 0; i < count; i++)
            players[i] = new(roster.Slots[i], roster.Generations[i],
                roster.Names[i] ?? "", (Hunter)roster.Hunters[i],
                roster.Colors[i], roster.Teams[i], roster.LobbyReady[i],
                roster.IsBot(i), roster.BotLevels[i],
                roster.DamageReductions[i], roster.Pings[i]);
        int teams = GameState.IsTeamMode(session.Match.Mode)
            ? LobbyRules.ResolveTeamLayout(session.Match).TeamCount : 0;
        return new(session.MatchId, session.AuthorityEpoch, session.Revision,
            NetSession.LocalIsLobbyOwner, session.OwnerSlot,
            NetSession.LocalSlot >= 0 ? (byte)NetSession.LocalSlot : byte.MaxValue,
            NetSession.LobbyCommandPending, session.LockTeams,
            (byte)Math.Clamp((int)teams, 0, 4), session.MaxPlayers,
            NetSession.MapPreparation?.State == MapAvailabilityState.Failed,
            NetSession.MapPreparationMessage, NetSession.LobbyMessage,
            HunterRules.Pool(session.Match.LowTier).ToArray(), players,
            MphRead.Mods.Chat.NetChat.History.TakeLast(32).ToArray());
    }

    public bool SubmitAdmin(RmlUiAdminRequest request)
    {
        RmlUiAdminSnapshot? current = ReadAdmin();
        if (current == null || request.MatchId != current.MatchId
            || request.AuthorityEpoch != current.AuthorityEpoch
            || request.SessionRevision != current.SessionRevision
            || current.CommandPending) return false;
        RmlUiAdminPlayer? target = current.Players.FirstOrDefault(p =>
            p.Slot == request.TargetSlot && p.Generation == request.TargetGeneration);
        bool found = request.TargetSlot != byte.MaxValue
            && target.HasValue && target.Value.Generation != 0;
        if (request.TargetSlot != byte.MaxValue && !found) return false;
        bool owner = current.Owner && NetSession.CanEditLobby;
        bool teamMember = found && target!.Value.Slot == current.LocalSlot;
        bool teamAllowed = found && current.TeamCount > 0
            && (owner || (teamMember && !current.TeamsLocked));
        bool bot = found && target!.Value.Bot;
        bool validBotOptions = request.Suit < 4 && request.BotLevel < 4
            && (request.Hunter == Hunter.Random
                || current.AllowedHunters.Contains(request.Hunter));
        switch (request.Operation)
        {
            case RmlUiAdminOperation.SendChat:
                if (String.IsNullOrWhiteSpace(request.Text)
                    || System.Text.Encoding.UTF8.GetByteCount(request.Text)
                        > ChatPacket.MaxTextBytes) return false;
                MphRead.Mods.Chat.NetChat.Send(request.Text);
                return true;
            case RmlUiAdminOperation.RetryMap:
                if (!current.CanRetryMap) return false;
                NetSession.RetryMapPreparation();
                return true;
            case RmlUiAdminOperation.SetTeam:
                return teamAllowed && request.Team >= -1 && request.Team < current.TeamCount
                    && NetSession.SendLobbyCommand(LobbyCommandType.SetTeam,
                        request.TargetSlot, request.Team);
            case RmlUiAdminOperation.SetHandicap:
                return owner && found && PlayerHandicap.IsValid(request.Handicap)
                    && NetSession.SendLobbyCommand(LobbyCommandType.SetHandicap,
                        request.TargetSlot, damageReduction: request.Handicap);
            case RmlUiAdminOperation.Kick:
                return owner && found && !bot
                    && target!.Value.Slot != current.OwnerSlot
                    && NetSession.SendLobbyCommand(LobbyCommandType.KickPlayer,
                        request.TargetSlot);
            case RmlUiAdminOperation.TransferOwner:
                return owner && found && !bot
                    && target!.Value.Slot != current.OwnerSlot
                    && NetSession.SendLobbyCommand(LobbyCommandType.TransferOwner,
                        request.TargetSlot);
            case RmlUiAdminOperation.CloseLobby:
                return owner && NetSession.SendLobbyCommand(LobbyCommandType.CloseLobby);
            case RmlUiAdminOperation.AddBot:
                return owner && validBotOptions && current.Players.Length < current.MaxPlayers
                    && NetSession.SendLobbyCommand(LobbyCommandType.AddBot,
                        hunter: (byte)request.Hunter, color: request.Suit,
                        botLevel: request.BotLevel);
            case RmlUiAdminOperation.UpdateBot:
                return owner && bot && validBotOptions
                    && NetSession.SendLobbyCommand(LobbyCommandType.UpdateBot,
                        request.TargetSlot, hunter: (byte)request.Hunter,
                        color: request.Suit, botLevel: request.BotLevel);
            case RmlUiAdminOperation.RemoveBot:
                return owner && bot && NetSession.SendLobbyCommand(
                    LobbyCommandType.RemoveBot, request.TargetSlot);
            case RmlUiAdminOperation.ToggleTeamLock:
                if (!owner || current.TeamCount == 0 || NetSession.ServerSession is not { } state)
                    return false;
                state.RuleFlags ^= LobbyRuleFlags.LockTeams;
                return NetSession.SendLobbyCommand(LobbyCommandType.UpdateMatch,
                    configuration: state);
            default:
                return false;
        }
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
