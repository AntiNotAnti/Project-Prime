#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Bounded UI selection and staged commands; replay playback and camera storage stay with the existing owner.</summary>
public sealed class TheatreCameraToolsController : IDisposable
{
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private readonly ITheatreCameraToolsBackend _backend;
    private readonly TheatreCameraToolsContext _original;
    private TheatreCameraToolsContext _context;
    private ImmutableArray<uint> _selection=ImmutableArray<uint>.Empty;
    private ImmutableArray<TheatreCameraKey> _clipboard=ImmutableArray<TheatreCameraKey>.Empty;
    private TheatreCameraToolsEdit? _pending;
    private uint _destination;
    private TheatreCameraCurveEdit _curve=new(0,0,0);
    private TheatreCameraHandles _handles=new(null,null,0,0,0,0);
    private TheatreCameraTransform _transform=new(0,0,0,0,0,0,0,0,0);
    private uint? _anchor;
    private int _focus;
    private bool _disposed,_authority;
    private long? _awaitingPicture;
    private string _status="Select saved keys and review an exact track edit.",_error="";
    public TheatreCameraToolsController(ITheatreCameraToolsBackend backend)
    {
        _backend=backend??throw new ArgumentNullException(nameof(backend));_original=_context=backend.CaptureTools();
        Validate(_context);_authority=_backend.ToolsCurrent(_context);_destination=_context.Camera.Frame;
        if(!_context.Camera.Keys.IsEmpty){_selection=ImmutableArray.Create(_context.Camera.Keys[0].Frame);_anchor=_selection[0];}
    }
    public TheatreCameraToolsSnapshot Snapshot()
    {
        Verify();if(!_disposed&&_authority&&!_backend.ToolsCurrent(_context))_authority=false;
        return new(_context,_focus,Focused,_selection,_destination,_clipboard.Length,_pending,!_disposed&&_authority,_status,_error);
    }
    private TheatreCameraKey? Focused=>_context.Camera.Keys.IsEmpty?null:_context.Camera.Keys[_focus];
    public bool Refresh()=>Run(()=>
    {
        Free();_authority=false;var next=_backend.CaptureTools();Validate(next);
        if(!ReferenceEquals(next.Owner,_original.Owner)||next.Camera.SceneGeneration!=_original.Camera.SceneGeneration||next.Camera.LogicalPath!=_original.Camera.LogicalPath
            ||next.Camera.PlaybackPath!=_original.Camera.PlaybackPath||next.SourceRevision!=_original.SourceRevision)
            throw new TheatreCameraStaleException("The original replay owner or source changed. Close camera tools.");
        if(_awaitingPicture is long previous&&next.Camera.PresentationRevision<=previous)
            throw new InvalidOperationException("Wait for a newer successful replay picture, then explicitly refresh.");
        uint? focused=Focused?.Frame;_context=next;_focus=0;
        for(int i=0;i<next.Camera.Keys.Length;i++)if(next.Camera.Keys[i].Frame==focused){_focus=i;break;}
        _selection=_selection.Where(frame=>next.Camera.Keys.Any(key=>key.Frame==frame)).ToImmutableArray();
        if(_anchor is uint anchor&&!_selection.Contains(anchor))_anchor=_selection.IsEmpty?null:_selection[0];
        _awaitingPicture=null;_authority=next.Camera.SettledPaused&&_backend.ToolsCurrent(next);
        _status=_authority?"Saved camera keys and current playback reviewed.":"Pause, wait for seeking, then refresh.";
    });
    public bool Destination(string text)=>Run(()=>
    {
        Free();if(!uint.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out uint frame)||frame>_context.Camera.Duration)
            throw new ArgumentException("Enter a whole destination frame within the reviewed replay.");
        _destination=frame;
    });
    public bool Transform(TheatreCameraTransform transform)=>Run(()=>
    {
        Free();foreach(float value in new[]{transform.X,transform.Y,transform.Z,transform.Yaw,transform.Pitch,transform.Roll,transform.FovOffset,transform.RollOffset})
            if(!float.IsFinite(value)||Math.Abs(value)>100000)throw new ArgumentException("Enter finite transform values within 100000.");
        _transform=transform;
    });
    public bool Handles(TheatreCameraHandles handles)=>Run(()=>
    {Free();if(!TheatreCameraController.ValidHandles(handles))throw new ArgumentException("Camera handles must contain finite values.");_handles=handles;});
    public bool Curve(TheatreCameraCurveEdit edit)=>Run(()=>
    {Free();if(edit.Channel is <0 or >4||edit.Handle is <-1 or >1||!float.IsFinite(edit.Delta))throw new ArgumentException("Invalid curve edit.");_curve=edit;});
    public bool ValidateReview()=>Run(()=>{Free();Reviewed();_backend.ValidateReviewedTools(_context);});
    public bool FocusSavedFrame(uint frame)=>Run(()=>
    {
        Free();Reviewed();int found=-1;
        for(int i=0;i<_context.Camera.Keys.Length;i++)if(_context.Camera.Keys[i].Frame==frame){found=i;break;}
        if(found<0)throw new TheatreCameraStaleException("The drawn camera key no longer belongs to this review.");
        _focus=found;
    });
    public bool MoveFocus(int direction)=>Run(()=>
    {Free();Reviewed();if(direction is not(-1 or 1))throw new ArgumentException("Choose previous or next key.");if(!_context.Camera.Keys.IsEmpty)_focus=(_focus+direction+_context.Camera.Keys.Length)%_context.Camera.Keys.Length;});
    public bool Nearest()=>Run(()=>
    {
        Free();Reviewed();if(Focused==null)throw new InvalidOperationException("No saved camera keys exist.");
        uint frame=_context.Camera.Frame;var key=_context.Camera.Keys.OrderBy(row=>Math.Abs((long)row.Frame-frame)).ThenBy(row=>row.Frame).First();
        _focus=_context.Camera.Keys.IndexOf(key);_selection=ImmutableArray.Create(key.Frame);_anchor=key.Frame;
    });
    public bool ToggleSelection()=>Run(()=>
    {Free();Reviewed();uint frame=Focus().Frame;_selection=_selection.Contains(frame)?_selection.Remove(frame):_selection.Add(frame).Sort();if(_anchor==null||!_selection.Contains(_anchor.Value))_anchor=_selection.IsEmpty?null:_selection[0];});
    public bool SelectRange()=>Run(()=>
    {
        Free();Reviewed();uint frame=Focus().Frame;uint anchor=_anchor??frame;
        _selection=_context.Camera.Keys.Where(key=>key.Frame>=Math.Min(frame,anchor)&&key.Frame<=Math.Max(frame,anchor)).Select(key=>key.Frame).ToImmutableArray();_anchor=anchor;
    });
    public bool SelectFrames(ImmutableArray<uint> frames)=>Run(()=>
    {
        Free();Reviewed();_backend.ValidateReviewedTools(_context);
        if(frames.IsDefault)throw new ArgumentException("Choose a reviewed set of camera frames.");
        var selected=frames.Distinct().OrderBy(frame=>frame).ToImmutableArray();
        if(selected.Any(frame=>!_context.Camera.Keys.Any(key=>key.Frame==frame)))
            throw new TheatreCameraStaleException("A selected camera key no longer belongs to this review.");
        _selection=selected;_anchor=selected.IsEmpty?null:selected[0];
        if(!selected.IsEmpty)_focus=_context.Camera.Keys.IndexOf(_context.Camera.Keys.First(key=>key.Frame==selected[0]));
        _status=$"Selected {selected.Length} saved camera key(s).";
    });
    public bool SelectAll()=>Run(()=>{Free();Reviewed();_selection=_context.Camera.Keys.Select(key=>key.Frame).ToImmutableArray();_anchor=_selection.IsEmpty?null:_selection[0];});
    public bool ClearSelection()=>Run(()=>{Free();_selection=ImmutableArray<uint>.Empty;_anchor=null;});
    public bool Copy()=>Run(()=>
    {Free();Reviewed();_clipboard=Selected();_status=$"Copied {_clipboard.Length} saved camera key(s) in this replay editor.";});
    public bool StageChoice(TheatreCameraToolsChange kind,int value)=>Stage(kind,value);
    public bool Stage(TheatreCameraToolsChange kind,int? choice=null)=>Run(()=>
    {
        Free();Reviewed();if(choice!=null&&kind is not(TheatreCameraToolsChange.Interpolation or TheatreCameraToolsChange.Ease or TheatreCameraToolsChange.LookAt))throw new ArgumentException("This edit does not accept a direct camera choice.");
        if(!Enum.IsDefined(kind))throw new ArgumentException("Unknown camera track command.");
        var before=_context.Camera.Keys;var after=before;var selected=kind is TheatreCameraToolsChange.Paste or TheatreCameraToolsChange.Duplicate?ImmutableArray<TheatreCameraKey>.Empty:Selected();
        switch(kind)
        {
            case TheatreCameraToolsChange.Move:
                var moved=Translate(selected,_destination);after=Insert(before.Where(key=>!_selection.Contains(key.Frame)).ToImmutableArray(),moved);break;
            case TheatreCameraToolsChange.Paste:
                if(_clipboard.IsEmpty)throw new InvalidOperationException("Copy saved keys in this editor before pasting.");
                after=Insert(before,Translate(_clipboard,_destination));break;
            case TheatreCameraToolsChange.Duplicate:
                var previous=before.Where(key=>key.Frame<_context.Camera.Frame).LastOrDefault()??throw new InvalidOperationException("No preceding key exists at this playhead.");
                after=Insert(before,ImmutableArray.Create(previous with{Frame=_context.Camera.Frame}));break;
            case TheatreCameraToolsChange.SnapEvent:case TheatreCameraToolsChange.SnapKill:case TheatreCameraToolsChange.SnapBookmark:
                if(selected.Length!=1)throw new InvalidOperationException("Select exactly one key before snapping.");
                var targetKind=kind==TheatreCameraToolsChange.SnapKill?TheatreCameraMarkerKind.Kill:kind==TheatreCameraToolsChange.SnapBookmark?TheatreCameraMarkerKind.Bookmark:TheatreCameraMarkerKind.Event;
                var marker=_context.Markers.Where(row=>targetKind==TheatreCameraMarkerKind.Event?row.Kind!=TheatreCameraMarkerKind.Bookmark:row.Kind==targetKind)
                    .OrderBy(row=>Math.Abs((long)row.Frame-_context.Camera.Frame)).ThenBy(row=>row.Frame).FirstOrDefault()??throw new InvalidOperationException("No reviewed marker is available for this snap.");
                after=Insert(before.Where(key=>key.Frame!=selected[0].Frame).ToImmutableArray(),ImmutableArray.Create(selected[0] with{Frame=marker.Frame}));break;
            case TheatreCameraToolsChange.Interpolation:
                int interpolation=choice??(Focus().Pose.Interpolation+1)%5;if(interpolation is <0 or >4)throw new ArgumentException("Choose a supported interpolation.");after=before.Select(key=>_selection.Contains(key.Frame)?key with{Pose=key.Pose with{Interpolation=interpolation}}:key).ToImmutableArray();break;
            case TheatreCameraToolsChange.Ease:
                int ease=choice??(Focus().Pose.Ease+1)%4;if(ease is <0 or >3)throw new ArgumentException("Choose a supported easing.");after=before.Select(key=>_selection.Contains(key.Frame)?key with{Pose=key.Pose with{Ease=ease}}:key).ToImmutableArray();break;
            case TheatreCameraToolsChange.LookAt:
                var slots=_context.LookAtSlots.Prepend(-1).Distinct().ToArray();int next=choice??slots[(Array.IndexOf(slots,Focus().Pose.LookAt)+1)%slots.Length];if(!slots.Contains(next))throw new ArgumentException("Choose a look-at slot in the reviewed replay.");
                after=before.Select(key=>_selection.Contains(key.Frame)?key with{Pose=key.Pose with{LookAt=next}}:key).ToImmutableArray();break;
            case TheatreCameraToolsChange.Curve:
                after=before.Select(key=>_selection.Contains(key.Frame)?CurveKey(key,before,_curve):key).ToImmutableArray();break;
            case TheatreCameraToolsChange.Handles:
                after=before.Select(key=>_selection.Contains(key.Frame)?key with{Pose=key.Pose with{Interpolation=4,Handles=_handles}}:key).ToImmutableArray();break;
            case TheatreCameraToolsChange.Transform:
                var rotation=Quaternion.CreateFromYawPitchRoll(_transform.Yaw*MathF.PI/180,_transform.Pitch*MathF.PI/180,_transform.Roll*MathF.PI/180);
                var changed=selected.Select(key=>
                {
                    long frame=(long)key.Frame+_transform.FrameOffset;
                    if(frame<0||frame>_context.Camera.Duration)throw new ArgumentException("The complete transformed group must remain within the replay.");
                    var pose=key.Pose;var position=Vector3.Transform(new Vector3(pose.X,pose.Y,pose.Z),rotation)+new Vector3(_transform.X,_transform.Y,_transform.Z);
                    var q=Quaternion.Normalize(rotation*new Quaternion(pose.Qx,pose.Qy,pose.Qz,pose.Qw));
                    TheatreCameraVector? Tangent(TheatreCameraVector? tangent)
                    {if(tangent==null)return null;var v=Vector3.Transform(new Vector3(tangent.X,tangent.Y,tangent.Z),rotation);return new(v.X,v.Y,v.Z);}
                    var handles=pose.Handles is{} h?h with{Incoming=Tangent(h.Incoming),Outgoing=Tangent(h.Outgoing)}:null;
                    return key with{Frame=(uint)frame,Pose=pose with{X=position.X,Y=position.Y,Z=position.Z,Qx=q.X,Qy=q.Y,Qz=q.Z,Qw=q.W,Fov=pose.Fov+_transform.FovOffset,Roll=pose.Roll+_transform.RollOffset,Handles=handles}};
                }).ToImmutableArray();
                after=Insert(before.Where(key=>!_selection.Contains(key.Frame)).ToImmutableArray(),changed);break;
            case TheatreCameraToolsChange.Remove:after=before.Where(key=>!_selection.Contains(key.Frame)).ToImmutableArray();break;
        }
        if(after.Length>64||after.Any(key=>key.Frame>_context.Camera.Duration||!TheatreCameraController.ValidPose(key.Pose))||after.Select(key=>key.Frame).Distinct().Count()!=after.Length)
            throw new ArgumentException("The edit exceeds the 64-key track limit or contains an invalid key.");
        if(after.SequenceEqual(before))throw new InvalidOperationException("This command does not change the reviewed track.");
        _pending=new(kind,_context,_selection,before,after);_status="Review the full exact track change before saving.";
    });
    private static TheatreCameraKey CurveKey(TheatreCameraKey key,ImmutableArray<TheatreCameraKey> keys,TheatreCameraCurveEdit edit)
    {
        var p=key.Pose;float d=edit.Delta;
        if(edit.Handle==0)return key with{Pose=edit.Channel switch{0=>p with{X=p.X+d},1=>p with{Y=p.Y+d},2=>p with{Z=p.Z+d},3=>p with{Fov=p.Fov+d},_=>p with{Roll=p.Roll+d}}};
        var h=p.Handles??new(null,null,0,0,0,0);
        if(edit.Channel<3)
        {
            var neighbour=edit.Handle<0?keys.LastOrDefault(k=>k.Frame<key.Frame):keys.FirstOrDefault(k=>k.Frame>key.Frame);
            var tangent=(edit.Handle<0?h.Incoming:h.Outgoing)??(neighbour==null?new TheatreCameraVector(0,0,0):new((neighbour.Pose.X-p.X)/3,(neighbour.Pose.Y-p.Y)/3,(neighbour.Pose.Z-p.Z)/3));
            tangent=edit.Channel switch{0=>tangent with{X=tangent.X+d},1=>tangent with{Y=tangent.Y+d},_=>tangent with{Z=tangent.Z+d}};
            h=edit.Handle<0?h with{Incoming=tangent}:h with{Outgoing=tangent};
        }
        else if(edit.Channel==3)h=edit.Handle<0?h with{FovIn=h.FovIn+d}:h with{FovOut=h.FovOut+d};
        else h=edit.Handle<0?h with{RollIn=h.RollIn+d}:h with{RollOut=h.RollOut+d};
        return key with{Pose=p with{Interpolation=4,Handles=h}};
    }
    private ImmutableArray<TheatreCameraKey> Translate(ImmutableArray<TheatreCameraKey> source,uint destination)
    {
        long delta=(long)destination-source.Min(key=>key.Frame);
        if(source.Any(key=>(long)key.Frame+delta<0||(long)key.Frame+delta>_context.Camera.Duration))throw new ArgumentException("The complete key group must remain within the replay.");
        return source.Select(key=>key with{Frame=(uint)((long)key.Frame+delta)}).ToImmutableArray();
    }
    private static ImmutableArray<TheatreCameraKey> Insert(ImmutableArray<TheatreCameraKey> existing,ImmutableArray<TheatreCameraKey> added)
    {
        if(added.Any(key=>existing.Any(row=>row.Frame==key.Frame)))throw new InvalidOperationException("A destination already contains an unselected camera key. No key was overwritten.");
        return existing.AddRange(added).OrderBy(key=>key.Frame).ToImmutableArray();
    }
    public bool Confirm()=>Run(()=>
    {
        Reviewed();var pending=_pending??throw new InvalidOperationException("No reviewed camera track change is pending.");
        var result=_backend.CommitTools(pending);_pending=null;_authority=false;_awaitingPicture=_context.Camera.PresentationRevision;
        _status=result.Notice.Length>0?result.Notice:"Camera track change saved. Wait for a new picture, then refresh before another edit.";
    });
    public void Cancel(){Verify();_pending=null;_error="";}
    public bool Preview()=>Run(()=>{Free();Reviewed();_backend.PreviewTools(_context,Focus());AwaitPicture("Key preview requested. Wait for seeking and a new picture, then refresh.");});
    public bool Motion(TheatreCameraMotionChange change)=>Run(()=>
    {Free();Reviewed();if(!Enum.IsDefined(change))throw new ArgumentException("Unknown camera motion option.");_backend.ChangeMotion(_context,change);AwaitPicture("Preview motion changed. Wait for a new picture, then refresh. Speed, collision and Director affect the current preview and export.");});
    private void AwaitPicture(string status){_authority=false;_awaitingPicture=_context.Camera.PresentationRevision;_status=status;}
    private TheatreCameraKey Focus()=>Focused??throw new InvalidOperationException("Choose a saved camera key.");
    private ImmutableArray<TheatreCameraKey> Selected()
    {var rows=_context.Camera.Keys.Where(key=>_selection.Contains(key.Frame)).ToImmutableArray();if(rows.IsEmpty)throw new InvalidOperationException("Select one or more saved camera keys first.");return rows;}
    private static void Validate(TheatreCameraToolsContext context)
    {
        var camera=context.Camera;
        if(camera.Keys.IsDefault||camera.Keys.Length>64||camera.Keys.Any(key=>key.Frame>camera.Duration||!TheatreCameraController.ValidPose(key.Pose))
            ||camera.Keys.Select(key=>key.Frame).Distinct().Count()!=camera.Keys.Length||!camera.Keys.SequenceEqual(camera.Keys.OrderBy(key=>key.Frame))||context.LookAtSlots.IsDefault
            ||context.LookAtSlots.Any(slot=>slot<0||slot>7)||context.Markers.IsDefault||context.Markers.Length>3||context.Markers.Any(row=>row.Frame>camera.Duration))
            throw new InvalidDataException("The reviewed camera keys, targets or markers are invalid.");
    }
    private void Reviewed(){if(!_authority||!_context.Camera.SettledPaused||!_backend.ToolsCurrent(_context)){_authority=false;throw new TheatreCameraStaleException("Camera tools review changed. Cancel any confirmation and refresh.");}}
    private void Free(){if(_pending!=null)throw new InvalidOperationException("Confirm or cancel the exact track edit first.");}
    private bool Run(Action action)
    {
        Verify();if(_disposed)return false;
        try{action();_error="";return true;}
        catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {if(error is TheatreCameraStaleException||!_backend.ToolsCurrent(_context))_authority=false;_error=error.Message;return false;}
    }
    private void Verify(){if(_thread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Camera tools belong to the engine owner thread.");}
    public void Dispose(){Verify();_disposed=true;_authority=false;_pending=null;_selection=ImmutableArray<uint>.Empty;_clipboard=ImmutableArray<TheatreCameraKey>.Empty;}
}
#endif
