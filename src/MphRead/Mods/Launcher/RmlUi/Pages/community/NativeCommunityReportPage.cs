#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

internal sealed class NativeCommunityReportPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityReportController _controller;
    private RmlUiDocumentToken _document;
    private long _shown = -1, _revision;
    private bool _disposed;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document);

    public NativeCommunityReportPage(RmlUiHost host, RmlUiPageManager pages, INativeCommunityReportAuthority authority)
        => (_host, _pages, _controller) = (host, pages, new(authority));

    public bool Open(NativeCommunityEntry selection, object? catalogWitness)
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen || !_controller.Open(selection, catalogWitness)) return false;
        try { _document = _pages.OpenModal(new("community-report", "pages/community/native-report.rml", "community_report_close")); }
        catch { _controller.Cancel(); throw; }
        _shown = -1; _host.SetField(_document, "community_report_details", "");
        Refresh(); return true;
    }

    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!IsOpen || _pages.Top != _document || !_host.IsVisible(_document) || intent.Document != _document) return false;
        if (intent.Kind is not (RmlUiIntentKind.CommunityReportReason or RmlUiIntentKind.CommunityConfirm or RmlUiIntentKind.CommunityCancel)
            || !_pages.Accept(intent)) return false;
        bool handled = intent.Kind switch
        {
            RmlUiIntentKind.CommunityReportReason => _controller.Reason(intent.Argument),
            RmlUiIntentKind.CommunityConfirm => _controller.Submit(_host.ReadField(_document, "community_report_details", 16000)),
            _ => Back()
        };
        Refresh(); return handled;
    }

    public bool Back()
    {
        _host.VerifyOwnerThread();
        if (!IsOpen || _pages.Top != _document || !_pages.CloseModal()) return false;
        _controller.Cancel(); _document = default; return true;
    }

    public void Refresh()
    {
        _host.VerifyOwnerThread();
        if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document)) { _controller.Cancel(); _document = default; return; }
        var view = _controller.Tick(); if (view.Revision == _shown) return;
        if (view.Selection == null) _host.SetField(_document, "community_report_details", "");
        var data = new Dictionary<string, RmlUiBindingValue>();
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        Text("community_report_target", view.Selection is { } item
            ? item.Name + "\nVERSION " + item.Version + "\nPACKAGE SHA-256 // " + item.LatestHash
            : "REPORT SELECTION RETIRED");
        Text("community_report_status", view.Status);
        Bool("disabled:community_report_submit", !view.CanSubmit);
        Bool("disabled:community_report_details", !view.CanSubmit);
        Text("community_report_close", view.Submitted ? "BACK TO MAPS" : "CANCEL REPORT");
        for (int i = 0; i < MapCreatorCatalog.ReportReasons.Length; i++)
        {
            string id = "community_report_reason" + i;
            Text(id, MapCreatorCatalog.ReportReasons[i]);
            Bool("class:" + id + ":selected", view.Reason == i);
            Bool("disabled:" + id, !view.CanSubmit);
        }
        _pages.Present(_document, ++_revision, data); _shown = view.Revision;
    }

    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return;
        if (IsOpen && _pages.Top == _document) _pages.CloseModal();
        _controller.Dispose(); _document = default; _disposed = true;
    }
}
#endif
