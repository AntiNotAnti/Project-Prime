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
using MphRead.Mods.Launcher.RmlUi.Pages.License;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual composed pages and native license overlay, synthetic career/account facts only.</summary>
internal static class LicenseCardGlAcceptance
{
    private const string Scope = "Actual production composed Home/Lobby, profile command, native license modal and final GL framebuffer. Synthetic career/account reader and local authority only; no network, account writes, real lobby or gameplay. Does not qualify physical Cocoa keyboard/IME delivery.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LICENSE_CARD_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-license-card", width + "x" + height));
        Directory.CreateDirectory(output);
        var services = new Services(); var reader = new Reader();
        bool requireOwnedPortrait = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LICENSE_CARD_OWNED_PORTRAIT") == "1";
        bool starCheck = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LICENSE_STAR_CHECK") == "1";
        using var controller = new RmlUiMenuController(services);
        using var layout = new RmlUiLauncherPages(host);
        NativeLicenseOverviewPage? page = null;
        long shown = -1; int checks = 0;
        var captures = new List<string>();
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            foreach (bool lobbyMode in new[] { false, true })
            {
                services.InLobby = lobbyMode;
                Until(() => controller.Tick().Bindings.TryGetValue("lobby_mode", out var mode) && mode.Boolean == lobbyMode,
                    "synthetic authority projects the requested parent route");
                Settle();
                RmlUiDocumentToken parent = layout.Document;
                Check(host.CurrentInputDocument == parent && host.IsVisible(parent), "composed parent owns input before profile");
                RmlUiPlatformInputEvent? delayedText = null;
                if (lobbyMode)
                {
                    layout.SetField("lobby_chat_input", "Private draft survives license");
                    Click(parent, "lobby_chat_input", focus: false);
                    Check(host.TryGetTextInputState(out var oldScope), "Lobby draft has a native input lifetime before opening license");
                    delayedText = new(oldScope.Document, RmlUiPlatformInputKind.TextCommitted,
                        RmlUiInputDevice.Keyboard, oldScope.FocusEpoch, Text: "MUST NOT APPEAR");
                }
                Click(parent, "profile", focus: false);
                Check(host.TryTakeIntent(out var open) && open.Kind == RmlUiIntentKind.LicenseOpen
                    && (int)open.Kind == 191 && open.Argument == 0
                    && RmlUiIntentRegistry.ToLegacy(open) == "license:open", "actual profile pointer emits distinct license:open command");
                Check(!host.TryTakeIntent(out _), "profile pointer emits exactly one command");
                Check(Open(open), "foreground profile opens license overlay");
                var document = layout.Manager.Top;
                Check(document != parent && layout.Manager.Page == parent && layout.Manager.ModalCount == 1
                    && host.IsAlive(parent) && host.IsVisible(parent) && host.CurrentInputDocument == document,
                    "license modal preserves visible parent and owns input");
                Check(!Open(open) && layout.Manager.ModalCount == 1, "duplicate profile command cannot stack license overlays");
                Check(!host.FocusDocument(parent, "profile") && !layout.Manager.Accept(open),
                    "covered parent cannot take focus or dispatch a delayed profile command");
                if (delayedText.HasValue) Check(host.Input.Dispatch(delayedText.Value) != RmlUiInputResult.Accepted
                    && host.ReadField(parent, "lobby_chat_input", 4096) == "Private draft survives license",
                    "late covered-Lobby text cannot mutate the preserved draft");
                Until(() => NativeSemanticReadback.Read(host, document).ContainsText("SYNTHETIC CARD HUNTER"), "verified synthetic license reaches actual modal");
                if (requireOwnedPortrait) OwnedPortrait(document);
                if (starCheck) VerifyStars(document, requireOwnedPortrait);
                Capture(lobbyMode ? "lobby-card" : "home-card");
                foreach (var (face, action) in new[] { ("stats", 2), ("history", 3), ("achievements", 4),
                    ("emblems", 5), ("titles", 6), ("comparison", 7), ("account", 8), ("overview", 0) })
                {
                    Activate("native_license_tab_" + face, action);
                    foreach (string other in new[] { "overview", "stats", "history", "achievements", "emblems", "titles", "comparison", "account" })
                        Check(Visible(document, "native_license_face_" + other) == (face == other), "retained face visibility " + face + "/" + other);
                    if (face == "account") Capture(lobbyMode ? "lobby-account-scroll" : "home-account-scroll", origin: false);
                }
                // Card-specific five-tab metrics are checked below once the native card is loaded.
                VerifyCard(document, lobbyMode);
                Click(document, "native_license_close", focus: true);
                Check(host.TryTakeIntent(out var close) && close.Kind == RmlUiIntentKind.LicenseAction
                    && close.Argument == 20 && page!.Handle(close) && page.Closed, "actual close control retires the license overlay");
                Check(!page!.Handle(close), "duplicate close sequence is rejected");
                page.Dispose(); page = null; layout.Resume(); Settle();
                Check(layout.Manager.ModalCount == 0 && layout.Manager.Page == parent && host.IsAlive(parent)
                    && host.CurrentInputDocument == parent && host.FocusedElement() == "profile", "close restores same parent and profile focus");
                if (lobbyMode) Check(layout.ReadField("lobby_chat_input", 4096) == "Private draft survives license", "unsent Lobby draft survives full license roundtrip");
                Click(parent, "profile", focus: false);
                Check(host.TryTakeIntent(out var reopen) && Open(reopen), "profile can reopen a fresh license generation");
                Check(!page!.Handle(close) && layout.Manager.ModalCount == 1, "stale close cannot dismiss reopened license");
                if (requireOwnedPortrait) OwnedPortrait(layout.Manager.Top);
                reader.Current = false; Frame();
                Until(() => NativeSemanticReadback.Read(host, layout.Manager.Top).ContainsText("LOCAL HUNTER"), "retired account clears verified identity");
                Check(!NativeSemanticReadback.Read(host, layout.Manager.Top).ContainsText("SYNTHETIC CARD HUNTER"), "retired account does not retain verified identity");
                Check(!Visible(layout.Manager.Top, "native_license_portrait")
                    && Visible(layout.Manager.Top, "native_license_portrait_label"),
                    "account retirement hides prior favorite portrait and restores placeholder");
                if (starCheck) NoStars(layout.Manager.Top, "RANK // UNVERIFIED");
                Capture(lobbyMode ? "lobby-unverified-card" : "home-unverified-card");
                VerifyUnverifiedCard();
                page.Back(); Check(page.Closed, "Back closes license through presenter lifecycle");
                page.Dispose(); page = null; layout.Resume(); reader.Current = true; Settle();
            }
            if (starCheck)
            {
                foreach (var (points, tier) in new (int, int?)[] { (0,1), (40,2), (140,3), (390,4), (750,5), (0,null), (750,3) })
                {
                    reader.Points = points; reader.Tier = tier;
                    Click(layout.Document, "profile", focus: false);
                    Check(host.TryTakeIntent(out var rankOpen) && Open(rankOpen), "actual profile click opens fresh rank generation " + points + "/" + tier);
                    var rankDocument = layout.Manager.Top;
                    Until(() => NativeSemanticReadback.Read(host, rankDocument).ContainsText("SYNTHETIC CARD HUNTER"), "rank matrix loads verified profile");
                    if (HunterLicenseStarRank.TryCreate(points, tier, out _)) VerifyStars(rankDocument, requireOwnedPortrait);
                    else if (points == 0 && tier == null) VerifyStartingStar(rankDocument, requireOwnedPortrait);
                    else NoStars(rankDocument, tier.HasValue ? "RANK // UNAVAILABLE" : "RANK // UNRANKED");
                    Capture("rank-" + points + "-" + (tier?.ToString() ?? "unranked"));
                    reader.Current = false;
                    Until(() => NativeSemanticReadback.Read(host, rankDocument).ContainsText("LOCAL HUNTER"), "rank matrix account retires");
                    NoStars(rankDocument, "RANK // UNVERIFIED");
                    page!.Back(); Check(page.Closed, "rank matrix Back retires modal");
                    page.Dispose(); page = null; layout.Resume(); reader.Current = true; Settle();
                }
                reader.Points = 175; reader.Tier = 3;
            }
            Click(layout.Document, "profile", focus: false);
            Check(host.TryTakeIntent(out var finalOpen) && Open(finalOpen), "profile reopens license after account lifecycle checks");
            var finalDocument = layout.Manager.Top;
            Until(() => NativeSemanticReadback.Read(host, finalDocument).ContainsText("SYNTHETIC CARD HUNTER"), "reopened license verifies the current account");
            Check(!host.TryGetElementBounds(finalDocument, "native_license_customize", out _, out _, out _, out _)
                && !host.TryGetElementBounds(finalDocument, "native_license_tab_customization", out _, out _, out _, out _),
                "license contains no customization tab or handoff control");
            Activate("native_license_tab_account", 8);
            Check(Visible(finalDocument, "native_license_face_account"), "account remains accessible after reopening license");
            Click(finalDocument, "native_license_close", focus: true);
            Check(host.TryTakeIntent(out var finalClose) && page!.Handle(finalClose) && page.Closed,
                "reopened account overlay closes through the retained control lifecycle");
            page!.Dispose(); page = null; layout.Resume(); Settle();
            Check(layout.Manager.ModalCount == 0 && host.CurrentInputDocument == layout.Document
                && host.FocusedElement() == "profile", "final close restores parent ownership and profile focus");
            Check(GL.GetError() == ErrorCode.NoError, "license overlay leaves no GL error");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure", origin: false); Save(false, error.ToString()); throw; }
        finally { page?.Dispose(); }

        bool Open(in RmlUiIntent intent)
        {
            if (page != null || !layout.HandleIntent(intent, out var forwarded) || forwarded.Kind != RmlUiIntentKind.LicenseOpen) return false;
            layout.Suspend(); page = new NativeLicenseOverviewPage(layout.Manager, reader, host); page.Open(overlay: true); Settle(); return true;
        }
        void Frame()
        {
            var snapshot = controller.Tick();
            if (snapshot.Revision > shown)
            {
                foreach (var binding in snapshot.Bindings)
                    if (binding.Value.Kind == RmlUiBindingKind.Text) layout.SetText(binding.Key, binding.Value.Text);
                    else layout.SetBool(binding.Key, binding.Value.Boolean);
                shown = snapshot.Revision;
            }
            layout.Flush(); page?.Refresh(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds = 180)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds); }
        void Until(Func<bool> predicate, string label)
        { var timer = Stopwatch.StartNew(); do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); } while (timer.ElapsedMilliseconds < 5000); throw new TimeoutException(label); }
        void Click(RmlUiDocumentToken document, string id, bool focus)
        {
            Frame();
            if (focus) Check(host.FocusDocument(document, id), "native focus/scroll reaches " + id);
            host.Update();
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= width + 1 && y + h <= height + 1,
                "actual pointer target is in framebuffer: " + id);
            host.DiscardIntents(); double px = x + w / 2, py = y + h / 2;
            host.Input.PointerMoved(px, py);
            Console.WriteLine("LICENSE CARD POINTER BEFORE " + JsonSerializer.Serialize(new {
                id, document, top = layout.Manager.Top, pageDocument = layout.Manager.Page,
                inputDocument = host.CurrentInputDocument, focused = host.FocusedElement(), hovered = host.HoveredElement(),
                x, y, w, h, pointerX = px, pointerY = py, framebufferScale = 1 }));
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool afterBounds = host.TryGetElementBounds(document, id, out float ax, out float ay, out float aw, out float ah);
            Console.WriteLine("LICENSE CARD POINTER AFTER " + JsonSerializer.Serialize(new {
                id, document, top = layout.Manager.Top, pageDocument = layout.Manager.Page,
                inputDocument = host.CurrentInputDocument, focused = host.FocusedElement(), hovered = host.HoveredElement(),
                afterBounds, x = ax, y = ay, w = aw, h = ah }));
        }
        void Activate(string id, int action)
        {
            RmlUiDocumentToken target = layout.Manager.Top;
            Click(target, id, focus: true);
            bool received = host.TryTakeIntent(out var intent);
            bool expected = received && intent.Kind == RmlUiIntentKind.LicenseAction && intent.Argument == action;
            bool handled = expected && page!.Handle(intent);
            Console.WriteLine("LICENSE CARD ACTIVATE " + JsonSerializer.Serialize(new {
                id, expectedAction = action, received, kind = intent.Kind.ToString(), kindCode = (int)intent.Kind,
                argument = intent.Argument, intent.Document, intent.Sequence, expected, handled, target,
                top = layout.Manager.Top, pageDocument = layout.Manager.Page, inputDocument = host.CurrentInputDocument,
                focused = host.FocusedElement(), hovered = host.HoveredElement(), closed = page?.Closed }));
            Check(expected && handled, "native license activation " + id);
            Check(!page!.Handle(intent) && !host.TryTakeIntent(out _), "activation is consumed once: " + id); Settle();
        }
        bool Visible(RmlUiDocumentToken document, string id) => host.TryGetElementBounds(document, id, out _, out _, out float w, out float h) && w > 0 && h > 0;
        void OwnedPortrait(RmlUiDocumentToken document)
        {
            Until(() => Visible(document, "native_license_portrait") && !Visible(document, "native_license_portrait_label"),
                "private owned original portrait replaces placeholder asynchronously");
            Check(host.TryGetElementBounds(document, "native_license_portrait", out float x, out float y, out float w, out float h)
                && host.TryGetElementBounds(document, "native_license_card", out float cx, out float cy, out float cw, out float ch)
                && x >= cx && y >= cy && x + w <= cx + cw + 1 && y + h <= cy + ch + 1,
                "original owned portrait bounds remain inside license card");
        }
        void VerifyStars(RmlUiDocumentToken document, bool owned)
        {
            Check(HunterLicenseStarRank.TryCreate(reader.Points, reader.Tier, out var rank), "star fixture has coherent accepted points/tier");
            if (owned) Until(() => Visible(document, "native_license_star_images")
                && !Visible(document, "native_license_stars_fallback"), "owned rank artwork replaces fallback asynchronously");
            Check(Visible(document, "native_license_star_row"), "verified rank star row is visible");
            if (owned) for (int star = 1; star <= 5; star++)
                Check(Visible(document, "native_license_star" + star) == (star <= rank.Tier), "owned rank has exact star image count " + rank.Tier + "/" + star);
            var semantic = NativeSemanticReadback.Read(host, document);
            Check(semantic.Label("native_license_rank") == rank.Title + " // " + rank.Tier + (rank.Tier == 1 ? " STAR" : " STARS"), "native rank status has exact title and count");
            string progress = rank.NextRankPoints is int threshold
                ? $"NEXT // {rank.NextTitle} · {threshold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} RP · {rank.PointsToNextRank!.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} TO GO"
                : "MAX RANK // LEGENDARY HUNTER";
            Check(semantic.Label("native_license_rank_progress") == progress, "native progress shows exact next threshold and remaining points");
        }
        void VerifyStartingStar(RmlUiDocumentToken document, bool owned)
        {
            Check(reader.Points == 0 && reader.Tier == null
                && !HunterLicenseStarRank.TryCreate(reader.Points, reader.Tier, out _),
                "starting star source retains zero RP and absent server rank");
            if (owned) Until(() => Visible(document, "native_license_star_images")
                && !Visible(document, "native_license_stars_fallback"), "owned red starting artwork replaces fallback asynchronously");
            Check(Visible(document, "native_license_star_row"), "unrated license shows its original starting star row");
            bool images = Visible(document, "native_license_star_images");
            Check(images != Visible(document, "native_license_stars_fallback"), "starting star has exactly one artwork or fallback presentation");
            var semantic = NativeSemanticReadback.Read(host, document);
            if (images)
                Check(Enumerable.Range(1,5).All(star => Visible(document, "native_license_star" + star) == (star == 1)),
                    "starting artwork contains exactly one star image");
            else Check(semantic.Label("native_license_stars_fallback") == "★", "starting fallback contains exactly one readable star");
            NativeLicenseOverview source = reader.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var projection = NativeLicenseOverviewBindings.Create(new NativeLicenseOverviewController.Snapshot(
                1, false, source, "", NativeLicenseFace.Overview, 0, 1, 0,
                Array.Empty<NativeLicenseMatch>(), false, false));
            Check(projection["class:native_license_star_row:rank-red"].Boolean
                && NativeLicenseOverviewBindings.DisplayStarTier(source) == 1,
                "shared native binding projection selects red artwork tier one for the starting star");
            Check(semantic.Label("native_license_rank") == "BOUNTY HUNTER // 1 STARTING STAR"
                && semantic.Label("native_license_rank_progress") == "UNRANKED // NO ELIGIBLE RANKED RESULT · STARTING STAR"
                && semantic.Label("native_license_points") == "0", "starting status distinguishes presentation from an earned ranked result");
            Check(reader.Points == 0 && reader.Tier == null, "rendering never grants server RP or tier");
        }
        void NoStars(RmlUiDocumentToken document, string expectedStatus)
        {
            Check(NativeSemanticReadback.Read(host, document).Label("native_license_rank") == expectedStatus, "native nonranked status is explicit " + expectedStatus);
            Check(!Visible(document, "native_license_star_row") && !Visible(document, "native_license_star_images")
                && !Visible(document, "native_license_stars_fallback")
                && Enumerable.Range(1,5).All(star => !Visible(document, "native_license_star" + star)),
                "invalid or retired native rank retains no visible row, fallback or star images");
        }
        void VerifyCard(RmlUiDocumentToken document, bool lobbyMode)
        {
            string[][] expected = {
                new[] { "125", "250", "50.0%" }, new[] { "1,234", "567", "321" },
                new[] { "75", "12", "2.18" }, new[] { "22", "33", "44" },
                new[] { "2h05m", "0m30s", "250" }
            };
            for (int tab = 0; tab < 5; tab++)
            {
                Activate("native_license_card_tab" + tab, 25 + tab);
                var semantic = NativeSemanticReadback.Read(host, document);
                Check(Compact(semantic.Label("native_license_points")).Contains("175", StringComparison.Ordinal), "verified card retains exact accepted rating points");
                for (int row = 0; row < 3; row++)
                {
                    string label = semantic.Label("native_license_card_title" + row);
                    Check(label.Length > 0 && label == label.ToUpperInvariant(), "card metric has an accessible uppercase title");
                    Check(Compact(semantic.Label("native_license_card_value" + row)) == expected[tab][row], "card tab " + tab + " row " + row + " shows the exact accepted metric");
                    Bounds(document, "native_license_card_value" + row);
                }
                if (!lobbyMode && tab is 0 or 2 or 4) Capture("home-card-tab" + tab);
            }
            Activate("native_license_card_tab0", 25);
            ScrollToOrigin(document);
            Bounds(document, "native_license_card");
            Check(host.TryGetElementBounds(document, "native_license", out float lx, out float ly, out float lw, out float lh)
                && host.TryGetElementBounds(document, "native_license_card", out float cx, out float cy, out float cw, out float ch)
                && cx >= lx && cy >= ly && cx + cw <= lx + lw + 1 && cy + ch <= ly + lh + 1,
                "entire original card remains within the compact responsive overlay");
            float density = (float)typeof(RmlUiHost).GetField("_density",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
            if (height / density > 755)
                Check(lh <= 681 * density, "tall viewport uses the compact 680dp license height");
        }
        void VerifyUnverifiedCard()
        {
            for (int tab = 0; tab < 5; tab++)
            {
                Activate("native_license_card_tab" + tab, 25 + tab);
                var semantic = NativeSemanticReadback.Read(host, layout.Manager.Top);
                Check(semantic.Label("native_license_points") == "UNVERIFIED", "retired account clears card rating points");
                for (int row = 0; row < 3; row++)
                    Check(semantic.Label("native_license_card_value" + row) == "SYNC REQUIRED", "unverified card tab " + tab + " row " + row + " does not invent zero or retain prior metrics");
            }
        }
        void Bounds(RmlUiDocumentToken document, string id)
        {
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= width + 1 && y + h <= height + 1,
                "card content fits viewport: " + id);
        }
        string Compact(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        void ScrollToOrigin(RmlUiDocumentToken document)
        {
            if (page != null && host.TryGetElementBounds(document, "native_license", out float x, out float y, out float w, out float h))
            {
                host.Input.PointerMoved(x + w / 2, y + h / 2);
                host.Input.PointerWheel(1000);
                Settle(350);
            }
        }
        void Capture(string name, bool origin = true)
        {
            Frame(); var document = layout.Manager.Top;
            if (document == default) return;
            if (origin) ScrollToOrigin(document);
            string path = Path.Combine(output, "synthetic-license-card-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "final native screenshot " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, document).RawJson);
            captures.Add(path);
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "license-card-summary.json"), JsonSerializer.Serialize(new { passed, checks, width, height, requireOwnedPortrait, starCheck, scope = Scope, captures, error }, new JsonSerializerOptions { WriteIndented = true }));
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException(label); checks++; Console.WriteLine("LICENSE CARD GL PASS " + label); }
    }

    private sealed class Reader : INativeLicenseOverviewReader
    {
        internal bool Current = true;
        internal int Points = 175;
        internal int? Tier = 3;
        public bool IsCurrent(NativeLicenseOverview value) => Current;
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token) => Task.FromResult(new NativeLicenseOverview(
            true, "SYNTHETIC CARD HUNTER", "SAMUS", "SYNTHETIC VERIFIED RECORD", "PRIVATE", Points, "TIER " + Tier,
            250, 125, 100, 1234, 567, 321, 0)
        { RatingTierNumber = Tier, PrivacyLease = 1, Account = new(true, true, false, false, false), Career = new(25, 987654, 60 * 60 * 125, 75, 12, 2, 8, 22, 33, 44) });
    }
    private sealed class Services : IRmlUiMenuServices
    {
        internal bool InLobby;
        public HubSnapshot Hub() => new("SYNTHETIC CARD HUNTER", Hunter.Samus, Hunter.Samus, 0, true, "NO NETWORK");
        public string BuildVersion() => "synthetic-license-card";
        public RmlUiLobbyView Lobby() => InLobby ? new(true, false, true, true, false, true, false,
            "SYNTHETIC LOBBY", "MP1 SANCTORUS", "Battle", "Free for all", "SYNTHETIC AUTHORITY", 8,
            new[] { new RmlUiLobbyPlayer("SYNTHETIC CARD HUNTER", "SAMUS", false, true, false) }) : new(false, false, false, false, false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "FIXTURE", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public bool AcceptJoin(LaunchPlan plan) => false;
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
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "FIXTURE"; return false; }
        public void Quit() { }
    }
}
