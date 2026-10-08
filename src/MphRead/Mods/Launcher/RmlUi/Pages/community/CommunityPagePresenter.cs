#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Authored documents bind copied Community state; explicit commands retain exact package/revision identity.</summary>
public sealed class CommunityPagePresenter : IDisposable
{
    // Authored maxlength values count characters; the bridge bounds UTF-8 bytes.
    // Four bytes per Unicode scalar preserves the full native field while the
    // controller retains its existing 4,000-character publication/report limit.
    private const int AddressBytes = 2048 * 4;
    private const int SearchBytes = 200 * 4;
    private const int PathBytes = 4096 * 4;
    private const int DescriptionBytes = 4000 * 4;
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly CommunityController _controller;
    private RmlUiDocumentToken _page, _form;
    private CommunitySnapshot? _presented;
    private long _bindingRevision;
    private bool _disposed;
    public RmlUiDocumentToken Document => _page;
    public CommunityController Controller => _controller;

    public CommunityPagePresenter(RmlUiHost host, RmlUiPageManager pages, CommunityController controller)
        => (_host, _pages, _controller) = (host, pages, controller);
    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("community", "pages/community/browser.rml", "community_search"));
        _host.SetField(_page, "community_search", "");
        _presented = null; _controller.Refresh(); Refresh();
    }

    public bool HandleAction(in RmlUiIntent intent)
    {
        if (_disposed || _page == default || _pages.Page != _page || !Owns(intent.Kind) || !_pages.Accept(intent)) return false;
        CommunitySnapshot prior = _presented ?? _controller.Snapshot();
        switch (intent.Kind)
        {
            case RmlUiIntentKind.CommunityRefresh:
                _controller.Refresh();
                break;
            case RmlUiIntentKind.CommunityTab: _controller.SetTab((CommunityTab)intent.Argument); break;
            case RmlUiIntentKind.CommunitySort: _controller.SetSort((CommunitySort)intent.Argument); break;
            case RmlUiIntentKind.CommunityLifecycle: _controller.SetLifecycle((CommunityLifecycle)intent.Argument); break;
            case RmlUiIntentKind.CommunitySearch: _controller.Search(_host.ReadField(_page, "community_search", SearchBytes)); break;
            case RmlUiIntentKind.CommunityPage: _controller.Page(intent.Argument); break;
            case RmlUiIntentKind.CommunitySelect: _controller.SelectRow(intent.Argument, prior.Revision); break;
            case RmlUiIntentKind.CommunityDetailAction:
                var action = (CommunityDetailAction)intent.Argument;
                if (_controller.Detail(action, prior.SelectedPackageHash).Accepted && action == CommunityDetailAction.CopyLink
                    && _controller.PackageLink() is string link) _host.SetClipboard(link);
                break;
            case RmlUiIntentKind.CommunityRevisionPage: _controller.RevisionPage(intent.Argument); break;
            case RmlUiIntentKind.CommunitySelectRevision: _controller.SelectRevision(intent.Argument, prior.Revision); break;
            case RmlUiIntentKind.CommunityCreatorAction: _controller.Creator((CommunityCreatorAction)intent.Argument, prior.SelectedPackageHash); break;
            case RmlUiIntentKind.CommunityConfirm: _controller.ConfirmAction(); break;
            case RmlUiIntentKind.CommunityCancel:
                if (_controller.Snapshot().Busy) _controller.CancelWork();
                _controller.CancelForm(); break;
            case RmlUiIntentKind.CommunityCancelWork: _controller.CancelWork(); break;
            case RmlUiIntentKind.CommunityUpload:
                if (_form != default)
                {
                    string notes = _host.ReadField(_form, "community_release_notes", DescriptionBytes);
                    if (intent.Argument == 0) _controller.PreparePublish(_host.ReadField(_form, "community_source_path", PathBytes), notes);
                    else _controller.Publish(notes);
                }
                break;
            case RmlUiIntentKind.CommunityVisibility: _controller.SetVisibility((CommunityVisibility)intent.Argument); break;
            case RmlUiIntentKind.CommunityReport: if (_form != default) _controller.SubmitReport(_host.ReadField(_form, "community_report_details", DescriptionBytes)); break;
            case RmlUiIntentKind.CommunityReportReason: _controller.SetReportReason(intent.Argument); break;
            case RmlUiIntentKind.CommunityConflict: _controller.Conflict((CommunityConflictAction)intent.Argument); break;
            case RmlUiIntentKind.CommunityImport:
                if (_form == default) _controller.OpenImport();
                else _controller.Import(_host.ReadField(_form, "community_import_path", PathBytes));
                break;
        }
        Refresh(); return true;
    }

    public void Tick() { if (!_disposed) { _controller.Tick(); Refresh(); } }
    public void Refresh()
    {
        if (_disposed || _page == default || !_host.IsAlive(_page) || _pages.Page != _page) return;
        if (_form != default && !_host.IsAlive(_form))
        {
            _form = default;
            if (_controller.Snapshot().Busy) _controller.CancelWork();
            _controller.CancelForm();
        }
        CommunitySnapshot s = _controller.Snapshot();
        bool opened = false;
        if (s.Form != CommunityForm.None && _form == default)
        {
            _form = _pages.OpenModal(new("community-flow", "pages/community/flow.rml"));
            _host.SetField(_form, "community_source_path", "");
            _host.SetField(_form, "community_release_notes", "");
            _host.SetField(_form, "community_report_details", "");
            _host.SetField(_form, "community_import_path", "");
            opened = true;
        }
        else if (s.Form == CommunityForm.None && _form != default)
        {
            if (_pages.CloseModal()) _form = default;
        }
        if (!opened && _presented?.Revision == s.Revision) return;
        long revision = ++_bindingRevision;
        var b = new Dictionary<string, RmlUiBindingValue>();
        Text(b, "community_status", s.State + " // " + s.Status);
        Text(b, "community_error", s.Error);
        Text(b, "community_count", s.Total == 0 ? "No maps match this view." : $"{s.First}–{Math.Min(s.First + CommunityController.PageSize - 1, s.Total)} of {s.Total} maps");
        Text(b, "community_title", s.Title); Text(b, "community_detail", s.Detail);
        Text(b, "community_detail_2", s.Favorited ? "REMOVE FAVORITE" : "ADD FAVORITE");
        Text(b, "community_transfer", Transfer(s.Progress));
        Bool(b, "visible:community_cancel_work", s.Busy);
        Bool(b, "disabled:community_previous", !s.PreviousPage || s.Busy);
        Bool(b, "disabled:community_next", !s.NextPage || s.Busy);
        Bool(b, "disabled:community_refresh", s.Busy);
        Bool(b, "visible:community_lifecycle_filters", s.Tab == CommunityTab.MyMaps);
        for (int i = 0; i < 3; i++)
        {
            Bool(b, "class:community_tab_" + i + ":selected", (int)s.Tab == i);
            Bool(b, "disabled:community_tab_" + i, s.Busy);
            Bool(b, "class:community_sort_" + i + ":selected", (int)s.Sort == i);
        }
        for (int i = 0; i < 4; i++) Bool(b, "class:community_lifecycle_" + i + ":selected", (int)s.Lifecycle == i);
        for (int i = 0; i < 8; i++)
        {
            bool exists = i < s.Maps.Length;
            Bool(b, "visible:community_row_" + i, exists);
            Bool(b, "disabled:community_select_" + i, !exists || s.Busy);
            Bool(b, "class:community_select_" + i + ":selected", exists && s.Maps[i].Selected);
            Text(b, "community_name_" + i, exists ? s.Maps[i].Name : "");
            Text(b, "community_meta_" + i, exists ? s.Maps[i].Detail : "");
        }
        for (int i = 0; i < 10; i++)
        {
            bool creator = i is 7 or 8 or 9;
            Bool(b, "visible:community_detail_" + i, !creator || s.CanManageLifecycle);
            Bool(b, "disabled:community_detail_" + i, s.Busy || i != 5 && !s.HasSelection
                || creator && !s.CanManageLifecycle || i == 7 && (s.MapArchived || s.MapDeleted)
                || i == 8 && !s.MapArchived && !s.MapDeleted);
        }
        _pages.Present(_page, revision, b);
        if (_form != default && _host.IsAlive(_form))
        {
            PresentForm(s, revision);
            if (opened || _presented?.Form != s.Form)
                _host.FocusDocument(_form, s.Form switch
                {
                    CommunityForm.Report => "community_report_details", CommunityForm.Publish => "community_source_path",
                    CommunityForm.Import => "community_import_path", CommunityForm.Confirm => "community_confirm",
                    CommunityForm.Conflict => "community_conflict_review", _ => "community_form_close"
                });
        }
        _presented = s;
    }

    public bool TryTakeHostRequest(out CommunityHostRequest request)
    {
        if (_disposed || _page == default || !_host.IsAlive(_page) || _pages.Page != _page) { request = default; return false; }
        return _controller.TryTakeHostRequest(out request);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _controller.CancelWork(); _controller.CancelForm();
        if (_pages.Page == _page && !_pages.ClosePage()) throw new InvalidOperationException("Community documents could not be closed.");
        _page = _form = default; _disposed = true;
    }

    private void PresentForm(CommunitySnapshot s, long revision)
    {
        var b = new Dictionary<string, RmlUiBindingValue>();
        Text(b, "community_form_title", s.Title);
        Text(b, "community_form_status", s.Status); Text(b, "community_form_error", s.Error);
        Text(b, "community_form_transfer", Transfer(s.Progress));
        Text(b, "community_confirmation", s.Confirmation);
        Text(b, "community_publish_source", s.PublishSource);
        Text(b, "community_visibility_label", "Visibility: " + s.Visibility);
        Text(b, "community_report_reason_label", "Reason: " + MapCreatorCatalog.ReportReasons[s.ReportReason]);
        Text(b, "community_conflict_message", s.Conflict);
        Text(b, "community_revision_detail", s.Detail);
        Text(b, "community_revision_count", s.RevisionTotal == 0 ? "No revisions returned." : $"{s.RevisionFirst}–{Math.Min(s.RevisionFirst + 7, s.RevisionTotal)} of {s.RevisionTotal} immutable revisions");
        foreach (CommunityForm form in Enum.GetValues<CommunityForm>())
            if (form != CommunityForm.None) Bool(b, "visible:community_form_" + form.ToString().ToLowerInvariant(), s.Form == form);
        Bool(b, "disabled:community_upload_validate", s.Busy);
        Bool(b, "disabled:community_upload_publish", !s.CanPublish || s.Busy);
        Bool(b, "disabled:community_import_submit", s.Busy);
        Bool(b, "disabled:community_report_submit", s.Busy);
        Bool(b, "disabled:community_confirm", s.Busy);
        Bool(b, "disabled:community_revision_previous", !s.PreviousRevisionPage || s.Busy);
        Bool(b, "disabled:community_revision_next", !s.NextRevisionPage || s.Busy);
        for (int i = 0; i < 8; i++)
        {
            bool exists = i < s.Revisions.Length;
            Bool(b, "visible:community_revision_row_" + i, exists);
            Bool(b, "disabled:community_revision_select_" + i, !exists || s.Busy);
            Text(b, "community_revision_name_" + i, exists ? s.Revisions[i].Name : "");
            Text(b, "community_revision_meta_" + i, exists ? s.Revisions[i].Detail : "");
        }
        Bool(b, "visible:community_creator_actions", s.CanManage);
        for (int i = 0; i < 5; i++) Bool(b, "disabled:community_creator_" + i,
            s.Busy || !s.HasRevision || !s.CanManage || i is 3 or 4 && !s.CanManageLifecycle
            || i == 3 && s.RevisionDeleted || i == 4 && !s.RevisionDeleted || i < 3 && (s.MapDeleted || s.RevisionDeleted));
        Bool(b, "visible:community_conflict_anyway", s.ConflictCode == "stale_parent");
        Bool(b, "visible:community_conflict_draft", s.ConflictCode == "stale_parent");
        Bool(b, "visible:community_conflict_revision", s.ConflictCode == "map_already_exists");
        _pages.Present(_form, revision, b);
    }
    private static string Transfer(CommunityTransferProgress p) => p.Total > 0 ? $"{p.Stage} // {p.Completed / 1048576d:0.0}/{p.Total / 1048576d:0.0} MiB // {Math.Clamp(p.Completed * 100d / p.Total, 0, 100):0}%" : p.Stage ?? "";
    private static void Text(Dictionary<string, RmlUiBindingValue> b, string id, string value) => b.Add(id, RmlUiBindingValue.FromText(value));
    private static void Bool(Dictionary<string, RmlUiBindingValue> b, string id, bool value) => b.Add(id, RmlUiBindingValue.FromBoolean(value));
    private static bool Owns(RmlUiIntentKind kind) => kind >= RmlUiIntentKind.CommunityRefresh && kind <= RmlUiIntentKind.CommunityImport;
}
#endif
