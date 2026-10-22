using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Updates;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Native update decisions only; no updater, browser, network or installer owner is constructed.</summary>
internal static class NativeUpdatePromptGlAcceptance
{
    private const string Scope = "Actual native update modal, authored responsive card, foreground pointer/Enter decisions, Back, modal coverage, duplicate/retired rejection, focus restoration and framebuffer. Versions are explicit synthetic labels. Decisions are observed only; no browser, HTTP, download, updater, installer, process launch or physical input/performance qualification.";

    internal static void Run(RmlUiHost host, int width, int height)
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Native update qualification requires explicit private PROJECT_PRIME_USER_DATA.");
        string density = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY") ?? "1";
        string output = Path.Combine(root, "native-update-prompt-gl", width + "x" + height + "-density-" + density);
        Directory.CreateDirectory(output);
        int checks = 0;
        bool passed = false;
        const string installedVersion = "SYNTHETIC-RML96-INSTALLED-BASELINE";
        const string availableVersion = "SYNTHETIC-RML96-NATIVE-OWNER-DO-NOT-INSTALL";
        var evidence = new List<object>();
        using var pages = new RmlUiPageManager(host);
        var baseline = host.HomeDocument;
        Check(host.FocusDocument(baseline, "nav_play"), "original launcher baseline accepts its Play focus");
        var parent = pages.OpenPage(new("update-fixture-parent", "pages/studio/native-entry.rml", "nav_play"));
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            Settle();
            foreach (string input in new[] { "pointer-confirm", "keyboard-later", "back", "covered-dispose" })
            {
                Check(host.FocusDocument(parent, "nav_play"), "underlying page owns known focus before " + input);
                using var prompt = new NativeUpdatePrompt(host, pages);
                prompt.Open(installedVersion, availableVersion);
                Settle();
                var document = prompt.Document;
                Check(pages.Top == document && host.CurrentInputDocument == document && pages.ModalCount == 1,
                    "update modal owns the exact foreground input document for " + input);
                var semantics = NativeSemanticReadback.Read(host, document);
                Check(semantics.ContainsText("UPDATE AVAILABLE") && semantics.ContainsText("LATER")
                    && semantics.ContainsText(OperatingSystem.IsMacOS() ? "OPEN RELEASE PAGE" : "UPDATE NOW"),
                    "native update heading and both platform-appropriate decision buttons are readable");
                var card = Box(document, "update_card");
                Check(card.X >= 0 && card.Y >= 0 && card.Right <= width + 1 && card.Bottom <= height + 1,
                    "native update card fits the measured framebuffer for " + input);
                Capture(input + "-initial", document);
                ReachVersions(document, card);
                Check(NativeSemanticReadback.Read(host, document).ContainsText(availableVersion),
                    "native available-version semantic status retains the complete long fixture tag");
                Capture(input + "-versions", document);
                ReachActions(document, card);
                Capture(input + "-actions", document);

                // A pointer over the real underlying header is caught by the
                // foreground modal; no background navigation enters the queue.
                var header = Box(parent, "nav_play");
                host.DiscardIntents();
                Click(header);
                Check(!host.TryTakeIntent(out _), "foreground modal blocks actual background Play pointer routing");
                var background = new RmlUiIntent(RmlUiIntentKind.Navigate,
                    (int)RmlUiRouteArgument.Home, parent, 1);
                Check(!pages.Accept(background) && !prompt.Handle(background) && !prompt.Closed,
                    "covered background document cannot dispatch navigation or decide the update");

                if (input == "back")
                {
                    prompt.Back();
                    Check(prompt.Closed && !prompt.Accepted, "Back records Later without an update side effect");
                    prompt.Back();
                    Check(!prompt.Handle(new(RmlUiIntentKind.SettingsApply, 0, document, 1)) && !prompt.Accepted,
                        "Back cannot be replaced by a delayed update confirmation");
                }
                else if (input == "covered-dispose")
                {
                    var cover = pages.OpenModal(new("update-fixture-cover", "pages/updates/available.rml", "update_later"));
                    Settle();
                    Check(!prompt.Handle(new(RmlUiIntentKind.SettingsApply, 0, document, 1)) && !prompt.Closed,
                        "covered update prompt rejects its delayed foreground decision");
                    prompt.Dispose();
                    Check(pages.Top == cover && pages.ModalCount == 2 && host.IsAlive(cover)
                        && prompt.Closed && !prompt.Accepted,
                        "disposing a covered update prompt cannot close another owner's top modal");
                    Check(!prompt.Handle(new(RmlUiIntentKind.SettingsApply, 0, document, 2)),
                        "disposed covered prompt rejects later confirmation");
                    Check(pages.CloseModal() && pages.Top == document && pages.CloseModal(),
                        "fixture explicitly retires its cover and already-disposed underlying modal");
                }
                else
                {
                    host.DiscardIntents();
                    bool confirm = input == "pointer-confirm";
                    if (confirm) Click(Box(document, "update_confirm"));
                    else
                    {
                        Check(host.FocusDocument(document, "update_later"), "native keyboard focus reaches Later");
                        host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                    }
                    Check(host.TryTakeIntent(out var intent) && intent.Document == document
                        && intent.Kind == (confirm ? RmlUiIntentKind.SettingsApply : RmlUiIntentKind.SettingsClose)
                        && !host.TryTakeIntent(out _), "actual " + input + " emits one exact update decision");
                    Check(!prompt.Handle(intent with { Kind = RmlUiIntentKind.Quit })
                        && !prompt.Handle(intent with { Sequence = 0 }) && !prompt.Closed,
                        "unrelated and sequence-zero intents cannot decide the update");
                    if (confirm)
                    {
                        pages.OpenModal(new("update-decision-cover", "pages/updates/available.rml", "update_later"));
                        host.Update();
                        Check(!prompt.Handle(intent) && !prompt.Closed,
                            "real queued pointer confirmation is refused while its prompt is covered");
                        Check(pages.CloseModal(), "test cover closes before foreground pointer decision");
                        host.Update();
                    }
                    Check(prompt.Handle(intent) && prompt.Closed && prompt.Accepted == confirm
                        && !prompt.Handle(intent), "foreground update decision is accepted exactly once for " + input);
                    prompt.Dispose();
                    Check(!prompt.Handle(intent), "retired update prompt rejects its actual delayed decision");
                }
                prompt.Dispose();
                Settle();
                Check(pages.Top == parent && pages.ModalCount == 0 && host.CurrentInputDocument == parent
                    && host.FocusedElement() == "nav_play" && !host.IsAlive(document),
                    "closing update prompt restores underlying document and exact focus for " + input);
                evidence.Add(new { input, prompt.Closed, prompt.Accepted, top = pages.Top.ToString(),
                    focus = host.FocusedElement() });
                host.DiscardIntents();
            }
            Check(pages.ClosePage(), "update fixture closes its underlying page");
            host.Update();
            Check(host.CurrentInputDocument == baseline && host.IsVisible(baseline) && host.FocusedElement() == "nav_play",
                "update fixture restores the original launcher baseline document and Play focus");
            Check(GL.GetError() == ErrorCode.NoError, "native update decisions leave no GL error");
            passed = true;
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(
                new { passed, checks, width, height, density, installedVersion, availableVersion, scope = Scope, evidence },
                new JsonSerializerOptions { WriteIndented = true }));
        }

        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException("NATIVE UPDATE GL: " + label); checks++; Console.WriteLine("NATIVE UPDATE GL PASS " + label); }
        void Frame()
        {
            host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds = 140)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds); }
        Rect Box(RmlUiDocumentToken document, string id)
        {
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0, "actual positive native bounds: " + id);
            return new(x, y, w, h);
        }
        void ReachVersions(RmlUiDocumentToken document, Rect card)
        {
            host.Input.PointerMoved(card.X + card.W / 2, card.Y + card.H / 2);
            host.Input.PointerWheel(10000); Settle(20);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var installed = Box(document, "update_installed");
                var available = Box(document, "update_available");
                Check(installed.X >= card.X - 1 && installed.Right <= card.Right + 1
                    && available.X >= card.X - 1 && available.Right <= card.Right + 1,
                    "both long version labels have positive actual widths inside the update card's horizontal edges");
                if (Contained(installed, card) && Contained(available, card))
                {
                    Check(true, "both complete native version-label boxes are contained inside the update card");
                    evidence.Add(new { document = document.ToString(), card, installed, available,
                        installedVersion, availableVersion, versionScrollSteps = attempt });
                    return;
                }
                host.Input.PointerMoved(card.X + card.W / 2, card.Y + card.H / 2);
                host.Input.PointerWheel(-1); Settle(12);
            }
            throw new InvalidOperationException("Native installed/available version labels cannot both be reached inside their scrolling card.");
        }
        void ReachActions(RmlUiDocumentToken document, Rect card)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var later = Box(document, "update_later"); var confirm = Box(document, "update_confirm");
                if (Contained(later, card) && Contained(confirm, card))
                {
                    Check(later.Right <= confirm.X + 1 || confirm.Right <= later.X + 1
                        || later.Bottom <= confirm.Y + 1 || confirm.Bottom <= later.Y + 1,
                        "both reachable native update buttons have separate contained bounds");
                    evidence.Add(new { document = document.ToString(), card, later, confirm, scrollSteps = attempt });
                    return;
                }
                host.Input.PointerMoved(card.X + card.W / 2, card.Y + card.H / 2);
                host.Input.PointerWheel(-3); Settle(12);
            }
            throw new InvalidOperationException("Native update action buttons cannot both be reached inside their scrolling card.");
        }
        void Click(Rect target)
        {
            float x = target.X + target.W / 2, y = target.Y + target.H / 2;
            host.Input.PointerMoved(x, y); host.Input.PointerButton(0, x, y, true);
            host.Input.PointerButton(0, x, y, false); host.Update();
        }
        void Capture(string label, RmlUiDocumentToken document)
        {
            Frame(); string path = Path.Combine(output, label + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "capture native update modal " + label);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, document).RawJson);
            evidence.Add(new { capture = path });
        }
    }

    private readonly record struct Rect(float X, float Y, float W, float H)
    { public float Right => X + W; public float Bottom => Y + H; }
    private static bool Contained(Rect target, Rect container) => target.X >= container.X - 1 && target.Y >= container.Y - 1
        && target.Right <= container.Right + 1 && target.Bottom <= container.Bottom + 1;
}
