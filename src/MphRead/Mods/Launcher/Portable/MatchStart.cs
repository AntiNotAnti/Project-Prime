using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Campaign;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Turning a <see cref="LaunchPlan"/> into a running match.
    ///
    /// This used to sit inside <c>LauncherEntry</c>, which is WinForms and
    /// therefore Windows. Nothing in it is: it opens a <see cref="RenderWindow"/>,
    /// fills the player slots and loads a room, all of which the Linux build
    /// has always been able to do. Having it here is what lets
    /// <see cref="TextLauncher"/> start exactly the match the window would have
    /// started -- the same bot count, the same cap on the slots, the same
    /// deference to what the server says it is running -- instead of a second
    /// implementation that agrees with it until it does not.
    /// </summary>
    public static class MatchStart
    {
        /// <summary>The last reason a synchronous launch returned false.</summary>
        public static string? LastError { get; private set; }
        // Failed commits remain retryable without retaining the old world.
        private static PostMatchCommitContext? _pendingCommit;
        internal static bool HasPendingCommit => _pendingCommit != null;

        /// <summary>In-memory ship boarding after a successfully committed save.</summary>
        public static CampaignDockReceipt? RecentShipDock { get; private set; }
        public static void ClearRecentShipDock() => RecentShipDock = null;
        public static CampaignEndingSummary? RecentCampaignEnding { get; private set; }
        public static void ClearRecentCampaignEnding() => RecentCampaignEnding = null;

        /// <summary>
        /// Load what the plan asked for into a window of its own and run until
        /// the match ends.
        ///
        /// The text launcher's path, and the only one left that owns a window:
        /// it has no front screen to come back to, so a match is the whole of
        /// its session. The launcher proper calls <see cref="Begin"/> against
        /// the window it is already running in.
        /// </summary>
        public static void Launch(MenuSettings settings, LaunchPlan plan)
        {
            using var configuredFiles = GameFiles.TryUseConfiguredFiles();
            if (configuredFiles == null) { LastError = GameFiles.SetupInProgress ? GameFiles.SetupWorking : "Game files are not ready."; return; }
            if (!OfflineMatchPreflight.TryAdmit(plan, out string offlineReason, prepareRoom: true))
            { LastError = offlineReason; Console.WriteLine($"[launcher] {offlineReason}"); return; }
            // A standalone match window constructs its Scene immediately.
            // No save slot may be active during that constructor: Adventure
            // selects its slot on the owning SceneGameState below, and every
            // other mode must never inherit a previous story session's slot.
            Menu.SaveSlot = 0;
            RenderWindow.LogCreatingWindow();
            using var renderer = RenderWindow.Create();
            if (!Begin(renderer, settings, plan))
            {
                return;
            }
            renderer.Run();
            AfterMatch(PrepareAfterMatch(renderer.Scene.GameState));
        }

        /// <summary>
        /// Build the match into a window that already exists.
        ///
        /// This is what <c>MatchStart.Launch</c> was, with the two lines that
        /// owned a window taken out -- the same order, the same slot
        /// arithmetic, the same deference to what the server says it is
        /// running. Taking them out is what lets the launcher run inside the
        /// game window: a match is now a scene loaded into the window the
        /// front screen is being drawn in, and leaving one unloads the scene
        /// rather than destroying the window.
        ///
        /// False means nothing was loaded and there is nothing to run: no game
        /// files, a map that will not build, a demo that will not open. The
        /// caller puts the front screen back up.
        /// </summary>
        public static bool Begin(RenderWindow window, MenuSettings settings, LaunchPlan plan)
        {
            using var configuredFiles = GameFiles.TryUseConfiguredFiles();
            if (configuredFiles == null) { LastError = GameFiles.SetupInProgress ? GameFiles.SetupWorking : "Game files are not ready."; return false; }
            return BeginCore(window, settings, plan);
        }

        private static bool BeginCore(RenderWindow window, MenuSettings settings, LaunchPlan plan)
        {
            LastError = null;
            try { RetryPendingCommit(); }
            catch (Exception ex)
            {
                LastError = "The previous Adventure save is still pending: " + ex.Message;
                return false;
            }
            if (plan.Kind == LaunchKind.AimTrainer && NetSession.Active)
            { LastError = "Disconnect before starting training."; return false; }
            Chat.ChatBox.Clear();
            if (plan.Kind == LaunchKind.Online)
            {
                try { NetSession.RequireExactMapForLoad(); }
                catch (Exception ex) { LastError = ex.Message; return false; }
            }
            if (!GameFiles.Ready)
            {
                LastError = "Game files are not ready.";
                Console.WriteLine("[launcher] no game files; nothing to load");
                return false;
            }
            GameFiles.ApplyPaths();
            // Adventure and demo return before the ordinary room preflight.
            // Explicit custom availability must be admitted before either
            // route can create or replace a scene.
            if ((plan.MatchRules.RiftlineM7Available || plan.OfflineCharacter != OfflineCharacterMode.Native)
                && !OfflineMatchPreflight.TryAdmit(plan, out string customReason))
            { LastError = customReason; Console.WriteLine($"[launcher] {customReason}"); return false; }
            if (plan.Kind == LaunchKind.Adventure)
            {
                bool began = BeginAdventure(window, plan);
                if (began) { ClearRecentShipDock(); ClearRecentCampaignEnding(); }
                return began;
            }
            ClearRecentShipDock();
            ClearRecentCampaignEnding();
            if (plan.Kind == LaunchKind.Demo)
            {
                return BeginDemo(window, plan);
            }
            // No slot means nothing can be written, which is what a match
            // needs: leaving the story's slot selected would let a multiplayer
            // session's exit commit whatever it had done to the shared
            // StorySave over a real save file.
            Menu.SaveSlot = 0;
            if (plan.Kind == LaunchKind.Online || plan.Kind == LaunchKind.Host)
            {
                (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
                if (room != null)
                {
                    settings.RoomKey = room.Value.RoomKey;
                }
                else
                {
                    Console.WriteLine("[net] no map reported by the server; "
                        + "loading the selected map instead");
                }
            }

            string roomKey = plan.Kind is LaunchKind.Offline or LaunchKind.AimTrainer
                ? plan.RoomKey
                : settings.RoomKey;
            if (roomKey.Length == 0 || roomKey == "none")
            {
                return false;
            }

            // Measure the whole critical path, including custom-map preflight.
            // The previous timer started after GenerateMissing and understated
            // exactly the cold-load stalls the barrier needs to diagnose.
            double loadStarted = NetSession.Clock;
            NetSession.ReportMatchLoadProgress(MatchLoadStage.Preflight);

            // Join lobby prewarm before falling back to synchronous generation.
            // This prevents Start Match from compiling/reading the selected room
            // a second time while the background prewarm is already doing it.
            bool joinedPrewarm = Mods.RoomPrewarm.JoinForLoad(roomKey);
            if (!joinedPrewarm)
                MapGen.CustomRooms.GenerateMissing(roomKey);
            // A custom map that failed to build is still a room in the table --
            // the launcher lists it and the picker shows a frame for it -- and
            // loading one reaches for binaries that are not there. On Windows
            // that is a process with no console dying on a null path, which
            // says nothing to the player and nothing to anybody debugging it.
            string? unplayable = MapGen.CustomRooms.WhyUnplayable(roomKey);
            if (unplayable != null)
            {
                LastError = unplayable;
                Console.WriteLine($"[launcher] {unplayable}");
                return false;
            }

            if (!OfflineMatchPreflight.TryAdmit(plan, out string offlineReason))
            { LastError = offlineReason; Console.WriteLine($"[launcher] {offlineReason}"); return false; }
            NetSession.ReportMatchLoadProgress(MatchLoadStage.WorldBuild);
            EnsureScene(window);
            // The server's rotation decides the mode as well as the map; a
            // client that kept its own menu choice would score a different
            // game from everyone else on the same level. Settled before the
            // players are built, because who is on which team follows from
            // it -- this used to be read afterwards, so a client joining a
            // team server took its team decision from its own launcher.
            GameMode mode = plan.Mode;
            if (NetSession.Active && NetLaunch.ServerRoom() is { } serverRoom)
            {
                mode = serverRoom.Mode;
            }
            // GameState's own list, not the mode's name: Capture is a team
            // mode that does not end in "Teams", and testing the name left
            // every player and bot in a Capture match on no team at all.
            //
            // Online the match's own mode is the only thing that may answer
            // this. A local "team play" preference splitting an FFA server's
            // slots into two halves would give this client a scoreboard
            // nobody else on the server is playing to.
            bool teamPlay = NetSession.Active
                ? GameState.IsTeamMode(mode)
                : plan.Kind != LaunchKind.AimTrainer && (settings.TeamPlay == "on" || GameState.IsTeamMode(plan.Mode));

            if (NetSession.Active)
            {
                NetLaunch.DisableCheatsForMatch();
                NetLaunch.BuildPlayers(window.Scene, plan.Hunter,
                    localRecolor: LauncherPrefs.LastColor, teams: teamPlay);
            }
            else
            {
                plan.MatchRules.ApplyModifiers(window.Scene.GameState);
                AddLocalPlayers(window.Scene, plan, teamPlay);
                if (!OfflineCharacterGameplay.TryBind(window.Scene, plan, out string characterReason))
                {
                    LastError = characterReason;
                    window.EndScene();
                    Console.WriteLine($"[launcher] {LastError}");
                    return false;
                }
            }
            window.AddRoom(roomKey, mode, playerCount: NetSession.Active
                ? NetLaunch.RoomPlayerCount
                : 0);
            // AddRoom/Setup installs the retail mode defaults (Battle is 7
            // points / 7 minutes, for example). Offline rules from the launcher
            // must be layered on after that setup or those defaults silently win.
            // Network matches deliberately skip this: the server is their rule source.
            if (!NetSession.Active)
            {
                Mods.GameSettings.ApplyMatchRules(window.Scene.GameState);
                plan.MatchRules.ApplyModifiers(window.Scene.GameState);
            }
            NetSession.ReportMatchLoadProgress(MatchLoadStage.PresentationLoad);
            if (plan.Kind == LaunchKind.AimTrainer)
                Training.AimTrainerSession.Attach(window.Scene, plan);
            window.LoadScene();
            if (plan.Kind==LaunchKind.Offline && plan.MatchRules.RiftlineM7Available)
            {
                try { window.Scene.EnableOfflineRifleAvailability(true); }
                catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
                {
                    LastError = ex.Message;
                    window.EndScene();
                    Console.WriteLine($"[launcher] {LastError}");
                    return false;
                }
            }
            NetSession.ReportMatchLoadProgress(MatchLoadStage.SceneReady);
            // Keep the bounded one-room cache through the match so same-map
            // rematches reuse raw entity/node/collision bytes.
            double loadSeconds = NetSession.Clock - loadStarted;
            Console.WriteLine($"[launcher] loaded {roomKey} in {loadSeconds:0.00}s"
                + (joinedPrewarm ? " (joined prewarm)" : ""));
            if (plan.Spectate && NetSession.Active) Mods.SpectatorMode.Start();
            NetSession.MarkMatchLoaded();
            return true;
        }

        /// <summary>
        /// The scene a match is built into: the window's own if it came with
        /// one, a new one otherwise.
        ///
        /// Both exist. A window opened for one match builds its scene in the
        /// constructor as it always did; the shell's window opens with none
        /// and is given one per match.
        /// </summary>
        private static void EnsureScene(RenderWindow window)
        {
            if (!window.HasScene)
            {
                window.BeginScene();
            }
        }

        /// <summary>
        /// What has to happen after a match, whichever way it ended.        }

        /// <summary>
        /// What has to happen after a match, whichever way it ended.
        ///
        /// The line after the render loop, when a match was a window; the
        /// point the scene is unloaded at, now that it is not.
        /// </summary>
        public static void AfterMatch()
        {
            CleanupAfterMatch();
            // Unsupported active room transitions keep the legacy capture
            // point after teardown/cancellation, not a raced early snapshot.
            CommitAdventureSave();
        }

        internal static void AfterMatch(PostMatchCommitContext commit)
        {
            CleanupAfterMatch();
            CompleteCommit(commit);
        }

        private static void CleanupAfterMatch()
        {
            Chat.ChatBox.Clear();
            if (Mods.Network.DemoPlayback.IsActive)
            {
                DemoPlayback.Stop();
            }
        }

        internal static PostMatchCommitContext? PrepareBeforeTeardown(SceneGameState state)
            => state.InRoomTransition ? null : PrepareAfterMatch(state);

        internal static PostMatchCommitContext PrepareAfterMatch(SceneGameState state)
        {
            if (_pendingCommit is { } pending)
            {
                if (pending.RequestVersion == Menu.SaveRequestVersion) return pending;
                throw new InvalidOperationException("The previous Adventure save must commit before capturing a new request.");
            }
            return _pendingCommit = PostMatchCommitContext.Capture(state,
                Menu.SaveSlot, Menu.NeededSave, Menu.SaveRequestVersion);
        }

        internal static void RetryPendingCommit()
        {
            if (_pendingCommit is { } pending) CompleteCommit(pending);
        }

        private static void CompleteCommit(PostMatchCommitContext commit)
        {
            bool wroteAdventure = commit.HasPayload && commit.Slot != 0;
            commit.Commit();
            // Publish only after Commit succeeded: the view cannot retain
            // the prior Scene, and a failed save must remain retryable.
            if (commit.Departure == CampaignDeparture.Ending && commit.Ending is { } ending)
                RecentCampaignEnding = ending with { SaveCommitted = wroteAdventure };
            else if (commit.Slot != 0)
                RecentCampaignEnding = null;
            // Only the real QuitGame(enteringShip:true) path can produce a
            // star-map ticket, and only after the native save was committed.
            if (wroteAdventure && commit.Departure == CampaignDeparture.ReturnedToShip
                && CampaignNavigation.ByLandingRoom(commit.DepartureRoomId) is { } planet)
                RecentShipDock = new CampaignDockReceipt(commit.Slot, planet.Id);
            else if (commit.Slot != 0)
                RecentShipDock = null;
            // A new scene/slot may already have issued its own request. The old
            // save completion must not erase it, including same-value requests.
            if (Menu.SaveRequestVersion == commit.RequestVersion) Menu.NeededSave = SaveWhen.Never;
            commit.Release();
            if (ReferenceEquals(_pendingCommit, commit)) _pendingCommit = null;
        }

        /// <summary>
        /// The story, from a save slot.
        ///
        /// Nothing here is new engine work: adventure mode is what
        /// <see cref="GameMode.SinglePlayer"/> has always meant, and the room
        /// to open comes out of the slot's own checkpoint. The two things the
        /// launcher has to get right are choosing the slot before the save is
        /// read -- <see cref="GameState.CommitSave"/> writes nothing while
        /// <see cref="Menu.SaveSlot"/> is 0 -- and asking for one player, since
        /// the multiplayer path's bot filling has no meaning here.
        /// </summary>
        private static bool BeginAdventure(RenderWindow window, LaunchPlan plan)
        {
            string? landingRoom = null;
            CampaignDockReceipt? checkedDock = RecentShipDock;
            if (plan.ExitShipToSurface &&
                (plan.AdventureDestination is not { } exitDestination
                    || checkedDock is not { } currentDock || exitDestination != currentDock.Planet))
            {
                LastError = "Exit Ship is only available at the current docked planet.";
                return false;
            }
            if (plan.AdventureDestination is { } destination)
            {
                if (!CampaignShipTravel.TryChooseLanding(GameState.PeekSave(plan.SaveSlot),
                    checkedDock, plan.SaveSlot, plan.NewGame, destination,
                    out string room, out string reason))
                {
                    LastError = reason;
                    return false;
                }
                landingRoom = room;
            }
            // The shell has no scene while its menus are up. Do not prepare the
            // bootstrap GameState and then construct a different SceneGameState:
            // that discards StartNewSave(), and Scene.Reset may immediately read
            // the old slot again. It is especially bad for a stale/corrupt slot
            // that the menu correctly presents as empty: NEW RUN then tried to
            // deserialize the file the player was asking to replace.
            //
            // Clear the slot before Scene construction so Reset cannot read or
            // commit whichever Adventure slot a previous session left selected.
            Menu.SaveSlot = 0;
            EnsureScene(window);

            // Back to the four a DS game had: a previous offline match in
            // the same session may have raised this to eight, and the
            // story's own setup counts on the retail number.
            window.Scene.Players.MaxPlayers = 4;

            string roomKey = AdventureSave.Begin(
                window.Scene.GameState, plan.SaveSlot, plan.NewGame);
            // Travel is allowed only after real ship boarding and unlock
            // validation above. Initialize the *live* landing spawn without
            // rewriting the persisted prior checkpoint until normal save.
            if (landingRoom != null && checkedDock is { } dock
                && plan.AdventureDestination is { } landing)
            {
                CampaignShipTravel.PrepareLandingSpawn(window.Scene.GameState.StorySave, dock, landing);
                roomKey = landingRoom;
            }
            if (roomKey.Length == 0)
            {
                Console.WriteLine("[launcher] no adventure room to load");
                return false;
            }

            window.Scene.GameState.Mode = GameMode.SinglePlayer;
            window.AddPlayer(MphRead.Mods.Multiplayer.HunterRules.Resolve(plan.Hunter, plan.MatchRules.LowTier), recolor: LauncherPrefs.LastColor, team: -1);
            window.Scene.SuppressNextCampaignLandingMovie = plan.ExitShipToSurface;
            window.AddRoom(roomKey, GameMode.SinglePlayer);
            if (plan.NewGame && !Cheats.SkipPlanetIntros)
            {
                // The original new-file story movie precedes the landing
                // sequence. AddRoom's ordinary landing film is replaced by
                // one chained intro + landing with the same native renderer.
                window.Scene.StartMovies(Movie.StoryIntro, Movie.CALanding,
                    FadeType.FadeOutInBlack, 0, FadeType.FadeOutWhite,
                    5 / 30f, AfterMovie.StartGame);
            }
            window.LoadScene();
            return true;
        }

        /// <summary>
        /// Watch a recorded match. Joins the demo file the same way
        /// <see cref="NetLaunch.Join"/> joins a live server -- blocking until
        /// the first recorded packets say what room and mode were being
        /// played -- then loads that room exactly like a normal online
        /// match, so every mode-specific and room-specific setup runs
        /// unchanged. <see cref="Mods.SpectatorMode"/> is entered as soon as
        /// a player becomes available, since there is no local player to
        /// spawn as here.
        /// </summary>
        private static bool BeginDemo(RenderWindow window, LaunchPlan plan)
        {
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            if (!DemoPlayback.Join(plan.DemoPath))
            {
                LastError = DemoPlayback.LastError
                    ?? $"The replay could not be opened: {DemoPlayback.LastResult}.";
                Console.WriteLine("[demo] could not open replay: " + LastError);
                return false;
            }
            (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
            if (room == null)
            {
                LastError = "The replay does not contain match information.";
                Console.WriteLine("[demo] the demo has no match info");
                DemoPlayback.Stop();
                return false;
            }
            Menu.SaveSlot = 0;
            MapGen.CustomRooms.GenerateMissing(room.Value.RoomKey);
            string? unplayable = MapGen.CustomRooms.WhyUnplayable(room.Value.RoomKey);
            if (unplayable != null)
            {
                LastError = $"The replay map cannot be loaded: {unplayable}";
                Console.WriteLine($"[demo] {room.Value.RoomKey} is not playable on this installation");
                DemoPlayback.Stop();
                return false;
            }
            EnsureScene(window);
            NetLaunch.BuildPlayers(window.Scene, Hunter.Samus, localRecolor: 0,
                teams: GameState.IsTeamMode(room.Value.Mode), localSlot: -1);
            window.AddRoom(room.Value.RoomKey, room.Value.Mode,
                playerCount: NetLaunch.RoomPlayerCount);
            window.LoadScene();
            return true;
        }

        /// <summary>
        /// Write the save the session asked for.
        ///
        /// The game does not write its own save: reaching the ship sets
        /// <see cref="Menu.NeededSave"/> and something else is expected to act
        /// on it once the window has closed. That something was the console
        /// menu's loop, which a launcher-started session never returns
        /// through -- so the story ran, asked to be saved, and lost everything
        /// on exit. This is that step, on the path the launcher does take.
        ///
        /// A prompt is honoured as a yes. The front screens have no console to
        /// ask on, and the two settings that reach here already carry the
        /// answer that matters: leaving through the ship asks (so it saves),
        /// and quitting outright defaults to Never (so it does not).
        ///
        /// Public because Android has the same problem and must not grow a
        /// second answer to it: there the render thread calls this as it tears
        /// the scene down, which is the same moment as the line below the
        /// render loop here.
        /// </summary>
        public static void CommitAdventureSave()
        {
            CompleteCommit(PrepareAfterMatch(GameState.Current));
        }

        /// <summary>
        /// One human and however many bots were asked for.
        ///
        /// Bots are ordinary players whose Controls are written by PlayerAi,
        /// which is what Scene.AddPlayer already arranges for every player
        /// after the first -- so the only work here is choosing who they are.
        /// A different hunter each keeps a practice match from being a room
        /// full of one's own reflection, and alternating teams is what makes
        /// the Teams modes mean anything offline.
        /// </summary>
        // Shared ordinary local population owner; the initialized-Scene tests
        // exercise this same path without manufacturing bot actors afterwards.
        internal static void AddLocalPlayers(Scene scene, LaunchPlan plan,
            bool teamPlay)
        {
            if (plan.OfflineCharacter is OfflineCharacterMode.TrooperScoutDroneCombatPractice or OfflineCharacterMode.TrooperArsenalPractice) teamPlay = false;
            int bots = Math.Clamp(plan.Bots, 0, PlayerEntity.SlotCapacity - 1);
            // PlayerEntity.Create refuses every slot past MaxPlayers, and the
            // offline default is still the four a DS match could hold, so
            // asking for seven opponents would silently produce three. Set
            // rather than raise: the launcher comes back between matches now,
            // and a seven-bot match must not leave the next one at eight.
            scene.Players.MaxPlayers = Math.Max(4, bots + 1);
            // The player's own suit, and the first one for each bot: they are
            // each a different hunter (see below), so nobody collides and
            // there is nothing for PlayerColors to resolve offline.
            scene.AddPlayer(MphRead.Mods.Multiplayer.HunterRules.Resolve(plan.Hunter, plan.MatchRules.LowTier), recolor: LauncherPrefs.LastColor,
                team: teamPlay ? 0 : -1);
            for (int i = 1; i <= bots; i++)
            {
                var hunter = MphRead.Mods.Multiplayer.HunterRules.RandomAllowed((uint)((int)plan.Hunter + i), plan.MatchRules.LowTier);
                scene.AddPlayer(hunter, recolor: 0, team: teamPlay ? i % 2 : -1);
            }
            int level = Math.Clamp(plan.BotLevel, 0, 3);
            for (int i = 0; i < scene.Players.Items.Count; i++)
            {
                PlayerEntity player = scene.Players.Items[i];
                if (player.IsBot)
                {
                    player.BotLevel = level;
                }
            }
        }
    }
}
