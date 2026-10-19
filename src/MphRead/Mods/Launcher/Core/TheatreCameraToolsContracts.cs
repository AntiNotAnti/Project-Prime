#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;

namespace MphRead.Mods.Launcher.Core;

public enum TheatreCameraMarkerKind { Event, Kill, Bookmark }
public sealed record TheatreCameraMarker(string Id,uint Frame,TheatreCameraMarkerKind Kind);
public sealed record TheatreCameraMotion(bool ConstantSpeed,bool Collision,bool Director);
public sealed record TheatreCameraToolsContext(TheatreCameraContext Camera,object Owner,string SourceRevision,
    string AnnotationRevision,ImmutableArray<TheatreCameraMarker> Markers,ImmutableArray<int> LookAtSlots,
    TheatreCameraMotion Motion,object Persisted)
{
    public ImmutableArray<TheatreCameraCurvePoint> Curve { get; init; }=ImmutableArray<TheatreCameraCurvePoint>.Empty;
}
public enum TheatreCameraToolsChange { Move, Paste, Duplicate, SnapEvent, SnapKill, SnapBookmark, Interpolation, Ease, LookAt, Remove, Transform, Handles, Curve }
public sealed record TheatreCameraTransform(float X,float Y,float Z,float Yaw,float Pitch,float Roll,int FrameOffset,float FovOffset,float RollOffset);
public sealed record TheatreCameraCurvePoint(uint Frame,float X,float Y,float Z,float Fov,float Roll);
public sealed record TheatreCameraCurveEdit(int Channel,int Handle,float Delta);
public enum TheatreCameraMotionChange { ConstantSpeed, Collision, Director }
public sealed record TheatreCameraToolsEdit(TheatreCameraToolsChange Kind,TheatreCameraToolsContext Context,
    ImmutableArray<uint> Selection,ImmutableArray<TheatreCameraKey> Before,ImmutableArray<TheatreCameraKey> After);
public sealed record TheatreCameraToolsResult(bool Verified,string Notice);
public sealed record TheatreCameraToolsSnapshot(TheatreCameraToolsContext Context,int FocusIndex,TheatreCameraKey? Focus,
    ImmutableArray<uint> Selection,uint Destination,int ClipboardCount,TheatreCameraToolsEdit? Pending,bool Current,
    string Status,string Error);

/// <summary>Optional advanced commands on the SAME camera owner used by the basic native panel.</summary>
public interface ITheatreCameraToolsBackend
{
    TheatreCameraToolsContext CaptureTools();
    bool ToolsCurrent(TheatreCameraToolsContext context);
    void ValidateReviewedTools(TheatreCameraToolsContext context);
    TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit reviewed);
    void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey focused);
    void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change);
}
#endif
