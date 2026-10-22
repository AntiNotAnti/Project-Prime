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

/// <summary>Actual native local import journey with a synthetic preparation and publication authority.</summary>
internal static class CommunityImportGlAcceptance
{
    private const string Scope = "Actual native Community import modal/input, foreground routing, review/explicit confirmation, cancellation, root retirement and framebuffer. Synthetic package/picker/commit only; no OS picker, HTTP, real installation, registration or canonical assets.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_IMPORT_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-community-import-rml87", width + "x" + height));
        Directory.CreateDirectory(output);
        Check(!GameFiles.Ready, "import fixture has no game files or real installation owner");
        var fixture = new Fixture(); var evidence = new List<object>();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeCommunityDiscoverPage(host, pages, fixture, fixture, fixture,
            clipboard: _ => throw new InvalidOperationException("Unexpected clipboard"), imports: fixture);
        host.Input.SetFramebufferScale(1, 1); page.Open(); var parent = pages.Page;
        RmlUiDocumentToken importDocument = default;
        try
        {
            Wait(() => Has("SYNTHETIC IMPORT CATALOG"), "synthetic catalog loads without network");
            Check(!Has("FULL COMMUNITY / STUDIO"), "native Community import exposes no legacy fallback control");
            OpenImport();
            Check(!host.FocusDocument(importDocument, "community_import_confirm"), "unprepared import cannot confirm");
            string path = "  \"/private/Synthetic Packages/é🙂 exact \"quoted\" arena.ppmap\"  ";
            host.SetField(importDocument, "community_import_path", path);
            Check(host.ReadField(importDocument, "community_import_path", 16384) == path,
                "native path field preserves exact Unicode whitespace and quotes");
            Activate("community_import_prepare", RmlUiIntentKind.CommunityDetailAction, 7);
            Wait(() => fixture.Preparations.Count == 1, "Prepare Package reaches synthetic authority");
            var first = fixture.Preparations.Last();
            Check(first.Path == path && !first.UsePicker && ReferenceEquals(first.Context, fixture.Context),
                "native prepare captures exact path and configured root context");
            Check(Has("VERIFYING AND PREPARING") && !host.FocusDocument(importDocument, "community_import_confirm")
                && !host.FocusDocument(importDocument, "community_import_path") && fixture.Commits == 0,
                "held preparation disables confirmation and input without optimistic registration");
            MeasureModal(); Capture("preparation-pending");
            var prepared = fixture.Package(); first.Completion.SetResult(prepared);
            Wait(() => host.FocusDocument(importDocument, "community_import_confirm"), "verified private package enables explicit confirmation");
            Check(Has(prepared.Name) && Has("VERSION // " + prepared.Version) && Has(prepared.PackageHash)
                && fixture.Commits == 0, "native review displays exact synthetic runtime name version and full hash before publication");
            MeasureModal(); Capture("review-exact-package");
            Activate("community_import_confirm", RmlUiIntentKind.CommunityConfirm, 0, pointer: false);
            Check(fixture.Commits == 1 && fixture.CommitAttempts == 1 && prepared.Disposed
                && prepared.CommitThread == Environment.CurrentManagedThreadId,
                "explicit Confirm publishes exactly once on the owner and releases preparation");
            Check(Has("PACKAGE IMPORTED") && Has("VIEW INSTALLED MAPS")
                && !host.FocusDocument(importDocument, "community_import_confirm"), "successful import shows verified outcome and cannot confirm twice");
            Capture("imported-once"); CloseImport();
            Wait(() => fixture.LastCatalog == NativeCommunityCatalog.Installed && Has("SYNTHETIC INSTALLED IMPORT"),
                "View Installed Maps returns to native Installed catalog without legacy fallback");

            OpenImport();
            Activate("community_import_pick", RmlUiIntentKind.CommunityImport, 0);
            Wait(() => fixture.Preparations.Count == 2, "Choose File uses injected held picker authority");
            var picked = fixture.Preparations.Last();
            Check(picked.UsePicker && picked.Path == "" && !host.FocusDocument(importDocument, "community_import_confirm"),
                "native picker preparation remains unconfirmable while held");
            picked.Completion.SetResult(null);
            Wait(() => Has("FILE SELECTION CANCELED"), "canceled synthetic picker returns to editable native form");
            Check(host.FocusDocument(importDocument, "community_import_path") && fixture.Commits == 1,
                "picker cancellation never commits a package"); Capture("picker-canceled");
            PreparePath("/synthetic/cancel-preparation.ppmap");
            var canceledWork = fixture.Preparations.Last();
            Activate("community_import_cancel_work", RmlUiIntentKind.CommunityCancelWork, 0);
            Check(canceledWork.Token.IsCancellationRequested && !host.FocusDocument(importDocument, "community_import_confirm"),
                "actual Cancel Preparation retires held work without closing the native modal");
            var canceledPackage = fixture.Package("CANCELED PREPARATION"); canceledWork.Completion.SetResult(canceledPackage);
            Wait(() => canceledPackage.Disposed, "canceled preparation releases late private snapshot");
            Check(!Has("CANCELED PREPARATION") && host.FocusDocument(importDocument, "community_import_path"),
                "canceled work cannot populate review and permits another explicit path"); CloseImport();

            OpenImport(); PreparePath("/synthetic/late.ppmap");
            var late = fixture.Preparations.Last(); var retiredDocument = importDocument;
            CloseImport();
            Check(late.Token.IsCancellationRequested, "closing modal cancels pending preparation");
            OpenImport(); PreparePath("/synthetic/replacement.ppmap");
            var replacementCall = fixture.Preparations.Last(); var replacement = fixture.Package("REPLACEMENT PACKAGE");
            replacementCall.Completion.SetResult(replacement);
            Wait(() => Has("REPLACEMENT PACKAGE"), "replacement native modal owns its preparation");
            var retired = fixture.Package("RETIRED PACKAGE"); late.Completion.SetResult(retired);
            Wait(() => retired.Disposed, "late canceled preparation is disposed instead of published");
            Check(importDocument != retiredDocument && !host.IsAlive(retiredDocument) && !Has("RETIRED PACKAGE") && fixture.Commits == 1,
                "late preparation cannot replace current review or register content");
            fixture.Context = new object();
            Wait(() => Has("GAME FILES CHANGED"), "configured root retirement invalidates prepared native review");
            Check(replacement.Disposed && !host.FocusDocument(importDocument, "community_import_confirm") && fixture.Commits == 1,
                "retired root releases prepared snapshot and blocks publication");
            Capture("root-context-retired");
            PreparePath("/synthetic/retry.ppmap"); var retryCall = fixture.Preparations.Last();
            var failed = fixture.Package("FAILED SYNTHETIC PACKAGE"); failed.FailCommit = true; retryCall.Completion.SetResult(failed);
            Wait(() => host.FocusDocument(importDocument, "community_import_confirm"), "fresh root can prepare again");
            Activate("community_import_confirm", RmlUiIntentKind.CommunityConfirm, 0, handled: false);
            Check(Has("IMPORT COULD NOT BE COMMITTED") && !Has("secret-token") && !Has("/private/failure")
                && failed.Disposed && fixture.Commits == 1 && fixture.CommitAttempts == 2,
                "commit failure is sanitized and releases preparation without optimistic success");
            Capture("commit-failed-retry");
            PreparePath("/synthetic/final-retry.ppmap"); var finalCall = fixture.Preparations.Last();
            var final = fixture.Package("FINAL RETRY PACKAGE"); finalCall.Completion.SetResult(final);
            Wait(() => host.FocusDocument(importDocument, "community_import_confirm"), "failed commit requires a fresh reviewed preparation");
            Activate("community_import_confirm", RmlUiIntentKind.CommunityConfirm, 0);
            Check(fixture.Commits == 2 && fixture.CommitAttempts == 3 && final.Disposed,
                "fresh explicit confirmation successfully retries failed synthetic import");
            MeasureModal(); Capture("retry-imported"); CloseImport();
            Check(fixture.Unexpected == 0 && GL.GetError() == ErrorCode.NoError,
                "native import journey has no real install account picker network registration or GL errors");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }
        finally { foreach (var pending in fixture.Preparations) pending.Completion.TrySetCanceled(); }

        void PreparePath(string path)
        {
            int before = fixture.Preparations.Count;
            host.SetField(importDocument, "community_import_path", path);
            Activate("community_import_prepare", RmlUiIntentKind.CommunityDetailAction, 7);
            Wait(() => fixture.Preparations.Count == before + 1, "native preparation begins for explicit path");
        }
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
        void OpenImport()
        {
            Activate("native_community_import", RmlUiIntentKind.CommunityImport, 0);
            importDocument = pages.Top;
            Check(importDocument != parent && host.CurrentInputDocument == importDocument && Has("IMPORT MAP PACKAGE"),
                "actual Import Package opens native foreground modal");
            Check(!page.Handle(new(RmlUiIntentKind.CommunityImport, 0, parent, ulong.MaxValue)),
                "background import action cannot bypass modal foreground");
        }
        void CloseImport()
        {
            Activate("community_import_close", RmlUiIntentKind.CommunityCancel, 0, pointer: false);
            Check(pages.Top == parent && host.CurrentInputDocument == parent,
                "native import close restores parent document and input ownership");
            Check(host.FocusedElement() == "native_community_import", "native import close restores Import Package focus");
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
                    if (pages.Top == parent || id is "community_import_confirm" or "community_import_close" or "community_import_cancel_work") return (x, y, w, h);
                    var scroll = Bounds("community_report_scroll");
                    if (y >= scroll.Y && y + h <= scroll.Y + scroll.H) return (x, y, w, h);
                }
                var viewport = Bounds(pages.Top == parent ? "native_community" : "community_report_scroll");
                host.Input.PointerMoved(viewport.X + viewport.W / 2, viewport.Y + viewport.H / 2);
                host.Input.PointerWheel(i == 0 ? 10000 : -1); Settle(20);
            }
            throw new InvalidOperationException("Native import target cannot be scrolled into viewport: " + id);
        }
        void Activate(string id, RmlUiIntentKind kind, int argument, bool pointer = true, bool handled = true)
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
                    "actual native input identifies current import action: " + id);
                Check(RmlUiPageCommandRouting.Community(intent.Kind) && page.Handle(intent) == handled, "existing shared Community routing admits native action: " + id);
                Check(!page.Handle(intent), "duplicate native sequence is rejected: " + id); count++;
            }
            Check(count == 1, "one native activation emits one import action: " + id); Settle();
        }
        void MeasureModal()
        {
            var modal = Bounds("community_report"); var scroll = Bounds("community_report_scroll"); var footer = Bounds("community_report_footer");
            var submit = Bounds("community_import_confirm"); var close = Bounds("community_import_close");
            var path = Bounds("community_import_path"); var pick = Bounds("community_import_pick");
            var prepare = Bounds("community_import_prepare"); var detail = Bounds("community_import_detail");
            Check(path.Y + path.H <= Math.Min(pick.Y, prepare.Y) + 1
                && Math.Max(pick.Y + pick.H, prepare.Y + prepare.H) <= detail.Y + 1,
                "path input, package actions and prepared detail occupy separate vertical space");
            Check(modal.X >= 0 && modal.Y >= 0 && modal.X + modal.W <= width + 1 && modal.Y + modal.H <= height + 1,
                "import modal fits actual framebuffer");
            Check(scroll.Y + scroll.H <= footer.Y + 1 && footer.Y + footer.H <= modal.Y + modal.H
                && submit.Y >= footer.Y && submit.Y + submit.H <= height && close.Y >= footer.Y && close.Y + close.H <= height,
                "scroll region remains separate from visible Confirm and Back footer");
            object Pack((float X, float Y, float W, float H) box) => new { box.X, box.Y, box.W, box.H };
            evidence.Add(new { modal = Pack(modal), scroll = Pack(scroll), footer = Pack(footer), submit = Pack(submit), close = Pack(close) });
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "synthetic-import-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "actual native import capture: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            string semantic = path + ".semantic.json"; File.WriteAllText(semantic, NativeSemanticReadback.Read(host, pages.Top).RawJson);
            evidence.Add(new { name, path, semantic });
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "import-" + width + "x" + height + ".json"),
            JsonSerializer.Serialize(new { passed, scope = Scope, width, height, prepares = fixture.Preparations.Count,
                fixture.CommitAttempts, fixture.Commits, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("COMMUNITY IMPORT GL PASS " + label); }
    private sealed record Preparation(string Path, bool UsePicker, object Context, CancellationToken Token,
        TaskCompletionSource<INativeCommunityPreparedImport?> Completion);
    private sealed class Prepared(Fixture owner, object context, string name) : INativeCommunityPreparedImport
    {
        public string Name => name;
        public string Version => "synthetic-v7";
        public string PackageHash => new('d', 64);
        public bool Disposed, FailCommit;
        public int CommitThread;
        public bool IsCurrent => !Disposed && ReferenceEquals(context, owner.Context);
        public void Commit()
        {
            CommitThread = Environment.CurrentManagedThreadId;
            Check(CommitThread == owner.OwnerThread && IsCurrent, "prepared fake commits only on original owner with current root");
            owner.CommitAttempts++;
            if (FailCommit) throw new InvalidOperationException("secret-token /private/failure");
            owner.Commits++;
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class Fixture : INativeCommunityLibraryReader, INativeCommunityInstaller,
        INativeCommunityCreatorAuthority, INativeCommunityImportAuthority
    {
        internal readonly int OwnerThread = Environment.CurrentManagedThreadId;
        internal object Context = new();
        internal readonly object Witness = new();
        internal int Commits, CommitAttempts, Unexpected;
        internal NativeCommunityCatalog LastCatalog;
        internal readonly ConcurrentQueue<Preparation> Preparations = new();
        public bool PickerAvailable => true;
        public object CaptureContext() => Context;
        public bool IsCurrentContext(object context) => ReferenceEquals(context, Context);
        internal Prepared Package(string name = "SYNTHETIC VERIFIED PACKAGE") => new(this, Context, name);
        public Task<INativeCommunityPreparedImport?> PrepareAsync(string path, bool usePicker, object context, CancellationToken token)
        {
            var completion = new TaskCompletionSource<INativeCommunityPreparedImport?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Preparations.Enqueue(new(path, usePicker, context, token, completion));
            return completion.Task; // Deliberately ignores cancellation to exercise retired snapshot disposal.
        }
        public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken token)
            => BrowseLibraryAsync(NativeCommunityCatalog.Discover, query, sort, page, token);
        public Task<NativeCommunityPage> BrowseLibraryAsync(NativeCommunityCatalog catalog, string query, string sort, int page, CancellationToken token)
        {
            LastCatalog = catalog;
            var row = new NativeCommunityEntry(Guid.Parse("33333333-3333-4333-8333-333333333333"),
                catalog == NativeCommunityCatalog.Installed ? "SYNTHETIC INSTALLED IMPORT" : "SYNTHETIC IMPORT CATALOG",
                "SYNTHETIC CREATOR", new string('a',64), 7, "Battle", 2, 8) { Version = "7", Installed = catalog == NativeCommunityCatalog.Installed };
            return Task.FromResult(new NativeCommunityPage(new[] { row }, 1, 1, 8) { Witness = Witness });
        }
        public bool IsCurrentWitness(object? witness) => ReferenceEquals(witness, Witness);
        public Task<object> SetFavoriteAsync(NativeCommunityEntry selected, bool favorite, object? witness, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public void Register(NativeCommunityInstalled installed) { Unexpected++; throw new InvalidOperationException(); }
        public Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid id, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
        public Task<bool> ExecuteAsync(NativeCreatorAction action, Guid id, CancellationToken token) { Unexpected++; throw new InvalidOperationException(); }
    }
}
