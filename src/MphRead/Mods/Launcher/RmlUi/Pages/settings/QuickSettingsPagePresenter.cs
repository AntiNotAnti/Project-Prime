#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

/// <summary>Native quick settings only. No global MenuSettings or video edits.</summary>
internal sealed class QuickSettingsPagePresenter : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly QuickLauncherSettingsController _controller;
    private RmlUiDocumentToken _page;
    private long _revision;
    private bool _disposed;

    public bool OpenFullSettingsRequested { get; private set; }
    public bool Closed { get; private set; }
    // Prevent header navigation from silently dropping a staged Settings edit.
    public bool CanNavigateAway => !_controller.Current().Dirty;
    public QuickSettingsPagePresenter(RmlUiPageManager pages, IQuickLauncherPreferenceStore store)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new QuickLauncherSettingsController(store);
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("quick-settings",
            "pages/settings/quick.rml", "quick_motion"));
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || _page == default || intent.Document != _page
            || _pages.Top != _page || !_pages.Accept(intent)) return false;
        bool result = false;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.SettingsAction:
                if (intent.Argument is >= 1 and <= 3)
                    result = _controller.Toggle((QuickPreference)(intent.Argument - 1));
                else if (intent.Argument == 5 && !_controller.Current().Dirty)
                {
                    OpenFullSettingsRequested = true;
                    result = true;
                }
                break;
            case RmlUiIntentKind.SettingsApply:
                result = _controller.Apply();
                break;
            case RmlUiIntentKind.SettingsDiscard:
                result = _controller.Discard();
                break;
            case RmlUiIntentKind.SettingsClose:
                if (!_controller.Current().Dirty)
                {
                    Closed = true;
                    result = true;
                }
                break;
        }
        if (result) Refresh();
        return result;
    }

    private void Refresh()
    {
        if (_disposed || _page == default) return;
        var current = _controller.Current();
        var model = new Dictionary<string,RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["quick_motion_state"] = RmlUiBindingValue.FromText(current.Options.ReduceMotion ? "ON" : "OFF"),
            ["quick_updates_state"] = RmlUiBindingValue.FromText(current.Options.AutoUpdate ? "ON" : "OFF"),
            ["quick_debug_state"] = RmlUiBindingValue.FromText(current.Options.DebugLogs ? "ON" : "OFF"),
            ["quick_setting_status"] = RmlUiBindingValue.FromText(current.Status),
            ["disabled:quick_apply"] = RmlUiBindingValue.FromBoolean(!current.Dirty)
        };
        _pages.Present(_page, ++_revision, model);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_page != default && _pages.Page == _page)
            _pages.ClosePage();
        _controller.Dispose();
    }
}

internal sealed class LauncherQuickPreferenceStore : IQuickLauncherPreferenceStore
{
    public QuickLauncherPreferences Capture() => new(
        LauncherPrefs.ReduceMotion, LauncherPrefs.AutoUpdate, LauncherPrefs.DebugLogs);

    public void Save(QuickLauncherPreferences value)
    {
        // Do not leave in-memory flags changed if a locked or inaccessible
        // launcher.txt refused persistence. Other subsystems read these
        // static settings immediately, even without a restart.
        QuickLauncherPreferences before = Capture();
        LauncherPrefs.ReduceMotion = value.ReduceMotion;
        LauncherPrefs.AutoUpdate = value.AutoUpdate;
        LauncherPrefs.DebugLogs = value.DebugLogs;
        if (!LauncherPrefs.TrySave())
        {
            LauncherPrefs.ReduceMotion = before.ReduceMotion;
            LauncherPrefs.AutoUpdate = before.AutoUpdate;
            LauncherPrefs.DebugLogs = before.DebugLogs;
            throw new InvalidOperationException("Launcher preferences could not be written.");
        }
    }
}
#endif
