using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using MphRead.Mods.Network;
using MphRead.Mods.Movement;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;

/// <summary>Authored native Lobby interaction regression; explicit fake authority and scoped text input, never a live lobby.</summary>
internal static class LobbyInteractionsGlAcceptance
{
    private const string Scope = "Actual production RmlUiHost, authored Lobby, composed LauncherPages, native pointer/key/scoped text input and controller dispatch. Synthetic local authority/start/chat only; no server, account, real chat or gameplay mutation. Physical framebuffer pointer coordinates; caller records native density. This does not assert physical Cocoa keyboard/IME delivery.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LOBBY_INTERACTIONS_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-lobby-interactions-rml88", width + "x" + height));
        Directory.CreateDirectory(output);
        var authority = new Authority(); var evidence = new List<object>();
        using var controller = new RmlUiMenuController(authority);
        using var layout = new RmlUiLauncherPages(host);
        long presented = -1;
        host.Input.SetFramebufferScale(1, 1);
        host.Input.DiagnosticsEnabled = true;
        try
        {
            Settle(); var lobby = layout.Document;
            Check(lobby != default && host.CurrentInputDocument == lobby, "actual composed Lobby owns foreground native input");
            Check(!host.FocusDocument(lobby, "lobby_start"), "authority-blocked Start is disabled in actual native DOM");
            Click("lobby_start"); Pump(0);
            Check(authority.Starts == 0, "pointer on disabled Start cannot issue a request");
            authority.CanStart = true; Settle();
            Check(host.FocusDocument(lobby, "lobby_start"), "verified owner state enables authored Start button");
            Click("lobby_start"); Pump(1, RmlUiIntentKind.LobbyStart);
            Check(authority.Starts == 1 && !authority.Starting && !Flag("lobby_starting"),
                "one pointer activation requests start without inventing authoritative Starting phase");
            host.Input.Key(2, true); host.Input.Key(2, false); host.Update(); Pump(1, RmlUiIntentKind.LobbyStart);
            Check(authority.Starts == 2 && !authority.Starting, "one Enter activation forwards exactly one second explicit start request");
            authority.Owner = false; Settle();
            Check(!host.FocusDocument(lobby, "lobby_start") && !controller.Dispatch(RmlUiIntentKind.LobbyStart)
                && authority.Starts == 2, "non-owner state cannot expose or request Start even with stale CanStart true");
            authority.Owner = true; authority.Starting = true; Settle();
            Check(!host.FocusDocument(lobby, "lobby_start") && !controller.Dispatch(RmlUiIntentKind.LobbyStart),
                "authoritative Starting state blocks duplicate match-start requests");
            authority.Starting = false; authority.CanStart = false; Settle(); Capture("start-authority-gates");

            // Do not programmatically focus or SetField this input: reproduce the player's first click.
            Click("lobby_chat_input"); Pump(0);
            bool chatScope = host.TryGetTextInputState(out var originalScope);
            evidence.Add(new { phase = "first-chat-pointer", focus = host.FocusedElement(), host.TextInputActive,
                chatScope, document = host.CurrentInputDocument, scope = originalScope, input = host.Input.Diagnostics });
            Check(host.FocusedElement() == "lobby_chat_input" && host.TextInputActive
                && chatScope && originalScope.Document == lobby,
                "actual first pointer click focuses lobby chat and activates scoped text input; focus="
                    + host.FocusedElement() + " active=" + host.TextInputActive + " scope=" + chatScope);
            Commit("Native chat é🙂");
            Check(layout.ReadField("lobby_chat_input", 4096) == "Native chat é🙂", "scoped committed Unicode reaches actual Lobby chat field");
            Commit("X"); host.Input.Key(RmlUiDesktopInput.TranslateKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backspace), true);
            host.Input.Key(RmlUiDesktopInput.TranslateKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backspace), false); host.Update();
            Check(layout.ReadField("lobby_chat_input", 4096) == "Native chat é🙂", "native Backspace edits the actual chat draft");
            authority.Revision++; Settle();
            Check(layout.ReadField("lobby_chat_input", 4096) == "Native chat é🙂" && host.FocusedElement() == "lobby_chat_input",
                "normal authoritative snapshot refresh preserves chat draft and text focus");
            MeasureChat(); Capture("typed-unicode-draft");
            host.Input.Key(2, true); host.Input.Key(2, false); host.Update(); Pump(1, RmlUiIntentKind.LobbyChatSend);
            Check(authority.Messages.SequenceEqual(new[] { "Native chat é🙂" }) && layout.ReadField("lobby_chat_input") == "",
                "Enter reads exact current Lobby draft once and clears only after accepted request");
            Check(!authority.ReadLobbyChrome().ChatHistory.Contains("Native chat", StringComparison.Ordinal),
                "accepted chat request never fabricates authoritative received history");
            Click("lobby_chat_input"); Pump(0); Commit("Pointer SEND second");
            Click("lobby_chat_send"); Pump(1, RmlUiIntentKind.LobbyChatSend);
            Check(authority.Messages.Count == 2 && authority.Messages[1] == "Pointer SEND second",
                "actual SEND pointer activation submits exactly the current second draft");

            Click("lobby_chat_input"); Pump(0); Commit("Retained private draft");
            Check(host.TryGetTextInputState(out var backgroundScope), "focused chat exposes current lifetime before modal");
            var modal = layout.Manager.OpenModal(RmlUiMenuPages.Rules); host.Update();
            Check(host.CurrentInputDocument == modal, "foreground native rules modal retires background input document");
            var stale = host.Input.Dispatch(new(backgroundScope.Document, RmlUiPlatformInputKind.TextCommitted,
                RmlUiInputDevice.Keyboard, backgroundScope.FocusEpoch, Text: "MUST NOT APPEAR"));
            Check(stale != RmlUiInputResult.Accepted && layout.ReadField("lobby_chat_input", 4096) == "Retained private draft",
                "late background text commit cannot enter hidden or covered Lobby field");
            Check(!host.FocusDocument(lobby, "lobby_chat_input"), "background Lobby cannot take focus from modal");
            Check(layout.Manager.CloseModal(), "rules modal closes through production page manager");
            Settle();
            Click("lobby_chat_input"); Pump(0);
            Check(host.TryGetTextInputState(out var replacementScope) && replacementScope.Document == lobby
                && replacementScope.FocusEpoch != backgroundScope.FocusEpoch,
                "returning actual pointer focus creates a fresh Lobby text lifetime");
            Check(layout.ReadField("lobby_chat_input", 4096) == "Retained private draft", "modal roundtrip preserves unsent draft");
            int end = RmlUiDesktopInput.TranslateKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys.End);
            host.Input.Key(end, true); host.Input.Key(end, false); host.Update();
            Commit(" again");
            Check(layout.ReadField("lobby_chat_input", 4096) == "Retained private draft again" && authority.Messages.Count == 2,
                "typing resumes after modal without submitting private draft");
            MeasureChat(); Capture("chat-after-modal");
            MovementRules();
            Check(authority.Starts == 2 && authority.Messages.Count == 2 && GL.GetError() == ErrorCode.NoError,
                "native interactions leave only explicit fake start/chat requests and no GL error");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }

        bool Flag(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;
        void Frame()
        {
            var snapshot = controller.Tick();
            if (snapshot.Revision > presented)
            {
                foreach (var pair in snapshot.Bindings)
                    if (pair.Value.Kind == RmlUiBindingKind.Text) layout.SetText(pair.Key, pair.Value.Text);
                    else layout.SetBool(pair.Key, pair.Value.Boolean);
                presented = snapshot.Revision;
            }
            layout.Flush(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds = 180)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds); }
        (float X, float Y, float W, float H) Box(string id)
        {
            Check(host.TryGetElementBounds(layout.Manager.Top, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0, "actual Lobby bounds exist: " + id);
            return (x, y, w, h);
        }
        void Click(string id)
        {
            Frame(); var b = Box(id);
            Check(b.X >= 0 && b.Y >= 0 && b.X + b.W <= width + 1 && b.Y + b.H <= height + 1,
                "native pointer target lies inside framebuffer: " + id);
            host.DiscardIntents(); double x = b.X + b.W / 2, y = b.Y + b.H / 2;
            host.Input.PointerMoved(x, y); host.Update();
            Console.WriteLine($"LOBBY POINTER {id} xy={x},{y} bounds={b} hover={host.HoveredElement()} action={host.HoveredAction()} focus={host.FocusedElement()} document={host.CurrentInputDocument}");
            host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false); host.Update();
        }
        void Pump(int expected, RmlUiIntentKind? kind = null, int? argument = null)
        {
            int count = 0;
            while (host.TryTakeIntent(out var intent))
            {
                Check((!kind.HasValue || intent.Kind == kind.Value) && (!argument.HasValue || intent.Argument == argument.Value), "native DOM emitted expected Lobby action and bounded argument");
                Check(layout.HandleIntent(intent, out var forwarded) && forwarded.Kind != 0,
                    "production composed-page routing forwards foreground Lobby action");
                string message = forwarded.Kind == RmlUiIntentKind.LobbyChatSend ? layout.ReadField("lobby_chat_input", 4096) : "";
                Check(controller.Dispatch(forwarded.Kind, forwarded.Argument, adminChat: message,
                    ruleTime: layout.ReadField("rules_time"), ruleGoal: layout.ReadField("rules_goal")), "production controller accepts current synthetic authority action");
                if (forwarded.Kind == RmlUiIntentKind.LobbyRulesOpen && controller.RuleInputs is { } seed)
                { layout.SetField("rules_time", seed.Time); layout.SetField("rules_goal", seed.Goal); }
                Check(layout.HandleIntent(intent, out var duplicate) && duplicate.Kind == 0,
                    "duplicate native sequence cannot repeat authority request");
                if (forwarded.Kind == RmlUiIntentKind.LobbyChatSend) layout.SetField("lobby_chat_input", "");
                count++;
            }
            Check(count == expected, "native activation request count matches expected " + expected); Settle();
        }
        void OpenRules()
        {
            Check(host.FocusDocument(layout.Manager.Top, "lobby_match_rules"), "actual native focus/scroll reaches Lobby Settings");
            Settle(450);
            Click("lobby_match_rules"); Pump(1, RmlUiIntentKind.LobbyRulesOpen);
        }
        void MovementRules()
        {
            Check(RmlUiIntentRegistry.IsValid(RmlUiIntentKind.LobbyRulesToggle, 16)
                && RmlUiIntentRegistry.IsValid(RmlUiIntentKind.LobbyRulesToggle, 17)
                && !RmlUiIntentRegistry.IsValid(RmlUiIntentKind.LobbyRulesToggle, 18), "online native action codec bounds retain Third Person16 and Movement17");
            OpenRules();
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "CLASSIC", "actual rules modal seeds Classic");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle);
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "MOMENTUM" && authority.Rules.Match.MovementModel == MovementModel.Native, "actual pointer changes only local Movement draft");
            Capture("momentum-rules-draft");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle, keyboard: true);
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "QUAKE III", "focused native Enter selects Quake III");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle, keyboard: true);
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "CLASSIC", "focused native Enter selects Classic");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle);
            RuleAction("rules_toggle16", RmlUiIntentKind.LobbyRulesToggle, keyboard: true);
            RuleAction("rules_toggle16", RmlUiIntentKind.LobbyRulesToggle);
            Check(controller.Tick().Bindings["rules_toggle16"].Text == "OFF" && controller.Tick().Bindings["rules_toggle17"].Text == "MOMENTUM", "existing Third Person16 stays independent of Movement17");
            RuleAction("rules_cancel", RmlUiIntentKind.LobbyRulesClose);
            Check(authority.RuleRequests == 0 && authority.Rules.Match.MovementModel == MovementModel.Native, "native Cancel discards Movement without an authority request");
            OpenRules();
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "CLASSIC", "cancel/reopen restores current server Classic");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle);
            RuleAction("rules_apply", RmlUiIntentKind.LobbyRulesApply);
            Check(authority.RuleRequests == 1 && authority.Rules.Match.MovementModel == MovementModel.Native
                && Flag("rules_pending"), "native Apply submits one draft and waits for synthetic authority confirmation");
            Capture("momentum-rules-pending");
            authority.ConfirmRules(); Settle();
            Check(!Flag("lobby_rules_open"), "synthetic authority revision confirms and closes submitted native rules");
            OpenRules();
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "MOMENTUM", "reopen adopts confirmed Momentum state");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle, keyboard: true);
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "QUAKE III", "confirmed Momentum advances to Quake III");
            RuleAction("rules_toggle17", RmlUiIntentKind.LobbyRulesToggle, keyboard: true);
            RuleAction("rules_apply", RmlUiIntentKind.LobbyRulesApply);
            authority.ConfirmRules(); Settle();
            OpenRules();
            Check(controller.Tick().Bindings["rules_toggle17"].Text == "CLASSIC" && authority.RuleRequests == 2, "keyboard Classic applies and reopens from confirmed state");
            Check(host.FocusDocument(layout.Manager.Top, "rules_toggle17"), "owner can position Movement before viewer permission change"); Settle(450);
            authority.Owner = false; authority.Revision++; Settle();
            Check(!host.FocusDocument(layout.Manager.Top, "rules_toggle17")
                && !host.FocusDocument(layout.Manager.Top, "rules_toggle16"), "viewer cannot focus disabled Movement or Third Person controls");
            Click("rules_toggle17"); Pump(0);
            Check(host.FocusDocument(layout.Manager.Top, "rules_cancel"), "viewer can focus and scroll to Cancel"); Settle(450);
            Check(!controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17) && authority.RuleRequests == 2,
                "disabled native viewer pointer and controller fence cannot edit Movement");
            Capture("movement-view-only");
            RuleAction("rules_cancel", RmlUiIntentKind.LobbyRulesClose);
            authority.Owner = true; authority.Revision++; Settle();
        }
        void RuleAction(string id, RmlUiIntentKind kind, bool keyboard = false)
        {
            Check(host.FocusDocument(layout.Manager.Top, id), "actual native focus/scroll reaches " + id); Settle(450);
            if (id == "rules_toggle17")
            {
                var movement = Box(id); var clip = Box("rules_columns");
                Check(movement.X >= clip.X - 1 && movement.Y >= clip.Y - 1
                    && movement.X + movement.W <= clip.X + clip.W + 1
                    && movement.Y + movement.H <= clip.Y + clip.H + 1,
                    "focused Movement full rectangle fits the actual rules scroll clip");
                evidence.Add(new { phase = "focused-movement-scroll-clip", movement, clip });
            }
            if (keyboard)
            {
                Check(host.FocusedElement() == id, "actual keyboard focus owns " + id);
                host.DiscardIntents(); host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
            }
            else Click(id);
            Pump(1, kind, id == "rules_toggle17" ? 17 : id == "rules_toggle16" ? 16 : null);
        }
        void Commit(string text)
        {
            Check(host.TryGetTextInputState(out var scope), "native committed text has a current focused field");
            Check(host.Input.Dispatch(new(scope.Document, RmlUiPlatformInputKind.TextCommitted,
                RmlUiInputDevice.Keyboard, scope.FocusEpoch, Text: text)) == RmlUiInputResult.Accepted,
                "platform-equivalent scoped text commit is accepted");
            host.Update();
        }
        void MeasureChat()
        {
            var panel = Box("lobby_chat_panel"); var input = Box("lobby_chat_input"); var send = Box("lobby_chat_send");
            Check(input.X >= panel.X && input.Y >= panel.Y && input.X + input.W <= panel.X + panel.W + 1
                && input.Y + input.H <= panel.Y + panel.H + 1 && send.Y + send.H <= panel.Y + panel.H + 1,
                "actual chat field and SEND fit inside the visible chat panel");
            Check(input.X + input.W <= send.X + 1, "chat field and SEND have separate pointer hit areas");
            evidence.Add(new { panel, input, send, physicalCoordinates = true });
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "synthetic-lobby-interactions-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "native Lobby interaction screenshot: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, layout.Manager.Top).RawJson);
            evidence.Add(new { name, path });
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "interactions-" + width + "x" + height + ".json"),
            JsonSerializer.Serialize(new { passed, scope = Scope, width, height, density = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY"), authority.Starts, authority.Messages, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("LOBBY INTERACTIONS GL PASS " + label); }
    private sealed class Authority : IRmlUiMenuServices
    {
        internal bool Owner = true, CanStart, Starting;
        internal int Starts, Revision;
        internal List<string> Messages = new();
        internal RmlUiRuleDraft Rules = new(7, 23, 1, new MatchDefinition {
            RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, PointGoal = 7, TimeLimitSeconds = 420 }, 0);
        internal RmlUiRuleDraft? PendingRules;
        internal int RuleRequests;
        internal void ConfirmRules()
        {
            if (PendingRules is not { } pending) throw new InvalidOperationException("No synthetic rule request to confirm.");
            Rules = pending with { SessionRevision = (ushort)(Rules.SessionRevision + 1) };
            PendingRules = null; Revision++;
        }
        public HubSnapshot Hub() => new("SYNTHETIC OWNER", Hunter.Samus, Hunter.Samus, 0, true, "NO NETWORK");
        public string BuildVersion() => "synthetic-lobby-interactions";
        public RmlUiLobbyView Lobby() => new(true, Starting, Owner, true, false, true, CanStart,
            "SYNTHETIC LOBBY", "MP1 SANCTORUS", "Battle", "Free for all", "SYNTHETIC REVISION " + Revision, 8,
            new[] { new RmlUiLobbyPlayer("SYNTHETIC OWNER", "SAMUS", false, true, false) });
        public RmlUiLobbyChrome ReadLobbyChrome() => new("7:00", "7 POINT GOAL", "NO RECEIVED MESSAGES", "SYNTHETIC REQUESTS ONLY", "MAP READY", "NO PREVIEW IN INPUT FIXTURE", "", "");
        public bool StartMatch() { if (!Owner || !CanStart || Starting) return false; Starts++; return true; }
        public bool SendLobbyChat(string text) { if (string.IsNullOrWhiteSpace(text)) return false; Messages.Add(text); return true; }
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "FIXTURE", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public bool AcceptJoin(LaunchPlan plan) => false;
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument route) => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => Rules;
        public IReadOnlyList<string> BuiltInArenaKeys() => Array.Empty<string>();
        public bool SubmitRules(RmlUiRuleDraft draft, out string error)
        {
            error = !Owner || Starting || draft.MatchId != Rules.MatchId || draft.AuthorityEpoch != Rules.AuthorityEpoch
                || draft.SessionRevision != Rules.SessionRevision ? "SYNTHETIC AUTHORITY CHANGED" : "";
            if (error.Length != 0) return false;
            PendingRules = draft; RuleRequests++; return true;
        }
        public void Quit() { }
    }
}
