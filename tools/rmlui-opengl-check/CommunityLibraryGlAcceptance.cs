using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.MapGen;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class CommunityLibraryGlAcceptance
{
    private const string Scope = "Actual native Community Library DOM/input/modal owners in the existing NativeWindow/context; explicit synthetic library/account/queue authorities and recording clipboard. No HTTP, live account, OS clipboard, installation, reports or game assets.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_LIBRARY_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-community-library-rml85", width + "x" + height));
        Directory.CreateDirectory(output);
        Check(!GameFiles.Ready, "library fixture owns no configured game assets");
        var fixture = new Fixture(); var copied = new List<string>(); var evidence = new List<object>();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeCommunityDiscoverPage(host, pages, fixture, fixture, fixture, fixture, fixture,
            clipboard: text => { copied.Add(text); return true; });
        host.Input.SetFramebufferScale(1, 1); page.Open(); var parent = pages.Page;
        try
        {
            Wait(() => Has("SYNTHETIC PUBLIC ARENA"), "public library readback");
            CheckListPaging(1);
            Activate("native_community_copy_link", RmlUiIntentKind.CommunityDetailAction, 6);
            string expectedLink = new Uri(MapCommunityClient.ValidateBaseAddress(NetworkMapIdentity.ConfiguredDownloadSource()),
                "packages/" + Fixture.Hash).AbsoluteUri;
            Check(copied.SequenceEqual(new[] { expectedLink }), "one native Copy Link activation records exact package SHA once without OS clipboard");
            Check(Has("EXACT PACKAGE LINK COPIED"), "copy outcome is exposed through native status");
            Capture("public-exact-package");

            Activate("native_community_installed", RmlUiIntentKind.CommunityTab, 4, pointer: false);
            Wait(() => Has("SYNTHETIC LOCAL INSTALLED"), "local installed catalog readback");
            CheckListPaging(1);
            var installedDetail = NativeSemanticReadback.Read(host, parent);
            Check(installedDetail.Label("native_community_hash") == Fixture.Hash,
                "installed detail exposes the complete exact local package hash");
            Check(installedDetail.Label("native_community_description").Contains("EXACT LOCAL PACKAGE"),
                "installed detail identifies its exact local package");
            Check(installedDetail.Label("native_community_description").Contains("READY IN MAP PICKER"),
                "installed detail exposes the existing map-picker handoff");
            foreach (string id in new[] { "native_community_favorite", "native_community_install", "native_community_history", "native_community_copy_link" })
                Check(!host.FocusDocument(parent, id), "installed library hides remote action: " + id);
            Check(fixture.InstallCalls == 0 && fixture.HistoryCalls == 0, "local installed inspection never contacts installer or remote history");
            Capture("installed-local-package");

            Activate("native_community_favorites", RmlUiIntentKind.CommunityTab, 3);
            Wait(() => Has("SYNTHETIC FAVORITE ARENA"), "verified Favorites catalog");
            CheckListPaging(1);
            Check(Has("REMOVE FAVORITE"), "Favorites shows authoritative removal action");
            fixture.HoldFavorite = true;
            Activate("native_community_favorite", RmlUiIntentKind.CommunityDetailAction, 2, pointer: false);
            Wait(() => fixture.FavoriteCalls.Count == 1, "synthetic remove command is submitted");
            var remove = fixture.FavoriteCalls.Single();
            Check(!remove.Favorite && remove.Hash == Fixture.Hash && ReferenceEquals(remove.Witness, fixture.Witness),
                "Favorites sends desired false with exact package and opaque current witness");
            Check(Has("SYNTHETIC FAVORITE ARENA") && !host.FocusDocument(parent, "native_community_favorite"),
                "pending favorite command keeps verified row and disables repeat mutation");
            fixture.HoldRead = true; fixture.HoldFavorite = false;
            fixture.FavoriteCompletion.TrySetResult(fixture.Witness);
            Wait(() => fixture.PendingReads.Count > 0, "accepted favorite update requests authoritative readback");
            Check(Has("SYNTHETIC FAVORITE ARENA"), "accepted favorite update does not optimistically remove the old row");
            fixture.ReleaseRead(Array.Empty<NativeCommunityEntry>());
            Wait(() => Has("NO FAVORITES FOUND"), "service readback removes favorite row");
            Check(!host.FocusDocument(parent, "native_community_row0"), "empty Favorites contains no actionable stale row");
            Capture("favorites-verified-removal");

            Activate("native_community_public", RmlUiIntentKind.CommunityTab, 0);
            Wait(() => Has("SYNTHETIC PUBLIC ARENA"), "return to public library");
            Activate("native_community_favorite", RmlUiIntentKind.CommunityDetailAction, 2);
            Wait(() => fixture.FavoriteCalls.Count == 2, "public add favorite command");
            Check(fixture.FavoriteCalls.Last().Favorite, "Discover sends desired true even for a row already marked favorite");
            Wait(() => Has("SHOWING"), "public favorite readback");
            Activate("native_community_favorites", RmlUiIntentKind.CommunityTab, 3);
            Wait(() => Has("SYNTHETIC FAVORITE ARENA"), "replacement Favorites readback");
            fixture.Witness = new object(); Frame();
            Wait(() => Has("ACCOUNT OR GAME FILES CHANGED"), "opaque lease replacement retires private library rows");
            Check(!Has("SYNTHETIC FAVORITE ARENA") && !host.FocusDocument(parent, "native_community_row0"),
                "changed lease clears private labels and selected action");
            Capture("account-lease-retired");
            Activate("native_community_refresh", RmlUiIntentKind.CommunityRefresh, 0);
            Wait(() => Has("SYNTHETIC FAVORITE ARENA"), "explicit refresh verifies replacement account witness");

            fixture.FailPrivate = true;
            Activate("native_community_owned", RmlUiIntentKind.CommunityTab, 1, pointer: false);
            Wait(() => Has("UNAVAILABLE"), "private catalog fails without exposing service error");
            Check(!Has("PRIVATE ERROR SECRET"), "private failure text is sanitized");
            Capture("private-failure");
            fixture.FailPrivate = false;
            Activate("native_community_refresh", RmlUiIntentKind.CommunityRefresh, 0);
            Wait(() => Has("NO OWNED"), "private catalog retries through its existing owner");
            Activate("native_community_transfers", RmlUiIntentKind.CommunityTab, 2);
            var transfers = pages.Top;
            Check(transfers != parent && Has("DOWNLOADS & HISTORY"), "existing transfer modal owns native foreground");
            Check(!page.Handle(new(RmlUiIntentKind.CommunityTab, 4, parent, ulong.MaxValue)),
                "background library tab cannot bypass the current modal owner");
            Activate("community_transfers_close", RmlUiIntentKind.CommunityCancel, 0, pointer: false);
            Check(pages.Top == parent && host.CurrentInputDocument == parent, "transfer Back restores original library document");

            Activate("native_community_public", RmlUiIntentKind.CommunityTab, 0);
            Wait(() => Has("SYNTHETIC PUBLIC ARENA"), "public catalog after modal");
            Check(Has("LONG SYNTHETIC NOTES"), "native details expose authored long description");
            Reach("native_community_notes"); Capture("long-notes-detail");
            host.SetField(parent, "native_community_search", "NO SYNTHETIC MATCH");
            Check(host.ReadField(parent, "native_community_search", 256) == "NO SYNTHETIC MATCH", "native search field accepts fixture text");
            Activate("native_community_find", RmlUiIntentKind.CommunitySearch, 0, pointer: false);
            Wait(() => Has("NO PUBLISHED MAPS FOUND"), "empty native search clears public selection");
            Check(!host.FocusDocument(parent, "native_community_copy_link") && !host.FocusDocument(parent, "native_community_row0"),
                "empty search cannot copy/install a stale package");
            Capture("empty-search");
            host.SetField(parent, "native_community_search", "");
            Check(host.ReadField(parent, "native_community_search", 256) == "", "native search clears");
            Activate("native_community_find", RmlUiIntentKind.CommunitySearch, 0);
            Wait(() => Has("SYNTHETIC PUBLIC ARENA"), "cleared query returns catalog");
            foreach (string id in new[] { "native_community_public", "native_community_installed", "native_community_favorites", "native_community_owned", "native_community_transfers", "native_community_import" }) Reach(id);
            Capture("navigation-tabs");
            Check(fixture.InstallCalls == 0 && fixture.HistoryCalls == 0 && fixture.Mutations == 0,
                "library journey performs no install, remote history or creator/queue mutation");
            Check(copied.Count == 1 && GL.GetError() == ErrorCode.NoError, "native library preserves one clipboard call and leaves no GL error");
            Save(true, null);
        }
        catch (Exception error) { Capture("failure"); Save(false, error.ToString()); throw; }
        finally { fixture.ReleaseRead(Array.Empty<NativeCommunityEntry>()); fixture.FavoriteCompletion.TrySetCanceled(); }

        bool Has(string text) => NativeSemanticReadback.Read(host, pages.Top).ContainsText(text);
        void Frame()
        {
            page.Refresh(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle(int ms = 180)
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < ms); }
        void Wait(Func<bool> predicate, string label)
        { var timer = Stopwatch.StartNew(); do { Frame(); if (predicate()) { Check(true, label); return; } Thread.Sleep(5); } while (timer.ElapsedMilliseconds < 5000); throw new TimeoutException(label); }
        (float X, float Y, float W, float H) Reach(string id)
        {
            Frame(); var document = pages.Top;
            Check(host.FocusDocument(document, id) || id == "native_community_notes", "native focus or readable paragraph exists: " + id);
            Settle();
            for (int i = 0; i < 160; i++)
            {
                if (host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                    && x >= 0 && x + w <= width + 1 && y >= 0 && y < height
                    && y + (id == "native_community_notes" ? Math.Min(h, 20) : h) <= height)
                    return (x, y, w, h);
                Check(host.TryGetElementBounds(document, "native_community", out float sx, out float sy, out float sw, out float sh),
                    "native library scroll viewport exists");
                host.Input.PointerMoved(sx + sw / 2, sy + sh / 2);
                // Begin at the top, then scan actual native scroll positions.
                host.Input.PointerWheel(i == 0 ? 10000 : -2); Settle(20);
            }
            throw new InvalidOperationException("Native library target is unreachable: " + id);
        }
        void Activate(string id, RmlUiIntentKind kind, int argument, bool pointer = true)
        {
            if (id is "native_community_history" or "native_community_copy_link" or "native_community_report"
                && pages.Top == parent && !host.FocusDocument(parent, id))
                Activate("native_community_details", RmlUiIntentKind.CommunityDetailAction, 7, pointer);
            var box = Reach(id); var document = pages.Top; host.DiscardIntents();
            if (pointer)
            {
                double x = box.X + box.W / 2, y = box.Y + Math.Min(box.H, height - box.Y) / 2;
                host.Input.PointerMoved(x, y); Frame();
                host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false);
            }
            else { host.Input.Key(2, true); host.Input.Key(2, false); }
            host.Update(); int emitted = 0;
            while (host.TryTakeIntent(out var intent))
            {
                Check(intent.Kind == kind && intent.Argument == argument && intent.Document == document,
                    "actual native event identifies current library target: " + id);
                Check(RmlUiPageCommandRouting.Community(intent.Kind) && page.Handle(intent), "existing Community owner handles native action: " + id);
                Check(!page.Handle(intent), "duplicate native sequence cannot repeat action: " + id); emitted++;
            }
            Check(emitted == 1, "one native activation emits exactly one action: " + id); Settle();
        }
        float FirstRowTop()
        {
            Check(host.TryGetElementBounds(parent, "native_community_row0", out _, out var y, out _, out _), "first collection row resolves");
            return y;
        }
        void CheckListPaging(int visibleRows)
        {
            Frame(); float lastBottom = float.NegativeInfinity;
            for (int i = 0; i < visibleRows; i++)
            {
                string id = "native_community_row" + i;
                Check(host.TryGetElementBounds(parent, id, out float x, out float y, out float w, out float h)
                    && w > 0 && h > 0 && y >= lastBottom - 1,
                    "catalog rows have separate positive vertical bounds: " + id);
                lastBottom = y + h;
            }
            foreach (string id in new[] { "native_community_previous", "native_community_next" })
                Check(host.TryGetElementBounds(parent, id, out float x, out float y, out float w, out float h)
                    && w > 0 && h > 0 && y + h <= FirstRowTop() + 1,
                    "header paging controls remain separate from collection rows: " + id);
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, "synthetic-library-" + name + "-" + width + "x" + height + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "actual library framebuffer captured: " + name);
            FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
            string semantics = path + ".semantic.json";
            File.WriteAllText(semantics, NativeSemanticReadback.Read(host, pages.Top).RawJson);
            evidence.Add(new { name, path, semantics, document = pages.Top.ToString(), focus = host.FocusedElement() });
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(output, "library-" + width + "x" + height + ".json"),
            JsonSerializer.Serialize(new { passed, scope = Scope, width, height, copied, favorites = fixture.FavoriteCalls.ToArray(), evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("COMMUNITY LIBRARY GL PASS " + label); }

    private sealed record FavoriteCommand(string Hash, bool Favorite, object? Witness);
    private sealed class Fixture : INativeCommunityLibraryReader, INativeCommunityInstaller, INativeCommunityCreatorAuthority,
        INativeCommunityRevisionsAuthority, INativeCommunityTransfersAuthority
    {
        internal static readonly string Hash = new('a', 64);
        internal object Witness = new();
        internal bool HoldFavorite, HoldRead, FailPrivate;
        internal int InstallCalls, HistoryCalls, Mutations;
        internal readonly ConcurrentQueue<FavoriteCommand> FavoriteCalls = new();
        internal readonly ConcurrentQueue<TaskCompletionSource<NativeCommunityPage>> PendingReads = new();
        internal readonly TaskCompletionSource<object> FavoriteCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private NativeCommunityEntry Entry(string name, bool installed = false) => new(Guid.Parse("11111111-1111-4111-8111-111111111111"),
            name, "SYNTHETIC CREATOR", Hash, 7, "Battle", 2, 8)
            { Installed = installed, Favorited = true, Version = "fixture-v7", Description = "LONG SYNTHETIC NOTES\n" + string.Join(" ", Enumerable.Repeat("Synthetic immutable package description for native wrapping and scrolling.", 12)) };
        public bool IsCurrentWitness(object? witness) => ReferenceEquals(witness, Witness);
        public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken token)
            => BrowseLibraryAsync(NativeCommunityCatalog.Discover, query, sort, page, token);
        public Task<NativeCommunityPage> BrowseLibraryAsync(NativeCommunityCatalog catalog, string query, string sort, int page, CancellationToken token)
        {
            if (HoldRead)
            { var pending = new TaskCompletionSource<NativeCommunityPage>(TaskCreationOptions.RunContinuationsAsynchronously); PendingReads.Enqueue(pending); return pending.Task; }
            var rows = query.Length > 0 ? Array.Empty<NativeCommunityEntry>() : new[] { Entry(catalog switch {
                NativeCommunityCatalog.Installed => "SYNTHETIC LOCAL INSTALLED", NativeCommunityCatalog.Favorites => "SYNTHETIC FAVORITE ARENA", _ => "SYNTHETIC PUBLIC ARENA" }, catalog == NativeCommunityCatalog.Installed) };
            return Task.FromResult(new NativeCommunityPage(rows, rows.Length, 1, 8) { Witness = Witness });
        }
        internal void ReleaseRead(NativeCommunityEntry[] rows)
        { HoldRead = false; while (PendingReads.TryDequeue(out var read)) read.TrySetResult(new(rows, rows.Length, 1, 8) { Witness = Witness }); }
        public Task<object> SetFavoriteAsync(NativeCommunityEntry selected, bool favorite, object? expectedWitness, CancellationToken token)
        {
            if (!ReferenceEquals(expectedWitness, Witness)) throw new InvalidOperationException("Synthetic stale witness");
            FavoriteCalls.Enqueue(new(selected.LatestHash, favorite, expectedWitness));
            return HoldFavorite ? FavoriteCompletion.Task : Task.FromResult(Witness);
        }
        public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected, CancellationToken token)
        { Interlocked.Increment(ref InstallCalls); throw new InvalidOperationException("No synthetic installations"); }
        public void Register(NativeCommunityInstalled installed) { Interlocked.Increment(ref InstallCalls); throw new InvalidOperationException(); }
        public Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token)
            => FailPrivate ? Task.FromException<NativeCreatorPage>(new IOException("PRIVATE ERROR SECRET"))
                : Task.FromResult(new NativeCreatorPage(Array.Empty<NativeCreatorEntry>(), 0, 1));
        public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid id, CancellationToken token) { Mutations++; throw new InvalidOperationException(); }
        public Task<bool> ExecuteAsync(NativeCreatorAction action, Guid id, CancellationToken token) { Mutations++; throw new InvalidOperationException(); }
        public Task<NativeCommunityRevisionCatalog> ReadAsync(NativeCommunityHistoryRequest request, CancellationToken token) { HistoryCalls++; throw new InvalidOperationException(); }
        public Task<NativeCommunityRevisionCatalog> PromoteAsync(NativeCommunityPromotion request, CancellationToken token) { Mutations++; throw new InvalidOperationException(); }
        public bool IsCurrentAccountLease(object? lease) => false;
        public Task<NativeCommunityTransferReceipt[]> ReadAsync(CancellationToken token) => Task.FromResult(Array.Empty<NativeCommunityTransferReceipt>());
        public Task<NativeCommunityTransferReceipt[]> ControlAsync(NativeCommunityTransferReceipt expected, NativeCommunityTransferAction action, CancellationToken token) { Mutations++; throw new InvalidOperationException(); }
    }
}
