using System.Diagnostics;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual shipped Home DOM/layout and pointer routes, with synthetic bounded account data only.</summary>
internal static class HomeRailsGlAcceptance
{
    private const string Scope = "Actual native Home/launcher composition, authored bundled news, Current Session/Friends layout, right-rail scrolling, pointer route into native Social, semantic readback and OpenGL framebuffer. Synthetic social/party/account fixtures only; no network, authentication, live membership, gameplay, process launch or performance acceptance.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string density = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY") ?? "1";
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Home rail qualification requires explicit private PROJECT_PRIME_USER_DATA.");
        string output = Path.Combine(root, "home-rails-gl", width + "x" + height + "-density-" + density);
        Directory.CreateDirectory(output);
        int checks = 0;
        var measurements = new List<object>();
        var captures = new List<string>();
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            Case(false); Case(true);
            Check(GL.GetError() == ErrorCode.NoError, "Home rail qualification leaves no GL error");
            Save(true, null);
        }
        catch (Exception error) { Save(false, error.ToString()); throw; }

        void Case(bool connected)
        {
            string name = connected ? "verified-friends" : "disconnected";
            var services = new Services(connected);
            using var controller = new RmlUiMenuController(services);
            using var layout = new RmlUiLauncherPages(host);
            long shown = -1;
            NativeSocialFriendsPage? social = null;
            try
            {
                Settle();
                Check(layout.Page == RmlUiMenuPage.Home, "real launcher composes Home for " + name);
                Capture("initial");
                var model = controller.Tick().Bindings;
                Check(model["home_party_count"].Text == (connected ? "4 / 4" : "1 / 4"), "Current Session retains four bounded seats for " + name);
                var activity = Box("activity_panel"); var rail = Box("home_right_rail");
                Check(Disjoint(activity, rail), "right rail cannot cover activity controls for " + name);
                if (width / float.Parse(density, System.Globalization.CultureInfo.InvariantCulture) >= 640
                    && (width / float.Parse(density, System.Globalization.CultureInfo.InvariantCulture) <= 900
                    || height / float.Parse(density, System.Globalization.CultureInfo.InvariantCulture) <= 490))
                {
                    var leftRail = Box("home_left_rail");
                    Check(rail.X - leftRail.Right >= width * .40f,
                        "compact Home utility rails preserve at least forty percent center chamber width");
                }
                var session = Box("session_panel"); var friends = Box("home_social_rail");
                Check(friends.Y >= session.Bottom - 1, "Friends remains below Current Session in natural flow for " + name);
                var seats = new[] { "home_party_slot0", "home_social", "home_party_slot2", "home_party_slot3" }.Select(Box).ToArray();
                for (int i = 1; i < seats.Length; i++)
                    Check(seats[i].Y >= seats[i - 1].Bottom - 1, "Current Session seats do not overlap: " + i);
                var dispatch = Reach("home_feature_open", "home_left_rail");
                Check(Disjoint(dispatch, activity) && Disjoint(dispatch, rail),
                    "featured Dispatch action has positive bounds separate from fixed activity and Friends");
                var semantics = NativeSemanticReadback.Read(host, layout.Document);
                Check(semantics.ContainsText("READ DISPATCH"), "compact featured news retains its complete news route");
                Capture("dispatch-reached");
                Reach("activity_selector", "home_left_rail"); Reach("deploy", "home_left_rail");
                if (connected)
                {
                    Check(model["home_friends_online"].Text == "2" && model["home_invites_count"].Text == "2"
                        && model["home_requests_count"].Text == "1", "Home counts come from verified synthetic friend, invitation and presence projections");
                    foreach (string id in new[] { "home_friend0", "home_friend1", "home_friend2" }) Reach(id);
                    Check(model["home_friend2_activity"].Text == "PRESENCE NOT SHARED"
                        && !model["home_friend2_joinable"].Boolean, "unshared friend presence does not invent online or joinable status");
                }
                else
                {
                    Reach("home_social_empty");
                    Check(model["home_friends_online"].Text == "UNKNOWN" && model["home_invites_count"].Text == "NOT SYNCED"
                        && model["home_requests_count"].Text == "NOT SYNCED" && !model["home_friend0_visible"].Boolean
                        && model["home_social_empty_text"].Text == "SOCIAL SERVICE UNAVAILABLE // OPEN SOCIAL FOR STATUS",
                        "disconnected Friends is visible and honest about unavailable private data");
                }
                var open = Reach("home_social_open");
                Capture("social-reached");
                host.DiscardIntents();
                float px = open.X + open.W / 2, py = open.Y + open.H / 2;
                Check(!Contains(activity, px, py), "Open Social pointer center cannot click through an activity control");
                host.Input.PointerMoved(px, py); host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
                Check(host.TryTakeIntent(out var intent) && intent.Kind == RmlUiIntentKind.Navigate
                    && intent.Argument == (int)RmlUiRouteArgument.Social && intent.Document == layout.Document
                    && !host.TryTakeIntent(out _), "native Open Social pointer emits one exact Social route");
                Check(layout.HandleIntent(intent, out var forwarded) && forwarded.Kind == RmlUiIntentKind.Navigate
                    && forwarded.Argument == (int)RmlUiRouteArgument.Social
                    && controller.Dispatch(forwarded.Kind, forwarded.Argument)
                    && services.SocialRoutes == 1 && services.OtherCalls == 0, "real launcher/controller forwards only the requested Social route");
                social = new NativeSocialFriendsPage(host, layout.Manager, new SocialService(services), localLobby: () => null);
                social.Open(); Settle(220);
                var socialText = NativeSemanticReadback.Read(host, layout.Manager.Page);
                Check(layout.Manager.Page != intent.Document && socialText.ContainsText("FRIENDS & PARTY")
                    && (!connected || socialText.ContainsText("SYNTHETIC FRIEND ALPHA")),
                    "pointer route reaches actual native Social content without external network");
                Capture("social-open");
            }
            finally { social?.Dispose(); }

            void Frame()
            {
                if (social == null)
                {
                    var state = controller.Tick();
                    if (state.Revision != shown)
                    {
                        foreach (var pair in state.Bindings)
                            if (pair.Value.Kind == RmlUiBindingKind.Text) layout.SetText(pair.Key, pair.Value.Text);
                            else layout.SetBool(pair.Key, pair.Value.Boolean);
                        shown = state.Revision;
                    }
                    layout.Flush();
                    host.SetBool(layout.Document, "class:screen:reduce-motion", true);
                    host.SetBool(layout.Document, "class:@document:reduce-motion", true);
                }
                else social.Refresh();
                host.Update(); if (social == null) layout.AfterUpdate();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
                GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
                RmlUiOpenGlFrame.Draw(host, width, height);
            }
            void Settle(int milliseconds = 180)
            { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds); }
            Rect Box(string id)
            {
                bool found = host.TryGetElementBounds(layout.Document, id, out float x, out float y, out float w, out float h);
                measurements.Add(new { fixture = name, id, found, x, y, width = w, height = h });
                Check(found && w > 0 && h > 0, "actual native positive bounds: " + id + " // " + name);
                return new(x, y, w, h);
            }
            Rect Reach(string id, string scrollRail = "home_right_rail")
            {
                var clip = Box(scrollRail);
                Rect? lastTarget = null;
                for (int i = 0; i < 180; i++)
                {
                    if (host.TryGetElementBounds(layout.Document, id, out float x, out float y, out float w, out float h))
                    {
                        var target = new Rect(x, y, w, h);
                        lastTarget = target;
                        if (w > 0 && h > 0 && x >= clip.X - 1 && target.Right <= clip.Right + 1
                            && y >= clip.Y - 1 && target.Bottom <= clip.Bottom + 1 && x >= 0 && y >= 0
                            && target.Right <= width + 1 && target.Bottom <= height + 1)
                        { measurements.Add(new { fixture = name, rail = scrollRail, reached = id, scrollSteps = i, target }); Check(true, "native rail scroll reaches " + id); return target; }
                    }
                    host.Input.PointerMoved(clip.X + clip.W / 2, clip.Y + clip.H / 2);
                    host.Input.PointerWheel(i == 0 ? 10000 : i < 90 ? -3 : 3);
                    Settle(12);
                }
                measurements.Add(new { fixture = name, rail = scrollRail, unreachable = id, clip, lastTarget });
                throw new InvalidOperationException("Native rail scroll cannot reach " + id);
            }
            void Capture(string label)
            {
                Frame(); string path = Path.Combine(output, name + "-" + label + ".png");
                Check(ScreenCapture.SaveWindow(width, height, path), "capture native Home rails " + name + " " + label);
                FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
                File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, social == null ? layout.Document : layout.Manager.Page).RawJson);
                captures.Add(path);
            }
        }
        void Check(bool pass, string label)
        { if (!pass) throw new InvalidOperationException(label); checks++; Console.WriteLine("HOME RAIL GL PASS " + label); }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(
            new { passed, checks, width, height, density, scope = Scope, measurements, captures, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private readonly record struct Rect(float X, float Y, float W, float H)
    { public float Right => X + W; public float Bottom => Y + H; }
    private static bool Disjoint(Rect a, Rect b) => a.Right <= b.X + 1 || b.Right <= a.X + 1 || a.Bottom <= b.Y + 1 || b.Bottom <= a.Y + 1;
    private static bool Contains(Rect a, float x, float y) => x >= a.X && x < a.Right && y >= a.Y && y < a.Bottom;
    private static string Id(int i) => $"PP-AAAA-BBBB-CCCC-DDDD-{i:X4}";
    private sealed class Services : IRmlUiMenuServices
    {
        internal readonly bool Connected;
        internal readonly NativeSocialDirectory Directory;
        internal int SocialRoutes, OtherCalls;
        private readonly RmlUiSocialHomeState _social;
        internal Services(bool connected)
        {
            Connected = connected;
            object witness = new(); var now = DateTimeOffset.UtcNow;
            string[] names = { "SYNTHETIC FRIEND ALPHA", "SYNTHETIC FRIEND BRAVO", "SYNTHETIC FRIEND CHARLIE" };
            Directory = new(Id(1), names.Select((name, i) => new NativeSocialPerson(Id(i + 2), name, NativeSocialRelation.Friend))
                .Append(new NativeSocialPerson(Id(10), "SYNTHETIC REQUEST", NativeSocialRelation.IncomingRequest)).ToArray())
                { AccountWitness = witness, InvitationsAvailable = true,
                    Invitations = Enumerable.Range(0, 2).Select(i => new NativeSocialInvitation((100 + i).ToString("X8") + "-1111-4111-8111-111111111111", Id(2), names[0], true,
                        (200 + i).ToString("X8") + "-1111-4111-8111-111111111111", "SYNTHETIC ROOM", "SYNTHETIC SERVER", "pending", now.AddMinutes(-1), now.AddMinutes(10))).ToArray() };
            var presence = new NativePresenceDirectory(Id(1), new("friends", "friends", "friends", false), new[]
            {
                new NativePresencePerson(Id(2), names[0], "in_match", "SYNTHETIC ROOM", "", false, now),
                new NativePresencePerson(Id(3), names[1], "menu", "", "", false, now)
            }) { AccountWitness = witness };
            _social = connected ? RmlUiSocialHomeState.Project(Directory, presence,
                Directory.People.Count(person => person.Relation == NativeSocialRelation.IncomingRequest), now) with
                {
                    InvitationsVerified = Directory.InvitationsAvailable,
                    IncomingInvitations = Directory.Invitations.Count(invite => NativeSocialInvitationValidation.Valid(invite)
                        && invite.Incoming && invite.Status == "pending" && invite.ExpiresAt > now)
                }
                : new(false, false, Array.Empty<string>(), 0);
        }
        public RmlUiSocialHomeState SocialHome() => _social;
        public NativePartyView PartyHome() => Connected ? new(true, false, "SYNTHETIC VERIFIED", 1,
            new NativePartySnapshot(Id(1), "00000001-1111-4111-8111-111111111111", Id(1), 1,
                Enumerable.Range(1, 4).Select(i => new NativePartyMember(Id(i), "SYNTHETIC MEMBER " + i, NativePresence.Unknown)).ToArray(),
                Array.Empty<NativePartyInvite>(), Array.Empty<NativePartyInvite>())) : new(false, false, "DISCONNECTED", 0, null);
        public HubSnapshot Hub() => new("SYNTHETIC HUNTER", Hunter.Samus, Hunter.Samus, 0, true, "NO NETWORK");
        public string BuildVersion() => "synthetic-home-rails";
        public NewsDispatch? FeaturedNews() => new BundledNewsProvider().Read()[0];
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public Task<RmlUiDirectory> Browse(CancellationToken token) { OtherCalls++; throw new InvalidOperationException("No network"); }
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) { OtherCalls++; throw new InvalidOperationException("No network"); }
        public Task<RmlUiJoinResult> Join(string endpoint, string playerName, CancellationToken token) { OtherCalls++; throw new InvalidOperationException("No network"); }
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) { OtherCalls++; throw new InvalidOperationException("No network"); }
        public bool NavigateTo(RmlUiRouteArgument route) { if (route == RmlUiRouteArgument.Social) { SocialRoutes++; return true; } OtherCalls++; return false; }
        public bool AcceptJoin(LaunchPlan plan) { OtherCalls++; return false; }
        public bool OpenCreateLobby() { OtherCalls++; return false; }
        public bool OpenStudio() { OtherCalls++; return false; }
        public bool SetReady() => false; public bool StartMatch() => false; public bool LeaveLobby() => false;
        public bool NextHunter() => false; public bool NextSuit() => false; public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public IReadOnlyList<string> BuiltInArenaKeys() => new[] { "MP1 SANCTORUS" };
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "NO NETWORK"; return false; }
        public void Quit() { OtherCalls++; }
    }
    private sealed class SocialService(Services services) : INativeSocialFriendsService
    {
        public Task<NativeSocialDirectory> ReadAsync(CancellationToken cancellation) => services.Connected
            ? Task.FromResult(services.Directory) : Task.FromException<NativeSocialDirectory>(new InvalidOperationException("Synthetic service unavailable"));
        public Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken cancellation) => throw new InvalidOperationException("No lookup authorized");
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId, CancellationToken cancellation) => throw new InvalidOperationException("No mutation authorized");
    }
}
