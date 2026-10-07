#if MPHREAD_RMLUI_ANDROID
using System;
using MphRead.Mods;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Training;

namespace MphRead.Droid;

/// <summary>Results documents on the match's existing native host and render owner.</summary>
internal sealed class AndroidRmlUiResultsSession : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly Action _openPause;
    private readonly Action<AimTrainerSession, AimResultsAction> _trainingAction;
    private readonly IMatchResultsBackend? _backend;
    private Scene? _scene;
    private MatchResultsController? _match;
    private MatchResultsPagePresenter? _matchPage;
    private AimTrainerSession? _training;
    private AimResultsController? _aim;
    private AimResultsPagePresenter? _aimPage;
    private bool _matchDismissed, _disposed;

    internal AndroidRmlUiResultsSession(RmlUiHost host, RmlUiPageManager pages,
        Action openPause, Action<AimTrainerSession, AimResultsAction> trainingAction,
        IMatchResultsBackend? backend = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _openPause = openPause ?? throw new ArgumentNullException(nameof(openPause));
        _trainingAction = trainingAction ?? throw new ArgumentNullException(nameof(trainingAction));
        _backend = backend;
    }

    internal bool Visible => !_disposed && (_matchPage?.Active == true || _aimPage?.Active == true);
    internal bool TrainingVisible => !_disposed && _aimPage?.Active == true;
    internal RmlUiDocumentToken Document => _aimPage?.Document ?? _matchPage?.Document ?? default;

    internal bool OpenIfReady(Scene? scene, bool menuVisible)
    {
        VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this);
        SelectScene(scene);
        if (scene == null) return false;
        bool available = MatchAvailable(scene);
        if (!available) _matchDismissed = false;
        if (menuVisible || ForeignPageVisible())
        {
            // Training owns its existing menu pause. Suspending its results
            // for another menu must let that same completed session return.
            Retire(restoreTraining: true);
            return false;
        }
        if (Visible) return true;
        if (scene.AimTrainer is { Completed: true, ResultsShown: false } training)
        {
            Retire();
            _training = training;
            _aim = new(AimResultsReport.From(training));
            _aimPage = new(_host, _pages, _aim);
            try
            {
                _host.ReleaseInput();
                _aimPage.Open();
                if (!_aimPage.Active) throw new InvalidOperationException("The native training results did not open.");
                training.ResultsShown = true;
                return true;
            }
            catch { Retire(); throw; }
        }
        if (!available || _matchDismissed || scene.AimTrainer != null) return false;
        _match = _backend == null ? new MatchResultsController() : new MatchResultsController(_backend);
        _matchPage = new(_host, _pages, _match);
        try
        {
            _host.ReleaseInput();
            _matchPage.Open();
            if (!_matchPage.Active) throw new InvalidOperationException("The native match results did not open.");
            EndScreen.PanelUp = true;
            return true;
        }
        catch { Retire(); throw; }
    }

    internal void Update(Scene? scene, bool menuVisible)
    {
        VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this);
        SelectScene(scene);
        ConsumeEffects();
        if (scene == null) return;
        if (_aimPage != null && (!ReferenceEquals(_training, scene.AimTrainer) || _training?.Completed != true)) Retire();
        if (_matchPage != null && !MatchAvailable(scene)) Retire();
        if (OpenIfReady(scene, menuVisible)) _matchPage?.Refresh();
        ConsumeEffects();
    }

    internal bool HandleIntent(in RmlUiIntent intent)
    {
        VerifyOwner();
        if (_disposed) return false;
        bool handled = _aimPage?.Handle(intent) == true || _matchPage?.Handle(intent) == true;
        if (handled) ConsumeEffects();
        return handled;
    }

    internal bool Back()
    {
        VerifyOwner();
        if (_disposed) return false;
        bool handled = _aimPage?.Back() == true || _matchPage?.Back() == true;
        if (handled) ConsumeEffects();
        return handled;
    }

    internal void Close()
    {
        VerifyOwner();
        _matchDismissed = true;
        Retire();
    }

    private bool MatchAvailable(Scene scene) => scene.GameState.MatchState is MatchState.GameOver or MatchState.Ending
        && !scene.GameState.MenuPause && (_backend?.Capture().Available ?? EndScreen.PanelAvailable);

    private bool ForeignPageVisible() => _pages.Page != default && _pages.Page != Document
        || _pages.Top != default && _pages.Top != _pages.Page;

    private void SelectScene(Scene? scene)
    {
        if (ReferenceEquals(scene, _scene)) return;
        Retire(); _scene = scene; _matchDismissed = false;
    }

    private void ConsumeEffects()
    {
        // Presenters keep their accepted effect even after closing a document.
        // Retire before invoking Android's generation-guarded UI callbacks.
        if (_aimPage?.TryTakeAction(out var action) == true)
        {
            AimTrainerSession training = _training!;
            Retire();
            _trainingAction(training, action);
        }
        else if (_matchPage?.TryTakeEffect(out var effect) == true)
        {
            _matchDismissed = true;
            Retire();
            if (effect == MatchResultsEffect.Close) _openPause();
            // Rematch has already reached OfflineRematch.StartNext, whose
            // Android callback queues the new match onto the activity thread.
        }
    }

    private void Retire(bool restoreTraining = false)
    {
        if (_matchPage == null && _aimPage == null) return;
        if (_host.CurrentInputDocument == Document) _host.ReleaseInput();
        _matchPage?.Dispose(); _matchPage = null;
        _match?.Dispose(); _match = null;
        _aimPage?.Dispose(); _aimPage = null;
        _aim?.Dispose(); _aim = null;
        if (restoreTraining && _training?.Completed == true) _training.ResultsShown = false;
        _training = null;
        EndScreen.PanelUp = false;
    }

    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Android result documents belong to the match render owner.");
    }

    public void Dispose()
    {
        VerifyOwner();
        if (_disposed) return;
        Retire(); _scene = null; _disposed = true;
    }
}
#endif
