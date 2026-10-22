#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

internal sealed class NativeCommunityImportPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityImportController _controller;
    private RmlUiDocumentToken _document;
    private long _shown = -1, _revision;
    private bool _disposed, _completed;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document);
    public NativeCommunityImportPage(RmlUiHost host, RmlUiPageManager pages, INativeCommunityImportAuthority authority)
        => (_host, _pages, _controller) = (host, pages, new(authority));
    public bool Open()
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen) return false;
        _controller.Cancel(); _completed = false;
        _document = _pages.OpenModal(new("community-import", "pages/community/native-import.rml", "community_import_path"));
        _shown = -1; _host.SetField(_document, "community_import_path", ""); Refresh(); return true;
    }
    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!IsOpen || _pages.Top != _document || !_host.IsVisible(_document) || intent.Document != _document) return false;
        bool recognized = intent.Kind is RmlUiIntentKind.CommunityImport or RmlUiIntentKind.CommunityConfirm
            or RmlUiIntentKind.CommunityCancel or RmlUiIntentKind.CommunityCancelWork
            || intent.Kind == RmlUiIntentKind.CommunityDetailAction && intent.Argument == 7;
        if (!recognized || !_pages.Accept(intent)) return false;
        bool result;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.CommunityImport: result = _controller.Prepare("", true); break;
            case RmlUiIntentKind.CommunityDetailAction: result = _controller.Prepare(_host.ReadField(_document, "community_import_path", 16384), false); break;
            case RmlUiIntentKind.CommunityConfirm: result = _controller.Confirm(); break;
            case RmlUiIntentKind.CommunityCancelWork: _controller.Cancel(); result = true; break;
            default: return Back();
        }
        Refresh(); return result;
    }
    public bool Back()
    {
        _host.VerifyOwnerThread(); if (!IsOpen || _pages.Top != _document) return false;
        bool imported = _controller.Tick().Imported;
        if (!_pages.CloseModal()) return false;
        _completed |= imported; _controller.Cancel(); _document = default; return true;
    }
    public bool TakeImported()
    {
        _host.VerifyOwnerThread(); bool value = _completed; _completed = false; return value;
    }
    public void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document)) { _controller.Cancel(); _document = default; return; }
        var view = _controller.Tick(); if (view.Revision == _shown) return;
        var data = new Dictionary<string, RmlUiBindingValue>();
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        Text("community_import_status", view.Status);
        Text("community_import_detail", view.CanConfirm
            ? "MAP // " + view.Name + (view.Version.Length == 0 ? "" : "\nVERSION // " + view.Version)
                + "\nPACKAGE SHA-256 // " + view.PackageHash + "\nConfirmation installs this verified private snapshot."
            : "Choose your local .ppmap package. Preparation validates and builds it privately; confirmation adds it to your map library.");
        Bool("disabled:community_import_path", view.Busy || view.Imported);
        Bool("disabled:community_import_prepare", view.Busy || view.Imported);
        Bool("disabled:community_import_pick", view.Busy || view.Imported || !view.PickerAvailable);
        Bool("disabled:community_import_confirm", !view.CanConfirm);
        Bool("visible:community_import_cancel_work", view.Busy || view.CanConfirm);
        Text("community_import_close", view.Imported ? "VIEW INSTALLED MAPS" : "BACK TO MAPS");
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
