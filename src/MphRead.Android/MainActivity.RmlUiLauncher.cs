#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Views;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;

namespace MphRead.Droid;

public partial class MainActivity
{
    internal const string NativeLauncherIntentExtra = "projectprime.rmlui.launcher";
    private GameView? _nativeLauncherView;
    private StartScreen? _nativeReadyHome;
    private bool _nativeLauncherRequested, _nativeLauncherAttempted, _nativePreviewRetiring;
    private Task<bool>? _nativePreviewRetirement;
    private volatile bool _nativeLauncherGlOwned;
    private Guid _nativeActivation = Guid.NewGuid();
    private (GameView View, RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest> Witness)? _nativeLocalAdmission;
    private (GameView View, RmlUiAndroidLocalWitness Witness)? _nativeHomeReturn;
    internal bool NativeLauncherOwnsGl => RmlUiAndroidLauncherPreviewBarrier.Blocked;
    private GameView? ActiveGlView => _gameView ?? _nativeLauncherView;

    private void InstallNativeLauncher()
    {
        _nativeLauncherRequested = Intent?.GetBooleanExtra(NativeLauncherIntentExtra, false) == true;
        if (!_nativeLauncherRequested) return;
        _nativeReadyHome = AndroidApp.Home;
        if (_nativeReadyHome != null) _nativeReadyHome.RmlUiReady += TryActivateNativeLauncher;
        if (_nativeAssetRoot != null)
            _ = _nativeAssetRoot.ContinueWith(_ => PostNativeLauncher(TryActivateNativeLauncher), TaskScheduler.Default);
        TryActivateNativeLauncher();
    }
    private void PostNativeLauncher(Action action)
    {
        if (_nativePauseDestroyed) return;
        try { RunOnUiThread(() => { if (!_nativePauseDestroyed) action(); }); }
        catch (ObjectDisposedException) { }
        catch (Java.Lang.IllegalStateException) when (_nativePauseDestroyed) { }
    }
    private void TryActivateNativeLauncher()
    {
        if (!_nativeLauncherRequested || _nativeLauncherAttempted || _nativePreviewRetiring
            || _nativePauseDestroyed || !_nativePauseForeground || _content == null
            || _gameView != null || _nativeLauncherView != null || _pending != null
            || NetSession.Active || DemoPlayback.IsActive || !GameFiles.Ready
            || _nativeAssetRoot?.IsCompletedSuccessfully != true || AndroidApp.Home?.RmlUiCanPresent != true)
            return;
        // Existing preview jobs own separate worlds/contexts. Do not enter a
        // native EGL owner while they are active, or restart them behind it.
        if (_renderingPreviews || _renderingHere) { _nativeLauncherAttempted = true; return; }
        if (!RmlUiAndroidLauncherPreviewBarrier.TryAcquire(_nativeActivation)) return;
        _nativePreviewRetiring = true; _nativeLauncherGlOwned = true;
        Guid activation = _nativeActivation;
        var preview = AndroidHunterShot.Current;
        _nativePreviewRetirement = Task.Run(() => { preview?.Retire(); return preview?.IsRetired != false; });
        _ = _nativePreviewRetirement.ContinueWith(task =>
            PostNativeLauncher(() =>
            {
                _nativePreviewRetiring = false;
                if (activation != _nativeActivation || !_nativePauseForeground || !task.IsCompletedSuccessfully || !task.Result
                    || _gameView != null || _pending != null || NetSession.Active || DemoPlayback.IsActive
                    || AndroidApp.Home?.RmlUiCanPresent != true)
                { RmlUiAndroidLauncherPreviewBarrier.Release(activation); _nativeLauncherGlOwned = false; return; }
                _nativeLauncherAttempted = true;
                CreateNativeLauncherView();
            }), TaskScheduler.Default);
    }
    private void CreateNativeLauncherView()
    {
        var home = AndroidApp.Home;
        if (_content == null || home == null || _nativeAssetRoot == null)
        { RmlUiAndroidLauncherPreviewBarrier.Release(_nativeActivation); _nativeLauncherGlOwned = false; return; }
        var options = new RmlUiAndroidLauncherOptions(_nativeAssetRoot, home.RmlUiRoomKeys,
            home.RmlUiLocalRules,
            NativeLauncherOpened, NativeOfflineRequested, NativeLauncherClosed, NativeSceneRetired, NativeSaveChangeRequested,
            (currentView, lifetime, change, publish, token) => NativeOfflineRulesRequested(home, currentView, lifetime, change, publish, token));
        GameView? view = null;
        view = new GameView(this, _controls, new AndroidInput(),
            (_, _) => throw new InvalidOperationException("No local match has been admitted."),
            () =>
            {
                var ended = view?.NativeLocalState;
                PostNativeLauncher(() => { if (ReferenceEquals(_gameView, view) && ended != null && SameNativeMatch(view!, ended)) EndMatch(); });
            },
            () =>
            {
                var loaded = view?.NativeLocalState;
                PostNativeLauncher(() => { if (ReferenceEquals(_gameView, view) && loaded != null && view!.NativeLocalCurrent(loaded)) MatchLoaded(); });
            },
            error =>
            {
                var failed = view?.NativeLocalState;
                PostNativeLauncher(() =>
                {
                    if (ReferenceEquals(_gameView, view) && failed != null && SameNativeMatch(view!, failed))
                        FailMatch(error, () => ReferenceEquals(_gameView, view) && SameNativeMatch(view!, failed));
                    else if (ReferenceEquals(_nativeLauncherView, view)) RestoreLegacyLauncher(quit: false);
                });
            },
            () =>
            {
                var menu = view?.NativeLocalState;
                PostNativeLauncher(() => { if (ReferenceEquals(_gameView, view) && menu != null && view!.NativeLocalCurrent(menu)) TogglePauseMenu(); });
            }, show =>
            {
                var input = view?.NativeLocalState;
                PostNativeLauncher(() => { if (ReferenceEquals(_gameView, view) && input != null && view!.NativeLocalCurrent(input)) ShowSoftKeyboard(show); });
            },
            CreateNativePauseOptions(), options);
        _nativeLauncherView = view;
        view.SetZOrderMediaOverlay(true); _content.AddView(view);
    }
    private void NativeLauncherOpened(GameView view, Guid lifetime) => PostNativeLauncher(() =>
    {
        if (!ReferenceEquals(_nativeLauncherView, view) || !_nativePauseForeground || !view.NativeLauncherOpen(lifetime)) return;
        if (_launcherView != null) _launcherView.Visibility = ViewStates.Gone;
        MovingBackdrop.Suspended = true; Deck.Asleep = true;
        MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
        view.RequestFocus(); GoImmersive(true);
    });
    private void NativeOfflineRequested(GameView view, RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest> witness)
        => PostNativeLauncher(() =>
        {
            if (!ReferenceEquals(_nativeLauncherView, view) || !view.NativeLauncherCurrent(witness)) return;
            if (!_nativePauseForeground || _pending != null || _gameView != null || AndroidApp.Home?.RmlUiCanPresent != true)
            { RestoreLegacyLauncher(quit: false); return; }
            _nativeLocalAdmission = (view, witness);
            // Existing StartScreen owns all startup/session/modifier/prefs
            // admission. Its Done event calls the existing StartMatch path.
            try
            {
                var request = witness.Request;
                bool accepted = !NetSession.Active && !DemoPlayback.IsActive && !MatchStart.HasPendingCommit
                    && request.IsCurrent(RmlUiAndroidLocalLaunchRequest.ReadSlot, () => MatchStart.RecentShipDock)
                    && (request.Kind switch
                    {
                        RmlUiAndroidLocalLaunchKind.Offline => AndroidApp.Home.RmlUiLaunchOffline(request.Offline),
                        RmlUiAndroidLocalLaunchKind.Adventure => AndroidApp.Home.RmlUiLaunchAdventure(request.Slot, request.NewGame, request.Hunter),
                        RmlUiAndroidLocalLaunchKind.Ship => AndroidApp.Home.RmlUiLaunchCampaignShip(request.ShipPlan, request.Dock, request.Fingerprint),
                        _ => false
                    });
                if (!accepted || _pending == null)
                    RestoreLegacyLauncher(quit: false);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            {
                Console.WriteLine("[android/rmlui] existing local admission failed: " + error.Message);
                RestoreLegacyLauncher(quit: false);
            }
        });
    private void NativeLauncherClosed(GameView view, Guid lifetime, bool quit) => PostNativeLauncher(() =>
    {
        if (!ReferenceEquals(_nativeLauncherView, view) || view.NativeLauncherLifetime != lifetime) return;
        RestoreLegacyLauncher(quit);
    });
    private Task<RmlUiAdventureSaveResult> NativeSaveChangeRequested(GameView view, Guid lifetime,
        RmlUiAdventureSaveChange change, CancellationToken token)
        => RmlUiAndroidSaveWork.Run(PostNativeLauncher,
            () => ReferenceEquals(_nativeLauncherView, view) && _nativePauseForeground && _pending == null && _gameView == null
                && view.NativeLauncherCanManageSaves(lifetime) && !NetSession.Active && !DemoPlayback.IsActive
                && !MatchStart.HasPendingCommit && Menu.NeededSave == SaveWhen.Never
                && AndroidApp.Home?.RmlUiCanPresent == true,
            (captured, cancellation) =>
            {
                string status = "SAVE CHANGE REFUSED // CURRENT SAVE OWNER UNAVAILABLE";
                bool success = AndroidApp.Home?.RmlUiManageAdventureSave(captured, out status, cancellation) == true;
                return new(captured, success, status);
            }, change, token);
    private Task<RmlUiOfflineRulesSaveResult> NativeOfflineRulesRequested(StartScreen home, GameView view, Guid lifetime,
        RmlUiOfflineRulesChange change, Action<RmlUiOfflineRules?> publish, CancellationToken token)
    {
        var activation = _nativeActivation;
        return RmlUiAndroidOfflineRulesWork.Run(PostNativeLauncher,
            () => activation == _nativeActivation && ReferenceEquals(AndroidApp.Home, home)
                && ReferenceEquals(_nativeLauncherView, view) && _nativePauseForeground
                && _pending == null && _gameView == null && view.NativeLauncherCanManageSaves(lifetime)
                && !NetSession.Active && !DemoPlayback.IsActive && !MatchStart.HasPendingCommit
                && Menu.NeededSave == SaveWhen.Never && home?.CanManageAdventureSaves == true,
            (captured, cancellation) => home!.RmlUiSaveOfflineRules(captured, cancellation),
            publish, change, token, readCurrent: () => home.RmlUiLocalRules);
    }
    private static bool SameNativeMatch(GameView view, RmlUiAndroidLocalWitness expected)
    {
        var current = view.NativeLocalState;
        return current != null && current.Lifetime == expected.Lifetime && current.Cycle == expected.Cycle && current.Match == expected.Match;
    }
    private void NativeSceneRetired(GameView view, RmlUiAndroidLocalWitness witness) => PostNativeLauncher(() =>
    {
        if (!ReferenceEquals(_gameView, view) || !view.NativeLocalCurrent(witness)) return;
        _nativeHomeReturn = (view, witness);
        ResumeNativeHomeReturn();
    });
    private void ResumeNativeHomeReturn()
    {
        if (!_nativePauseForeground || _nativeHomeReturn is not { } returned) return;
        _nativeHomeReturn = null;
        var view = returned.View;
        if (!ReferenceEquals(_gameView, view) || !view.NativeLocalCurrent(returned.Witness)) return;
        try
        {
            if (_content == null || _nativeAssetRoot?.IsCompletedSuccessfully != true || NetSession.Active || DemoPlayback.IsActive)
                throw new InvalidOperationException("Local Home no longer owns this Activity.");
            _matchUiEpoch++;
            OfflineRematch.StartNext = null; _pending = null; _nativeLocalAdmission = null;
            _pauseMenuOpen = _replayEditorOnLoad = _replayEditorOpen = false; _nativePauseDeferred = null;
            HideNotice(); HideEndPanel(); _endPanelTick = null; AndroidUiSurface.Current?.Hide();
            if (_overlay != null) { _content.RemoveView(_overlay); _overlay = null; }
            ShowSoftKeyboard(false); _controls.ReleaseEverything(); _controls.SetSpectator(false, false);
            AndroidPerformance.SetMatchActive(false);
            ResetLauncher();
            var home = AndroidApp.Home;
            if (home?.RmlUiCanPresent != true) throw new InvalidOperationException("The existing local launch owner is not ready.");
            var options = new RmlUiAndroidLauncherOptions(_nativeAssetRoot, home.RmlUiRoomKeys, home.RmlUiLocalRules,
                NativeLauncherOpened, NativeOfflineRequested, NativeLauncherClosed, NativeSceneRetired, NativeSaveChangeRequested,
                (currentView, lifetime, change, publish, token) => NativeOfflineRulesRequested(home, currentView, lifetime, change, publish, token));
            if (!view.AcknowledgeNativeHome(returned.Witness, options))
                throw new InvalidOperationException("The native Home return was retired.");
            _gameView = null; _nativeLauncherView = view;
            if (_launcherView != null) _launcherView.Visibility = ViewStates.Gone;
            view.TranslationX = 0;
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
            Deck.Asleep = true; MovingBackdrop.Suspended = true;
            Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            RequestedOrientation = _orientationBefore; GoImmersive(true);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            Console.WriteLine("[android/rmlui] local Home return failed: " + error.Message);
            // This exact View remains the only owner until terminal Stop is
            // acknowledged; the preview barrier is released after that ack.
            _gameView = null; _nativeLauncherView = view; RestoreLegacyLauncher(quit: false);
        }
    }
    private bool TryReturnCurrentNativeHome()
    {
        var view = _gameView; var witness = view?.NativeLocalState;
        if (view == null || witness == null) return false;
        if (witness.Phase is RmlUiAndroidLocalPhase.ReturnRequested or RmlUiAndroidLocalPhase.Retiring
            or RmlUiAndroidLocalPhase.AwaitingHomeAck or RmlUiAndroidLocalPhase.HomeAccepted) return true;
        if (!view.TryReturnNativeHome(witness)) return false;
        _controls.ReleaseEverything(); ShowSoftKeyboard(false);
        MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
        HideEndPanel(); return true;
    }
    private bool TransferNativeLauncher(LaunchPlan plan)
    {
        if (_nativeLauncherView == null) return false;
        var view = _nativeLauncherView;
        if (_nativeLocalAdmission is not { } admission || !ReferenceEquals(admission.View, view)
            || NetSession.Active || DemoPlayback.IsActive || MatchStart.HasPendingCommit
            || !admission.Witness.Request.MatchesPlan(plan, LauncherPrefs.LastColor)
            || !admission.Witness.Request.IsCurrent(RmlUiAndroidLocalLaunchRequest.ReadSlot, () => MatchStart.RecentShipDock)
            || !view.AcceptNativeLauncher(admission.Witness, plan))
        { RestoreLegacyLauncher(quit: false); return false; }
        _nativeLocalAdmission = null; _nativeLauncherView = null; _gameView = view;
        return true;
    }
    private void RestoreLegacyLauncher(bool quit)
    {
        var view = _nativeLauncherView;
        _nativeLauncherView = null; _nativeLocalAdmission = null;
        _nativeLauncherAttempted = true;
        if (view != null)
        {
            view.Stop(); _content?.RemoveView(view);
            ReleaseNativePreviewAfter(view);
        }
        if (_pending != null) { _pending = null; HideNotice(); }
        _controls.ReleaseEverything();
        AndroidPerformance.SetMatchActive(false); OfflineRematch.StartNext = null;
        if (_launcherView != null) _launcherView.Visibility = ViewStates.Visible;
        MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
        Deck.Asleep = false; MovingBackdrop.Suspended = _renderingPreviews;
        ResetLauncher();
        Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
        GoImmersive(true);
        if (quit) AndroidApp.Home?.GoBack(); // Existing confirmation, never an accidental direct exit.
    }
    private void ReleaseNativePreviewAfter(GameView view)
    {
        Guid activation = _nativeActivation;
        _ = view.NativeStopped.ContinueWith(_ => PostNativeLauncher(() =>
        {
            if (activation == _nativeActivation && _nativeLauncherView == null) _nativeLauncherGlOwned = false;
        }), TaskScheduler.Default);
        // This release is independent of the Activity dispatcher: OnDestroy
        // deliberately rejects every UI callback, including a late stop ack.
        _ = view.NativeStopped.ContinueWith(_ => RmlUiAndroidLauncherPreviewBarrier.Release(activation), TaskScheduler.Default);
    }
    private void DisposeNativeLauncher()
    {
        Guid activation = _nativeActivation;
        if (_nativeReadyHome != null) _nativeReadyHome.RmlUiReady -= TryActivateNativeLauncher;
        _nativeReadyHome = null;
        if (_nativeLauncherView != null)
        { _nativeLauncherView.Stop(); ReleaseNativePreviewAfter(_nativeLauncherView); }
        else if (_nativeLauncherGlOwned && _gameView != null) ReleaseNativePreviewAfter(_gameView);
        else if (_nativePreviewRetirement != null)
            _ = _nativePreviewRetirement.ContinueWith(_ => RmlUiAndroidLauncherPreviewBarrier.Release(activation), TaskScheduler.Default);
        else RmlUiAndroidLauncherPreviewBarrier.Release(activation);
        _nativeActivation = Guid.NewGuid();
        _nativeLauncherView = null; _nativeLocalAdmission = null; _nativeHomeReturn = null;
    }
}
#endif
