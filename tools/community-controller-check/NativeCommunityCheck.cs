#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.MapGen;

internal static class NativeCommunityCheck
{
    public static void Run(string library, string assets)
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("Community native: " + name); checks++; Console.WriteLine("PASS " + name); }
        nint module = NativeLibrary.Load(Path.GetFullPath(library));
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
        try
        {
            foreach (var viewport in new[] { (1280, 720, 1f), (1920, 1080, 1f), (960, 540, 1f), (1920, 1080, 2f) })
            {
                using var host = new RmlUiHost();
                Check(host.Initialize(viewport.Item1, viewport.Item2, viewport.Item3, Path.GetFullPath(assets), RmlUiRenderBackend.DrawList), "real native bridge initialization");
                using var pages = new RmlUiPageManager(host);
                var backend = new FakeCommunityBackend();
                backend.Projects[16] = backend.Projects[16] with { DisplayName = "<button id='injected_action' data-action='community:confirm'>bad</button>" };
                var fields = new NativeCommunityFields(backend);
                using var controller = new CommunityController(fields);
                using var presenter = new CommunityPagePresenter(host, pages, controller);
                void Drain()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (controller.Snapshot().Busy) { presenter.Tick(); host.Update(); if (DateTime.UtcNow > deadline) throw new TimeoutException("Native Community worker"); Thread.Sleep(1); }
                    presenter.Tick(); host.Update();
                }
                void Press(string id, RmlUiIntentKind expected, int? argument = null)
                {
                    Check(host.FocusDocument(pages.Top, id), "focus " + id);
                    host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                    Check(host.TryTakeIntent(out var action) && action.Kind == expected && action.Document == pages.Top && (!argument.HasValue || action.Argument == argument.Value), "actual DOM typed action " + id);
                    Check(presenter.HandleAction(action), "presenter accepts " + id); host.Update();
                }
                presenter.Open(); Drain(); var page = presenter.Document;
                Check(host.IsAlive(page) && pages.PageKey == "community" && host.FocusedElement() == "community_search", "authored shared-shell Community document opens");
                string unicodeAddress = "http://127.0.0.1:47800/";
                unicodeAddress += new string('界', 2048 - unicodeAddress.Length);
                host.SetField(page, "community_address", unicodeAddress);
                Press("community_refresh", RmlUiIntentKind.CommunityRefresh); Drain();
                Check(controller.Snapshot().Address == unicodeAddress && fields.LastAddress == unicodeAddress,
                    "maximum authored Unicode service address reaches endpoint validation without UTF-8 truncation");
                string unicodeSearch = new string('界', 200);
                host.SetField(page, "community_search", unicodeSearch); Press("community_search_apply", RmlUiIntentKind.CommunitySearch);
                Check(controller.Snapshot().Search == unicodeSearch && controller.Snapshot().Total == 0,
                    "maximum Unicode search remains an exact native submitted query");
                host.SetField(page, "community_search", ""); Press("community_search_apply", RmlUiIntentKind.CommunitySearch);
                Check(!host.TryGetElementBounds(page, "injected_action", out _, out _, out _, out _), "untrusted catalog metadata remains escaped native text");
                Check(host.TryGetElementBounds(page, "community_search", out var sx, out _, out var sw, out _) && sx >= 0 && sw > 0 && sx + sw <= viewport.Item1 + 1, "search input fits actual viewport/density");
                Check(host.TryGetElementBounds(page, "community_heading", out _, out var hy, out _, out var hh)
                    && host.TryGetElementBounds(page, "community_description", out _, out var dy, out _, out _) && dy >= hy + hh + 4, "native heading and paragraph occupy separate block flow");
                bool catalogBounds = host.TryGetElementBounds(page, "community_catalog", out var ax, out var ay, out var aw, out var ah);
                bool detailBounds = host.TryGetElementBounds(page, "community_detail_panel", out var bx, out var by, out var bw, out var bh);
                Check(catalogBounds && detailBounds && aw >= viewport.Item1 * .32f && bw >= viewport.Item1 * .32f
                    && (Math.Abs(ay - by) < 1 ? ax + aw <= bx : ay + ah <= by), "catalog/detail panels have readable widths and do not overlap");
                foreach (var item in new[] { ("community_select_0", ax, aw), ("community_detail_5", bx, bw) })
                    Check(host.TryGetElementBounds(page, item.Item1, out var x, out _, out var width, out var height)
                        && width >= item.Item3 * .8f && height >= 30 * viewport.Item3 && x >= item.Item2 && x + width <= item.Item2 + item.Item3,
                        "full readable control stays within panel: " + item.Item1);
                Check(host.TryGetElementBounds(page, "community_page", out _, out var pageY, out var pageW, out var pageH)
                    && host.TryGetElementBounds(page, "stage", out _, out _, out _, out var stageH) && pageW >= viewport.Item1 * .98f && pageH <= stageH + 1,
                    "Community scroll viewport preserves shell width/height");
                Check(host.TryGetElementBounds(page, "community_sort_0", out var tx, out var ty, out var tw, out var th), "actual catalog sort pointer target");
                host.Input.PointerButton(0, tx + tw / 2, ty + th / 2, true); host.Input.PointerButton(0, tx + tw / 2, ty + th / 2, false); host.Update();
                Check(host.TryTakeIntent(out var sortPointer) && sortPointer.Kind == RmlUiIntentKind.CommunitySort && sortPointer.Argument == 0,
                    "actual pointer reaches sort control rather than full-parent scrollbar");
                Check(presenter.HandleAction(sortPointer), "presenter consumes actual sort pointer"); host.Update();
                host.Render(viewport.Item1, viewport.Item2);
                Press("community_tab_1", RmlUiIntentKind.CommunityTab, 1); Drain();
                host.SetField(page, "community_search", "00"); Press("community_search_apply", RmlUiIntentKind.CommunitySearch);
                Press("community_select_0", RmlUiIntentKind.CommunitySelect, 0);
                Check(controller.Snapshot().Title == "Map 00", "native selection points at real filtered service row");
                Press("community_detail_3", RmlUiIntentKind.CommunityDetailAction, 3); Drain(); var history = pages.Top;
                Check(pages.ModalCount == 1 && controller.Snapshot().RevisionTotal == 9, "actual immutable revision modal opens");
                Check(host.TryGetElementBounds(history, "community_dialog", out var mx, out var my, out var mw, out var mh)
                    && mx >= 0 && my >= 0 && mw > 0 && mh > 0 && mx + mw <= viewport.Item1 + 1 && my + mh <= viewport.Item2 + 1, "native modal fits actual viewport and density");
                Press("community_revision_next", RmlUiIntentKind.CommunityRevisionPage, 1);
                Press("community_revision_select_0", RmlUiIntentKind.CommunitySelectRevision, 0);
                Check(controller.Snapshot().SelectedPackageHash == FakeCommunityBackend.Hash(109), "actual revision DOM establishes immutable hash anchor");
                Press("community_creator_0", RmlUiIntentKind.CommunityCreatorAction, 0); Drain();
                Check(backend.LastCreator?.Revision == 9 && backend.LastCreator?.Hash == FakeCommunityBackend.Hash(109), "real creator button preserves exact revision/hash");
                Press("community_form_close", RmlUiIntentKind.CommunityCancel);
                Check(!host.IsAlive(history) && pages.Top == page, "closing native history retires document and restores page");
                Check(!presenter.HandleAction(new(RmlUiIntentKind.CommunityCreatorAction, 0, history, 100000)), "retired native modal cannot dispatch creator command");
                Press("community_detail_4", RmlUiIntentKind.CommunityDetailAction, 4);
                Check(host.FocusedElement() == "community_report_details", "report opens with relevant native field focus");
                string unicodeDescription = new string('界', 4000);
                var report = pages.Top; host.SetField(report, "community_report_details", unicodeDescription);
                Press("community_report_reason_3", RmlUiIntentKind.CommunityReportReason, 3);
                Check(host.ReadField(report, "community_report_details", 16000) == unicodeDescription, "copied snapshots preserve maximum Unicode report draft");
                Press("community_report_submit", RmlUiIntentKind.CommunityReport); Drain();
                Check(backend.LastReport?.Reason == MapCreatorCatalog.ReportReasons[3] && backend.LastReport.Details == unicodeDescription,
                    "real report submits all 4,000 Unicode characters through existing business limit");
                Press("community_detail_5", RmlUiIntentKind.CommunityDetailAction, 5); var publish = pages.Top;
                Check(host.FocusedElement() == "community_source_path", "publish focuses saved-project path field");
                string unicodePath = "/tmp/" + new string('界', 4096 - "/tmp/.json".Length) + ".json";
                host.SetField(publish, "community_source_path", unicodePath); host.SetField(publish, "community_release_notes", unicodeDescription);
                Check(!host.FocusDocument(publish, "community_upload_publish"), "upload disabled before authoritative preparation");
                Press("community_upload_validate", RmlUiIntentKind.CommunityUpload, 0); Drain();
                Check(controller.Snapshot().CanPublish && backend.PublishCalls == 0 && fields.LastPublicationPath == unicodePath
                    && host.ReadField(publish, "community_release_notes", 16000) == unicodeDescription,
                    "native validation forwards exact 4,096 Unicode path characters and preserves maximum notes without upload");
                Press("community_visibility_2", RmlUiIntentKind.CommunityVisibility, 2);
                Press("community_upload_publish", RmlUiIntentKind.CommunityUpload, 1); Drain();
                Check(backend.LastVisibility == CommunityVisibility.Draft && backend.LastPublishRequest?.ReleaseNotes == unicodeDescription && !host.IsAlive(publish),
                    "native upload preserves all 4,000 Unicode release-note characters and chosen visibility");
                Press("community_detail_1", RmlUiIntentKind.CommunityDetailAction, 1); Drain();
                Check(presenter.TryTakeHostRequest(out var request) && request.Identity.PackageHash.ToString() == controller.Snapshot().SelectedPackageHash && !presenter.TryTakeHostRequest(out _), "native install-and-host hands off exact installed identity once");
                Press("community_detail_9", RmlUiIntentKind.CommunityDetailAction, 9); var confirm = pages.Top;
                Check(backend.MapCalls == 0 && controller.Snapshot().Form == CommunityForm.Confirm, "native delete opens concrete confirmation first");
                Press("community_confirm", RmlUiIntentKind.CommunityConfirm); Drain();
                Check(backend.LastMapAction == CommunityDetailAction.DeleteMap && !host.IsAlive(confirm), "explicit native confirmation invokes map soft-delete authority");
                Press("community_import_open", RmlUiIntentKind.CommunityImport);
                string unicodeImport = "/tmp/" + new string('界', 4096 - "/tmp/.ppmap".Length) + ".ppmap";
                host.SetField(pages.Top, "community_import_path", unicodeImport); Press("community_import_submit", RmlUiIntentKind.CommunityImport); Drain();
                Check(backend.ImportCalls == 1 && pages.ModalCount == 0 && fields.LastImportPath == unicodeImport,
                    "native import forwards all 4,096 Unicode path characters to package preparation/owner commit");
                Press("community_detail_5", RmlUiIntentKind.CommunityDetailAction, 5);
                host.SetField(pages.Top, "community_source_path", "/tmp/project.json"); Press("community_upload_validate", RmlUiIntentKind.CommunityUpload, 0); Drain();
                backend.PublishError = new CommunityRevisionConflictException(new("stale_parent", backend.Projects[0].MapId, FakeCommunityBackend.Hash(1), FakeCommunityBackend.Hash(2), FakeCommunityBackend.Hash(1), 2, true, "Latest revision changed."));
                Press("community_upload_publish", RmlUiIntentKind.CommunityUpload, 1); Drain();
                Check(controller.Snapshot().Form == CommunityForm.Conflict && host.FocusedElement() == "community_conflict_review", "native conflict replaces form and focuses explicit decision");
                backend.PublishError = null; Press("community_conflict_draft", RmlUiIntentKind.CommunityConflict, 2); Drain();
                Check(backend.LastVisibility == CommunityVisibility.Draft && backend.LastPublishRequest!.AllowStaleParent, "native conflict draft command uses explicit stale-parent contract");
                Press("community_detail_4", RmlUiIntentKind.CommunityDetailAction, 4); var back = pages.Top;
                Check(pages.Back(), "shared Back closes Community form"); presenter.Tick(); host.Update();
                Check(controller.Snapshot().Form == CommunityForm.None && !host.IsAlive(back), "Back cancels controller draft without reopening stale modal");
                host.TryGetElementBounds(page, "community_select_0", out _, out var oldRowY, out _, out _);
                host.Input.PointerMoved(viewport.Item1 * .8, pageY + pageH / 2); host.Input.PointerWheel(-20); host.Update();
                Check(host.TryGetElementBounds(page, "community_select_0", out _, out var newRowY, out _, out _) && newRowY < oldRowY,
                    "actual wheel scrolls Community content within shell");
                presenter.Dispose(); Check(!host.IsAlive(page) && !presenter.TryTakeHostRequest(out _), "native page retirement cancels ownership and pending handoffs");
                host.Shutdown();
            }
            Console.WriteLine($"Community actual native integration: {checks} assertions passed.");
        }
        finally { NativeLibrary.Free(module); }
    }
}

// Records field handoffs at the existing service seam; all established fixture
// operations retain their original behavior and immutable identity contracts.
internal sealed class NativeCommunityFields(FakeCommunityBackend backend) : ICommunityBackend
{
    public string? LastAddress, LastPublicationPath, LastImportPath;
    public string DefaultAddress => backend.DefaultAddress;
    public void SetAddress(string address) { backend.SetAddress(address); LastAddress = address; }
    public MapContentIdentity? Installed(Guid mapId) => backend.Installed(mapId);
    public void ValidateInstallation(CommunityMap? package) => backend.ValidateInstallation(package);
    public Task<CommunityMapProject[]> BrowseAsync(CommunityTab tab, CancellationToken cancellation) => backend.BrowseAsync(tab, cancellation);
    public Task<CommunityMapRevision[]> RevisionsAsync(Guid map, bool authenticated, CancellationToken cancellation) => backend.RevisionsAsync(map, authenticated, cancellation);
    public Task<ICommunityInstallation> PrepareInstallAsync(CommunityMap package, Action<CommunityTransferProgress> progress, CancellationToken cancellation) => backend.PrepareInstallAsync(package, progress, cancellation);
    public Task<ICommunityInstallation> PrepareImportAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation) { LastImportPath = path; return backend.PrepareImportAsync(path, progress, cancellation); }
    public Task FavoriteAsync(Guid map, bool favorite, CancellationToken cancellation) => backend.FavoriteAsync(map, favorite, cancellation);
    public Task ReportAsync(Guid map, MapReportRequest report, CancellationToken cancellation) => backend.ReportAsync(map, report, cancellation);
    public Task ModifyAsync(Guid map, string hash, int revision, CommunityCreatorAction action, CancellationToken cancellation) => backend.ModifyAsync(map, hash, revision, action, cancellation);
    public Task ModifyMapAsync(Guid map, CommunityDetailAction action, CancellationToken cancellation) => backend.ModifyMapAsync(map, action, cancellation);
    public Task<ICommunityPublication> PreparePublicationAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation) { LastPublicationPath = path; return backend.PreparePublicationAsync(path, progress, cancellation); }
    public Task<CommunityPublishResult> PublishAsync(ICommunityPublication publication, CommunityPublishRequest request, CommunityVisibility visibility, Action<CommunityTransferProgress> progress, CancellationToken cancellation) => backend.PublishAsync(publication, request, visibility, progress, cancellation);
    public Task DiscardPublicationAsync(ICommunityPublication publication, CancellationToken cancellation) => backend.DiscardPublicationAsync(publication, cancellation);
}
#endif
