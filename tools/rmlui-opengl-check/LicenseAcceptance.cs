using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.License;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class LicenseAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        LicenseAdapterAcceptance.Run();
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-license-rml41"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi OpenGL framebuffer captures. All Hunter License values are deterministic synthetic fixture data. No network calls, real account data or extracted game assets are used.\n");
        using var pages = new RmlUiPageManager(host);
        var service = new FixtureReader();
        using var page = new NativeLicenseOverviewPage(pages, service, host);
        page.Open();
        RmlUiDocumentToken document = pages.Page;
        host.Input.SetFramebufferScale(1, 1);
        Frame();
        Check(host.IsAlive(document), "actual native License document loads and accepts bindings");
        Check(host.FocusedElement() == "native_license_tab_overview", "initial focus belongs to the Overview tab");

        Click("native_license_tab_history", 3);
        bool loaded = false;
        for (int i = 0; i < 200 && !loaded; i++)
        {
            page.Refresh();
            host.Update();
            loaded = Visible("native_license_match_0");
            if (!loaded) Thread.Sleep(5);
        }
        Check(loaded && Visible("native_license_match_7"), "synthetic 25-match snapshot reaches actual native rows");

        foreach (var (name, action) in new[] { ("overview", 0), ("stats", 2), ("history", 3), ("achievements", 4),
            ("comparison", 7), ("account", 8), ("emblems", 5), ("titles", 6) })
        {
            Click("native_license_tab_" + name, action);
            foreach (string face in new[] { "overview", "stats", "history", "achievements", "comparison", "account", "emblems", "titles" })
                Check(Visible("native_license_face_" + face) == (face == name), "native face visibility " + name + " / " + face);
            Capture(name);
        }

        Click("native_license_tab_history", 3);
        for (int i = 0; i < 3; i++) Click("native_license_next_page", 12);
        Check(Visible("native_license_match_0") && !Visible("native_license_match_1"),
            "fourth history page shows only the final synthetic match");
        Capture("history-page-4");
        Click("native_license_previous_page", 11);
        Check(Visible("native_license_match_7"), "previous history page restores eight native match rows");

        Click("native_license_tab_account", 8);
        Check(!host.FocusDocument(document, "native_license_recover"),
            "verified guest with career history cannot recover over its existing career");
        FillCredentials();
        Click("native_license_send_verification", 13, CheckCredentialsCleared);
        WaitUntil(() => Volatile.Read(ref service.AccountCalls) == 1,
            "actual native verification click dispatches asynchronous account request");
        Check(service.LastAccountAction == NativeLicenseAccountAction.SendVerification
            && service.LastEmail == "synthetic-account@example.test" && service.LastPassword == ""
            && !host.FocusDocument(document, "native_license_send_verification"),
            "verification extracts only its email, clears credentials and blocks duplicate account requests");
        Capture("account-pending", scrollToOrigin: false);
        service.ConfirmAccount();
        WaitUntil(() => host.FocusDocument(document, "native_license_email"),
            "native verification completion refreshes authoritative account fields");

        // Simulate completing email verification outside the game. No actual
        // browser, provider, credentials or account service participates.
        service.CompleteSyntheticVerification();
        Click("native_license_refresh_account", 19);
        WaitUntil(() => host.FocusDocument(document, "native_license_finish_password"),
            "verified linked-email fixture enables native password action after sync");
        FillCredentials();
        Click("native_license_finish_password", 14, CheckCredentialsCleared);
        WaitUntil(() => Volatile.Read(ref service.AccountCalls) == 2,
            "native password click dispatches the synthetic account writer");
        Check(service.LastAccountAction == NativeLicenseAccountAction.SetPassword
            && service.LastEmail == "" && service.LastPassword == "synthetic-password-only",
            "password action extracts the two matching native password fields without submitting email");
        service.ConfirmAccount();
        WaitUntil(() => host.FocusDocument(document, "native_license_email"),
            "native password completion refreshes account presentation");
        Capture("account-secured");
        Check(host.FocusDocument(document, "native_license_password"),
            "native account input region can receive focus after confirmed refresh");
        Settle(450);
        Capture("account-fields-cleared", scrollToOrigin: false);

        FillCredentials();
        Click("native_license_tab_stats", 2, CheckCredentialsCleared);
        CheckCredentialsCleared();
        Click("native_license_tab_account", 8);
        CheckCredentialsCleared();
        Check(!host.FocusDocument(document, "native_license_recover"),
            "secured account cannot overwrite its identity through native recovery");

        var rejected = new RmlUiIntent(RmlUiIntentKind.LicenseAction, 20, host.HomeDocument, ulong.MaxValue);
        Check(!page.Handle(rejected) && !page.Closed, "wrong document cannot close native License");
        var modal = pages.OpenModal(new("license-fixture-modal", "pages/social/confirm.rml", "social_confirm_cancel"));
        Check(!page.Handle(new(RmlUiIntentKind.LicenseAction, 20, document, ulong.MaxValue)) && !page.Closed,
            "foreground modal fences native License actions");
        Check(pages.CloseModal() && !host.IsAlive(modal), "native fixture modal retires");

        Check(!page.Handle(new(RmlUiIntentKind.LicenseAction, 21, host.HomeDocument, ulong.MaxValue))
            && service.SaveCalls == 0, "background document cannot invoke native privacy writer");
        pages.OpenModal(new("license-privacy-modal", "pages/social/confirm.rml", "social_confirm_cancel"));
        Check(!page.Handle(new(RmlUiIntentKind.LicenseAction, 21, document, ulong.MaxValue))
            && service.SaveCalls == 0, "foreground modal fences native privacy writes");
        Check(pages.CloseModal(), "privacy fixture modal closes");

        Click("native_license_tab_account", 8);
        Click("native_license_hide_profile", 21);
        Check(service.SaveCalls == 1 && !host.FocusDocument(document, "native_license_hide_profile"),
            "true native hide click starts one save and disables duplicates before verification");
        Capture("privacy-pending");
        service.ConfirmPrivate();
        Settle(100);
        Check(host.FocusDocument(document, "native_license_hide_profile"),
            "confirmed native privacy readback enables explicit private-preference persistence");
        Capture("privacy-hidden");
        Click("native_license_hide_profile", 21);
        Check(service.SaveCalls == 2, "already-private profile still persists an explicit opt-out");
        Click("native_license_cancel_refresh", 10);
        service.ConfirmPrivate(); // The simulated server may already have committed despite cancellation.
        Settle(100);
        Check(!host.FocusDocument(document, "native_license_hide_profile"),
            "cancelled native save does not claim confirmed privacy or accept late completion");
        Click("native_license_refresh", 9);
        Settle(100);
        Check(host.FocusDocument(document, "native_license_hide_profile"),
            "fresh sync restores authoritative privacy after an uncertain save");

        Click("native_license_show_profile", 22);
        Check(Visible("native_license_publish_confirmation") && service.PublicCalls == 0,
            "native show action presents consent without publishing");
        Check(host.FocusDocument(document, "native_license_publish_confirm"), "public consent is reachable before any write");
        Settle(450);
        Capture("public-confirmation", scrollToOrigin: false);
        Click("native_license_publish_cancel", 24);
        Check(!Visible("native_license_publish_confirmation") && service.PublicCalls == 0,
            "native keep-private click retires consent without any public write");
        Click("native_license_show_profile", 22);
        Click("native_license_publish_confirm", 23);
        WaitUntil(() => Volatile.Read(ref service.PublicCalls) == 1,
            "native public confirmation dispatches one asynchronous publish request");
        Check(!Visible("native_license_publish_confirmation")
            && !host.FocusDocument(document, "native_license_show_profile")
            && !host.FocusDocument(document, "native_license_hide_profile"),
            "native pending publication clears consent and blocks duplicate visibility writes");
        Capture("public-pending");
        service.ConfirmPublic();
        WaitUntil(() => host.FocusDocument(document, "native_license_hide_profile"),
            "server-confirmed synthetic publication restores verified public profile");
        Check(!host.FocusDocument(document, "native_license_show_profile"),
            "already-public profile cannot request another publish consent");
        Capture("public-visible");

        Click("native_license_tab_overview", 0);
        Click("native_license_close", 20);
        Check(page.Closed, "actual native Back click closes License presenter");
        page.Dispose();
        Check(!host.IsAlive(document)
            && !page.Handle(new(RmlUiIntentKind.LicenseAction, 9, document, ulong.MaxValue)),
            "retired native document cannot restart sync");
        Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors after native License acceptance");
        Console.WriteLine("RML LICENSE GL synthetic-fixture screenshots: " + output);

        bool Visible(string id) => host.TryGetElementBounds(document, id, out _, out _, out _, out _);

        void FillCredentials()
        {
            host.SetField(document, "native_license_email", "synthetic-account@example.test");
            host.SetField(document, "native_license_password", "synthetic-password-only");
            host.SetField(document, "native_license_confirmation", "synthetic-password-only");
        }

        void CheckCredentialsCleared()
        {
            foreach (string field in new[] { "native_license_email", "native_license_password", "native_license_confirmation" })
                Check(host.ReadField(document, field) == "", "native credential field cleared immediately: " + field);
        }

        void WaitUntil(Func<bool> condition, string label)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 3000)
            {
                Frame();
                if (condition()) { Check(true, label); return; }
                Thread.Sleep(5);
            }
            throw new TimeoutException(label);
        }

        void Frame()
        {
            // The preceding GL-state test deliberately leaves a nondefault viewport,
            // read FBO and scissor. Reset the final framebuffer before each capture.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            page.Refresh();
            host.Update();
            RmlUiOpenGlFrame.Draw(host, width, height);
        }

        void Settle(int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Frame();
                Thread.Sleep(10);
            } while (timer.ElapsedMilliseconds < milliseconds);
            Frame();
        }

        void Click(string id, int action, Action? immediatelyAfterDispatch = null)
        {
            Frame();
            Check(host.FocusDocument(document, id), "native focus/scroll reaches " + id);
            Check(host.FocusedElement() == id, "actual native focus belongs to " + id);
            host.Update();
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h),
                "native bounds resolve " + id);
            double px = x + w / 2, py = y + h / 2;
            Check(px >= 0 && px < width && py >= 0 && py < height,
                "native control center is inside framebuffer " + id);
            host.Input.PointerButton(0, px, py, true);
            host.Input.PointerButton(0, px, py, false);
            host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.Handle(intent);
                if (intent.Kind != RmlUiIntentKind.LicenseAction || intent.Argument != action) continue;
                Check(intent.Document == document && accepted, "true native pointer click dispatches " + id);
                Check(!page.Handle(intent), "duplicate native sequence is rejected " + id);
                immediatelyAfterDispatch?.Invoke();
                received = true;
            }
            Check(received, "native click emits expected License action " + action);
            Frame();
            // Product button colors transition over 140ms; capture their settled state.
            Settle(180);
        }

        void Capture(string name, bool scrollToOrigin = true)
        {
            Frame();
            // Focus scrolls just far enough to reach a clicked control. Capture
            // each face at its scroll origin so the title/identity are inspectable.
            if (scrollToOrigin && host.TryGetElementBounds(document, "native_license", out float x, out float y,
                out float w, out float h))
            {
                host.Input.PointerMoved(x + w / 2, y + h / 2);
                host.Input.PointerWheel(1000);
                host.Update();
                // Native smooth scrolling and button transitions must finish before readback.
                Settle(450);
            }
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC LICENSE FIXTURE - ACTUAL NATIVE GL FRAMEBUFFER\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--)
                    stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Native Hunter License acceptance with deterministic synthetic fixture data; actual final GL framebuffer before swap.");
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("RML LICENSE GL PASS " + name);
    }

    private sealed class FixtureReader : INativeLicenseOverviewReader, INativeLicensePrivacyWriter,
        INativeLicensePublicProfileWriter, INativeLicenseAccountWriter
    {
        private TaskCompletionSource<bool>? _pending;
        private TaskCompletionSource<bool>? _publicPending;
        private TaskCompletionSource<bool>? _accountPending;
        private int _public = 1;
        private int _secure;
        public int SaveCalls;
        public int PublicCalls;
        public int AccountCalls;
        public NativeLicenseAccountAction LastAccountAction;
        public string LastEmail = "";
        public string LastPassword = "";

        public Task<bool> ExecuteAccountAsync(long lease, NativeLicenseAccountAction action,
            string email, string password, CancellationToken token)
        {
            if (lease != 1) throw new InvalidOperationException("Unexpected synthetic account lease");
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            LastAccountAction = action;
            LastEmail = email;
            LastPassword = password;
            Volatile.Write(ref _accountPending, pending);
            Interlocked.Increment(ref AccountCalls);
            return pending.Task;
        }

        public void ConfirmAccount()
        {
            if (Volatile.Read(ref _accountPending)?.TrySetResult(true) != true)
                throw new InvalidOperationException("No pending synthetic account action");
        }

        public void CompleteSyntheticVerification() => Volatile.Write(ref _secure, 1);

        public Task<bool> ShowPublicProfileAsync(long lease, CancellationToken token)
        {
            if (lease != 1) throw new InvalidOperationException("Unexpected synthetic publication lease");
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _publicPending, pending);
            Interlocked.Increment(ref PublicCalls);
            return pending.Task;
        }

        public void ConfirmPublic()
        {
            Volatile.Write(ref _public, 1);
            if (Volatile.Read(ref _publicPending)?.TrySetResult(true) != true)
                throw new InvalidOperationException("No pending synthetic publication");
        }

        public Task<bool> HidePublicProfileAsync(long privacyLease, CancellationToken cancellationToken)
        {
            if (privacyLease != 1) throw new InvalidOperationException("Unexpected synthetic privacy lease");
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _pending, pending);
            Interlocked.Increment(ref SaveCalls);
            return pending.Task; // Deliberately ignores cancellation to exercise late-result fencing.
        }

        public void ConfirmPrivate()
        {
            Volatile.Write(ref _public, 0);
            if (Volatile.Read(ref _pending)?.TrySetResult(true) != true)
                throw new InvalidOperationException("No pending synthetic privacy save");
        }

        public Task<NativeLicenseOverview> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new NativeLicenseOverview(true, "SYNTHETIC FIXTURE HUNTER", "SAMUS", "",
                Volatile.Read(ref _public) == 1 ? "PUBLIC" : "PRIVATE",
                1750, "TIER 2", 250, 125, 100, 1234, 567, 321, 25)
            {
                PrivacyLease = 1,
                Account = new(Volatile.Read(ref _secure) == 1, Volatile.Read(ref _secure) == 1,
                    false, false, false),
                Career = new(25, 987654, 60 * 60 * 125, 75, 12, 2, 8, 22, 33, 44),
                History = Array.AsReadOnly(Enumerable.Range(0, 25).Select(i => new NativeLicenseMatch(
                    "SYNTHETIC ARENA " + (i + 1), i % 2 == 0 ? "BATTLE" : "CAPTURE",
                    new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero).AddMinutes(-i * 15),
                    i % 3 == 0, i % 5 == 0, i % 4 != 0, i % 6 != 0,
                    10 + i, 2 + i, i, 1000 + i * 50, (long)(60 * 60 * (5 + i)))).ToArray())
            });
        }
    }
}
