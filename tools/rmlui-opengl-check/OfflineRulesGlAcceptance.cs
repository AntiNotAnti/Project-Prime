using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class OfflineRulesGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        ReplayFileAcceptance.Run();
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-offline-rules-rml42"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi GL framebuffer evidence with deterministic SYNTHETIC local rule data. Persistence is an in-memory fake; no real preferences, game files, network or match are touched.\n");
        var services = new FixtureServices();
        using var controller = new RmlUiMenuController(services);
        using var manager = new RmlUiPageManager(host);
        using var layout = new RmlUiLauncherPages(host, manager);
        host.Input.SetFramebufferScale(1, 1);
        long presented = 0;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline),
            "synthetic Offline route starts without a legacy handoff");
        Frame();
        RmlUiDocumentToken offline = layout.Document;
        Click("offline_rules_open", RmlUiIntentKind.OfflineOpenRules);
        RmlUiDocumentToken modal = manager.Top;
        Check(modal != offline && manager.ModalCount == 1
            && host.ReadField(modal, "offline_point_goal") == "7"
            && host.ReadField(modal, "offline_time_limit") == "7:00"
            && host.ReadField(modal, "offline_time_goal") == "3:00",
            "actual native rules modal receives controller field seeds");
        Check(RmlUiIntentRegistry.IsValid(RmlUiIntentKind.OfflineRuleToggle, 12)
            && RmlUiIntentRegistry.IsValid(RmlUiIntentKind.OfflineRuleToggle, 13)
            && !RmlUiIntentRegistry.IsValid(RmlUiIntentKind.OfflineRuleToggle, 14), "offline native action codec bounds retain Third Person and Movement only");
        Check(controller.Tick().Bindings["offline_rule13_label"].Text == "MOVEMENT // CLASSIC", "native modal seeds Classic movement label");
        Click("offline_rule_13", RmlUiIntentKind.OfflineRuleToggle, 13);
        Check(controller.OfflineRuleInputs?.MovementModel == "Momentum" && services.Rules.MovementModel == "Native", "native pointer selects Momentum in draft only");
        Capture("momentum-draft", atOrigin: false);
        Click("offline_rule_13", RmlUiIntentKind.OfflineRuleToggle, 13, keyboard: true);
        Check(controller.Tick().Bindings["offline_rule13_label"].Text == "MOVEMENT // QUAKE III", "focused native Enter selects Quake III");
        Click("offline_rule_13", RmlUiIntentKind.OfflineRuleToggle, 13, keyboard: true);
        Check(controller.Tick().Bindings["offline_rule13_label"].Text == "MOVEMENT // CLASSIC", "focused native Enter selects Classic");
        Click("offline_rule_13", RmlUiIntentKind.OfflineRuleToggle, 13);
        Click("offline_rule_12", RmlUiIntentKind.OfflineRuleToggle, 12);
        Click("offline_rule_12", RmlUiIntentKind.OfflineRuleToggle, 12, keyboard: true);
        Check(controller.OfflineRuleInputs?.Enabled(12) == false && controller.OfflineRuleInputs?.MovementModel == "Momentum", "Third Person bound12 remains independent of Movement13");
        Capture("rules");
        Click("offline_rule_8", RmlUiIntentKind.OfflineRuleToggle, 8);
        Click("offline_damage_value", RmlUiIntentKind.OfflineDamage);
        Check(controller.OfflineRuleInputs?.Enabled(8) == true && services.Saves == 0,
            "native toggle and damage clicks change only the draft");
        Fields("12", "7:65", "3:00");
        Click("offline_rules_apply", RmlUiIntentKind.OfflineApplyRules, expectedAccepted: false);
        Check(manager.Top == modal && Error().Contains("DURATIONS") && services.SaveAttempts == 0,
            "native Apply extracts actual fields and retains malformed duration error");
        Capture("invalid-duration-at-actions", atOrigin: false);
        Fields("12", "8:20", "3:00");
        services.FailSave = true;
        Click("offline_rules_apply", RmlUiIntentKind.OfflineApplyRules, expectedAccepted: false);
        Check(manager.Top == modal && Error().Contains("SYNTHETIC SAVE FAILURE") && services.Saves == 0,
            "native failed save preserves modal and visible retry error");
        Capture("save-failure-at-actions", atOrigin: false);
        services.FailSave = false;
        Click("offline_rules_apply", RmlUiIntentKind.OfflineApplyRules);
        Check(manager.ModalCount == 0 && !host.IsAlive(modal) && services.Saves == 1
            && services.Rules.PointGoal == "12" && services.Rules.TimeLimit == "8:20"
            && services.Rules.Enabled(8) && services.Rules.MovementModel == "Momentum", "native Apply commits the fake store exactly once and retires its modal");
        Capture("applied-offline");
        Check(!layout.HandleIntent(new(RmlUiIntentKind.OfflineApplyRules, 0, modal, ulong.MaxValue), out _)
            && services.Saves == 1, "retired native rules document cannot repeat Apply");
        Click("offline_rules_open", RmlUiIntentKind.OfflineOpenRules);
        Check(manager.Top != modal && host.ReadField(manager.Top, "offline_point_goal") == "12",
            "reopened native rules use newly committed field seeds");
        Check(controller.OfflineRuleInputs?.MovementModel == "Momentum"
            && controller.Tick().Bindings["offline_rule13_label"].Text == "MOVEMENT // MOMENTUM", "reopened modal reads saved Momentum");
        Click("offline_rule_13", RmlUiIntentKind.OfflineRuleToggle, 13, keyboard: true);
        Click("offline_rule_6", RmlUiIntentKind.OfflineRuleToggle, 6);
        Click("offline_rules_cancel", RmlUiIntentKind.OfflineCancelRules);
        Check(manager.ModalCount == 0 && services.Saves == 1 && !services.Rules.Enabled(6)
            && services.Rules.MovementModel == "Momentum" && layout.ReadField("offline_point_goal") == "",
            "actual Cancel click discards the edit and clears retired field cache");
        Click("offline_rules_open", RmlUiIntentKind.OfflineOpenRules);
        Check(controller.OfflineRuleInputs?.Enabled(6) == false
            && layout.Back(out var back) && back.Kind == RmlUiIntentKind.OfflineCancelRules
            && Dispatch(back), "production composed Back cancels the current native rules modal");
        Frame();
        Check(manager.ModalCount == 0 && services.Saves == 1,
            "Back releases native modal ownership without another save");
        Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors after native Offline Rules acceptance");
        Console.WriteLine("RML OFFLINE RULES GL synthetic-fixture screenshots: " + output);

        string Error() => controller.Tick().Bindings["offline_rules_error"].Text;

        void Fields(string points, string limit, string goal)
        {
            layout.SetField("offline_point_goal", points);
            layout.SetField("offline_time_limit", limit);
            layout.SetField("offline_time_goal", goal);
        }

        bool Dispatch(in RmlUiIntent intent)
        {
            bool accepted = controller.Dispatch(intent.Kind, intent.Argument,
                offlinePoints: intent.Kind == RmlUiIntentKind.OfflineApplyRules ? layout.ReadField("offline_point_goal") : "",
                offlineLimit: intent.Kind == RmlUiIntentKind.OfflineApplyRules ? layout.ReadField("offline_time_limit") : "",
                offlineGoal: intent.Kind == RmlUiIntentKind.OfflineApplyRules ? layout.ReadField("offline_time_goal") : "");
            if (accepted && intent.Kind == RmlUiIntentKind.OfflineOpenRules && controller.OfflineRuleInputs is { } seed)
                Fields(seed.PointGoal, seed.TimeLimit, seed.TimeGoal);
            return accepted;
        }

        void Frame()
        {
            var state = controller.Tick();
            if (state.Revision > presented)
            {
                foreach (var item in state.Bindings)
                {
                    if (item.Value.Kind == RmlUiBindingKind.Text) layout.SetText(item.Key, item.Value.Text);
                    else layout.SetBool(item.Key, item.Value.Boolean);
                }
                presented = state.Revision;
            }
            layout.Flush();
            host.Update();
            layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }

        void Settle(int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds);
            Frame();
        }

        void Click(string id, RmlUiIntentKind kind, int argument = 0, bool expectedAccepted = true, bool keyboard = false)
        {
            Frame();
            var document = manager.Top;
            Check(host.FocusDocument(document, id), "native focus/scroll reaches " + id);
            Settle(450);
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h),
                "actual native bounds resolve " + id);
            double px = x + w / 2, py = y + h / 2;
            Check(px >= 0 && px < width && py >= 0 && py < height,
                "native control is reachable inside framebuffer " + id);
            if (keyboard)
            {
                Check(host.FocusedElement() == id, "actual native keyboard focus remains on " + id);
                host.Input.Key(2, true); host.Input.Key(2, false);
            }
            else
            {
                host.Input.PointerButton(0, px, py, true);
                host.Input.PointerButton(0, px, py, false);
            }
            host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var raw))
            {
                if (!layout.HandleIntent(raw, out var forwarded) || forwarded.Kind == 0) continue;
                bool accepted = Dispatch(forwarded);
                if (forwarded.Kind != kind || forwarded.Argument != argument) continue;
                Check(accepted == expectedAccepted && forwarded.Document == document,
                    "true native " + (keyboard ? "Enter" : "pointer") + " dispatches " + id);
                Check(layout.HandleIntent(raw, out var duplicate) && duplicate == default,
                    "duplicate foreground native sequence rejected " + id);
                received = true;
            }
            Check(received, "native DOM emits expected action " + id);
            Settle(180);
        }

        void Capture(string name, bool atOrigin = true)
        {
            if (atOrigin)
            {
                host.Input.PointerMoved(width / 2, height / 2);
                host.Input.PointerWheel(1000);
                Settle(450);
            }
            Frame();
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC OFFLINE RULES - ACTUAL NATIVE GL FRAMEBUFFER\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Actual native Offline Rules framebuffer with synthetic in-memory service data; no real settings persistence or game assets.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("RML OFFLINE RULES GL PASS " + message);
    }

    private sealed class FixtureServices : IRmlUiMenuServices
    {
        public RmlUiOfflineRules Rules = new("7", "7:00", "3:00", "medium", 0);
        public bool FailSave;
        public int Saves, SaveAttempts;
        public HubSnapshot Hub() => new("SYNTHETIC FIXTURE", Hunter.Samus, Hunter.Samus, 0, true, "SYNTHETIC GL FIXTURE");
        public string BuildVersion() => "synthetic-offline-rules";
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false,
            "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public RmlUiOfflineRules? ReadOfflineRules() => Rules;
        public bool SaveOfflineRules(RmlUiOfflineRules expected, RmlUiOfflineRules next, out string error)
        {
            SaveAttempts++;
            error = FailSave ? "SYNTHETIC SAVE FAILURE // RETRY OR CANCEL" : Rules != expected ? "SYNTHETIC RULES CHANGED" : "";
            if (error.Length != 0) return false;
            Rules = next; Saves++; return true;
        }
        public IReadOnlyList<string> BuiltInArenaKeys() => new[] { "SYNTHETIC ARENA" };
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "FIXTURE", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public bool AcceptJoin(LaunchPlan plan) => false;
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument destination) => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool StartMatch() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "FIXTURE"; return false; }
        public void Quit() { }
    }
}
