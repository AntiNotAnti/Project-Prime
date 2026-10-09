#if MPHREAD_SHELL
using System;
using Avalonia.LogicalTree;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
namespace MphRead.Mods.Launcher.Gui
{
    internal static class PrimeUiChecks
    {
        private static int _checks;
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); _checks++; }
        private static readonly Size[] Sizes = { new(1920,1080), new(1600,900), new(1440,900), new(1366,768), new(1280,720), new(830,390) };
        internal static PrimeShell Create()
        {
            var settings = new MenuSettings();
            string[] rooms = { "MP1 SANCTORUS", "MP3 PROVING GROUND" };
            PrimeShell? shell = null;
            Control Create(PrimeRoute route) => route switch
            {
                PrimeRoute.News => new NewsWorkspace(shell!.Overlays),
                PrimeRoute.Play => new PlayWorkspace(new[] {
                    new ServerBrowserEntry(new MasterListing { Address = "127.0.0.1", Port = 27888, ServerName = "LOCAL TEST ARENA" },
                        new ServerStatus { Online = true, Protocol = NetConfig.ProtocolVersion, RoomKey = rooms[0],
                            Mode = GameMode.Battle, Players = 3, MaxPlayers = 8, Latency = 24 }),
                    new ServerBrowserEntry(new MasterListing { Address = "127.0.0.2", Port = 27888, ServerName = "FULL TEST ARENA" },
                        new ServerStatus { Online = true, Protocol = NetConfig.ProtocolVersion, RoomKey = rooms[1],
                            Mode = GameMode.BattleTeams, Players = 8, MaxPlayers = 8, Latency = 42, WaitlistSupported = true, WaitlistCount = 3 })
                }) { Overlays = shell!.Overlays },
                PrimeRoute.HunterLicense => new LicenseWorkspace(loadProfile: false),
                PrimeRoute.Settings => new SettingsView(settings, shell: true),
                PrimeRoute.Offline => new OfflineWorkspace(settings, rooms, shell!.Overlays),
                PrimeRoute.Theatre => new TheatreWorkspace(manageStorage: false),
                PrimeRoute.Forge => new MapStudioScreen(shell!.Overlays, preview: true),
                PrimeRoute.Lobby => LobbyFixture(rooms, shell!.Overlays),
                _ => throw new ArgumentOutOfRangeException(nameof(route))
            };
            shell = new PrimeShell(Create, () => { }); shell.Start(); return shell;
        }
        private static LobbyScreen LobbyFixture(string[] rooms, PrimeOverlayHost overlays)
        {
            var lobby = new LobbyScreen(rooms) { Overlays = overlays };
            var roster = RosterPacket.Create(); roster.Count = 8;
            var players = (StackPanel)typeof(LobbyScreen).GetField("_players", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(lobby)!;
            for (int i = 0; i < roster.Count; i++)
            {
                roster.Slots[i] = (byte)i; roster.Names[i] = "PLAYER " + (i + 1);
                roster.Hunters[i] = (byte)(i % 7); roster.Teams[i] = (sbyte)(i % 2);
                roster.LobbyReady[i] = true; roster.Pings[i] = (ushort)(18 + i * 3);
                players.Children.Add(new LobbyPlayerRow(roster, i, 0, changeTeam: _ => { }));
            }
            return lobby;
        }
        private static void CheckCosmeticThumbnailReentry()
        {
            string path = Path.Combine(Path.GetTempPath(), "prime-thumbnail-" + Guid.NewGuid() + ".png");
            var window = new Window { Width = 200, Height = 200, ShowInTaskbar = false,
                Position = new PixelPoint(-4000, -4000), WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(8, 8),
                    new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque))
                    bitmap.Save(path);
                var image = LicenseWorkspace.CreateCosmeticThumbnail(path);
                window.Show();
                Avalonia.Media.IImage? previous = null;
                for (int visit = 0; visit < 4; visit++)
                {
                    window.Content = image; Drain(window);
                    Check(image.Source != null && !ReferenceEquals(previous, image.Source),
                        "cosmetic thumbnail reloads on license visit " + visit);
                    previous = image.Source;
                    window.Content = null; Drain(window);
                    Check(image.Source == null, "detached cosmetic thumbnail releases its source " + visit);
                }
            }
            finally { window.Close(); File.Delete(path); }
        }

        private static void CheckCosmeticPreviewModes()
        {
            var license = new LicenseWorkspace(loadProfile: false);
            var window = new Window { Width = 1280, Height = 720, Content = license, ShowInTaskbar = false,
                Position = new PixelPoint(-4000, -4000), WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                window.Show(); Drain(window);
                var customize = ControllerNav.Find(license, "hunter-license.customization")!;
                customize.Focus(); FocusNavigator.Key(customize, Key.Enter); Drain(window);
                foreach (int mode in new[] { 1, 2, 0, 2, 1 })
                {
                    ((ChoiceRow)ControllerNav.Find(license, "cosmetics.preview-mode")!).Index = mode;
                    Drain(window);
                    var stand = license.GetVisualDescendants().OfType<HunterStand>().Single();
                    Check((int)stand.PreviewMode == mode, "license selects preview model " + mode);
                    window.Content = null; Drain(window); window.Content = license; Drain(window);
                    Check(((ChoiceRow)ControllerNav.Find(license, "cosmetics.preview-mode")!).Index == mode,
                        "license retains preview mode after re-entry " + mode);
                }
            }
            finally { window.Close(); }
        }

        private static void CheckHunterLicenseIdentitySurface()
        {
            var license = new LicenseWorkspace(loadProfile: false);
            var window = new Window
            {
                Width = 1280,
                Height = 720,
                Content = license,
                ShowInTaskbar = false,
                Position = new PixelPoint(-4000, -4000),
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            var flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            var face = typeof(LicenseWorkspace).GetNestedType(
                "Face", System.Reflection.BindingFlags.NonPublic)!;
            var snapshot = new HunterLicenseSnapshot
            {
                Connected = true,
                Status = "SYNCED",
                Account = new HunterLicenseAccount
                {
                    IsAnonymous = false,
                    Email = "hunter@example.com",
                    Providers = new List<string> { "email" }
                },
                Profile = new HunterLicenseProfile
                {
                    PlayerId = "12345678-1111-2222-3333-444444444444",
                    DisplayName = "Fixture Hunter",
                    FavoriteHunter = (int)Hunter.Trace,
                    CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                    RatingPoints = 1234,
                    RatingTier = 4
                },
                Stats = new HunterLicenseStats
                {
                    GamesPlayed = 12,
                    Wins = 7,
                    Losses = 4,
                    Ties = 1,
                    Kills = 96,
                    Deaths = 48,
                    Assists = 31,
                    Damage = 15420,
                    PlayedTicks = 60 * 60 * 3,
                    Headshots = 22,
                    LongestKillStreak = 8,
                    CurrentWinStreak = 2,
                    LongestWinStreak = 4,
                    OctolithScores = 6,
                    NodesCaptured = 11,
                    KillsAsPrime = 9
                }
            };
            bool[] results = { true, true, false, true, false };
            bool[] ties = { false, false, false, false, true };
            for (int i = 0; i < 5; i++)
            {
                snapshot.Matches.Add(new HunterLicenseMatch
                {
                    MatchId = "fixture-" + i,
                    PlayedAt = DateTimeOffset.UtcNow.AddMinutes(-i * 10),
                    RoomKey = i == 0 ? "MP1 SANCTORUS" : "MP3 PROVING GROUND",
                    Mode = (int)GameMode.Battle,
                    CareerEligible = true,
                    Eligible = true,
                    Won = results[i],
                    Tied = ties[i],
                    PlayedTicks = 60 * 300,
                    Kills = 10 - i,
                    Deaths = 4 + i,
                    Assists = 2,
                    Damage = 1200 - i * 50
                });
            }

            try
            {
                window.Show();
                typeof(LicenseWorkspace).GetField("_snapshot", flags)!
                    .SetValue(license, snapshot);
                typeof(LicenseWorkspace).GetMethod("ApplySnapshot", flags)!
                    .Invoke(license, new object[] { snapshot });
                typeof(LicenseWorkspace).GetMethod("Show", flags)!
                    .Invoke(license, new[] { Enum.Parse(face, "Overview") });
                Drain(window);

                string[] texts = license.GetVisualDescendants().OfType<TextBlock>()
                    .Select(block => block.Text ?? "").ToArray();
                Check(texts.Any(text => text.Contains("FIXTURE HUNTER", StringComparison.Ordinal)),
                    "Hunter License renders the license holder identity");
                Check(texts.Any(text => text.Contains("FAVORITE HUNTER", StringComparison.Ordinal)
                    && text.Contains("TRACE", StringComparison.Ordinal)),
                    "Hunter License renders authoritative favorite hunter");
                Check(texts.Any(text => text.Contains("TIER 4", StringComparison.Ordinal)),
                    "Hunter License surfaces the rating tier");
                Check(texts.Any(text => text.Contains("3 W / 1 L / 1 T", StringComparison.Ordinal)),
                    "Hunter License recent form is computed from accepted matches");
                Check(texts.Any(text => text.Contains("LATEST // WIN", StringComparison.Ordinal)
                    && text.Contains("DATA SHRINE", StringComparison.OrdinalIgnoreCase)),
                    "Hunter License identifies the latest accepted match");
                Check(license.GetVisualDescendants().OfType<HunterStand>().Single().Name2
                    == Hunter.Trace.ToString(),
                    "Hunter License presentation follows favorite hunter");
            }
            finally
            {
                window.Close();
            }
        }

        private static void CheckLicenseAccountFeedback()
        {
            var license = new LicenseWorkspace(loadProfile: false);
            var window = new Window { Width = 1280, Height = 720, Content = license, ShowInTaskbar = false,
                Position = new PixelPoint(-4000, -4000), WindowStartupLocation = WindowStartupLocation.Manual };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var face = typeof(LicenseWorkspace).GetNestedType("Face", System.Reflection.BindingFlags.NonPublic)!;
            void OpenAccount()
            {
                typeof(LicenseWorkspace).GetMethod("Show", flags)!.Invoke(license,
                    new[] { Enum.Parse(face, "Account") });
                Drain(window);
            }
            try
            {
                window.Show(); OpenAccount();
                HubNavButton Button(string label) => license.GetVisualDescendants().OfType<HubNavButton>()
                    .Single(button => button.Label == label);
                Check(Button("SEND VERIFICATION").IsEnabled,
                    "offline profile does not disable email linking retries");
                Check(Button("REFRESH LINK STATUS").IsEnabled,
                    "offline account can refresh its connection");
                Check(!Button("SIGN IN // RECOVER LICENSE").IsEnabled,
                    "offline career cannot authorize replacing the current guest identity");
                var send = Button("SEND VERIFICATION");
                send.Focus(); FocusNavigator.Key(send, Key.Enter); Drain(window);
                Check(license.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Enter a valid email address."),
                    "email verification validates email without requiring a password");
                var fields = license.GetVisualDescendants().OfType<FieldRow>().ToArray();
                fields[0].Box.Text = "creator@example.com";
                fields[1].Box.Text = "short";
                fields[2].Box.Text = "different";
                var finish = Button("I VERIFIED // FINISH");
                finish.Focus(); FocusNavigator.Key(finish, Key.Enter); Drain(window);
                var status = license.GetVisualDescendants().OfType<TextBlock>()
                    .Single(block => block.Name == "license-account-status");
                Check(status.Text!.Contains("both password fields must match"),
                    "registration validation displays actionable feedback");
                Check(status.TranslatePoint(default, window) is { } point && point.Y >= 0 && point.Y < 350,
                    "account feedback is visible above the form");
                fields = license.GetVisualDescendants().OfType<FieldRow>().ToArray();
                Check(fields[0].Value == "creator@example.com" && fields[1].Value == "short"
                    && fields[2].Value == "different", "account validation preserves distinct input values");
                typeof(LicenseWorkspace).GetField("_securityBusy", flags)!.SetValue(license, true);
                OpenAccount();
                Check(!Button("SEND VERIFICATION").IsEnabled
                    && license.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Contacting account service…"),
                    "busy account shows progress and prevents duplicate submission");
            }
            finally { window.Close(); }
        }

        private static void CheckCommunityMapPicker()
        {
            var map = new MapGen.CommunityMap(new string('a', 64), Guid.NewGuid(), new string('b', 64),
                "COMMUNITY FIXTURE", "Community Test Arena", "Test Creator", "1.0", 100);
            var download = new System.Threading.Tasks.TaskCompletionSource<string>();
            int installs = 0;
            System.Threading.CancellationToken installToken = default;
            string? picked = null;
            var picker = new MapCardPicker(new[] { "MP1 SANCTORUS" }, null, includeCommunity: true,
                browseCommunity: _ => System.Threading.Tasks.Task.FromResult(new[] { map }),
                installCommunity: (_, token) => { installs++; installToken = token; return download.Task; });
            var window = new Window { Width = 1280, Height = 720, Content = picker, ShowInTaskbar = false,
                Position = new PixelPoint(-4000, -4000), WindowStartupLocation = WindowStartupLocation.Manual };
            picker.Done += (_, room) => picked = room;
            void Click(Control control) { control.Focus(); FocusNavigator.Key(control, Key.Enter); Drain(window); }
            try
            {
                window.Show(); Drain(window);
                Check(picker.GetVisualDescendants().OfType<DeckTile>().Count() == 2,
                    "community cards appear alongside local maps");
                var search = (TextBox)ControllerNav.Find(picker, "map-picker.search")!;
                search.Text = "Test Creator"; Drain(window);
                var visible = picker.GetVisualDescendants().OfType<DeckTile>().Where(t => t.IsVisible).ToArray();
                Check(visible.Length == 1 && visible[0].RoomKey == map.Name,
                    "community maps can be searched by author");
                Click(visible[0]);
                var use = ControllerNav.Find(picker, "map-picker.use")!;
                Click(use);
                Check(installs == 1 && picked == null && !use.IsEnabled,
                    "community selection waits for preparation before changing the lobby");
                download.SetResult(map.Name); Drain(window);
                Check(picked == map.Name, "prepared community map confirms the selected room");
                window.Content = null; Drain(window);
                Check(installToken.IsCancellationRequested, "closing picker cancels its community lifetime");

                var failure = new MapCardPicker(new[] { "MP1 SANCTORUS" }, "MP1 SANCTORUS", includeCommunity: true,
                    browseCommunity: _ => System.Threading.Tasks.Task.FromException<MapGen.CommunityMap[]>(new IOException("test outage")));
                window.Content = failure; Drain(window);
                Check(ControllerNav.Find(failure, "map-picker.use")!.IsEnabled,
                    "community outage leaves local selection available");
                window.Content = null; Drain(window);

                var blocked = new MapCardPicker(Array.Empty<string>(), null, _ => "Unsupported test mode", includeCommunity: true,
                    browseCommunity: _ => System.Threading.Tasks.Task.FromResult(new[] { map }),
                    installCommunity: (_, _) => System.Threading.Tasks.Task.FromResult(map.Name));
                string? rejected = null; blocked.Done += (_, room) => rejected = room;
                window.Content = blocked; Drain(window);
                Click(blocked.GetVisualDescendants().OfType<DeckTile>().Single());
                Click(ControllerNav.Find(blocked, "map-picker.use")!);
                Check(rejected == null, "community selection rechecks mode compatibility after installation");
                window.Content = null; Drain(window);

                var late = new System.Threading.Tasks.TaskCompletionSource<MapGen.CommunityMap[]>();
                var closed = new MapCardPicker(Array.Empty<string>(), null, includeCommunity: true,
                    browseCommunity: _ => late.Task);
                window.Content = closed; Drain(window);
                window.Content = null; Drain(window);
                late.SetResult(new[] { map }); Drain(window);
                Check(!closed.GetLogicalDescendants().OfType<DeckTile>().Any(),
                    "late community results do not populate a dismissed picker");
            }
            finally { window.Close(); }
        }

        private static void CheckSavedLobbyLimits()
        {
            GameMode previousMode = LauncherPrefs.LastLobbyMode;
            int previousTime = LauncherPrefs.LastLobbyTimeLimitSeconds;
            int previousGoal = LauncherPrefs.LastLobbyGoal;
            try
            {
                LauncherPrefs.LastLobbyMode = GameMode.Battle;
                LauncherPrefs.LastLobbyTimeLimitSeconds = 10 * 60;
                LauncherPrefs.LastLobbyGoal = 25;

                var saved = CreateServerScreen.InitialMatchLimits(GameMode.Battle);
                Check(saved.TimeLimit == 10 * 60 && saved.PointGoal == 25,
                    "new lobby reuses the last accepted time and goal");

                var differentMode = CreateServerScreen.InitialMatchLimits(GameMode.Capture);
                Check(differentMode.TimeLimit == 10 * 60
                    && differentMode.PointGoal == MatchGoalRules.DefaultValue(GameMode.Capture),
                    "new lobby keeps the clock but does not leak an incompatible goal across modes");
            }
            finally
            {
                LauncherPrefs.LastLobbyMode = previousMode;
                LauncherPrefs.LastLobbyTimeLimitSeconds = previousTime;
                LauncherPrefs.LastLobbyGoal = previousGoal;
            }
        }

        private static void CheckInProgressAdmission()
        {
            // Connect already knows the server is InMatch before creating the
            // lobby screen. Repeat after disconnect to cover same-match rejoin.
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    NetSession.StartClient("127.0.0.1", 9);
                    NetSession.ApplySessionState(new SessionStatePacket
                    {
                        Policy = ServerSessionPolicy.Lobby, Phase = SessionPhase.InMatch,
                        MatchId = 1, AuthorityEpoch = 1, StartGeneration = 1, Revision = 1,
                        Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle }
                    });
                    var lobby = new LobbyScreen(new[] { "MP1 SANCTORUS" });
                    using var coordinator = new LobbySessionCoordinator();
                    coordinator.Start();
                    coordinator.Screen = lobby;
                    Check(!lobby.IsSuspended,
                        "early in-progress hydration waits for the gameplay handoff subscriber");
                    coordinator.Stop();

                    int requests = 0;
                    lobby.MatchRequested += (_, plan) =>
                    {
                        Check(plan.Kind == LaunchKind.Online && plan.RoomKey == "MP1 SANCTORUS",
                            "in-progress admission loads the active match");
                        requests++;
                    };
                    coordinator.Tick();
                    Check(requests == 1 && lobby.IsSuspended,
                        "in-progress join/rejoin hands the lobby connection to gameplay");
                    coordinator.Tick();
                    Check(requests == 1, "gameplay handoff does not request a duplicate scene load");
                    NetSession.Stop();
                }
            }
            finally { NetSession.Stop(); }
        }
        private static void CheckAdvancedRules()
        {
            using var stopped = new LobbySessionCoordinator();
            var overlays = new PrimeOverlayHost();
            var lobby = new LobbyScreen(new[] { "MP1 SANCTORUS" }) { Overlays = overlays };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            ButtonToggleRow Toggle(string name) => (ButtonToggleRow)typeof(LobbyScreen).GetField(name, flags)!.GetValue(lobby)!;
            var insta = Toggle("_instaGib"); var noImp = Toggle("_noImperialist"); var low = Toggle("_lowTier");
            Check(new[] { insta, noImp, low }.All(row => row.Children.OfType<PrimeButton>().Count(button => button.Focusable) == 2), "advanced rules support controller focus");
            var advanced = (Control)insta.Parent!.Parent!.Parent!;
            typeof(LobbyScreen).GetMethod("ShowSheet", flags)!.Invoke(lobby, new object[] { "LOBBY RULES", advanced });
            Check(overlays.GetLogicalDescendants().Contains(insta) && overlays.GetLogicalDescendants().Contains(noImp)
                && overlays.GetLogicalDescendants().Contains(low), "advanced rules are in the open rules sheet");
            noImp.On = true; FocusNavigator.Key(insta.Children.OfType<PrimeButton>().Last(), Key.Enter);
            Check(insta.On && !noImp.On, "Insta-Gib clears conflicting No Imp");
            noImp.On = true;
            Check(noImp.On && !insta.On, "No Imp clears conflicting Insta-Gib");
            Check(!Toggle("_spawnProtection").On && !Toggle("_freeze").On, "lobby protection and freeze defaults off");
            Check(!OfflineLaunch.Modes.Any(mode => mode.Mode == GameMode.InstaGib), "legacy mode is absent from offline selector");
            overlays.Close();

            var target = (ChoiceRow)typeof(LobbyScreen).GetField("_target", flags)!.GetValue(lobby)!;
            var administration = (Control)target.Parent!;
            typeof(LobbyScreen).GetMethod("ShowSheet", flags)!.Invoke(lobby,
                new object[] { "PLAYER MANAGEMENT", administration });
            // No Window is attached in this UI composition fixture. The
            // visual ScrollViewer presenter has not been materialized; the
            // commands exist in the logical tree until it gets a Window.
            Check(overlays.GetLogicalDescendants().Contains(target)
                && overlays.GetLogicalDescendants().OfType<HubNavButton>()
                    .Any(button => button.Label == "TRANSFER OWNER")
                && overlays.GetLogicalDescendants().OfType<HubNavButton>()
                    .Any(button => button.Label == "KICK"),
                "player management is contextual and retains owner actions");
            overlays.Close();
        }

        private static void CheckWorkspaceTransitionReentry()
        {
            bool previousStill = Deck.Still;
            bool previousReduce = LauncherPrefs.ReduceMotion;
            Deck.Still = false;
            LauncherPrefs.ReduceMotion = false;
            var created = new Dictionary<PrimeRoute, Control>();
            var host = new PrimeWorkspaceHost(route =>
            {
                var panel = new PrimePanel(PrimeChrome.Text(route.ToString()));
                created[route] = panel;
                return panel;
            });
            var window = new Window
            {
                Width = 900,
                Height = 600,
                Content = host,
                ShowInTaskbar = false,
                Position = new PixelPoint(-4000, -4000),
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            try
            {
                window.Show();
                Drain(window);
                host.Show(PrimeRoute.News);
                host.Show(PrimeRoute.Play);
                Check(created[PrimeRoute.News].GetVisualParent() == null
                    && ReferenceEquals(host.Content, created[PrimeRoute.Play]),
                    "route transition removes outgoing Home before incoming motion");

                // Re-enter immediately while Play is still animating. The incoming-only
                // transition must cancel cleanly without showing or reparenting Play.
                host.Show(PrimeRoute.News);
                Check(created[PrimeRoute.Play].GetVisualParent() == null
                    && ReferenceEquals(host.Content, created[PrimeRoute.News]),
                    "rapid Home reentry swaps directly to the cached incoming page");

                Deck.Still = true;
                host.Show(PrimeRoute.Play);
                Drain(window);
                Check(created[PrimeRoute.News].GetVisualParent() == null
                    && ReferenceEquals(host.Content, created[PrimeRoute.Play]),
                    "settled incoming transition owns only the active workspace");
            }
            finally
            {
                window.Close();
                host.Dispose();
                Deck.Still = previousStill;
                LauncherPrefs.ReduceMotion = previousReduce;
            }
        }

        public static int Run(string? directory)
        {
            if (!GuiLauncher.EnsureSetup(requireDisplay: false)) return 1;
            _checks = 0; bool previousStill = Deck.Still; Deck.Still = true;
            try
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    foreach (string asset in new[] {
                        "avares://ProjectPrime/Assets/Fonts/Rajdhani-SemiBold.ttf",
                        "avares://ProjectPrime/Assets/Fonts/Rajdhani-Bold.ttf",
                        "avares://ProjectPrime/Assets/Fonts/Rajdhani-OFL.txt" })
                        Check(Avalonia.Platform.AssetLoader.Exists(new Uri(asset)), "bundled tactical font asset " + asset);
                    Check(new Avalonia.Media.Typeface(PrimeTypography.Display, weight: Avalonia.Media.FontWeight.Bold)
                        .GlyphTypeface.FamilyName.Contains("Rajdhani", StringComparison.OrdinalIgnoreCase), "display resolves to bundled Rajdhani");
                    Check(new Avalonia.Media.Typeface(PrimeTypography.Label, weight: Avalonia.Media.FontWeight.SemiBold)
                        .GlyphTypeface.FamilyName.Contains("Rajdhani", StringComparison.OrdinalIgnoreCase), "controls resolve to bundled Rajdhani");
                    foreach (var mode in Enum.GetValues<WindowStartMode>())
                        Check(WindowMode.Parse(WindowMode.Serialize(mode), WindowStartMode.Windowed) == mode,
                            "window mode preference round trip: " + mode);
                    Check(WindowMode.Parse("borderless", WindowStartMode.Windowed) == WindowStartMode.BorderlessFullscreen
                        && WindowMode.Parse("1", WindowStartMode.Windowed) == WindowStartMode.BorderlessFullscreen,
                        "legacy borderless preferences remain valid");
                    CheckCosmeticThumbnailReentry();
                    CheckCosmeticPreviewModes();
                    CheckHunterLicenseIdentitySurface();
                    CheckLicenseAccountFeedback();
                    CheckCommunityMapPicker();
                    CheckSavedLobbyLimits();
                    CheckAdvancedRules();
                    CheckInProgressAdmission();
                    CheckWorkspaceTransitionReentry();
                    var shell = Create();
                    var window = new Window { Width = 1280, Height = 720, Content = shell, ShowInTaskbar = false,
                        Position = new PixelPoint(-4000,-4000), WindowStartupLocation = WindowStartupLocation.Manual };
                    window.Show(); Drain(window);
                    var header = shell.Header; var footer = shell.Footer;
                    var shellTabs = header.GetVisualDescendants().OfType<PrimeTabButton>()
                        .Select(button => button.Label).ToArray();
                    Check(shellTabs.Contains("HOME") && shellTabs.Contains("REPLAY STUDIO")
                        && shellTabs.Contains("MAP STUDIO"),
                        "shell uses player-facing Home and studio destination labels");
                    shell.Router.Navigate(PrimeRoute.News); Drain(window);
                    Check(shell.Workspaces.Get(PrimeRoute.News)
                        .GetVisualDescendants().OfType<PrimeHeroPanel>().Any(),
                        "Home exposes cinematic hero presentation");
                    var retained = new Dictionary<PrimeRoute, Control>();
                    foreach (var route in PrimeRouter.Tabs)
                    {
                        shell.Router.Navigate(route); Drain(window);
                        Check(ReferenceEquals(header, shell.Header) && ReferenceEquals(footer, shell.Footer), "persistent chrome: " + route);
                        Check(FocusNavigator.Ensure(shell.Workspaces) != null, "reachable workspace control: " + route);
                        retained[route] = (Control)shell.Workspaces.Content!;
                    }
                    foreach (var (route, view) in retained)
                    { shell.Router.Navigate(route); Drain(window); Check(ReferenceEquals(view, shell.Workspaces.Content), "retained workspace " + route); }
                    shell.Router.Navigate(PrimeRoute.News); shell.Router.PreviousRoute();
                    Check(shell.Router.Current == PrimeRoute.Settings, "previous tab wraps");
                    shell.Router.NextRoute(); Check(shell.Router.Current == PrimeRoute.News, "next tab wraps");

                    shell.Router.Navigate(PrimeRoute.Forge); Drain(window);
                    var mapStudio = shell.Workspaces.Get(PrimeRoute.Forge);
                    Check(ControllerNav.Find(mapStudio, "studio.back") != null
                        && ControllerNav.Find(mapStudio, "studio.save") != null
                        && ControllerNav.Find(mapStudio, "studio.validate") != null
                        && ControllerNav.Find(mapStudio, "studio.build") != null
                        && ControllerNav.Find(mapStudio, "studio.playtest") != null,
                        "Map Studio exposes persistent authoring and project actions");
                    string[] studioHeadings = mapStudio.GetVisualDescendants().OfType<TextBlock>()
                        .Select(block => block.Text ?? "").ToArray();
                    Check(studioHeadings.Contains("SCENE HIERARCHY")
                        && studioHeadings.Contains("VIEWPORT")
                        && studioHeadings.Contains("INSPECTOR"),
                        "Map Studio separates hierarchy viewport and inspector workspaces");
                    var assetsQuick = ControllerNav.Find(mapStudio, "studio.assets")!;
                    assetsQuick.Focus(); FocusNavigator.Key(assetsQuick, Key.Enter); Drain(window);
                    Check(mapStudio.GetVisualDescendants().OfType<TextBlock>()
                        .Any(block => block.Text == "ASSETS & MUSIC"),
                        "Map Studio contextual Assets workspace is directly reachable");
                    var healthQuick = ControllerNav.Find(mapStudio, "studio.health")!;
                    healthQuick.Focus(); FocusNavigator.Key(healthQuick, Key.Enter); Drain(window);
                    Check(mapStudio.GetVisualDescendants().OfType<TextBlock>()
                        .Any(block => block.Text == "MAP HEALTH"),
                        "Map Studio contextual health workspace is directly reachable");

                    shell.Router.Navigate(PrimeRoute.Lobby); Drain(window);
                    var lobbyPresentation = shell.Workspaces.Get(PrimeRoute.Lobby);
                    Check(lobbyPresentation.GetVisualDescendants().OfType<PrimeHeroPanel>().Any(),
                        "Lobby exposes cinematic arena presentation");
                    Check(lobbyPresentation.GetVisualDescendants().OfType<PrimeButton>()
                        .All(button => button.Label != "TEAMS & ADMINISTRATION"),
                        "Lobby removes permanent teams and administration entry");
                    Check(ControllerNav.Find(lobbyPresentation, "lobby.manage-selected") != null,
                        "Lobby exposes contextual player-management action");

                    shell.Router.Navigate(PrimeRoute.Play); Drain(window);
                    var play = shell.Workspaces.Get(PrimeRoute.Play);
                    Check(play.GetVisualDescendants().OfType<PrimeHeroPanel>().Any(),
                        "Multiplayer exposes cinematic hero presentation");
                    var quick = ControllerNav.Find(play,"multiplayer.quick")!;
                    quick.Focus(); FocusNavigator.Key(quick, Key.Enter); Drain(window);
                    var join = ControllerNav.Find(play,"multiplayer.join")!;
                    Check(join.IsEnabled, "Quick Play selects an open compatible fixture");
                    join.Focus(); shell.Router.Navigate(PrimeRoute.News); shell.Router.Navigate(PrimeRoute.Play); Drain(window);
                    Check(ReferenceEquals(play, shell.Workspaces.Content) && join.IsEnabled, "selected server survives navigation");
                    Check(join.IsFocused, "workspace focus is restored");
                    FocusNavigator.Key(join, Key.Up); Drain(window);
                    Check(FocusNavigator.Focused(shell) != join, "keyboard arrows move spatial focus");
                    join.Focus();
                    var stand = play.GetVisualDescendants().OfType<HunterStand>().First();
                    Check(stand.CanPresentPreview(), "visible workspace hunter can present");
                    shell.Overlays.Show(new PrimePanel(PrimeChrome.Stack(new PrimeButton("CANCEL", shell.Overlays.Close))), PrimeModalSize.Small);
                    Drain(window);
                    Check(!stand.CanPresentPreview(), "workspace hunter cannot paint over a modal");
                    Check(ControllerNav.ModalRoot(shell) == shell.Overlays, "controller focus is trapped in modal");
                    foreach (var direction in new[] { Mods.Input.UiAction.Up, Mods.Input.UiAction.Down, Mods.Input.UiAction.Left, Mods.Input.UiAction.Right })
                    {
                        FocusNavigator.Move(shell, direction);
                        Check(FocusNavigator.Focused(shell.Overlays) != null, "directional focus remains in modal");
                    }
                    FocusNavigator.Key(FocusNavigator.Ensure(shell)!, Key.Escape); Drain(window);
                    Check(!shell.Overlays.IsOpen && shell.Router.Current == PrimeRoute.Play, "Escape closes modal first");
                    Check(join.IsFocused, "modal close restores focus");
                    Check(stand.CanPresentPreview(), "hunter presentation resumes after modal closes");
                    bool resumed = false;
                    shell.Overlays.Show(new PrimePanel(PrimeChrome.Text("pause")), cancel: () => { shell.Overlays.Close(); resumed = true; });
                    shell.Back(); Check(resumed && !shell.Overlays.IsOpen, "Back dispatches overlay cancellation semantics");
                    shell.Router.Navigate(PrimeRoute.Settings); Drain(window);
                    var settings = (SettingsView)shell.Workspaces.Content!;
                    Check(settings.GetVisualDescendants().OfType<PrimeTabButton>().Count() == 9
                        && !settings.GetVisualDescendants().OfType<Control>().Any(c =>
                            c.GetValue(ControllerNav.NavIdProperty)?.StartsWith("settings.detail.Display", StringComparison.OrdinalIgnoreCase) == true),
                        "shell settings has one category strip without a duplicate sidebar");

                    var systemTab = settings.GetVisualDescendants().OfType<PrimeTabButton>()
                        .Single(button => button.Label == "SYSTEM");
                    var maintenanceTab = settings.GetVisualDescendants().OfType<PrimeTabButton>()
                        .Single(button => button.Label == "MAINTENANCE");
                    Check(!systemTab.IsVisible && !maintenanceTab.IsVisible,
                        "Basic settings hides System and Maintenance categories");
                    var advancedMode = ControllerNav.Find(settings, "settings.mode.advanced")!;
                    advancedMode.Focus(); FocusNavigator.Key(advancedMode, Key.Enter); Drain(window);
                    Check(systemTab.IsVisible && maintenanceTab.IsVisible,
                        "Advanced settings exposes full category surface");
                    var basicMode = ControllerNav.Find(settings, "settings.mode.basic")!;
                    basicMode.Focus(); FocusNavigator.Key(basicMode, Key.Enter); Drain(window);

                    var settingsSearch = (TextBox)ControllerNav.Find(settings, "settings.search")!;
                    settingsSearch.Text = "renderer"; Drain(window);
                    var rendererResult = settings.GetVisualDescendants().OfType<PrimeButton>()
                        .First(button => button.Label == "RENDERER");
                    rendererResult.Focus(); FocusNavigator.Key(rendererResult, Key.Enter); Drain(window);
                    Check(settings.GetVisualDescendants().OfType<TextBlock>().Any(block =>
                            block.Text?.Contains("graphics backend", StringComparison.OrdinalIgnoreCase) == true),
                        "Settings search focuses a result and surfaces per-setting help");
                    var renderer = settings.GetVisualDescendants().OfType<ChoiceRow>()
                        .First(row => row.IsEffectivelyVisible && row.Label == "Renderer");
                    int rendererBefore = renderer.Index;
                    renderer.Index = rendererBefore == 0 ? 1 : 0;
                    Drain(window);
                    if (renderer.Index != rendererBefore)
                    {
                        Check(settings.GetVisualDescendants().OfType<TextBlock>().Any(block =>
                                block.Text?.Contains("RENDERER REQUIRES RESTART", StringComparison.Ordinal) == true),
                            "renderer edit surfaces restart-required draft state");
                        var graphicsReset = ControllerNav.Find(settings, "settings.category.reset")!;
                        graphicsReset.Focus(); FocusNavigator.Key(graphicsReset, Key.Enter); Drain(window);
                        Check(renderer.Index == rendererBefore,
                            "Graphics category reset restores renderer selection");
                    }

                    settings.ShowSection("Display"); Drain(window);
                    CheckTeamCycling();
                    var slider = settings.GetVisualDescendants().OfType<SliderRow>()
                        .First(row => row.IsEffectivelyVisible);
                    int value = slider.Value;
                    slider.Value = value == 0 ? 1 : value - 1;
                    Drain(window);
                    Check(settings.IsDirty, "settings edit marks draft dirty");
                    Check(settings.GetVisualDescendants().OfType<TextBlock>().Any(block =>
                            block.Text?.StartsWith("UNSAVED CHANGES", StringComparison.Ordinal) == true),
                        "settings command card reports dirty draft state");
                    var resetCategory = ControllerNav.Find(settings, "settings.category.reset")!;
                    Check(resetCategory.IsEnabled, "dirty settings category enables category reset");
                    resetCategory.Focus(); FocusNavigator.Key(resetCategory, Key.Enter); Drain(window);
                    Check(slider.Value == value && !settings.IsDirty,
                        "Reset Category restores only the active category draft");

                    slider.Value = value == 0 ? 1 : value - 1;
                    settings.ShowSection("Audio"); settings.ShowSection("Display");
                    Check(settings.IsDirty, "settings category retains draft");
                    settings.DiscardDraft();
                    Check(slider.Value == value && !settings.IsDirty,
                        "Discard restores controls and clean state");
                    var previousRuntime = Mods.Input.GamepadRuntimeConfig.Current;
                    try
                    {
                        Mods.Input.GamepadRuntimeConfig.Current = new Mods.Input.GamepadRuntimeConfig();
                        var defaults = new SettingsDraft(new Panel());
                        Mods.Input.PadBindings.ApplyPreset("Southpaw");
                        defaults.Discard();
                        Check(!defaults.IsDirty, "default controller preset survives Discard");
                    }
                    finally { Mods.Input.GamepadRuntimeConfig.Current = previousRuntime; }
                    var rows = play.GetVisualDescendants().OfType<ServerRow>().ToArray();
                    Check(rows.Length == 2 && rows[0].CanJoin && !rows[1].CanJoin, "full server cannot be joined");
                    Check(rows.Any(row => row.Bounds.Height > 0)
                        && rows[0].TranslatePoint(default, play) is { } serverPoint
                        && serverPoint.Y < play.Bounds.Height,
                        "Play keeps the live server browser inside the visible workspace");
                    Check(play.GetVisualDescendants().OfType<PrimePanel>().Any(panel =>
                        panel.GetVisualDescendants().OfType<TextBlock>()
                            .Any(block => block.Text == "SELECTED SESSION")
                        && panel.GetVisualDescendants().OfType<TextBlock>()
                            .Any(block => block.Text == "DEPLOYMENT LOADOUT")),
                        "Play combines selected session and deployment loadout in one right panel");
                    Check(rows[1].CanQueue && rows[1].WaitingCount == 3, "full compatible server exposes advertised queue");
                    var queueDialog = LobbyQueueDialog.ShowAsync(shell.Overlays, "127.0.0.1", 27888);
                    Drain(window);
                    Check(shell.Overlays.IsOpen && !queueDialog.IsCompleted, "queue requires explicit opt-in");
                    Check(shell.Overlays.GetVisualDescendants().OfType<PrimeButton>().Single(b => b.Label == "ACCEPT SEAT").IsEnabled == false, "no acceptance before a server offer");
                    shell.Back(); Drain(window);
                    Check(!shell.Overlays.IsOpen && queueDialog.IsCompletedSuccessfully && queueDialog.Result == null, "queue cancellation releases modal without admission");
                    shell.Footer.SetStatus("DOWNLOADING 42%"); shell.Refresh();
                    Check(shell.Footer.Version.Label == "DOWNLOADING 42%", "telemetry refresh preserves update progress");
                    shell.Footer.SetStatus("SIM: 60 HZ // BUILD LOCAL");
                    var played = new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = "MP1 SANCTORUS",
                        Mode = GameMode.Battle, Bots = 5, BotLevel = 3, Hunter = Hunter.Trace, PlayerName = "CHECK" };
                    Check(OfflineRematch.TryPlan(played, "", out var same) && same.RoomKey == played.RoomKey,
                        "unselected bot results repeat current arena");
                    Check(OfflineRematch.TryPlan(played, "MP3 PROVING GROUND", out var next)
                        && next.RoomKey == "MP3 PROVING GROUND" && next.Bots == 5 && next.BotLevel == 3
                        && next.Hunter == Hunter.Trace && next.Mode == played.Mode && next.PlayerName == played.PlayerName,
                        "next arena retains bot match configuration");
                    Check(!OfflineRematch.TryPlan(played with { Kind = LaunchKind.Online }, "MP3 PROVING GROUND", out _)
                        && !OfflineRematch.TryPlan(played with { Kind = LaunchKind.Adventure }, "", out _)
                        && !OfflineRematch.TryPlan(played with { IsPlaytest = true }, "", out _),
                        "local rematch excludes network and Adventure sessions");
                    string? picked = null;
                    var picker = new MapCardPicker(new[] { "MP1 SANCTORUS", "MP3 PROVING GROUND" }, "MP1 SANCTORUS");
                    picker.Done += (_, room) => picked = room;
                    shell.Overlays.Show(picker); Drain(window);
                    var search = (TextBox)ControllerNav.Find(picker, "map-picker.search")!;
                    search.Text = "PROVING"; Drain(window);
                    var cards = picker.GetVisualDescendants().OfType<DeckTile>().ToArray();
                    Check(cards.Count(c => c.IsVisible) == 1, "map picker filters arena cards");
                    var card = cards.Single(c => c.IsVisible); card.Focus(); FocusNavigator.Key(card, Key.Enter);
                    var use = ControllerNav.Find(picker, "map-picker.use")!;
                    use.Focus(); FocusNavigator.Key(use, Key.Enter);
                    Check(picked == "MP3 PROVING GROUND", "map picker confirms selected arena");
                    shell.Overlays.Close();
                    window.Content = null; window.Close();
                    if (directory != null)
                    {
                        Directory.CreateDirectory(directory);
                        _ = LobbyQueueDialog.ShowAsync(shell.Overlays, "127.0.0.1", 27888);
                        Check(UiCapture.Capture(shell, Path.Combine(directory, "prime-player-waitlist-960x540.png"), new Size(960,540)), "queue modal capture");
                        shell.Overlays.Close();
                        foreach (var size in Sizes)
                        foreach (var route in Enum.GetValues<PrimeRoute>())
                        {
                            shell.Router.Navigate(route);
                            string path = Path.Combine(directory, $"prime-{route.ToString().ToLowerInvariant()}-{size.Width:0}x{size.Height:0}.png");
                            Check(UiCapture.Capture(shell, path, size), "capture " + path);
                            Geometry(shell, size, route);
                        }
                        shell.Router.Navigate(PrimeRoute.Settings);
                        settings.ShowSection("Credits");
                        Check(UiCapture.Capture(shell, Path.Combine(directory, "prime-credits-1280x720.png"), new Size(1280,720)), "credits capture");
                        settings.ShowSection("Display");
                        shell.Router.Navigate(PrimeRoute.Lobby);
                        var lobby = (Control)shell.Workspaces.Content!;
                        var rules = lobby.GetVisualDescendants().OfType<PrimeButton>().Single(b => b.Label == "ADVANCED RULES");
                        rules.Focus(); FocusNavigator.Key(rules, Key.Enter);
                        Check(shell.Overlays.IsOpen, "lobby rules open");
                        Check(UiCapture.Capture(shell, Path.Combine(directory, "prime-lobby-rules-1280x720.png"), new Size(1280,720)), "lobby rules capture");
                        foreach (var toggle in shell.Overlays.GetVisualDescendants().OfType<ButtonToggleRow>().Where(t => t.IsVisible))
                        foreach (var button in toggle.GetVisualDescendants().OfType<PrimeButton>())
                            Check(button.Bounds.Width >= 50 && button.Bounds.Height >= 38, "rule toggle has a readable hit target");
                        Check(!shell.Overlays.GetVisualDescendants().OfType<ScrollViewer>().Any(), "all lobby rules fit without a scroll container");
                        Check(UiCapture.Capture(shell, Path.Combine(directory, "prime-lobby-rules-960x540.png"), new Size(960,540)), "compact lobby rules capture");
                        shell.Overlays.Close();

                        var target = (ChoiceRow)typeof(LobbyScreen)
                            .GetField("_target", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .GetValue(lobby)!;
                        var administration = (Control)target.Parent!;
                        typeof(LobbyScreen).GetMethod("ShowSheet",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(lobby, new object[] { "PLAYER MANAGEMENT", administration });
                        Check(UiCapture.Capture(shell,
                            Path.Combine(directory, "prime-lobby-player-management-1280x720.png"),
                            new Size(1280,720)), "lobby player management capture");
                        shell.Overlays.Close();
                        shell.Router.Navigate(PrimeRoute.Offline);
                        var offlineRules = ((Control)shell.Workspaces.Content!).GetVisualDescendants().OfType<PrimeButton>().Single(b => b.Label == "ADVANCED MATCH RULES");
                        offlineRules.Focus(); FocusNavigator.Key(offlineRules, Key.Enter);
                        Check(UiCapture.Capture(shell, Path.Combine(directory, "prime-offline-rules-1280x720.png"), new Size(1280,720)), "offline rules capture");
                        Check(!shell.Overlays.GetVisualDescendants().OfType<ScrollViewer>().Any(view => view.Content is StackPanel), "offline rules have no scrolling form");
                        shell.Overlays.Close();
                        shell.Router.Navigate(PrimeRoute.Play);
                        shell.Overlays.Show(new MapCardPicker(new[] { "MP1 SANCTORUS", "MP3 PROVING GROUND" }, "MP1 SANCTORUS"));
                        Check(UiCapture.Capture(shell, Path.Combine(directory, "prime-map-picker-1280x720.png"), new Size(1280,720)), "map picker capture");
                        shell.Overlays.Close();
                    }
                    shell.Dispose();
                    CheckStartup(directory);
                    // Motion begins only when Avalonia attaches the control to
                    // a TopLevel and receives compositor frames. A detached
                    // control must not complete just because RunJobs drained.
                    bool oldReduceMotion = LauncherPrefs.ReduceMotion;
                    var animated = new PrimePanel(PrimeChrome.Text("motion"));
                    var motionWindow = new Window
                    {
                        Width = 320, Height = 240, ShowInTaskbar = false,
                        Position = new PixelPoint(-4000, -4000),
                        WindowStartupLocation = WindowStartupLocation.Manual
                    };
                    try
                    {
                        Deck.Still = false;
                        LauncherPrefs.ReduceMotion = false;
                        HubMotion.Enter(animated);
                        Dispatcher.UIThread.RunJobs();
                        Check(TopLevel.GetTopLevel(animated) == null && animated.Opacity == 0
                            && animated.RenderTransform != null,
                            "detached route motion waits for visual attachment");
                        motionWindow.Content = animated;
                        motionWindow.Show();
                        Drain(motionWindow);
                        var motionDeadline = System.Diagnostics.Stopwatch.StartNew();
                        while ((animated.Opacity != 1 || animated.RenderTransform != null)
                            && motionDeadline.Elapsed < TimeSpan.FromSeconds(5))
                        {
                            System.Threading.Thread.Sleep(10);
                            Drain(motionWindow);
                        }
                        Check(ReferenceEquals(TopLevel.GetTopLevel(animated), motionWindow)
                            && animated.Opacity == 1 && animated.RenderTransform == null,
                            "headless frame clock completes route motion after visual attachment");
                    }
                    finally
                    {
                        motionWindow.Content = null;
                        motionWindow.Close();
                        LauncherPrefs.ReduceMotion = oldReduceMotion;
                        Deck.Still = true;
                    }
                    int pulses = 0;
                    using var pulse = new PrimeUiPulse(TimeSpan.FromMilliseconds(10), () =>
                    { Check(Dispatcher.UIThread.CheckAccess(), "wall-clock pulse runs on UI thread"); pulses++; });
                    pulse.Start();
                    // A busy CI worker may not schedule the thread-pool timer
                    // within one short sleep. Pump until delivery or a bounded deadline.
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    while (pulses == 0 && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    { System.Threading.Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
                    Check(pulses > 0, "embedded shell pulse advances without a native event loop");
                    pulse.Stop(); int stopped = pulses;
                    System.Threading.Thread.Sleep(30); Dispatcher.UIThread.RunJobs();
                    Check(pulses == stopped, "detached shell pulse stops");
                });
                Console.WriteLine($"[primeuicheck] PASS: {_checks} checks"); return 0;
            }
            catch (Exception ex) { Console.WriteLine("[primeuicheck] FAIL: " + ex); return 1; }
            finally { Deck.Still = previousStill; }
        }
        private static void Drain(Window window)
        { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
        private static void CheckStartup(string? directory)
        {
            bool autoUpdate = LauncherPrefs.AutoUpdate;
            LauncherPrefs.AutoUpdate = false;
            try
            {
                using var front = new StartScreen(new MenuSettings(), new[] { "MP1 SANCTORUS" });
                var shell = front.Prime;
                var header = shell.Header;
                if (directory != null)
                    foreach (var size in Sizes)
                        Check(UiCapture.Capture(front, Path.Combine(directory,
                            $"prime-startup-{size.Width:0}x{size.Height:0}.png"), size), "startup landscape capture");
                var window = new Window { Width = 1280, Height = 720, Content = front,
                    ShowInTaskbar = false, Position = new PixelPoint(-4000,-4000) };
                try
                {
                    window.Show(); Drain(window);
                    var startup = front.GetVisualDescendants().OfType<PrimeStartupScreen>().Single();
                    Check(startup.IsFocused && !shell.IsVisible && !shell.Overlays.IsOpen,
                        "startup owns focus and defers setup prompts");
                    shell.Router.Navigate(PrimeRoute.Settings);
                    Check(shell.Router.Current == PrimeRoute.News, "startup blocks shell routes");
                    FocusNavigator.Key(startup, Key.Escape); FocusNavigator.Key(startup, Key.E);
                    Check(startup.IsVisible && !startup.Leaving, "startup consumes back and tab navigation");
                    FocusNavigator.Key(startup, Key.Enter); Drain(window);
                    Check(shell.IsVisible && shell.IsEnabled && ReferenceEquals(header, shell.Header)
                        && !front.GetVisualDescendants().OfType<PrimeStartupScreen>().Any(),
                        "Enter reveals the same mounted shell and removes startup");
                    shell.Overlays.Clear();
                    shell.Router.Navigate(PrimeRoute.News);
                    FocusNavigator.Key(FocusNavigator.Ensure(shell)!, Key.Escape);
                    Drain(window);
                    Check(shell.Overlays.GetVisualDescendants().OfType<ConfirmScreen>().Any(),
                        "Escape on the main screen opens quit confirmation");
                    FocusNavigator.Key(FocusNavigator.Ensure(shell.Overlays)!, Key.Escape);
                    Drain(window);
                    Check(!shell.Overlays.IsOpen && shell.Router.Current == PrimeRoute.News,
                        "cancelling quit stays on the main screen");
                    var oldOffline = shell.Workspaces.Get(PrimeRoute.Offline);
                    var refreshedSettings = new MenuSettings { PointGoal = "25", TimeLimit = "10:00" };
                    startup.Continue(); front.Reset(refreshedSettings); Drain(window);
                    Check(shell.IsVisible && !front.GetVisualDescendants().OfType<PrimeStartupScreen>().Any(),
                        "repeat input and match return never reopen startup");
                    var newOffline = (OfflineWorkspace)shell.Workspaces.Get(PrimeRoute.Offline);
                    var settingsField = typeof(OfflineWorkspace).GetField("_settings",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    Check(!ReferenceEquals(oldOffline, newOffline)
                        && ReferenceEquals(settingsField.GetValue(newOffline), refreshedSettings),
                        "match return rebuilds offline settings against the freshly loaded object");
                }
                finally { window.Content = null; window.Close(); }
            }
            finally { LauncherPrefs.AutoUpdate = autoUpdate; }
        }
        private static void CheckTeamCycling()
        {
            var roster = RosterPacket.Create(); roster.Count = 3;
            for (int i = 0; i < 3; i++) { roster.Slots[i] = (byte)i; roster.Teams[i] = (sbyte)i; }
            var layout = new TeamLayout(3, 2, 1, 2);
            Check(LobbyPlayerRow.NextTeam(roster, 0, layout, 1) == 2, "team arrows skip full destinations");
            Check(LobbyPlayerRow.NextTeam(roster, 0, layout, -1) == 2, "previous team wraps");
            Check(LobbyPlayerRow.NextTeam(roster, 2, layout, 1) == 0, "next team wraps");
            Check(LobbyPlayerRow.NextTeam(roster, 0, new TeamLayout(3, 1, 1, 1), 1) == null,
                "full teams cannot be selected");
            Check(LobbyPlayerRow.NextTeam(roster, 7, layout, 1) == null, "departed player cannot change team");
            Check(LobbyPlayerRow.NextTeam(roster, 0, default, 1) == null, "FFA has no team destinations");
            roster.Teams[0] = -1;
            Check(LobbyPlayerRow.NextTeam(roster, 0, layout, 1) == 0, "unassigned player can choose first team");
            Check(LobbyPlayerRow.NextTeam(roster, 0, layout, -1) == 2, "unassigned previous chooses last team");
        }

        private static void Geometry(PrimeShell shell, Size size, PrimeRoute route)
        {
            foreach (var element in shell.GetVisualDescendants().OfType<Control>())
            {
                Check(double.IsFinite(element.Bounds.Width) && double.IsFinite(element.Bounds.Height)
                    && element.Bounds.Width >= 0 && element.Bounds.Height >= 0, "finite geometry: " + element.GetType().Name);
            }
            Check(shell.Header.Bounds.Height > 0 && shell.Footer.Bounds.Height > 0, "chrome visible " + route);
            if (route == PrimeRoute.Lobby)
            {
                var players = shell.GetVisualDescendants().OfType<LobbyPlayerRow>().ToArray();
                Check(players.Length == 8, "eight-player roster rendered");
                var bottom = players[^1].TranslatePoint(new Point(0, players[^1].Bounds.Height), shell);
                Check(bottom.HasValue && bottom.Value.Y < size.Height - 20, "eight-player roster fits");
                var rosterPanel = players[^1].GetVisualAncestors().OfType<PrimePanel>().First();
                var rosterBottom = players[^1].TranslatePoint(new Point(0, players[^1].Bounds.Height), rosterPanel);
                Check(rosterBottom.HasValue && rosterBottom.Value.Y <= rosterPanel.Bounds.Height,
                    "eight-player roster fits inside its panel above loadout");
            }
            string? cta = route switch { PrimeRoute.Play => "multiplayer.join", PrimeRoute.Offline => "offline.start",
                PrimeRoute.Lobby => "lobby.start", PrimeRoute.Settings => "settings.detail.save", _ => null };
            if (cta != null)
            {
                var control = ControllerNav.Find(shell, cta);
                Check(control != null && control.Bounds.Height > 0, "CTA laid out " + cta);
                Point? origin = control!.TranslatePoint(new Point(0,0), shell);
                Point? bottom = control!.TranslatePoint(new Point(control!.Bounds.Width, control.Bounds.Height), shell);
                Check(origin != null && bottom != null && origin.Value.Y >= 0 && origin.Value.X >= 0
                    && bottom.Value.Y <= size.Height - 15 && bottom.Value.X <= size.Width + 1, "CTA inside canvas " + cta);
            }
        }
    }
}
#endif
