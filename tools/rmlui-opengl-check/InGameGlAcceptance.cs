using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MphRead;
using MphRead.Formats;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class InGameGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-ingame-rml44"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi OpenGL framebuffer captures with an injected SYNTHETIC IInGameBackend only. No world, scene, network session, real bot, map-vote service or game account is created or mutated. Effects are inspected and never applied. This fixture does not establish live-scene acceptance.\n");
        var backend = new FixtureBackend();
        using var controller = new InGameController(backend);
        using var pages = new RmlUiPageManager(host);
        var story = CampaignGlFixture.Save();
        story.Logbook[29 >> 3] |= (byte)(1 << (29 & 7));
        var journal = CampaignJournal.Capture(story, new[]
        {
            new StringTableEntry("L027", 'L', "SYNTHETIC SECRET", "must never appear", 0, 'L'),
            new StringTableEntry("L029", 'L', "SYNTHETIC DISCOVERED", "Recorded observation only.\nSecond line.", 0, 'L')
        });
        using var page = new InGamePagePresenter(host, pages, controller, journal);
        host.Input.SetFramebufferScale(1, 1);
        page.Open();
        RmlUiDocumentToken document = page.Document;
        Frame();
        Check(page.Active && host.FocusedElement() == "ingame_resume",
            "native match menu opens against synthetic backend and focuses Resume");
        Capture("pause");

        Click("ingame_journal", InGameAction.Journal);
        var journalDocument = pages.Top;
        Check(journalDocument != document && host.FocusedElement() == "journal_close",
            "campaign journal opens over existing pause owner");
        Check(!page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Resume, document, ulong.MaxValue)),
            "foreground journal rejects background Resume");
        Click("journal_scans", InGameAction.JournalScans);
        var journalText = NativeSemanticReadback.Read(host, journalDocument);
        Check(journalText.ContainsText("#029 // LORE // SYNTHETIC DISCOVERED")
            && !journalText.ContainsText("SYNTHETIC SECRET"),
            "native logbook displays only actual discovered sparse scan ID");
        Check(journalText.ContainsText("Recorded observation only."),
            "native logbook preserves detached description");
        Check(!host.FocusDocument(journalDocument, "journal_next"), "single discovered scan disables Next");
        Capture("campaign-journal");
        Click("journal_category", InGameAction.JournalCategory);
        Click("journal_category", InGameAction.JournalCategory);
        Check(NativeSemanticReadback.Read(host, journalDocument).ContainsText("0 / 0")
            && !host.TryGetElementBounds(journalDocument, "journal_row0", out _, out _, out _, out _),
            "empty Bioform category clears previous discovered entry");
        Check(page.Back() && pages.Top == document && !host.IsAlive(journalDocument)
            && !page.TryTakeEffect(out _), "Back retires journal without resuming paused match");
        Check(!page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.JournalBack, journalDocument, ulong.MaxValue)),
            "retired journal cannot affect current pause");
        Check(backend.Executions.Count == 0, "journal navigation never mutates campaign or match authority");

        Click("ingame_resume", InGameAction.Resume);
        Effect(InGameEffect.Resume);
        Check(backend.Executions.Count == 0 && backend.Edits.Count == 0,
            "Resume transfers an effect without mutating a world or backend");
        Click("ingame_settings", InGameAction.Settings);
        Effect(InGameEffect.Settings);
        Check(backend.Executions.Count == 0, "Settings transfers its effect without opening an actual settings UI");

        Click("ingame_propose", InGameAction.Vote);
        Check(controller.Snapshot().Page == InGamePage.MapVote
            && controller.Snapshot().SelectedMap == "SYNTHETIC ARENA ALPHA",
            "native map proposal uses only injected map choices");
        Click("ingame_map", InGameAction.MapNext);
        Capture("map-vote");
        Click("ingame_vote_submit", InGameAction.VoteSubmit);
        Check(backend.Executions.Count == 1 && backend.Executions[0]
            == (InGameAction.VoteSubmit, "SYNTHETIC ARENA BETA"),
            "native proposal sends the selected synthetic arena only to the fake backend");
        Effect(InGameEffect.Resume);
        backend.Facts = backend.Facts with { VoteActive = true, VotePrompt = "SYNTHETIC MAP BALLOT", VoteTally = "YES 1 / NO 0" };
        Frame();
        Click("ingame_vote_yes", InGameAction.VoteYes);
        Effect(InGameEffect.Resume);
        Check(backend.Executions[^1].Action == InGameAction.VoteYes
            && !host.FocusDocument(document, "ingame_vote_yes"),
            "actual vote click consults refreshed fake facts and disables answered vote");

        Click("ingame_bots", InGameAction.Bots);
        Click("ingame_bot_hunter", InGameAction.BotHunter);
        Click("ingame_bot_suit", InGameAction.BotSuit);
        Click("ingame_bot_skill", InGameAction.BotSkill);
        Click("ingame_bot_team", InGameAction.BotTeam);
        InGameBot draft = controller.Snapshot().BotDraft;
        Check(backend.Edits.Count == 0 && draft.Slot == 2 && draft.Generation == 7
            && draft.Hunter == Hunter.Kanden && draft.Suit == 1 && draft.Skill == 2,
            "owner bot controls edit a local draft without touching fake or real roster");
        Capture("bot-draft");
        Click("ingame_bot_apply", InGameAction.BotApply);
        Check(backend.Edits.Count == 1 && backend.Edits[^1] == (InGameAction.BotApply, draft),
            "Apply submits the exact selected slot, generation and draft to injected authority");
        Click("ingame_bot_handicap", InGameAction.BotHandicap);
        Check(backend.Edits.Count == 2 && backend.Edits[^1].Action == InGameAction.BotHandicap
            && backend.Edits[^1].Bot.Handicap > draft.Handicap,
            "damage-reduction button submits a separate fake server command");
        Click("ingame_bot_add", InGameAction.BotAdd);
        Check(backend.Edits.Count == 3 && backend.Edits[^1].Action == InGameAction.BotAdd,
            "Add Bot submits its draft without creating a real bot");
        Click("ingame_bot_remove", InGameAction.BotRemove);
        RmlUiDocumentToken removedModal = pages.Top;
        Check(removedModal != document && backend.Edits.Count == 3,
            "bot removal opens native confirmation before any fake authority command");
        Capture("bot-remove-confirmation");
        Click("ingame_cancel", InGameAction.Cancel);
        Check(pages.Top == document && !host.IsAlive(removedModal) && backend.Edits.Count == 3,
            "native cancel retires bot-removal modal without issuing a command");
        Click("ingame_bot_remove", InGameAction.BotRemove);
        Click("ingame_confirm", InGameAction.Confirm);
        Check(backend.Edits.Count == 4 && backend.Edits[^1].Action == InGameAction.BotRemove
            && backend.Edits[^1].Bot.Slot == 2 && backend.Edits[^1].Bot.Generation == 7,
            "confirmed native removal carries exact fake slot generation");
        Check(!page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Confirm,
            removedModal, ulong.MaxValue)) && backend.Edits.Count == 4,
            "retired confirmation document cannot remove a bot again");
        Click("ingame_bot_back", InGameAction.Cancel);

        foreach (var operation in new[]
            { (Id: "ingame_leave", Action: InGameAction.Leave, Name: "leave"),
                (Id: "ingame_quit", Action: InGameAction.Quit, Name: "quit"),
                (Id: "ingame_return", Action: InGameAction.ReturnLobby, Name: "return-lobby") })
        {
            int before = backend.Executions.Count;
            Click(operation.Id, operation.Action);
            RmlUiDocumentToken modal = pages.Top;
            Check(modal != document && controller.Snapshot().Confirmation == operation.Action
                && backend.Executions.Count == before,
                "native " + operation.Name + " opens confirmation before authority action");
            Check(!page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Confirm, document, ulong.MaxValue))
                && !page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Confirm, host.HomeDocument, ulong.MaxValue))
                && backend.Executions.Count == before,
                "background and wrong document cannot answer native " + operation.Name + " confirmation");
            Capture(operation.Name + "-confirmation");
            Click("ingame_cancel", InGameAction.Cancel);
            Check(!host.IsAlive(modal) && backend.Executions.Count == before,
                "native " + operation.Name + " cancellation preserves injected session state");
            Check(!page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Confirm, modal, ulong.MaxValue)),
                "cancelled native " + operation.Name + " modal rejects late confirmation");
            Click(operation.Id, operation.Action);
            Click("ingame_confirm", InGameAction.Confirm);
            Check(backend.Executions.Count == before + 1 && backend.Executions[^1].Action == operation.Action,
                "native " + operation.Name + " executes once through injected backend only");
            Effect(InGameEffect.Resume);
        }

        backend.Facts = backend.Facts with { Owner = false, CanManageBots = false, CanAddBot = false };
        Frame();
        Check(!host.FocusDocument(document, "ingame_bots") && !host.FocusDocument(document, "ingame_return"),
            "fresh non-owner facts disable owner bot and return-to-lobby controls");
        Capture("non-owner");
        page.Dispose();
        Check(!host.IsAlive(document)
            && !page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Resume, document, ulong.MaxValue)),
            "retired pause page rejects late native intents");
        Check(GL.GetError() == ErrorCode.NoError, "native in-game synthetic fixture leaves no OpenGL error");
        Console.WriteLine("RML INGAME GL synthetic-only screenshots: " + output);
        CapabilityFaces(host, width, height, output);

        void Effect(InGameEffect expected)
        {
            Check(page.TryTakeEffect(out InGameEffect effect) && effect == expected && !page.TryTakeEffect(out _),
                "presenter transfers " + expected + " effect exactly once without applying it");
        }
        void Frame()
        {
            page.Refresh();
            host.Update();
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
        void Click(string id, InGameAction expected)
        {
            Frame();
            RmlUiDocumentToken target = pages.Top;
            Check(host.FocusDocument(target, id), "actual native focus reaches " + id);
            Settle(450);
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h),
                "native bounds resolve " + id);
            double px = x + w / 2, py = y + h / 2;
            Check(px >= 0 && px < width && py >= 0 && py < height,
                "native button center is reachable in framebuffer: " + id);
            host.Input.PointerButton(0, px, py, true);
            host.Input.PointerButton(0, px, py, false);
            host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.Handle(intent);
                if (intent.Kind != RmlUiIntentKind.InGameAction || intent.Argument != (int)expected) continue;
                Check(accepted && intent.Document == target, "actual native pointer dispatches " + id);
                Check(!page.Handle(intent), "duplicate native sequence is rejected for " + id);
                received = true;
            }
            Check(received, "native button emits expected in-game action: " + id);
            Settle(180);
        }
        void Capture(string name)
        {
            host.Input.PointerMoved(width / 2, height / 2);
            host.Input.PointerWheel(1000);
            Settle(450);
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC INGAME BACKEND - ACTUAL NATIVE GL FRAMEBUFFER\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Actual native GL framebuffer with injected synthetic match-menu backend. No scene or world acceptance is claimed.");
        }
    }

    // These cases inspect injected facts and menu capability admission only.
    // They never pause a scene, change replay transport or toggle a real window.
    private static void CapabilityFaces(RmlUiHost host, int width, int height, string output)
    {
        int checks = 0;
        foreach (var example in new[]
        {
            (Name: "local-running", Live: false, Replay: false, Paused: false, Fullscreen: false,
                Title: "LOCAL MENU", Session: "LOCAL SESSION // GAMEPLAY CONTINUES"),
            (Name: "local-paused", Live: false, Replay: false, Paused: true, Fullscreen: false,
                Title: "MATCH PAUSED", Session: "LOCAL SESSION // PAUSED"),
            (Name: "replay-running", Live: false, Replay: true, Paused: false, Fullscreen: true,
                Title: "REPLAY MENU", Session: "REPLAY SESSION // TRANSPORT UNCHANGED"),
            (Name: "replay-paused", Live: false, Replay: true, Paused: true, Fullscreen: false,
                Title: "REPLAY PAUSED", Session: "LOCAL SESSION // PAUSED"),
            (Name: "live-continues", Live: true, Replay: false, Paused: true, Fullscreen: true,
                Title: "LIVE MATCH", Session: "NETWORK SESSION // GAMEPLAY CONTINUES")
        })
        {
            var backend = new FixtureBackend();
            backend.Facts = backend.Facts with
            {
                Live = example.Live, Replay = example.Replay, Spectating = false, CanSpectate = false,
                Owner = false, Persistent = false, CanManageBots = false, CanAddBot = false,
                VoteActive = false, VoteAnswered = false, VotePrompt = "", VoteTally = "", VoteUnavailable = "",
                Bots = ImmutableArray<InGameBot>.Empty
            };
            using var controller = new InGameController(backend);
            using var pages = new RmlUiPageManager(host);
            using var page = new InGamePagePresenter(host, pages, controller,
                offerFullscreen: example.Fullscreen, localSessionPaused: example.Paused);
            page.Open(); Settle(); var document = page.Document;
            var text = NativeSemanticReadback.Read(host, document);
            if (!text.ContainsText(example.Title) || !text.ContainsText(example.Session))
            {
                Console.WriteLine($"RML INGAME CAPABILITY SEMANTIC FAILURE {example.Name} expectedTitle=[{example.Title}] expectedSession=[{example.Session}] title=[{text.Label("ingame_title")}] session=[{text.Label("ingame_session")}] focused={host.FocusedElement()}");
                foreach (string id in new[] { "ingame_title", "ingame_session", "ingame_resume" })
                {
                    bool found = host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h);
                    Console.WriteLine($"RML INGAME CAPABILITY BOUNDS {id} found={found} bounds={x},{y},{w},{h} framebuffer={width}x{height}");
                }
                var nodes = ((string Id, string Label)[])typeof(NativeSemanticReadback)
                    .GetField("_nodes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(text)!;
                foreach (var node in nodes) Console.WriteLine($"RML INGAME CAPABILITY SEMANTIC NODE [{node.Id}] [{node.Label}]");
                Frame();
                byte[] failedPixels = FinalCompositeCapture.Read(width, height);
                string failedPath = Path.Combine(output, $"synthetic-capability-preassert-failure-{example.Name}-{width}x{height}.ppm");
                using (var stream = File.Create(failedPath))
                {
                    stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC INGAME CAPABILITY ASSERTION DIAGNOSTIC\n{width} {height}\n255\n"));
                    for (int row = height - 1; row >= 0; row--) stream.Write(failedPixels, row * width * 3, width * 3);
                }
                FinalCompositeCapture.WriteEvidence(failedPath, width, height,
                    "Pre-assert framebuffer for unchanged synthetic local/replay semantic assertion; diagnostic labels/bounds logged, no scene/pause/transport/window effects.");
            }
            Verify(text.ContainsText(example.Title) && text.ContainsText(example.Session),
                "native title/session reflect injected facts without claiming a physical pause: " + example.Name);
            Verify(host.TryGetElementBounds(document, "ingame_session", out _, out float sessionY, out _, out float sessionHeight)
                && host.TryGetElementBounds(document, "ingame_resume", out _, out float resumeY, out _, out _)
                && sessionY + sessionHeight <= resumeY + 0.5f,
                "actual session caption ends before Resume begins: " + example.Name);
            Verify(host.FocusDocument(document, "ingame_replay") == example.Replay
                && host.FocusDocument(document, "ingame_record") == example.Live
                && host.FocusDocument(document, "ingame_propose") == example.Live,
                "replay and live-only transport/record/vote controls match captured facts: " + example.Name);
            Verify(!host.FocusDocument(document, "ingame_bots") && !host.FocusDocument(document, "ingame_return")
                && !host.FocusDocument(document, "ingame_spectate") && !host.FocusDocument(document, "ingame_rejoin"),
                "unavailable ownership/spectator capabilities cannot receive native focus: " + example.Name);
            Verify(backend.Executions.Count == 0 && backend.Edits.Count == 0,
                "capability projection does not execute fake or real gameplay authority: " + example.Name);
            if (example.Replay)
            {
                Click("ingame_replay", InGameAction.Replay);
                Verify(page.TryTakeEffect(out var effect) && effect == InGameEffect.Replay
                    && !page.TryTakeEffect(out _) && backend.Executions.Count == 0,
                    "native replay controls transfer one effect without changing transport: " + example.Name);
            }
            Verify(host.FocusDocument(document, "ingame_fullscreen") == example.Fullscreen,
                "native fullscreen focus follows the supplied platform capability: " + example.Name);
            if (example.Fullscreen)
            {
                Click("ingame_fullscreen", InGameAction.Fullscreen);
                Verify(backend.Executions.Count == 1 && backend.Executions[0].Action == InGameAction.Fullscreen,
                    "offered fullscreen delegates once only to the synthetic backend: " + example.Name);
                Verify(page.TryTakeEffect(out var effect) && effect == InGameEffect.Resume,
                    "fullscreen synthetic response transfers its normal completion effect: " + example.Name);
            }
            else
            {
                Verify(!host.TryGetElementBounds(document, "ingame_fullscreen", out _, out _, out _, out _)
                    && !page.Handle(new(RmlUiIntentKind.InGameAction, (int)InGameAction.Fullscreen,
                        document, ulong.MaxValue)) && backend.Executions.Count == 0,
                    "hidden fullscreen rejects direct native admission before any backend call: " + example.Name);
            }
            Frame();
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-capability-{example.Name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC CAPABILITIES ONLY - NO PHYSICAL PAUSE OR TRANSPORT ACCEPTANCE\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Native labels/capabilities driven only by synthetic facts and supplied pause/fullscreen flags; no actual pause, replay transport, OS fullscreen or world effects.");
            foreach (string footer in new[] { "ingame_leave", "ingame_quit" })
            {
                Verify(host.FocusDocument(document, footer), "native footer remains focusable: " + example.Name + " // " + footer);
                Settle();
                Verify(host.TryGetElementBounds(document, footer, out float fx, out float fy, out float fw, out float fh)
                    && fx >= -0.5f && fy >= -0.5f && fx + fw <= width + 0.5f && fy + fh <= height + 0.5f,
                    "actual footer can scroll fully inside framebuffer: " + example.Name + " // " + footer);
            }

            void Frame()
            {
                page.Refresh(); host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
                GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
                RmlUiOpenGlFrame.Draw(host, width, height);
            }
            void Settle()
            {
                var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < 450);
            }
            void Click(string id, InGameAction action)
            {
                Verify(host.FocusDocument(document, id), "capability control receives native focus: " + id); Settle();
                Verify(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h),
                    "capability control resolves actual bounds: " + id);
                double px = x + w / 2d, py = y + h / 2d;
                Verify(px >= 0 && px < width && py >= 0 && py < height,
                    "capability native pointer is within framebuffer: " + id);
                host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
                Verify(host.TryTakeIntent(out var intent) && intent.Document == document
                    && intent.Kind == RmlUiIntentKind.InGameAction && intent.Argument == (int)action
                    && page.Handle(intent) && !page.Handle(intent) && !host.TryTakeIntent(out _),
                    "one actual native capability action is accepted and duplicate refused: " + id);
                Settle();
            }
        }
        Verify(GL.GetError() == ErrorCode.NoError, "synthetic local/replay capability cases leave no GL error");
        Console.WriteLine($"RML INGAME CAPABILITY GL {checks} checks passed; injected facts and fake backend only");

        void Verify(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            ++checks; Console.WriteLine("RML INGAME CAPABILITY GL PASS " + label);
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML INGAME GL PASS " + label);
    }

    private sealed class FixtureBackend : IInGameBackend
    {
        internal InGameFacts Facts = new(true, false, false, true, false, true, true, false,
            false, false, "SYNTHETIC MATCH // NO LIVE SERVICE", "", "")
        {
            CanManageBots = true, CanAddBot = true,
            Bots = ImmutableArray.Create(new InGameBot(2, 7, Hunter.Samus, 0, 1, 0, 0)),
            BotHunters = ImmutableArray.Create(Hunter.Samus, Hunter.Kanden),
            Teams = ImmutableArray.Create(new InGameTeam(0, 2, 4), new InGameTeam(1, 1, 4)),
            CommandStatus = "SYNTHETIC OWNER AUTHORITY"
        };
        internal readonly List<(InGameAction Action, string Map)> Executions = new();
        internal readonly List<(InGameAction Action, InGameBot Bot)> Edits = new();
        public InGameFacts Capture() => Facts;
        public ImmutableArray<string> Maps() => ImmutableArray.Create("SYNTHETIC ARENA ALPHA", "SYNTHETIC ARENA BETA");
        public string Execute(InGameAction action, string map)
        {
            Executions.Add((action, map));
            if (action is InGameAction.VoteYes or InGameAction.VoteNo) Facts = Facts with { VoteAnswered = true };
            return "";
        }
        public string EditBot(InGameAction action, InGameBot bot)
        {
            Edits.Add((action, bot));
            return "";
        }
    }
}
