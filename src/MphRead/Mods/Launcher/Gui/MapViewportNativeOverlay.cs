using System;
using System.Collections.Generic;
using System.Numerics;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapViewport
{
    private const int NativeOverlayMaxLines=4096,NativeOverlayMaxPoints=2048;
    private long _nativeOverlayRevision;
    private long _nativeOverlayFingerprintRevision=-1;
    private ulong _nativeOverlayFingerprint;
    private NativeOverlayKey? _nativeOverlayKey;
    private MapViewportOverlay _nativeOverlay=MapViewportOverlay.Empty;
    private readonly record struct NativeOverlayKey(DocumentStateId State,object Definition,long Revision,ulong Fingerprint,
        Guid Active,Guid SubObject,int Faces,int Edges,int Vertices,int ActiveFace,int ActiveVertex,MapEdge? ActiveEdge,
        string Mode,string Tool,string Axes,bool Local,string Pivot,Vector3 Cursor,Vector3 Preview,float Rotation,float Scale,
        bool ReadOnly,object? SubPreview);

    // The native session calls this after its direct component-row selection changes.
    internal void NativeOverlaySelectionChanged(){unchecked{_nativeOverlayRevision++;}}

    private MapViewportOverlay NativeOverlayFrame()
    {
        if(!NativePresentationLeased)return MapViewportOverlay.Empty;
        if(_nativeOverlayFingerprintRevision!=_nativeOverlayRevision)
        {
            ulong sum=0,xor=0;
            void Add(ulong item){ulong mixed=OverlayMix(item);unchecked{sum+=mixed;}xor^=mixed;}
            foreach(int face in SubSelection.Faces)Add((ulong)(uint)face*4+1);
            foreach(int vertex in SubSelection.Vertices)Add((ulong)(uint)vertex*4+2);
            foreach(var edge in SubSelection.Edges)Add(((ulong)(uint)edge.A<<32)|(uint)edge.B);
            _nativeOverlayFingerprint=OverlayMix(sum)^xor;
            _nativeOverlayFingerprintRevision=_nativeOverlayRevision;
        }
        var key=new NativeOverlayKey(Document.CurrentStateId,Document.Project.Definition,_nativeOverlayRevision,_nativeOverlayFingerprint,
            Document.ActiveObjectId,SubSelection.ObjectId,SubSelection.Faces.Count,SubSelection.Edges.Count,SubSelection.Vertices.Count,
            SubSelection.ActiveFace,SubSelection.ActiveVertex,SubSelection.ActiveEdge,ElementMode,Tool,Axes,LocalAxes,PivotMode,CursorPivot,
            _preview,_rotation,_scale,ReadOnlyPreview,_subPreview);
        if(_nativeOverlayKey is {} cached && cached==key)return _nativeOverlay;
        var batches=new List<MapViewportOverlayBatch>();
        int lineCount=0,pointCount=0;bool truncated=false;
        if(NativeGizmoAxes is {} axes)
        {
            batches.Add(new(MapViewportOverlayPrimitive.Lines,new[]{axes.Center,axes.X},new(1,0,0,1),3,false));
            batches.Add(new(MapViewportOverlayPrimitive.Lines,new[]{axes.Center,axes.Y},new(0,1,0,1),3,false));
            batches.Add(new(MapViewportOverlayPrimitive.Lines,new[]{axes.Center,axes.Z},new(0,.65f,1,1),3,false));
            batches.Add(new(MapViewportOverlayPrimitive.Points,new[]{axes.Center},new(1,.8f,.2f,1),7,false));
            lineCount=3;pointCount=1;
        }
        if(ActiveSelection?.Value is MapMesh mesh && SubSelection.ObjectId==mesh.Id)
        {
            var lines=new List<Vector3>();var uniqueEdges=new HashSet<ulong>();
            bool AddEdge(int a,int b)
            {
                if((uint)a>=mesh.Vertices.Count||(uint)b>=mesh.Vertices.Count||a==b)return true;
                ulong edge=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                if(!uniqueEdges.Add(edge))return true;
                if(lineCount>=NativeOverlayMaxLines){truncated=true;return false;}
                var start=SubWorld(mesh,a);var end=SubWorld(mesh,b);
                if(!NativeFinite(start)||!NativeFinite(end))return true;
                lines.Add(start);lines.Add(end);lineCount++;return true;
            }
            bool full=false;
            foreach(int face in SubSelection.Faces)
            {
                if((uint)face>=mesh.Faces.Count)continue;
                var indices=mesh.Faces[face];
                for(int i=0;i<indices.Length;i++)
                    if(!AddEdge(indices[i],indices[(i+1)%indices.Length])){full=true;break;}
                if(full)break;
            }
            if(!full)
                foreach(var edge in SubSelection.Edges)
                    if(!AddEdge(edge.A,edge.B))break;
            if(lines.Count>0)batches.Add(new(MapViewportOverlayPrimitive.Lines,lines.ToArray(),new(1,.75f,.15f,1),3,false));
            if(ElementMode=="Vertex")
            {
                var selected=new List<Vector3>();var others=new List<Vector3>();
                bool AddPoint(int index,List<Vector3> destination)
                {
                    if((uint)index>=mesh.Vertices.Count)return true;
                    if(pointCount>=NativeOverlayMaxPoints){truncated=true;return false;}
                    var world=SubWorld(mesh,index);if(!NativeFinite(world))return true;
                    destination.Add(world);pointCount++;return true;
                }
                bool pointFull=false;
                foreach(int vertex in SubSelection.Vertices)
                    if(!AddPoint(vertex,selected)){pointFull=true;break;}
                if(!pointFull)
                    for(int i=0;i<mesh.Vertices.Count;i++)
                        if(!SubSelection.Vertices.Contains(i)&&!AddPoint(i,others))break;
                if(others.Count>0)batches.Add(new(MapViewportOverlayPrimitive.Points,others.ToArray(),new(.44f,.5f,.56f,1),4,false));
                if(selected.Count>0)batches.Add(new(MapViewportOverlayPrimitive.Points,selected.ToArray(),new(1,.75f,.15f,1),7,false));
            }
        }
        _nativeOverlayKey=key;
        return _nativeOverlay=new(batches,truncated);
    }
    private static ulong OverlayMix(ulong value)
    {
        unchecked{value=(value^(value>>30))*0xbf58476d1ce4e5b9UL;value=(value^(value>>27))*0x94d049bb133111ebUL;return value^(value>>31);}
    }
}
