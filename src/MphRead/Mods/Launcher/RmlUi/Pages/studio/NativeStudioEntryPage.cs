#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

/// <summary>Native Studio selection, sharing only the existing launcher route.</summary>
internal sealed class NativeStudioEntryPage : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly NativeStudioEntryController _controller;
    private RmlUiDocumentToken _page;
    private long _presented;
    private bool _disposed;

    public bool Closed => _controller.Current().Closed;

    public NativeStudioEntryPage(RmlUiPageManager pages, INativeStudioLauncher launcher)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeStudioEntryController(launcher);
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("studio-entry",
            "pages/studio/native-entry.rml", "native_studio_launch"));
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || intent.Kind != RmlUiIntentKind.StudioAction
            || intent.Document != _page || _pages.Top != _page || !_pages.Accept(intent))
            return false;
        switch (intent.Argument)
        {
            case 0:
                _controller.Launch();
                break;
            case 4:
                _controller.Back();
                break;
            default:
                return false;
        }
        Refresh();
        return true;
    }

    private void Refresh()
    {
        if (_disposed || _page == default) return;
        var current = _controller.Current();
        if (current.Revision <= _presented) return;
        _pages.Present(_page, current.Revision,
            new Dictionary<string, RmlUiBindingValue>
            {
                ["native_studio_status"] = RmlUiBindingValue.FromText(current.Status)
            });
        _presented = current.Revision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
        _controller.Dispose();
    }
}

internal sealed class CurrentStudioLauncher : INativeStudioLauncher
{
    public bool LaunchMapStudio() => Shell.RmlUiNavigateStudio();
}
#endif
