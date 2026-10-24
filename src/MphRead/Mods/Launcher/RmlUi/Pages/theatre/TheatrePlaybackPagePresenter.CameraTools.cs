#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public sealed partial class TheatrePlaybackPagePresenter
{
    private TheatreCameraToolsController? _cameraTools;
    private RmlUiDocumentToken _cameraToolsModal,_cameraToolsConfirmation;
    private string _cameraToolsProblem="";
    private bool HandleCameraToolsIntent(in RmlUiIntent intent)
    {
        if(!IsOpen||_cameraBackend is not ITheatreCameraToolsBackend backend||!Enum.IsDefined((TheatrePlaybackAction)intent.Argument))return false;
        var action=(TheatrePlaybackAction)intent.Argument;
        bool opening=action==TheatrePlaybackAction.CameraToolsOpen&&_cameraToolsModal==default
            &&(intent.Document==_document||_cameraModal!=default&&intent.Document==_cameraModal);
        bool confirming=_cameraToolsConfirmation!=default&&intent.Document==_cameraToolsConfirmation
            &&action is TheatrePlaybackAction.CameraToolsConfirm or TheatrePlaybackAction.CameraToolsCancel;
        bool editing=_cameraToolsConfirmation==default&&_cameraToolsModal!=default&&intent.Document==_cameraToolsModal
            &&(action is TheatrePlaybackAction.CameraToolsDiscard or TheatrePlaybackAction.CameraToolsSave or TheatrePlaybackAction.CameraToolsUndo or TheatrePlaybackAction.CameraToolsRedo or TheatrePlaybackAction.CameraToolsTransform or TheatrePlaybackAction.CameraToolsHandles or TheatrePlaybackAction.CameraToolsCurveKey||intent.Argument>=(int)TheatrePlaybackAction.CameraToolsOrientationCurve&&intent.Argument<=(int)TheatrePlaybackAction.CameraToolsRollEase||intent.Argument>=(int)TheatrePlaybackAction.CameraToolsRefresh&&intent.Argument<=(int)TheatrePlaybackAction.CameraToolsNearest)
            &&action is not(TheatrePlaybackAction.CameraToolsConfirm or TheatrePlaybackAction.CameraToolsCancel);
        if((!opening&&!confirming&&!editing)||!_pages.Accept(intent))return false;
        try
        {
            _cameraToolsProblem="";
            if(opening)
            {
                Viewport.Release();_cameraTools?.Dispose();_cameraTools=new(backend);
                _cameraCurveReview=null;_cameraToolsModal=_pages.OpenModal(new("replay-camera-tools","pages/theatre/camera-tools.rml","replay_camera_tools_refresh"));
                _host.SetField(_cameraToolsModal,"replay_camera_tools_destination",_cameraTools.Snapshot().Destination.ToString(CultureInfo.InvariantCulture));
            }
            else if(_cameraTools!=null)switch(action)
            {
                case TheatrePlaybackAction.CameraToolsOrientationCurve:StageCameraTools(TheatreCameraToolsChange.OrientationCurve);break;
                case TheatrePlaybackAction.CameraToolsOrientationEase:StageCameraTools(TheatreCameraToolsChange.OrientationEase);break;
                case TheatrePlaybackAction.CameraToolsFovCurve:StageCameraTools(TheatreCameraToolsChange.FovCurve);break;
                case TheatrePlaybackAction.CameraToolsFovEase:StageCameraTools(TheatreCameraToolsChange.FovEase);break;
                case TheatrePlaybackAction.CameraToolsRollCurve:StageCameraTools(TheatreCameraToolsChange.RollCurve);break;
                case TheatrePlaybackAction.CameraToolsRollEase:StageCameraTools(TheatreCameraToolsChange.RollEase);break;
                case TheatrePlaybackAction.CameraToolsCurveKey:ActivateCameraCurveKey();break;
                case TheatrePlaybackAction.CameraToolsHandles:
                    if(_cameraTools.Handles(ReadCameraHandles()))StageCameraTools(TheatreCameraToolsChange.Handles);break;
                case TheatrePlaybackAction.CameraToolsTransform:
                    if(_cameraTools.Transform(ReadCameraTransform()))StageCameraTools(TheatreCameraToolsChange.Transform);break;
                case TheatrePlaybackAction.CameraToolsDiscard:_cameraTools.Discard();break;
                case TheatrePlaybackAction.CameraToolsSave:_cameraTools.Save();break;
                case TheatrePlaybackAction.CameraToolsUndo:_cameraTools.Undo();break;
                case TheatrePlaybackAction.CameraToolsRedo:_cameraTools.Redo();break;
                case TheatrePlaybackAction.CameraToolsRefresh:_cameraTools.Refresh();break;
                case TheatrePlaybackAction.CameraToolsPrevious:_cameraTools.MoveFocus(-1);break;
                case TheatrePlaybackAction.CameraToolsNext:_cameraTools.MoveFocus(1);break;
                case TheatrePlaybackAction.CameraToolsToggleSelection:_cameraTools.ToggleSelection();break;
                case TheatrePlaybackAction.CameraToolsRangeSelection:_cameraTools.SelectRange();break;
                case TheatrePlaybackAction.CameraToolsSelectAll:_cameraTools.SelectAll();break;
                case TheatrePlaybackAction.CameraToolsClearSelection:_cameraTools.ClearSelection();break;
                case TheatrePlaybackAction.CameraToolsNearest:_cameraTools.Nearest();break;
                case TheatrePlaybackAction.CameraToolsCopy:_cameraTools.Copy();break;
                case TheatrePlaybackAction.CameraToolsMove:StageCameraTools(TheatreCameraToolsChange.Move);break;
                case TheatrePlaybackAction.CameraToolsPaste:StageCameraTools(TheatreCameraToolsChange.Paste);break;
                case TheatrePlaybackAction.CameraToolsDuplicate:StageCameraTools(TheatreCameraToolsChange.Duplicate);break;
                case TheatrePlaybackAction.CameraToolsSnapEvent:StageCameraTools(TheatreCameraToolsChange.SnapEvent);break;
                case TheatrePlaybackAction.CameraToolsSnapKill:StageCameraTools(TheatreCameraToolsChange.SnapKill);break;
                case TheatrePlaybackAction.CameraToolsSnapBookmark:StageCameraTools(TheatreCameraToolsChange.SnapBookmark);break;
                case TheatrePlaybackAction.CameraToolsInterpolation:StageCameraTools(TheatreCameraToolsChange.Interpolation);break;
                case TheatrePlaybackAction.CameraToolsEase:StageCameraTools(TheatreCameraToolsChange.Ease);break;
                case TheatrePlaybackAction.CameraToolsLookAt:StageCameraTools(TheatreCameraToolsChange.LookAt);break;
                case TheatrePlaybackAction.CameraToolsRemoveSelected:StageCameraTools(TheatreCameraToolsChange.Remove);break;
                case TheatrePlaybackAction.CameraToolsConstantSpeed:_cameraTools.Motion(TheatreCameraMotionChange.ConstantSpeed);break;
                case TheatrePlaybackAction.CameraToolsCollision:_cameraTools.Motion(TheatreCameraMotionChange.Collision);break;
                case TheatrePlaybackAction.CameraToolsDirector:_cameraTools.Motion(TheatreCameraMotionChange.Director);break;
                case TheatrePlaybackAction.CameraToolsPreview:_cameraTools.Preview();break;
                case TheatrePlaybackAction.CameraToolsConfirm:
                    if(_cameraTools.Confirm())
                    {
                        if(TimelineOpen)_timelinePublicationNotice=_cameraTools.Snapshot().Status;
                        if(_pages.CloseModal())_cameraToolsConfirmation=default;
                    }
                    break;
                case TheatrePlaybackAction.CameraToolsCancel:BackCameraTools();break;
                case TheatrePlaybackAction.CameraToolsClose:CloseCameraTools();break;
            }
        }
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_cameraToolsProblem=error.Message;}
        PresentCameraTools();return true;
    }
    private void StageCameraTools(TheatreCameraToolsChange kind)
    {
        if(_cameraTools==null)return;
        if(kind is TheatreCameraToolsChange.Move or TheatreCameraToolsChange.Paste
            &&!_cameraTools.Destination(_host.ReadField(_cameraToolsModal,"replay_camera_tools_destination")))return;
        if(_cameraTools.MemoryEditing){_cameraTools.Apply(kind);return;}
        if(_cameraTools.Stage(kind))_cameraToolsConfirmation=_pages.OpenModal(new("replay-camera-tools-confirm","pages/theatre/camera-tools-confirm.rml","replay_camera_tools_confirm_cancel"));
    }
    private void PresentCameraTools()
    {
        if(_cameraToolsConfirmation!=default&&!_host.IsAlive(_cameraToolsConfirmation)){_cameraToolsConfirmation=default;_cameraTools?.Cancel();}
        if(_cameraToolsModal!=default&&!_host.IsAlive(_cameraToolsModal)){_cameraToolsModal=default;_cameraTools?.Dispose();_cameraTools=null;}
        if(_cameraToolsModal==default||_cameraTools==null)return;
        var state=_cameraTools.Snapshot();var camera=state.Context.Camera;bool keys=state.Focus!=null,selected=!state.Selection.IsEmpty;
        _host.SetText(_cameraToolsModal,"replay_camera_tools_replay",Path.GetFileName(camera.LogicalPath));
        _host.SetText(_cameraToolsModal,"replay_camera_tools_review",$"REVIEWED PLAYHEAD {camera.Frame} // {camera.Keys.Length} / 1024 KEYS // {state.ClipboardCount} COPIED");
        _host.SetText(_cameraToolsModal,"replay_camera_tools_focus",state.Focus is{} key?$"FOCUS {state.FocusIndex+1} / {camera.Keys.Length}\n"+DescribeToolsKey(key):"NO SAVED CAMERA KEYS");
        _host.SetText(_cameraToolsModal,"replay_camera_tools_selection","SELECTED FRAMES: "+(selected?string.Join(", ",state.Selection):"NONE"));
        _host.SetText(_cameraToolsModal,"replay_camera_tools_speed","CONSTANT SPEED: "+(state.Context.Motion.ConstantSpeed?"ON":"OFF"));
        _host.SetText(_cameraToolsModal,"replay_camera_tools_collision","PATH COLLISION: "+(state.Context.Motion.Collision?"ON":"OFF"));
        _host.SetText(_cameraToolsModal,"replay_camera_tools_director","DIRECTOR: "+(state.Context.Motion.Director?"ON":"OFF"));
        PresentCameraChannels(state);
        PresentCameraCurves(state.Context);
        _host.SetText(_cameraToolsModal,"replay_camera_tools_status",state.Status+(state.Dirty?" // UNSAVED":" // SAVED")+$" // UNDO {state.UndoCount} / REDO {state.RedoCount}");
        _host.SetText(_cameraToolsModal,"replay_camera_tools_error",_cameraToolsProblem.Length>0?_cameraToolsProblem:state.Error.Length>0?state.Error:!state.Current?"Pause, wait for a new picture and refresh before editing.":"");
        foreach(string id in new[]{"previous","next","toggle","range","all","nearest","preview"})_host.SetBool(_cameraToolsModal,"disabled:replay_camera_tools_"+id,!state.Current||!keys);
        foreach(string id in new[]{"move","copy","snap_event","snap_kill","snap_bookmark","interpolation","ease","look_at","remove","transform","handles"})_host.SetBool(_cameraToolsModal,"disabled:replay_camera_tools_"+id,!state.Current||!selected);
        foreach(string id in new[]{"speed","collision","director","duplicate"})_host.SetBool(_cameraToolsModal,"disabled:replay_camera_tools_"+id,!state.Current);
        _host.SetBool(_cameraToolsModal,"disabled:replay_camera_tools_paste",!state.Current||state.ClipboardCount==0);
        _host.SetBool(_cameraToolsModal,"disabled:replay_camera_tools_timeline",!state.Current||state.Pending!=null||_editorBackend==null);
        PresentCameraCurveKeys(state);
        if(_cameraToolsConfirmation==default)return;
        _host.SetText(_cameraToolsConfirmation,"replay_camera_tools_confirm_title",state.Pending?.Kind switch { TheatreCameraToolsChange.Curve=>"APPLY THIS REVIEWED CURVE DRAG?",TheatreCameraToolsChange.Handles=>"APPLY THESE BEZIER HANDLES?",TheatreCameraToolsChange.Transform=>"TRANSFORM THESE SELECTED KEYS?",TheatreCameraToolsChange.Move=>"MOVE THESE SELECTED KEYS?",TheatreCameraToolsChange.Paste=>"PASTE THESE COPIED KEYS?",TheatreCameraToolsChange.Duplicate=>"DUPLICATE THIS SAVED KEY?",TheatreCameraToolsChange.Remove=>"REMOVE THESE SELECTED KEYS?",TheatreCameraToolsChange.Interpolation=>"CHANGE SELECTED INTERPOLATION?",TheatreCameraToolsChange.Ease=>"CHANGE SELECTED EASING?",TheatreCameraToolsChange.LookAt=>"CHANGE SELECTED LOOK-AT TARGETS?",_=>"SAVE THIS REVIEWED CAMERA SNAP?" });
        _host.SetText(_cameraToolsConfirmation,"replay_camera_tools_confirm_before",state.Pending==null?"No edit is pending.":string.Join("\n\n",state.Pending.Before.Select(DescribeToolsKey)));
        _host.SetText(_cameraToolsConfirmation,"replay_camera_tools_confirm_after",state.Pending==null?"":string.Join("\n\n",state.Pending.After.Select(DescribeToolsKey)));
        _host.SetText(_cameraToolsConfirmation,"replay_camera_tools_confirm_error",_cameraToolsProblem.Length>0?_cameraToolsProblem:state.Error);
        _host.SetBool(_cameraToolsConfirmation,"disabled:replay_camera_tools_confirm_apply",!state.Current||state.Pending==null);
    }
    private TheatreCameraToolsContext? _cameraCurveReview;
    private void PresentCameraCurves(TheatreCameraToolsContext context)
    {
        if(ReferenceEquals(_cameraCurveReview,context))return;_cameraCurveReview=context;
        ReleaseCameraCurveInput();
        for(int channel=0;channel<5;channel++)
        {
            float Value(TheatreCameraCurvePoint p)=>channel switch{0=>p.X,1=>p.Y,2=>p.Z,3=>p.Fov,_=>p.Roll};
            var values=context.Curve.Select(Value).ToArray();var keyValues=context.Camera.Keys.Select(k=>CurveValue(k.Pose,channel));
            var domain=values.Concat(keyValues).ToArray();float min=domain.Length==0?0:domain.Min(),max=domain.Length==0?0:domain.Max();
            _curveRanges[channel]=(min,Math.Max(min+1,max));
            _host.SetText(_cameraToolsModal,"replay_camera_curve_bounds_"+channel,$"{new[]{"X","Y","Z","FOV","ROLL"}[channel]} // {min:0.###}…{max:0.###} // FRAMES 0–{context.Camera.Duration}");
            for(int i=0;i<64;i++)
            {
                int height=i>=values.Length?0:(int)Math.Round((values[i]-min)/Math.Max(.00001f,max-min)*15);
                _host.SetBool(_cameraToolsModal,$"visible:replay_camera_curve_{channel}_{i}",i<values.Length);
                for(int level=0;level<16;level++)_host.SetBool(_cameraToolsModal,$"class:replay_camera_curve_{channel}_{i}:curve_y_{level}",height==level);
            }
        }
        var speed=new float[context.Curve.Length];
        for(int i=1;i<context.Curve.Length;i++)
        {
            var a=context.Curve[i-1];var b=context.Curve[i];float dt=(b.Frame-a.Frame)/60f;
            if(dt>0)speed[i]=MathF.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y)+(b.Z-a.Z)*(b.Z-a.Z))/dt;
        }
        float peak=speed.Length==0?0:speed.Max();
        _host.SetText(_cameraToolsModal,"replay_camera_curve_speed_bounds",$"SPEED // 0…{peak:0.###} WORLD UNITS / SECOND");
        for(int i=0;i<64;i++)
        {
            _host.SetBool(_cameraToolsModal,"visible:replay_camera_curve_speed_"+i,i<speed.Length);
            int level=i<speed.Length?(int)Math.Round(speed[i]/Math.Max(.00001f,peak)*15):0;
            for(int y=0;y<16;y++)_host.SetBool(_cameraToolsModal,$"class:replay_camera_curve_speed_{i}:curve_y_{y}",y==level);
        }
    }
    private TheatreCameraHandles ReadCameraHandles()
    {
        float[] Values(string id,int count)
        {
            var parts=_host.ReadField(_cameraToolsModal,"replay_camera_handles_"+id).Split(',');
            if(parts.Length!=count)throw new ArgumentException("Enter three vector values or four scalar values, separated by commas.");
            return parts.Select(value=>float.TryParse(value.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out float result)&&float.IsFinite(result)?result:throw new ArgumentException("Enter finite handle values.")).ToArray();
        }
        TheatreCameraVector? Vector(string id)
        {if(_host.ReadField(_cameraToolsModal,"replay_camera_handles_"+id).Trim().Equals("auto",StringComparison.OrdinalIgnoreCase))return null;var v=Values(id,3);return new(v[0],v[1],v[2]);}
        var scalar=Values("scalar",4);return new(Vector("in"),Vector("out"),scalar[0],scalar[1],scalar[2],scalar[3]);
    }
    private TheatreCameraTransform ReadCameraTransform()
    {
        float Value(string id)
        {
            if(!float.TryParse(_host.ReadField(_cameraToolsModal,"replay_camera_transform_"+id),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value))
                throw new ArgumentException("Enter finite camera transform values using decimal points.");
            return value;
        }
        if(!int.TryParse(_host.ReadField(_cameraToolsModal,"replay_camera_transform_frame"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int frame))
            throw new ArgumentException("Enter a whole frame offset.");
        return new(Value("x"),Value("y"),Value("z"),Value("yaw"),Value("pitch"),Value("roll"),frame,Value("fov"),Value("lens_roll"));
    }
    private static string DescribeToolsKey(TheatreCameraKey key)=>$"FRAME {key.Frame} // {CameraPose(key.Pose)}\nINTERPOLATION {new[]{"LINEAR","SMOOTH","SPLINE","HOLD","BEZIER"}[key.Pose.Interpolation]} // EASE {new[]{"NONE","IN","OUT","IN OUT"}[key.Pose.Ease]} // LOOK AT {(key.Pose.LookAt < 0 ? "NONE" : "PLAYER " + (key.Pose.LookAt + 1).ToString(CultureInfo.InvariantCulture))}"+DescribeCameraHandles(key.Pose.Handles)+DescribeCameraChannels(key.Pose.Channels)+(key.Pose.LookAt>=0?"\nLOOK-AT OVERRIDES AUTHORED ORIENTATION. TARGET SWITCHES AND RECORDED MOTION MAY PRODUCE ABRUPT TURNS.":"");
    private static string ChannelMode(int value)=>new[]{"INHERIT","LINEAR","SMOOTH","BEZIER","HOLD"}[value];
    private static string ChannelEase(int value)=>new[]{"INHERIT","NONE","IN","OUT","IN OUT"}[value];
    private static string DescribeCameraChannels(TheatreCameraChannelCurves? value)
    {
        var c=value??new(0,0,0,0,0,0);
        return $"\nORIENTATION {ChannelMode(c.Orientation)} / {ChannelEase(c.OrientationEase)} // FOV {ChannelMode(c.Fov)} / {ChannelEase(c.FovEase)} // ROLL {ChannelMode(c.Roll)} / {ChannelEase(c.RollEase)}";
    }
    private void PresentCameraChannels(TheatreCameraToolsSnapshot state)
    {
        var c=state.Focus?.Pose.Channels??new(0,0,0,0,0,0);
        string[] ids={"orientation_curve","orientation_ease","fov_curve","fov_ease","roll_curve","roll_ease"};
        string[] labels={"ORIENTATION: "+ChannelMode(c.Orientation),"ORIENTATION EASE: "+ChannelEase(c.OrientationEase),
            "FOV: "+ChannelMode(c.Fov),"FOV EASE: "+ChannelEase(c.FovEase),"ROLL: "+ChannelMode(c.Roll),"ROLL EASE: "+ChannelEase(c.RollEase)};
        for(int i=0;i<ids.Length;i++)
        {
            _host.SetText(_cameraToolsModal,"replay_camera_tools_"+ids[i],labels[i]);
            _host.SetBool(_cameraToolsModal,"disabled:replay_camera_tools_"+ids[i],!state.Current||state.Selection.IsEmpty||state.Pending!=null);
        }
    }
    private static string DescribeCameraHandles(TheatreCameraHandles? handles)
    {
        if(handles==null)return "\nHANDLES AUTO // FOV/ROLL OFFSETS 0";
        string Vector(TheatreCameraVector? value)=>value==null?"AUTO":string.Join(",",new[]{value.X,value.Y,value.Z}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        return "\nIN "+Vector(handles.Incoming)+" // OUT "+Vector(handles.Outgoing)+"\nFOV IN/OUT "+handles.FovIn.ToString("R",CultureInfo.InvariantCulture)+"/"+handles.FovOut.ToString("R",CultureInfo.InvariantCulture)
            +" // ROLL IN/OUT "+handles.RollIn.ToString("R",CultureInfo.InvariantCulture)+"/"+handles.RollOut.ToString("R",CultureInfo.InvariantCulture);
    }
    private bool BackCameraTools()
    {if(WorkspaceMode&&!_workspaceClosing&&_cameraToolsConfirmation==default)return false;
        if(_cameraToolsConfirmation!=default){if(!_pages.CloseModal())return true;_cameraToolsConfirmation=default;_cameraTools?.Cancel();PresentCameraTools();return true;}
        if(_cameraToolsModal==default)return false;CloseCameraTools();return true;
    }
    private bool CloseCameraTools()
    {
        if(_cameraTools?.Snapshot().Dirty==true)
        {_cameraToolsProblem="Camera document has unsaved changes. Save or undo before closing; the document and history are retained.";return false;}
        if(WorkspaceMode&&!_workspaceClosing){SelectWorkspaceTab("camera");return true;}
        if(_cameraToolsConfirmation!=default){if(_host.IsAlive(_cameraToolsConfirmation)&&!_pages.CloseModal())return false;_cameraToolsConfirmation=default;}
        if(_cameraToolsModal!=default){if(_cameraToolsModal!=_document&&_host.IsAlive(_cameraToolsModal)&&!_pages.CloseModal())return false;_cameraToolsModal=default;}
        ReleaseCameraCurveInput();_cameraTools?.Dispose();_cameraTools=null;_cameraToolsProblem="";return true;
    }
}
#endif
