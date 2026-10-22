using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Formats.Collision;
using MphRead.Utility;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen
{
    /// <summary>
    /// Writes a room's collision file.
    ///
    /// Upstream's packer produces the same bytes and was the first thing this
    /// used, but it was written to round-trip one of the game's own rooms and
    /// its cost shows on a converted level: it looks each point up by scanning
    /// the list it is building, and it fills the lookup grid by asking every
    /// cell about every face. A hand-built arena of a hundred faces does not
    /// notice. A level with tens of thousands does -- it is quadratic twice
    /// over, and the second one is cells times faces.
    ///
    /// Same runtime semantics: points are deduplicated
    /// through a dictionary, and faces are pushed into the cells their bounds
    /// cover instead of every cell interrogating every face. Cells claim a
    /// face by its bounding box rather than by the exact polygon test, which
    /// can only list a face in a cell it does not quite reach -- the real
    /// polygon is tested at run time anyway, so the cost is a slightly longer
    /// list and never a missed surface.
    /// </summary>
    public static class MapCollisionPacker
    {
        /// <summary>The grid step is fixed: the run-time lookup divides by four.</summary>
        private const float CellSize = 4f;

        public static byte[] Pack(IReadOnlyList<CollisionDataEditor> data, IReadOnlyList<Portal>? portals = null)
        {
            portals ??= Array.Empty<Portal>();
            MapCollisionOptimizer.Result optimization=MapCollisionOptimizer.Optimize(data);
            data=optimization.Editors;
            if(data.Count==0)throw new ProgramException("A map needs at least one solid face.");
            var points=new List<Vector3>();var pointIds=new Dictionary<Vector3,int>();
            var planes=new List<Vector4>();var planeIds=new Dictionary<Vector4,int>();
            var pointIndices=new List<int>();
            var faces=new List<(int PlaneIndex,CollisionDataEditor Editor,int Count,int Start)>();
            var min=new Vector3(Single.MaxValue);var max=new Vector3(Single.MinValue);
            foreach(CollisionDataEditor editor in data)
            {
                if(editor.Points.Count<3||editor.Points.Count>10)
                    throw new ProgramException($"A collision face has {editor.Points.Count} points; the format allows 3 to 10.");
                if(!planeIds.TryGetValue(editor.Plane,out int planeIndex))
                {planeIndex=planes.Count;planes.Add(editor.Plane);planeIds.Add(editor.Plane,planeIndex);}
                int start=pointIndices.Count;
                foreach(Vector3 point in editor.Points)
                {
                    if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z)
                        ||Math.Abs(point.X)>=524288||Math.Abs(point.Y)>=524288||Math.Abs(point.Z)>=524288)
                        throw new MapAuthoringException("FP-MAP-004","Collision coordinates exceed the fixed-point range.");
                    if(!pointIds.TryGetValue(point,out int pointIndex))
                    {pointIndex=points.Count;points.Add(point);pointIds.Add(point,pointIndex);
                     min=Vector3.ComponentMin(min,point);max=Vector3.ComponentMax(max,point);}
                    pointIndices.Add(pointIndex);
                }
                faces.Add((planeIndex,editor,editor.Points.Count,start));
            }
            int partsX=Math.Max(1,(int)MathF.Floor((max.X-min.X)/CellSize)+1);
            int partsY=Math.Max(1,(int)MathF.Floor((max.Y-min.Y)/CellSize)+1);
            int partsZ=Math.Max(1,(int)MathF.Floor((max.Z-min.Z)/CellSize)+1);
            if((long)partsX*partsY*partsZ>MapBudgetValidator.MaxGridCells)
                throw new MapAuthoringException("FP-MAP-003","Collision grid budget exceeded.");
            var cells=new List<int>[partsX*partsY*partsZ];
            for(int i=0;i<data.Count;i++)
            {
                CollisionDataEditor editor=data[i];var faceMin=new Vector3(Single.MaxValue);var faceMax=new Vector3(Single.MinValue);
                foreach(Vector3 point in editor.Points){faceMin=Vector3.ComponentMin(faceMin,point);faceMax=Vector3.ComponentMax(faceMax,point);}
                int x0=CellIndex(faceMin.X,min.X,partsX),x1=CellIndex(faceMax.X,min.X,partsX);
                int y0=CellIndex(faceMin.Y,min.Y,partsY),y1=CellIndex(faceMax.Y,min.Y,partsY);
                int z0=CellIndex(faceMin.Z,min.Z,partsZ),z1=CellIndex(faceMax.Z,min.Z,partsZ);
                for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    (cells[y*partsX*partsZ+z*partsX+x]??=new List<int>()).Add(i);
            }
            var dataIndices=new List<int>();var entries=new List<(int Count,int Start)>();
            foreach(List<int>? cell in cells){int start=dataIndices.Count;if(cell!=null)dataIndices.AddRange(cell);entries.Add((cell?.Count??0,start));}
            bool extended=points.Count>ushort.MaxValue||planes.Count>ushort.MaxValue||faces.Count>ushort.MaxValue
                ||pointIndices.Count>ushort.MaxValue||dataIndices.Count>ushort.MaxValue
                ||pointIndices.Any(i=>i>ushort.MaxValue)||dataIndices.Any(i=>i>ushort.MaxValue)
                ||faces.Any(f=>f.PlaneIndex>ushort.MaxValue||f.Start>ushort.MaxValue||f.Count>ushort.MaxValue)
                ||entries.Any(e=>e.Count>ushort.MaxValue||e.Start>ushort.MaxValue);
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);stream.Position=Sizes.CollisionHeader;
            int pointOffset=(int)stream.Position;foreach(Vector3 point in points)writer.WriteVector3(point);
            int planeOffset=(int)stream.Position;foreach(Vector4 plane in planes)writer.WriteVector4(plane);
            int pointIndexOffset=(int)stream.Position;
            if(extended)foreach(int index in pointIndices)writer.Write((uint)index);
            else{foreach(int index in pointIndices)writer.Write((ushort)index);Align(stream,writer);}
            int dataOffset=(int)stream.Position;
            foreach((int planeIndex,CollisionDataEditor editor,int count,int start) in faces)
            {
                writer.Write(0);
                if(extended){writer.Write((uint)planeIndex);writer.Write((ushort)editor.Flags);writer.Write(editor.LayerMask);
                    writer.Write((uint)count);writer.Write((uint)start);}
                else{writer.Write((ushort)planeIndex);writer.Write((ushort)editor.Flags);writer.Write(editor.LayerMask);writer.Write((ushort)0);
                    writer.Write((ushort)count);writer.Write((ushort)start);}
            }
            int dataIndexOffset=(int)stream.Position;
            if(extended)foreach(int index in dataIndices)writer.Write((uint)index);
            else{foreach(int index in dataIndices)writer.Write((ushort)index);Align(stream,writer);}
            int entryOffset=(int)stream.Position;
            foreach((int count,int start) in entries)
                if(extended){writer.Write((uint)count);writer.Write((uint)start);}
                else{writer.Write((ushort)count);writer.Write((ushort)start);}
            int portalOffset=(int)stream.Position;
            foreach(Portal portal in portals)
            {
                if(portal.Points.Count!=4||portal.Planes.Count!=4)
                    throw new MapAuthoringException("FP-MAP-003","Runtime partition portals require four points and four edge planes.");
                writer.WriteString(portal.Name,40);writer.WriteString(portal.NodeName1,24);writer.WriteString(portal.NodeName2,24);
                foreach(Vector3 point in portal.Points)writer.WriteVector3(point);foreach(Vector4 plane in portal.Planes)writer.WriteVector4(plane);
                writer.WriteVector4(portal.Plane);writer.Write((ushort)0);writer.Write(portal.LayerMask);writer.Write((ushort)4);
                writer.Write(portal.Unknown00);writer.Write(portal.Unknown01);
            }
            stream.Position=0;writer.Write((extended?"wc02":"wc01").ToCharArray());writer.Write(points.Count);writer.Write(pointOffset);
            writer.Write(planes.Count);writer.Write(planeOffset);writer.Write(pointIndices.Count);writer.Write(pointIndexOffset);
            writer.Write(faces.Count);writer.Write(dataOffset);writer.Write(dataIndices.Count);writer.Write(dataIndexOffset);
            writer.Write(partsX);writer.Write(partsY);writer.Write(partsZ);writer.WriteVector3(min);
            writer.Write(entries.Count);writer.Write(entryOffset);writer.Write(portals.Count);writer.Write(portalOffset);
            return stream.ToArray();
        }

        private static int CellIndex(float value, float origin, int parts)
        {
            return Math.Clamp((int)((value - origin) / CellSize), 0, parts - 1);
        }

        private static void Align(MemoryStream stream, BinaryWriter writer)
        {
            while (stream.Position % 4 != 0)
            {
                writer.Write((byte)0);
            }
        }
    }
}
