#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Updates;

/// <summary>Presentation and one foreground decision only; the existing update owner performs installation.</summary>
internal sealed class NativeUpdatePrompt : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private bool _disposed;
    internal RmlUiDocumentToken Document { get; private set; }
    internal bool Closed { get; private set; }
    internal bool Accepted { get; private set; }
    internal NativeUpdatePrompt(RmlUiHost host, RmlUiPageManager pages)
        => (_host, _pages) = (host, pages);
    internal void Open(string installed, string available)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Document = _pages.OpenModal(new("update-available", "pages/updates/available.rml", "update_later"));
        _host.SetText(Document, "update_installed", installed);
        _host.SetText(Document, "update_available", available);
        _host.SetText(Document, "update_confirm", OperatingSystem.IsMacOS() ? "OPEN RELEASE PAGE" : "UPDATE NOW");
    }
    internal bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || Closed || intent.Document != Document || _pages.Top != Document
            || intent.Kind is not (RmlUiIntentKind.SettingsApply or RmlUiIntentKind.SettingsClose)
            || !_pages.Accept(intent)) return false;
        Accepted = intent.Kind == RmlUiIntentKind.SettingsApply;
        Closed = true;
        return true;
    }
    internal void Back() { if (!_disposed && !Closed) { Accepted = false; Closed = true; } }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = Closed = true;
        if (_pages.Top == Document) _pages.CloseModal();
    }
}
#endif
