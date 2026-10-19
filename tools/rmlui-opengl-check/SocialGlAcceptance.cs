using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

// Native document/input/render acceptance only. Every service and account witness is synthetic.
internal static class SocialGlAcceptance
{
    private const string Scope = "Actual native RmlUi Social document, pointer/Enter routing, visible-element bounds and GL framebuffer; injected synthetic friends, invitations, presence, account witness and local lobby only. No authentication, backend, network transport, NetSession, gameplay, OS-focus, performance or release acceptance. Visibility assertions read native visible-element bounds; text assertions inspect submitted managed bindings, not native DOM text readback.";
    private static string Id(int n) => $"PP-AAAA-BBBB-CCCC-DDDD-{n:X4}";
    private static string Uuid(int n) => $"{n:X8}-1111-4111-8111-111111111111";

    internal static void Run(RmlUiHost host, int width, int height, int clientWidth, int clientHeight)
    {
        string user = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(user))
            throw new ArgumentException("--social-invites requires an explicit absolute private PROJECT_PRIME_USER_DATA.");
        user = Path.GetFullPath(user);
        string canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Project Prime");
        if (user == canonical || user.StartsWith(canonical + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Never use the canonical player directory for synthetic Social acceptance.");
        string output = Path.Combine(user, "social-invites-gl");
        if (Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("Social acceptance requires a fresh output directory; preserve previous attempts.");
        Directory.CreateDirectory(output);
        var expectedNative = OperatingSystem.IsMacOS()
            ? LoadedNativeModuleCheck.CaptureExpectedPackage(AppContext.BaseDirectory) : null;
        if (expectedNative != null)
            LoadedNativeModuleCheck.WriteNew(Path.Combine(output, "loaded-native-module.json"),
                LoadedNativeModuleCheck.RequireLoaded(expectedNative));

        int checks = 0;
        string status = "INCOMPLETE", failure = "";
        var captures = new List<object>();
        var authority = new FixtureService();
        using var presence = new NativePresenceController(new FixturePresence(authority));
        using var pages = new RmlUiPageManager(host);
        var local = new NativePartyLocalLobby(42, 100, "SYNTHETIC LOCAL ROOM", "SYNTHETIC LOCAL SERVER");
        using var page = new NativeSocialFriendsPage(host, pages, authority, presence, () => local);
        RmlUiDocumentToken document = default;
        // Read-only observations; no private controller/host state is written by the fixture.
        var controller = (NativeSocialFriendsController)typeof(NativeSocialFriendsPage)
            .GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            presence.Refresh(); page.Open(); document = pages.Page;
            Ready();
            Check(controller.Current().Selected == null, "fresh synthetic Social has no selected friend or invitation");
            Enabled("social_recent", true);
            Activate("social_recent", 5);
            Activate("social_row_0", 13);
            Check(controller.Current().Tab == "RECENT" && controller.Current().Selected is
                { Relation: NativeSocialRelation.Friend, RecentEncounters: 3 }
                && BindingText("social_row_detail_0").Contains("3 ENCOUNTERS", StringComparison.Ordinal),
                "native Recent tab selects exact verified fixture person and displays encounter metadata");
            Capture("recent-players");
            Activate("social_friends", 0);
            VisibleCommands();
            Activate("social_invites", 3);
            Activate("social_row_1", 14, keyboard: true);
            Check(controller.Current().SelectedInvitation?.InviteId == Uuid(101),
                "native Enter selects the second UUID even when both rows name the same friend");
            Enabled("social_command_30", true); Enabled("social_command_31", true); Enabled("social_command_32", false);
            VisibleCommands(30, 31);
            Check(BindingText("social_command_30") == "ACCEPT GAME INVITE",
                "managed pending-incoming command label asks to accept the game invitation");
            Capture("incoming-second-uuid");
            Activate("social_command_31", 31); Ready();
            var declined = authority.Commands.Last();
            Check(declined.Action == NativeSocialLobbyAction.Decline && declined.Invitation is { } declinedInvite
                && declinedInvite.InviteId == Uuid(101) && declinedInvite.PrimeId == Id(2) && !page.TryTakeJoin(out _),
                "native Decline binds the selected incoming UUID and produces no join");

            Reload(); Activate("social_invites", 3); Activate("social_row_2", 15);
            Enabled("social_command_30", false); Enabled("social_command_31", false); Enabled("social_command_32", true);
            VisibleCommands(32);
            Capture("outgoing-direction");
            Activate("social_command_32", 32); Ready();
            Check(authority.Commands.Last() is { Action: NativeSocialLobbyAction.Cancel, Invitation.InviteId: var cancelled }
                && cancelled == Uuid(102) && !page.TryTakeJoin(out _),
                "native Cancel binds the outgoing UUID rather than either same-friend incoming invitation");

            Reload(); Activate("social_invites", 3); Activate("social_row_0", 13);
            authority.HoldLobby = true;
            Activate("social_command_30", 30);
            Wait(() => authority.PendingLobby.Count == 1 && controller.Current().Busy, "synthetic invitation operation is genuinely pending");
            Enabled("social_invites", false); Enabled("social_row_0", false); Enabled("social_command_30", false);
            VisibleCommands(30, 31);
            Enabled("social_cancel_refresh", true); Capture("pending-invitation");
            Activate("social_cancel_refresh", 8);
            Check(!controller.Current().Busy && !page.TryTakeJoin(out _), "native cancellation retires pending invitation work without joining");
            Check(authority.PendingLobby.TryDequeue(out var pending) && pending.Token.IsCancellationRequested,
                "pending fake service observes its exact cancelled token");
            pending!.Completion.SetResult(new(authority.Snapshot, authority.Target(authority.Snapshot)));
            Wait(() => Volatile.Read(ref authority.CompletedHeldLobby) == 1, "deliberately late fake service completion returned");
            Frames(80);
            Check(!page.TryTakeJoin(out _) && !page.JoinPending, "cancelled generation cannot publish the late invitation join target");
            authority.HoldLobby = false;

            authority.HoldReads = true; Activate("social_refresh", 7);
            Wait(() => authority.PendingReads.Count == 1 && controller.Current().Busy, "native refresh waits for held fake read");
            Activate("social_cancel_refresh", 8);
            Check(authority.PendingReads.TryDequeue(out var read) && read.Token.IsCancellationRequested,
                "native refresh cancellation cancels the captured fake read token");
            read!.Completion.SetResult(authority.Snapshot);
            Wait(() => Volatile.Read(ref authority.CompletedHeldReads) == 1, "deliberately late fake directory completion returned");
            Frames(80);
            Check(!controller.Current().Connected && controller.Current().Total == 0,
                "cancelled refresh cannot restore its late private directory rows");
            authority.HoldReads = false; Reload();

            Activate("social_friends", 0); Activate("social_row_0", 13);
            VisibleCommands(25, 26, 28, 29, 33);
            int beforeModal = authority.Commands.Count;
            var modal = pages.OpenModal(new("synthetic-social-fence", "pages/social/confirm.rml", "social_confirm_cancel"));
            Frame();
            Check(!page.Handle(new(RmlUiIntentKind.SocialAction, 28, document, ulong.MaxValue))
                && !page.Handle(new(RmlUiIntentKind.SocialAction, 28, host.HomeDocument, ulong.MaxValue))
                && authority.Commands.Count == beforeModal,
                "foreground modal and wrong document fence background Social invitation actions");
            Capture("foreground-modal"); pages.CloseModal();
            Check(!host.IsAlive(modal), "synthetic modal retires before foreground Social resumes");
            Activate("social_command_28", 28); Ready();
            Check(authority.Commands.Last() is { Action: NativeSocialLobbyAction.Invite, Friend.PrimeId: var invited }
                && invited == Id(2) && authority.Commands.Last().LocalLobby == local && !page.TryTakeJoin(out _),
                "Invite captures the exact selected friend and injected local lobby without starting a session");
            Wait(() => !presence.Current().Busy, "synthetic current presence settles before Join Friend");
            Enabled("social_command_29", true); Activate("social_command_29", 29); Ready();
            NativeSocialJoinTarget? friendTarget = null;
            Wait(() => page.TryTakeJoin(out friendTarget!), "current synthetic presence enables one Join Friend target");
            Check(authority.Commands.Last().Action == NativeSocialLobbyAction.JoinFriend && page.IsJoinCurrent(friendTarget!)
                && !page.TryTakeJoin(out _), "Join Friend target is current and drains exactly once");
            authority.RotateAccount(); Frame();
            Check(!page.IsJoinCurrent(friendTarget!) && !page.JoinPending && !controller.Current().Connected,
                "account retirement clears private Social rows and invalidates the exact pending join");
            page.JoinCompleted(friendTarget!, true, "STALE SYNTHETIC COMPLETION");
            Check(!page.Closed, "retired account completion cannot close the Social page");
            Enabled("social_invites", false); Capture("account-retired");

            Reload(); Activate("social_invites", 3); Activate("social_row_0", 13);
            Activate("social_command_30", 30); Ready();
            NativeSocialJoinTarget? target = null;
            Wait(() => page.TryTakeJoin(out target!), "native incoming Accept emits a synthetic join locator");
            var acceptedTarget = target ?? throw new InvalidOperationException("Accept returned no target.");
            Check(authority.Commands.Last() is { Action: NativeSocialLobbyAction.Accept, Invitation.InviteId: var accepted }
                && accepted == Uuid(100) && acceptedTarget.LobbyId == Uuid(200) && page.IsJoinCurrent(acceptedTarget)
                && !page.IsJoinCurrent(acceptedTarget with { }) && !page.TryTakeJoin(out _),
                "Accept binds the first invitation UUID/lobby and only its exact one-shot target remains current");
            Enabled("social_command_30", false); Enabled("social_refresh", false);
            VisibleCommands(30);
            Check(BindingText("social_command_30") == "JOIN LOBBY",
                "managed accepted-incoming command label retries joining its lobby");
            Capture("join-admission-pending");
            const string refused = "SYNTHETIC ADMISSION REFUSED // NO SESSION CREATED";
            page.JoinCompleted(acceptedTarget, false, refused); Frame();
            Check(!page.Closed && !page.JoinPending && host.IsVisible(document)
                && BindingText("social_status") == refused,
                "refused join keeps the native page visible and submits the exact refusal binding");
            Enabled("social_command_30", true); Capture("join-refused-retry");
            Activate("social_command_30", 30); Ready();
            NativeSocialJoinTarget? retry = null;
            Wait(() => page.TryTakeJoin(out retry!), "explicit native retry emits a fresh join target");
            Check(!ReferenceEquals(acceptedTarget, retry) && page.IsJoinCurrent(retry!), "retry never reuses the refused target object");
            page.JoinCompleted(acceptedTarget, true, "STALE REFUSED TARGET");
            Check(!page.Closed && page.IsJoinCurrent(retry!), "stale completion cannot consume the current retry");
            page.JoinCompleted(retry!, true, "SYNTHETIC ACCEPTANCE");
            Check(page.Closed && !page.JoinPending && !page.IsJoinCurrent(retry!) && !page.TryTakeJoin(out _),
                "accepted completion closes the page owner and retires its join target");
            Check(!page.Handle(new(RmlUiIntentKind.SocialAction, 7, document, ulong.MaxValue)),
                "closed page refuses delayed native actions");
            page.Dispose();
            Check(!host.IsAlive(document), "Social disposal retires its actual native document");
            Check(GL.GetError() == ErrorCode.NoError, "synthetic Social interactions leave no OpenGL error");
            if (expectedNative != null)
                LoadedNativeModuleCheck.WriteNew(Path.Combine(output, "loaded-native-module-post.json"),
                    LoadedNativeModuleCheck.RequireLoaded(expectedNative));
            status = "LOCAL_SYNTHETIC_SOCIAL_GL_FIXTURE_PASS";
            Console.WriteLine($"RML SOCIAL GL {checks} checks passed; synthetic services only: {output}");
        }
        catch (Exception error)
        {
            failure = error.ToString();
            if (document != default && host.IsAlive(document))
                try { Capture("failure"); } catch (Exception captureError) { Console.WriteLine("SOCIAL FAILURE CAPTURE " + captureError.Message); }
            throw;
        }
        finally
        {
            authority.ReleasePending();
            using var stream = new FileStream(Path.Combine(output, "manifest.json"), FileMode.CreateNew);
            JsonSerializer.Serialize(stream, new { schema = 1, status, scope = Scope, checks, failure,
                client = new { width = clientWidth, height = clientHeight }, framebuffer = new { width, height },
                hardware = new { vendor = GL.GetString(StringName.Vendor),
                    renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version) },
                captures, syntheticLobbyCommands = authority.Commands.ToArray(),
                lateHeldReadsReturned = authority.CompletedHeldReads, lateHeldLobbyReturned = authority.CompletedHeldLobby },
                new JsonSerializerOptions { WriteIndented = true });
        }

        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("RML SOCIAL GL: " + name);
            ++checks; Console.WriteLine("RML SOCIAL GL PASS " + name);
        }
        void Frame()
        {
            presence.Tick(); page.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Frames(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            do { Frame(); Thread.Sleep(5); } while (watch.ElapsedMilliseconds < milliseconds);
        }
        void Wait(Func<bool> predicate, string label)
        {
            var watch = Stopwatch.StartNew();
            do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); }
            while (watch.Elapsed < TimeSpan.FromSeconds(3));
            throw new TimeoutException("RML SOCIAL GL: " + label);
        }
        void Ready() => Wait(() => !controller.Current().Busy && controller.Current().Connected, "native Social settles its verified synthetic directory");
        void Reload() { authority.ResetInvitations(); Activate("social_refresh", 7); Ready(); }
        void Enabled(string id, bool enabled)
            => Check(host.FocusDocument(document, id) == enabled, "native enabled/focus state " + id + " = " + enabled);
        void VisibleCommands(params int[] visible)
        {
            Frame();
            for (int command = 21; command <= 33; command++)
            {
                string id = "social_command_" + command;
                bool shown = host.TryGetElementBounds(document, id, out _, out _, out _, out _);
                Check(shown == visible.Contains(command),
                    "native visible element/border bounds " + id + " = " + visible.Contains(command));
            }
        }
        void Activate(string id, int argument, bool keyboard = false)
        {
            Frame(); var active = pages.Top; host.DiscardIntents();
            Check(host.FocusDocument(active, id), "native focus reaches " + id); Frames(450);
            if (keyboard) { host.Input.Key(2, true); host.Input.Key(2, false); }
            else
            {
                Check(host.TryGetElementBounds(active, id, out float x, out float y, out float w, out float h)
                    && w > 0 && h > 0, "native bounds resolve " + id);
                double px = x + w / 2, py = y + h / 2;
                Check(px >= 0 && px < width && py >= 0 && py < height, "native pointer center is inside framebuffer: " + id);
                host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false);
            }
            host.Update(); int received = 0;
            while (host.TryTakeIntent(out var intent))
            {
                Check(intent.Kind == RmlUiIntentKind.SocialAction && intent.Argument == argument && intent.Document == active,
                    "native " + (keyboard ? "Enter" : "pointer") + " emitted exact Social intent for " + id);
                Check(page.Handle(intent) && !page.Handle(intent), "Social handles once and rejects duplicate native sequence: " + id);
                ++received;
            }
            Check(received == 1, "exactly one native action emitted: " + id); Frame();
        }
        string BindingText(string id)
        {
            var documents = (IDictionary)typeof(RmlUiHost).GetField("_documents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
            var state = documents[document.DocumentId]!;
            var bindings = (Dictionary<string, RmlUiBindingValue>)state.GetType().GetField("Bindings")!.GetValue(state)!;
            return bindings.TryGetValue(id, out var value) ? value.Text : "";
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "synthetic-" + name + ".png");
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            int painted = 0;
            for (int at = 0; at < pixels.Length; at += 3)
                if (Math.Abs(pixels[at] - 6) + Math.Abs(pixels[at + 1] - 13) + Math.Abs(pixels[at + 2] - 20) > 24) ++painted;
            Check(painted > width * height / 300, "native Social paints positive framebuffer pixels: " + name);
            GL.Finish(); Check(ScreenCapture.SaveWindow(width, height, path), "actual native Social PNG capture: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            captures.Add(new { name, path = Path.GetFileName(path), paintedPixels = painted, state = controller.Current(),
                joinPending = page.JoinPending, closed = page.Closed, managedStatusBinding = BindingText("social_status") });
        }
    }

    private sealed record Held<T>(TaskCompletionSource<T> Completion, CancellationToken Token);
    private sealed class FixtureService : INativeSocialFriendsService
    {
        private readonly object _gate = new();
        private object _lease = new();
        private NativeSocialDirectory _snapshot = null!;
        internal readonly ConcurrentQueue<NativeSocialLobbyCommand> Commands = new();
        internal readonly ConcurrentQueue<Held<NativeSocialDirectory>> PendingReads = new();
        internal readonly ConcurrentQueue<Held<NativeSocialLobbyResult>> PendingLobby = new();
        internal volatile bool HoldReads, HoldLobby;
        internal int CompletedHeldReads, CompletedHeldLobby;
        internal FixtureService() => ResetInvitations();
        internal NativeSocialDirectory Snapshot { get { lock (_gate) return _snapshot; } }
        internal void RotateAccount() { lock (_gate) _lease = new(); }
        internal void ResetInvitations()
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                _snapshot = new(Id(1), new[] { new NativeSocialPerson(Id(2), "SYNTHETIC SAME FRIEND", NativeSocialRelation.Friend) })
                {
                    AccountWitness = _lease, InvitationsAvailable = true,
                    RecentAvailable = true,
                    RecentPlayers = new[] { new NativeSocialRecentPlayer(Id(2), "SYNTHETIC SAME FRIEND", now.AddMinutes(-2), 3) },
                    Invitations = Enumerable.Range(0, 3).Select(i => new NativeSocialInvitation(Uuid(100 + i), Id(2),
                        "SYNTHETIC SAME FRIEND", i != 2, Uuid(200 + i), "SYNTHETIC ROOM " + i,
                        "SYNTHETIC SERVER " + i, "pending", now.AddMinutes(-1).AddSeconds(-i), now.AddMinutes(20))).ToArray()
                };
            }
        }
        public bool IsCurrent(NativeSocialDirectory value) { lock (_gate) return ReferenceEquals(value.AccountWitness, _lease); }
        internal NativePresenceDirectory Presence()
        {
            lock (_gate) return new(Id(1), new("friends", "friends", "friends", false),
                new[] { new NativePresencePerson(Id(2), "SYNTHETIC SAME FRIEND", "lobby", "SYNTHETIC ROOM", Uuid(200), true, DateTimeOffset.UtcNow) }) { AccountWitness = _lease };
        }
        internal bool CurrentPresence(NativePresenceDirectory value) { lock (_gate) return ReferenceEquals(value.AccountWitness, _lease); }
        internal NativeSocialJoinTarget Target(NativeSocialDirectory directory, string? lobby = null)
            => new(directory.SelfPrimeId, lobby ?? Uuid(200), "203.0.113.7", 27015, 57, 100,
                "SYNTHETIC ROOM", "SYNTHETIC SERVER", DateTimeOffset.UtcNow.AddMinutes(20)) { AccountWitness = directory.AccountWitness };
        public async Task<NativeSocialDirectory> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HoldReads) return Snapshot;
            var completion = new TaskCompletionSource<NativeSocialDirectory>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingReads.Enqueue(new(completion, token));
            var result = await completion.Task.ConfigureAwait(false); // Deliberately ignores cancellation to test retirement.
            Interlocked.Increment(ref CompletedHeldReads); return result;
        }
        public Task<NativeSocialPerson?> LookupAsync(string id, CancellationToken token)
            => throw new InvalidOperationException("Synthetic invitation fixture must not perform account lookup.");
        public Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string id, CancellationToken token)
            => throw new InvalidOperationException("Synthetic invitation fixture must not mutate friendship state.");
        public async Task<NativeSocialLobbyResult> ExecuteLobbyAsync(NativeSocialLobbyCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Commands.Enqueue(command);
            if (!IsCurrent(command.Directory)) throw new InvalidOperationException("Synthetic account witness changed.");
            if (HoldLobby)
            {
                var completion = new TaskCompletionSource<NativeSocialLobbyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                PendingLobby.Enqueue(new(completion, token));
                var late = await completion.Task.ConfigureAwait(false);
                Interlocked.Increment(ref CompletedHeldLobby); return late;
            }
            lock (_gate)
            {
                if (command.Invitation is { } invitation)
                    _snapshot = _snapshot with { Invitations = _snapshot.Invitations.Select(i => i.InviteId == invitation.InviteId
                        ? i with { Status = command.Action == NativeSocialLobbyAction.Decline ? "declined"
                            : command.Action == NativeSocialLobbyAction.Cancel ? "cancelled" : "accepted" } : i).ToArray() };
                return new(_snapshot, command.Action is NativeSocialLobbyAction.Accept or NativeSocialLobbyAction.JoinFriend
                    ? Target(_snapshot, command.Invitation?.LobbyId) : null);
            }
        }
        internal void ReleasePending()
        {
            while (PendingReads.TryDequeue(out var read)) read.Completion.TrySetCanceled();
            while (PendingLobby.TryDequeue(out var lobby)) lobby.Completion.TrySetCanceled();
        }
    }
    private sealed class FixturePresence(FixtureService authority) : INativePresenceService
    {
        public bool IsCurrent(NativePresenceDirectory value) => authority.CurrentPresence(value);
        public Task<NativePresenceDirectory> ReadAsync(CancellationToken token) => Task.FromResult(authority.Presence());
        public Task<NativePresenceDirectory> HeartbeatAsync(string self, Guid session, string activity, string room, CancellationToken token)
            => Task.FromResult(authority.Presence());
        public Task<NativePresenceDirectory> PrivacyAsync(string self, NativeSocialPrivacy expected, NativeSocialPrivacy next, CancellationToken token)
            => throw new InvalidOperationException("Synthetic invitation fixture must not change privacy.");
        public Task LeaveAsync(string self, Guid session, CancellationToken token) => Task.CompletedTask;
    }
}
