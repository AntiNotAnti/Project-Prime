#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Setup;
namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeGameFilesPage? _gameFiles;
    private static Func<bool>? _gameFilesOwner;
    private static Action? _gameFilesCompleted;
    private static bool _gameFilesRetiring;
    private static Action? _gameFilesFallback;
    internal static bool NativeGameFilesOpen => !_disabled && _gameFiles?.HasLivePage == true;

    // Called only by the current desktop render owner while its GL context is
    // current (including Shell.TickUi's initial-prompt frame). Returning false
    // leaves the existing SetupScreen fallback untouched.
    internal static bool TryOpenNativeGameFiles(RenderWindow window, Func<bool> ownerCurrent, Action completed, Action? fallback = null)
    {
        if (!Requested || _disabled || window.HasScene || !ownerCurrent()
            || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0) return false;
        if (_gameFiles != null)
        {
            if (!_gameFiles.HasLivePage) CloseNativeGameFiles();
            else return !_gameFilesRetiring && _gameFilesOwner?.Invoke() == true;
        }
        if (!TryStart(window) || _host is not { Active: true } host || _pages == null
            || _pages.ModalCount != 0 || _pages.Top != _pages.Page) return false;
        // The new page replaces route presenters, not their retained services.
        // Retire stale route fields before the manager replaces their documents.
        CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense(); CloseNativeCommunity();
        CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
        CloseNativeCampaignEnding(); CloseNativeCampaignShip();
        if (!ownerCurrent() || _pages.ModalCount != 0 || _pages.Top != _pages.Page) return false;
        NativeGameFilesPage? next = null;
        try
        {
            next = new(host, _pages, new NativeGameFilesService(), ownerCurrent);
            next.Open();
            if (!ownerCurrent() || !host.IsAlive(next.Document) || !host.IsVisible(next.Document)
                || host.CurrentInputDocument != next.Document)
            { next.Dispose(); return false; }
            _launcherPages?.Suspend();
            _gameFiles = next; _gameFilesOwner = ownerCurrent; _gameFilesCompleted = completed; _gameFilesFallback = fallback;
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        { next?.Dispose(); Mods.DebugLog.Exception("native-game-files", error); return false; }
    }
    private static bool HandleNativeGameFiles(in RmlUiIntent intent)
    {
        if (_gameFiles is not { } page || !page.OwnsForeground) return false;
        page.Handle(intent); RefreshNativeGameFiles(); return true;
    }
    private static bool BackNativeGameFiles()
    {
        if (_gameFiles is not { } page || !page.OwnsForeground) return false;
        page.Back(); RefreshNativeGameFiles(); return true;
    }
    private static void RefreshNativeGameFiles()
    {
        if (_gameFiles is not { } page) return;
        if (_gameFilesRetiring || _gameFilesOwner?.Invoke() != true) { CloseNativeGameFiles(); return; }
        page.Refresh();
        if (page.TryTakeInstalled())
        {
            var completed = _gameFilesCompleted;
            // Close native ownership before refreshing the retained launcher.
            // The callback independently rechecks the exact captured owner.
            CloseNativeGameFiles(); completed?.Invoke(); _controller?.GameFilesSetupClosed(installed: true);
        }
        else if (page.Closed) { CloseNativeGameFiles(); _controller?.GameFilesSetupClosed(installed: false); }
    }
    private static Action? TakeNativeGameFilesFallback()
    {
        var fallback = _gameFiles != null && _gameFilesOwner?.Invoke() == true ? _gameFilesFallback : null;
        _gameFilesFallback = null; return fallback;
    }
    private static void CloseNativeGameFiles(bool teardown = false)
    {
        var old = _gameFiles; if (old == null) return;
        if (!teardown && !old.OwnsForeground && _host is { Active: true } && old.HasLivePage)
        {
            _gameFilesRetiring = true; _gameFilesOwner = null; _gameFilesCompleted = null; _gameFilesFallback = null;
            old.RetireWork(); return; // never pop or Resume through a foreign modal
        }
        _gameFiles = null; _gameFilesOwner = null; _gameFilesCompleted = null; _gameFilesFallback = null; _gameFilesRetiring = false;
        // A different page/modal may already own this manager. Never Resume
        // over that replacement merely because our old document retired.
        bool resume = !teardown && (old.OwnsForeground
            || _pages != null && _pages.Page == default && _pages.Top == default);
        old.Dispose(); if (resume) _launcherPages?.Resume();
    }
}
#endif
