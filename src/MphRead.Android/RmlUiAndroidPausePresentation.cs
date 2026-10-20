#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using MphRead.Formats;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Network;
using MphRead.Text;

namespace MphRead.Droid;

/// <summary>Engine adapter; constructed, captured and retired on the EGL owner.</summary>
internal sealed class RmlUiAndroidPausePresentation : IRmlUiAndroidPauseSurface
{
    private readonly Scene _scene;
    private readonly string _root;
    private readonly RmlUiAndroidDrawListSession _session = new();
    private RmlUiPageManager? _pages;
    private InGameController? _controller;
    private InGamePagePresenter? _presenter;
    private RmlUiAndroidPauseState _effect;
    private bool _leased, _disposed;
    internal RmlUiAndroidPausePresentation(Scene scene, string root) => (_scene, _root) = (scene, root);
    public RmlUiAndroidInputQueue Input => _session.Input;

    public void Open(int width, int height, float density)
    {
        if (!RmlUiAndroidPauseNativeLease.TryAcquire(this))
            throw new InvalidOperationException("The previous native pause session has not retired.");
        _leased = true;
        if (!_session.Initialize(width, height, density, _root))
            throw new InvalidOperationException("The native pause session could not initialize.");
        _pages = new(_session.Host);
        _controller = new(new Backend(this));
        CampaignJournal? journal = null;
        if (_scene.Players.PlayerCount > 0 && _scene.GameState.SinglePlayer && !_scene.Services.IsReplica
            && !NetSession.Active && !DemoPlayback.IsActive)
        {
            IReadOnlyList<StringTableEntry>? entries = null;
            try { entries = Strings.ReadStringTable(StringTables.ScanLog); }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            { Console.WriteLine("[android/rmlui] scan archive text unavailable: " + error.Message); }
            journal = CampaignJournal.Capture(_scene.GameState.StorySave, entries);
        }
        _presenter = new(_session.Host, _pages, _controller, journal,
            offerFullscreen: false, localSessionPaused: false);
        _presenter.Open();
        _session.Input.Refresh(_session.Host);
    }
    public void Back() => _presenter?.Back();
    public void Render(int width, int height, float density)
    {
        _presenter?.Refresh();
        _session.Render(width, height, density);
        for (int i = 0; i < 64 && _session.Host.TryTakeIntent(out var intent); i++)
        {
            _presenter?.Handle(intent);
            if (_effect == RmlUiAndroidPauseState.Idle && _presenter?.TryTakeEffect(out var pageEffect) == true)
                _effect = Translate(pageEffect);
            if (_effect != RmlUiAndroidPauseState.Idle) break;
        }
        _session.Input.Refresh(_session.Host);
    }
    public bool TryTakeEffect(out RmlUiAndroidPauseState effect)
    {
        effect = _effect; _effect = RmlUiAndroidPauseState.Idle;
        if (_presenter?.TryTakeEffect(out var pageEffect) == true && effect == RmlUiAndroidPauseState.Idle)
            effect = Translate(pageEffect);
        return effect != RmlUiAndroidPauseState.Idle;
    }
    private static RmlUiAndroidPauseState Translate(InGameEffect effect) => effect switch
    {
        InGameEffect.Settings => RmlUiAndroidPauseState.Settings,
        InGameEffect.Replay => RmlUiAndroidPauseState.Replay,
        _ => RmlUiAndroidPauseState.Resume
    };
    public void Suspend() => _session.SuspendInput();
    public void Resume() => _session.ResumeInput();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Exception? first = null;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException)) { first ??= error; }
        }
        // Every owner is retired even when a document or input-release call
        // fails. Native shutdown happens before GameView destroys its context.
        Cleanup(_session.SuspendInput);
        Cleanup(() => _presenter?.Dispose()); _presenter = null;
        Cleanup(() => _controller?.Dispose()); _controller = null;
        Cleanup(() => _pages?.Dispose()); _pages = null;
        bool shutdownSucceeded = false;
        try { _session.Shutdown(); shutdownSucceeded = true; }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException)) { first ??= error; }
        finally
        {
            if (_leased) RmlUiAndroidPauseNativeLease.Release(this, shutdownSucceeded);
            _leased = false;
        }
        if (first != null) throw new InvalidOperationException("Native pause cleanup failed.", first);
    }

    private sealed class Backend(RmlUiAndroidPausePresentation owner) : IInGameBackend
    {
        private readonly IInGameBackend _engine = InGameController.CreateEngineBackend(owner._scene);
        public InGameFacts Capture() => _engine.Capture();
        public ImmutableArray<string> Maps() => _engine.Maps();
        public string EditBot(InGameAction action, InGameBot bot) => _engine.EditBot(action, bot);
        public string Execute(InGameAction action, string map)
        {
            if (action is InGameAction.Leave or InGameAction.Quit)
            {
                owner._effect = action == InGameAction.Leave ? RmlUiAndroidPauseState.Leave : RmlUiAndroidPauseState.Quit;
                return "";
            }
            if (action == InGameAction.Fullscreen) return "Window mode controls are unavailable on Android.";
            return _engine.Execute(action, map);
        }
    }
}
#endif
