using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native layout/input/controller with EXPLICIT SYNTHETIC authority; no game/network/account owner.</summary>
internal static class LobbyRestorationGlAcceptance
{
    private const string Claim = "Actual native RmlUi Home and Lobby DOM, measured desktop density, single existing RenderWindow, pointer/keyboard intents, composed page admission and MenuController dispatch. Lobby identities and command authority are explicit synthetic in-memory fixtures; no live server or bot creation. The original procedural chamber and platforms use the existing stage renderer; owned cartridge Hunter models are rendered only from explicit private configured assets; a real selected-room thumbnail is shown only when mapPng is provided. No player save or account owner.";
    private readonly record struct Box(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;
        public float Bottom => Y + Height;
        public bool Contains(Box other) => other.X >= X - 1 && other.Y >= Y - 1
            && other.Right <= Right + 1 && other.Bottom <= Bottom + 1;
    }

    internal static int RunWindow(string assets, int requestedWidth, int requestedHeight)
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root) || !File.Exists(Path.Combine(root, "paths.txt")))
            throw new ArgumentException("Actual lobby GL requires explicit private configured userdata.");
        Directory.SetCurrentDirectory(root); GameFiles.Root = root;
        Check(GameFiles.Ready && Menu.SaveSlot == 0 && !NetSession.Active && !DemoPlayback.IsActive,
            "actual local asset fixture owns no player save, network or replay");
        GraphicsBackendPolicy.Configure("opengl");
        var expected = LoadedNativeModuleCheck.CaptureExpectedPackage(AppContext.BaseDirectory);
        RenderWindow? window = null; Scene? sentinel = null; RmlUiHost? host = null;
        var sceneField = typeof(RenderWindow).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            window = new RenderWindow(); window.ClientSize = new(requestedWidth, requestedHeight);
            sentinel = window.Scene; sceneField.SetValue(window, null);
            int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
            float density = width / (float)Math.Max(1, window.ClientSize.X);
            host = new RmlUiHost();
            Check(host.Initialize(width, height, density, assets, RmlUiRenderBackend.OpenGl),
                "actual RenderWindow and native lobby initialize at measured desktop density");
            Console.WriteLine($"LOBBY REAL WINDOW client={window.ClientSize.X}x{window.ClientSize.Y} framebuffer={width}x{height} density={density}");
            LoadedNativeModuleCheck.WriteNew(Path.Combine(root, "loaded-native-module.json"),
                LoadedNativeModuleCheck.RequireLoaded(expected));
            Run(host, width, height, window);
            Check(!window.HasScene && !NetSession.Active && !DemoPlayback.IsActive && Menu.SaveSlot == 0,
                "native lobby fixture never enters a gameplay, network, replay or saved-player owner");
            return 0;
        }
        finally
        {
            host?.Dispose(); RmlUiHunterStageRenderer.Hide();
            var field = typeof(LauncherHunter).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (field.GetValue(null) is Scene preview) preview.UnloadGl();
            field.SetValue(null, null); LauncherHunter.Reset();
            if (window != null) { sceneField.SetValue(window, sentinel); window.EndScene(); window.Dispose(); }
            MphRead.Sound.AudioLifetime.Shutdown(); Read.ClearCache();
        }
    }

    internal static void Run(RmlUiHost host, int width, int height, RenderWindow? window = null)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LOBBY_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-lobby-restoration-rml84-20261018", "runs"));
        Directory.CreateDirectory(output);
        using var pages = new RmlUiPageManager(host);
        using var layout = new RmlUiLauncherPages(host, pages);
        var authority = new FixtureAuthority();
        bool ownedArt = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LOBBY_OWNED_ART") == "1";
        using var mapCache = new RmlUiMapPreviewCache(Path.Combine(MphRead.Mods.Platform.AppPaths.UserDataDirectory, "lobby-map-cache"));
        string mapPng = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LOBBY_MAP_PNG") ?? "";
        if (ownedArt)
        {
            Check(Path.IsPathFullyQualified(MphRead.Mods.Platform.AppPaths.UserDataDirectory)
                && File.Exists(Path.Combine(MphRead.Mods.Platform.AppPaths.UserDataDirectory, "paths.txt")),
                "owned-art check requires explicit private configured userdata");
            Directory.SetCurrentDirectory(MphRead.Mods.Platform.AppPaths.UserDataDirectory);
            Check(GameFiles.Ready, "private owned cartridge files configure through the existing game-files owner");
        }
        if (mapPng.Length > 0)
        {
            var ready = Stopwatch.StartNew();
            do { authority.MapPreview = mapCache.Read(mapPng); if (authority.MapPreview.Length == 0) Thread.Sleep(20); }
            while (authority.MapPreview.Length == 0 && ready.ElapsedMilliseconds < 5000);
            Check(authority.MapPreview.Length != 0, "actual generated room PNG decodes through the existing native map cache");
        }
        using var controller = new RmlUiMenuController(authority);
        host.Input.SetFramebufferScale(1, 1);
        layout.SetBool("reduce_motion", true);
        var lobby = layout.ShowBaseline(RmlUiMenuPage.Home);
        var stagePage = RmlUiMenuPage.Home;
        authority.LobbyActive = false;
        long presented = 0;
        var evidence = new List<object>();
        try
        {
            Settle();
            Check(pages.Top == lobby && host.CurrentInputDocument == lobby,
                "existing composed Home owns actual native foreground");
            var homeStage = Bounds("stage"); var activity = Bounds("activity_panel");
            Check(new Box(0, 0, width, height).Contains(homeStage)
                && homeStage.Contains(activity) && activity.Contains(Bounds("deploy")),
                "actual Home stage, activity panel and Deploy control fit the framebuffer");
            Check(Flag(controller.Tick(), "home_social_rail_visible")
                && Flag(controller.Tick(), "home_feature_available"),
                "explicit synthetic connected social and bundled feature fixtures populate Home rails");
            // These authored rails deliberately disappear at compact breakpoints.
            // Require all three in the desktop reference composition.
            if (window != null && window.ClientSize.X > 1180 && window.ClientSize.Y > 490)
            {
                var feature = Bounds("home_feature_rail"); var session = Bounds("session_panel");
                var social = Bounds("home_social_rail");
                Check(homeStage.Contains(feature) && homeStage.Contains(session) && homeStage.Contains(social),
                    "Home feature, session and Friends / Party rails fit the actual native stage");
                Check(feature.Right <= homeStage.X + homeStage.Width * .45f
                    && Math.Min(session.X, social.X) >= homeStage.X + homeStage.Width * .65f,
                    "Home side rails leave the center chamber clear");
                Check(session.Bottom <= social.Y + 1,
                    "Home session and Friends / Party rails occupy separate vertical regions");
                evidence.Add(new { phase = "home-rails", feature, session, social,
                    source = "explicit synthetic connected social state; bundled News provider" });
            }
            Check(Text(controller.Tick(), "home_party_count") == "1 / 4"
                && Enumerable.Range(1, 3).All(i => !Flag(controller.Tick(), "home_party_slot" + i + "_occupied")),
                "connected friends never imply Home party membership");
            if (window != null && window.ClientSize.X >= 1024 && window.ClientSize.Y > 490)
            {
                var session = Bounds("session_panel");
                foreach (string id in new[] { "home_party_slot0", "home_social", "home_party_slot2", "home_party_slot3" })
                    Check(session.Contains(Bounds(id)), "one of four actual Home session seats fits its panel: " + id);
                foreach (string id in new[] { "home_social", "home_party_slot2", "home_party_slot3" })
                {
                    Queue(id, pointer: id != "home_party_slot2");
                    Drain(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Social, accepted: true);
                }
                Check(authority.SocialRoutes == 3, "all three actual open seats route through existing Social navigation");
            }
            RequireProceduralChamber(lobbyMode: false);
            evidence.Add(new { phase = "home", stage = homeStage, activity });
            Capture("home-procedural-platform");
            authority.Party = SyntheticParty(); Settle();
            Check(Text(controller.Tick(), "home_party_count") == "4 / 4"
                && Enumerable.Range(1, 3).All(i => Flag(controller.Tick(), "home_party_slot" + i + "_occupied")
                    && Text(controller.Tick(), "home_party_slot" + i + "_name") == "SYNTHETIC PARTY MEMBER " + i),
                "only explicit synthetic verified Party members populate the three Home seats");
            Check(RmlUiHunterStagePlan.Build(authority.Hub(), authority.Lobby(), null).Count == 0,
                "Home party names never guess remote Hunter or suit identities");
            Capture("home-four-synthetic-party-members");
            authority.Party = new(false, false, "SYNTHETIC DISCONNECTED", 0, null); Settle();
            authority.LobbyActive = true;
            stagePage = RmlUiMenuPage.Lobby;
            lobby = layout.ShowBaseline(stagePage);
            Settle();
            Check(pages.Top == lobby && host.CurrentInputDocument == lobby,
                "existing composed Lobby owns actual native foreground");
            MeasureRails("initial-owner");
            RequireProceduralChamber(lobbyMode: true);
            Capture("lobby-procedural-platforms");
            if (authority.MapPreview.Length > 0)
            {
                Check(Flag(controller.Tick(), "lobby_map_image_ready"), "actual decoded map path makes the authored lobby image visible");
                var preview = Bounds("lobby_map_preview"); var image = Bounds("lobby_map_image");
                Check(preview.Contains(image) && Bounds("lobby_left_panels").Contains(image),
                    "actual generated map image remains inside the visible bottom-left preview and left rail");
                Capture("actual-room-map-preview");
            }
            Check(NativeSemanticReadback.Read(host, lobby).ContainsText("ADD BOT"),
                "eligible empty capacity exposes the actual authored Add Bot control");
            Capture("eligible-empty-row");

            Queue("lobby_add_bot2", pointer: true);
            // Change the authoritative revision AFTER rendering/queuing input.
            // The actual controller must capture fresh state while dispatching.
            authority.Roster = authority.Roster with { SessionRevision = 27 };
            Drain(RmlUiIntentKind.LobbyPlayerSelect, 2, accepted: true);
            Check(authority.Requests.Count == 1, "one native empty-row click submits one existing owner request");
            var request = authority.Requests.Single();
            Check(request.Operation == RmlUiAdminOperation.AddBot
                && request.MatchId == authority.Roster.MatchId && request.AuthorityEpoch == authority.Roster.AuthorityEpoch
                && request.SessionRevision == 27 && request.TargetSlot == byte.MaxValue && request.TargetGeneration == 0
                && request.Hunter == Hunter.Random && request.Suit == 0 && request.BotLevel == 1,
                "Add Bot uses the fresh existing request identity and default bot policy, never the presentation index as a slot");
            Frame();
            Check(authority.Roster.Players.Length == 2 && !Flag(controller.Tick(), "slot2_occupied")
                && !controller.AdminOpen,
                "accepted request neither invents a bot row nor opens occupant administration");
            Capture("submitted-no-optimistic-row");

            // Only a replacement authority snapshot may populate a row.
            authority.Roster = authority.Roster with { SessionRevision = 28,
                Players = authority.Roster.Players.Append(Actor(6, "SYNTHETIC ACKNOWLEDGED BOT", true)).ToArray() };
            Settle();
            Check(Flag(controller.Tick(), "slot2_occupied")
                && Text(controller.Tick(), "slot2_name") == "SYNTHETIC ACKNOWLEDGED BOT"
                && !host.FocusDocument(lobby, "lobby_add_bot2"),
                "only explicit synthetic authority acknowledgement replaces the empty capacity control");

            foreach (string gate in new[] { "lobby-nonowner", "roster-nonowner", "pending", "starting", "roster-capacity", "lobby-capacity" })
            {
                authority.Reset(); Settle();
                Queue("lobby_add_bot2", pointer: false);
                // Retain the real queued native event while authority changes.
                switch (gate)
                {
                    case "lobby-nonowner": authority.LobbyOwner = false; break;
                    case "roster-nonowner": authority.Roster = authority.Roster with { Owner = false }; break;
                    case "pending": authority.Roster = authority.Roster with { CommandPending = true }; break;
                    case "starting": authority.Starting = true; break;
                    case "roster-capacity": authority.Roster = authority.Roster with { MaxPlayers = 2 }; break;
                    case "lobby-capacity": authority.MaxPlayers = 2; break;
                }
                int before = authority.Requests.Count;
                Drain(RmlUiIntentKind.LobbyPlayerSelect, 2, accepted: false);
                Settle();
                Check(authority.Requests.Count == before && !controller.AdminOpen,
                    "queued empty-row command cannot submit after authoritative gate changes: " + gate);
                Check(!host.FocusDocument(lobby, "lobby_add_bot2"),
                    "current native empty-row action is unavailable under gate: " + gate);
                evidence.Add(new { gate, requests = authority.Requests.Count, authority.Roster.SessionRevision });
            }

            authority.Reset(); Settle();
            Queue("lobby_add_bot2", pointer: true);
            var occupant = Actor(6, "SYNTHETIC NEW OCCUPANT", false);
            authority.Roster = authority.Roster with { SessionRevision = 31,
                Players = authority.Roster.Players.Append(occupant).ToArray() };
            int beforeOccupied = authority.Requests.Count;
            Drain(RmlUiIntentKind.LobbyPlayerSelect, 2, accepted: true);
            Settle();
            Check(authority.Requests.Count == beforeOccupied && controller.AdminOpen
                && Text(controller.Tick(), "admin_selected_name") == occupant.Name
                && pages.Top != lobby && host.CurrentInputDocument == pages.Top,
                "queued stale empty-row action resolves the fresh occupant into existing administration without adding a bot");
            Capture("stale-row-current-occupant-admin");
            Queue("admin_close", pointer: false);
            Drain(RmlUiIntentKind.LobbyAdminClose, 0, accepted: true);
            Frame();
            Check(!controller.AdminOpen && pages.Top == lobby && host.CurrentInputDocument == lobby,
                "existing native admin Back restores the original Lobby document");
            MeasureRails("after-owner-actions");
            Check(GL.GetError() == ErrorCode.NoError, "Lobby layout and actual actions leave no GL error");
            File.WriteAllText(Path.Combine(output, $"lobby-{width}x{height}.json"), JsonSerializer.Serialize(new
            { passed = true, claim = Claim, ownedArt, mapPng, width, height, requests = authority.Requests, evidence },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            Capture("failure");
            File.WriteAllText(Path.Combine(output, $"lobby-{width}x{height}.json"), JsonSerializer.Serialize(new
            { passed = false, claim = Claim, ownedArt, mapPng, width, height, requests = authority.Requests, evidence, error = error.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }

        finally { LauncherBackdropComposer.Release(); }

        void Frame()
        {
            var snapshot = controller.Tick();
            if (snapshot.Revision > presented)
            {
                foreach (var item in snapshot.Bindings)
                    if (item.Value.Kind == RmlUiBindingKind.Text) layout.SetText(item.Key, item.Value.Text);
                    else layout.SetBool(item.Key, item.Value.Boolean);
                presented = snapshot.Revision;
            }
            layout.Flush(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            if (window != null)
            {
                RmlUiHunterStageRenderer.Draw(window, host, stagePage, false,
                    authority.Hub(), authority.Lobby(), authority.Roster, width, height);
            }
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void RequireProceduralChamber(bool lobbyMode)
        {
            Check(window != null, "procedural stage check uses the one existing RenderWindow");
            int program = (int)typeof(LauncherBackdropComposer)
                .GetField("_program", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Check(program != 0 && GL.IsProgram(program),
                "original procedural chamber shader is created by the actual stage renderer");
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            int modeLocation = GL.GetUniformLocation(program, "lobby_mode");
            int floorLocation = GL.GetUniformLocation(program, "floor_glow");
            int homeLocation = GL.GetUniformLocation(program, "home_party_mode");
            Check(linked != 0 && modeLocation >= 0 && floorLocation >= 0 && homeLocation >= 0,
                "original chamber program links with active platform and formation uniforms");
            GL.GetUniform(program, modeLocation, out float mode);
            GL.GetUniform(program, floorLocation, out float floorGlow);
            GL.GetUniform(program, homeLocation, out float homePartyMode);
            Check(mode == (lobbyMode ? 1f : 0f) && homePartyMode == (lobbyMode ? 0f : 1f) && floorGlow > 0,
                "actual stage selects the correct Home platform or Lobby formation with floor glow");
            for (int i = 0; i < LauncherLobbyFormation.Capacity; i++)
            {
                int pad = GL.GetUniformLocation(program, "lobby_pad_geometry[" + i + "]");
                Check(pad >= 0, "original procedural program retains authored Lobby platform " + i);
            }
            evidence.Add(new { phase = lobbyMode ? "lobby-program" : "home-program", program, linked, mode, homePartyMode, floorGlow });
        }
        void Settle()
        {
            var timer = Stopwatch.StartNew();
            do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < 450);
            Frame();
        }
        Box Bounds(string id, RmlUiDocumentToken document = default)
        {
            if (document == default) document = lobby;
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                && float.IsFinite(x) && float.IsFinite(y) && w > 0 && h > 0,
                "actual laid-out native bounds exist: " + id);
            return new(x, y, w, h);
        }
        void MeasureRails(string phase)
        {
            var stage = Bounds("lobby_stage"); var left = Bounds("lobby_left_panels");
            var roster = Bounds("lobby_roster_panel"); var chat = Bounds("lobby_chat_panel");
            var screen = new Box(0, 0, width, height);
            Check(screen.Contains(stage) && stage.Contains(left) && stage.Contains(roster) && stage.Contains(chat),
                "left rail, roster and chat remain fully inside actual Lobby stage");
            Check(Math.Abs(left.Width - roster.Width) <= 2 && Math.Abs(left.Width - chat.Width) <= 2,
                "actual right roster/chat widths match the established left rail");
            Check(Math.Abs(roster.X - chat.X) <= 2 && Math.Abs(roster.Right - chat.Right) <= 2
                && left.Right < roster.X && roster.X - left.Right >= stage.Width * .30f,
                "aligned right rail preserves at least thirty percent of the chamber between panels");
            Check(roster.Bottom <= chat.Y + 1, "roster and chat occupy separate vertical regions");
            for (int i = authority.Roster.Players.Length; i < authority.MaxPlayers; i++)
                if (RmlUiLobbyDisplayOrder.CanAddBot(authority.Lobby(), authority.Roster, i))
                    Check(roster.Contains(Bounds("lobby_add_bot" + i)), "every eligible native Add Bot remains fully inside roster: " + i);
            evidence.Add(new { phase, stage, left, roster, chat });
        }
        void Queue(string id, bool pointer)
        {
            Frame(); var document = pages.Top;
            Check(host.FocusDocument(document, id), "actual native focus reaches " + id);
            Settle(); var box = Bounds(id, document);
            Check(new Box(0, 0, width, height).Contains(box), "focused native action fits framebuffer: " + id);
            if (id.StartsWith("lobby_add_bot", StringComparison.Ordinal))
                Check(Bounds("lobby_roster_panel").Contains(box), "Add Bot remains inside its clipped roster: " + id);
            host.DiscardIntents();
            if (pointer)
            {
                double x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
                host.Input.PointerMoved(x, y);
                // Nested OPEN SLOT / ADD BOT labels may own the center hit.
                // Find a real exposed point on the actual button, then dispatch
                // the native pointer event through its existing click listener.
                foreach (float fraction in new[] { .04f, .96f, .15f, .85f })
                {
                    if (host.HoveredElement() == id) break;
                    x = box.X + box.Width * fraction;
                    host.Input.PointerMoved(x, y);
                }
                Check(host.HoveredElement() == id, "actual native hit test reaches " + id);
                host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false);
            }
            else { host.Input.Key(2, true); host.Input.Key(2, false); }
            host.Update();
        }
        void Drain(RmlUiIntentKind kind, int argument, bool accepted)
        {
            int count = 0;
            // Same actual host -> composed foreground admission -> MenuController
            // dispatch path as the production Lobby; no decoded or forged intent.
            while (host.TryTakeIntent(out var raw))
            {
                Check(raw.Kind == kind && raw.Argument == argument && raw.Document == pages.Top,
                    "actual native event has the current document and expected indexed action");
                Check(layout.HandleIntent(raw, out var forwarded) && forwarded == raw,
                    "production composed foreground admission forwards native Lobby command");
                Check(controller.Dispatch(forwarded.Kind, forwarded.Argument) == accepted,
                    "existing owner/controller accepts or refuses the actual native command");
                Check(layout.HandleIntent(raw, out var duplicate) && duplicate.Kind == 0,
                    "duplicate native sequence cannot submit a second owner request");
                count++;
            }
            Check(count == 1, "one native activation emits exactly one admitted command");
        }
        void Capture(string phase)
        {
            Frame(); var pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-lobby-{phase}-{width}x{height}.ppm");
            using var stream = File.Create(path);
            stream.Write(Encoding.ASCII.GetBytes($"P6\n# EXPLICIT SYNTHETIC AUTHORITY / ACTUAL NATIVE HOME / LOBBY GL\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            evidence.Add(new { phase, capture = path });
        }
    }

    private static bool Flag(RmlUiMenuSnapshot snapshot, string key)
        => snapshot.Bindings.TryGetValue(key, out var value) && value.Boolean;
    private static string Text(RmlUiMenuSnapshot snapshot, string key)
        => snapshot.Bindings.TryGetValue(key, out var value) ? value.Text : "";
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("RML LOBBY RESTORATION GL PASS " + message);
    }
    private static RmlUiAdminPlayer Actor(byte slot, string name, bool bot)
        => new(slot, (ushort)(40 + slot), name, slot == 7 ? Hunter.Weavel : Hunter.Samus, 0, -1, false, bot, 1, 0, 0);

    private static NativePartyView SyntheticParty()
    {
        string Id(int n) => "PP-0000-0000-0000-0000-" + n.ToString("X4");
        return new(true, false, "EXPLICIT SYNTHETIC PARTY", 1,
            new(Id(0), "00000000-0000-0000-0000-000000000001", Id(0), 1,
                Enumerable.Range(0, 4).Select(i => new NativePartyMember(Id(i),
                    i == 0 ? "SYNTHETIC LOCAL OWNER" : "SYNTHETIC PARTY MEMBER " + i, NativePresence.Online)).ToArray(),
                Array.Empty<NativePartyInvite>(), Array.Empty<NativePartyInvite>()));
    }
    private sealed class FixtureAuthority : IRmlUiMenuServices
    {
        public RmlUiAdminSnapshot Roster = null!;
        public bool LobbyOwner, Starting;
        public bool LobbyActive = true;
        public NativePartyView Party = new(false, false, "SYNTHETIC DISCONNECTED", 0, null);
        public int SocialRoutes;
        public NativePartyView PartyHome() => Party;
        public int MaxPlayers;
        public string MapPreview = "";
        public List<RmlUiAdminRequest> Requests { get; } = new();
        public FixtureAuthority() => Reset();
        public void Reset()
        {
            LobbyOwner = true; Starting = false; MaxPlayers = 8;
            Roster = new(137, 2468, 20, true, 7, 7, false, false, 0, 8, false,
                "SYNTHETIC MAP STATUS", "SYNTHETIC AUTHORITY // NO NETWORK",
                new[] { Hunter.Samus, Hunter.Weavel }, new[] { Actor(7, "SYNTHETIC LOCAL OWNER", false), Actor(4, "SYNTHETIC REMOTE", false) },
                Array.Empty<string>());
        }
        public HubSnapshot Hub() => new("SYNTHETIC LOCAL OWNER", Hunter.Weavel, Hunter.Weavel, 0, true, "EXPLICIT LOBBY FIXTURE");
        public string BuildVersion() => "synthetic-lobby-restoration";
        public NewsDispatch? FeaturedNews() => new BundledNewsProvider().Read().FirstOrDefault();
        public RmlUiSocialHomeState SocialHome() => new(true, true,
            new[] { "SYNTHETIC FRIEND ALPHA", "SYNTHETIC FRIEND BRAVO" }, 0)
        {
            PresenceVerified = true, SharingPresence = 2,
            FriendActivities = new[] { "SYNTHETIC LOBBY", "SYNTHETIC MENU" },
            FriendJoinable = new[] { false, false },
            InvitationsVerified = true, IncomingInvitations = 0
        };
        public RmlUiLobbyView Lobby() => new(LobbyActive, Starting, LobbyOwner, false, true, true, false,
            "SYNTHETIC LOBBY", MapPreview.Length > 0 ? "AD1 TRANSFER LOCK BT" : "SYNTHETIC MAP", "Battle", "Free for all",
            "NO NETWORK // SYNTHETIC AUTHORITY", MaxPlayers, RmlUiLobbyDisplayOrder.Ordered(Roster)
                .Select(p => new RmlUiLobbyPlayer(p.Name, p.Hunter.ToString(), p.Ready, p.Slot == Roster.LocalSlot, p.Bot)).ToArray());
        public RmlUiAdminSnapshot? ReadAdmin() => Roster;
        public bool SubmitAdmin(RmlUiAdminRequest request) { Requests.Add(request); return true; }
        public RmlUiLobbyChrome ReadLobbyChrome() => new("7:00", "7", "SYNTHETIC CHAT", "NO NETWORK",
            "EXPLICIT FIXTURE", MapPreview.Length > 0 ? "AD1 TRANSFER LOCK BT // ACTUAL ROOM PREVIEW" : "NO MAP ASSET IN THIS CHECK", "", "")
            { MapPreviewPath = MapPreview };
        public IReadOnlyList<string> BuiltInArenaKeys() => Array.Empty<string>();
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "FIXTURE", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, new LaunchPlan(), "FIXTURE"));
        public bool AcceptJoin(LaunchPlan plan) => false;
        public bool OpenCreateLobby() => false;
        public bool NavigateTo(RmlUiRouteArgument destination)
        { if (destination != RmlUiRouteArgument.Social) return false; SocialRoutes++; return true; }
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
