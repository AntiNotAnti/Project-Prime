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
using OpenTK.Graphics.OpenGL;

internal static class MapPickerGlAcceptance
{
    private const string Scope = "Actual authored native map modal, five native category commands, pending custom catalog with immediately selectable MPH maps, owner-bound synthetic TGA image, offline-only disabled choice, scrolling and pager. Synthetic catalog/preview only; no server, HTTP, game setup, actual package install or rule submission.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_MAP_PICKER_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-map-picker-rml88", width + "x" + height));
        Directory.CreateDirectory(output); var evidence = new List<object>();
        byte[] rgba = Enumerable.Range(0, 64).SelectMany(_ => new byte[] { 13, 211, 133, 255 }).ToArray();
        string preview = Path.Combine(output, "synthetic-card.tga");
        File.WriteAllBytes(preview, RmlUiMapPreviewCache.EncodeTga(rgba, 8, 8));
        var authority = new Authority { Preview = preview };
        using var controller = new RmlUiMenuController(authority);
        using var layout = new RmlUiLauncherPages(host);
        long presented = -1; host.Input.SetFramebufferScale(1, 1);
        try
        {
            Settle(); Open();
            Check(authority.CatalogCalls == 1 && !authority.Catalog.Task.IsCompleted && controller.MapPickerOpen,
                "actual map modal opens without waiting for held installed catalog");
            Check(Flag("map_vanilla_selected") && Text("map_choice0_name").Length > 0,
                "held custom catalog still presents actionable MPH cards immediately");
            string firstMap = Text("map_choice0_name");
            Activate("map_choice0", RmlUiIntentKind.LobbyMapSelect, 0);
            Check(!controller.MapPickerOpen && Text("rules_map") == firstMap && authority.Submissions == 0,
                "native MPH card selection edits only the draft while custom catalog is held");
            controller.Dispatch(RmlUiIntentKind.LobbyRulesClose); Settle();
            authority.Catalog.TrySetCanceled();
            authority.Catalog = new(TaskCreationOptions.RunContinuationsAsynchronously); Open();
            authority.Catalog.SetResult(new[]
            {
                new RmlUiArenaChoice("SYNTHETIC ECHOES OFFLINE", RmlUiArenaCategory.Echoes, false, "OFFLINE ONLY // NOT NETWORK SELECTABLE"),
                new RmlUiArenaChoice("SYNTHETIC ECHOES VERIFIED", RmlUiArenaCategory.Echoes),
                new RmlUiArenaChoice("SYNTHETIC HALO VERIFIED", RmlUiArenaCategory.Halo),
                new RmlUiArenaChoice("SYNTHETIC QUAKE VERIFIED", RmlUiArenaCategory.Quake),
                new RmlUiArenaChoice("SYNTHETIC COMMUNITY VERIFIED", RmlUiArenaCategory.Community)
            });
            Settle();
            string[] ids = { "map_vanilla", "map_echoes", "map_halo", "map_quake", "map_community" };
            int[] values = { 0, 2, 3, 4, 1 };
            string[] keys = { "map_vanilla_selected", "map_echoes_selected", "map_halo_selected", "map_quake_selected", "map_community_selected" };
            var tabBounds = ids.Select(Box).ToArray();
            for (int i = 1; i < tabBounds.Length; i++)
                Check(tabBounds[i].Y > tabBounds[i - 1].Y + 1 || Math.Abs(tabBounds[i].Y - tabBounds[i - 1].Y) <= 1
                    && tabBounds[i].X >= tabBounds[i - 1].X + tabBounds[i - 1].W - 1,
                    "native visual tab order preserves MPH Echoes Halo Quake Community");
            for (int i = 0; i < ids.Length; i++)
            {
                Activate(ids[i], RmlUiIntentKind.LobbyMapCategory, values[i], pointer: i % 2 == 0);
                Check(Flag(keys[i]) && keys.Count(Flag) == 1, "native category command selects exactly " + ids[i]);
                if (i > 0) Check(Text("map_choice0_name").Contains(i switch { 1 => "ECHOES", 2 => "HALO", 3 => "QUAKE", _ => "SYNTHETIC" }),
                    "selected category receives its authoritative synthetic installed rows");
                if (i == 1)
                {
                    Check(!host.FocusDocument(layout.Manager.Top, "map_choice0"), "offline-only Echoes card is disabled in actual native DOM");
                    Check(!controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0) && controller.MapPickerOpen && authority.Submissions == 0,
                        "offline-only scoped choice cannot enter the network rule draft");
                    Check(Flag("map_choice1_enabled"), "other verified choice in same category remains selectable");
                }
                Capture("category-" + values[i]);
            }
            Activate("map_vanilla", RmlUiIntentKind.LobbyMapCategory, 0);
            Reach("map_choice0"); Frame();
            Check(host.TryGetElementBounds(layout.Manager.Top, "map_image0", out float ix, out float iy, out float iw, out float ih)
                && iw > 0 && ih > 0 && !host.TryGetElementBounds(layout.Manager.Top, "map_art_label0", out _, out _, out _, out _),
                "owner-bound image is visible and placeholder is hidden on authored map card");
            byte[] pixel = new byte[4];
            GL.ReadPixels((int)(ix + iw / 2), height - 1 - (int)(iy + ih / 2), 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            Check(Math.Abs(pixel[0] - 13) < 12 && Math.Abs(pixel[1] - 211) < 12 && Math.Abs(pixel[2] - 133) < 12,
                "actual map image paints expected synthetic TGA pixels through owner binding");
            evidence.Add(new { image = preview, ix, iy, iw, ih, pixel });
            Check(Flag("map_can_next"), "six-card native map picker exposes next page for longer MPH list");
            Activate("map_next", RmlUiIntentKind.LobbyMapNext, 0);
            Check(Text("map_page").StartsWith("2 /", StringComparison.Ordinal), "actual pager changes current card page");
            Reach("map_choice0");
            Activate("map_previous", RmlUiIntentKind.LobbyMapPrevious, 0);
            Check(Text("map_page").StartsWith("1 /", StringComparison.Ordinal), "native previous pager remains reachable at small viewport");
            Measure(); Capture("mph-image-and-pager");
            Activate("map_close", RmlUiIntentKind.LobbyMapClose, 0);
            Check(!controller.MapPickerOpen && authority.Submissions == 0 && GL.GetError() == ErrorCode.NoError,
                "closing native map picker never applies rules or mutates a real server");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }
        finally { authority.Catalog.TrySetCanceled(); }

        bool Flag(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;
        string Text(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) ? value.Text : "";
        void Open() { Activate("lobby_map_preview", RmlUiIntentKind.LobbyMapOpen, 0); Check(controller.MapPickerOpen, "actual Lobby map preview opens authored map modal"); }
        void Frame()
        {
            var snapshot = controller.Tick();
            if (snapshot.Revision > presented)
            {
                foreach (var pair in snapshot.Bindings)
                    if (pair.Value.Kind == RmlUiBindingKind.Text) layout.SetText(pair.Key, pair.Value.Text); else layout.SetBool(pair.Key, pair.Value.Boolean);
                presented = snapshot.Revision;
            }
            layout.Flush(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.025f, .05f, .08f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds = 180) { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds); }
        (float X, float Y, float W, float H) Box(string id)
        {
            Check(host.TryGetElementBounds(layout.Manager.Top, id, out float x, out float y, out float w, out float h) && w > 0 && h > 0,
                "actual native map control bounds exist: " + id); return (x, y, w, h);
        }
        (float X, float Y, float W, float H) Reach(string id)
        {
            Frame(); Check(host.FocusDocument(layout.Manager.Top, id), "actual native map control can focus: " + id); Settle();
            for (int i = 0; i < 120; i++)
            {
                var b = Box(id);
                if (b.X >= 0 && b.Y >= 0 && b.X + b.W <= width + 1 && b.Y + b.H <= height + 1)
                {
                    if (!id.StartsWith("map_choice", StringComparison.Ordinal)) return b;
                    var grid = Box("map_grid"); if (b.Y >= grid.Y && b.Y + b.H <= grid.Y + grid.H + 1) return b;
                }
                var viewport = Box(controller.MapPickerOpen ? "map_grid" : "lobby_left_panels");
                host.Input.PointerMoved(viewport.X + viewport.W / 2, viewport.Y + viewport.H / 2);
                host.Input.PointerWheel(i == 0 ? 10000 : -1); Settle(20);
            }
            throw new InvalidOperationException("Native map target cannot be scrolled into viewport: " + id);
        }
        void Activate(string id, RmlUiIntentKind kind, int argument, bool pointer = true)
        {
            var b = Reach(id); host.DiscardIntents();
            if (pointer) { double x = b.X + b.W / 2, y = b.Y + b.H / 2; host.Input.PointerMoved(x, y); host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false); }
            else { host.Input.Key(2, true); host.Input.Key(2, false); }
            host.Update(); int count = 0;
            while (host.TryTakeIntent(out var intent))
            {
                Check(intent.Kind == kind && intent.Argument == argument, "native bridge decodes exact map action/category");
                Check(layout.HandleIntent(intent, out var forwarded) && forwarded.Kind != 0
                    && controller.Dispatch(forwarded.Kind, forwarded.Argument), "existing page/controller accepts native map action");
                Check(layout.HandleIntent(intent, out var duplicate) && duplicate.Kind == 0, "native sequence cannot repeat map action"); count++;
            }
            Check(count == 1, "one activation emits one native map action"); Settle();
        }
        void Measure()
        {
            var panel = Box("map_panel"); var grid = Box("map_grid"); var pager = Box("map_pager");
            Check(panel.X >= 0 && panel.Y >= 0 && panel.X + panel.W <= width + 1 && panel.Y + panel.H <= height + 1
                && grid.Y + grid.H <= pager.Y + 1 && pager.Y + pager.H <= height + 1,
                "map panel grid scroll region and visible pager fit actual framebuffer");
            evidence.Add(new { panel, grid, pager });
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "synthetic-map-picker-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "actual map modal screenshot: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, layout.Manager.Top).RawJson); evidence.Add(new { name, path });
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "map-picker-" + width + "x" + height + ".json"),
            JsonSerializer.Serialize(new { passed, scope = Scope, width, height, authority.CatalogCalls, authority.Submissions, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("MAP PICKER GL PASS " + label); }
    private sealed class Authority : IRmlUiMenuServices
    {
        internal string Preview = "";
        internal int CatalogCalls, Submissions;
        internal TaskCompletionSource<IReadOnlyList<RmlUiArenaChoice>> Catalog = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string[] _builtIn = Metadata.RoomList.Select(r => r.Name)
            .Where(n => n.StartsWith("MP", StringComparison.Ordinal) || n.StartsWith("AD", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal).Take(8).ToArray();
        public HubSnapshot Hub() => new("SYNTHETIC OWNER", Hunter.Samus, Hunter.Samus, 0, true, "NO NETWORK");
        public string BuildVersion() => "synthetic-map-picker";
        public RmlUiLobbyView Lobby() => new(true, false, true, false, false, true, false,
            "SYNTHETIC LOBBY", "MP1 SANCTORUS", "Battle", "Free for all", "NO NETWORK", 8,
            new[] { new RmlUiLobbyPlayer("SYNTHETIC OWNER", "SAMUS", false, true, false) });
        public IReadOnlyList<string> BuiltInArenaKeys() => _builtIn;
        public Task<IReadOnlyList<RmlUiArenaChoice>> ReadMapPickerCatalogAsync(CancellationToken token)
        { CatalogCalls++; return Catalog.Task; }
        public RmlUiArenaPreview[] ReadMapPickerPreviews(IReadOnlyList<string> rooms)
            => rooms.Select(room => new RmlUiArenaPreview(room.Length == 0 ? "" : Preview, "SYNTHETIC OWNER PREVIEW")).ToArray();
        public RmlUiRuleDraft? ReadRules() => new(5, 999, 17,
            new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle,
                Format = MatchFormat.FreeForAll, PointGoal = 7, TimeLimitSeconds = 420 }, LobbyRuleFlags.None);
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { Submissions++; error = "FIXTURE"; return false; }
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
        public void Quit() { }
    }
}
