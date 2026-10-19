using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Production composed pages and native inputs; deterministic discovery fixture, no network or game session.</summary>
internal static class RandomMatchGlAcceptance
{
    private const string Scope = "Actual production composed Home/Play pages, native pointer activation, foreground page routing, controller search/cancel epochs and final GL framebuffer. Fake held discovery and monotonic clock only; no network, server admission, account writes or gameplay. Does not qualify physical Cocoa input delivery.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_RANDOM_MATCH_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-random-match", width + "x" + height));
        Directory.CreateDirectory(output);
        var services = new Services(); long clock = 1000;
        using var controller = new RmlUiMenuController(services,
            token => Task.Delay(Timeout.Infinite, token), () => clock);
        using var layout = new RmlUiLauncherPages(host);
        var captures = new List<string>(); long shown = -1; int checks = 0;
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            Settle();
            var home = NativeSemanticReadback.Read(host, layout.Document);
            Check(layout.Page == RmlUiMenuPage.Home && home.ContainsText("MODE")
                && home.ContainsText("ONLINE") && home.ContainsText("MATCHMAKING"),
                "composed Home exposes MODE ONLINE MATCHMAKING labels");
            Capture("home");
            Click("activity_selector"); Pump(1, null);
            Click("drawer_browser"); Pump(1, RmlUiIntentKind.StageSelect);
            Click("deploy"); Pump(1, RmlUiIntentKind.PlayBrowse);
            Check(layout.Page == RmlUiMenuPage.Play && Flag("play_browser_mode") && !controller.Busy,
                "native Home selection and deployment enter ready Play browser");
            Check(NativeSemanticReadback.Read(host, layout.Document).ContainsText("RANDOM MATCH"),
                "authored Play exposes RANDOM MATCH");
            Click("play_quick"); Pump(1, RmlUiIntentKind.PlayQuick);
            clock += 7000; Settle();
            Check(services.Scans == 1 && !services.Scan.Task.IsCompleted && controller.Busy
                && controller.Joining && Flag("play_random_search"),
                "native RANDOM MATCH starts continuing controller search with held discovery");
            Check(NativeSemanticReadback.Read(host, layout.Document).ContainsText("7s")
                && NativeSemanticReadback.Read(host, layout.Document).ContainsText("CANCEL SEARCH"),
                "actual search frame shows elapsed time and CANCEL SEARCH");
            foreach (string id in new[] { "play_quick", "play_create_open", "play_refresh", "play_join", "play_player_name", "play_join_address" })
                Check(!host.FocusDocument(layout.Document, id), "search disables native competing control " + id);
            foreach (string id in new[] { "play_quick", "play_create_open", "play_refresh", "play_join" })
            { Click(id); Pump(0, null); }
            Check(services.Scans == 1 && services.Joins == 0 && services.Creates == 0,
                "disabled native controls cannot duplicate search create or direct join");
            Capture("search");
            var retired = services.Scan;
            Click("play_cancel_search"); Pump(1, RmlUiIntentKind.PlayCancel);
            Check(services.ScanToken.IsCancellationRequested && layout.Page == RmlUiMenuPage.Play
                && Flag("play_browser_mode") && !Flag("play_random_search") && !controller.Joining && !controller.Busy,
                "native cancel retires search and keeps ready browser");
            retired.SetResult(new RmlUiServerCandidate("DELAYED FIXTURE", "127.0.0.1:1", 0, 8, 1, true));
            Settle();
            Check(services.Joins == 0 && services.Accepted == 0,
                "late cancelled discovery cannot create or accept a session");
            Check(host.FocusDocument(layout.Document, "play_quick"), "browser RANDOM MATCH becomes usable after cancellation");
            Capture("cancelled");
            services.Scan = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click("play_quick"); Pump(1, RmlUiIntentKind.PlayQuick);
            Check(services.Scans == 2 && Flag("play_random_search"), "second native search has a fresh live attempt");
            Click("play_cancel_search"); Pump(1, RmlUiIntentKind.PlayCancel);
            Check(layout.Page == RmlUiMenuPage.Play && !controller.Busy && services.ScanToken.IsCancellationRequested,
                "repeat search and cancel preserve usable browser");
            Check(GL.GetError() == ErrorCode.NoError, "native search interaction leaves no GL error");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }

        bool Flag(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException(label); checks++; Console.WriteLine("RANDOM MATCH GL PASS " + label); }
        void Frame()
        {
            var snapshot = controller.Tick();
            if (snapshot.Revision > shown)
            {
                foreach (var pair in snapshot.Bindings)
                    if (pair.Value.Kind == RmlUiBindingKind.Text) layout.SetText(pair.Key, pair.Value.Text);
                    else layout.SetBool(pair.Key, pair.Value.Boolean);
                shown = snapshot.Revision;
            }
            layout.Flush(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds = 180)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds); }
        void Click(string id)
        {
            Frame();
            Check(host.TryGetElementBounds(layout.Document, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= width + 1 && y + h <= height + 1,
                "native pointer target fits framebuffer " + id);
            host.DiscardIntents(); double px = x + w / 2, py = y + h / 2;
            host.Input.PointerMoved(px, py); host.Input.PointerButton(0, px, py, true);
            host.Input.PointerButton(0, px, py, false); host.Update();
        }
        void Pump(int expected, RmlUiIntentKind? kind)
        {
            int count = 0;
            while (host.TryTakeIntent(out var intent))
            {
                bool preview = intent.Kind == RmlUiIntentKind.StagePreview;
                Check(layout.HandleIntent(intent, out var forwarded), "production page manager handles native action");
                if (kind.HasValue && !preview) Check(forwarded.Kind == kind, "composed action forwards expected " + kind);
                if (forwarded.Kind != 0)
                    Check(controller.Dispatch(forwarded.Kind, forwarded.Argument,
                        address: layout.ReadField("play_join_address", 120),
                        playerName: layout.ReadField("play_player_name", 24)), "controller accepts forwarded native action");
                bool duplicateHandled = layout.HandleIntent(intent, out var duplicate);
                Check(!duplicateHandled || duplicate.Kind == 0, "duplicate native sequence cannot repeat action");
                if (!preview) count++;
            }
            Check(count == expected, "native request count matches " + expected); Settle();
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "random-match-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "native screenshot " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, layout.Manager.Top).RawJson);
            captures.Add(path);
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "random-match-summary.json"),
            JsonSerializer.Serialize(new { passed, checks, width, height, scope = Scope, services.Scans,
                services.Joins, services.Creates, services.Accepted, captures, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class Services : IRmlUiMenuServices
    {
        internal TaskCompletionSource<RmlUiServerCandidate?> Scan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken ScanToken;
        internal int Scans, Joins, Creates, Accepted;
        public HubSnapshot Hub() => new("SYNTHETIC HUNTER", Hunter.Samus, Hunter.Samus, 0, true, "NO NETWORK");
        public string BuildVersion() => "synthetic-random-match";
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false,
            "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(
            new RmlUiDirectory(true, "SYNTHETIC EMPTY DIRECTORY", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token)
        { Scans++; ScanToken = token; return Scan.Task; }
        public Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token)
        { Joins++; return Task.FromResult(new RmlUiJoinResult(false, default, "NO NETWORK")); }
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token)
        { Creates++; return Task.FromResult(new RmlUiJoinResult(false, default, "NO NETWORK")); }
        public bool AcceptJoin(LaunchPlan plan) { Accepted++; return false; }
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument route) => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool StartMatch() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public IReadOnlyList<string> BuiltInArenaKeys() => Array.Empty<string>();
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "NO NETWORK"; return false; }
        public void Quit() { }
    }
}
