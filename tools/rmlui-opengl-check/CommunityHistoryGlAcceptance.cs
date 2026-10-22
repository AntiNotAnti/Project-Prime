using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class CommunityHistoryGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-community-history-rml51"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi Community/revision modal with SYNTHETIC public/creator/history authorities. No HTTP/account/backend writes or cartridge/user files are used. Confirmed visibility/deletion/restoration changes affect only a fake in-memory catalog; installation is deliberately disabled without game files. This does not establish live Community service or installation acceptance.\n");
        Check(!GameFiles.Ready, "Community history fixture has no game assets and cannot install");
        var authority = new Authority(); var installer = new Installer();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeCommunityDiscoverPage(host, pages, new Reader(), installer, new Creator(authority), authority);
        host.Input.SetFramebufferScale(1, 1); page.Open(); var parent = pages.Page;
        int historyOpens = 0;
        ReadyBase();
        Click("native_community_history", RmlUiIntentKind.CommunityDetailAction, 3);
        var history = pages.Top; ReadyRows();
        Check(history != parent && NativeSemanticReadback.Read(host, history).ContainsText("SYNTHETIC COMMUNITY HISTORY"),
            "actual native history opens above selected public project");
        Check(!host.FocusDocument(history, "community_history_promote") && !host.FocusDocument(history, "community_history_install"),
            "public history cannot promote and asset-free fixture cannot install");
        Check(!page.Handle(new(RmlUiIntentKind.CommunityDetailAction, 0, parent, ulong.MaxValue)),
            "background project cannot install while revision modal owns foreground");
        Capture("public-history", origin: true);
        Click("community_history_next", RmlUiIntentKind.CommunityRevisionPage, 1);
        Check(NativeSemanticReadback.Read(host, history).Label("community_history_row0").Contains("REV 10"),
            "actual native revision paging resolves second eight-row slice");
        Click("community_history_next", RmlUiIntentKind.CommunityRevisionPage, 1);
        Click("community_history_row1", RmlUiIntentKind.CommunitySelectRevision, 1);
        Check(NativeSemanticReadback.Read(host, history).ContainsText("PACKAGE SHA-256 // " + Hash(1))
            && !host.FocusDocument(history, "community_history_next"), "historical selection retains exact first-revision SHA and bounded last page");
        Capture("historical-exact-package", origin: true);
        var external = pages.OpenModal(new("synthetic-history-foreground", "pages/social/confirm.rml", "social_confirm_cancel"));
        Frame();
        Check(!page.Handle(new(RmlUiIntentKind.CommunityConfirm, 0, history, ulong.MaxValue)),
            "unrelated modal fences history confirmation");
        pages.CloseModal(); Check(!host.IsAlive(external), "synthetic history foreground fence retires");
        Check(page.Back() && !host.IsAlive(history) && pages.Top == parent
            && host.FocusedElement() == "native_community_history", "Back closes history and restores source project focus");
        Check(!page.Handle(new(RmlUiIntentKind.CommunityConfirm, 0, history, ulong.MaxValue)),
            "retired history cannot promote a revision");

        Click("native_community_owned", RmlUiIntentKind.CommunityTab, 1); ReadyBase();
        Click("native_community_history", RmlUiIntentKind.CommunityDetailAction, 3); history = pages.Top; ReadyRows();
        Click("community_history_row1", RmlUiIntentKind.CommunitySelectRevision, 1);
        Click("community_history_promote", RmlUiIntentKind.CommunityCreatorAction, 0);
        Check(authority.Commands.Count == 0 && NativeSemanticReadback.Read(host, history).ContainsText("MAKE REVISION 17 THE CURRENT PUBLIC REVISION?")
            && !host.FocusDocument(history, "community_history_row2"), "native owned promotion captures exact revision and waits for explicit confirmation");
        Capture("confirm-exact-public-revision", origin: true);
        Click("community_history_dismiss", RmlUiIntentKind.CommunityCancel, 0);
        Check(host.IsAlive(history) && authority.Commands.Count == 0, "native Cancel Change keeps history open without mutation");
        Click("community_history_promote", RmlUiIntentKind.CommunityCreatorAction, 0);
        Click("community_history_confirm", RmlUiIntentKind.CommunityConfirm, 0); ReadyRows();
        var expected = authority.Commands.Single();
        Check(expected.MapId == Map && expected.Number == 17 && expected.Hash == Hash(17)
            && expected.CurrentHash == Hash(18) && expected.LatestHash == Hash(18)
            && expected.Account == Account && ReferenceEquals(expected.AccountLease, authority.Lease),
            "native confirmation sends exact captured project, current/latest hashes, revision and account witness only to fake authority");
        Check(NativeSemanticReadback.Read(host, history).Label("community_history_row1").Contains("CURRENT")
            && authority.CurrentHash == Hash(17) && authority.LatestHash == Hash(18),
            "native current-public state changes after verified fake readback while latest immutable revision remains");
        Capture("verified-public-readback", origin: true);

        // Keep the original public/history/promotion journey above; extend the same real modal
        // with retained-package lifecycle actions against the fake client authority only.
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        ConfirmLifecycle("community_history_draft", RmlUiIntentKind.CommunityCreatorAction, 2);
        Check(authority.CurrentHash == Hash(17) && authority.LatestHash == Hash(18)
            && authority.Revision(18).Draft && !authority.Revision(18).Listed,
            "native Draft preserves immutable latest package and valid current public pointer");
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        Check(!host.FocusDocument(history, "community_history_install"), "native private draft cannot enter anonymous installation owner");
        ConfirmLifecycle("community_history_publish", RmlUiIntentKind.CommunityVisibility, 0);
        Check(authority.CurrentHash == Hash(18) && authority.LatestHash == Hash(18)
            && authority.Revision(18).Listed && !authority.Revision(18).Draft,
            "native Publish exposes retained exact package and makes it current without new upload");
        Capture("lifecycle-published-retained-package", origin: true);
        ConfirmLifecycle("community_history_unlist", RmlUiIntentKind.CommunityCreatorAction, 1);
        Check(authority.CurrentHash == Hash(17) && authority.LatestHash == Hash(18)
            && !authority.Revision(18).Listed && !authority.Revision(18).Draft,
            "native Unlist of current falls back to highest active public revision");
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        ConfirmLifecycle("community_history_delete", RmlUiIntentKind.CommunityCreatorAction, 3);
        Check(authority.CurrentHash == Hash(17) && authority.LatestHash == Hash(17) && authority.Revision(18).Deleted
            && authority.Revision(18).DeleteAfter > DateTimeOffset.UtcNow && authority.RevisionCount == 18,
            "native Delete retains immutable tombstone and recomputes latest while preserving valid public pointer");
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        Check(NativeSemanticReadback.Read(host, history).Label("community_history_row0").Contains("DELETED")
            && NativeSemanticReadback.Read(host, history).ContainsText("SERVICE RESTORE DEADLINE")
            && !host.FocusDocument(history, "community_history_delete") && !host.FocusDocument(history, "community_history_publish"),
            "native deleted revision remains inspectable with actual fake retention deadline and restricted controls");
        Capture("lifecycle-retained-deleted-revision", origin: true);
        ConfirmLifecycle("community_history_restore", RmlUiIntentKind.CommunityCreatorAction, 4);
        Check(!authority.Revision(18).Deleted && authority.Revision(18).DeleteAfter == null
            && authority.CurrentHash == Hash(17) && authority.LatestHash == Hash(18) && !authority.Revision(18).Listed,
            "native Restore preserves prior unlisted visibility and does not replace valid current public revision");

        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        authority.CorruptReadbackOnce = true;
        int beforeUnknown = authority.Commands.Count;
        Click("community_history_delete", RmlUiIntentKind.CommunityCreatorAction, 3);
        Click("community_history_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => NativeSemanticReadback.Read(host, history).ContainsText("OUTCOME UNVERIFIED"),
            "native accepted delete with malformed readback reports an unverified outcome");
        Check(authority.Commands.Count == beforeUnknown + 1 && authority.LatestHash == Hash(17)
            && !host.FocusDocument(history, "community_history_restore"),
            "native unknown outcome removes action authority without pretending accepted delete was rolled back");
        Capture("lifecycle-unverified-write", origin: true);
        Click("community_history_refresh", RmlUiIntentKind.CommunityRefresh, 0); ReadyRows();
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        Check(NativeSemanticReadback.Read(host, history).Label("community_history_row0").Contains("DELETED"),
            "native read-only refresh reconciles changed latest pointer under the same account lease");
        ConfirmLifecycle("community_history_restore", RmlUiIntentKind.CommunityCreatorAction, 4);
        Check(authority.LatestHash == Hash(18), "native new confirmation uses reconciled latest witness");

        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        Click("community_history_publish", RmlUiIntentKind.CommunityVisibility, 0);
        int beforeStale = authority.Commands.Count;
        authority.Edit(15, row => row with { Listed = false });
        Click("community_history_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => NativeSemanticReadback.Read(host, history).ContainsText("REVISION HISTORY UNAVAILABLE"), "native stale exact history clears authority after failed fresh preflight");
        Check(authority.Commands.Count == beforeStale && authority.CurrentHash == Hash(17),
            "unrelated revision visibility change rejects old confirmation without claiming server CAS");
        Click("community_history_refresh", RmlUiIntentKind.CommunityRefresh, 0); ReadyRows();
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        ConfirmLifecycle("community_history_publish", RmlUiIntentKind.CommunityVisibility, 0);
        Check(authority.CurrentHash == Hash(18) && !authority.Revision(15).Listed,
            "explicit refresh captures changed history and retries one confirmed exact action");
        Click("community_history_row1", RmlUiIntentKind.CommunitySelectRevision, 1);
        ConfirmLifecycle("community_history_promote", RmlUiIntentKind.CommunityCreatorAction, 0);
        Check(authority.CurrentHash == Hash(17) && authority.LatestHash == Hash(18),
            "lifecycle extension returns to original historical-current baseline without changing immutable latest package");

        Click("community_history_row2", RmlUiIntentKind.CommunitySelectRevision, 2);
        authority.Reject = true;
        Click("community_history_promote", RmlUiIntentKind.CommunityCreatorAction, 0);
        Click("community_history_confirm", RmlUiIntentKind.CommunityConfirm, 0);
        Wait(() => NativeSemanticReadback.Read(host, history).ContainsText("REVISION HISTORY UNAVAILABLE"), "native failed preflight clears prior history");
        Check(authority.CurrentHash == Hash(17) && !host.FocusDocument(history, "community_history_promote"),
            "rejected fake promotion never changes current hash or leaves stale authority enabled");
        Capture("rejected-promotion", origin: true);
        authority.Reject = false;
        Click("community_history_refresh", RmlUiIntentKind.CommunityRefresh, 0); ReadyRows();
        authority.CurrentLease = new SecretLease(); Frame();
        var redacted = NativeSemanticReadback.Read(host, history);
        Check(redacted.ContainsText("ACCOUNT CHANGED") && !redacted.ContainsText("SYNTHETIC NOTES")
            && !redacted.ContainsText("PACKAGE SHA-256 // " + Hash(17))
            && !host.FocusDocument(history, "community_history_confirm") && !host.FocusDocument(history, "community_history_promote"),
            "account lease change immediately removes native private history and confirmation controls");
        Check(!redacted.ContainsText(Account.ToString()) && !redacted.ContainsText("SYNTHETIC SECRET"),
            "no private account identity or auth lease text enters native document");
        Capture("account-change-redacted", origin: true);
        Check(page.Back() && pages.Top == parent && installer.Attempts == 0 && installer.Registers == 0,
            "close cancels history without installing files or executing game authority");

        // A fresh synthetic account/catalog scenario exercises server last-active constraints.
        authority.CurrentLease = authority.Lease; authority.LastActiveOnly();
        Click("native_community_refresh", RmlUiIntentKind.CommunityRefresh, 0); ReadyBase();
        Click("native_community_history", RmlUiIntentKind.CommunityDetailAction, 3); history = pages.Top; ReadyRows();
        Check(!host.FocusDocument(history, "community_history_delete") && authority.ActiveCount == 1,
            "native Delete is unavailable for last active revision even with retained deleted history");
        Click("community_history_row1", RmlUiIntentKind.CommunitySelectRevision, 1);
        ConfirmLifecycle("community_history_restore", RmlUiIntentKind.CommunityCreatorAction, 4);
        Check(authority.ActiveCount == 2 && authority.CurrentHash == Hash(18) && authority.LatestHash == Hash(18),
            "native Restore adds retained active revision without replacing valid latest/current");
        Click("community_history_row1", RmlUiIntentKind.CommunitySelectRevision, 1);
        ConfirmLifecycle("community_history_delete", RmlUiIntentKind.CommunityCreatorAction, 3);
        Check(authority.ActiveCount == 1 && authority.Revision(17).Deleted && authority.RevisionCount == 18,
            "native Delete becomes available after restoration and still retains exact historical packages");
        authority.Edit(17, row => row with { DeleteAfter = DateTimeOffset.UtcNow.AddSeconds(-1) });
        Click("community_history_refresh", RmlUiIntentKind.CommunityRefresh, 0); ReadyRows();
        Click("community_history_row1", RmlUiIntentKind.CommunitySelectRevision, 1);
        Check(!host.FocusDocument(history, "community_history_restore"), "native Restore disables an expired service retention deadline");
        AssertLifecycleLayout();
        Capture("lifecycle-last-active-expired-restore", origin: true);
        Click("community_history_row0", RmlUiIntentKind.CommunitySelectRevision, 0);
        Click("community_history_draft", RmlUiIntentKind.CommunityCreatorAction, 2);
        int beforeLease = authority.Commands.Count;
        authority.CurrentLease = new SecretLease(); Frame();
        Check(!host.FocusDocument(history, "community_history_confirm") && authority.Commands.Count == beforeLease
            && NativeSemanticReadback.Read(host, history).ContainsText("ACCOUNT CHANGED"),
            "account switch during native lifecycle confirmation clears private draft proposal before any write");
        Check(page.Back(), "native lifecycle fixture closes account-invalidated history");
        page.Dispose();
        Check(!host.IsAlive(parent) && GL.GetError() == ErrorCode.NoError, "Community history fixture retires native documents without GL errors");
        Console.WriteLine("RML COMMUNITY HISTORY GL synthetic-only screenshots: " + output);

        void Frame()
        {
            page.Refresh(); host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(0.025f, 0.05f, 0.08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int milliseconds)
        {
            var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds); Frame();
        }
        void Wait(Func<bool> predicate, string label)
        {
            var timer = Stopwatch.StartNew();
            do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); } while (timer.Elapsed < TimeSpan.FromSeconds(4));
            throw new TimeoutException(label);
        }
        void ReadyBase()
        {
            Wait(() => host.FocusDocument(parent, "native_community_details"), "native project selection settles synthetic catalog");
            if (!host.FocusDocument(parent, "native_community_history"))
                Click("native_community_details", RmlUiIntentKind.CommunityDetailAction, 7);
            Wait(() => host.FocusDocument(parent, "native_community_history"), "expanded technical details expose revision history");
        }
        void ReadyRows() => Wait(() => host.FocusDocument(pages.Top, "community_history_row0"), "native history settles bounded synthetic revisions");
        void AssertLifecycleLayout()
        {
            string[] ids = { "community_history_promote", "community_history_publish", "community_history_unlist",
                "community_history_draft", "community_history_delete", "community_history_restore" };
            Check(host.TryGetElementBounds(history, "community_history_scroll", out float sx, out float sy, out float sw, out float sh)
                && sh >= 38 && sx >= 0 && sx + sw <= width + 1, "history owns a bounded independent detail scroll");
            float previousBottom = float.MinValue;
            foreach (string id in ids)
            {
                Check(host.TryGetElementBounds(history, id, out float x, out float y, out float w, out float h),
                    "all lifecycle controls expose native geometry including disabled: " + id);
                Check(x >= sx - 1 && x + w <= sx + sw + 1 && x >= 0 && x + w <= width + 1,
                    "lifecycle button is horizontally contained: " + id);
                Check(y >= previousBottom + 1, "lifecycle buttons have disjoint vertical bounds: " + id);
                previousBottom = y + h;
            }
            host.Input.PointerMoved(sx + sw / 2d, sy + sh / 2d); host.Input.PointerWheel(1000); Settle(450);
            foreach (string id in ids)
            {
                bool reachable = false;
                for (int attempt = 0; attempt < 45; attempt++)
                {
                    host.TryGetElementBounds(history, "community_history_scroll", out sx, out sy, out sw, out sh);
                    if (host.TryGetElementBounds(history, id, out float x, out float y, out float w, out float h)
                        && y >= sy - 1 && y + h <= sy + sh + 1 && y >= 0 && y + h <= height)
                    { reachable = true; break; }
                    host.Input.PointerMoved(sx + sw / 2d, sy + sh / 2d); host.Input.PointerWheel(-1); Settle(180);
                }
                Check(reachable, "disabled or enabled lifecycle control is fully reachable by scrolling: " + id);
                Check(host.TryGetElementBounds(history, "community_history_close", out float bx, out float by, out float bw, out float bh)
                    && bx >= 0 && by >= sy + sh - 1 && bx + bw <= width && by + bh <= height,
                    "Back footer remains visible while scrolling lifecycle controls: " + id);
            }
        }
        void ConfirmLifecycle(string id, RmlUiIntentKind kind, int argument)
        {
            int before = authority.Commands.Count;
            Click(id, kind, argument);
            Check(authority.Commands.Count == before && !host.FocusDocument(history, "community_history_row0"),
                "native lifecycle proposal waits for exact confirmation: " + id);
            Click("community_history_confirm", RmlUiIntentKind.CommunityConfirm, 0); ReadyRows();
            Check(authority.Commands.Count == before + 1, "native lifecycle confirmation submits once: " + id);
        }
        void Click(string id, RmlUiIntentKind kind, int argument)
        {
            Frame(); var target = pages.Top;
            Check(host.FocusDocument(target, id), "native focus reaches " + id); Settle(450);
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h), "native history bounds resolve " + id);
            double px = x + w / 2d, py = y + h / 2d;
            host.Input.PointerMoved(px, py); Frame();
            Console.WriteLine($"RML COMMUNITY HISTORY POINTER {id} bounds={x},{y},{w},{h} center={px},{py} framebuffer={width}x{height} hovered={host.HoveredElement()} focused={host.FocusedElement()}");
            if (id == "native_community_history") Capture($"diagnostic-before-history-{++historyOpens}", origin: false);
            Check(px >= 0 && px < width && py >= 0 && py < height, "native history button is within framebuffer: " + id);
            host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                Check(RmlUiPageCommandRouting.Community(intent.Kind), "production root routing delegates actual native history intent: " + id);
                bool accepted = page.Handle(intent);
                if (intent.Kind != kind || intent.Argument != argument) continue;
                Check(accepted && intent.Document == target, "actual native pointer dispatches history action " + id);
                Check(!page.Handle(intent), "duplicate native history sequence rejected: " + id); received = true;
            }
            Check(received, "native history button emits expected action: " + id); Settle(180);
        }
        void Capture(string name, bool origin)
        {
            if (origin) { host.Input.PointerMoved(width / 2d, height / 2d); host.Input.PointerWheel(1000); Settle(450); }
            else Settle(180);
            byte[] pixels = FinalCompositeCapture.Read(width, height);
            string path = Path.Combine(output, $"synthetic-{name}-{width}x{height}.ppm");
            using (var stream = File.Create(path))
            {
                stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC COMMUNITY HISTORY - ACTUAL NATIVE GL - NO BACKEND WRITES\n{width} {height}\n255\n"));
                for (int row = height - 1; row >= 0; row--) stream.Write(pixels, row * width * 3, width * 3);
            }
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native Community revision journey with synthetic catalog/account lease/promotion; no HTTP/backend writes/game files/installs.");
        }
    }
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); Console.WriteLine("RML COMMUNITY HISTORY GL PASS " + label); }
    private static readonly Guid Map = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly Guid Account = Guid.Parse("bbbbbbbb-1111-4111-8111-111111111111");
    private static string Hash(int number) => number.ToString("X64");
    private static NativeCommunityEntry Entry(int number = 18) => new(Map,"SYNTHETIC COMMUNITY HISTORY","SYNTHETIC CREATOR",Hash(number),number,"BATTLE",1,8);
    private sealed class Reader : INativeCommunityReader
    { public Task<NativeCommunityPage> BrowseAsync(string query,string sort,int page,CancellationToken token) => Task.FromResult(new NativeCommunityPage(new[]{Entry()},1,1,8)); }
    private sealed class Installer : INativeCommunityInstaller
    {
        internal int Attempts, Registers;
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,CancellationToken token)
        { Attempts++; throw new InvalidOperationException("Asset-free fixture must never install."); }
        public void Register(NativeCommunityInstalled installed) { Registers++; throw new InvalidOperationException("Fixture must never register a map."); }
    }
    private sealed class SecretLease { public string AccessToken { get; } = "SYNTHETIC SECRET ACCESS"; }
    private sealed class Creator(Authority owner) : INativeCommunityCreatorAuthority, INativeCommunityCreatorAccountLease
    {
        public bool IsCurrentAccountLease(object? lease) => owner.IsCurrentAccountLease(lease);
        private static NativeCreatorEntry EntryOwned() => new(Map,"SYNTHETIC COMMUNITY HISTORY",Hash(18),Hash(18),18,false,false,true);
        public Task<NativeCreatorPage> BrowseMineAsync(int page,CancellationToken token)
            => Task.FromResult(new NativeCreatorPage(new[]{EntryOwned()},1,1){AccountLease=owner.Lease});
        public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid map,CancellationToken token) => Task.FromResult<NativeCreatorEntry?>(EntryOwned());
        public Task<bool> ExecuteAsync(NativeCreatorAction action,Guid map,CancellationToken token)
            => throw new InvalidOperationException("Fixture must not execute project lifecycle actions.");
    }
    private sealed class Authority : INativeCommunityRevisionsAuthority
    {
        internal readonly object Lease = new SecretLease();
        internal object CurrentLease;
        internal string CurrentHash = Hash(18), LatestHash = Hash(18);
        internal bool Reject, CorruptReadbackOnce;
        internal readonly List<NativeCommunityPromotion> Commands = new();
        private NativeCommunityRevision[] _rows = Enumerable.Range(1,18).Select(i=>new NativeCommunityRevision(i,Hash(i),i==1?"":Hash(i-1),
            "SYNTHETIC NOTES "+i,DateTimeOffset.UnixEpoch.AddDays(i),true,false,false,Entry(i))).ToArray();
        internal int RevisionCount => _rows.Length;
        internal int ActiveCount => _rows.Count(row=>!row.Deleted);
        internal NativeCommunityRevision Revision(int number) => _rows.Single(row=>row.Number==number);
        internal void Edit(int number,Func<NativeCommunityRevision,NativeCommunityRevision> edit)
            => _rows=_rows.Select(row=>row.Number==number?edit(row):row).ToArray();
        internal void LastActiveOnly()
        {
            _rows=_rows.Select(row=>row.Number==18?row with{Deleted=false,Listed=true,Draft=false,DeleteAfter=null}
                :row with{Deleted=true,DeleteAfter=DateTimeOffset.UtcNow.AddDays(1)}).ToArray();
            CurrentHash=LatestHash=Hash(18);
        }
        internal Authority() => CurrentLease = Lease;
        public bool IsCurrentAccountLease(object? lease) => ReferenceEquals(lease,CurrentLease);
        public Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(request.MapId!=Map || request.AnchorHash!=LatestHash && !(request.Owned && request.ReconcileAfterWrite)
                || request.Owned && !IsCurrentAccountLease(request.AccountLease))
                throw new InvalidOperationException("Synthetic stale project/account.");
            return Task.FromResult(Catalog(request.Owned));
        }
        private NativeCommunityRevisionCatalog Catalog(bool owned) => new(Map,"SYNTHETIC COMMUNITY HISTORY",CurrentHash,LatestHash,
            owned,false,false,_rows.ToArray())
            {Account=owned?Account:Guid.Empty,AccountLease=owned?Lease:null,OwnerId=owned?"SYNTHETIC PRIVATE OWNER":""};
        public Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion request,CancellationToken token)
            => ChangeAsync(request,token);
        public Task<NativeCommunityRevisionCatalog> ChangeAsync(NativeCommunityPromotion request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(Reject || !IsCurrentAccountLease(request.AccountLease) || request.Account!=Account || request.MapId!=Map
                || request.CurrentHash!=CurrentHash || request.LatestHash!=LatestHash || request.Hash!=Hash(request.Number)
                || !NativeCommunityRevisionPolicy.MatchesPreflight(request,Catalog(true)))
                throw new InvalidOperationException("SYNTHETIC SECRET preflight rejection");
            Edit(request.Number,row=>request.Action switch
            {
                NativeCommunityRevisionAction.Promote or NativeCommunityRevisionAction.Publish=>row with{Listed=true,Draft=false},
                NativeCommunityRevisionAction.Unlist=>row with{Listed=false,Draft=false},
                NativeCommunityRevisionAction.Draft=>row with{Listed=false,Draft=true},
                NativeCommunityRevisionAction.Delete=>row with{Deleted=true,DeleteAfter=DateTimeOffset.UtcNow.AddDays(1)},
                _=>row with{Deleted=false,DeleteAfter=null}
            });
            // Match existing ApplyVisibility/RecomputePointersLocked behavior. This fake models
            // client preflight plus the old endpoint; it does not imply a server CAS contract.
            if(request.Action is NativeCommunityRevisionAction.Promote or NativeCommunityRevisionAction.Publish)CurrentHash=request.Hash;
            else if(!_rows.Any(row=>row.Hash==CurrentHash&&!row.Deleted&&row.Listed&&!row.Draft))
                CurrentHash=_rows.Where(row=>!row.Deleted&&row.Listed&&!row.Draft).MaxBy(row=>row.Number)?.Hash??"";
            if(request.Action is NativeCommunityRevisionAction.Delete or NativeCommunityRevisionAction.Restore)
                LatestHash=_rows.Where(row=>!row.Deleted).MaxBy(row=>row.Number)!.Hash;
            Commands.Add(request);
            var result = Catalog(true);
            if(CorruptReadbackOnce)
            {
                CorruptReadbackOnce=false;
                result=result with{LatestHash=request.LatestHash};
            }
            return Task.FromResult(result);
        }
    }
}
