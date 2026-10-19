using System;
using System.Collections.Generic;
using System.Numerics;

namespace MphRead.Mods.Launcher;

public enum NativeStudioElementMode { Object, Face, Edge, Vertex }
public enum NativeStudioTransformTool { Move, Rotate, Scale }
public enum NativeStudioAxes { Free, X, Y, Z, XY, XZ, YZ }
public enum NativeStudioSelectionMode { Replace, Add, Toggle }
public enum NativeStudioPivot { Individual, Center, Active, World, Cursor }
public enum NativeStudioModelingOperation { SelectAll, Clear, Linked, Grow, Shrink, Boundary, ConvertMesh, ApplyTransform,
    ApplyUv, FitUv, MatchDensity, CopyUv, PasteUv, RotateUvPositive, RotateUvNegative, FlipU, FlipV, ExplicitUv, ProjectedUv, ToggleChecker }
public sealed record NativeStudioModelingValues(float X=0,float Y=0,float Z=0,float Angle=0,float Scale=1,
    float ScaleV=1,float OffsetU=0,float OffsetV=0,float Density=16);
public readonly record struct NativeStudioModelingTicket(NativeStudioEditTicket Owner,long ConfigurationRevision,Guid Mesh,string Topology,string Elements);
public sealed record NativeStudioComponentRow(int Index,int A,int B,string Label,bool Selected);
public sealed record NativeStudioModelingSnapshot(long Revision,NativeStudioModelingTicket Ticket,bool Alive,
    NativeStudioElementMode Mode,NativeStudioTransformTool Tool,NativeStudioAxes Axes,NativeStudioSelectionMode SelectionMode,
    NativeStudioPivot Pivot,bool Local,float GridSnap,float AngleSnap,float ScaleSnap,Vector3 Cursor,
    IReadOnlyList<NativeStudioComponentRow> Components,string Selection,bool GestureActive,bool Checker,string Status);
public interface INativeStudioEditorModeling
{
    NativeStudioModelingSnapshot CaptureModeling();
    NativeStudioResult ConfigureModeling(NativeStudioModelingTicket ticket,NativeStudioElementMode? mode=null,
        NativeStudioTransformTool? tool=null,NativeStudioAxes? axes=null,bool? local=null,NativeStudioSelectionMode? selection=null,
        NativeStudioPivot? pivot=null,float? gridSnap=null,float? angleSnap=null,float? scaleSnap=null,Vector3? cursor=null);
    NativeStudioResult SelectComponent(NativeStudioModelingTicket ticket,NativeStudioComponentRow component);
    NativeStudioResult ExecuteModeling(NativeStudioModelingTicket ticket,NativeStudioModelingOperation operation,NativeStudioModelingValues values);
}

/// <summary>Same retained viewport; page supplies only its last successful draw's coordinate basis.</summary>
public interface INativeStudioEditorModelingViewport
{
    NativeStudioViewportBasis? CaptureModelingBasis(int frameWidth,int frameHeight,int clientWidth,int clientHeight);
    bool BeginModelingPointer(NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double x,double y);
    bool PickModelingPointer(NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double x,double y);
    bool MoveModelingPointer(NativeStudioViewportBasis basis,double x,double y);
    NativeStudioResult EndModelingPointer(NativeStudioViewportBasis basis,double x,double y);
    void ValidateModelingInput(NativeStudioViewportBasis? basis);
}

/// <summary>The exact last successful draw basis, including camera and density; never hidden legacy control Bounds.</summary>
public readonly record struct NativeStudioViewportBasis(int FrameWidth,int FrameHeight,int ClientWidth,int ClientHeight,
    int X,int Y,int Width,int Height,Vector3 CameraPosition,Vector3 CameraTarget,bool Perspective)
{
    private static bool Finite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
    public bool IsValid=>FrameWidth>0&&FrameHeight>0&&ClientWidth>0&&ClientHeight>0&&Width>0&&Height>0&&X>=0&&Y>=0
        &&X+(long)Width<=FrameWidth&&Y+(long)Height<=FrameHeight&&Finite(CameraPosition)&&Finite(CameraTarget)
        &&float.IsFinite(Vector3.DistanceSquared(CameraPosition,CameraTarget))&&Vector3.DistanceSquared(CameraPosition,CameraTarget)>1e-10f;
    public bool Contains(double clientX,double clientY)=>IsValid&&double.IsFinite(clientX)&&double.IsFinite(clientY)
        &&clientX*FrameWidth/ClientWidth>=X&&clientY*FrameHeight/ClientHeight>=Y
        &&clientX*FrameWidth/ClientWidth<X+(double)Width&&clientY*FrameHeight/ClientHeight<Y+(double)Height;
    public (double X,double Y) Local(double clientX,double clientY)
        =>(clientX*FrameWidth/ClientWidth-X,clientY*FrameHeight/ClientHeight-Y);
}
public sealed record NativeStudioGestureCapture(Guid Id,NativeStudioModelingTicket Ticket,NativeStudioViewportBasis Basis,double StartX,double StartY);

/// <summary>Lease-scoped input fence only. Existing viewport previews and document commands own geometry and Undo.</summary>
public sealed class NativeStudioGestureFence
{
    private readonly int _thread=Environment.CurrentManagedThreadId;
    public NativeStudioGestureCapture? Active { get; private set; }
    private void Check(){if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("Studio gestures belong to their editor/render owner thread.");}
    public bool Begin(NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double clientX,double clientY,out Guid gesture)
    {
        Check();gesture=Guid.Empty;if(Active!=null||!basis.Contains(clientX,clientY)||ticket.Owner.Lease==Guid.Empty)return false;
        gesture=Guid.NewGuid();Active=new(gesture,ticket,basis,clientX,clientY);return true;
    }
    public bool Validate(Guid gesture,NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis)
    {
        Check();if(Active is not {} active||active.Id!=gesture)return false;
        if(active.Ticket==ticket&&active.Basis==basis&&basis.IsValid)return true;
        Active=null;return false;
    }
    public bool Move(Guid gesture,NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double clientX,double clientY,out Vector2 logicalDelta)
    {
        Check();logicalDelta=default;if(!Validate(gesture,ticket,basis))return false;
        if(!basis.Contains(clientX,clientY)||Math.Abs(clientX-Active!.StartX)>1000000||Math.Abs(clientY-Active.StartY)>1000000)
        {Active=null;return false;}
        logicalDelta=new((float)(clientX-Active!.StartX),(float)(clientY-Active.StartY));return true;
    }
    public NativeStudioGestureCapture? Finish(Guid gesture,NativeStudioModelingTicket ticket,NativeStudioViewportBasis basis,double clientX,double clientY)
    {
        Check();if(!Move(gesture,ticket,basis,clientX,clientY,out _))return null;var finished=Active;Active=null;return finished;
    }
    public void Cancel(Guid gesture){Check();if(Active?.Id==gesture)Active=null;}
}
