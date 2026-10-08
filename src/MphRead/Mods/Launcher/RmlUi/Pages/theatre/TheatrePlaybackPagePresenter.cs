#if MPHREAD_RMLUI_POC
using System;
using System.Globalization;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

/// <summary>Engine-owned playback transport and viewport input, presented by a native document.</summary>
public sealed class TheatrePlaybackPagePresenter : IDisposable
{
    private static readonly RmlUiPageSpec Page = new("theatre-playback", "pages/theatre/playback.rml", "replay_pause");
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly TheatrePlaybackController _controller;
    private RmlUiDocumentToken _document;
    private bool _disposed;
    public TheatrePlaybackPagePresenter(RmlUiHost host, RmlUiPageManager pages, TheatrePlaybackController controller)
        => (_host, _pages, _controller) = (host, pages, controller);
    public TheatreViewportController Viewport { get; } = new();
    public RmlUiDocumentToken Document => _document;
    public bool IsOpen => !_disposed && _document != default && _host.IsAlive(_document) && _pages.Page == _document;
    public void Open() { ObjectDisposedException.ThrowIf(_disposed, this); _document = _pages.OpenPage(Page); Present(); }
    public bool Present()
    {
        if (!IsOpen) return false;
        TheatrePlaybackSnapshot state = _controller.Snapshot();
        _host.SetText(_document, "replay_pause", state.PlayAction ? "PLAY" : "PAUSE");
        _host.SetText(_document, "replay_time", Time(state.Frame) + " / " + Time(state.Duration));
        _host.SetText(_document, "replay_state", state.State.ToUpperInvariant());
        _host.SetText(_document, "replay_rate", state.Rate.ToString("0.##", CultureInfo.InvariantCulture) + "× SPEED");
        _host.SetText(_document, "replay_camera", state.Camera.ToUpperInvariant() + " CAMERA");
        _host.SetText(_document, "replay_error", state.Error); _host.SetBool(_document, "replay_error", state.Error.Length > 0);
        _host.SetBool(_document, "replay_inactive", !state.Active);
        foreach (string id in new[] { "pause", "jump_back", "jump_forward", "restart", "step", "rate", "camera", "previous_player", "next_player", "position", "studio", "fullscreen" })
            _host.SetBool(_document, "disabled:replay_" + id, !state.Active);
        if (_host.FocusedElement() != "replay_position")
            _host.SetField(_document, "replay_position", state.Duration == 0 ? "0" : ((long)state.Frame * 1000 / state.Duration).ToString(CultureInfo.InvariantCulture));
        return true;
    }
    public bool HandleIntent(in RmlUiIntent intent)
    {
        if (!IsOpen || intent.Kind != RmlUiIntentKind.ReplayAction || intent.Document != _document
            || !Enum.IsDefined((TheatrePlaybackAction)intent.Argument) || !_pages.Accept(intent)) return false;
        int position = 0;
        if ((TheatrePlaybackAction)intent.Argument == TheatrePlaybackAction.Seek)
        {
            // RmlUi range controls expose their value in floating-point form,
            // including step-one values such as "125.000000".
            if (!Double.TryParse(_host.ReadField(_document, "replay_position"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !Double.IsFinite(value)) return false;
            position = (int)Math.Round(Math.Clamp(value, 0, 1000), MidpointRounding.AwayFromZero);
        }
        _controller.Dispatch((TheatrePlaybackAction)intent.Argument, position);
        Present(); return true;
    }
    public bool TryTakeEngineCommand(out TheatrePlaybackAction command) => _controller.TryTakeEngineCommand(out command);
    public bool Close()
    {
        Viewport.Release();
        if (IsOpen && !_pages.ClosePage()) return false;
        _document = default; return true;
    }
    public void Dispose() { if (_disposed) return; if (!Close()) throw new InvalidOperationException("The replay controls could not be closed."); _disposed = true; }
    private static string Time(uint frames) => $"{frames / 3600}:{frames / 60 % 60:00}";
}
#endif
