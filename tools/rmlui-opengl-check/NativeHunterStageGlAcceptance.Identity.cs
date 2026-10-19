using System.Text.Json;
using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;

internal static partial class NativeHunterStageGlAcceptance
{
    private static void LobbyIdentityCards(RenderWindow window, RmlUiHost host, RmlUiLauncherPages layout,
        int width, int height, string output)
    {
        var services = new IdentityAuthority();
        using var controller = new RmlUiMenuController(services);
        layout.ShowBaseline(RmlUiMenuPage.Lobby);
        Check(controller.Dispatch(RmlUiIntentKind.HunterOpen), "actual identity controller opens local lobby draft");
        Publish();
        var document = layout.Manager.Top;
        string[] expected = { "hunter_choice0", "hunter_placeholder1", "hunter_choice5", "hunter_choice1", "hunter_choice2", "hunter_choice4", "hunter_choice3", "hunter_choice6", "hunter_placeholder8", "hunter_placeholder9", "hunter_random" };
        using (var tree = JsonDocument.Parse(NativeSemanticReadback.Read(host, document).RawJson))
        {
            var cards = tree.RootElement.GetProperty("nodes").EnumerateArray()
                .Where(n => expected.Contains(n.GetProperty("id").GetString())).ToArray();
            Check(cards.Select(n => n.GetProperty("id").GetString()).SequenceEqual(expected), "actual eleven identity cards retain the requested display order");
            Check(cards.Where(n => n.GetProperty("id").GetString()!.StartsWith("hunter_placeholder"))
                .All(n => !n.GetProperty("enabled").GetBoolean() && n.GetProperty("actions").GetInt32() == 0),
                "three actual placeholder cards are disabled and expose no activation");
        }
        var cardBounds = expected.Select(Rect).ToArray();
        for (int i = 0; i < cardBounds.Length; i++)
        for (int j = i + 1; j < cardBounds.Length; j++)
            Check(cardBounds[i].Right <= cardBounds[j].X + 1 || cardBounds[j].Right <= cardBounds[i].X + 1
                || cardBounds[i].Bottom <= cardBounds[j].Y + 1 || cardBounds[j].Bottom <= cardBounds[i].Y + 1,
                "native identity card rectangles do not overlap: " + i + "/" + j);
        Paint("identity-initial");
        // Model-status retirement can reflow an auto-height modal after the
        // initial paint. Determine overflow from one fresh native layout, never
        // from card coordinates measured before that first successful draw.
        var initial = Rect("hunter_choice0"); var scroller = Rect("identity_cards");
        var beforeWheelCards = expected.Select(Rect).ToArray();
        bool scrollRequired = beforeWheelCards.Max(r => r.Bottom) > scroller.Bottom + 1;
        host.Input.PointerMoved(scroller.X + scroller.W / 2, scroller.Y + scroller.H / 2);
        string wheelHover = host.HoveredElement();
        host.Input.PointerWheel(-4); Settle();
        var afterWheel = Rect("hunter_choice0");
        File.WriteAllText(Path.Combine(output,"identity-wheel-bounds.json"),JsonSerializer.Serialize(new {
            scrollRequired,initial,scroller,beforeWheelCards,afterWheel,wheelHover,
            deltaY=afterWheel.Y-initial.Y },new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        if (scrollRequired)
            Check(afterWheel.Y < initial.Y, "compact identity selector scrolls through real native pointer wheel");
        Paint("identity-wheel");
        for (int hunter = 0; hunter < 7; hunter++)
        for (int suit = 0; suit < 4; suit++)
        {
            Click("hunter_choice" + hunter, RmlUiIntentKind.HunterSelect, hunter);
            Click("hunter_suit" + suit, RmlUiIntentKind.HunterSuit, suit);
            Check(host.FocusDocument(document, "hunter_choice" + hunter), "selected card becomes reachable for real model capture"); Settle();
            Paint("identity-" + hunter + "-suit-" + suit);
            var slots = (HunterPreviewEntity?[])typeof(Scene).GetField("_launcherSlotPreviews", Hidden)!.GetValue(PreviewScene())!;
            Check(slots[8 + hunter] is { Ready: true } actor && actor.Shown == (Hunter)hunter
                && actor.ShownSuit == suit && actor.Mode == SkinContext.Biped,
                "dedicated actual card model is the selected full-body Hunter and suit");
            Check(services.Applies == 0, "card and suit pointer selection never sends Apply");
        }
        services.Allowed = new[] { Hunter.Trace, Hunter.Weavel }; Publish();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Click("hunter_random", RmlUiIntentKind.HunterRandom, 0);
            var state = controller.Tick();
            Check(services.Allowed.Any(h => state.Bindings["hunter_choice" + (int)h + "_selected"].Boolean),
                "actual Random pointer chooses only an allowed playable Hunter");
            Check(services.Applies == 0, "Random never implicitly applies identity");
        }
        Click("hunter_cancel", RmlUiIntentKind.HunterCancel, 0);
        Check(!controller.HunterOpen && services.Applies == 0, "cancel retires actual identity draft without transmission");
        Rows.Add(new { page = "LobbyIdentity", order = expected, models = "seven real full-body models, four suits each", random = "restricted Trace/Weavel pool", applies = services.Applies });

        void Publish()
        {
            foreach (var entry in controller.Tick().Bindings)
                if (entry.Value.Kind == RmlUiBindingKind.Boolean) layout.SetBool(entry.Key, entry.Value.Boolean);
                else layout.SetText(entry.Key, entry.Value.Text);
            layout.Flush(); host.Update(); layout.AfterUpdate();
        }
        void Settle() { for (int i = 0; i < 4; i++) { Thread.Sleep(15); host.Update(); } }
        IdentityRect Rect(string id)
        {
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h) && w > 0 && h > 0,
                "actual identity bounds exist: " + id);
            return new(x, y, w, h);
        }
        void Click(string id, RmlUiIntentKind kind, int argument)
        {
            host.DiscardIntents(); Check(host.FocusDocument(document, id), "actual identity control can focus: " + id); Settle();
            var b = Rect(id); double x = b.X + b.W / 2, y = b.Y + b.H / 2;
            host.Input.PointerMoved(x, y); host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false); host.Update();
            Check(host.TryTakeIntent(out var intent) && intent.Document == document && intent.Kind == kind && intent.Argument == argument
                && layout.Manager.Accept(intent) && !host.TryTakeIntent(out _), "real pointer emits one exact identity intent: " + id);
            Check(controller.Dispatch(intent.Kind, intent.Argument), "actual controller accepts identity intent: " + id); Publish();
        }
        void Paint(string name)
        {
            Clear(width, height); NativeOverlay(host, width, height);
            // Capture the exact native layout that the UI pass just painted.
            // Successful models retire status text only after all model draws;
            // reading DOM bounds after that mutation could advance smooth scroll.
            var clip = Rect("identity_cards"); var modal = Rect("identity_modal");
            var modelBounds = Enumerable.Range(0, 7).Select(i => (Hunter: i, Bounds: Rect("hunter_model" + i))).ToArray();
            var visible = modelBounds.Where(p => p.Bounds.Inside(clip) && p.Bounds.Inside(modal)).ToArray();
            byte[] before = FinalCompositeCapture.Read(width, height); string state = GlState();
            var cosmetics = (CosmeticPreview.Loadout, CosmeticPreview.Mode, CosmeticPreview.Yaw, CosmeticPreview.Zoom, CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest);
            RmlUiHunterStageRenderer.DrawLobbyHunterCards(window, host, document, controller.HunterDraftSuit, width, height);
            Check(GlState() == state, "identity cards restore caller GL state");
            Check(cosmetics == (CosmeticPreview.Loadout, CosmeticPreview.Mode, CosmeticPreview.Yaw, CosmeticPreview.Zoom, CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest),
                "identity card rendering restores the complete shared cosmetic preview state");
            byte[] after = FinalCompositeCapture.Read(width, height);

            foreach (var card in visible)
                Check(Changed(card.Bounds) > 8, "actual full-body card changes native framebuffer pixels: " + card.Hunter);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = ((height - 1 - y) * width + x) * 3;
                if (Math.Abs(before[at] - after[at]) + Math.Abs(before[at + 1] - after[at + 1]) + Math.Abs(before[at + 2] - after[at + 2]) <= 20) continue;
                bool contained = false;
                foreach (var card in visible) if (card.Bounds.Contains(x, y, 1)) { contained = true; break; }
                if (!contained)
                {
                    string evidence = Path.Combine(output, name + "-clip-failure.json");
                    File.WriteAllText(evidence, JsonSerializer.Serialize(new { x, y, width, height, clip, modal,
                        modelBounds, visible, before = new[] { before[at], before[at + 1], before[at + 2] },
                        after = new[] { after[at], after[at + 1], after[at + 2] } },
                        new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                    Capture(output, name + "-clip-failure", width, height);
                }
                Check(contained, "card model pixels remain inside fully visible native clip rectangles");
            }
            Capture(output, name, width, height);
            int Changed(IdentityRect rect)
            {
                int changed = 0;
                for (int y = Math.Max(0, (int)rect.Y); y < Math.Min(height, rect.Bottom); y++)
                for (int x = Math.Max(0, (int)rect.X); x < Math.Min(width, rect.Right); x++)
                { int at = ((height - 1 - y) * width + x) * 3; if (Math.Abs(before[at] - after[at]) + Math.Abs(before[at + 1] - after[at + 1]) + Math.Abs(before[at + 2] - after[at + 2]) > 20) changed++; }
                return changed;
            }
        }
    }
    private static void QuitAlignedScreens(RmlUiHost host, RmlUiLauncherPages layout, int width, int height, string output)
    {
        layout.ShowBaseline(RmlUiMenuPage.Home);
        foreach (var route in new[] {
            (Name: "hunters", Path: "pages/hunters/native-picker.rml"),
            (Name: "community", Path: "pages/community/native-discover.rml"),
            (Name: "studio", Path: "pages/studio/native-entry.rml"),
            (Name: "settings", Path: "pages/settings/native-full.rml") })
        {
            var document = layout.Manager.OpenPage(new("quit-alignment-" + route.Name, route.Path));
            host.SetBool(document, "visible:footer_actions", true);
            host.SetBool(document, "visible:lobby_footer_state", false);
            for (int i = 0; i < 4; i++) { Thread.Sleep(15); host.Update(); }
            Check(host.TryGetElementBounds(document, "footer_quit", out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x >= width * .80f && x + w <= width && y >= 0 && y + h <= height,
                "actual native Quit stays at the right screen edge: " + route.Name);
            Clear(width, height); NativeOverlay(host, width, height);
            Rows.Add(new { page = "QuitAlignment", route = route.Name, x, y, width = w, height = h,
                capture = Capture(output, "quit-right-" + route.Name, width, height) });
            layout.Manager.ClosePage();
        }
    }

    private readonly record struct IdentityRect(float X, float Y, float W, float H)
    {
        internal float Right => X + W; internal float Bottom => Y + H;
        internal bool Inside(IdentityRect b) => X >= b.X && Y >= b.Y && Right <= b.Right && Bottom <= b.Bottom;
        internal bool Contains(float x, float y, float margin) => x >= X - margin && x <= Right + margin && y >= Y - margin && y <= Bottom + margin;
    }
    private sealed class IdentityAuthority : IRmlUiMenuServices
    {
        internal Hunter[] Allowed = Enumerable.Range(0, 7).Select(i => (Hunter)i).ToArray(); internal int Applies;
        public HubSnapshot Hub() => new("EXPLICIT IDENTITY FIXTURE", Hunter.Samus, Hunter.Samus, 0, true, "LOCAL ONLY");
        public string BuildVersion() => "identity-fixture";
        public RmlUiLobbyView Lobby() => new(true, false, true, false, false, false, false, "FIXTURE", "MP1 SANCTORUS", "Battle", "FFA", "LOCAL", 8, Array.Empty<RmlUiLobbyPlayer>());
        public RmlUiHunterDraft? ReadHunterDraft() => new(17, 99, Hunter.Samus, 0, Allowed, false);
        public bool ApplyHunterDraft(Hunter hunter, byte suit, ushort matchId, ulong epoch) { Applies++; return false; }
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "FIXTURE", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, "FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, "FIXTURE"));
        public bool AcceptJoin(LaunchPlan plan) => false; public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument route) => false; public bool OpenStudio() => false;
        public bool SetReady() => false; public bool StartMatch() => false; public bool LeaveLobby() => false;
        public bool NextHunter() => false; public bool NextSuit() => false; public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null; public IReadOnlyList<string> BuiltInArenaKeys() => Array.Empty<string>();
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "FIXTURE"; return false; } public void Quit() { }
    }
}
