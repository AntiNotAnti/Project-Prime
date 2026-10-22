#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

internal sealed class NativeCommunityTransfersPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityTransfersController _controller;
    private RmlUiDocumentToken _document;
    private long _shown = -1, _revision;
    private bool _disposed;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document);

    internal NativeCommunityTransfersPage(RmlUiHost host, RmlUiPageManager pages,
        INativeCommunityTransfersAuthority authority)
        => (_host, _pages, _controller) = (host, pages, new(authority));

    public bool Open()
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen) return false;
        _document = _pages.OpenModal(new("community-transfers", "pages/community/native-transfers.rml", "community_transfers_close"));
        _shown = -1; _controller.Open(); Refresh(); return true;
    }

    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!IsOpen || _pages.Top != _document || intent.Document != _document) return false;
        bool recognized = intent.Kind is RmlUiIntentKind.CommunitySelect or RmlUiIntentKind.CommunityPage
            or RmlUiIntentKind.CommunityRefresh or RmlUiIntentKind.CommunityTransferAction
            or RmlUiIntentKind.CommunityConfirm or RmlUiIntentKind.CommunityCancel;
        if (!recognized || !_pages.Accept(intent)) return false;
        bool accepted = intent.Kind switch
        {
            RmlUiIntentKind.CommunitySelect => _controller.Select(intent.Argument),
            RmlUiIntentKind.CommunityPage => _controller.MovePage(intent.Argument),
            RmlUiIntentKind.CommunityRefresh => _controller.Refresh(),
            RmlUiIntentKind.CommunityTransferAction => _controller.Control((NativeCommunityTransferAction)intent.Argument),
            RmlUiIntentKind.CommunityConfirm => _controller.Confirm(),
            RmlUiIntentKind.CommunityCancel => Back(),
            _ => false
        };
        Refresh(); return accepted;
    }

    public bool Back()
    {
        _host.VerifyOwnerThread(); if (!IsOpen || _pages.Top != _document) return false;
        if (_controller.Dismiss()) { Refresh(); return true; }
        if (!_pages.CloseModal()) return false;
        _controller.Close(); _document = default; return true;
    }

    public void Refresh()
    {
        _host.VerifyOwnerThread(); if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document)) { _controller.Close(); _document = default; return; }
        var state = _controller.Tick(); if (_shown == state.Revision) return;
        var data = new Dictionary<string, RmlUiBindingValue>();
        void Text(string id, string value) => data[id] = RmlUiBindingValue.FromText(value);
        void Bool(string id, bool value) => data[id] = RmlUiBindingValue.FromBoolean(value);
        Text("community_transfers_status", state.Status);
        Text("community_transfers_page", state.Total == 0 ? "0 / 0" : $"{state.Page + 1} / {state.Pages}");
        Text("community_transfers_detail", state.Selected is { } row
            ? row.Title + "\n" + row.Kind.ToString().ToUpperInvariant() + " // " + row.State.ToString().ToUpperInvariant()
                + $"\n{row.Received:N0} / {row.Total:N0} BYTES\nPACKAGE SHA-256 // " + row.PackageHash
                + (row.State == CommunityTransferState.Completed && row.Kind == CommunityTransferKind.Download
                    ? "\nDownload completed. Choose this map in Discover to install it; installation rechecks its exact package."
                    : row.State == CommunityTransferState.NeedsReview ? "\nReview the current revision in My Uploads before resuming publication."
                    : row.State == CommunityTransferState.NeedsAuthorization ? "\nReopen Publish from the owning account to review and authorize these retained upload bytes."
                    : "")
            : "Downloads and creator uploads appear here and survive a restart. Select a transfer to see its progress.");
        Bool("visible:community_transfers_empty", state.Total == 0);
        Bool("visible:community_transfers_confirmation", state.Confirmation.HasValue);
        Text("community_transfers_confirmation", state.Confirmation.HasValue ? state.Status : "");
        Bool("visible:community_transfers_confirm", state.Confirmation.HasValue);
        Text("community_transfers_confirm", state.Confirmation == NativeCommunityTransferAction.Cancel ? "CONFIRM CANCEL TRANSFER" : "CONFIRM REMOVE FROM HISTORY");
        Text("community_transfers_close", state.Confirmation.HasValue ? "KEEP TRANSFER" : "BACK TO COMMUNITY");
        Bool("disabled:community_transfers_confirm", state.Busy);
        Bool("disabled:community_transfers_refresh", state.Busy || state.Confirmation.HasValue);
        Bool("disabled:community_transfers_previous", state.Busy || state.Confirmation.HasValue || state.Page == 0);
        Bool("disabled:community_transfers_next", state.Busy || state.Confirmation.HasValue || state.Page + 1 >= state.Pages);
        for (int action = 0; action < 4; action++)
        {
            bool allowed = state.Selected != null && NativeCommunityTransfersController.Allows(state.Selected, (NativeCommunityTransferAction)action);
            Bool("visible:community_transfers_action" + action, allowed);
            Bool("disabled:community_transfers_action" + action, state.Busy || state.Confirmation.HasValue || !allowed);
        }
        for (int i = 0; i < 8; i++)
        {
            var item = i < state.Rows.Count ? state.Rows[i] : null;
            Bool("visible:community_transfers_row" + i, item != null);
            Bool("disabled:community_transfers_row" + i, state.Busy || state.Confirmation.HasValue);
            Bool("class:community_transfers_row" + i + ":selected", item != null && item.Id == state.Selected?.Id);
            Text("community_transfers_name" + i, item?.Title ?? "");
            Text("community_transfers_meta" + i, item == null ? "" : $"{item.Kind} // {item.State} // {item.Received:N0} / {item.Total:N0} bytes");
        }
        _pages.Present(_document, ++_revision, data); _shown = state.Revision;
    }

    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return;
        _controller.Dispose(); if (_document != default && _pages.Top == _document) _pages.CloseModal();
        _document = default; _disposed = true;
    }
}
#endif
