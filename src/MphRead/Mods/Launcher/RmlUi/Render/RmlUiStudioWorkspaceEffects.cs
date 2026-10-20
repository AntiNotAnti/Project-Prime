#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private sealed record StudioProjectImport(TheatrePlaybackPagePresenter Page,
        NativeReplayPageLease Lease, CancellationTokenSource Cancellation, Task<string> Work);
    private static StudioProjectImport? _studioProjectImport;
    private static string? _studioProjectReady;
    private static (NativeFullSettingsPage Owner, Task<string?> Work)? _arenaPicker;

    private static bool PumpNativeStudioWorkspaceRequests(RenderWindow window)
    {
        if (!Shell.StudioWindow || _playback is not { } page || !NativeReplayLeaseCurrent(window)) return false;
        if (page.TryTakeStudioSettingsRequest())
        {
            ReleaseNativeInput();
            TryOpenNativeFullSettings(window);
            return true;
        }
        if (_studioProjectImport is { } pending)
        {
            if (!ReferenceEquals(page,pending.Page) || !ReferenceEquals(_nativeReplayLease,pending.Lease))
            { RetireNativeStudioProjectImport(); return false; }
            if (!pending.Work.IsCompleted) return false;
            _studioProjectImport=null;
            try
            {
                string path=pending.Work.GetAwaiter().GetResult();
                // Adopt only after the existing Shell scene teardown. The current
                // replay and its source/sidecars remain owned until this point.
                CloseNativePlayback();
                ReleaseNativeInput();
                _studioProjectReady=path;
                Shell.RequestEndMatch();
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or OperationCanceledException)
            { _controller?.ReportStudioLaunch(false,error.Message); }
            finally { pending.Cancellation.Dispose(); }
            return true;
        }
        if (page.TryTakeStudioOpenRequest(out string selected) && _nativeReplayLease is { } lease)
        {
            var cancellation=new CancellationTokenSource();
            _studioProjectImport=new(page,lease,cancellation,RmlUiReplayProjectLibrary.ImportAsync(selected,cancellation.Token));
        }
        return false;
    }

    private static bool PumpNativeStudioProjectOpen(RenderWindow window)
    {
        if (_studioProjectReady is not { } path || window.HasScene || _controller==null) return false;
        _studioProjectReady=null;
        _controller.ReplayPlaybackClosed();
        _controller.OpenStudioReplayPath(path);
        return true;
    }

    private static void RetireNativeStudioProjectImport()
    {
        var pending=_studioProjectImport;_studioProjectImport=null;
        if(pending==null)return;
        pending.Cancellation.Cancel();pending.Cancellation.Dispose();
        // Observe worker faults without touching native state from a worker.
        _=pending.Work.ContinueWith(task=>_ = task.Exception,CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }

    private static void StartNativeArenaPicker(NativeFullSettingsPage settings)
    {
        RetireNativeArenaPicker();
        try
        {
            if(!NativeFilePicker.Available)throw new InvalidOperationException("The native file picker is unavailable.");
            _arenaPicker=(settings,NativeFilePicker.OpenFile("Import from locally owned Prime or Echoes media","GameCube disc image","iso","gcm","rvz"));
        }
        catch(Exception error)when(error is IOException or InvalidOperationException)
        { settings.BeginArenaImport(null);_controller?.ReportStudioLaunch(false,error.Message); }
    }

    private static void PumpNativeArenaPicker(NativeFullSettingsPage settings)
    {
        if(_arenaPicker is not { } pending)return;
        if(!ReferenceEquals(pending.Owner,settings)){RetireNativeArenaPicker();return;}
        if(!pending.Work.IsCompleted)return;
        _arenaPicker=null;
        try { settings.BeginArenaImport(pending.Work.GetAwaiter().GetResult()); }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { settings.BeginArenaImport(null);_controller?.ReportStudioLaunch(false,error.Message); }
    }

    private static void RetireNativeArenaPicker()
    {
        var pending=_arenaPicker;_arenaPicker=null;
        if(pending!=null)_=pending.Value.Work.ContinueWith(task=>_ = task.Exception,CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
}
#endif
