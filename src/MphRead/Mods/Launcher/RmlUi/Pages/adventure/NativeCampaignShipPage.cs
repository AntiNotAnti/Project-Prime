#if (MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID || PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

/// <summary>Gunship navigation over an immutable projection and the existing Adventure launch owner.</summary>
public sealed class NativeCampaignShipPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCampaignShipController<LaunchPlan> _controller;
    private readonly CampaignAudioSession _audio;
    private CampaignAudioPage? _audioPage;
    private RmlUiDocumentToken _document;
    private long _revision;
    private bool _opened, _disposed;

    public NativeCampaignShipDock Dock => _controller.Snapshot().Dock;
    public RmlUiDocumentToken Document => _document;
    public bool Closed => _controller.Snapshot().Closed;

    public NativeCampaignShipPage(RmlUiHost host, RmlUiPageManager pages, NativeCampaignShipController<LaunchPlan> controller)
        : this(host, pages, controller, new CampaignAudioSession()) { }
    internal NativeCampaignShipPage(RmlUiHost host, RmlUiPageManager pages, NativeCampaignShipController<LaunchPlan> controller,
        ICampaignAudioSink audio) : this(host, pages, controller, new CampaignAudioSession(audio)) { }
    private NativeCampaignShipPage(RmlUiHost host, RmlUiPageManager pages, NativeCampaignShipController<LaunchPlan> controller,
        CampaignAudioSession audio)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _audio = audio;
    }

    public void Open()
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_opened) return;
        _document = _pages.OpenPage(new("campaign-ship", "pages/adventure/ship.rml", "ship_open_map"));
        _opened = true; Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!Active() || intent.Document != _document) return false;
        bool returnToSaves = intent.Kind == RmlUiIntentKind.Navigate && intent.Argument == (int)RmlUiRouteArgument.Adventure;
        if (!returnToSaves && (intent.Kind != RmlUiIntentKind.CampaignShipAction || intent.Argument is < 0 or > 9)) return false;
        if (!_pages.Accept(intent)) return false;
        bool accepted = returnToSaves ? _controller.ReturnToSaves() : intent.Argument switch
        {
            0 => _controller.OpenMap(), 1 => _controller.Cockpit(),
            2 => _controller.Launch(exitShipToSurface: true), 3 => _controller.Launch(exitShipToSurface: false),
            4 => _controller.Refresh(), _ => _controller.Select(intent.Argument - 5)
        };
        if (accepted && !returnToSaves && intent.Argument is 2 or 3) _audio.LandingCue();
        if (Closed) _audio.Dispose();
        Refresh(); return accepted;
    }

    public bool Back()
    {
        _host.VerifyOwnerThread();
        if (!Active() || !_controller.Back()) return false;
        if (Closed) _audio.Dispose();
        Refresh(); return true;
    }

    public bool TryTakeEffect(out NativeCampaignShipEffect<LaunchPlan> effect)
    { _host.VerifyOwnerThread(); return _controller.TryTakeEffect(out effect); }

    private bool Active() => !_disposed && _opened && !Closed && _pages.Top == _document
        && _host.IsAlive(_document) && _host.IsVisible(_document);

    public void Refresh()
    {
        _host.VerifyOwnerThread();
        if (_disposed || !_opened) return;
        if (!_host.IsAlive(_document)) { _audio.Dispose(); return; }
        var state = _controller.Snapshot();
        if (!state.Closed) _audio.Tick();
        if (state.Revision <= _revision) return;
        CampaignAudioPage audioPage = state.StarMap ? CampaignAudioPage.StarMap : CampaignAudioPage.Dock;
        if (state.Verified && !state.Closed && _audioPage != audioPage)
        {
            _audio.Enter(audioPage, (CampaignPlanet)state.Dock.Planet);
            _audioPage = audioPage;
        }
        var fields = new Dictionary<string, RmlUiBindingValue>();
        void Text(string key, string value) => fields[key] = RmlUiBindingValue.FromText(value);
        void Bool(string key, bool value) => fields[key] = RmlUiBindingValue.FromBoolean(value);
        Text("ship_title", state.StarMap ? "ALIMBIC CLUSTER // STAR MAP" : "HUNTER GUNSHIP // DOCKED");
        Text("ship_identity", $"SAVE SLOT {state.Dock.Slot:00} // {((Hunter)state.Hunter).ToString().ToUpperInvariant()}");
        Text("ship_status", state.Verified ? "DOCK AND SAVE VERIFIED" : "DEPARTURE UNAVAILABLE");
        Text("ship_error", state.Error);
        Bool("visible:ship_error", state.Error.Length != 0);
        Bool("visible:ship_verified", state.Verified);
        Bool("visible:ship_dock_face", !state.StarMap);
        Bool("visible:ship_map_face", state.StarMap);
        bool usable = state.Verified && !state.Closed;
        Bool("disabled:ship_open_map", !usable);
        Bool("disabled:ship_exit", !usable);
        Bool("disabled:ship_refresh", state.Closed);
        Bool("disabled:ship_back", state.Closed);
        Bool("disabled:ship_cockpit", state.Closed);
        int detailIndex = state.StarMap ? state.Selected : state.Dock.Planet;
        string detail = "REFRESH TO VERIFY THE DOCKED SAVE BEFORE DEPARTURE.";
        bool canLand = false;
        for (int i = 0; i < 5; i++)
        {
            bool present = state.Verified && state.Destinations.Count == 5;
            var row = present ? state.Destinations[i] : default;
            Bool("visible:ship_planet_" + i, present);
            Bool("disabled:ship_planet_" + i, !usable);
            Bool("class:ship_planet_" + i + ":selected", present && state.Selected == i);
            Bool("class:ship_planet_" + i + ":locked", present && !row.Unlocked);
            Text("ship_planet_label_" + i, present ? row.Name + (row.Unlocked ? "" : " // LOCKED") : "");
            if (present && detailIndex == i)
            {
                detail = row.Name + (state.StarMap ? row.Unlocked ? row.Cleared ? " // AREA CLEARED" : " // ACCESS GRANTED" : " // SECTOR LOCKED" : " // SHIP DOCKED");
                detail += row.Unlocked ? $"\nOCTOLITHS {row.Octoliths}/{row.OctolithTotal} // ARTIFACTS {row.Artifacts}/{row.ArtifactTotal}"
                    + (state.StarMap ? row.Visited ? "\nPREVIOUSLY VISITED" : "\nFIRST LANDING" : "")
                    : "\nExplore the Alimbic Cluster to unlock this destination.";
                canLand = row.Unlocked;
            }
        }
        Text("ship_detail", detail);
        Bool("disabled:ship_land", !usable || !canLand);
        if (_pages.Present(_document, state.Revision, fields)) _revision = state.Revision;
    }

    public void Dispose()
    {
        _host.VerifyOwnerThread(); if (_disposed) return;
        _disposed = true; _audio.Dispose(); _controller.Dispose();
        if (_document != default && _pages.Page == _document) _pages.ClosePage();
        _document = default;
    }
}
#endif
