#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Hunters;

/// <summary>
/// Independent native Home Hunter picker. The active lobby still uses its
/// own authoritative choice/roster/low-tier rules, never this local draft.
/// </summary>
internal sealed class NativeHunterPagePresenter : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly LauncherHunterChoiceController _controller;
    private RmlUiDocumentToken _page;
    private long _revision;
    private bool _disposed;

    public bool Closed { get; private set; }
    public bool OpenFullLicenseRequested { get; private set; }
    public bool CanNavigateAway => !_controller.Current().Dirty;

    public NativeHunterPagePresenter(RmlUiPageManager pages, ILauncherHunterChoiceStore store)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new LauncherHunterChoiceController(store);
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("native-hunter",
            "pages/hunters/native-picker.rml", "hunter_choice0"));
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || intent.Document != _page || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        bool accepted = intent.Kind switch
        {
            RmlUiIntentKind.HunterSelect => _controller.SelectHunter(intent.Argument),
            RmlUiIntentKind.HunterSuit => _controller.SelectSuit(intent.Argument),
            RmlUiIntentKind.HunterApply => _controller.Apply(),
            RmlUiIntentKind.HunterResetPreview => Discard(),
            RmlUiIntentKind.HunterCancel => CloseIfClean(),
            RmlUiIntentKind.HunterOpen => FullIfClean(),
            _ => false
        };
        if (accepted) Refresh();
        return accepted;
    }

    private bool Discard()
    {
        _controller.Discard();
        return true;
    }

    private bool CloseIfClean()
    {
        if (_controller.Current().Dirty) return false;
        Closed = true;
        return true;
    }

    private bool FullIfClean()
    {
        if (_controller.Current().Dirty) return false;
        OpenFullLicenseRequested = true;
        return true;
    }

    private void Refresh()
    {
        var state = _controller.Current();
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["native_hunter_name"] = RmlUiBindingValue.FromText(
                state.Choice.Hunter.ToString().ToUpperInvariant()),
            ["native_hunter_status"] = RmlUiBindingValue.FromText(state.Status),
            ["native_hunter_hint"] = RmlUiBindingValue.FromText(
                "SUIT " + (state.Choice.Suit + 1) + " // YOUR NEXT DEPLOYMENT"),
            ["disabled:hunter_apply"] = RmlUiBindingValue.FromBoolean(!state.Dirty)
        };
        for (int i = 0; i < LauncherHunterChoiceController.Playable.Length; i++)
            data["class:hunter_choice" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                LauncherHunterChoiceController.Playable[i] == state.Choice.Hunter);
        for (int i = 0; i < 4; i++)
            data["class:hunter_suit" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                i == state.Choice.Suit);
        _pages.Present(_page, ++_revision, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
        _controller.Dispose();
    }
}

internal sealed class LauncherHunterChoiceStore : ILauncherHunterChoiceStore
{
    public LauncherHunterChoice Capture()
        => new(LauncherPrefs.LastHunter, LauncherPrefs.LastColor);

    public void Save(LauncherHunterChoice choice)
    {
        LauncherHunterChoice before = Capture();
        LauncherPrefs.LastHunter = choice.Hunter;
        LauncherPrefs.LastColor = choice.Suit;
        if (!LauncherPrefs.TrySave())
        {
            LauncherPrefs.LastHunter = before.Hunter;
            LauncherPrefs.LastColor = before.Suit;
            throw new InvalidOperationException("Hunter profile could not be saved.");
        }
    }
}
#endif
