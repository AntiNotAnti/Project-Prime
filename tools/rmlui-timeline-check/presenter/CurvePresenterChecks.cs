using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
internal static class CurvePresenterChecks
{
    internal static void Run()
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        var metrics=new TimelineMetrics(1280,720,1280,720,1);
        var backend=new Memory();var host=new RmlUiHost();var pages=new RmlUiPageManager(host);using var page=new TheatrePlaybackPagePresenter(host,pages,backend);
        Check(page.Send(TheatrePlaybackAction.CameraToolsOpen),"curve fixture opens actual camera tools");page.RefreshFixture();
        string key="replay_camera_curve_key_3_0";page.SetFocus(key);
        Check(host.TryGetElementBounds(pages.Top,key,out float x,out float y,out float w,out float h),"actual authored key marker has host bounds");x+=w/2;y+=h/2;
        page.CameraCurvePointerButton(0,x,y,true,metrics,true);page.CameraCurvePointerMove(x,y-10,metrics,true);page.CameraCurvePointerButton(0,x,y-10,false,metrics,true);
        Check(page.CameraState.Pending==null&&backend.Writes==0,"unpresented graph cannot begin a mutation gesture");
        var ticket=page.PrepareCameraCurves(metrics,true);Check(ticket!=null&&page.CameraCurvesPresented(ticket,metrics,true),"successful presented graph ticket admits exact controls");
        page.SetFocus(key);page.CameraCurvePointerButton(0,x,y,true,metrics,true);page.CameraCurvePointerMove(x,y-10,metrics,true);page.CameraCurvePointerButton(0,x,y-10,false,metrics,true);
        Check(page.CameraState.Pending is{Kind:TheatreCameraToolsChange.Curve} pending&&pending.After[0].Pose.Fov>pending.Before[0].Pose.Fov&&backend.Writes==0,"key vertical drag stages actual FOV change without writing");
        page.Send(TheatrePlaybackAction.CameraToolsCancel);backend.Context=backend.Context with{Camera=backend.Context.Camera with{PresentationRevision=backend.Context.Camera.PresentationRevision+1}};page.Send(TheatrePlaybackAction.CameraToolsRefresh);page.RefreshFixture();
        string handle="replay_camera_curve_out_3";page.SetFocus(handle);host.TryGetElementBounds(pages.Top,handle,out x,out y,out w,out h);x+=w/2;y+=h/2;
        ticket=page.PrepareCameraCurves(metrics,true);page.CameraCurvesPresented(ticket!,metrics,true);
        page.CameraCurvePointerButton(0,x,y,true,metrics,true);page.CameraCurvePointerMove(x,y-8,metrics,true);page.CameraCurvePointerButton(0,x,y-8,false,metrics,true);
        Check(page.CameraState.Pending is{} handles&&handles.After[0].Pose.Interpolation==4&&handles.After[0].Pose.Handles?.FovOut>0&&backend.Writes==0,"outgoing graph handle stages real Bezier scalar controls");
        page.Send(TheatrePlaybackAction.CameraToolsCancel);backend.Context=backend.Context with{Camera=backend.Context.Camera with{PresentationRevision=backend.Context.Camera.PresentationRevision+1}};page.Send(TheatrePlaybackAction.CameraToolsRefresh);page.RefreshFixture();page.SetFocus(key);
        host.TryGetElementBounds(pages.Top,key,out x,out y,out w,out h);x+=w/2;y+=h/2;ticket=page.PrepareCameraCurves(metrics,true);page.CameraCurvesPresented(ticket!,metrics,true);
        page.CameraCurvePointerButton(0,x,y,true,metrics,true);page.CameraCurvePointerMove(x,y-8,metrics,true);host.ScrollY=7;page.CameraCurvePointerButton(0,x,y-8,false,metrics,true);
        Check(page.CameraState.Pending==null&&backend.Writes==0,"scrolling retires a held curve gesture without staging stale geometry");
        host.ScrollY=0;page.RefreshFixture();
        ticket=page.PrepareCameraCurves(metrics,true);Check(ticket!=null&&page.CameraCurvesPresented(ticket,metrics,true)&&!ticket.Lanes.IsDefaultOrEmpty,"box selection retains successfully presented lane geometry even on empty track areas");
        var lane=ticket!.Lanes.First(row=>row.Channel==3).Box;
        page.SetFocus("replay_camera_curve_lane_3");
        Check(page.CameraCurvePointerButton(0,lane.X+1,lane.Y+1,true,metrics,true,shift:true),"Shift press captures the exact presented lane");
        page.CameraCurvePointerMove(lane.X+lane.Width-1,lane.Y+lane.Height-1,metrics,true);
        Check(page.CameraState.Pending==null&&backend.Writes==0,"rectangle preview never stages or saves an edit");
        page.CameraCurvePointerButton(0,lane.X+lane.Width-1,lane.Y+lane.Height-1,false,metrics,true,shift:true);
        Check(page.CameraState.Selection.SequenceEqual(new uint[]{10,20,40})&&page.CameraState.Pending==null&&backend.Writes==0,"rectangle selects exact authored frames once without persisting");
        ticket=page.PrepareCameraCurves(metrics,true);page.CameraCurvesPresented(ticket!,metrics,true);page.SetFocus("replay_camera_curve_lane_3");
        page.CameraCurvePointerButton(0,lane.X+lane.Width-1,lane.Y+lane.Height-1,true,metrics,true,shift:true);
        page.CameraCurvePointerMove(lane.X+1,lane.Y+1,metrics,true);page.CameraCurvePointerButton(0,lane.X+1,lane.Y+1,false,metrics,true,shift:true);
        Check(page.CameraState.Selection.SequenceEqual(new uint[]{10,20,40}),"reverse rectangle has the same authored selection");
        ticket=page.PrepareCameraCurves(metrics,true);page.CameraCurvesPresented(ticket!,metrics,true);page.SetFocus("replay_camera_curve_lane_3");
        page.CameraCurvePointerButton(0,lane.X+lane.Width-8,lane.Y+1,true,metrics,true,shift:true);
        page.CameraCurvePointerMove(lane.X+lane.Width-1,lane.Y+lane.Height-1,metrics,true);page.CameraCurvePointerButton(0,lane.X+lane.Width-1,lane.Y+lane.Height-1,false,metrics,true,shift:true);
        Check(page.CameraState.Selection.IsEmpty&&backend.Writes==0,"empty rectangle clears selection with no save");
        ticket=page.PrepareCameraCurves(metrics,true);page.CameraCurvesPresented(ticket!,metrics,true);page.SetFocus("replay_camera_curve_lane_3");
        page.CameraCurvePointerButton(0,lane.X+1,lane.Y+1,true,metrics,true,shift:true);host.ScrollY=7;
        page.CameraCurvePointerButton(0,lane.X+lane.Width-1,lane.Y+lane.Height-1,false,metrics,true,shift:true);
        Check(page.CameraState.Selection.IsEmpty&&page.CameraState.Pending==null&&backend.Writes==0,"scroll retires rectangle geometry before selection mutation");
        host.ScrollY=0;page.RefreshFixture();ticket=page.PrepareCameraCurves(metrics,true);page.CameraCurvesPresented(ticket!,metrics,true);page.SetFocus("replay_camera_curve_lane_3");
        page.CameraCurvePointerButton(0,lane.X+1,lane.Y+1,true,metrics,true,shift:true);backend.FilesValid=false;
        page.CameraCurvePointerButton(0,lane.X+lane.Width-1,lane.Y+lane.Height-1,false,metrics,true,shift:true);
        Check(page.CameraState.Selection.IsEmpty&&!page.CameraState.Current&&backend.Writes==0,"strong source witness rejects selection release after sidecar replacement");
        Console.WriteLine($"PASS {checks} actual curve presenter/gesture/source-model checks; host bounds seam only");
    }
}
