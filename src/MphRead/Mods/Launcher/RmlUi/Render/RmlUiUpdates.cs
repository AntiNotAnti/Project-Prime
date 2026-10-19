#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Updates;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeUpdatePrompt? _updatePrompt;
    private static Func<bool>? _updateOwner;
    private static Action<bool>? _updateDecision;
    internal static bool NativeUpdateOpen => _updatePrompt is { Closed: false } page && _host is { Active: true } host
        && _pages?.Top == page.Document && host.CurrentInputDocument == page.Document && _updateOwner?.Invoke() == true;

    internal static bool TryOpenNativeUpdate(RenderWindow window, UpdateInfo update, string installed,
        Func<bool> owner, Action<bool> decision)
    {
        if (!Requested || _disabled || Shell.StudioWindow || !ReferenceEquals(Shell.Window, window)
            || window.HasScene || !owner() || _updatePrompt != null || _pages is not { ModalCount: 0 }
            || _host is not { Active: true } host || _launcherPages is not { Suspended: false, Page: RmlUiMenuPage.Home }
            || _controller is not { Busy: false, Joining: false } || Shell.NativeStartupPending) return false;
        var next = new NativeUpdatePrompt(host, _pages);
        try
        {
            next.Open(installed, update.Tag);
            _updatePrompt = next; _updateOwner = owner; _updateDecision = decision;
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { next.Dispose(); Fail(error); return false; }
    }
    private static bool HandleNativeUpdate(in RmlUiIntent intent)
    {
        if (_updatePrompt is not { } prompt) return false;
        if (!NativeUpdateOpen) { CloseNativeUpdate(); return true; }
        prompt.Handle(intent);
        CompleteNativeUpdate();
        return true;
    }
    private static void CompleteNativeUpdate()
    {
        if (_updatePrompt is not { Closed: true } prompt) return;
        bool accepted = prompt.Accepted;
        var decision = _updateOwner?.Invoke() == true ? _updateDecision : null;
        CloseNativeUpdate();
        decision?.Invoke(accepted);
    }
    private static void CloseNativeUpdate()
    {
        var old = _updatePrompt;
        _updatePrompt = null; _updateOwner = null; _updateDecision = null;
        old?.Dispose();
    }
}
#endif
