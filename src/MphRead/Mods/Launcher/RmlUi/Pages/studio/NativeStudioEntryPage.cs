#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

/// <summary>Prime Studio destination hub. Map authoring remains unavailable and acquires no project owner.</summary>
internal sealed class NativeStudioEntryPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly bool _isStudio;
    private bool? _workspaceLaunch;
    private CancellationTokenRegistration _retirement;
    private RmlUiDocumentToken _page;
    private bool _disposed, _closed, _dirty = true;
    private long _revision;
    private string _status = "MAP STUDIO // COMING SOON";
    public bool ShowingChooser => true;
    public bool Closed => _closed;
    internal const string PublishButtonId = "native_studio_publish";
    internal const string EditorButtonId = "native_studio_edit_current";
    internal RmlUiDocumentToken Document => _page;
    internal bool TryTakeProjectChangeRequest(out INativeStudioProjectChanges? changes) { changes = null; return false; }
    internal bool TryTakeWorkspaceLaunch(out bool replay)
    {
        replay = _workspaceLaunch == true;
        bool requested = _workspaceLaunch.HasValue;
        _workspaceLaunch = null;
        return requested && !_isStudio && !_disposed && !_closed && _pages.Top == _page
            && _host.CurrentInputDocument == _page && _host.IsAlive(_page);
    }
    internal bool TryTakeEditorRequest() => false;
    internal bool TryTakePublishRequest() => false;
    internal void ShowStatus(string message)
    { if (!_disposed) { _status = message; _dirty = true; Refresh(); } }
    public NativeStudioEntryPage(RmlUiHost host, RmlUiPageManager pages, INativeStudioLauncher launcher, bool isStudio = false)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        ArgumentNullException.ThrowIfNull(launcher);
        _isStudio = isStudio;
        // Deliberately do not construct a library controller, read a catalog, or retain a map-owner capability.
    }
    public void Open(bool showChooser = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("studio-entry", "pages/studio/native-entry.rml", _isStudio ? "native_studio_replay_open" : "native_studio_theatre"));
        _retirement = _pages.Lifetime(_page).Register(() => { _disposed = _closed = true; _workspaceLaunch = null; });
        _dirty = true; Refresh();
    }
    public bool Handle(in RmlUiIntent intent)
    {
        bool publish = intent.Kind == RmlUiIntentKind.CommunityDetailAction && intent.Argument == 5;
        if (_disposed || _closed || (!publish && intent.Kind != RmlUiIntentKind.StudioAction)
            || intent.Document != _page || _pages.Top != _page || _host.CurrentInputDocument != _page || !_pages.Accept(intent)) return false;
        if (!publish && intent.Argument == 4) { _workspaceLaunch = null; _closed = true; return true; }
        if (!_isStudio && !publish && intent.Argument is 0 or 1)
        {
            if (_workspaceLaunch.HasValue) return false;
            _workspaceLaunch = intent.Argument == 1;
            return true;
        }
        _status = "MAP STUDIO // COMING SOON"; _dirty = true; Refresh();
        return false;
    }
    public void Refresh()
    {
        if (_disposed || !_dirty || _page == default || !_host.IsAlive(_page) || _pages.Top != _page || _host.CurrentInputDocument != _page) return;
        _pages.Present(_page, ++_revision, new Dictionary<string, RmlUiBindingValue>
        {
            ["native_studio_status"] = RmlUiBindingValue.FromText(_status),
            ["disabled:native_studio_map"] = RmlUiBindingValue.FromBoolean(_isStudio),
            ["native_studio_map"] = RmlUiBindingValue.FromText(_isStudio ? "COMING SOON" : "OPEN MAP STUDIO"),
            ["visible:native_studio_theatre"] = RmlUiBindingValue.FromBoolean(!_isStudio),
            ["visible:native_studio_chooser_back"] = RmlUiBindingValue.FromBoolean(!_isStudio),
            ["visible:native_studio_replay_open"] = RmlUiBindingValue.FromBoolean(_isStudio)
        });
        _dirty = false;
    }
    public void Dispose()
    {
        _retirement.Dispose();
        if (_disposed) return;
        _disposed = _closed = true; _workspaceLaunch = null;
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
    }
}
#endif
