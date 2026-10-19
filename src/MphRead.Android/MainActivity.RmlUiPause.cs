#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Views;

namespace MphRead.Droid;

public partial class MainActivity
{
    internal const string NativePauseIntentExtra = "projectprime.rmlui.pause";
    private CancellationTokenSource? _nativeAssetCancellation;
    private Task<string>? _nativeAssetRoot;
    private volatile bool _nativePauseDestroyed;
    private bool _nativePauseForeground;
    private (GameView View, RmlUiAndroidPauseSnapshot Snapshot)? _nativePauseDeferred;

    private void PrepareNativePauseAssets()
    {
        if (Intent?.GetBooleanExtra(NativePauseIntentExtra, false) != true
            && Intent?.GetBooleanExtra(NativeLauncherIntentExtra, false) != true) return;
        string? cache = ApplicationContext?.CacheDir?.AbsolutePath;
        var assets = ApplicationContext?.Assets;
        if (cache == null || assets == null) return;
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _nativeAssetCancellation = cancellation;
        string ownedCache = Path.Combine(cache, "native-rmlui");
        _nativeAssetRoot = Task.Run(() => RmlUiAndroidApkAssets.Extract(
            new RmlUiAndroidPackageAssets(assets), ownedCache, cancellation: token).Root, token);
        // Observe worker failure without publishing into an Activity that may
        // already be gone. A failed/not-yet-ready package uses existing pause.
        _ = _nativeAssetRoot.ContinueWith(task => Console.WriteLine(
            "[android/rmlui] packaged pause assets unavailable: " + task.Exception?.GetBaseException().Message),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }
    private RmlUiAndroidPauseOptions? CreateNativePauseOptions()
        => _nativeAssetRoot == null ? null : new(_nativeAssetRoot,
            Resources?.DisplayMetrics?.Density ?? 1, NativePauseNotification);

    private void NativePauseNotification(GameView view, RmlUiAndroidPauseSnapshot notification)
    {
        if (_nativePauseDestroyed) return;
        try { RunOnUiThread(() =>
        {
            if (!NativePauseCallbackCurrent(view, notification)) return;
            if (!_nativePauseForeground) { _nativePauseDeferred = (view, notification); return; }
            ApplyNativePauseNotification(view, notification);
        }); }
        catch (ObjectDisposedException) { /* Activity already retired; no route effect remains valid. */ }
        catch (Java.Lang.IllegalStateException) when (_nativePauseDestroyed) { }
    }
    private bool NativePauseCallbackCurrent(GameView view, RmlUiAndroidPauseSnapshot notification)
        => !_nativePauseDestroyed && ReferenceEquals(_gameView, view) && view.NativePauseCurrent(notification);
    private void ApplyNativePauseNotification(GameView view, RmlUiAndroidPauseSnapshot notification)
    {
        if (!NativePauseCallbackCurrent(view, notification)) return;
        _nativePauseDeferred = null;
        if (notification.State == RmlUiAndroidPauseState.Open)
        {
            _controls.ReleaseEverything(); HideEndPanel(); ShowSoftKeyboard(false);
            if (_overlay != null) _overlay.Visibility = ViewStates.Gone;
            if (_launcherView != null) _launcherView.Visibility = ViewStates.Gone;
            view.TranslationX = 0; view.RequestFocus();
            MphRead.Mods.Input.GamepadContexts.MenuVisible = true;
            MphRead.Mods.Launcher.Gui.Deck.Asleep = true;
            GoImmersive(true);
            return;
        }
        CloseNativePauseUi();
        // The notification itself is the current route/request witness and
        // this exact GameView still belongs to this Activity. No delayed old
        // Scene callback can open a page over a replacement match.
        switch (notification.State)
        {
            case RmlUiAndroidPauseState.Settings: OpenLegacyPauseMenu(openSettings: true); break;
            case RmlUiAndroidPauseState.Replay: ShowReplayEditor(); break;
            case RmlUiAndroidPauseState.Leave: EndMatch(); break;
            case RmlUiAndroidPauseState.Quit: Finish(); break;
            case RmlUiAndroidPauseState.Fallback: OpenLegacyPauseMenu(); break;
        }
    }
    private void CloseNativePauseUi()
    {
        if (_launcherView != null) _launcherView.Visibility = ViewStates.Gone;
        MphRead.Mods.Input.GamepadContexts.MenuVisible = false;
        MphRead.Mods.Launcher.Gui.Deck.Asleep = true;
        if (_gameView != null) _gameView.TranslationX = 0;
        if (_overlay != null) { _overlay.Visibility = ViewStates.Visible; _overlay.BringToFront(); }
        _controls.ReleaseEverything(); _controls.ReloadSettings();
        GoImmersive(true);
    }
    private void NativePauseForeground(bool foreground)
    {
        _nativePauseForeground = foreground;
        if (foreground) { ResumeNativeHomeReturn(); TryActivateNativeLauncher(); }
        if (foreground && _nativePauseDeferred is { } deferred)
        {
            _nativePauseDeferred = null;
            ApplyNativePauseNotification(deferred.View, deferred.Snapshot);
        }
    }
    private void DisposeNativePausePreparation()
    {
        _nativePauseDestroyed = true; _nativePauseDeferred = null;
        _nativeAssetCancellation?.Cancel(); _nativeAssetCancellation?.Dispose();
        _nativeAssetCancellation = null;
    }
}
#endif
