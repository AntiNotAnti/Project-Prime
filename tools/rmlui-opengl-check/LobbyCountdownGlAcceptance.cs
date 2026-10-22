using System.Reflection;
using System.Text.Json;
using Avalonia.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native countdown and Shell ownership with synthetic session facts; no transport or match launch.</summary>
internal static class LobbyCountdownGlAcceptance
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static int RunWindow(string assets, int requestedWidth, int requestedHeight)
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Set isolated absolute PROJECT_PRIME_USER_DATA.");
        Directory.CreateDirectory(root);
        string output = Path.Combine(root, "lobby-countdown-evidence"); Directory.CreateDirectory(output);
        if (!GuiLauncher.EnsureSetup(requireDisplay: false)) throw new InvalidOperationException("Avalonia dispatcher unavailable.");
        GraphicsBackendPolicy.Configure("opengl");
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("LOBBY COUNTDOWN: " + label); checks++; Console.WriteLine("LOBBY COUNTDOWN PASS " + label); }
        object? Static(Type type, string name) => type.GetField(name, PrivateStatic)!.GetValue(null);
        void Set(Type type, string name, object? value) => type.GetField(name, PrivateStatic)!.SetValue(null, value);
        void Property(Type type, string name, object? value) => type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, value);
        var previous = new Dictionary<(Type, string), object?>();
        void Stage(Type type, string name, object? value) { previous.TryAdd((type, name), Static(type, name)); Set(type, name, value); }
        var oldSession = NetSession.ServerSession; var oldRole = NetSession.Role; bool oldActive = Shell.Active;
        StartScreen? front = null;
        using var window = new RenderWindow(shell: true);
        window.MinimumSize = new(64, 64);
        window.ClientSize = new(requestedWidth, requestedHeight);
        var sceneField = typeof(RenderWindow).GetField("_scene", PrivateInstance)!;
        object? oldScene = sceneField.GetValue(window);
        var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        using var host = new RmlUiHost();
        int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
        Check(host.Initialize(width, height, width / (float)Math.Max(1, window.ClientSize.X), assets), "real GL host initializes at measured framebuffer density");
        using var pages = new RmlUiPageManager(host);
        using var layout = new RmlUiLauncherPages(host, pages);
        layout.ShowBaseline(RmlUiMenuPage.Lobby);
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                front = new StartScreen(new MenuSettings(), Array.Empty<string>());
                typeof(StartScreen).GetField("_nativeStartupPending", PrivateInstance)!.SetValue(front, false);
                typeof(StartScreen).GetField("_bypassGuard", PrivateInstance)!.SetValue(front, true);
                front.Prime.Start(); front.Prime.Router.Navigate(PrimeRoute.Lobby);
            });
            Stage(typeof(Shell), "_window", window); Stage(typeof(Shell), "_front", front); Stage(typeof(Shell), "_matchLoading", false);
            Property(typeof(Shell), nameof(Shell.Active), true);
            Property(typeof(NetSession), nameof(NetSession.Role), NetRole.Client);
            Stage(typeof(RmlUiOpenGlSmoke), "_host", host); Stage(typeof(RmlUiOpenGlSmoke), "_pages", pages);
            Stage(typeof(RmlUiOpenGlSmoke), "_launcherPages", layout); Stage(typeof(RmlUiOpenGlSmoke), "_shown", false);
            var session = new SessionStatePacket { Phase = SessionPhase.Starting, Policy = ServerSessionPolicy.Lobby,
                StartStage = StartStage.Loading, ExpectedParticipants = 0b1011, LoadedParticipants = 0b1001,
                Match = new MatchDefinition { RoomKey = "SYNTHETIC COUNTDOWN MAP" } };
            void Authority() => Property(typeof(NetSession), nameof(NetSession.ServerSession), session);
            Authority(); sceneField.SetValue(window, null);
            Check(Shell.RmlUiMatchStartVisible && Shell.RmlUiCanPresent, "scene-free persistent Starting retains native Shell ownership");
            Check(RmlUiOpenGlSmoke.BeforeLauncherComposite(window), "native presentation suppresses legacy launcher compositor before local load");
            RmlUiOpenGlSmoke.Draw(window); host.Update();
            var document = (RmlUiDocumentToken)Static(typeof(RmlUiOpenGlSmoke), "_matchStartDocument")!;
            Check(document != default && host.CurrentInputDocument == document && RmlUiOpenGlSmoke.CapturesInput,
                "production Draw opens authored countdown as sole foreground native input owner");
            Check(NativeSemanticReadback.Read(host, document).ContainsText("LOADING WORLD"), "actual native DOM carries loading progress");
            Check(NativeSemanticReadback.Read(host, document).ContainsText("SYNTHETIC COUNTDOWN MAP"), "actual native DOM shows authoritative map identity");
            Check(host.TryGetElementBounds(document, "lobby_countdown_card", out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= width + 1 && y + h <= height + 1,
                "authored countdown card fits actual framebuffer");
            sceneField.SetValue(window, scene); Set(typeof(Shell), "_matchLoading", true);
            Check(Shell.RmlUiMatchStartVisible && Shell.RmlUiCanPresent && RmlUiOpenGlSmoke.BeforeLauncherComposite(window),
                "actual loaded Scene retains narrowly scoped native start ownership");
            int forwarded = 0;
            bool swallowed = (bool)typeof(RmlUiOpenGlSmoke).GetMethod("Forward", PrivateStatic)!.Invoke(null, new object[] { (Action<RmlUiHost>)(_ => forwarded++) })!;
            Check(swallowed && forwarded == 0, "production input forwarding consumes countdown input without dispatching background actions");
            session.StartStage = StartStage.Synchronizing; session.WorldReadyParticipants = 1; Authority();
            RmlUiOpenGlSmoke.Draw(window); host.Update();
            Check(NativeSemanticReadback.Read(host, document).ContainsText("SYNCHRONIZING WORLD"), "loaded-scene Draw refreshes actual native synchronization detail");
            GL.Finish(); Check(ScreenCapture.SaveWindow(width, height, Path.Combine(output, "loaded-scene-countdown.png")), "loaded-scene native countdown capture");
            session.StartStage = StartStage.Countdown; Authority();
            Stage(typeof(NetSession), "_countdownIdentity", new MatchStartIdentity(session.MatchId, session.AuthorityEpoch, session.StartGeneration));
            Stage(typeof(NetSession), "_startCountdownEndsAt", NetSession.Clock + 2.8);
            RmlUiOpenGlSmoke.Draw(window); host.Update();
            var countdownDom = NativeSemanticReadback.Read(host, document);
            Check(countdownDom.ContainsText("3") && countdownDom.ContainsText("READY // MATCH STARTING"),
                "native DOM reads the actual committed countdown clock");
            GL.Finish(); Check(ScreenCapture.SaveWindow(width, height, Path.Combine(output, "committed-countdown.png")), "committed countdown native capture");
            foreach (var (seconds, number) in new[] { (3d, "3"), (2.01d, "3"), (2d, "2"), (.01d, "1"), (0d, "READY") })
            {
                session.StartStage = StartStage.Countdown;
                var projection = RmlUiLobbyStartPresentation.Read(session, seconds);
                Check(projection.Number == number, "authoritative countdown boundary " + seconds + " projects " + number);
            }
            var loading = RmlUiLobbyStartPresentation.Read(session with { StartStage = StartStage.Loading }, 0);
            Check(loading.Number == "..." && loading.Detail.Contains("2/3"), "loading projection counts sparse participant masks");
            var sync = RmlUiLobbyStartPresentation.Read(session with { StartStage = StartStage.Synchronizing, WorldReadyParticipants = 0b11110001 }, 9);
            Check(sync.Number == "..." && sync.Detail.Contains("1/3"), "synchronization excludes ready bits outside expected participants and ignores stale countdown time");
            var expired = RmlUiLobbyStartPresentation.Read(session, 0);
            Check(expired.Detail == "WAITING FOR WORLD RELEASE", "expired countdown never invents gameplay release");
            Set(typeof(Shell), "_matchLoading", false);
            Check(!Shell.RmlUiMatchStartVisible && !Shell.RmlUiCanPresent, "loaded gameplay cannot admit launcher without Shell loading ownership");
            Set(typeof(Shell), "_matchLoading", true); session.Policy = ServerSessionPolicy.Continuous; Authority();
            Check(!Shell.RmlUiMatchStartVisible, "nonpersistent session cannot admit countdown surface");
            session.Policy = ServerSessionPolicy.Lobby; session.Phase = SessionPhase.InMatch; Authority();
            Check(Shell.RmlUiMatchStartVisible && RmlUiOpenGlSmoke.BeforeLauncherComposite(window),
                "committed phase change retains loaded native compositor until Shell releases its frame owner");
            RmlUiOpenGlSmoke.Draw(window);
            Check(host.IsAlive(document) && host.CurrentInputDocument == document,
                "final held native frame survives phase commit without legacy presentation gap");
            Set(typeof(Shell), "_matchLoading", false);
            Check(!Shell.RmlUiMatchStartVisible && !RmlUiOpenGlSmoke.BeforeLauncherComposite(window) && !RmlUiOpenGlSmoke.CapturesInput,
                "Shell loading owner release immediately retires launcher input ownership");
            RmlUiOpenGlSmoke.Draw(window);
            Check(!host.IsAlive(document), "production gameplay Draw closes previous countdown modal on release");
            Check(RmlUiLobbyStartPresentation.Read(session, 2).Number.Length == 0, "nonstarting phase has no countdown projection");
            File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new { checks, width, height,
                claim = "Actual RenderWindow, uninitialized loaded Scene, production Shell gates and native Draw with synthetic session authority and detached Avalonia front. No transport, account, match launch or full scene compositor loop." }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            typeof(RmlUiOpenGlSmoke).GetMethod("CloseNativeMatchStart", PrivateStatic)?.Invoke(null, null);
            foreach (var entry in previous.Reverse()) Set(entry.Key.Item1, entry.Key.Item2, entry.Value);
            Property(typeof(NetSession), nameof(NetSession.ServerSession), oldSession); Property(typeof(NetSession), nameof(NetSession.Role), oldRole);
            Property(typeof(Shell), nameof(Shell.Active), oldActive);
            sceneField.SetValue(window, oldScene);
            if (front != null) Dispatcher.UIThread.Invoke(front.Dispose);
        }
    }
}
