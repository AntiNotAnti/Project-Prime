#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

/// <summary>Real local Hunter/suit persistence; no preview, account or synthetic entitlement.</summary>
internal sealed class RmlUiAndroidLocalHunterPage : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly LauncherHunterChoiceController _controller;
    private RmlUiDocumentToken _document;
    private long _revision;
    private string _notice = "";
    internal bool Closed { get; private set; }
    internal RmlUiAndroidLocalHunterPage(RmlUiPageManager pages, ILauncherHunterChoiceStore? store = null)
    { _pages = pages; _controller = new(store ?? new Store()); }
    internal void Open()
    { _document = _pages.OpenPage(new("android-local-hunter", "pages/hunters/local-picker.rml", "local_hunter0")); Refresh(); }
    internal bool Handle(in RmlUiIntent intent)
    {
        if (Closed || intent.Document != _document || _pages.Top != _document || !_pages.Accept(intent)) return false;
        _notice = "";
        bool accepted = intent.Kind switch
        {
            RmlUiIntentKind.HunterSelect => _controller.SelectHunter(intent.Argument),
            RmlUiIntentKind.HunterSuit => _controller.SelectSuit(intent.Argument),
            RmlUiIntentKind.HunterApply => _controller.Apply(),
            RmlUiIntentKind.HunterResetPreview => Discard(),
            RmlUiIntentKind.HunterCancel => Back(), _ => false
        };
        Refresh(); return accepted;
    }
    internal bool Back()
    {
        if (_controller.Current().Dirty) { _notice = "APPLY OR DISCARD YOUR CHANGES BEFORE RETURNING"; Refresh(); return false; }
        Closed = true; return true;
    }
    private bool Discard() { _controller.Discard(); return true; }
    private void Refresh()
    {
        if (Closed) return;
        var state = _controller.Current();
        var fields = new Dictionary<string, RmlUiBindingValue>
        {
            ["local_hunter_name"] = RmlUiBindingValue.FromText(state.Choice.Hunter.ToString().ToUpperInvariant()),
            ["local_hunter_status"] = RmlUiBindingValue.FromText(_notice.Length == 0 ? state.Status : _notice),
            ["disabled:local_apply"] = RmlUiBindingValue.FromBoolean(!state.Dirty)
        };
        for (int i = 0; i < 7; i++) fields["class:local_hunter" + i + ":selected"] = RmlUiBindingValue.FromBoolean((int)state.Choice.Hunter == i);
        for (int i = 0; i < 4; i++) fields["class:local_suit" + i + ":selected"] = RmlUiBindingValue.FromBoolean(state.Choice.Suit == i);
        _pages.Present(_document, ++_revision, fields);
    }
    public void Dispose()
    {
        try { if (_pages.Page == _document) _pages.ClosePage(); }
        finally { _controller.Dispose(); Closed = true; }
    }
    private sealed class Store : ILauncherHunterChoiceStore
    {
        public LauncherHunterChoice Capture() => new(LauncherPrefs.LastHunter, LauncherPrefs.LastColor);
        public void Save(LauncherHunterChoice choice)
        {
            var previous = Capture();
            LauncherPrefs.LastHunter = choice.Hunter; LauncherPrefs.LastColor = choice.Suit;
            if (LauncherPrefs.TrySave()) return;
            LauncherPrefs.LastHunter = previous.Hunter; LauncherPrefs.LastColor = previous.Suit;
            throw new InvalidOperationException("Hunter profile could not be saved.");
        }
    }
}
#endif
