using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

// Real native document/input/pixels. Queue receipts and all authorities are synthetic.
internal static class CommunityTransfersGlAcceptance
{
    private const string Scope = "Actual native Community Discover/transfer modal, native pointer/Enter intents, focus, scrolling, visible bounds and GL captures; injected synthetic opaque queue receipts only. No CommunityTransferManager, live queue, files, backend, network, installation, account, physical hardware input, performance or release acceptance. Run separately at 640x360, 960x600 and 1280x720; each manifest reports its observed dimensions only.";

    internal static void Run(RmlUiHost host, int width, int height, int clientWidth, int clientHeight)
    {
        string user = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(user)) throw new ArgumentException("--community-transfers requires absolute private PROJECT_PRIME_USER_DATA.");
        user = Path.GetFullPath(user);
        string canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Project Prime");
        if (user == canonical || user.StartsWith(canonical + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Synthetic transfer acceptance cannot use player data.");
        string output = Path.Combine(user, "community-transfers-gl");
        if (Directory.Exists(output) || File.Exists(output)) throw new ArgumentException("Preserve prior attempts; use fresh private output.");
        Directory.CreateDirectory(output);
        var expectedNative = OperatingSystem.IsMacOS() ? LoadedNativeModuleCheck.CaptureExpectedPackage(AppContext.BaseDirectory) : null;
        if (expectedNative != null) LoadedNativeModuleCheck.WriteNew(Path.Combine(output, "loaded-native-module.json"), LoadedNativeModuleCheck.RequireLoaded(expectedNative));

        int checks = 0; string status = "INCOMPLETE", failure = "";
        var captures = new List<object>();
        var authority = new FixtureAuthority();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeCommunityDiscoverPage(host, pages, authority, authority, authority, authority, authority);
        // Read-only inspection of the actual page's portable controller. No private state is mutated.
        var transfers = (NativeCommunityTransfersPage)typeof(NativeCommunityDiscoverPage)
            .GetField("_transfers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        var controller = (NativeCommunityTransfersController)typeof(NativeCommunityTransfersPage)
            .GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(transfers)!;
        RmlUiDocumentToken document = default, parent = default;
        host.Input.SetFramebufferScale(1, 1);
        try
        {
            page.Open(); parent = pages.Page;
            Wait(() => host.FocusDocument(parent, "native_community_transfers"), "Discover transfer entry becomes focusable");
            Activate("native_community_transfers", RmlUiIntentKind.CommunityTab, 2, keyboard: true);
            document = pages.Top; Ready();
            Check(document != parent && transfers.IsOpen && State().Rows.Count == 8 && State().Total == 10,
                "real Discover opens an eight-row native transfer modal over its parent");
            Check(!host.FocusDocument(document, "community_transfers_previous") && host.FocusDocument(document, "community_transfers_next"),
                "first-page boundary disables Previous and permits Next");
            for (int i = 0; i < 8; i++)
            {
                Activate("community_transfers_row" + i, RmlUiIntentKind.CommunitySelect, i, keyboard: i % 2 == 0);
                Check(State().Selected?.Id == FixtureAuthority.Id(i), "native row selects exact opaque job " + i);
            }
            Footer(); Capture("first-page-scrolled-eight-rows");
            Activate("community_transfers_next", RmlUiIntentKind.CommunityPage, 1);
            Check(State().Page == 1 && State().Rows.Count == 2 && State().Selected?.Id == FixtureAuthority.Id(8), "Next selects the first job of final two-row slice");
            Check(!host.FocusDocument(document, "community_transfers_next") && host.FocusDocument(document, "community_transfers_previous")
                && !host.TryGetElementBounds(document, "community_transfers_row2", out _, out _, out _, out _), "last page hides unused rows and disables Next");
            Footer(); Capture("last-page-boundaries");
            Activate("community_transfers_previous", RmlUiIntentKind.CommunityPage, -1);

            Activate("community_transfers_row0", RmlUiIntentKind.CommunitySelect, 0);
            Actions(0, 2); Capture("active-download-actions");
            Activate("community_transfers_action0", RmlUiIntentKind.CommunityTransferAction, 0); Ready();
            Check(authority.Commands.Last() is { Action: NativeCommunityTransferAction.Pause, Id: var paused } && paused == FixtureAuthority.Id(0)
                && State().Selected?.State == CommunityTransferState.Paused, "Pause sends exact synthetic job witness and verified readback shows Paused");
            Actions(1, 2, 3);
            Activate("community_transfers_action1", RmlUiIntentKind.CommunityTransferAction, 1, keyboard: true); Ready();
            Check(authority.Commands.Last().Action == NativeCommunityTransferAction.Resume && State().Selected?.State == CommunityTransferState.Downloading,
                "Enter Resume returns the synthetic download to its verified running state");

            int commands = authority.Commands.Count;
            Activate("community_transfers_action2", RmlUiIntentKind.CommunityTransferAction, 2);
            Check(State().Confirmation == NativeCommunityTransferAction.Cancel && authority.Commands.Count == commands
                && !host.FocusDocument(document, "community_transfers_row1"), "Cancel transfer requires confirmation and fences row retargeting");
            Footer(); Capture("cancel-review");
            Activate("community_transfers_close", RmlUiIntentKind.CommunityCancel, 0, keyboard: true); Ready();
            Check(transfers.IsOpen && State().Confirmation == null && authority.Commands.Count == commands, "Keep Transfer dismisses confirmation without closing or mutating job");
            Activate("community_transfers_action2", RmlUiIntentKind.CommunityTransferAction, 2);
            Activate("community_transfers_confirm", RmlUiIntentKind.CommunityConfirm, 0); Ready();
            Check(authority.Commands.Count == commands + 1 && State().Selected?.State == CommunityTransferState.Cancelled,
                "explicit confirmation cancels only captured synthetic transfer once");
            Actions(3);
            Activate("community_transfers_action3", RmlUiIntentKind.CommunityTransferAction, 3);
            Check(authority.Commands.Count == commands + 1 && State().Confirmation == NativeCommunityTransferAction.Forget,
                "Remove from History also requires explicit review");
            Activate("community_transfers_close", RmlUiIntentKind.CommunityCancel, 0); Ready();
            Check(State().Total == 10, "dismissing Remove keeps the receipt");
            Activate("community_transfers_action3", RmlUiIntentKind.CommunityTransferAction, 3);
            Activate("community_transfers_confirm", RmlUiIntentKind.CommunityConfirm, 0); Ready();
            Check(State().Total == 9 && authority.Commands.Last().Action == NativeCommunityTransferAction.Forget
                && authority.Commands.Last().Id == FixtureAuthority.Id(0), "confirmed Remove forgets exact cancelled receipt and repages current history");

            // Completed download is now first after removing row zero.
            Activate("community_transfers_row0", RmlUiIntentKind.CommunitySelect, 0);
            Actions(3);
            var completedSemantics = NativeSemanticReadback.Read(host, document);
            File.WriteAllText(Path.Combine(output, "completed-download-semantic-snapshot.json"), completedSemantics.RawJson);
            Check(completedSemantics.Label("community_transfers_detail").Contains("Download completed. Choose this map in Discover to install it; installation rechecks its exact package.", StringComparison.Ordinal),
                "completed download directs installation through Discover and explicitly rechecks its exact package");
            Capture("completed-download-install-guidance");
            Activate("community_transfers_row1", RmlUiIntentKind.CommunitySelect, 1); Actions(1, 2, 3);
            authority.Progress(FixtureAuthority.Id(2), CommunityTransferState.Uploading, 700);
            Activate("community_transfers_refresh", RmlUiIntentKind.CommunityRefresh, 0); Ready(); Actions(0, 2);
            Check(State().Selected is { Received: 700, Total: 1000, State: CommunityTransferState.Uploading }
                && NativeSemanticReadback.Read(host, document).ContainsText($"{700:N0} / {1000:N0} BYTES"), "refresh publishes exact synthetic progress and state into real native detail");
            Footer(); Capture("upload-progress");
            Activate("community_transfers_row2", RmlUiIntentKind.CommunitySelect, 2); Actions(2, 3);
            Check(NativeSemanticReadback.Read(host, document).ContainsText("Reopen Publish from the owning account"),
                "retained upload needing authorization routes the user back through Publish review");
            Activate("community_transfers_row6", RmlUiIntentKind.CommunitySelect, 6); Actions(2, 3);
            Check(NativeSemanticReadback.Read(host, document).ContainsText("Review the current revision in My Uploads"),
                "upload needing review explains the existing creator revision workflow");
            Activate("community_transfers_row1", RmlUiIntentKind.CommunitySelect, 1);

            int beforeStaleWitness = authority.Commands.Count;
            authority.HoldReads = true; // An incidental poll must not reconcile this deliberate stale review first.
            authority.Progress(FixtureAuthority.Id(2), CommunityTransferState.Uploading, 800);
            Activate("community_transfers_action0", RmlUiIntentKind.CommunityTransferAction, 0); Ready();
            Check(authority.Commands.Count == beforeStaleWitness && State().Selected?.Received == 700
                && State().Status.Contains("COULD NOT BE VERIFIED", StringComparison.Ordinal),
                "changed opaque job witness refuses control and keeps prior display until explicit verified refresh");
            authority.HoldReads = false; authority.Release();
            Activate("community_transfers_refresh", RmlUiIntentKind.CommunityRefresh, 0); Ready();
            Check(State().Selected?.Received == 800, "explicit refresh reconciles the changed synthetic queue witness");

            int beforeFence = authority.Commands.Count;
            Check(!page.Handle(new(RmlUiIntentKind.CommunityTransferAction, 0, parent, ulong.MaxValue)), "hidden parent document cannot control transfer modal");
            var foreground = pages.OpenModal(new("synthetic-transfers-fence", "pages/social/confirm.rml", "social_confirm_cancel")); Frame();
            Check(!page.Handle(new(RmlUiIntentKind.CommunityTransferAction, 0, document, ulong.MaxValue)) && authority.Commands.Count == beforeFence,
                "unrelated foreground document fences queued transfer controls");
            pages.CloseModal(); Check(!host.IsAlive(foreground), "foreground fence retires");
            pages.SetVisible(false);
            Check(!page.Handle(new(RmlUiIntentKind.CommunityTransferAction, 0, document, ulong.MaxValue)) && authority.Commands.Count == beforeFence,
                "hidden page manager refuses modal transfer intents");
            pages.SetVisible(true); Frame();

            authority.HoldReads = true;
            Activate("community_transfers_refresh", RmlUiIntentKind.CommunityRefresh, 0);
            Wait(() => authority.Pending.Count == 1 && State().Busy, "refresh is genuinely pending on synthetic authority");
            Check(authority.Pending.TryDequeue(out var held), "capture exact pending observation");
            var retired = document;
            Activate("community_transfers_close", RmlUiIntentKind.CommunityCancel, 0);
            Check(!transfers.IsOpen && !host.IsAlive(retired) && pages.Top == parent && held!.Token.IsCancellationRequested,
                "Back retires modal and cancels observation while retaining shared synthetic jobs");
            Check(!page.Handle(new(RmlUiIntentKind.CommunityTransferAction, 0, retired, ulong.MaxValue)), "stale document cannot dispatch after Back");
            authority.HoldReads = false;
            Activate("native_community_transfers", RmlUiIntentKind.CommunityTab, 2); document = pages.Top; Ready();
            Check(document != retired && State().Total == 9, "reopen creates fresh modal and rehydrates retained synthetic jobs");
            held!.Completion.SetResult(new[] { FixtureAuthority.Row(31, CommunityTransferKind.Upload, CommunityTransferState.Paused, "LATE RETIRED POLL") });
            Wait(() => Volatile.Read(ref authority.LateReturned) == 1, "late cancelled synthetic read actually returns");
            Frames(100);
            Check(State().Total == 9 && State().Rows.All(row => row.Title != "LATE RETIRED POLL"), "retired poll cannot replace freshly reopened history");
            Footer(); Capture("reopened-after-late-poll");
            Activate("community_transfers_close", RmlUiIntentKind.CommunityCancel, 0);
            Check(host.FocusedElement() == "native_community_transfers" && authority.InstallAttempts == 0 && authority.UnexpectedCalls == 0,
                "modal Back restores actual Discover entry focus with no installer, creator or revision backend call");
            Check(authority.Commands.All(command => command.ExactOpaqueWitness), "all accepted synthetic controls retained the exact opaque queue witness");
            Check(GL.GetError() == ErrorCode.NoError, "native synthetic transfer journey leaves no GL error");
            if (expectedNative != null) LoadedNativeModuleCheck.WriteNew(Path.Combine(output, "loaded-native-module-post.json"), LoadedNativeModuleCheck.RequireLoaded(expectedNative));
            status = "LOCAL_SYNTHETIC_COMMUNITY_TRANSFERS_GL_FIXTURE_PASS";
        }
        catch (Exception error)
        {
            failure = error.ToString();
            if (document != default && host.IsAlive(document)) try { Capture("failure"); } catch { }
            throw;
        }
        finally
        {
            authority.Release();
            using var stream = new FileStream(Path.Combine(output, "manifest.json"), FileMode.CreateNew);
            JsonSerializer.Serialize(stream, new { schema = 1, status, scope = Scope, checks, failure,
                client = new { width = clientWidth, height = clientHeight }, framebuffer = new { width, height }, captures,
                commands = authority.Commands.ToArray(), lateReturned = authority.LateReturned, authority.InstallAttempts, authority.UnexpectedCalls },
                new JsonSerializerOptions { WriteIndented = true });
        }

        NativeCommunityTransfersView State() => controller.Tick();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("RML COMMUNITY TRANSFERS GL: " + label); checks++; Console.WriteLine("RML COMMUNITY TRANSFERS GL PASS " + label); }
        void Frame()
        {
            page.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Frames(int milliseconds) { var watch = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (watch.ElapsedMilliseconds < milliseconds); }
        void Wait(Func<bool> predicate, string label)
        {
            var watch = Stopwatch.StartNew(); do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); } while (watch.Elapsed < TimeSpan.FromSeconds(3));
            throw new TimeoutException("RML COMMUNITY TRANSFERS GL: " + label);
        }
        void Ready() => Wait(() => !State().Busy && State().Total > 0, "verified synthetic transfer readback settles");
        void Actions(params int[] allowed)
        {
            Frame(); for (int i = 0; i < 4; i++) Check(host.FocusDocument(document, "community_transfers_action" + i) == allowed.Contains(i), "contextual native action focusability " + i + " = " + allowed.Contains(i));
        }
        void Footer()
        {
            var ids = new List<string> { "community_transfers_previous", "community_transfers_next", "community_transfers_refresh", "community_transfers_close" };
            var state = State();
            for (int action = 0; action < 4; action++)
                if (state.Selected is { } selected && NativeCommunityTransfersController.Allows(selected, (NativeCommunityTransferAction)action))
                    ids.Add("community_transfers_action" + action);
            if (state.Confirmation.HasValue) ids.Add("community_transfers_confirm");
            Frame(); foreach (string id in ids)
                Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                    && x >= 0 && y >= 0 && w > 0 && h >= 28 && x + w <= width + 1 && y + h <= height + 1,
                    "persistent native footer stays inside framebuffer: " + id);
        }
        void Activate(string id, RmlUiIntentKind kind, int argument, bool keyboard = false)
        {
            Frame(); var active = pages.Top; host.DiscardIntents();
            Check(host.FocusDocument(active, id), "actual native focus reaches " + id); Frames(40);
            if (active == document && id.StartsWith("community_transfers_row", StringComparison.Ordinal))
                Reveal(id);
            if (keyboard) { host.Input.Key(2, true); host.Input.Key(2, false); }
            else
            {
                Check(host.TryGetElementBounds(active, id, out float x, out float y, out float w, out float h) && w > 0 && h > 0
                    && x >= 0 && y >= 0 && x + w <= width + 1 && y + h <= height + 1,
                    "actual native pointer center fits framebuffer: " + id);
                host.Input.PointerButton(0, x + w / 2d, y + h / 2d, true); host.Input.PointerButton(0, x + w / 2d, y + h / 2d, false);
            }
            host.Update(); int received = 0;
            while (host.TryTakeIntent(out var intent))
            {
                Check(intent.Kind == kind && intent.Argument == argument && intent.Document == active, "exact native " + (keyboard ? "Enter" : "pointer") + " intent: " + id);
                Check(RmlUiPageCommandRouting.Community(intent.Kind), "production root routing delegates actual native transfer intent: " + id);
                Check(page.Handle(intent) && !page.Handle(intent), "native sequence handles once and rejects duplicate: " + id); received++;
            }
            Check(received == 1, "exactly one native intent: " + id); Frame();
        }
        void Reveal(string id)
        {
            Check(host.TryGetElementBounds(document, "community_transfers_scroll", out float sx, out float sy, out float sw, out float sh) && sh > 0,
                "actual modal scroll viewport exists");
            host.Input.PointerMoved(sx + sw / 2d, sy + sh / 2d); host.Input.PointerWheel(1000); Frames(40);
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                    && y >= sy && y + h <= sy + sh && x >= 0 && x + w <= width) return;
                // Native visible bounds may be absent for a fully clipped child.
                // Scan down from the known top rather than treating missing bounds as y=0.
                host.Input.PointerMoved(sx + sw / 2d, sy + sh / 2d); host.Input.PointerWheel(-1); Frames(20);
            }
            throw new InvalidOperationException("Native modal control cannot be scrolled into viewport: " + id);
        }
        void Capture(string name)
        {
            Frame(); byte[] pixels = FinalCompositeCapture.Read(width, height); int painted = 0;
            for (int i = 0; i < pixels.Length; i += 3) if (Math.Abs(pixels[i] - 6) + Math.Abs(pixels[i + 1] - 13) + Math.Abs(pixels[i + 2] - 20) > 24) painted++;
            Check(painted > width * height / 300, "native modal paints positive framebuffer pixels: " + name);
            string path = Path.Combine(output, "synthetic-" + name + ".png"); GL.Finish();
            Check(ScreenCapture.SaveWindow(width, height, path), "actual framebuffer PNG captured: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            captures.Add(new { name, path = Path.GetFileName(path), paintedPixels = painted, state = State(), focused = host.FocusedElement() });
        }
    }

    private sealed record Held(TaskCompletionSource<NativeCommunityTransferReceipt[]> Completion, CancellationToken Token);
    private sealed record Command(Guid Id, NativeCommunityTransferAction Action, string Hash, bool ExactOpaqueWitness);
    private sealed class FixtureAuthority : INativeCommunityTransfersAuthority, INativeCommunityReader, INativeCommunityInstaller,
        INativeCommunityCreatorAuthority, INativeCommunityRevisionsAuthority
    {
        private readonly object _gate = new();
        private NativeCommunityTransferReceipt[] _rows = Enumerable.Range(0, 10).Select(i => Row(i,
            i is 0 or 1 or 4 or 8 ? CommunityTransferKind.Download : CommunityTransferKind.Upload,
            i switch { 0 => CommunityTransferState.Downloading, 1 => CommunityTransferState.Completed, 2 => CommunityTransferState.Paused,
                3 => CommunityTransferState.NeedsAuthorization, 4 => CommunityTransferState.Retryable, 5 => CommunityTransferState.Failed,
                6 => CommunityTransferState.Cancelled, 7 => CommunityTransferState.NeedsReview, 8 => CommunityTransferState.Queued, _ => CommunityTransferState.Completed })).ToArray();
        internal readonly ConcurrentQueue<Command> Commands = new();
        internal readonly ConcurrentQueue<Held> Pending = new();
        internal volatile bool HoldReads;
        internal int LateReturned, InstallAttempts, UnexpectedCalls;
        internal static Guid Id(int index) => Guid.Parse($"{index + 1:x8}-1111-4111-8111-111111111111");
        internal static NativeCommunityTransferReceipt Row(int index, CommunityTransferKind kind, CommunityTransferState state, string? title = null)
            => new(Id(index), kind, state, (index + 1).ToString("x64"), title ?? "SYNTHETIC TRANSFER " + index,
                state == CommunityTransferState.Completed ? 1000 : 250, 1000, DateTimeOffset.UnixEpoch.AddDays(100 - index)) { QueueWitness = new object() };
        public async Task<NativeCommunityTransferReceipt[]> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HoldReads) { lock (_gate) return _rows.ToArray(); }
            var completion = new TaskCompletionSource<NativeCommunityTransferReceipt[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(new(completion, token));
            var result = await completion.Task.ConfigureAwait(false); // Deliberately returns after cancellation to test retirement.
            Interlocked.Increment(ref LateReturned); return result;
        }
        public Task<NativeCommunityTransferReceipt[]> ControlAsync(NativeCommunityTransferReceipt expected, NativeCommunityTransferAction action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); lock (_gate)
            {
                var current = _rows.Single(row => row.Id == expected.Id);
                bool witness = ReferenceEquals(current.QueueWitness, expected.QueueWitness);
                if (!witness || current != expected) throw new InvalidOperationException("Synthetic stale queue witness.");
                Commands.Enqueue(new(current.Id, action, current.PackageHash, witness));
                _rows = action == NativeCommunityTransferAction.Forget ? _rows.Where(row => row.Id != current.Id).ToArray()
                    : _rows.Select(row => row.Id != current.Id ? row : row with { State = action switch {
                        NativeCommunityTransferAction.Pause => CommunityTransferState.Paused,
                        NativeCommunityTransferAction.Resume => row.Kind == CommunityTransferKind.Download ? CommunityTransferState.Downloading : CommunityTransferState.Uploading,
                        NativeCommunityTransferAction.Cancel => CommunityTransferState.Cancelled, _ => throw new InvalidOperationException() }, QueueWitness = new object() }).ToArray();
                return Task.FromResult(_rows.ToArray());
            }
        }
        internal void Progress(Guid id, CommunityTransferState state, long received)
        { lock (_gate) _rows = _rows.Select(row => row.Id == id ? row with { State = state, Received = received, QueueWitness = new object() } : row).ToArray(); }
        internal void Release() { while (Pending.TryDequeue(out var pending)) pending.Completion.TrySetCanceled(); }
        public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken token)
            => Task.FromResult(new NativeCommunityPage(Array.Empty<NativeCommunityEntry>(), 0, 1, 8));
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected, CancellationToken token)
        { Interlocked.Increment(ref InstallAttempts); throw new InvalidOperationException("Synthetic fixture must not install."); }
        public void Register(NativeCommunityInstalled installed) { Interlocked.Increment(ref InstallAttempts); throw new InvalidOperationException("Synthetic fixture must not register."); }
        public Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token) => Unexpected<NativeCreatorPage>();
        public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid mapId, CancellationToken token) => Unexpected<NativeCreatorEntry?>();
        public Task<bool> ExecuteAsync(NativeCreatorAction action, Guid mapId, CancellationToken token) => Unexpected<bool>();
        public Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request, CancellationToken token) => Unexpected<NativeCommunityRevisionCatalog>();
        public Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion request, CancellationToken token) => Unexpected<NativeCommunityRevisionCatalog>();
        public bool IsCurrentAccountLease(object? lease) => false;
        private Task<T> Unexpected<T>() { Interlocked.Increment(ref UnexpectedCalls); throw new InvalidOperationException("Synthetic transfer fixture crossed unrelated authority."); }
    }
}
