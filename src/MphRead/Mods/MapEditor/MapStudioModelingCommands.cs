using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public enum MapStudioUvOperation { Transform, Fit, Density, Paste, RotatePositive, RotateNegative, FlipU, FlipV, Explicit, Projected }
public readonly record struct MapStudioUvValues(float ScaleU=1,float ScaleV=1,float OffsetU=0,float OffsetV=0,float Angle=0,float Density=16)
{ public MapStudioUvValues():this(1,1,0,0,0,16){} }

/// <summary>Native presentation calls the retained editor's ordinary component-preview and object-history operations.</summary>
public static class MapStudioModelingCommands
{
    public static void ConvertSelection(MapDocument document)
    {
        var selected=document.Selection.ToHashSet();
        var eligible=document.Project.Definition.Geometry.Where(g=>selected.Contains(g.Id)&&!g.Locked&&g is not MapMesh).ToArray();
        if(eligible.Length==0)throw new InvalidOperationException("Select unlocked authored primitives to convert.");
        var scales=eligible.ToDictionary(g=>g.Id,g=>g.Material>=0&&g.Material<document.Project.Definition.Materials.Count
            ?document.Project.Definition.Materials[g.Material].TexScale:throw new InvalidOperationException("Repair the object's material index first."));
        document.EditObjects("Convert to mesh",eligible.Select(g=>g.Id),scratch=>{
            for(int i=0;i<scratch.Geometry.Count;i++)if(scales.TryGetValue(scratch.Geometry[i].Id,out float scale))
                scratch.Geometry[i]=MapMeshEditing.Convert(scratch.Geometry[i],scale);
        });
    }
    // Capture only at gesture begin and commit; the native owner separately caches
    // presentation topology by document state. Face ordering is part of identity.
    public static string TopologySignature(MapMesh mesh)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] number=new byte[4];
        void Add(int value){BinaryPrimitives.WriteInt32LittleEndian(number,value);hash.AppendData(number);}
        Add(mesh.Vertices.Count);Add(mesh.Faces.Count);
        foreach(var face in mesh.Faces){Add(face.Length);foreach(int vertex in face)Add(vertex);}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static void CommitPreview(MapDocument document,MapSubTransformPreview preview,string tool,string expectedTopology)
    {
        var mesh=Mesh(document,preview.ObjectId);
        if(string.IsNullOrEmpty(expectedTopology)||TopologySignature(mesh)!=expectedTopology)
            throw new InvalidOperationException("Mesh topology changed during the gesture; preview discarded.");
        var checkedMesh=MapMeshEditing.Clone(mesh);
        if(preview.Positions.Length!=checkedMesh.Vertices.Count)throw new InvalidOperationException("Mesh changed during the gesture.");
        checkedMesh.Vertices=preview.Positions.Select(p=>new[]{p.X,p.Y,p.Z}).ToList();MapMeshEditing.CheckOperation(checkedMesh);
        preview.Commit(document,tool);
    }
    public static void TransformElements(MapDocument document,MapSubSelection selection,string mode,string tool,
        Vector3 move,float angle,float scale,bool local,Vector3 axis,Vector3 scaleAxes,string pivot,Vector3 cursor)
    {
        if(tool is not("Move" or "Rotate" or "Scale")||mode is not("Face" or "Edge" or "Vertex"))throw new ArgumentException("Choose a component transform.");
        if(!Finite(move)||!float.IsFinite(angle)||!float.IsFinite(scale)||scale is <.001f or >1000||Math.Abs(angle)>3600
            ||Math.Abs(move.X)>1000000||Math.Abs(move.Y)>1000000||Math.Abs(move.Z)>1000000
            ||!Finite(axis)||!float.IsFinite(axis.LengthSquared())||axis.LengthSquared()<.0001f||!Finite(scaleAxes)||!Finite(cursor))throw new ArgumentOutOfRangeException(nameof(move));
        var mesh=Mesh(document,selection.ObjectId);string topology=TopologySignature(mesh);var preview=new MapSubTransformPreview(mesh,selection,mode,pivot,cursor);
        preview.Update(tool,move,angle,scale,local,axis,scaleAxes);CommitPreview(document,preview,tool,topology);
    }
    public static void AssignFaces(MapDocument document,Guid id,IEnumerable<int> selected,int material)
    {
        var mesh=Mesh(document,id);var faces=Faces(mesh,selected);
        if(material<0||material>=document.Project.Definition.Materials.Count)throw new ArgumentOutOfRangeException(nameof(material));
        document.EditObjects("Assign selected face material",new[]{id},scratch=>{
            var target=(MapMesh)MapObjects.Find(scratch,id)!.Value;
            while(target.FaceMaterials.Count<target.Faces.Count)target.FaceMaterials.Add(target.Material);
            foreach(int face in faces)target.FaceMaterials[face]=material;
        });
    }
    public static float[][] CopyUv(MapDocument document,Guid id,IEnumerable<int> selected)
    {
        var mesh=Mesh(document,id);var faces=Faces(mesh,selected);float scale=TexScale(document,mesh);
        return GeometryCompiler.Compile(mesh,scale)[faces[0]].Texcoords.Select(p=>new[]{p.X,p.Y}).ToArray();
    }
    public static void EditUv(MapDocument document,Guid id,IEnumerable<int> selected,MapStudioUvOperation operation,MapStudioUvValues values,float[][]? clipboard=null)
    {
        var mesh=Mesh(document,id);var faces=Faces(mesh,selected);float texScale=TexScale(document,mesh);
        if(!Enum.IsDefined(operation)||!float.IsFinite(values.ScaleU)||!float.IsFinite(values.ScaleV)||Math.Abs(values.ScaleU)>1000||Math.Abs(values.ScaleV)>1000
            ||Math.Abs(values.ScaleU)<.001f||Math.Abs(values.ScaleV)<.001f||!float.IsFinite(values.OffsetU)||!float.IsFinite(values.OffsetV)
            ||Math.Abs(values.OffsetU)>1000000||Math.Abs(values.OffsetV)>1000000||!float.IsFinite(values.Angle)||Math.Abs(values.Angle)>3600
            ||!float.IsFinite(values.Density)||values.Density is <.001f or >65536)throw new ArgumentOutOfRangeException(nameof(values));
        if(operation==MapStudioUvOperation.Paste&&(clipboard==null||clipboard.Any(p=>p is not{Length:2}||p.Any(n=>!float.IsFinite(n)))
            ||faces.Any(f=>mesh.Faces[f].Length!=clipboard.Length)))throw new InvalidOperationException("Copy a face with the same number of corners first.");
        document.EditObjects(operation+" UV",new[]{id},scratch=>{
            var target=(MapMesh)MapObjects.Find(scratch,id)!.Value;
            switch(operation)
            {
                case MapStudioUvOperation.Transform:MapMeshEditing.TransformUv(target,faces,new(values.ScaleU,values.ScaleV),new(values.OffsetU,values.OffsetV),values.Angle,texScale);break;
                case MapStudioUvOperation.Fit:MapMeshEditing.FitUv(target,faces,texScale);break;
                case MapStudioUvOperation.Density:MapMeshEditing.MatchTexelDensity(target,faces,values.Density,texScale);break;
                case MapStudioUvOperation.RotatePositive:MapMeshEditing.TransformUv(target,faces,Vector2.One,Vector2.Zero,90,texScale);break;
                case MapStudioUvOperation.RotateNegative:MapMeshEditing.TransformUv(target,faces,Vector2.One,Vector2.Zero,-90,texScale);break;
                case MapStudioUvOperation.FlipU:MapMeshEditing.TransformUv(target,faces,new(-1,1),Vector2.Zero,0,texScale);break;
                case MapStudioUvOperation.FlipV:MapMeshEditing.TransformUv(target,faces,new(1,-1),Vector2.Zero,0,texScale);break;
                case MapStudioUvOperation.Explicit:MapMeshEditing.TransformUv(target,faces,Vector2.One,Vector2.Zero,0,texScale);break;
                case MapStudioUvOperation.Projected:foreach(int face in faces)if(face<target.FaceTexcoords.Count)target.FaceTexcoords[face]=null;break;
                case MapStudioUvOperation.Paste:while(target.FaceTexcoords.Count<target.Faces.Count)target.FaceTexcoords.Add(null);foreach(int face in faces)target.FaceTexcoords[face]=clipboard!.Select(p=>(float[])p.Clone()).ToArray();break;
            }
            foreach(int face in faces)
                if(face<target.FaceTexcoords.Count&&target.FaceTexcoords[face] is {} uv
                    &&(uv.Length!=target.Faces[face].Length||uv.Any(p=>p is not{Length:2}||p.Any(n=>!float.IsFinite(n)))))
                    throw new InvalidOperationException("UV operation produced invalid coordinates; original UVs retained.");
        });
    }
    private static MapMesh Mesh(MapDocument document,Guid id)=>MapObjects.Find(document.Project.Definition,id)?.Value is MapMesh{Locked:false} mesh
        ?mesh:throw new InvalidOperationException("Select an unlocked editable mesh first.");
    private static int[] Faces(MapMesh mesh,IEnumerable<int> selected)
    {var faces=selected.Distinct().Order().ToArray();if(faces.Length==0||faces.Any(f=>f<0||f>=mesh.Faces.Count))throw new InvalidOperationException("Select current mesh faces first.");return faces;}
    private static float TexScale(MapDocument document,MapMesh mesh)=>mesh.Material>=0&&mesh.Material<document.Project.Definition.Materials.Count
        ?document.Project.Definition.Materials[mesh.Material].TexScale:throw new InvalidOperationException("Repair the selected mesh material first.");
    private static bool Finite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
}
