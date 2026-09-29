using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

internal static class NextPassChecks
{
    public static void Modeling(Action<bool,string> check)
    {
        MapMesh Box()=>MapMeshEditing.Convert(new MapBox(),16);
        var projected=Box();projected.FaceUv[1]=new MapUv { Rotation=37 };
        MapMeshEditing.DeleteFace(projected,0);check(projected.FaceUv.ContainsKey(0)&&!projected.FaceUv.ContainsKey(1),"face deletion remaps projection overrides");
        MapMeshEditing.Triangulate(projected,new[]{0});check(projected.FaceUv.Count==2,"triangulation preserves per-face projections");
        var orderedSelection=new MapSubSelection();orderedSelection.Vertices.UnionWith(new[]{5,2});orderedSelection.ActiveVertex=5;orderedSelection.ActiveVertex=2;
        check(orderedSelection.OrderedVertices.SequenceEqual(new[]{5,2}),"merge uses vertex selection order");
        var mesh=Box();var topology=new MapMeshTopology(mesh);
        check(topology.Edges.Count()==12&&topology.BoundaryEdges().Length==0&&topology.FaceIslands().Length==1,"box topology has twelve manifold edges");
        var edge=topology.Edges.First();int count=mesh.Vertices.Count;MapMeshEditing.SplitEdge(mesh,edge,cuts:3);
        check(mesh.Vertices.Count==count+3&&new MapMeshTopology(mesh).BoundaryEdges().Length==0,"edge split updates both faces");
        check(GeometryCompiler.Compile(mesh,16).Count==6,"collinear split corners compile");
        mesh=Box();var selection=new MapSubSelection();selection.Bind(mesh.Id);selection.Faces.Add(0);selection.ActiveFace=0;
        var definition=new MapDefinition();definition.Geometry.Add(mesh);definition.Materials.Add(new());var document=new MapDocument(new MapProject(definition));
        definition=document.Project.Definition;mesh=(MapMesh)definition.Geometry[0];
        string original=definition.Serialize();var preview=new MapSubTransformPreview(mesh,selection,"Face","Center",Vector3.Zero);
        for(int i=0;i<100;i++)preview.Update("Move",new(0,i*.1f,0),0,1,false,Vector3.UnitY,Vector3.One);
        check(definition.Serialize()==original&&document.History.CommandCount==0,"100 preview updates leave document and history unchanged");
        preview.Commit(document,"Move");check(document.History.CommandCount==1,"sub-transform commits exactly one command");document.History.Undo();check(definition.Serialize()==original,"sub-transform undo restores exact geometry");document.History.Redo();document.History.Undo();
        var cancelled=new MapSubTransformPreview(mesh,selection,"Face","World",Vector3.Zero);cancelled.Update("Scale",Vector3.Zero,0,0,false,Vector3.UnitZ,Vector3.UnitZ);
        check(definition.Serialize()==original&&document.History.CommandCount==1,"discarded flatten preview creates no history");
        mesh=Box();var region=new[]{0,2};int expectedWalls=new MapMeshTopology(mesh).Edges.Count(e=>new MapMeshTopology(mesh).AdjacentFaces(e).Count(region.Contains)==1);
        MapMeshEditing.ExtrudeRegion(mesh,region,.2f);check(mesh.Faces.Count==6+expectedWalls,"region extrusion creates only boundary walls");MapMeshEditing.CheckOperation(mesh);
        mesh=Box();MapMeshEditing.DeleteFace(mesh,0);topology=new(mesh);check(topology.OpenBoundaries().Length==1,"missing cube face yields one boundary");MapMeshEditing.Fill(mesh,topology.BoundaryEdges());check(new MapMeshTopology(mesh).BoundaryEdges().Length==0,"fill closes selected boundary");
        mesh=Box();MapMeshEditing.BevelEdge(mesh,new MapMeshTopology(mesh).Edges.First(),.1f,3);MapMeshEditing.CheckOperation(mesh);
        check(new MapMeshTopology(mesh).BoundaryEdges().Length==0&&new MapMeshTopology(mesh).NonManifoldEdges().Length==0,"segmented edge bevel stays closed and manifold");
        check(!MapMeshValidator.Validate(mesh).Any(p=>p.Message.Contains("winding")),"edge bevel preserves winding");
        var a=new MapBox{Transform=new(){Scale=new[]{2f,2,2}}};var b=new MapBox{Transform=new(){Position=new[]{1f,0,0},Scale=new[]{2f,2,2}}};
        double Volume(MapMesh m){double sum=0;foreach(var face in m.Faces)for(int i=1;i+1<face.Length;i++)sum+=Vector3.Dot(MapMeshEditing.VertexWorld(m,face[0]),Vector3.Cross(MapMeshEditing.VertexWorld(m,face[i]),MapMeshEditing.VertexWorld(m,face[i+1])))/6;return sum;}
        check(Math.Abs(MapCsgService.Execute(a,b,"Union").Sum(Volume)-12)<.0001,"CSG union retains exact volume instead of bounds");
        check(Math.Abs(MapCsgService.Execute(a,b,"Difference").Sum(Volume)-4)<.0001,"CSG difference volume");
        check(Math.Abs(MapCsgService.Execute(a,b,"Intersection").Sum(Volume)-4)<.0001,"CSG intersection volume");
        check(MapCsgService.Execute(a,a,"Difference").Count==0,"coincident subtraction is empty");
        b.Transform.Position=new[]{2f,0,0};check(MapCsgService.Execute(a,b,"Intersection").Count==0,"touching brushes have empty intersection");
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f);b.Transform.Rotation=new[]{q.X,q.Y,q.Z,q.W};check(MapCsgService.Execute(a,b,"Difference").All(m=>m.Vertices.All(v=>v.All(float.IsFinite))),"rotated brush clipping stays finite");
        mesh=Box();mesh.Vertices.Add(new[]{float.NaN,0,0});check(MapMeshValidator.Validate(mesh).Any(p=>p.Severity==MapDiagnosticSeverity.Error),"validator rejects non-finite orphan vertex");
        mesh=Box();var dup=MapMeshEditing.DuplicateFaces(mesh,new[]{0,1});check(dup.FaceTexcoords.Count==2&&dup.FaceMaterials.Count==2,"duplicated faces preserve channels");
        MapMeshEditing.Triangulate(dup,new[]{0,1});check(dup.Faces.Count==4,"triangulation preserves both selected polygons");
    }
    public static void Gltf(Action<bool,string> check,string root)
    {
        string folder=Path.Combine(root,"gltf");Directory.CreateDirectory(folder);
        using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,Encoding.UTF8,true)){foreach(float f in new[]{0f,0,0,1,0,0,0,1,0})writer.Write(f);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)2);writer.Write((ushort)0);}
        byte[] buffer=bytes.ToArray();File.WriteAllBytes(Path.Combine(folder,"mesh.bin"),buffer);
        string Document(string? uri)=>JsonSerializer.Serialize(new{asset=new{version="2.0"},buffers=new[]{new{uri,byteLength=buffer.Length}},bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=36},new{buffer=0,byteOffset=36,byteLength=6}},accessors=new[]{new{bufferView=0,componentType=5126,count=3,type="VEC3"},new{bufferView=1,componentType=5123,count=3,type="SCALAR"}},meshes=new[]{new{primitives=new[]{new{attributes=new{POSITION=0},indices=1}}}},nodes=new[]{new{mesh=0,translation=new[]{2,3,4}}},scenes=new[]{new{nodes=new[]{0}}},scene=0},new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});
        string path=Path.Combine(folder,"scene.gltf");File.WriteAllText(path,Document("mesh.bin"));
        var model=ModelImportService.Import(path,new());check(model.Meshes.Count==1&&model.Meshes[0].Vertices[0].SequenceEqual(new[]{2f,3,4}),"glTF flattens scene transform");
        check(model.Dependencies.Count==2,"glTF records external buffer dependency");string hash=MapSourceFingerprint.Hash(model.Dependencies);buffer[0]=1;File.WriteAllBytes(Path.Combine(folder,"mesh.bin"),buffer);check(hash!=MapSourceFingerprint.Hash(model.Dependencies),"buffer change invalidates source fingerprint");buffer[0]=0;File.WriteAllBytes(Path.Combine(folder,"mesh.bin"),buffer);
        File.WriteAllText(path,Document("data:application/octet-stream;base64,"+Convert.ToBase64String(buffer)));check(ModelImportService.Import(path,new()).Meshes[0].Faces.Count==1,"glTF data URI buffers");
        byte[] json=Encoding.UTF8.GetBytes(Document(null));int padded=(json.Length+3)&~3;
        string glb=Path.Combine(folder,"scene.glb");using(var writer=new BinaryWriter(File.Create(glb))){writer.Write(0x46546c67u);writer.Write(2u);writer.Write(12+8+padded+8+buffer.Length);writer.Write(padded);writer.Write(0x4e4f534au);writer.Write(json);for(int i=json.Length;i<padded;i++)writer.Write((byte)32);writer.Write(buffer.Length);writer.Write(0x004e4942u);writer.Write(buffer);}
        check(ModelImportService.Import(glb,new()).Meshes[0].Vertices.Count==3,"GLB JSON and embedded BIN decode");
        File.WriteAllText(path,Document("../escape.bin"));bool refused=false;try{ModelImportService.Import(path,new());}catch(InvalidDataException){refused=true;}check(refused,"glTF rejects dependencies outside source folder");
        File.WriteAllText(path,Document("mesh.bin").Replace("\"byteLength\":36","\"byteLength\":360"));refused=false;try{ModelImportService.Import(path,new());}catch(InvalidDataException){refused=true;}check(refused,"glTF rejects out-of-bounds buffer views");
        File.WriteAllText(path,Document("mesh.bin"));model=ModelImportService.Import(path,new());var definition=new MapDefinition{BaseDirectory=folder};ModelReimport.Apply(definition,model,path,MapSourceFingerprint.Hash(model.Dependencies),new());
        string export=MapProjectFolder.Export(definition,Path.Combine(root,"exported"));var loaded=MapDefinition.Load(export);check(File.Exists(loaded.ModelSources[0].Source)&&loaded.ModelSources[0].Dependencies.All(d=>File.Exists(d.Path)),"project-folder export carries model dependency graph");
        check(ModelImportService.Import(loaded.ModelSources[0].Source,new()).Meshes.Count==1,"exported glTF can be reimported");
    }
}
