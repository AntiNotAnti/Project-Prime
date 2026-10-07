using System;
using System.Threading.Tasks;
using Android.Views;
using Android.Widget;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

public partial class MainActivity
{
    private AndroidRmlUiView? _rmlLauncher;
    private Task _rmlStop = Task.CompletedTask;
    private bool _rmlFailed, _rmlMatchSuspended;
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
            MenuSettings settings = GameState.LoadSettings();
            GameSettings.Apply(settings);
            if (GameFiles.Ready) GameFiles.ApplyPaths();
            string[] rooms = GameFiles.Ready ? System.Linq.Enumerable.ToArray(ThumbnailGenerator.MultiplayerRooms()) : Array.Empty<string>();
            _rmlMatchSuspended = false;
            _rmlLauncher = new(this, settings, rooms, StartMatch, OpenNativePlatformRoute, Finish,
                NativeLauncherFailed, OpenNativeConnectedLobby, PickNativeRomDocument);
            _content.AddView(_rmlLauncher, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _rmlLauncher.RequestFocus();
            _rmlLauncher.RequestApplyInsets();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[rmlui-android] native initialization failed: " + ex);
            NativeLauncherFailed(ex.Message);
        }
    }

    private Task RetireNativeLauncher()
    {
        if (_rmlLauncher is not { } view) return _rmlStop;
        _rmlLauncher = null;
        _rmlMatchSuspended = false;
        _rmlStop = view.StopAsync();
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
            _rmlStop = Task.CompletedTask;
            CancelPending(ex.Message);
        }
    }

    private void OpenNativePlatformRoute(RmlUiRouteArgument route)
    {
        // All supported Android routes are native documents. Preserve the
        // owner and report an unavailable extension rather than changing heads.
        Console.WriteLine("[rmlui-android] unavailable platform route " + route);
        _rmlLauncher?.Enqueue(session =>
        {
            session.Open(RmlUiRouteArgument.Home);
            session.Pages.SetText("system_status", "This route is unavailable on this Android build.");
        });
    }

    private void OpenNativeConnectedLobby(LaunchPlan plan)
    {
        // The retained native session pumps persistent lobbies itself. This
        // compatibility callback must never construct a second lobby owner.
        _rmlLauncher?.Navigate(RmlUiRouteArgument.Play);
    }

    private async void NativeLauncherFailed(string error)
    {
        if (_rmlFailed || _destroyed) return;
        _rmlFailed = true;
        Console.WriteLine("[rmlui-android] native launcher unavailable: " + error);
        bool retired = false;
        try { await RetireNativeLauncher(); retired = true; }
        catch (Exception ex) { Console.WriteLine("[rmlui-android] owner retirement failed: " + ex); }
        if (_destroyed || _content == null) return;
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetGravity(GravityFlags.Center);
        panel.SetBackgroundColor(Android.Graphics.Color.Rgb(10, 12, 16));
        var message = new TextView(this) { Text = retired
            ? "Project Prime could not initialize its native interface.\n" + error
            : "The native renderer could not shut down. Restart Project Prime." };
        message.SetTextColor(Android.Graphics.Color.White);
        message.Gravity = GravityFlags.Center;
        panel.AddView(message);
        if (retired)
        {
            var retry = new Android.Widget.Button(this) { Text = "RETRY NATIVE INTERFACE" };
            retry.Click += (_, _) =>
            {
                _content?.RemoveView(panel);
                _rmlFailed = false;
                BeginNativeLauncher();
            };
            panel.AddView(retry);
        }
        var quit = new Android.Widget.Button(this) { Text = "CLOSE PROJECT PRIME" };
        quit.Click += (_, _) => Finish();
        panel.AddView(quit);
        _content.AddView(panel, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    private void RestoreNativeLauncher(bool keepSession)
    {
        if (_rmlLauncher is { } view && _rmlMatchSuspended && _content != null)
        {
            _rmlMatchSuspended = false;
            _rmlStop = Task.CompletedTask;
            _content.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            view.ResumeAfterMatch();
            view.RequestFocus();
            view.RequestApplyInsets();
            return;
        }
        if (!_rmlFailed) BeginNativeLauncher();
    }

    private bool NativeLauncherBack()
    {
        if (_rmlLauncher == null) return false;
        _rmlLauncher.Back();
        return true;
    }
    private void PauseNativeLauncher(bool paused) => _rmlLauncher?.SetPaused(paused);
    private void DestroyNativeLauncher() => RetireNativeLauncher();
}
