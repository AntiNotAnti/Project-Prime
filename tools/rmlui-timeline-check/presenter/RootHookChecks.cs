using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;

internal static class RootHookChecks
{
    private static string Repository(){var path=new DirectoryInfo(Environment.CurrentDirectory);while(path!=null){if(Directory.Exists(Path.Combine(path.FullName,"src/MphRead")))return path.FullName;path=path.Parent;}throw new DirectoryNotFoundException();}
    internal static void Run()
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
        var backend=new Memory();var host=new RmlUiHost();var pages=new RmlUiPageManager(host);
        using var page=new TheatrePlaybackPagePresenter(host,pages,backend);var window=new MphRead.RenderWindow();var metrics=new TimelineMetrics(1280,720,1280,720,1);
        RmlUiOpenGlSmoke.AttachFixture(page);page.Send(TheatrePlaybackAction.CameraToolsOpen);page.Send(TheatrePlaybackAction.TimelineOpen);page.RefreshFixture();
        var ticket=page.PrepareTimeline(metrics,true)!;RmlUiOpenGlSmoke.QueueFixture(window,page,ticket,metrics);
        RmlUiOpenGlSmoke.BeginNativePresentation(window);RmlUiOpenGlSmoke.PublishNativeTimelinePresentedFrame(window);
        page.SetFocus("replay_timeline_key_1");page.Send(TheatrePlaybackAction.TimelineActivate);
        Check(page.CameraState.Focus?.Frame==10,"actual begin-render hook drops unpresented prior-frame ticket before any early return");
        page.RefreshFixture();ticket=page.PrepareTimeline(metrics,true)!;RmlUiOpenGlSmoke.QueueFixture(window,page,ticket,metrics);
        backend.Context=backend.Context with{Camera=backend.Context.Camera with{PresentationRevision=backend.Context.Camera.PresentationRevision+1}};
        RmlUiOpenGlSmoke.PublishNativeTimelinePresentedFrame(window);page.SetFocus("replay_timeline_key_1");page.Send(TheatrePlaybackAction.TimelineActivate);
        Check(page.CameraState.Focus?.Frame==20,"actual successful-swap hook accepts newer picture id with same paused pose/track/owner");
        page.ReleaseTimelineInput();page.RefreshFixture();ticket=page.PrepareTimeline(metrics,true)!;RmlUiOpenGlSmoke.QueueFixture(window,page,ticket,metrics);
        window.ClientSize=(640,360);RmlUiOpenGlSmoke.PublishNativeTimelinePresentedFrame(window);page.SetFocus("replay_timeline_key_2");page.Send(TheatrePlaybackAction.TimelineActivate);
        Check(page.CameraState.Focus?.Frame==20,"actual swap hook rejects changed client/framebuffer density before publishing DOM pointer authority");window.ClientSize=(1280,720);
        page.RefreshFixture();ticket=page.PrepareTimeline(metrics,true)!;RmlUiOpenGlSmoke.QueueFixture(window,page,ticket,metrics);
        backend.Context=backend.Context with{Camera=backend.Context.Camera with{Pose=backend.Context.Camera.Pose with{X=9}}};
        RmlUiOpenGlSmoke.PublishNativeTimelinePresentedFrame(window);
        Check(!page.CameraState.Current,"actual swap publication checks retained current pose after successful world camera publication");
        page.Send(TheatrePlaybackAction.TimelineRefresh);page.RefreshFixture();ticket=page.PrepareTimeline(metrics,true)!;RmlUiOpenGlSmoke.QueueFixture(window,page,ticket,metrics);
        window.IsFocused=false;RmlUiOpenGlSmoke.PublishNativeTimelinePresentedFrame(window);window.IsFocused=true;
        page.SetFocus("replay_timeline_key_2");page.Send(TheatrePlaybackAction.TimelineActivate);
        Check(page.CameraState.Focus?.Frame==20,"focus loss at presentation cannot later revive the old native marker ticket");
        string root=Path.Combine(Repository(),"src/MphRead");
        string renderer=File.ReadAllText(Path.Combine(root,"Renderer.cs"));int begin=renderer.IndexOf("RmlUiOpenGlSmoke.BeginNativePresentation(this)",StringComparison.Ordinal);
        Check(begin>renderer.IndexOf("protected override void OnRenderFrame",StringComparison.Ordinal)&&begin<renderer.IndexOf("Mods.Network.DemoRecorder.PollSaves",begin,StringComparison.Ordinal),"actual Renderer draft clears pending picture before world/launcher early branches");
        int swap=renderer.IndexOf("try { PresentFrame(); }",StringComparison.Ordinal),camera=renderer.IndexOf("Scene.PublishNativeReplayPresentedFrame();",swap,StringComparison.Ordinal),ui=renderer.IndexOf("PublishNativeTimelinePresentedFrame(this)",camera,StringComparison.Ordinal);
        Check(swap<camera&&camera<ui,"actual Renderer draft publishes world camera then exact queued timeline only after successful SwapBuffers");
        string overlay=File.ReadAllText(Path.Combine(root,"Mods/Launcher/RmlUi/Render/RmlUiGameplayOverlay.cs"));
        int update=overlay.IndexOf("            host.Update();",StringComparison.Ordinal),prepare=overlay.IndexOf("PrepareTimeline(",update,StringComparison.Ordinal),draw=overlay.IndexOf("RmlUiOpenGlFrame.Draw(host",prepare,StringComparison.Ordinal),queue=overlay.IndexOf("QueueReplayTimeline(",draw,StringComparison.Ordinal);
        Check(update<prepare&&prepare<draw&&draw<queue,"actual gameplay overlay draft prepares native post-update DOM and queues only after native draw returns");
        string input=File.ReadAllText(Path.Combine(root,"Mods/Launcher/RmlUi/Render/RmlUiOpenGlSmoke.cs"));
        int move=input.IndexOf("host.Input.PointerMoved(x,y);",StringComparison.Ordinal);
        Check(move<input.IndexOf("_playback.TimelinePointerMove(",move,StringComparison.Ordinal),"actual pointer draft updates native hover before presenter hit routing");
        int down=input.IndexOf("if(code>=0)host.Input.PointerButton(code,x,y,down);",StringComparison.Ordinal);
        Check(down<input.IndexOf("_playback.TimelinePointerButton(",down,StringComparison.Ordinal),"actual pointer draft delivers native click exactly once before ephemeral drag release");
        string release=File.ReadAllText(Path.Combine(root,"Mods/Launcher/RmlUi/Render/RmlUiNativeDesktopInput.cs"));
        Check(release.Contains("_pendingReplayTimeline=null;")&&release.Contains("_playback?.ReleaseTimelineInput();"),"actual focus/input release draft retires queued picture and drag state together");
        Console.WriteLine($"PASS {checks} private actual root timeline presentation hook/paused-picture/source integration checks; fake window, no GL");
    }
}

namespace MphRead
{
    internal sealed class RenderWindow
    {internal (int X,int Y) FramebufferSize=(1280,720),ClientSize=(1280,720);internal bool IsFocused=true;}
}
namespace MphRead.Mods.Launcher.RmlUi.Render
{
    internal static partial class RmlUiOpenGlSmoke
    {
        private static TheatrePlaybackPagePresenter? _playback;private static bool GameplayLeaseCurrent=true;
        private static float Density(MphRead.RenderWindow window)=>(float)window.FramebufferSize.X/window.ClientSize.X;
        internal static void AttachFixture(TheatrePlaybackPagePresenter page)=>_playback=page;
        internal static void QueueFixture(MphRead.RenderWindow window,TheatrePlaybackPagePresenter page,TimelineRenderTicket ticket,TimelineMetrics metrics)
            =>QueueReplayTimeline(window,page,ticket,metrics);
    }
}
