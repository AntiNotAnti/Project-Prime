using System;
using System.Threading.Tasks;
#if MPHREAD_RMLUI_ANDROID
using Android.Views;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
#endif

namespace MphRead.Droid;

public partial class MainActivity
{
#if MPHREAD_RMLUI_ANDROID
    private AndroidRmlUiView? _rmlLauncher;
    private Task _rmlStop = Task.CompletedTask;
    private bool _rmlFallback, _rmlFailed, _rmlMatchSuspended;
    private bool NativeRmlOwnsGraphics => (_rmlLauncher != null && !_rmlMatchSuspended) || !_rmlStop.IsCompleted;
    private async void BeginNativeLauncher()
    {
        if (_destroyed || _content == null || InMatch || _pending != null || _rmlLauncher != null || _rmlFailed) return;
        try
        {
            await _rmlStop;
            if (_destroyed || InMatch || _pending != null || _rmlLauncher != null) return;
            _hunterStop = AndroidHunterShot.Current?.RetireAsync() ?? Task.CompletedTask;
            await _hunterStop;
            if (_destroyed || InMatch || _pending != null || _rmlLauncher != null) return;
            MenuSettings settings = GameState.LoadSettings(); GameSettings.Apply(settings);
            if (GameFiles.Ready) GameFiles.ApplyPaths();
            string[] rooms = GameFiles.Ready ? System.Linq.Enumerable.ToArray(ThumbnailGenerator.MultiplayerRooms()) : Array.Empty<string>();
            _rmlFallback = false; _rmlMatchSuspended = false;
            if (_launcherView != null) _launcherView.Visibility = ViewStates.Gone;
            Deck.Asleep = true; MovingBackdrop.Suspended = true;
            _rmlLauncher = new(this, settings, rooms, StartMatch, OpenNativePlatformRoute, Finish,
                error => NativeLauncherFailed(error), OpenNativeConnectedLobby, PickNativeRomDocument);
            _content.AddView(_rmlLauncher, new Android.Widget.FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _rmlLauncher.RequestFocus(); _rmlLauncher.RequestApplyInsets();
        }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] " + ex); NativeLauncherFailed(ex.Message); }
    }
    private Task RetireNativeLauncher()
    {
        var view = _rmlLauncher;
        if (view == null) return _rmlStop;
        _rmlLauncher = null; _rmlMatchSuspended = false;
        _rmlStop = view.StopAsync();
        // Removing a SurfaceView waits only for the owner to detach its window,
        // while full document/GPU retirement completes asynchronously.
        _content?.RemoveView(view);
        return _rmlStop;
    }
    private void RetireNativeLauncherForMatch()
    {
        if (_rmlLauncher is not { } view) return;
        _rmlMatchSuspended = true;
        _rmlStop = view.SuspendForMatchAsync();
        _content?.RemoveView(view);
        _rendererStop = Task.WhenAll(_rendererStop, ObserveNativeMatchHandoff(_rmlStop));
    }
    private async Task ObserveNativeMatchHandoff(Task handoff)
    {
        try { await handoff; }
        catch (AndroidRmlUiLaunchChangedException ex)
        {
            // Native documents/GPU were successfully retired; only the lobby
            // start witness became stale. Permit the retained controller to
            // resume and accept a later authoritative start.
            _rmlStop = Task.CompletedTask;
            CancelPending(ex.Message);
        }
    }
    private async void OpenNativePlatformRoute(RmlUiRouteArgument route)
    {
        try
        {
            await RetireNativeLauncher();
            if (_destroyed || InMatch || _pending != null) return;
            _rmlFallback = true;
            if (_launcherView != null) _launcherView.Visibility = ViewStates.Visible;
            Deck.Asleep = false; MovingBackdrop.Suspended = _renderingPreviews;
            Console.WriteLine("[rmlui-android] explicit platform presentation " + route);
            PrimeRoute platform = route switch
            {
                RmlUiRouteArgument.Play => PrimeRoute.Play,
                RmlUiRouteArgument.Offline => PrimeRoute.Offline,
                RmlUiRouteArgument.HunterLicense => PrimeRoute.HunterLicense,
                RmlUiRouteArgument.Theatre => PrimeRoute.Theatre,
                RmlUiRouteArgument.Settings => PrimeRoute.Settings,
                RmlUiRouteArgument.Community => PrimeRoute.Forge,
                _ => PrimeRoute.News
            };
            AndroidApp.Home?.OpenRouteFromRml(platform);
        }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] " + ex); NativeLauncherFailed(ex.Message); }
    }
    private async void OpenNativeConnectedLobby(LaunchPlan plan)
    {
        try
        {
            await RetireNativeLauncher();
            if (_destroyed || InMatch || _pending != null) return;
            _rmlFallback = true;
            if (_launcherView != null) _launcherView.Visibility = ViewStates.Visible;
            Deck.Asleep = false; MovingBackdrop.Suspended = _renderingPreviews;
            Console.WriteLine("[rmlui-android] native Play connected; explicit legacy lobby presentation");
            AndroidApp.Home?.OpenConnectedFromRml(plan);
        }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] " + ex); NativeLauncherFailed(ex.Message); }
    }
    private async void NativeLauncherFailed(string error)
    {
        _rmlFailed = true;
        Console.WriteLine("[rmlui-android] native launcher unavailable; restoring platform UI: " + error);
        try { await RetireNativeLauncher(); }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] failed owner retirement: " + ex); }
        if (_destroyed) return;
        if (_launcherView != null) _launcherView.Visibility = ViewStates.Visible;
        Deck.Asleep = false; MovingBackdrop.Suspended = _renderingPreviews;
        Android.Widget.Toast.MakeText(this, "Native UI unavailable: " + error, Android.Widget.ToastLength.Long)?.Show();
    }
    private void RestoreNativeLauncher(bool keepSession)
    {
        if (_rmlLauncher is { } view && _rmlMatchSuspended && _content != null)
        {
            _rmlMatchSuspended = false; _rmlStop = Task.CompletedTask;
            if (_launcherView != null) _launcherView.Visibility = ViewStates.Gone;
            Deck.Asleep = true; MovingBackdrop.Suspended = true;
            _content.AddView(view, new Android.Widget.FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            view.ResumeAfterMatch(); view.RequestFocus(); view.RequestApplyInsets();
            return;
        }
        if (!_rmlFailed) BeginNativeLauncher();
    }
    private bool NativeLauncherBack()
    {
        if (_rmlLauncher != null) { _rmlLauncher.Back(); return true; }
        if (_rmlFallback && !_rmlFailed) { BeginNativeLauncher(); return true; }
        return false;
    }
    private void PauseNativeLauncher(bool paused) => _rmlLauncher?.SetPaused(paused);
    private void DestroyNativeLauncher() => RetireNativeLauncher();
#else
    private bool NativeRmlOwnsGraphics => false;
    private void BeginNativeLauncher() { }
    private void RetireNativeLauncherForMatch() { }
    private void RestoreNativeLauncher(bool keepSession) { }
    private bool NativeLauncherBack() => false;
    private void PauseNativeLauncher(bool paused) { }
    private void DestroyNativeLauncher() { }
#endif
}
