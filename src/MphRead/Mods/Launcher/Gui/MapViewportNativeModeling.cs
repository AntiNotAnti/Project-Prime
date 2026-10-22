using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapViewport
{
    private string? _nativeTransformTopology;
    private void CancelNativeTransform() => _nativeTransformTopology=null;

    private (Point Point,double Depth)? Project(MapViewportLayout layout,Vector point)
    {
        var projected=Camera.Project(layout,point);
        return projected is {} p && double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Depth)
            ? (new Point(p.X,p.Y),p.Depth) : null;
    }

    internal bool NativeTransformActive => _drag;

    internal (Vector Center,Vector X,Vector Y,Vector Z)? NativeGizmoAxes
    {
        get
        {
            if(ReadOnlyPreview || ActiveSelection is not {} selected || !Document.Selection.Contains(selected.Id)
                || selected.Value is MapGeometry { Locked:true } || (ElementMode!="Object" && !HasSubSelection))return null;
            Vector center=GizmoCenter+_preview;
            Vector x=center+GizmoAxis(selected,0)*3,y=center+GizmoAxis(selected,1)*3,z=center+GizmoAxis(selected,2)*3;
            return NativeFinite(center)&&NativeFinite(x)&&NativeFinite(y)&&NativeFinite(z)?(center,x,y,z):null;
        }
    }

    internal bool NativePickComponent(MapViewportLayout layout,double x,double y,bool add,bool toggle)
        => NativePickComponent(layout,x,y,add,toggle,1,1);
    internal bool NativePickComponent(MapViewportLayout layout,double x,double y,bool add,bool toggle,double pixelPerClientX,double pixelPerClientY)
    {
        if(!NativePresentationLeased || ReadOnlyPreview || _drag || !NativePoint(layout,x,y)
            || !double.IsFinite(pixelPerClientX)||!double.IsFinite(pixelPerClientY)||pixelPerClientX<=0||pixelPerClientY<=0
            || !NativeCameraFinite() || ElementMode is not("Face" or "Edge" or "Vertex")
            || ActiveSelection?.Value is not MapMesh { Locked:false })return false;
        if(!PickSubElement(layout,new Point(x,y),add,toggle,false,pixelPerClientX,pixelPerClientY))return false;
        SubChanged();return true;
    }

    internal bool NativeBeginTransform(MapViewportLayout layout,double x,double y,double pixelPerClientX,double pixelPerClientY)
    {
        if(!NativePresentationLeased || ReadOnlyPreview || _drag || _boxSelect || _orbit || _pan
            || Tool is not("Move" or "Rotate" or "Scale") || !NativePoint(layout,x,y)
            || !double.IsFinite(pixelPerClientX) || !double.IsFinite(pixelPerClientY)
            || pixelPerClientX<=0 || pixelPerClientY<=0 || NativeGizmoAxes is not {} axes)return false;
        var selected=MapObjects.All(Document.Project.Definition).Where(o=>Document.Selection.Contains(o.Id)).ToArray();
        if(selected.Length==0 || selected.Length!=Document.Selection.Count || selected.Any(o=>o.Value is MapGeometry { Locked:true }))return false;
        if(!NativeCameraFinite() || !float.IsFinite(Snap) || !float.IsFinite(AngleSnap)
            || TransformPivot is {} pivot && !NativeFinite(pivot))return false;
        var center=Project(layout,axes.Center);
        if(center is not {} origin)return false;
        double LogicalDistance(Point a,Point b)
        {
            double dx=(a.X-b.X)/pixelPerClientX,dy=(a.Y-b.Y)/pixelPerClientY;
            return Math.Sqrt(dx*dx+dy*dy);
        }
        var pointer=new Point(x,y);
        int axis=-1;
        Vector[] ends={axes.X,axes.Y,axes.Z};
        for(int i=0;i<3;i++)
            if(Project(layout,ends[i]) is {} end && LogicalDistance(end.Point,origin.Point)>18
                && LogicalDistance(end.Point,pointer)<12){axis=i;break;}
        if(axis<0 && LogicalDistance(origin.Point,pointer)>=7)return false;
        MapSubTransformPreview? preview=null;
        string? topology=null;
        if(ElementMode!="Object")
        {
            if(ActiveSelection?.Value is not MapMesh mesh || !HasSubSelection)return false;
            try
            {
                topology=MapStudioModelingCommands.TopologySignature(mesh);
                preview=new(mesh,SubSelection,ElementMode,PivotMode,CursorPivot);
            }
            catch(Exception ex){ModelingError?.Invoke(ex.Message);return false;}
        }
        _nativeTransformTopology=topology;
        _subPreview=preview;_axis=axis;_preview=Vector.Zero;_rotation=0;_scale=1;
        _keyboardTransform=false;_numericTransform="";_start=_last=pointer;_drag=true;
        InvalidateVisual();return true;
    }

    // Native coordinates arrive as total logical-client displacement from pointer-down.
    internal void NativeUpdateTransform(double logicalDx,double logicalDy)
    {
        if(!NativePresentationLeased || ReadOnlyPreview || !_drag || !double.IsFinite(logicalDx) || !double.IsFinite(logicalDy)
            || !float.IsFinite((float)logicalDx) || !float.IsFinite((float)logicalDy))return;
        UpdateTransformDrag(new Avalonia.Vector(logicalDx,logicalDy),KeyModifiers.None);
        InvalidateVisual();
    }

    internal void NativeCommitTransform()
    {
        try
        {
            if(!NativePresentationLeased || ReadOnlyPreview || !_drag)return;
            if(!NativeFinite(_preview)||Math.Abs(_preview.X)>1000000||Math.Abs(_preview.Y)>1000000||Math.Abs(_preview.Z)>1000000
                || !float.IsFinite(_rotation)||Math.Abs(_rotation)>3600||!float.IsFinite(_scale)||_scale is <.001f or >1000)
                throw new InvalidOperationException("Transform values are outside supported finite bounds.");
            var preview=_subPreview;_subPreview=null;
            if(preview!=null)MapStudioModelingCommands.CommitPreview(Document,preview,Tool,
                _nativeTransformTopology ?? throw new InvalidOperationException("Component transform topology was not captured."));
            else Document.TransformSelection(Document.Selection.ToHashSet(),Tool,_preview,_rotation,_scale,LocalAxes,
                rotationAxis:TurnAxis,scaleAxes:ScaleAxes,pivot:TransformPivot);
        }
        finally { CancelInteraction(); }
    }

    private bool NativeCameraFinite()
    {
        var (right,up,forward)=Basis();
        return NativeFinite(CameraPosition)&&NativeFinite(CameraTarget)&&NativeFinite(right)&&NativeFinite(up)&&NativeFinite(forward)
            && float.IsFinite(Vector.Distance(CameraPosition,CameraTarget));
    }
    private static bool NativeFinite(Vector p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    private static bool NativePoint(MapViewportLayout layout,double x,double y)=>layout.IsValid
        && double.IsFinite(x)&&double.IsFinite(y)&&x>=0&&y>=0&&x<layout.Width&&y<layout.Height;

    // One transform-preview engine serves the retained Avalonia and native presentation gestures.
    private void UpdateTransformDrag(Avalonia.Vector full,KeyModifiers modifiers)
    {
        var(right,up,_)=Basis();float distance=Vector.Distance(CameraPosition,CameraTarget);
        UpdateTransformDrag(full,modifiers,right,up,distance);
    }
    private void UpdateTransformDrag(Avalonia.Vector full,KeyModifiers modifiers,Vector right,Vector up,float distance)
    {
        _preview=(right*(float)full.X-up*(float)full.Y)*distance/500;
        if (LocalAxes && ActiveSelection?.Value is MapGeometry activeGeometry)
        { var r = activeGeometry.Transform.Rotation; _preview = Vector.Transform(_preview, Quaternion.Inverse(new Quaternion(r[0],r[1],r[2],r[3]))); }
        if(_axis>=0){float value=_preview[_axis];_preview=Vector.Zero;_preview[_axis]=value;}
        else if(Axes!="Free"){for(int i=0;i<3;i++)if(!Axes.Contains("XYZ"[i]))_preview[i]=0;}
        else if(_subPreview==null&&(View=="Perspective"||View=="Top"))_preview.Y=0;
        else if(_subPreview==null&&View=="Front")_preview.Z=0;
        else if(_subPreview==null&&View=="Side")_preview.X=0;
        if(Snap>0)for(int i=0;i<3;i++)_preview[i]=MathF.Round(_preview[i]/Snap)*Snap;
        _rotation=MathF.Round((float)full.X/Math.Max(1,AngleSnap))*Math.Max(1,AngleSnap);
        float scaleStep=float.IsFinite(ScaleSnap)&&ScaleSnap>0?ScaleSnap:.01f;
        _scale=Math.Max(scaleStep,1+MathF.Round((float)full.X/100/scaleStep)*scaleStep);
        if(Tool!="Move")_preview=Vector.Zero;
        if(_subPreview!=null&&Tool=="Move"&&modifiers.HasFlag(KeyModifiers.Control)&&ActiveSelection?.Value is MapMesh snappingMesh)
        {
            var r=snappingMesh.Transform.Rotation;var orientation=new Quaternion(r[0],r[1],r[2],r[3]);
            var worldDelta=LocalAxes?Vector.Transform(_preview,orientation):_preview;var desired=_subPreview.Pivot+worldDelta;
            var hit=MapLayoutCommands.NearestSurface(desired,Cache.SurfaceNear(desired),new HashSet<Guid>{snappingMesh.Id});
            if(hit is {} contact){worldDelta=contact.Point-_subPreview.Pivot;_preview=LocalAxes?Vector.Transform(worldDelta,Quaternion.Inverse(orientation)):worldDelta;}
        }
        _subPreview?.Update(Tool,_preview,_rotation,_scale,LocalAxes,TurnAxis,ScaleAxes);
    }
}
