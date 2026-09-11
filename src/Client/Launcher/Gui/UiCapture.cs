using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ProjectPrime.Server.Shared;
using MphRead.Entities;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Input;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// A fixed viewport used by launcher capture.  Keeping the name with the
    /// dimensions makes generated files self-describing and prevents a test
    /// from silently drifting to a platform-dependent default size.
    /// </summary>
    internal readonly record struct UiCaptureSize(string Name, int Width, int Height)
    {
        public Size AvaloniaSize => new Size(Width, Height);
    }

    /// <summary>
    /// A deterministic launcher state that can be built without an account,
    /// Node connection, game files, or a live renderer.  The factory is kept
    /// alongside the production capture command so headless tests exercise
    /// the same concrete controls as the developer screenshot command.
    /// </summary>
    internal sealed record UiCaptureFixtureDefinition(
        string Name,
        Func<MenuSettings, IReadOnlyList<string>, Control> Build);

    /// <summary>
    /// Screenshots of the front screen, without a screen.
    ///
    /// The launcher is the one part of this program that could not be looked
    /// at from here: the game renders through GL and can be read back
    /// (ScreenCapture), but the launcher is Avalonia, and checking a change to
    /// it meant opening a window on a machine with a display and looking. On a
    /// headless box, or over SSH, or in CI, there was no way to see what a
    /// layout change had actually done -- which is how a control that moves
    /// under the pointer ships.
    ///
    /// Avalonia can measure, arrange and draw a control into a bitmap with no
    /// window involved, which is all a screenshot of a layout needs. So
    /// `-uishot DIR` builds each screen at a fixed size, renders it, and
    /// writes a PNG.
    ///
    /// What this does *not* prove: that a real window manager gives the window
    /// the size asked for, that the fonts on another machine are these ones,
    /// or that anything is clickable. It proves the layout -- which is what
    /// every report about this screen has been about.
    /// </summary>
    internal static class UiCapture
    {
        /// <summary>The window size the launcher opens at (see HomeWindow).</summary>
        private static readonly Size _windowSize = new Size(940, 560);

        /// <summary>
        /// Required capture viewports from the P5 visual acceptance checklist.
        /// The 940x560 entry is deliberately explicit: it is the normal
        /// desktop startup viewport, not an approximation of 1280x720.
        /// </summary>
        private static readonly UiCaptureSize[] _requiredSizes =
        {
            new("1920x1080", 1920, 1080),
            new("2560x1440", 2560, 1440),
            new("1280x720", 1280, 720),
            new("940x560", 940, 560),
            new("900x1100", 900, 1100),
            new("560x800", 560, 800)
        };

        private static readonly PlayerId CapturePlayerId
            = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        private static readonly Guid CaptureSessionId
            = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid CaptureNodeId
            = Guid.Parse("99999999-9999-9999-9999-999999999999");

        /// <summary>
        /// P5 states that can currently be constructed truthfully by the
        /// offline capture seam.  A fixture is deliberately not advertised
        /// unless its name describes the state rendered by its factory.
        /// </summary>
        private static readonly UiCaptureFixtureDefinition[] _fixtures =
        {
            new("title-loading", (settings, rooms) => PrimeShellView.CreateTitleCapture(
                settings, rooms, new(PrimeTitleScreenPhase.Loading))),
            new("title-ready-keyboard", (settings, rooms) => PrimeShellView.CreateTitleCapture(
                settings, rooms, new(PrimeTitleScreenPhase.Ready,
                    PrimeInputDevice.KeyboardMouse))),
            new("title-ready-controller", (settings, rooms) => PrimeShellView.CreateTitleCapture(
                settings, rooms, new(PrimeTitleScreenPhase.Ready,
                    PrimeInputDevice.Gamepad, ControllerFamily.Xbox))),
            new("title-ready-touch", (settings, rooms) => PrimeShellView.CreateTitleCapture(
                settings, rooms, new(PrimeTitleScreenPhase.Ready,
                    PrimeInputDevice.Touch))),
            new("title-reduced-motion", (settings, rooms) => PrimeShellView.CreateTitleCapture(
                settings, rooms, new(PrimeTitleScreenPhase.Ready,
                    PrimeInputDevice.KeyboardMouse, ReducedMotion: true))),

            new("gateway-default", CreateGateway),
            new("gateway-login", CreateGatewayLogin),
            new("gateway-register", CreateGatewayRegister),
            new("gateway-confirm", CreateGatewayConfirm),
            new("gateway-confirm-clean", CreateGatewayConfirmClean),
            new("gateway-error", CreateGatewayError),
            new("gateway-guest", CreateGatewayGuest),

            new("play-home", CreatePlay),
            new("play-finding", CreatePlayFinding),
            new("play-empty", CreatePlayEmpty),
            new("play-browser", CreatePlayBrowser),
            new("play-browser-full", CreatePlayBrowserFull),
            new("play-network-error", CreatePlayNetworkError),
            new("play-advanced-network", CreatePlayAdvancedNetwork),
            new("maps-default", (_, _) => new MapsHubView(captureMode: true)),
            new("host-wide", CreateHostMatch),
            new("host-compact", CreateHostMatch),
            new("host-mobile", CreateHostMatch),

            new("lobby-owner-team", CreateLobby),
            new("lobby-owner-ffa", CreateLobbyOwnerFfa),
            new("lobby-ffa", CreateLobbyOwnerFfa),
            new("lobby-team-selector", CreateLobby),
            new("lobby-member-team", CreateLobbyMemberTeam),
            new("lobby-observer", CreateLobbyObserver),
            new("lobby-full", CreateLobbyFull),
            new("lobby-waitlist", CreateLobbyWaitlist),
            new("lobby-seat-offer", CreateLobbySeatOffer),
            new("lobby-chat", CreateLobbyChat),
            new("lobby-disconnected", CreateLobbyDisconnected),
            new("lobby-handoff-failure", CreateLobbyHandoffFailure),
            new("lobby-postmatch", CreateLobbyPostmatch),
            new("lobby-owner-ffa-wide", CreateLobbyOwnerFfa),
            new("lobby-owner-team-wide", CreateLobby),
            new("lobby-member-wide", CreateLobbyMemberTeam),
            new("lobby-observer-wide", CreateLobbyObserver),
            new("lobby-waitlist-wide", CreateLobbyWaitlist),
            new("lobby-chat-wide", CreateLobbyChat),
            new("lobby-mobile", CreateLobbyOwnerFfa),

            new("results-ffa", CreateResultsFfa),
            new("results-team", CreateResultsTeam),
            new("results-ballot", CreateResultsBallot),
            new("results-voted", CreateResultsVoted),
            new("results-resolved", CreateResultsResolved),
            new("results-no-authoritative-result", CreateResults),

            new("settings-gameplay", (settings, _) => CreateSettings(settings, "Gameplay")),
            new("settings-controls", (settings, _) => CreateSettings(settings, "Controls")),
            new("controls-gamepad", CreateControlsGamepad),
            new("controls-mobile", CreateControlsMobile),
            new("settings-graphics", (settings, _) => CreateSettings(settings, "Graphics")),
            new("settings-audio", (settings, _) => CreateSettings(settings, "Audio")),
            new("settings-system", (settings, _) => CreateSettings(settings, "System")),
            new("settings-network", (settings, _) => CreateSettings(settings, "Network")),
            new("settings-accessibility", (settings, _) => CreateSettings(settings, "Accessibility")),
            new("settings-about", (settings, _) => CreateSettings(settings, "About")),
            new("settings-pro-hud-off", CreateSettingsProHudOff),
            new("settings-pro-hud-on", CreateSettingsProHudOn),
            new("settings-radar-custom", CreateSettingsRadarCustom),
            new("settings-gyro-unsupported", CreateSettingsGyroUnsupported),
            new("settings-gyro-supported", CreateSettingsGyroSupported),
            new("settings-touch-buttons-off", CreateSettingsTouchButtonsOff),
            new("settings-touch-buttons-on", CreateSettingsTouchButtonsOn),
            new("settings-advanced-controller-collapsed", CreateSettingsAdvancedCollapsed),
            new("settings-advanced-controller-expanded", CreateSettingsAdvancedExpanded),

            new("hunter-overview", CreateHunterOverview),
            new("hunter-arsenal", CreateHunterArsenal),
            new("hunter-roster", CreateHunterRoster),
            new("hunter-career", CreateHunterCareer),
            new("hunter-matches", CreateHunterMatches),
            new("hunter-history", CreateHunterMatches),
            new("hunter-overview-simplified", CreateHunterOverview),
            new("hunter-empty-history", CreateHunterEmptyHistory),
            new("hunter-preview-failure", CreateHunterPreviewFailure),

            new("rankings-mobile", CreateRankingsMobile),
        };

        /// <summary>
        /// Requested P5 states that need controller, transport, platform, or
        /// private shell state injection which is not available in this
        /// capture-only surface.  Keeping these names visible makes the
        /// coverage gap reviewable without pretending that a default route
        /// is the requested state.
        /// </summary>
        private static readonly string[] _plannedButUnavailableFixtures = Array.Empty<string>();

        internal static IReadOnlyList<UiCaptureSize> RequiredSizes => _requiredSizes;

        internal static IReadOnlyList<UiCaptureFixtureDefinition> FixtureDefinitions => _fixtures;

        internal static IReadOnlyList<string> PlannedButUnavailableFixtures
            => _plannedButUnavailableFixtures;

        internal static UiCaptureFixtureDefinition GetFixture(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A capture fixture name is required.", nameof(name));
            foreach (UiCaptureFixtureDefinition fixture in _fixtures)
            {
                if (String.Equals(fixture.Name, name, StringComparison.OrdinalIgnoreCase))
                    return fixture;
            }
            throw new KeyNotFoundException($"Unknown UI capture fixture '{name}'.");
        }

        internal static Control BuildFixture(string name, MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(rooms);
            return GetFixture(name).Build(settings, rooms);
        }

        public static int Run(string directory)
        {
            if (!GuiLauncher.EnsureSetup())
            {
                Console.WriteLine("[uishot] no Avalonia backend on this machine; nothing captured");
                return 1;
            }
            // The normal Program setup path populates the path table after it
            // checks paths.txt. uishot intentionally runs before that check,
            // so initialize the same in-memory table here before the replay
            // picker asks for Paths.Export. This reads no game files and keeps
            // a fresh checkout a valid capture host.
            Paths.UpdatePaths();
            Directory.CreateDirectory(directory);
            // The front screen's Share button only exists where something can
            // receive a file, which today is Android alone -- so without a
            // stand-in the one corner this tool was made to check could never
            // be photographed as a phone draws it. Same reason as SampleReplays
            // below, and it is still only offered when real logs exist.
            Mods.LogShare.Current ??= new CaptureLogShare();
            int written = 0;
            // On the toolkit's own thread, and drained afterwards: the views
            // post work to the dispatcher as they are built (the front screen
            // focuses its first control that way), and a render before that
            // has run is a picture of a half-built screen.
            Dispatcher.UIThread.Invoke(() =>
            {
                var settings = new MenuSettings();
                List<string> rooms = RoomList();
                foreach ((string name, Control view, Size size) in Screens(settings, rooms))
                {
                    string path = Path.Combine(directory, $"{name}.png");
                    if (Capture(view, path, size))
                    {
                        written++;
                        Console.WriteLine($"[uishot] {path}");
                    }
                }
            });
            Console.WriteLine($"[uishot] {written} screen(s) written to {directory}");
            return written > 0 ? 0 : 1;
        }

        private static List<string> RoomList()
        {
            var rooms = new List<string>();
            try
            {
                foreach (RoomMetadata meta in Metadata.RoomMetadata.Values)
                {
                    if (meta.Multiplayer)
                    {
                        rooms.Add(meta.Name);
                    }
                }
            }
            catch (Exception)
            {
                // No game files here. The screens still lay out; the map rows
                // are simply empty, which is itself worth being able to see.
            }
            rooms.Sort(StringComparer.OrdinalIgnoreCase);
            return rooms;
        }

        private static IEnumerable<(string, Control, Size)> Screens(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            yield return ("home", new HomeView(settings, rooms), _windowSize);
            if (rooms.Count > 0)
            {
                yield return ("mappicker", new MapPickerView(rooms, rooms[0]), _windowSize);
            }
            // Both halves of it: the list a machine that has recorded
            // something gets, and the line a machine that has not gets --
            // which is the one carrying the folder's path and the only place
            // that path is ever written down.
            yield return ("replaypicker", new ReplayPickerView(SampleReplays(),
                Network.ReplayLibrary.Directory), _windowSize);
            yield return ("replaypicker-empty", new ReplayPickerView(
                Array.Empty<Network.ReplayRecording>(), Network.ReplayLibrary.Directory),
                _windowSize);
            yield return ("pausemenu", new PauseMenuView(offerWindowMode: true), _windowSize);
            // Deliberately shorter than the menu's own content, and shorter
            // than the game window is now allowed to be. The pause menu is
            // laid over the game window, so its host is whatever size the
            // player dragged that to, and entries drawn off the bottom edge
            // are a player who cannot leave the match. This is the check that
            // the scroll view carries them.
            yield return ("pausemenu-small", new PauseMenuView(offerWindowMode: true),
                new Size(560, 320));

            // Keep the two non-P5 routes that the developer capture command
            // has historically emitted.  Gateway, Play, Hunter, Settings,
            // and the lobby are represented by the truthful named fixtures
            // below, so they are not emitted a second time under opaque route
            // aliases.
            foreach (PrimeRoute route in new[] { PrimeRoute.Theatre, PrimeRoute.Rankings })
            {
                foreach (UiCaptureSize captureSize in _requiredSizes)
                {
                    yield return ($"prime-{route.ToString().ToLowerInvariant()}-{captureSize.Name}",
                        PrimeShellView.CreateCapture(settings, rooms, route),
                        captureSize.AvaloniaSize);
                }
            }

            // The named matrix is the only source for route/settings fixture
            // captures.  Each generated image therefore has a unique state
            // name and one of the six required viewports, without retaining
            // the old duplicate route matrix.
            foreach (UiCaptureFixtureDefinition fixture in _fixtures)
            {
                foreach (UiCaptureSize captureSize in _requiredSizes)
                {
                    yield return ($"{fixture.Name}-{captureSize.Name}",
                        fixture.Build(settings, rooms), captureSize.AvaloniaSize);
                }
            }
        }

        /// <summary>
        /// Somewhere for the Share button to point while it is being
        /// photographed. Nothing is built and nothing is sent: a capture has
        /// nobody to press it.
        /// </summary>
        private sealed class CaptureLogShare : Mods.ILogShare
        {
            public string StagingPath(string fileName) =>
                Path.Combine(Path.GetTempPath(), fileName);

            public bool Share(string path, string subject, out string error)
            {
                error = "there is nothing to share to on this platform";
                return false;
            }
        }

        /// <summary>
        /// Recordings that are not there, so the list can be seen on a machine
        /// that has never recorded one.
        /// </summary>
        private static IReadOnlyList<Network.ReplayRecording> SampleReplays()
        {
            var now = new DateTime(2026, 9, 4, 18, 22, 7);
            return new[]
            {
                new Network.ReplayRecording("MP3 PROVING GROUND_2026-09-04_18-22-07.fpreplay",
                    "MP3 PROVING GROUND", now, 1_512_320),
                new Network.ReplayRecording("COMBAT HALL_2026-09-02_21-04-55.fpreplay",
                    "COMBAT HALL", now.AddDays(-2), 402_112),
                new Network.ReplayRecording("sent-to-me.fpreplay", "", now.AddDays(-9), 88_400)
            };
        }

        private static Control CreateGateway(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway);

        private static Control CreateGatewayLogin(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway,
                new PrimeShellCaptureState(
                    Gateway: new GatewayState(GatewayPhase.SigningIn,
                        "Signing in to the Project Prime Backend…", false, false,
                        false, false, null, "Guest")));

        private static Control CreateGatewayRegister(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway,
                new PrimeShellCaptureState(
                    Gateway: new GatewayState(GatewayPhase.Registering,
                        "Creating a new Project Prime account…", false, false,
                        false, false, null, "Guest")));

        private static Control CreateGatewayConfirm(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            PlayerId player = CapturePlayerId;
            return PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway,
                new PrimeShellCaptureState(
                    Gateway: new GatewayState(GatewayPhase.Confirming,
                        "Enter the confirmation code delivered to your email.",
                        false, false, false, false, player, "Guest"),
                    PendingRegistration: new PendingRegistration(player,
                        "pilot@example.com")));
        }

        private static Control CreateGatewayConfirmClean(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway,
                new PrimeShellCaptureState(
                    Gateway: new GatewayState(GatewayPhase.Confirming,
                        "Enter the confirmation code delivered to your email.",
                        false, false, false, false, null, "Guest"),
                    PendingRegistration: new PendingRegistration(CapturePlayerId,
                        "pilot@example.com")));

        private static Control CreateGatewayError(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway,
                new PrimeShellCaptureState(
                    Gateway: new GatewayState(GatewayPhase.Failed,
                        "The capture Backend is unavailable; no request was sent.",
                        false, false, false, false, null, "Guest")));

        private static Control CreateGatewayGuest(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Gateway,
                new PrimeShellCaptureState(
                    Gateway: new GatewayState(GatewayPhase.Guest,
                        "Guest access selected. Career progress requires an account.",
                        false, true, false, false, null, "Capture Guest"),
                    Identity: PrimeShellCaptureIdentity.Guest));

        private static Control CreatePlay(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Play);

        private static Control CreatePlayFinding(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreatePlayCapture(settings, rooms, new PrimeShellCaptureState(
                Play: new PlayState(PlayPhase.Connected, CaptureNodes(),
                    new NodeControlClient.ViewState(Session: CaptureSession()),
                    Hunter.Samus, "Finding a match…", Loading: true, Revision: 1),
                Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreatePlayEmpty(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreatePlayCapture(settings, rooms, new PrimeShellCaptureState(
                Play: new PlayState(PlayPhase.Nodes, Array.Empty<NodeListing>(), null,
                    Hunter.Samus, "No compatible servers are online.", Loading: false,
                    Revision: 0),
                Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreatePlayBrowser(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreatePlayCapture(settings, rooms, new PrimeShellCaptureState(
                Play: BrowserPlayState(full: false),
                PlaySubsection: PlaySubsection.Browser,
                Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreatePlayBrowserFull(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreatePlayCapture(settings, rooms, new PrimeShellCaptureState(
                Play: BrowserPlayState(full: true),
                PlaySubsection: PlaySubsection.Browser,
                Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreatePlayNetworkError(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreatePlayCapture(settings, rooms, new PrimeShellCaptureState(
                Play: new PlayState(PlayPhase.Error, Array.Empty<NodeListing>(),
                    new NodeControlClient.ViewState(Session: CaptureSession(),
                        Error: "The Node directory request failed during capture."),
                    Hunter.Samus, "The Node directory request failed during capture.",
                    Loading: false, Revision: 2),
                Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreatePlayAdvancedNetwork(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreatePlayCapture(settings, rooms, new PrimeShellCaptureState(
                Play: new PlayState(PlayPhase.Connected, CaptureNodes(),
                    new NodeControlClient.ViewState(Session: CaptureSession()),
                    Hunter.Samus, "", Loading: false, Revision: 1),
                ExpandAdvancedNetwork: true,
                Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreateHostMatch(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateLobbyCapture(settings, rooms,
                new PrimeShellCaptureState(
                    Play: new PlayState(PlayPhase.Connected, CaptureNodes(),
                        new NodeControlClient.ViewState(Session: CaptureSession()),
                        Hunter.Samus, "", Loading: false, Revision: 1),
                    PlaySubsection: PlaySubsection.HostMatch,
                    Identity: PrimeShellCaptureIdentity.SignedIn));

        private static Control CreatePlayCapture(MenuSettings settings,
            IReadOnlyList<string> rooms, PrimeShellCaptureState captureState)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Play, captureState);

        private static NodeSessionSnapshot CaptureSession()
            => new(CaptureSessionId, CapturePlayerId.Value, "Capture Preview",
                CaptureNodeId, new string('A', 43));

        private static NodeListing[] CaptureNodes()
            =>
            [
                new(CaptureNodeId, "SOL-77", "US-East",
                    "https://node.capture.invalid/control", 1, "capture-build",
                    "capture-content", 64, 12, 3, 1, "verified",
                    new DateTimeOffset(2026, 9, 4, 18, 22, 7, TimeSpan.Zero),
                    new[] { "MP3 PROVING GROUND", "COMBAT HALL" }),
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "LUNA-12",
                    "EU-West", "https://luna.capture.invalid/control", 1,
                    "capture-build", "capture-content", 48, 7, 2, 0, "verified",
                    new DateTimeOffset(2026, 9, 4, 18, 21, 33, TimeSpan.Zero),
                    new[] { "MP3 PROVING GROUND" })
            ];

        private static PlayState BrowserPlayState(bool full)
        {
            ImmutableArray<LobbyListEntry> entries = full
                ? ImmutableArray.Create(
                    new LobbyListEntry(
                        Guid.Parse("a1000000-0000-4000-8000-000000000001"),
                        "Full Team Queue", LobbyPhase.Open, 8, 8, 2, 12,
                        WaitlistCount: 4, ObserverLimit: 16, BotCount: 0,
                        MapKey: "MP3 PROVING GROUND", Mode: MatchMode.TeamBattle,
                        TimeLimitSeconds: 600, PointGoal: 12),
                    new LobbyListEntry(
                        Guid.Parse("a1000000-0000-4000-8000-000000000002"),
                        "Full Duel", LobbyPhase.Open, 4, 4, 0, 8,
                        WaitlistCount: 1, ObserverLimit: 0, BotCount: 0,
                        MapKey: "COMBAT HALL", Mode: MatchMode.Battle,
                        TimeLimitSeconds: 300, PointGoal: 10))
                : ImmutableArray.Create(
                    new LobbyListEntry(
                        Guid.Parse("a2000000-0000-4000-8000-000000000001"),
                        "Alinos Skirmish", LobbyPhase.Open, 3, 8, 1, 21,
                        WaitlistCount: 0, ObserverLimit: 16, BotCount: 0,
                        MapKey: "MP3 PROVING GROUND", Mode: MatchMode.Battle,
                        TimeLimitSeconds: 600, PointGoal: 12),
                    new LobbyListEntry(
                        Guid.Parse("a2000000-0000-4000-8000-000000000002"),
                        "Team Training", LobbyPhase.Open, 6, 8, 0, 18,
                        WaitlistCount: 0, ObserverLimit: 8, BotCount: 1,
                        MapKey: "COMBAT HALL", Mode: MatchMode.TeamBattle,
                        TimeLimitSeconds: 480, PointGoal: 12));
            var snapshot = new LobbyListSnapshot(entries, null);
            return new PlayState(PlayPhase.Connected, CaptureNodes(),
                new NodeControlClient.ViewState(Session: CaptureSession(), Lobbies: snapshot),
                Hunter.Samus, "", Loading: false, Revision: 3)
            {
                BrowsedLobbies = snapshot
            };
        }

        private static Control CreateRankingsMobile(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Rankings,
                new PrimeShellCaptureState(Rankings: CaptureRankings(),
                    Identity: PrimeShellCaptureIdentity.SignedIn));

        private static RankingsState CaptureRankings()
        {
            ImmutableArray<PrimeLeaderboardRow> rows = ImmutableArray.Create(
                new PrimeLeaderboardRow(new LeaderboardEntry(
                    new PlayerId(OtherPlayer(1)), "Lastraven", Kills: 103,
                    Deaths: 48, Wins: 15, Matches: 22, AttributedMatches: 22,
                    Score: 515m, Points: 515, Tier: 4, Title: "Frontier Veteran"),
                    IsCurrentPlayer: false),
                new PrimeLeaderboardRow(new LeaderboardEntry(CapturePlayerId,
                    "Capture Preview", Kills: 96, Deaths: 54, Wins: 12, Matches: 18,
                    AttributedMatches: 18, Score: 420m, Points: 420, Tier: 4,
                    Title: "Frontier Veteran"), IsCurrentPlayer: true),
                new PrimeLeaderboardRow(new LeaderboardEntry(
                    new PlayerId(OtherPlayer(2)), "Vuum", Kills: 88, Deaths: 61,
                    Wins: 10, Matches: 19, AttributedMatches: 19, Score: 390m,
                    Points: 390, Tier: 3, Title: "Elite Hunter"), IsCurrentPlayer: false));
            return new RankingsState("rp", null, rows, NextCursor: null,
                Loading: false, Error: null);
        }

        private static Control CreateLobby(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => PrimeShellView.CreateLobbyCapture(settings, rooms);

        private static Control CreateLobbyOwnerFfa(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.OwnerFfa);

        private static Control CreateLobbyMemberTeam(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.MemberTeam);

        private static Control CreateLobbyObserver(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.Observer);

        private static Control CreateLobbyFull(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.Full);

        private static Control CreateLobbyWaitlist(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.Waitlist);

        private static Control CreateLobbySeatOffer(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.SeatOffer);

        private static Control CreateLobbyChat(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.Chat);

        private static Control CreateLobbyDisconnected(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.Disconnected);

        private static Control CreateLobbyHandoffFailure(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.HandoffFailure);

        private static Control CreateLobbyPostmatch(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateLobbyVariant(settings, rooms, CaptureLobbyVariant.Postmatch);

        private enum CaptureLobbyVariant
        {
            OwnerFfa,
            MemberTeam,
            Observer,
            Full,
            Waitlist,
            SeatOffer,
            Chat,
            Disconnected,
            HandoffFailure,
            Postmatch
        }

        private static Control CreateLobbyVariant(MenuSettings settings,
            IReadOnlyList<string> rooms, CaptureLobbyVariant variant)
            => PrimeShellView.CreateLobbyCapture(settings, rooms,
                CreateLobbyCaptureState(rooms, variant));

        private static PrimeShellCaptureState CreateLobbyCaptureState(
            IReadOnlyList<string> rooms, CaptureLobbyVariant variant)
        {
            string mapKey = rooms.FirstOrDefault() ?? "MP3 PROVING GROUND";
            LobbySnapshot lobby;
            NodeControlClient.ViewState node;
            string message = "";

            switch (variant)
            {
                case CaptureLobbyVariant.OwnerFfa:
                    lobby = MakeLobby("FFA Owner Room", LobbyPhase.Open,
                        owner: CaptureSessionId, currentMatchId: null,
                        playerLimit: 8, observerLimit: 16,
                        members: MakePlayers(3, CaptureSessionId, owner: true,
                            teamMode: false), mapKey: mapKey, mode: MatchMode.Battle);
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.MemberTeam:
                    lobby = MakeLobby("Team Member Room", LobbyPhase.Open,
                        owner: OtherSession(1), currentMatchId: null,
                        playerLimit: 8, observerLimit: 16,
                        members: MakePlayers(4, CaptureSessionId, owner: false,
                            teamMode: true), mapKey: mapKey, mode: MatchMode.TeamBattle);
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.Observer:
                    lobby = MakeLobby("Observer Room", LobbyPhase.Open,
                        owner: OtherSession(1), currentMatchId: null,
                        playerLimit: 4, observerLimit: 8,
                        members: MakeObserverLobbyMembers(), mapKey: mapKey,
                        mode: MatchMode.TeamBattle);
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.Full:
                    lobby = MakeLobby("Full Player Room", LobbyPhase.Open,
                        owner: CaptureSessionId, currentMatchId: null,
                        playerLimit: 8, observerLimit: 16,
                        members: MakePlayers(8, CaptureSessionId, owner: true,
                            teamMode: true), mapKey: mapKey, mode: MatchMode.TeamBattle);
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.Waitlist:
                    lobby = MakeLobby("Waitlist Room", LobbyPhase.Open,
                        owner: OtherSession(1), currentMatchId: null,
                        playerLimit: 2, observerLimit: 8,
                        members: MakePlayers(2, CaptureSessionId, owner: false,
                            teamMode: false, includeCurrent: false), mapKey: mapKey,
                        mode: MatchMode.Battle,
                        waitlist: MakeWaitlist(LobbyQueueEntryState.Queued));
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.SeatOffer:
                    lobby = MakeLobby("Seat Offer Room", LobbyPhase.Open,
                        owner: OtherSession(1), currentMatchId: null,
                        playerLimit: 2, observerLimit: 8,
                        members: MakePlayers(2, CaptureSessionId, owner: false,
                            teamMode: false, includeCurrent: false), mapKey: mapKey,
                        mode: MatchMode.Battle,
                        waitlist: MakeWaitlist(LobbyQueueEntryState.SeatOffered));
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.Chat:
                    lobby = MakeLobby("Chat Practice Room", LobbyPhase.Open,
                        owner: CaptureSessionId, currentMatchId: null,
                        playerLimit: 8, observerLimit: 16,
                        members: MakePlayers(3, CaptureSessionId, owner: true,
                            teamMode: true), mapKey: mapKey, mode: MatchMode.TeamBattle,
                        chat: MakeChat());
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby);
                    break;
                case CaptureLobbyVariant.Disconnected:
                    lobby = MakeLobby("Disconnected Room", LobbyPhase.Open,
                        owner: CaptureSessionId, currentMatchId: null,
                        playerLimit: 8, observerLimit: 16,
                        members: MakePlayers(3, CaptureSessionId, owner: true,
                            teamMode: true), mapKey: mapKey, mode: MatchMode.TeamBattle);
                    message = "Node connection lost while this lobby was open.";
                    node = new NodeControlClient.ViewState(Lobby: lobby, Error: message);
                    break;
                case CaptureLobbyVariant.HandoffFailure:
                    lobby = MakeLobby("Worker Recovery Room", LobbyPhase.InMatch,
                        owner: CaptureSessionId,
                        currentMatchId: Guid.Parse("b1000000-0000-4000-8000-000000000001"),
                        playerLimit: 4, observerLimit: 8,
                        members: MakePlayers(4, CaptureSessionId, owner: true,
                            teamMode: true), mapKey: mapKey, mode: MatchMode.TeamBattle);
                    message = "Worker handoff failed; retry the gameplay connection.";
                    node = new NodeControlClient.ViewState(
                        Lobby: lobby,
                        Handoff: MakeHandoff(lobby.CurrentMatchId!.Value),
                        Error: message);
                    break;
                case CaptureLobbyVariant.Postmatch:
                    lobby = MakeLobby("Post-match Room", LobbyPhase.PostMatch,
                        owner: CaptureSessionId,
                        currentMatchId: Guid.Parse("b2000000-0000-4000-8000-000000000001"),
                        playerLimit: 8, observerLimit: 16,
                        members: MakePlayers(3, CaptureSessionId, owner: true,
                            teamMode: true), mapKey: mapKey, mode: MatchMode.TeamBattle);
                    node = new NodeControlClient.ViewState(Session: CaptureSession(), Lobby: lobby,
                        Round: MakeLobbyRound(lobby));
                    message = "The match is complete; the Node is preparing the next round.";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(variant));
            }

            PlayPhase phase = variant == CaptureLobbyVariant.HandoffFailure
                ? PlayPhase.Handoff : PlayPhase.Lobby;
            var play = new PlayState(phase, CaptureNodes(), node, Hunter.Samus,
                message.Length == 0 ? lobby.Name : message, Loading: false,
                Revision: lobby.Revision);
            return new PrimeShellCaptureState(Play: play,
                Identity: PrimeShellCaptureIdentity.SignedIn);
        }

        private static LobbySnapshot MakeLobby(string name, LobbyPhase phase,
            Guid owner, Guid? currentMatchId, int playerLimit, int observerLimit,
            ImmutableArray<LobbyMember> members, string mapKey, MatchMode mode,
            ImmutableArray<LobbyChatEntry>? chat = null,
            LobbyWaitlistSnapshot? waitlist = null)
            => new(CaptureLobbyId(name), name, LobbyVisibility.Public, owner, phase, 9,
                playerLimit, observerLimit, members,
                chat ?? ImmutableArray<LobbyChatEntry>.Empty, mapKey, mode,
                currentMatchId, BotCount: 0, TimeLimitSeconds: 600, PointGoal: 12,
                Waitlist: waitlist);

        private static Guid CaptureLobbyId(string name)
            => name switch
            {
                "FFA Owner Room" => Guid.Parse("b0000000-0000-4000-8000-000000000001"),
                "Team Member Room" => Guid.Parse("b0000000-0000-4000-8000-000000000002"),
                "Observer Room" => Guid.Parse("b0000000-0000-4000-8000-000000000003"),
                "Full Player Room" => Guid.Parse("b0000000-0000-4000-8000-000000000004"),
                "Waitlist Room" => Guid.Parse("b0000000-0000-4000-8000-000000000005"),
                "Seat Offer Room" => Guid.Parse("b0000000-0000-4000-8000-000000000006"),
                "Chat Practice Room" => Guid.Parse("b0000000-0000-4000-8000-000000000007"),
                "Disconnected Room" => Guid.Parse("b0000000-0000-4000-8000-000000000008"),
                "Worker Recovery Room" => Guid.Parse("b0000000-0000-4000-8000-000000000009"),
                "Post-match Room" => Guid.Parse("b0000000-0000-4000-8000-00000000000a"),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };

        private static ImmutableArray<LobbyMember> MakePlayers(int count,
            Guid currentSession, bool owner, bool teamMode,
            bool includeCurrent = true)
        {
            var members = ImmutableArray.CreateBuilder<LobbyMember>(count);
            int index = 0;
            if (owner)
            {
                members.Add(CaptureMember(currentSession, CapturePlayerId.Value,
                    "Capture Preview", index, teamMode, ready: true));
                index++;
            }
            else
            {
                members.Add(CaptureMember(OtherSession(1), OtherPlayer(1),
                    "Room Owner", index++, teamMode, ready: true));
            }
            int targetOtherMembers = includeCurrent && !owner ? count - 1 : count;
            while (members.Count < targetOtherMembers)
            {
                int otherIndex = members.Count + 1;
                members.Add(CaptureMember(OtherSession(otherIndex),
                    OtherPlayer(otherIndex), $"Pilot-{otherIndex:00}",
                    index++, teamMode, ready: true));
            }
            if (includeCurrent && !owner)
            {
                members.Add(CaptureMember(currentSession, CapturePlayerId.Value,
                    "Capture Preview", index, teamMode, ready: false));
            }
            return members.ToImmutable();
        }

        private static LobbyMember CaptureMember(Guid session, Guid player,
            string name, int index, bool teamMode, bool ready)
            => new(session, player, name, (Hunter)(index % 7),
                teamMode ? (byte)(index % 2) : (byte)0, ready, Observer: false);

        private static ImmutableArray<LobbyMember> MakeObserverLobbyMembers()
            => ImmutableArray.Create(
                new LobbyMember(OtherSession(1), OtherPlayer(1), "Room Owner",
                    Hunter.Samus, 0, Ready: true, Observer: false),
                new LobbyMember(CaptureSessionId, CapturePlayerId.Value,
                    "Capture Preview", Hunter.Trace, 1, Ready: false, Observer: true),
                new LobbyMember(OtherSession(2), OtherPlayer(2), "Observer-02",
                    Hunter.Sylux, 0, Ready: false, Observer: true));

        private static LobbyWaitlistSnapshot MakeWaitlist(LobbyQueueEntryState state)
        {
            Guid? offerId = state == LobbyQueueEntryState.SeatOffered
                ? Guid.Parse("c1000000-0000-4000-8000-000000000001") : null;
            var entries = ImmutableArray.Create(
                new LobbyQueueEntrySummary(1, "Capture Preview", state, 101),
                new LobbyQueueEntrySummary(2, "Queued-Pilot", LobbyQueueEntryState.Queued, 102));
            return new LobbyWaitlistSnapshot(2, entries, IsSelfQueued: true,
                SelfState: state, SelfQueueSequence: 101,
                SelfOffer: offerId is { } id
                    ? new LobbyQueueOffer(id,
                        DateTimeOffset.UtcNow.AddSeconds(12),
                        LobbySeatPolicy.ImmediateSeat)
                    : null);
        }

        private static ImmutableArray<LobbyChatEntry> MakeChat()
            => ImmutableArray.Create(
                new LobbyChatEntry(1, OtherSession(1), "Pilot-01",
                    "Welcome to the proving ground."),
                new LobbyChatEntry(2, CaptureSessionId, "Capture Preview",
                    "Ready when the squad is set."),
                new LobbyChatEntry(3, OtherSession(2), "Pilot-02",
                    "Loading the team rules now."));

        private static NodeMatchHandoff MakeHandoff(Guid matchId)
            => new(matchId, 0x50560001, "127.0.0.1", 4111,
                "capture-worker-ticket", 0x50560001UL, Observer: false,
                Hunter.Samus);

        private static NodeRoundSnapshot MakeLobbyRound(LobbySnapshot lobby)
        {
            var round = new NodeRoundSnapshot(lobby,
                Guid.Parse("d1000000-0000-4000-8000-000000000001"),
                Guid.Parse("d1000000-0000-4000-8000-000000000002"),
                Paused: false, TournamentEnded: false, ConfigurationRevision: 1,
                BallotRevision: 2,
                VoteDeadline: DateTimeOffset.UtcNow.AddSeconds(25),
                Options: ImmutableArray.Create(
                    new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, lobby.MapKey,
                        lobby.Mode, 3),
                    new LobbyVoteEntry(2, LobbyVoteChoice.ReturnToLobby, lobby.MapKey,
                        lobby.Mode, 1)), OwnVote: 1);
            NodeControlCodec.ValidateEventPayload(round);
            return round;
        }

        private static Guid OtherSession(int index)
            => new Guid($"{index + 3:00000000}-3333-4333-8333-333333333333");

        private static Guid OtherPlayer(int index)
            => new Guid($"{index + 16:00000000}-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

        private static Control CreateResults(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => new PostMatchView(results: null, localSlot: -1);

        private static Control CreateResultsFfa(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => new PostMatchView(CreateResultsSnapshot(GameMode.Battle), localSlot: 0);

        private static Control CreateResultsTeam(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => new PostMatchView(CreateResultsSnapshot(GameMode.BattleTeams), localSlot: 0);

        private static Control CreateResultsBallot(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            var view = new PostMatchView(CreateResultsSnapshot(GameMode.Battle), localSlot: 0);
            view.Update(CreateRound(ownVote: 0));
            return view;
        }

        private static Control CreateResultsVoted(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            var view = new PostMatchView(CreateResultsSnapshot(GameMode.Battle), localSlot: 0);
            view.Update(CreateRound(ownVote: 2));
            return view;
        }

        private static Control CreateResultsResolved(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            var view = new PostMatchView(CreateResultsSnapshot(GameMode.Battle), localSlot: 0);
            ImmutableArray<LobbyVoteEntry> options = BallotOptions();
            view.Update(CreateRound(ownVote: 0, resolvedOption: options[1]));
            return view;
        }

        private static MatchResultsSnapshot CreateResultsSnapshot(GameMode mode)
        {
            using Scene scene = Scene.CreateHeadless();
            scene.Services = new CaptureReplicaServices();
            MatchRuntime match = scene.Match;
            match.MatchId = 0x50560001;
            match.ApplyRules(MatchRules.CreateDefault(mode.ToMatchMode(),
                mode.IsTeamMode() ? "CAPTURE TEAM ARENA" : "CAPTURE FFA ARENA"));

            bool teams = mode.IsTeamMode();
            int playerCount = teams ? 4 : 3;
            for (int slot = 0; slot < playerCount; slot++)
            {
                // MatchPlayers creates all fixed slots when a headless Scene is
                // constructed. Marking those slots directly avoids PrepareSlot's
                // model/asset loading while preserving the same active-player,
                // ranking, and result-slot path used by the match logic.
                PlayerEntity player = scene.Players[slot];
                player.LoadFlags = LoadFlags.SlotActive | LoadFlags.Initial | LoadFlags.Active;
                player.TeamIndex = teams ? slot % 2 : -1;
            }
            scene.Players.ActiveCount = playerCount;

            int[] points = teams ? new[] { 12, 4, 9, 3 } : new[] { 12, 8, 3 };
            int[] kills = teams ? new[] { 6, 2, 4, 1 } : new[] { 6, 4, 1 };
            int[] deaths = teams ? new[] { 1, 4, 2, 5 } : new[] { 1, 2, 4 };
            for (int slot = 0; slot < playerCount; slot++)
            {
                PlayerMatchStats stats = match.Players[slot];
                stats.Points = points[slot];
                stats.Kills = kills[slot];
                stats.Deaths = deaths[slot];
                stats.Assists = slot + 1;
                stats.DamageDealt = 100 + slot * 37;
                stats.HeadshotKills = slot;
                stats.LongestKillStreak = Math.Max(1, kills[slot]);
            }
            match.Logic.UpdateState();
            match.Phase = MatchPhase.Ending;

            var identities = new PlayerResultIdentity[PlayerEntity.SlotCapacity];
            for (int slot = 0; slot < identities.Length; slot++)
            {
                identities[slot] = new PlayerResultIdentity((Hunter)slot,
                    teams ? slot % 2 : -1, slot < playerCount,
                    teams ? $"Team {slot % 2 + 1} · Player {slot + 1}" : $"Hunter {slot + 1}",
                    IsBot: slot > 0);
            }
            match.CaptureReplicatedResult(123.0f, MatchEndReason.ScoreGoal, identities);
            return new MatchResultsSnapshot(
                teams ? "CAPTURE TEAM ARENA" : "CAPTURE FFA ARENA", mode, match.Result!);
        }

        private static NodeRoundSnapshot CreateRound(byte ownVote,
            LobbyVoteEntry? resolvedOption = null)
        {
            var round = new NodeRoundSnapshot(CreateResultsRoundLobby(),
                Guid.Parse("50560000-0000-4000-8000-000000000001"),
                Guid.Parse("50560000-0000-4000-8000-000000000002"),
                Paused: false, TournamentEnded: false, ConfigurationRevision: 1,
                BallotRevision: 7,
                VoteDeadline: DateTimeOffset.UtcNow.AddSeconds(25),
                Options: BallotOptions(), OwnVote: ownVote, ResolvedOption: resolvedOption);
            // Keep the offline fixture on the same contract path as a Node
            // event. This catches a missing lobby or ballot field at fixture
            // construction time rather than allowing a visually plausible,
            // wire-invalid round to ship.
            NodeControlCodec.ValidateEventPayload(round);
            return round;
        }

        private static LobbySnapshot CreateResultsRoundLobby()
            => MakeLobby("Post-match Room", LobbyPhase.PostMatch,
                owner: CaptureSessionId,
                currentMatchId: Guid.Parse("b2000000-0000-4000-8000-000000000001"),
                playerLimit: 8, observerLimit: 16,
                members: MakePlayers(3, CaptureSessionId, owner: true,
                    teamMode: false),
                mapKey: "CAPTURE FFA ARENA", mode: MatchMode.Battle);

        private static ImmutableArray<LobbyVoteEntry> BallotOptions()
            => ImmutableArray.Create(
                new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "CAPTURE FFA ARENA",
                    MatchMode.Battle, 4),
                new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "MP3 PROVING GROUND",
                    MatchMode.Battle, 2),
                new LobbyVoteEntry(3, LobbyVoteChoice.ReturnToLobby, "CAPTURE FFA ARENA",
                    MatchMode.Battle, 1));

        private sealed class CaptureReplicaServices : ISceneServices
        {
            public bool IsReplica => true;
        }

        private static SettingsView CreateSettings(MenuSettings settings, string section)
        {
            var view = new SettingsView(settings);
            view.ShowSection(section);
            return view;
        }

        private static SettingsView CreateControlsGamepad(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            SettingsView view = CreateCaptureSettings(settings, "Controls");
            view.ShowControlsTab("Gamepad");
            return view;
        }

        private static SettingsView CreateControlsMobile(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            SettingsView view = CreateCaptureSettings(settings, "Controls",
                touchControls: true, advancedControllerExpanded: false);
            view.ShowControlsTab("Touch");
            return view;
        }

        private static SettingsView CreateCaptureSettings(MenuSettings settings,
            string section, bool touchControls = false, bool? gyroSupported = null,
            bool? advancedControllerExpanded = null)
        {
            var view = new SettingsView(settings, inGame: false, scene: null,
                captureTouchControls: touchControls,
                captureGyroSupported: gyroSupported,
                captureAdvancedControllerExpanded: advancedControllerExpanded);
            view.ShowSection(section);
            return view;
        }

        private static Control CreateSettingsProHudOff(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsFeatureVariant(settings, false);

        private static Control CreateSettingsProHudOn(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsFeatureVariant(settings, true);

        private static Control CreateSettingsRadarCustom(MenuSettings settings,
            IReadOnlyList<string> rooms)
        {
            global::MphRead.Hud.Radar.RadarStyle previousStyle
                = global::MphRead.Hud.Radar.RadarSettings.Style;
            global::MphRead.Hud.Radar.RadarOrientation previousOrientation
                = global::MphRead.Hud.Radar.RadarSettings.Orientation;
            global::MphRead.Hud.Radar.RadarAnchor previousAnchor
                = global::MphRead.Hud.Radar.RadarSettings.Anchor;
            float previousScale = global::MphRead.Hud.Radar.RadarSettings.Scale;
            float previousOffsetX = global::MphRead.Hud.Radar.RadarSettings.OffsetX;
            float previousOffsetY = global::MphRead.Hud.Radar.RadarSettings.OffsetY;

            global::MphRead.Hud.Radar.RadarSettings.Style
                = global::MphRead.Hud.Radar.RadarStyle.Enhanced;
            global::MphRead.Hud.Radar.RadarSettings.Orientation
                = global::MphRead.Hud.Radar.RadarOrientation.North;
            global::MphRead.Hud.Radar.RadarSettings.Anchor
                = global::MphRead.Hud.Radar.RadarAnchor.Custom;
            global::MphRead.Hud.Radar.RadarSettings.Scale = 1.25f;
            global::MphRead.Hud.Radar.RadarSettings.OffsetX = 24;
            global::MphRead.Hud.Radar.RadarSettings.OffsetY = -16;
            void Restore()
            {
                global::MphRead.Hud.Radar.RadarSettings.Style = previousStyle;
                global::MphRead.Hud.Radar.RadarSettings.Orientation = previousOrientation;
                global::MphRead.Hud.Radar.RadarSettings.Anchor = previousAnchor;
                global::MphRead.Hud.Radar.RadarSettings.Scale = previousScale;
                global::MphRead.Hud.Radar.RadarSettings.OffsetX = previousOffsetX;
                global::MphRead.Hud.Radar.RadarSettings.OffsetY = previousOffsetY;
            }
            try
            {
                return new ScopedSettingsView(CreateSettings(settings, "Graphics"), Restore);
            }
            catch
            {
                Restore();
                throw;
            }
        }

        private static Control CreateSettingsGyroUnsupported(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsGyroVariant(settings, supported: false);

        private static Control CreateSettingsGyroSupported(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsGyroVariant(settings, supported: true);

        private static Control CreateSettingsGyroVariant(MenuSettings settings,
            bool supported)
        {
            bool previousGyroEnabled = InputSettings.GamepadGyroEnabled;
            InputSettings.GamepadGyroEnabled = supported;
            try
            {
                return new ScopedSettingsView(CreateCaptureSettings(settings, "Controls",
                        gyroSupported: supported),
                    () => InputSettings.GamepadGyroEnabled = previousGyroEnabled);
            }
            catch
            {
                InputSettings.GamepadGyroEnabled = previousGyroEnabled;
                throw;
            }
        }

        private static Control CreateSettingsTouchButtonsOff(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsTouchVariant(settings, buttonsVisible: false);

        private static Control CreateSettingsTouchButtonsOn(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsTouchVariant(settings, buttonsVisible: true);

        private static Control CreateSettingsTouchVariant(MenuSettings settings,
            bool buttonsVisible)
        {
            bool previousButtonsVisible = Input.TouchSettings.ButtonsVisible;
            bool[] previousControls = Input.TouchSettings.Order
                .Select(item => Input.TouchSettings.IsEnabled(item.Control)).ToArray();
            Input.TouchSettings.ButtonsVisible = buttonsVisible;
            try
            {
                return new ScopedSettingsView(CreateCaptureSettings(settings, "Controls",
                        touchControls: true), () =>
                {
                    Input.TouchSettings.ButtonsVisible = previousButtonsVisible;
                    for (int i = 0; i < Input.TouchSettings.Order.Length; i++)
                        Input.TouchSettings.SetEnabled(Input.TouchSettings.Order[i].Control,
                            previousControls[i]);
                });
            }
            catch
            {
                Input.TouchSettings.ButtonsVisible = previousButtonsVisible;
                for (int i = 0; i < Input.TouchSettings.Order.Length; i++)
                    Input.TouchSettings.SetEnabled(Input.TouchSettings.Order[i].Control,
                        previousControls[i]);
                throw;
            }
        }

        private static Control CreateSettingsAdvancedCollapsed(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsAdvancedVariant(settings, expanded: false);

        private static Control CreateSettingsAdvancedExpanded(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateSettingsAdvancedVariant(settings, expanded: true);

        private static Control CreateSettingsAdvancedVariant(MenuSettings settings,
            bool expanded)
            => CreateCaptureSettings(settings, "Controls",
                advancedControllerExpanded: expanded);

        private static Control CreateSettingsFeatureVariant(MenuSettings settings, bool value)
        {
            bool previous = Features.ProHud;
            Features.ProHud = value;
            try
            {
                return new ScopedSettingsView(CreateSettings(settings, "Graphics"),
                    () => Features.ProHud = previous);
            }
            catch
            {
                Features.ProHud = previous;
                throw;
            }
        }

        private sealed class ScopedSettingsView : ContentControl, IDisposable
        {
            private readonly Action _restore;
            private bool _disposed;

            public ScopedSettingsView(SettingsView view, Action restore)
            {
                Content = view;
                _restore = restore;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _restore();
            }
        }

        private static Control CreateHunterOverview(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateHunterCapture(settings, rooms, HunterLicenseSection.Overview);

        private static Control CreateHunterArsenal(MenuSettings settings,
            IReadOnlyList<string> rooms)
            // CreateCapture uses the existing Armory compatibility route to
            // select the concrete Arsenal section before normalizing to the
            // unified Hunter route.
            => PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Armory);

        private static Control CreateHunterRoster(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateHunterCapture(settings, rooms, HunterLicenseSection.Hunters);

        private static Control CreateHunterCareer(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateHunterCapture(settings, rooms, HunterLicenseSection.Career);

        private static Control CreateHunterMatches(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateHunterCapture(settings, rooms, HunterLicenseSection.Matches,
                emptyHistory: false);

        private static Control CreateHunterEmptyHistory(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateHunterCapture(settings, rooms, HunterLicenseSection.Matches,
                emptyHistory: true);

        private static Control CreateHunterPreviewFailure(MenuSettings settings,
            IReadOnlyList<string> rooms)
            => CreateHunterCapture(settings, rooms, HunterLicenseSection.Hunters,
                previewFailure: true);

        private static Control CreateHunterCapture(MenuSettings settings,
            IReadOnlyList<string> rooms, HunterLicenseSection section,
            bool emptyHistory = false, bool previewFailure = false)
        {
            CareerSummary career = CaptureCareer();
            ImmutableArray<MatchHistoryEntry> matches = emptyHistory
                ? ImmutableArray<MatchHistoryEntry>.Empty : CaptureHistory();
            var state = new HunterLicensePageState(CaptureLicense(), career, matches,
                NextHistoryCursor: null, Section: section, LoadingLicense: false,
                LoadingCareer: false, LoadingMatches: false, Error: null);
            HunterSection shellSection = section switch
            {
                HunterLicenseSection.Hunters => HunterSection.Hunters,
                HunterLicenseSection.Career => HunterSection.Career,
                HunterLicenseSection.Matches => HunterSection.Matches,
                _ => HunterSection.Overview
            };
            return PrimeShellView.CreateCapture(settings, rooms, PrimeRoute.Hunter,
                new PrimeShellCaptureState(License: state,
                    Hunters: CaptureHunters(), HunterSection: shellSection,
                    HunterPreviewFailure: previewFailure,
                    Identity: PrimeShellCaptureIdentity.SignedIn));
        }

        private static HunterLicense CaptureLicense()
            => new(CapturePlayerId, "Capture Preview", (int)Hunter.Samus,
                new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                Points: 420, Tier: 4, Title: "Frontier Veteran", NextThreshold: 500,
                LastOfficialDelta: 18);

        private static IReadOnlyList<HunterDossier> CaptureHunters()
        {
            string[] weapons = { "Power Beam", "Magmaul", "Judicator", "Imperialist",
                "Shock Coil", "Battlehammer", "Volt Driver" };
            return Enum.GetValues<Hunter>().Where(hunter => hunter <= Hunter.Weavel)
                .Select((hunter, index) => new HunterDossier(hunter, hunter.ToString(),
                    weapons[index], ModelAsset: null, IsFavorite: index == 0,
                    IsMostPlayed: index == 1, IsBest: index == 0,
                    PreviewStatus: "No local preview available."))
                .ToArray();
        }

        private static CareerSummary CaptureCareer()
            => new("official", MatchTrustClass.Ranked,
                new CareerTotals(Matches: 18, Wins: 12, Ties: 1, PlayedTicks: 18 * 60 * 60,
                    Kills: 96, Deaths: 54, Assists: 31, Damage: 12_840,
                    Losses: 5, HeadshotKills: 22, BipedKills: 0, AltFormKills: 7,
                    LongestKillStreak: 9, LongestWinStreak: 4,
                    KillDeathRatio: 1.78m, WinRatio: .667m),
                MostPlayedHunter: new CareerChoice("Samus", 8, 8),
                FavoriteMap: new CareerChoice("MP3 PROVING GROUND", 6, 6),
                FavoriteMode: new CareerChoice("Battle", 10, 10),
                FavoriteWeapon: new CareerChoice("PowerBeam", 14, 14),
                BestMap: new CareerChoice("COMBAT HALL", 5, 5),
                BestHunter: new CareerChoice("Samus", 8, 8),
                BestMinimumMatches: 3, RatingStatus: "Official rating active",
                Rating: new CareerRatingSummary(420, 4, "Frontier Veteran", 500,
                    18, "PairwiseNormalizedV1"));

        private static ImmutableArray<MatchHistoryEntry> CaptureHistory()
            => ImmutableArray.Create(
                new MatchHistoryEntry(
                    Guid.Parse("e1000000-0000-4000-8000-000000000001"), 18,
                    new DateTimeOffset(2026, 9, 3, 18, 20, 0, TimeSpan.Zero),
                    "MP3 PROVING GROUND", MatchMode.Battle, MatchTrustClass.Ranked,
                    Eligible: true, Won: true, Tied: false,
                    Outcome: CareerOutcome.FinishedWin,
                    PlayedTicks: 36_000, Kills: 8, Deaths: 3, Assists: 2, Damage: 1_240,
                    RatingStatus: "+18 RP"),
                new MatchHistoryEntry(
                    Guid.Parse("e1000000-0000-4000-8000-000000000002"), 17,
                    new DateTimeOffset(2026, 9, 2, 21, 5, 0, TimeSpan.Zero),
                    "COMBAT HALL", MatchMode.TeamBattle, MatchTrustClass.Ranked,
                    Eligible: true, Won: false, Tied: false,
                    Outcome: CareerOutcome.FinishedLoss,
                    PlayedTicks: 30_000, Kills: 5, Deaths: 7, Assists: 4, Damage: 980,
                    RatingStatus: "-6 RP"));

        /// <summary>
        /// Render one screen.
        ///
        /// Through a real <see cref="Window"/>, not by laying the control out
        /// on its own. Avalonia resolves styles through the visual tree's
        /// style host, and a control with no window above it has none: it
        /// measures, arranges and renders perfectly happily and comes out a
        /// flat rectangle of the background colour, which is exactly what the
        /// first attempt at this produced. The window is what connects the
        /// tree to the Application's styles.
        ///
        /// It is shown, because a window that has never been shown has no
        /// layout pass behind it -- but shown *off the side of the display*
        /// and without taking focus, so a capture run does not steal the
        /// pointer or flash a window per screen.
        /// </summary>
        private static bool Capture(Control view, string path, Size size)
        {
            Window? window = null;
            try
            {
                window = new Window
                {
                    Width = size.Width,
                    Height = size.Height,
                    Background = GuiTheme.PanelBrush,
                    RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                    SystemDecorations = SystemDecorations.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(-4000, -4000),
                    Content = view
                };
                window.Show();
                window.Measure(size);
                window.Arrange(new Rect(size));
                // The views post work to the dispatcher as they are built --
                // the front screen focuses its first control that way, and the
                // map picker loads its pictures -- and a render before that has
                // run is a picture of a half-built screen. Establish layout
                // first so a posted focus adorner never caches the control's
                // pre-layout origin over the shell header. Several passes are
                // needed because one job can queue another.
                for (int i = 0; i < 8; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                }
                window.Measure(size);
                window.Arrange(new Rect(size));
                Dispatcher.UIThread.RunJobs();
                var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)size.Width, (int)size.Height),
                    new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[uishot] {Path.GetFileName(path)} could not be rendered: {ex.Message}");
                return false;
            }
            finally
            {
                window?.Close();
                if (view is PrimeShellView prime)
                    prime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                else if (view is IDisposable disposable)
                    disposable.Dispose();
            }
        }

    }
}
