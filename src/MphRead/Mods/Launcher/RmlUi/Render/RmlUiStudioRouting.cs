#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeCommunityPublishPage? _studioPublish;
    private static NativeStudioEditorPage? _studioEditor;
    private static NativeStudioProjectChangePage? _studioChange;
    private static int _studioLayoutWidth, _studioLayoutHeight, _studioLayoutClientWidth, _studioLayoutClientHeight;

    private static void PublishNativeStudioLayoutBasis(RenderWindow window, int width, int height)
    {
        _studioLayoutWidth = width; _studioLayoutHeight = height;
        _studioLayoutClientWidth = window.ClientSize.X; _studioLayoutClientHeight = window.ClientSize.Y;
    }

    private static bool HandleNativeStudioRoute(in RmlUiIntent intent)
    {
        if (_studioChange is { } change)
        {
            change.Handle(intent);
            ApplyNativeStudioProjectChange();
            return true;
        }
        if (_studioPublish is { } publish)
        {
            publish.Handle(intent);
            if (!publish.IsOpen) CloseNativeStudioPublish();
            return true;
        }
        if (_studioEditor is { } editor)
        {
            editor.Handle(intent);
            if (editor.TryTakePublishRequest()) OpenNativeStudioPublish(NativeStudioEditorPage.PublishButtonId, editor.ShowStatus);
            ApplyNativeStudioEditorReturn();
            return true;
        }
        if (_studio is not { } studio || !(intent.Kind == RmlUiIntentKind.StudioAction
            || intent.Kind == RmlUiIntentKind.CommunityDetailAction && intent.Argument == 5)) return false;
        studio.Handle(intent);
        if (studio.TryTakeWorkspaceLaunch(out bool replay)) LaunchPrimeStudioWorkspace(studio, replay);
        else if (studio.TryTakeProjectChangeRequest(out var changes) && changes != null)
            OpenNativeStudioProjectChange(changes, studio);
        else if (studio.TryTakePublishRequest()) OpenNativeStudioPublish(NativeStudioEntryPage.PublishButtonId, studio.ShowStatus);
        else if (studio.TryTakeEditorRequest()) OpenNativeStudioEditor(studio);
        if (ReferenceEquals(_studio, studio) && studio.Closed)
        {
            CloseNativeStudio();
            if (!Shell.StudioWindow) _controller?.Dispatch(RmlUiIntentKind.Navigate,
                (int)RmlUiRouteArgument.Home);
        }
        return true;
    }

    private static void OpenNativeStudioPublish(string returnFocus, Action<string> showStatus)
    {
        if (_host is not { } host || _pages == null || _studioPublish != null) return;
        if (!Shell.TryCaptureCommunityPublishIntent(out var captured, out string error) || captured == null)
        { showStatus(error); return; }
        var next = new NativeCommunityPublishPage(host, _pages);
        try
        {
            if (!next.Open(captured, returnFocus))
            {
                next.Dispose();
                showStatus("CURRENT PROJECT CANNOT BE PREPARED FOR PUBLISHING");
                return;
            }
            _studioPublish = next;
        }
        catch { next.Dispose(); throw; }
    }

    private static void RefreshNativeStudioRoute()
    {
        if (_studioChange == null && _studioEditor == null && _studioPublish == null
            && _studio is { } studio && studio.TryTakeProjectChangeRequest(out var changes) && changes != null)
            OpenNativeStudioProjectChange(changes, studio);
        _studioChange?.Refresh();
        ApplyNativeStudioProjectChange();
        _studioEditor?.Refresh();
        _studioPublish?.Refresh();
        if (_studioPublish is { IsOpen: false }) CloseNativeStudioPublish();
        ApplyNativeStudioEditorReturn();
    }

    private static bool BackNativeStudioRoute()
    {
        if (_studioChange is { } change)
        {
            change.Back();
            ApplyNativeStudioProjectChange();
            return true;
        }
        if (_studioPublish is { } publish)
        {
            publish.Back();
            if (!publish.IsOpen) CloseNativeStudioPublish();
            return true;
        }
        if (_studioEditor is not { } editor) return false;
        editor.Back();
        ApplyNativeStudioEditorReturn();
        return true;
    }

    private static void CloseNativeStudioPublish()
    {
        var closing = _studioPublish;
        _studioPublish = null;
        closing?.Dispose();
    }

    private static void OpenNativeStudioEditor(NativeStudioEntryPage studio)
    {
        if (_host is not { } host || _pages == null || _studioEditor != null) return;
        if (!Shell.TryAcquireNativeStudioEditor(out var session, out string error) || session == null)
        { studio.ShowStatus(error); return; }
        OpenNativeStudioEditor(session);
    }

    private static void OpenNativeStudioEditor(INativeStudioEditorSession session)
    {
        if (_host is not { } host || _pages == null)
        { session.Dispose(); return; }
        NativeStudioEditorPage? next = null;
        try
        {
            next = new NativeStudioEditorPage(host, _pages, session);
            CloseNativeStudio();
            next.Open();
            _studioEditor = next;
        }
        catch
        {
            if (next != null) next.Dispose();
            else session.Dispose();
            throw;
        }
    }

    private static void OpenNativeStudioProjectChange(INativeStudioProjectChanges changes, NativeStudioEntryPage studio)
    {
        if (_host is not { } host || _pages == null || _studioChange != null) return;
        var next = new NativeStudioProjectChangePage(host, _pages, changes);
        try
        {
            if (!next.Open())
            {
                next.Dispose();
                studio.ShowStatus("PROJECT CHANGE IS NO LONGER AVAILABLE");
                return;
            }
            _studioChange = next;
            ApplyNativeStudioProjectChange();
        }
        catch { next.Dispose(); throw; }
    }

    private static void ApplyNativeStudioProjectChange()
    {
        if (_studioChange is not { } change) return;
        // Consume the committed lease before modal/entry retirement can cancel
        // the pending transition. Cancellation closes only the decision modal.
        if (change.TryTakeCommittedEditor(out var session) && session != null)
        {
            CloseNativeStudioProjectChange();
            OpenNativeStudioEditor(session);
        }
        else if (!change.IsOpen) CloseNativeStudioProjectChange();
    }

    private static void CloseNativeStudioProjectChange()
    {
        var closing = _studioChange;
        _studioChange = null;
        closing?.Dispose();
    }

    private static void ApplyNativeStudioEditorReturn()
    {
        if (_studioEditor is not { Closed: true } editor) return;
        bool legacy = editor.LegacyRequested;
        CloseNativeStudioPublish();
        CloseNativeStudioEditor();
        if (legacy) Shell.RmlUiNavigateStudio();
        else OpenNativeStudio(showChooser: false);
    }

    private static void CloseNativeStudioEditor()
    {
        var closing = _studioEditor;
        _studioEditor = null;
        closing?.Dispose();
    }

    private static bool NativeStudioInputReady(RmlUiHost host)
    {
        if (_studioEditor is not { } editor) return true;
        if (_inputWindow is { } window)
        {
            editor.ValidateInputBasis(window.FramebufferSize.X, window.FramebufferSize.Y,
                window.ClientSize.X, window.ClientSize.Y);
            // Page controls remain usable when the viewport renderer is unavailable.
            // Coordinate events wait for layout after an actual window-basis change.
            if (_studioLayoutWidth == window.FramebufferSize.X && _studioLayoutHeight == window.FramebufferSize.Y
                && _studioLayoutClientWidth == window.ClientSize.X && _studioLayoutClientHeight == window.ClientSize.Y)
                return true;
        }
        ReleaseNativeInput(host);
        return false;
    }

}
#endif
