#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed record StudioWaveEffect(RenderWindow Window,object Lease,TheatrePlaybackPagePresenter Page,
        StudioWaveFileRequest Request,CancellationTokenSource Cancellation,Task<string?> Task);
    private static StudioWaveEffect? _studioWaveEffect;
    private static void PumpNativeStudioWaveChooser(RenderWindow window)
    {
        if(_studioWaveEffect is {} effect)
        {
            bool current=Shell.StudioWindow&&ReferenceEquals(window,effect.Window)&&ReferenceEquals(_playback,effect.Page)
                &&ReferenceEquals(_nativeReplayLease,effect.Lease)&&NativeReplayLeaseCurrent(window)
                &&effect.Page.StudioWaveRequestCurrent(effect.Request);
            if(!current){RetireNativeStudioWaveChooser();return;}
            if(!effect.Task.IsCompleted)return;
            _studioWaveEffect=null;
            try
            {
                string? selected=effect.Task.GetAwaiter().GetResult();
                effect.Page.CompleteStudioWaveRequest(effect.Request,selected,null);
            }
            catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
            {effect.Page.CompleteStudioWaveRequest(effect.Request,null,error is OperationCanceledException?null:error.Message);}
            finally{effect.Cancellation.Dispose();}
            return;
        }
        if(!Shell.StudioWindow||_nativeReplayLease==null||!NativeReplayLeaseCurrent(window)||_playback is not {} page
            ||!page.TryTakeStudioWaveRequest(out var request)||request==null)return;
        var cancellation=new CancellationTokenSource();
        try
        {
            var task=NativeStudioAudioFilePicker.Choose(request.Target,cancellation.Token);
            _studioWaveEffect=new(window,_nativeReplayLease,page,request,cancellation,task);
        }
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
        {cancellation.Dispose();page.CompleteStudioWaveRequest(request,null,error.Message);}
    }
    private static void RetireNativeStudioWaveChooser()
    {
        var old=_studioWaveEffect;_studioWaveEffect=null;
        if(old!=null)
        {
            old.Cancellation.Cancel();old.Cancellation.Dispose();old.Page.CancelStudioWaveRequests();
            _=old.Task.ContinueWith(completed=>{_=completed.Exception;},TaskScheduler.Default);
        }
        _playback?.CancelStudioWaveRequests();
    }
}
#endif
