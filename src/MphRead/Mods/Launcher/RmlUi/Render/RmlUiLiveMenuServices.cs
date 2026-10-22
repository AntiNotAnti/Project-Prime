#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Training;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Uses the exact same authoritative services and StartScreen lobby coordinator
/// as the current client. No RmlUi-owned NetSession.Pump or session mutation
/// occurs on a worker completion.
/// </summary>
internal sealed partial class RmlUiLiveMenuServices : IRmlUiMenuServices, IDisposable
{
    private readonly Func<NativePresenceView?>? _presenceView;
    internal RmlUiLiveMenuServices(Func<NativePresenceView?>? presenceView = null)
        => _presenceView = presenceView;
    private readonly RmlUiMapPreviewCache _mapPreviews = new(
        Path.Combine(GameFiles.Root, "rmlui-media", "map-previews"));
    private readonly RmlUiMapPickerPreviews _pickerPreviews = new();
    private string _previewRoom = "", _previewRoot = "";
    private bool _previewAttempted;
    private CancellationTokenSource? _previewCancel;
    private Task? _previewGeneration;

    private NativePartyAuthorityController? _homeParty;
    private long _homePartyRefreshAt;
    private bool _homePartyPageActive;
    internal void SetHomePartyPageActive(bool active)
    {
        _homePartyPageActive = active;
        if (!active) _homePartyRefreshAt = 0;
    }
    internal NativePartyAuthorityController HomePartyController
        => _homeParty ??= new(new AuthenticatedNativePartyAuthority());

    public NativePartyView PartyHome()
    {
        // Reuse the Party page's controller and its account/revision fences.
        // Refresh only on the menu owner; no parallel network-session owner.
        if (SocialClient.Current?.Self == null)
        {
            _homePartyRefreshAt = 0;
            return new(false, false, "PARTY NOT CONNECTED", 0, null);
        }
        NativePartyAuthorityController owner = HomePartyController;
        NativePartyView view = owner.Tick();
        long now = Environment.TickCount64;
        if (!_homePartyPageActive && !view.Busy && now >= _homePartyRefreshAt)
        {
            _homePartyRefreshAt = now + 15000;
            owner.Refresh(preserveCurrent: true);
            view = owner.Tick();
        }
        return view;
    }

    public void Dispose()
    {
        lock(_createdJoinGate)_createdJoinRetired=true;
        CancelCreatedLobby();
        CancelPartyJoin(); CancelSocialJoin(); _mapPreviews.Dispose(); _pickerPreviews.Dispose();
        _homeParty?.Dispose(); _homeParty = null;
        RetireMapPreview();
    }

    private void RetireMapPreview()
    {
        _previewRoom = _previewRoot = "";
        _previewAttempted = false;
        if (_previewCancel is not { } cancel) return;
        _previewCancel = null;
        cancel.Cancel();
        if (_previewGeneration is { IsCompleted: false } work)
            _ = work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
        else cancel.Dispose();
    }

    private bool EnsureMapPreview(string room)
    {
        string root = GameFiles.Root;
        if (_previewRoom != room || _previewRoot != root)
        {
            RetireMapPreview();
            _previewRoom = room; _previewRoot = root;
        }
        // One request, through ThumbnailBatch's existing serialized worker,
        // per selected room/root. A retired worker never publishes UI state.
        if (_previewGeneration is { IsCompleted: false }) return true;
        _previewGeneration = null;
        if (_previewAttempted || String.IsNullOrWhiteSpace(room)) return false;
        _previewAttempted = true;
        var cancel = _previewCancel = new();
        _previewGeneration = Task.Run(async () =>
        {
            try { await ThumbnailHost.RenderRoomAsync(room, root, _ => { }, cancel.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OutOfMemoryException
                and not StackOverflowException and not AccessViolationException)
            { DebugLog.Line("rmlui", "Map preview generation unavailable: " + error.GetType().Name); }
        });
        return true;
    }

    private static readonly NewsDispatch? HomeDispatch =
        new BundledNewsProvider().Read().FirstOrDefault();
    public NewsDispatch? FeaturedNews() => HomeDispatch;
    public HubSnapshot Hub() => HubState.Capture();
    public RmlUiSocialHomeState SocialHome()
    {
        SocialSnapshot? state = SocialClient.Current;
        if (state?.Self == null || state.Friends == null || state.IncomingRequests == null)
            return new(true, false, Array.Empty<string>(), 0);
        var projected = RmlUiSocialHomeState.Project(new(state.Self.PrimeId,
            state.Friends.Where(p => p != null)
                .Select(p => new NativeSocialPerson(p.PrimeId, p.DisplayName ?? "Player", NativeSocialRelation.Friend))
                .ToArray()) { AccountWitness = state.AccountWitness }, _presenceView?.Invoke()?.Directory, state.IncomingRequests.Count,
            DateTimeOffset.UtcNow);
        var invites = SocialClient.InvitationHome;
        return invites != null && invites.Directory.SelfPrimeId == state.Self.PrimeId
            && ReferenceEquals(invites.Directory.AccountWitness, state.AccountWitness)
                ? projected with { InvitationsVerified = invites.Directory.InvitationsAvailable,
                    IncomingInvitations = invites.Directory.Invitations.Count(i => i.Incoming
                        && NativeSocialInvitationValidation.Actionable(i, DateTimeOffset.UtcNow)) }
                : projected;
    }



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
        bool rulesReady = LobbyRules.Validate(session.Match, roster, session.RequireReady,
            out string startReason) == LobbyResultCode.Ok;
        bool canStart = inLobby && NetSession.CanEditLobby && !NetSession.LobbyCommandPending && rulesReady;
        string status = NetSession.LobbyMessage.Length != 0 ? NetSession.LobbyMessage
            : !NetSession.RequiredMapReady ? NetSession.MapPreparationMessage
            : !NetSession.RequiredWeaponPackagesReady ? NetSession.WeaponPackagePreparationMessage
            : session.Phase == SessionPhase.Starting
                ? "STARTING MATCH // WAITING FOR SERVER"
                : !rulesReady ? startReason.ToUpperInvariant()
                : "READY TO DEPLOY";
        status += " // MOVEMENT: " + Movement.MovementModelRules.DisplayName(session.Match.MovementModel).ToUpperInvariant();

        return new(true, session.Phase == SessionPhase.Starting, owner, session.RequireReady,
            localReady, canReady, canStart, "MULTIPLAYER LOBBY",
            session.Match.RoomKey, session.Match.Mode.ToString().ToUpperInvariant(),
            session.Match.Format.ToString().ToUpperInvariant(), status,
            session.MaxPlayers, players, PackageReviewRequired: !NetSession.RequiredWeaponPackagesReady);
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
        var found = new ConcurrentBag<RmlUiServerCandidate>();
        await ServerBrowserService.DiscoverAsync(entry =>
        {
            if (entry.Live && entry.Compatible && entry.Status.MaxPlayers > 0
                && entry.Status.Players < entry.Status.MaxPlayers)
                found.Add(ToCandidate(entry));
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return RmlUiRandomMatch.Choose(found.ToArray(), Random.Shared.Next);
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

    public Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
    {
        string root = GameFiles.Root;
        string[] rooms = Shell.RmlUiOfflineRooms().ToArray();
        if (!GameFiles.Ready) return Task.FromResult("CONFIGURE GAME FILES BEFORE OFFLINE PLAY");
        if (!request.ValidateForm(rooms)) return Task.FromResult("SELECT AN INSTALLED OFFLINE ARENA AND TRY AGAIN");
        // Reading the entity layer belongs to an immutable configured-files
        // lease. Other menu/status workers can otherwise republish the global
        // Paths between form validation and the asynchronous map inspection.
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var files = GameFiles.TryUseConfiguredFiles(root);
            if (files == null) return "GAME FILE CONFIGURATION CHANGED // REOPEN OFFLINE BATTLE";
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

    public RmlUiOfflineRules? ReadOfflineRules() => Shell.RmlUiReadOfflineRules();

    public bool SaveOfflineRules(RmlUiOfflineRules expected, RmlUiOfflineRules next, out string error)
        => Shell.RmlUiSaveOfflineRules(expected, next, out error);

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

    public bool CanOpenCampaignShip(byte slot) => !NetSession.Active
        && MatchStart.RecentShipDock is { } dock && dock.Slot == slot;

    public bool OpenCampaignShip(byte slot, Hunter hunter)
        => !NetSession.Active && MatchStart.RecentShipDock is { } dock && dock.Slot == slot
            && Shell.Window is { HasScene: false } window
            && RmlUiOpenGlSmoke.TryOpenNativeCampaignShip(window, dock, hunter);

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
    {
        string root = GameFiles.Root;
        return ReplayStorageJobs.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            // Status reads may republish Paths. Keep the configured-files
            // capability across the worker's directory and metadata reads.
            using var files = GameFiles.TryUseConfiguredFiles(root)
                ?? throw new InvalidOperationException("GAME FILE CONFIGURATION CHANGED // REFRESH REPLAY LIBRARY");
            return ScanReplayEntries(token);
        }, token);
    }

    private RmlUiVirtualReplayLibrary.Prepared? _preparedVirtualReplay;

    private static RmlUiReplayEntry[] ScanReplayEntries(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (ReplayMutationGate)
        {
            token.ThrowIfCancellationRequested();
            var recordings = DemoLibrary.List();
            return recordings.Select(demo =>
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(demo.Path);
                bool interrupted = demo.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
                bool playable = !interrupted && demo.Compatibility == ReplayOpenResult.Success;
                var entry = new RmlUiReplayEntry(
                    demo.Path, demo.DisplayName, DemoLibrary.Describe(demo),
                    demo.Room, demo.Recorded.Ticks, demo.Bytes,
                    info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                    demo.Favorite, demo.Metadata?.Type == ReplayType.Clip,
                    interrupted || demo.Compatibility == ReplayOpenResult.Truncated,
                    playable);
                return RmlUiVirtualReplayLibrary.ProjectMetadata(entry, demo.Metadata, demo.DurationFrames);
            }).Concat(RmlUiVirtualReplayLibrary.List(DemoLibrary.Directory, recordings, token)).ToArray();
        }
    }

    private static bool ExactReplay(RmlUiReplayEntry entry, bool allowInterrupted = false)
    {
        if (String.IsNullOrWhiteSpace(entry.Path)) return false;
        if (entry.VirtualWitness.HasValue || RmlUiVirtualReplayLibrary.IsVirtual(entry.Path))
            return RmlUiVirtualReplayLibrary.Exact(entry, DemoLibrary.Directory, requireSource: !allowInterrupted);
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
                && !file.EndsWith(DemoFile.LegacyExtension, comparison)
                && !(allowInterrupted && (file.EndsWith(DemoFile.Extension + ".part", comparison)
                    || file.EndsWith(DemoFile.LegacyExtension + ".part", comparison)))))
            return false;
        var info = new FileInfo(file);
        return info.Exists && info.LinkTarget == null && info.Length == entry.Bytes
            && info.LastWriteTimeUtc.Ticks == entry.LastWriteTicks;
    }

    public Task<string> ValidateReplayAsync(RmlUiReplayEntry entry, CancellationToken token)
    {
        string root = GameFiles.Root;
        return ReplayStorageJobs.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var files = GameFiles.TryUseConfiguredFiles(root);
            if (files == null) return "GAME FILE CONFIGURATION CHANGED // REFRESH REPLAY LIBRARY";
            lock (ReplayMutationGate)
            {
                token.ThrowIfCancellationRequested();
                _preparedVirtualReplay = null;
                if (!entry.Playable || !ExactReplay(entry))
                    return "REPLAY CHANGED, IS INTERRUPTED, OR IS OUTSIDE THE LOCAL LIBRARY";
                string path = entry.Path;
                RmlUiVirtualReplayLibrary.Prepared? prepared = null;
                if (entry.VirtualWitness.HasValue)
                {
                    prepared = RmlUiVirtualReplayLibrary.Resolve(entry, DemoLibrary.Directory, token, out var open);
                    if (prepared == null) return "VIRTUAL CLIP COULD NOT BE PREPARED // " + open.ToString().ToUpperInvariant();
                    path = prepared.Value.PlaybackPath;
                }
                ReplayLaunchValidator.Result result = ReplayLaunchValidator.Probe(path);
                token.ThrowIfCancellationRequested();
                if (!result.Ok) return result.Error ?? "REPLAY FAILED VALIDATION";
                if (!ExactReplay(entry)) return "REPLAY OR VIRTUAL CLIP SOURCE CHANGED // REFRESH";
                _preparedVirtualReplay = prepared;
                return "";
            }
        }, token);
    }

    public bool LaunchReplay(RmlUiReplayEntry entry)
        => LaunchReplay(entry, openEditor: false);

    public bool LaunchReplay(RmlUiReplayEntry entry, bool openEditor)
    {
        if (openEditor && !ReplayEditingAllowed) return false;
        lock (ReplayMutationGate)
        {
            if (!entry.Playable || !ExactReplay(entry)) return false;
            if (entry.VirtualWitness.HasValue)
            {
                if (_preparedVirtualReplay is not { } prepared
                    || !RmlUiVirtualReplayLibrary.PreparedUnchanged(prepared, entry, DemoLibrary.Directory)) return false;
                // Existing playback consumes the materialized file. Its LogicalPath
                // maps this owned cache back to the descriptor for annotations.
                entry = entry with { Path = prepared.PlaybackPath, Bytes = prepared.Bytes,
                    LastWriteTicks = prepared.LastWriteTicks, VirtualWitness = null };
            }
            return Shell.RmlUiLaunchReplay(entry, openEditor);
        }
    }

    public bool ToggleReplayFavorite(RmlUiReplayEntry entry)
    {
        if (!ExactReplay(entry) || !entry.Playable) return false;
        try
        {
            if (entry.VirtualWitness.HasValue)
            {
                if (!RmlUiVirtualReplayLibrary.SafeArtifacts(entry, DemoLibrary.Directory)) return false;
                MphRead.Mods.Replay.ReplayVirtualClips.ToggleFavorite(entry.Path);
            }
            else DemoLibrary.ToggleFavorite(entry.Path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool AcceptJoin(LaunchPlan plan) => Shell.RmlUiAcceptJoin(plan);
    public bool OpenCreateLobby() => Shell.RmlUiOpenCreateLobby();
    public bool ConfigureGameFiles() => Shell.RmlUiConfigureGameFiles();
    public bool NavigateTo(RmlUiRouteArgument route) => Shell.RmlUiNavigate(route);
    public bool OpenStudio() => Shell.RmlUiNavigateStudio();
    public bool ReplayEditingAllowed => Shell.StudioWindow;
    public bool OpenStudioReplay(RmlUiReplayEntry entry, out string error)
    {
        error = "REPLAY CHANGED // REFRESH THE LIBRARY";
        lock (ReplayMutationGate)
        {
            if (ReplayEditingAllowed || !entry.Playable || !ExactReplay(entry)) return false;
            bool accepted = MphRead.Mods.StudioIntegration.StudioApplicationLauncher.TryOpen(entry.Path, false, out string? problem);
            error = problem ?? "";
            return accepted;
        }
    }
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
        if (!NetSession.Active || !NetSession.PersistentLobby || NetSession.ServerSession is not { } session
            || session.Phase is not (SessionPhase.Lobby or SessionPhase.Starting))
        {
            RetireMapPreview();
            _mapPreviews.Read("");
            return RmlUiLobbyChrome.Empty;
        }
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
        string mapName = LauncherRoomDisplayName.Get(match.RoomKey);
        string preview = _mapPreviews.Read(ThumbnailGenerator.PathFor(match.RoomKey));
        bool generatingPreview = preview.Length == 0 && EnsureMapPreview(match.RoomKey);
        var start = RmlUiLobbyStartPresentation.Read(session, NetSession.StartCountdownRemainingSeconds);
        return new(time, score, history, status, field,
            preview.Length != 0 ? "MAP: " + mapName
            : generatingPreview ? "MAP: " + mapName + " // GENERATING PREVIEW"
            : String.IsNullOrWhiteSpace(NetSession.MapPreparationMessage)
                ? "MAP: " + mapName + " // PREVIEW NOT AVAILABLE"
                : NetSession.MapPreparationMessage,
            start.Number, start.Detail) { MapPreviewPath = preview };
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
            NetSession.LobbyAdministrationPending, session.LockTeams,
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
    public int LobbyPlayerCapacity(string roomKey) => Math.Clamp(
        MapGen.CustomRooms.Definitions.FirstOrDefault(map => String.Equals(map.Name, roomKey,
            StringComparison.Ordinal))?.Capabilities?.MaxPlayers ?? 8, 2, 8);
    public IReadOnlyList<string> OfflineArenaKeys() => Shell.RmlUiOfflineRooms();

    public Task<IReadOnlyList<RmlUiArenaChoice>> ReadMapPickerCatalogAsync(CancellationToken token)
        => RmlUiMapPickerCatalog.ReadAsync(GameFiles.Root, token);

    public RmlUiArenaPreview[] ReadMapPickerPreviews(IReadOnlyList<string> rooms) => _pickerPreviews.Read(rooms);

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
