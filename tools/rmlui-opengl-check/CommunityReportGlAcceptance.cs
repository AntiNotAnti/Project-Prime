using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native report modal/input with explicit fake reporting authority; never sends a real report.</summary>
internal static class CommunityReportGlAcceptance
{
    private const string Scope = "Actual native Community Report modal, foreground routing, focus, pointer/Enter input, scroll/footer and framebuffer; explicit synthetic catalog/account/report completions only. No HTTP, account, moderation report, install, queue or OS clipboard mutation; no game assets.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_REPORT_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-community-report-rml86", width + "x" + height));
        Directory.CreateDirectory(output);
        Check(!GameFiles.Ready, "report fixture has no game assets or installation owner");
        var fixture = new Fixture(); var evidence = new List<object>();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeCommunityDiscoverPage(host, pages, fixture, fixture, fixture,
            clipboard: _ => throw new InvalidOperationException("No clipboard action in report fixture"), reports: fixture);
        host.Input.SetFramebufferScale(1, 1); page.Open(); var parent = pages.Page;
        RmlUiDocumentToken report = default;
        try
        {
            Wait(() => Has("SYNTHETIC REPORT ARENA"), "synthetic public exact revision appears");
            fixture.HoldPrepare = true;
            OpenReport();
            Wait(() => fixture.Prepares.Count == 1, "native Report Map requests account verification");
            var firstPrepare = fixture.Prepares.Single();
            Check(ReferenceEquals(firstPrepare.Witness, fixture.CatalogWitness) && firstPrepare.Selection == Fixture.Selection,
                "report preparation captures exact immutable entry and opaque catalog witness");
            Check(!host.FocusDocument(report, "community_report_submit") && !host.FocusDocument(report, "community_report_details")
                && Has("VERIFYING REPORTING ACCOUNT"), "unverified account cannot enter details or submit");
            MeasureModal(); Capture("account-verification-pending");
            fixture.HoldPrepare = false; firstPrepare.Completion.TrySetResult(fixture.Lease);
            Wait(() => host.FocusDocument(report, "community_report_details"), "verified synthetic account enables report form");
            var target = NativeSemanticReadback.Read(host, report).Label("community_report_target");
            Check(target.Contains(Fixture.Selection.Name) && target.Contains("VERSION " + Fixture.Selection.Version)
                && target.Contains(Fixture.Selection.LatestHash), "native report target fixes title, version and full package hash");
            for (int i = 0; i < MapCreatorCatalog.ReportReasons.Length; i++)
            {
                Check(Has(MapCreatorCatalog.ReportReasons[i]), "actual modal presents reason: " + MapCreatorCatalog.ReportReasons[i]);
                Activate("community_report_reason" + i, RmlUiIntentKind.CommunityReportReason, i, pointer: i % 2 == 0);
            }
            string details = "EXPLICIT SYNTHETIC REPORT\nExact Unicode details: "
                + string.Concat(Enumerable.Repeat("é🙂", 1000)) + "\nPunctuation / < > & preserved.";
            Check(details.Length <= 4000 && Encoding.UTF8.GetByteCount(details) > 4001,
                "native report fixture exercises valid character count beyond four-thousand UTF-8 bytes");
            host.SetField(report, "community_report_details", details);
            Check(host.ReadField(report, "community_report_details", 16000) == details, "actual native details preserve exact fixture text");
            fixture.HoldSubmit = true;
            Activate("community_report_submit", RmlUiIntentKind.CommunityConfirm, 0);
            Wait(() => fixture.Submissions.Count == 1, "one explicit native Submit reaches fake authority");
            var submitted = fixture.Submissions.Single();
            Check(submitted.Selection == Fixture.Selection && submitted.Reason == "Other" && submitted.Details == details
                && ReferenceEquals(submitted.Lease, fixture.Lease), "actual Submit captures Other, exact details, map/hash/version and account lease");
            Check(Has("SUBMITTING REPORT") && !Has("REPORT SUBMITTED FOR MODERATOR REVIEW")
                && !host.FocusDocument(report, "community_report_submit") && !host.FocusDocument(report, "community_report_details"),
                "pending submission disables repeat mutation without optimistic success");
            MeasureModal(); Capture("submission-pending");
            submitted.Completion.TrySetResult(true);
            Wait(() => Has("REPORT SUBMITTED FOR MODERATOR REVIEW"), "fake service completion verifies report outcome");
            Check(!host.FocusDocument(report, "community_report_submit") && !host.FocusDocument(report, "community_report_reason5"),
                "completed report cannot be resubmitted or edited");
            Capture("submitted-once"); CloseReport();
            Check(fixture.Submissions.Count == 1, "duplicate native sequences produce only one fake report");

            fixture.HoldSubmit = false; OpenReport();
            Wait(() => host.FocusDocument(report, "community_report_details"), "second report verifies current account");
            host.SetField(report, "community_report_details", "PRIVATE SYNTHETIC DETAILS");
            fixture.Lease = new object(); Frame();
            Wait(() => Has("ACCOUNT CHANGED"), "changed opaque account lease retires active report");
            Check(host.ReadField(report, "community_report_details", 16000) == ""
                && !host.FocusDocument(report, "community_report_submit") && !host.FocusDocument(report, "community_report_details"),
                "account retirement clears private details and disables submission");
            Capture("account-retired"); CloseReport();

            fixture.HoldPrepare = true; OpenReport();
            Wait(() => fixture.Prepares.Count == 3, "third report holds account preparation");
            var latePrepare = fixture.Prepares.Last(); var retiredPrepareDocument = report;
            CloseReport();
            Check(latePrepare.Token.IsCancellationRequested, "Cancel retires held account preparation");
            latePrepare.Completion.TrySetResult(fixture.Lease);
            Wait(() => fixture.PrepareReturned >= 3, "fake preparation returns after modal cancellation");
            Frame(); Check(pages.Top == parent && !host.IsAlive(retiredPrepareDocument),
                "late account verification cannot reopen retired report modal");

            fixture.HoldPrepare = false; fixture.HoldSubmit = true; OpenReport();
            Wait(() => host.FocusDocument(report, "community_report_details"), "new report owns replacement preparation");
            host.SetField(report, "community_report_details", "CANCELLED SYNTHETIC SUBMISSION");
            Activate("community_report_submit", RmlUiIntentKind.CommunityConfirm, 0, pointer: false);
            Wait(() => fixture.Submissions.Count == 2, "second fake submission is held");
            var lateSubmit = fixture.Submissions.Last(); var retiredSubmitDocument = report;
            CloseReport();
            Check(lateSubmit.Token.IsCancellationRequested, "Cancel retires held submission observation");
            fixture.HoldSubmit = false; OpenReport();
            Wait(() => host.FocusDocument(report, "community_report_details"), "replacement report remains independently editable");
            lateSubmit.Completion.TrySetResult(true);
            Wait(() => fixture.SubmitReturned >= 2, "fake submission returns after old modal closes");
            Frame();
            Check(report != retiredSubmitDocument && !host.IsAlive(retiredSubmitDocument)
                && !Has("REPORT SUBMITTED FOR MODERATOR REVIEW") && host.FocusDocument(report, "community_report_details"),
                "late old submission cannot mark replacement modal successful");
            MeasureModal(); Capture("replacement-after-retired-submit"); CloseReport();
            Check(fixture.Submissions.Count == 2 && fixture.Unexpected == 0 && GL.GetError() == ErrorCode.NoError,
                "native report journey leaves only two explicit fake submits and no unrelated mutations or GL error");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }
        finally { foreach (var held in fixture.Prepares) held.Completion.TrySetCanceled(); foreach (var held in fixture.Submissions) held.Completion.TrySetCanceled(); }

        bool Has(string text) => NativeSemanticReadback.Read(host, pages.Top).ContainsText(text);
        void Frame()
        {
            page.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int ms = 150) { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < ms); }
        void Wait(Func<bool> predicate, string label)
        { var timer = Stopwatch.StartNew(); do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); } while (timer.ElapsedMilliseconds < 5000); throw new TimeoutException(label); }
        void OpenReport()
        {
            Activate("native_community_report", RmlUiIntentKind.CommunityReport, 0);
            report = pages.Top;
            Check(report != parent && host.CurrentInputDocument == report && Has("REPORT MAP"), "actual native Report Map opens shared foreground modal");
            Check(!page.Handle(new(RmlUiIntentKind.CommunityReport, 0, parent, ulong.MaxValue)), "stale background map action cannot bypass report foreground");
        }
        void CloseReport()
        {
            Activate("community_report_close", RmlUiIntentKind.CommunityCancel, 0, pointer: false);
            Check(pages.Top == parent && host.CurrentInputDocument == parent && host.FocusedElement() == "native_community_report",
                "native Cancel or Back restores map document and Report Map focus");
        }
        (float X, float Y, float W, float H) Bounds(string id)
        {
            Check(host.TryGetElementBounds(pages.Top, id, out float x, out float y, out float w, out float h) && w > 0 && h > 0,
                "actual native bounds exist: " + id); return (x, y, w, h);
        }
        (float X, float Y, float W, float H) Reach(string id)
        {
            Frame(); Check(host.FocusDocument(pages.Top, id), "actual native focus reaches " + id); Settle();
            for (int i = 0; i < 120; i++)
            {
                if (host.TryGetElementBounds(pages.Top, id, out float x, out float y, out float w, out float h)
                    && x >= 0 && y >= 0 && x + w <= width + 1 && y + h <= height + 1)
                {
                    if (pages.Top == parent || id is "community_report_submit" or "community_report_close") return (x, y, w, h);
                    var scroll = Bounds("community_report_scroll");
                    if (y >= scroll.Y && y + h <= scroll.Y + scroll.H) return (x, y, w, h);
                }
                var viewport = Bounds(pages.Top == parent ? "native_community" : "community_report_scroll");
                host.Input.PointerMoved(viewport.X + viewport.W / 2, viewport.Y + viewport.H / 2);
                host.Input.PointerWheel(i == 0 ? 10000 : -1); Settle(20);
            }
            throw new InvalidOperationException("Native report target cannot be scrolled into viewport: " + id);
        }
        void Activate(string id, RmlUiIntentKind kind, int argument, bool pointer = true)
        {
            var box = Reach(id); var document = pages.Top; host.DiscardIntents();
            if (pointer)
            {
                double x = box.X + box.W / 2, y = box.Y + box.H / 2;
                host.Input.PointerMoved(x, y); Frame(); host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false);
            }
            else { host.Input.Key(2, true); host.Input.Key(2, false); }
            host.Update(); int count = 0;
            while (host.TryTakeIntent(out var intent))
            {
                Check(intent.Kind == kind && intent.Argument == argument && intent.Document == document,
                    "actual native input identifies current report action: " + id);
                Check(RmlUiPageCommandRouting.Community(intent.Kind) && page.Handle(intent), "existing shared Community routing admits native action: " + id);
                Check(!page.Handle(intent), "duplicate native sequence is rejected: " + id); count++;
            }
            Check(count == 1, "one native activation emits one report action: " + id); Settle();
        }
        void MeasureModal()
        {
            var modal = Bounds("community_report"); var scroll = Bounds("community_report_scroll"); var footer = Bounds("community_report_footer");
            var submit = Bounds("community_report_submit"); var close = Bounds("community_report_close");
            Check(modal.X >= 0 && modal.Y >= 0 && modal.X + modal.W <= width + 1 && modal.Y + modal.H <= height + 1,
                "report modal fits actual framebuffer");
            Check(scroll.Y + scroll.H <= footer.Y + 1 && footer.Y + footer.H <= modal.Y + modal.H
                && submit.Y >= footer.Y && submit.Y + submit.H <= height && close.Y >= footer.Y && close.Y + close.H <= height,
                "scroll region remains separate from visible Submit and Cancel footer");
            object Pack((float X, float Y, float W, float H) box) => new { box.X, box.Y, box.W, box.H };
            evidence.Add(new { modal = Pack(modal), scroll = Pack(scroll), footer = Pack(footer), submit = Pack(submit), close = Pack(close) });
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "synthetic-report-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "actual native report capture: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            string semantic = path + ".semantic.json"; File.WriteAllText(semantic, NativeSemanticReadback.Read(host, pages.Top).RawJson);
            evidence.Add(new { name, path, semantic });
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "report-" + width + "x" + height + ".json"),
            JsonSerializer.Serialize(new { passed, scope = Scope, width, height, prepareCalls = fixture.Prepares.Count,
                submitted = fixture.Submissions.Select(s => new { s.Selection.MapId, s.Selection.LatestHash, s.Selection.Version, s.Reason, s.Details }), evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("COMMUNITY REPORT GL PASS " + label); }
    private sealed record Preparation(NativeCommunityEntry Selection, object? Witness, CancellationToken Token, TaskCompletionSource<object> Completion);
    private sealed record Submission(NativeCommunityEntry Selection, string Reason, string Details, object Lease, CancellationToken Token, TaskCompletionSource<bool> Completion);
    private sealed class Fixture : INativeCommunityLibraryReader, INativeCommunityInstaller, INativeCommunityCreatorAuthority, INativeCommunityReportAuthority
    {
        internal static readonly NativeCommunityEntry Selection = new(Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "SYNTHETIC REPORT ARENA", "SYNTHETIC CREATOR", new string('b', 64), 7, "Battle", 2, 8) { Version = "report-fixture-v7" };
        internal readonly object CatalogWitness = new(); internal object Lease = new();
        internal bool HoldPrepare, HoldSubmit;
        internal int PrepareReturned, SubmitReturned, Unexpected;
        internal readonly ConcurrentQueue<Preparation> Prepares = new(); internal readonly ConcurrentQueue<Submission> Submissions = new();
        public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken token) => BrowseLibraryAsync(NativeCommunityCatalog.Discover, query, sort, page, token);
        public Task<NativeCommunityPage> BrowseLibraryAsync(NativeCommunityCatalog catalog, string query, string sort, int page, CancellationToken token)
            => Task.FromResult(new NativeCommunityPage(new[] { Selection }, 1, 1, 8) { Witness = CatalogWitness });
        public bool IsCurrentWitness(object? witness) => ReferenceEquals(witness, CatalogWitness);
        public Task<object> SetFavoriteAsync(NativeCommunityEntry selected, bool favorite, object? witness, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public async Task<object> PrepareAsync(NativeCommunityEntry selected, object? witness, CancellationToken token)
        {
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            Prepares.Enqueue(new(selected, witness, token, completion)); if (!HoldPrepare) completion.TrySetResult(Lease);
            object result = await completion.Task.ConfigureAwait(false); // Deliberately ignores cancellation for retirement acceptance.
            Interlocked.Increment(ref PrepareReturned); return result;
        }
        public bool IsCurrentLease(object lease) => ReferenceEquals(lease, Lease);
        public async Task SubmitAsync(NativeCommunityEntry selected, string reason, string details, object lease, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Submissions.Enqueue(new(selected, reason, details, lease, token, completion)); if (!HoldSubmit) completion.TrySetResult(true);
            await completion.Task.ConfigureAwait(false); Interlocked.Increment(ref SubmitReturned);
        }
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public void Register(NativeCommunityInstalled installed) { Unexpected++; throw new InvalidOperationException(); }
        public Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid id, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public Task<bool> ExecuteAsync(NativeCreatorAction action, Guid id, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
    }
}
