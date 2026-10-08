using System;
using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

/// <summary>Borrowed page manager; document retirement cancels the picker before a late selection can launch an application.</summary>
public sealed class StudioPagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly StudioController _controller;
    private CancellationTokenRegistration _retirement;
    private RmlUiDocumentToken _document;
    private ulong _presentedVersion, _pathEpoch;
    private long _bindingVersion;
    private bool _disposed;

    public RmlUiDocumentToken Document => _document;
    public StudioController Controller => _controller;
    public event Action? Closed;

    public StudioPagePresenter(RmlUiHost host, RmlUiPageManager pages, StudioController controller)
    {
        (_host, _pages, _controller) = (host ?? throw new ArgumentNullException(nameof(host)),
            pages ?? throw new ArgumentNullException(nameof(pages)), controller ?? throw new ArgumentNullException(nameof(controller)));
        _host.VerifyOwnerThread();
        _ = _controller.Snapshot; // Both owners must match the window/render thread.
    }

    public void Open(string? documentPath = null, bool recover = false)
    {
        _host.VerifyOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_document == default)
        {
            _document = _pages.OpenPage(new("studio", "pages/studio/entry.rml", "studio_launch"));
            _retirement = _pages.Lifetime(_document).Register(OnRetired);
        }
        if (documentPath != null) _controller.OpenPath(documentPath, recover);
        else if (recover) _controller.Recover();
        Refresh();
    }

    /// <summary>Handles only foreground Studio actions. Navigation remains with the shared router.</summary>
    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (_disposed || intent.Document != _document || intent.Kind is not (RmlUiIntentKind.StudioAction or RmlUiIntentKind.OpenStudio)) return false;
        if (!_pages.Accept(intent)) return true;
        int action = intent.Kind == RmlUiIntentKind.OpenStudio ? 0 : intent.Argument;
        switch (action)
        {
            case 0: _controller.Launch(); break;
            case 1: _controller.PickMap(); break;
            case 2: _controller.OpenPath(_host.ReadField(_document, "studio_path", maximumBytes: 131072)); break;
            case 3: _controller.Recover(); break;
            case 4: Back(); break;
            default: return false;
        }
        Refresh();
        return true;
    }

    public bool Back()
    {
        _host.VerifyOwnerThread();
        if (_disposed) return false;
        if (_controller.Snapshot.Busy) _controller.Cancel();
        else Closed?.Invoke();
        Refresh();
        return true;
    }

    public void Refresh()
    {
        _host.VerifyOwnerThread();
        if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document) || _pages.Page != _document) { Dispose(); return; }
        _controller.Pump();
        StudioViewSnapshot snapshot = _controller.Snapshot;
        if (snapshot.Version == _presentedVersion) return;
        if (snapshot.PathEpoch != _pathEpoch)
        {
            _host.SetField(_document, "studio_path", _controller.PathDraft);
            _pathEpoch = snapshot.PathEpoch;
        }
        var bindings = new Dictionary<string, RmlUiBindingValue>
        {
            ["studio_availability"] = RmlUiBindingValue.FromText(snapshot.Availability.Message),
            ["studio_status"] = RmlUiBindingValue.FromText(snapshot.Status),
            ["studio_error"] = RmlUiBindingValue.FromText(snapshot.Error),
            ["studio_document"] = RmlUiBindingValue.FromText(snapshot.DocumentName.Length == 0 ? ""
                : snapshot.DocumentKind + " // " + snapshot.DocumentName),
            ["studio_cancel_label"] = RmlUiBindingValue.FromText(snapshot.Busy ? "CANCEL SELECTION" : "BACK"),
            ["visible:studio_error"] = RmlUiBindingValue.FromBoolean(snapshot.Error.Length > 0),
            ["visible:studio_document"] = RmlUiBindingValue.FromBoolean(snapshot.DocumentName.Length > 0),
            ["visible:studio_picker_help"] = RmlUiBindingValue.FromBoolean(!snapshot.Availability.PickerAvailable),
            ["disabled:studio_launch"] = RmlUiBindingValue.FromBoolean(!snapshot.CanLaunch),
            ["disabled:studio_pick_map"] = RmlUiBindingValue.FromBoolean(!snapshot.CanPick),
            ["disabled:studio_open_path"] = RmlUiBindingValue.FromBoolean(!snapshot.CanLaunch),
            ["disabled:studio_recover"] = RmlUiBindingValue.FromBoolean(!snapshot.CanLaunch),
            ["disabled:studio_path"] = RmlUiBindingValue.FromBoolean(snapshot.Busy)
        };
        _pages.Present(_document, ++_bindingVersion, bindings);
        _presentedVersion = snapshot.Version;
    }

    private void OnRetired()
    {
        // Host cancellation is published on its owner after native retirement.
        // Do not reenter the manager from this callback.
        _disposed = true;
        _controller.Dispose();
        Closed = null;
    }

    public void Dispose()
    {
        _host.VerifyOwnerThread();
        _retirement.Dispose();
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        if (_pages.Page == _document && !_pages.ClosePage())
            throw new InvalidOperationException("The Studio entry page could not close.");
        _document = default;
        Closed = null;
    }
}
