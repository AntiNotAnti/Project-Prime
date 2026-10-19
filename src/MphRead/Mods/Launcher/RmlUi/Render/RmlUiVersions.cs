#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Updates;

namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private static NativeVersionManager? _versions;
    internal static bool TryOpenNativeVersions(RenderWindow window, StartScreen owner)
    {
        if (!Requested || _disabled || !ReferenceEquals(Shell.Window, window)
            || window.HasScene || !owner.RmlUiUpdateOwnerCurrent || _versions != null || _pages is not { ModalCount: 0 }
            || _host is not { Active: true } host || _pages.Page == default
            || _controller is not { Busy: false, Joining: false }) return false;
        if (!PrepareFullSettingsNavigation()
            || (_quickSettings is { } settings && !settings.PrepareNavigation())
            || (_hunterChoice is { } hunter && !hunter.PrepareNavigation())) return false;
        var next = new NativeVersionManager(host, _pages, owner);
        try { next.Open(); _versions = next; return true; }
        catch (Exception error) when (Recoverable(error)) { next.Dispose(); Fail(error); return false; }
    }
    private static void TickNativeVersions()
    {
        if (_versions is not { } page) return;
        page.Tick();
        if (page.Closed) CloseNativeVersions();
    }
    private static bool HandleNativeVersions(in RmlUiIntent intent)
    {
        if (_versions is not { } page) return false;
        if (_host is not { Active: true } host || _pages?.Top != page.Document || host.CurrentInputDocument != page.Document)
        { return false; }
        page.Handle(intent); TickNativeVersions(); return true;
    }
    private static bool BackNativeVersions()
    {
        if (_versions is not { } page) return false;
        page.Back(); TickNativeVersions(); return true;
    }
    private static void CloseNativeVersions()
    {
        var old = _versions; _versions = null; old?.Dispose();
    }
}
#endif
