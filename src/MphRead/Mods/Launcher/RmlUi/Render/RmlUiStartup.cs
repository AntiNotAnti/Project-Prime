#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Startup;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeStartupPage? _startupNative;
    private static Func<bool>? _startupOwner;
    private static Action? _startupCompleted;
    internal static bool NativeStartupOpen => !_disabled && _startupNative is { Closed: false } page
        && _host is { Active: true } host && _pages?.Top == page.Document
        && host.CurrentInputDocument == page.Document && _startupOwner?.Invoke() == true;

    internal static bool TryOpenNativeStartup(RenderWindow window, Func<bool> ownerCurrent, Action completed)
    {
        if (!Requested || _disabled || !ReferenceEquals(Shell.Window, window) || window.HasScene
            || !ownerCurrent() || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0) return false;
        if (_startupNative != null) return NativeStartupOpen;
        if (!TryStart(window) || _host is not { Active: true } host || _pages == null) return false;
        NativeStartupPage? next = null;
        try
        {
            if (_pages.ModalCount != 0 || !ownerCurrent()) return false;
            CloseNativeStudio(); CloseNativeNews(); CloseNativeQuickSettings();
            _launcherPages?.Suspend();
            next = new(host, _pages, Shell.StudioWindow);
            next.Open();
            if (!ownerCurrent() || host.CurrentInputDocument != next.Document)
            { next.Dispose(); return false; }
            _startupNative = next; _startupOwner = ownerCurrent; _startupCompleted = completed;
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        { next?.Dispose(); Fail(error); return false; }
    }

    private static bool HandleNativeStartup(in RmlUiIntent intent)
    {
        if (_startupNative is not { } page) return false;
        if (!NativeStartupOpen) return true;
        page.Handle(intent);
        if (page.Closed)
        {
            // Retire the exact document before activating the retained front
            // owner. The callback independently verifies that owner's lifetime.
            var completed = _startupCompleted;
            CloseNativeStartup();
            completed?.Invoke();
        }
        return true;
    }

    private static void CloseNativeStartup(bool teardown = false)
    {
        var old = _startupNative;
        _startupNative = null; _startupOwner = null; _startupCompleted = null;
        if (old == null) return;
        bool resume = !teardown && _pages?.Page == old.Document;
        old.Dispose();
        if (resume) _launcherPages?.Resume();
    }
}
#endif
