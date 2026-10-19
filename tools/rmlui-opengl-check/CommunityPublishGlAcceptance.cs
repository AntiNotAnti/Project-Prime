using MphRead.Mods.Render;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;

internal static class CommunityPublishGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-community-publish-rml54"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"), "Actual native RmlUi publication dialog, SYNTHETIC frozen source/account/queue only. No package build, user/editor files, credentials, HTTP or backend mutation. The fake completion does not certify live publishing.\n");
        using var pages = new RmlUiPageManager(host);
        var parent = pages.OpenPage(new("synthetic-publish-studio", "pages/studio/native-entry.rml", "native_studio_publish"));
        pages.Present(parent, 1, new Dictionary<string, RmlUiBindingValue>
            { ["native_studio_status"] = RmlUiBindingValue.FromText("SYNTHETIC RETAINED STUDIO PARENT // NO EDITOR FILES") });
        var authority = new Authority();
        using var page = new NativeCommunityPublishPage(host, pages, authority);
        host.Input.SetFramebufferScale(1, 1);
        Check(page.OpenSynthetic(new(Map, "SYNTHETIC FROZEN STUDIO PROJECT"), "native_studio_publish"), "actual native publication modal opens above retained Studio parent");
        var modal = page.Document; Ready();
        Check(NativeSemanticReadback.Read(host, modal).ContainsText("EXPECTED PARENT SHA-256 // " + Parent),
            "native publication displays captured exact parent without account identity");
        Check(!page.Handle(new(RmlUiIntentKind.CommunityConfirm, 0, parent, ulong.MaxValue)), "parent cannot confirm foreground publication");
        Capture("initial-private-draft", true);
        Click("community_publish_published", RmlUiIntentKind.CommunityVisibility, 0);
        host.SetField(modal, "community_publish_notes", "SYNTHETIC NOTES 日本 😀"); Frame();
        Click("community_publish_prepare", RmlUiIntentKind.CommunityUpload, 0);
        Check(authority.Queues == 0 && !host.FocusDocument(modal, "community_publish_notes")
            && NativeSemanticReadback.Read(host, modal).ContainsText("QUEUE PUBLISHED"), "actual native proposal locks captured notes and waits for confirmation");
        Capture("review-exact-parent", true);
        Click("community_publish_close", RmlUiIntentKind.CommunityCancel, 0);
        Check(page.IsOpen && authority.Queues == 0, "Back dismisses proposal without queue admission");
        Click("community_publish_prepare", RmlUiIntentKind.CommunityUpload, 0);
        var foreground = pages.OpenModal(new("synthetic-publish-fence", "pages/social/confirm.rml", "social_confirm_cancel")); Frame();
        Check(!page.Handle(new(RmlUiIntentKind.CommunityConfirm, 0, modal, ulong.MaxValue)), "unrelated foreground document fences publish confirmation");
        pages.CloseModal(); Check(!host.IsAlive(foreground), "synthetic foreground fence retires");
        Click("community_publish_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => authority.Queues == 1 && NativeSemanticReadback.Read(host, modal).ContainsText("EXACT PACKAGE SHA-256 // " + Package), "actual native queue receives one immutable synthetic package receipt");
        Check(authority.Notes == "SYNTHETIC NOTES 日本 😀" && authority.Visibility == NativeCommunityPublishVisibility.Published,
            "native textarea UTF-8 roundtrip and visibility reach captured queue request exactly");
        Check(!host.FocusDocument(modal, "community_publish_prepare"), "native admitted package cannot be queued twice");
        Wait(() => host.FocusDocument(modal, "community_publish_pause"), "durable upload offers native Pause after progress readback");
        Capture("queued-not-published", true);
        Click("community_publish_pause", RmlUiIntentKind.CommunityDetailAction, 0);
        Wait(() => host.FocusDocument(modal, "community_publish_resume"), "paused receipt offers separately verified resume");
        Click("community_publish_resume", RmlUiIntentKind.CommunityDetailAction, 1);
        Check(authority.Controls == 2 && authority.Queues == 1, "native pause/resume operate original job without another queue request");
        authority.Complete = true;
        Wait(() => NativeSemanticReadback.Read(host, modal).ContainsText("PUBLICATION VERIFIED"), "only synthetic verified completion produces native publication success");
        Capture("synthetic-verified-publication", true);
        authority.CurrentLease = new Secret(); Frame();
        Check(host.ReadField(modal, "community_publish_notes", 16004) == ""
            && NativeSemanticReadback.Read(host, modal).ContainsText("ACCOUNT CHANGED")
            && !NativeSemanticReadback.Read(host, modal).ContainsText(Package)
            && !NativeSemanticReadback.Read(host, modal).ContainsText(Parent)
            && !host.FocusDocument(modal, "community_publish_confirm"), "account retirement clears actual notes, private parent, receipt and mutation controls");
        Capture("account-retirement", true);
        Click("community_publish_close", RmlUiIntentKind.CommunityCancel, 0);
        Check(!page.IsOpen && !host.IsAlive(modal) && pages.Top == parent && host.FocusedElement() == "native_studio_publish",
            "actual native Back retires modal and restores Studio publish focus");
        Check(!page.Handle(new(RmlUiIntentKind.CommunityConfirm, 0, modal, ulong.MaxValue)), "retired publication document cannot replay confirmation");

        authority.Reset(); authority.Reject = true;
        Check(page.OpenSynthetic(new(Map, "SYNTHETIC FROZEN STUDIO PROJECT"), "native_studio_publish"), "fresh synthetic publication scenario reopens safely");
        modal = page.Document; Ready();
        host.SetField(modal, "community_publish_notes", "FAILED SYNTHETIC NOTES");
        Click("community_publish_prepare", RmlUiIntentKind.CommunityUpload, 0);
        Click("community_publish_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => NativeSemanticReadback.Read(host, modal).ContainsText("OUTCOME UNVERIFIED"), "failed write cannot claim no change or publication success");
        Check(!host.FocusDocument(modal, "community_publish_prepare") && host.ReadField(modal, "community_publish_notes") == ""
            && !NativeSemanticReadback.Read(host, modal).ContainsText("SYNTHETIC SECRET"), "failed publication removes unchecked draft and redacts exception/notes");
        Capture("failed-unverified-outcome", true);
        authority.Reject = false;
        Click("community_publish_refresh", RmlUiIntentKind.CommunityRefresh, 0); Ready();
        Check(authority.Queues == 2 && !host.FocusDocument(modal, "community_publish_confirm"), "explicit refresh requires another proposal rather than automatic retry");
        Click("community_publish_prepare", RmlUiIntentKind.CommunityUpload, 0);
        authority.CurrentParent = new string('C', 64);
        Click("community_publish_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => NativeSemanticReadback.Read(host, modal).ContainsText("OUTCOME UNVERIFIED"), "changed expected parent rejects pending native proposal");
        Check(authority.Accepted == 1, "parent change cannot silently reparent or admit another package");
        Click("community_publish_refresh", RmlUiIntentKind.CommunityRefresh, 0); Ready();
        Check(NativeSemanticReadback.Read(host, modal).ContainsText(authority.CurrentParent), "explicit refresh displays newly captured parent before a new proposal");
        Click("community_publish_prepare", RmlUiIntentKind.CommunityUpload, 0);
        authority.CurrentMap = Guid.NewGuid();
        Click("community_publish_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => NativeSemanticReadback.Read(host, modal).ContainsText("OUTCOME UNVERIFIED"), "changed project authority rejects pending frozen-source native proposal");
        Check(authority.Accepted == 1 && !host.FocusDocument(modal, "community_publish_confirm"), "source authority change cannot reuse previous confirmation");
        authority.CurrentMap = Map;
        Click("community_publish_refresh", RmlUiIntentKind.CommunityRefresh, 0); Ready();
        AssertFooter();
        Check(page.Back(), "synthetic publication finally closes");
        page.Dispose(); pages.ClosePage();
        Check(!host.IsAlive(parent) && GL.GetError() == ErrorCode.NoError, "publication fixture retires all documents without GL errors");
        Console.WriteLine("RML COMMUNITY PUBLISH GL synthetic screenshots: " + output);

        void Frame()
        {
            page.Refresh(); host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.025f, .05f, .08f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds); Frame(); }
        void Wait(Func<bool> predicate, string label)
        {
            var timer = Stopwatch.StartNew();
            do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); } while (timer.Elapsed < TimeSpan.FromSeconds(5));
            throw new TimeoutException(label);
        }
        void Ready() => Wait(() => host.FocusDocument(modal, "community_publish_prepare"), "native synthetic account/project preflight settles");
        void Click(string id, RmlUiIntentKind kind, int argument)
        {
            Frame(); Check(host.FocusDocument(modal, id), "native focus reaches " + id); Settle(450);
            Check(host.TryGetElementBounds(modal, id, out float x, out float y, out float w, out float h), "native bounds resolve " + id);
            double px = x + w / 2d, py = y + h / 2d;
            Check(px >= 0 && px < width && py >= 0 && py < height, "native publish button is inside framebuffer " + id);
            host.Input.PointerMoved(px, py); Frame();
            Check(host.HoveredElement() == id, "native hover matches publication target " + id);
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.Handle(intent);
                if (intent.Kind != kind || intent.Argument != argument) continue;
                Check(accepted && intent.Document == modal, "actual native pointer dispatches " + id);
                Check(!page.Handle(intent), "duplicate native publication sequence rejected " + id); received = true;
            }
            Check(received, "native publication emits expected action " + id); Settle(180);
        }
        void AssertFooter()
        {
            Check(host.TryGetElementBounds(modal, "community_publish_scroll", out float sx, out float sy, out float sw, out float sh)
                && sh > 30 && sx >= 0 && sx + sw <= width, "publication uses independent bounded detail scroll");
            host.Input.PointerMoved(sx + sw / 2d, sy + sh / 2d); host.Input.PointerWheel(-1000); Settle(450);
            Check(host.TryGetElementBounds(modal, "community_publish_close", out float x, out float y, out float w, out float h)
                && x >= 0 && y >= sy + sh - 1 && x + w <= width && y + h <= height,
                "publication Back footer remains visible after scroll at every tested size");
        }
        void Capture(string label, bool origin)
        {
            if (origin && host.TryGetElementBounds(modal, "community_publish_scroll", out float x, out float y, out float w, out float h))
            { host.Input.PointerMoved(x + w / 2d, y + h / 2d); host.Input.PointerWheel(1000); }
            Settle(450);
            string path = Path.Combine(output, $"synthetic-{label}-{width}x{height}.ppm");
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC COMMUNITY PUBLISH - ACTUAL NATIVE GL - NO BACKEND WRITES\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height, "Real native publication page with synthetic frozen source/account/queue; no builds, user files, auth, HTTP or backend writes.");
        }
    }
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); Console.WriteLine("RML COMMUNITY PUBLISH GL PASS " + label); }
    private static readonly Guid Map = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly string Parent = new('A', 64), Package = new('B', 64);
    private sealed class Secret { public string AccessToken => "SYNTHETIC SECRET ACCESS"; }
    private sealed class Authority : INativeCommunityPublishAuthority
    {
        private readonly object _lease = new Secret(), _account = new Secret();
        private Guid _job = Guid.NewGuid();
        internal object CurrentLease;
        internal int Queues, Controls, Accepted;
        internal bool Complete, Paused, Reject;
        internal string Notes = "";
        internal string CurrentParent = Parent;
        internal Guid CurrentMap = Map;
        internal NativeCommunityPublishVisibility Visibility;
        internal Authority() => CurrentLease = _lease;
        internal void Reset() { CurrentLease = _lease; Complete = Paused = false; CurrentParent = Parent; CurrentMap = Map; _job = Guid.NewGuid(); }
        public bool IsCurrentAccountLease(object? lease) => ReferenceEquals(lease, CurrentLease);
        public Task<NativeCommunityPublishDraft> ReadAsync(NativeCommunityPublishSource source, CancellationToken token)
            => Task.FromResult(new NativeCommunityPublishDraft(source.MapId, source.RuntimeName, true, CurrentParent) { AccountLease = _lease, Account = _account });
        public Task<NativeCommunityPublishReceipt> QueueAsync(NativeCommunityPublishDraft draft, NativeCommunityPublishVisibility visibility, string notes, CancellationToken token)
        {
            Interlocked.Increment(ref Queues); Notes = notes; Visibility = visibility;
            if (Reject || draft.MapId != CurrentMap || draft.ParentHash != CurrentParent)
                throw new InvalidOperationException("SYNTHETIC SECRET write response");
            Interlocked.Increment(ref Accepted);
            return Task.FromResult(new NativeCommunityPublishReceipt(_job, draft.MapId, draft.RuntimeName, Package, true, draft.ParentHash, visibility)
                { AccountLease = draft.AccountLease, Account = draft.Account });
        }
        public Task<NativeCommunityPublishProgress> ReadTransferAsync(NativeCommunityPublishReceipt receipt, CancellationToken token)
            => Task.FromResult(new NativeCommunityPublishProgress(_job, Package, Complete ? "COMPLETED" : Paused ? "PAUSED" : "UPLOADING", Complete ? 1000 : 100, 1000,
                Complete, !Complete && !Paused, !Complete && Paused, false));
        public Task<NativeCommunityPublishProgress> ControlTransferAsync(NativeCommunityPublishReceipt receipt, bool pause, CancellationToken token)
        { Interlocked.Increment(ref Controls); Paused = pause; return ReadTransferAsync(receipt, token); }
    }
}
