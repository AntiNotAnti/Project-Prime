using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>
/// Private CPU-only source staging. Exercises actual document, preview, history and UV helpers.
/// No owner screen, window, Scene, GL, filesystem fixture, or independent modeling implementation.
/// Rename/include only when the Studio C owner explicitly enables a build.
/// </summary>
internal static class StudioModelingCoreChecks
{
    internal static void Run(Action<bool,string> check)
    {
        Conversion(check);
        DetachedPreview(check);
        ObjectPreview(check);
        UvOperations(check);
        FaceMaterials(check);
        InvalidCommands(check);
        GestureFence(check);
        StalePreview(check);
    }

    private static void Conversion(Action<bool,string> check)
    {
        var box=new MapBox { Label="Synthetic authored box", Material=0,
            Transform=new() { Position=new[]{3f,4,5}, Scale=new[]{2f,3,4} } };
        var locked=new MapBox { Label="Synthetic locked box", Material=0, Locked=true };
        var document=Document(box,locked);document.Selection.UnionWith(new[]{box.Id,locked.Id});
        var definition=document.Project.Definition;var history=document.History;
        var expected=GeometryCompiler.Compile(definition.Geometry[0],16);
        string before=Snapshot(document);var state=document.CurrentStateId;int count=history.CommandCount;
        MapStudioModelingCommands.ConvertSelection(document);
        var mesh=(MapMesh)MapObjects.Find(document.Project.Definition,box.Id)!.Value;
        var compiled=GeometryCompiler.Compile(mesh,16);
        check(mesh.Id==box.Id&&document.Selection.SetEquals(new[]{box.Id,locked.Id})
            &&document.Project.Definition.Geometry[1] is MapBox {Locked:true},
            "conversion preserves selected stable IDs and excludes locked primitives");
        check(compiled.Count==expected.Count&&compiled.Zip(expected).All(pair=>
            pair.First.Points.SequenceEqual(pair.Second.Points)&&pair.First.Texcoords.SequenceEqual(pair.Second.Texcoords)
            &&pair.First.Material==pair.Second.Material),"conversion preserves actual compiled world geometry and UVs");
        string after=Snapshot(document);var afterState=document.CurrentStateId;
        check(before!=after&&history.CommandCount==count+1&&ReferenceEquals(history,document.History)
            &&ReferenceEquals(definition,document.Project.Definition),"conversion creates one command in the same document/history owners");
        history.Undo();check(Snapshot(document)==before&&document.CurrentStateId==state,"conversion undo restores exact source and state");
        history.Redo();check(Snapshot(document)==after&&document.CurrentStateId==afterState,"conversion redo restores exact mesh and state");
        AtomicReject(document,()=>MapStudioModelingCommands.ConvertSelection(document),check,"already converted and locked selection rejects atomically");
    }

    private static void DetachedPreview(Action<bool,string> check)
    {
        var document=MeshDocument();var mesh=Mesh(document);var selection=Vertices(mesh,0,1);
        string before=Snapshot(document);var state=document.CurrentStateId;int notices=0;
        document.Invalidated+=_=>notices++;
        var preview=new MapSubTransformPreview(mesh,selection,"Vertex","Center",Vector3.Zero);
        var original=mesh.Vertices.Select(Point).ToArray();
        for(int i=1;i<=12;i++)preview.Update("Move",new Vector3(i/4f,0,0),0,1,false,Vector3.UnitX,Vector3.One);
        check(Snapshot(document)==before&&document.CurrentStateId==state&&document.History.CommandCount==0&&notices==0,
            "repeated actual component preview updates leave document, history and invalidation untouched");
        check(preview.Positions[0]==original[0]+new Vector3(3,0,0)&&preview.Positions[1]==original[1]+new Vector3(3,0,0)
            &&preview.Positions[2]==original[2]&&preview.World(0)==preview.Positions[0],
            "component preview uses gesture-start positions and updates selected detached positions only");
        MapStudioModelingCommands.CommitPreview(document,preview,"Move",MapStudioModelingCommands.TopologySignature(mesh));
        string after=Snapshot(document);var afterState=document.CurrentStateId;
        check(after!=before&&document.History.CommandCount==1&&notices==1,
            "component gesture commits exactly one actual history command");
        preview.Positions[0]=new Vector3(999,999,999);
        check(Snapshot(document)==after,"committed history owns a detached copy of preview output");
        document.History.Undo();check(Snapshot(document)==before&&document.CurrentStateId==state,"component undo restores exact source and state");
        document.History.Redo();check(Snapshot(document)==after&&document.CurrentStateId==afterState,"component redo restores exact committed output and state");
        var noop=new MapSubTransformPreview(Mesh(document),Vertices(Mesh(document),0),"Vertex","Center",Vector3.Zero);
        int count=document.History.CommandCount;int oldNotices=notices;
        MapStudioModelingCommands.CommitPreview(document,noop,"Move",MapStudioModelingCommands.TopologySignature(Mesh(document)));
        check(Snapshot(document)==after&&document.CurrentStateId==afterState&&document.History.CommandCount==count&&notices==oldNotices,
            "unchanged component preview creates no history command or invalidation");
    }

    private static void ObjectPreview(Action<bool,string> check)
    {
        var document=MeshDocument();var mesh=Mesh(document);mesh.Transform.Position=new[]{2f,3,0};
        var item=MapObjects.Find(document.Project.Definition,mesh.Id)!;
        var point=MapMeshEditing.VertexWorld(mesh,1);string before=Snapshot(document);
        Matrix4x4 matrix=MapTransformPreview.Matrix(item,"Move",new(3,2,1),0,1,false,Vector3.UnitZ,Vector3.One,null);
        check(Snapshot(document)==before&&!document.History.CanUndo&&Vector3.Transform(point,matrix)==point+new Vector3(3,2,1),
            "actual object preview matrix is detached and predicts world-space translation");
        document.TransformSelection(new[]{mesh.Id},"Move",new(3,2,1),0,1,false);
        check(MapMeshEditing.VertexWorld(Mesh(document),1)==Vector3.Transform(point,matrix),
            "actual object history commit agrees with object preview geometry");
        string moved=Snapshot(document);document.History.Undo();check(Snapshot(document)==before,"object transform undo is exact");
        document.History.Redo();check(Snapshot(document)==moved,"object transform redo is exact");
        foreach(string tool in new[]{"Rotate","Scale"})
        {
            var d=MeshDocument();var m=Mesh(d);m.Transform.Position=new[]{2f,3,0};
            var o=MapObjects.Find(d.Project.Definition,m.Id)!;var initial=MapMeshEditing.VertexWorld(m,1);
            var pivot=new Vector3(1,1,0);float angle=90,scale=2;
            var predicted=MapTransformPreview.Matrix(o,tool,Vector3.Zero,angle,scale,false,Vector3.UnitZ,Vector3.One,pivot);
            string source=Snapshot(d);d.TransformSelection(new[]{m.Id},tool,Vector3.Zero,angle,scale,false,
                rotationAxis:Vector3.UnitZ,scaleAxes:Vector3.One,pivot:pivot);
            check(Vector3.Distance(MapMeshEditing.VertexWorld(Mesh(d),1),Vector3.Transform(initial,predicted))<.0001f,
                "actual object "+tool+" preview agrees with committed world geometry and external pivot");
            string changed=Snapshot(d);d.History.Undo();check(Snapshot(d)==source,"object "+tool+" exact undo");
            d.History.Redo();check(Snapshot(d)==changed,"object "+tool+" exact redo");
        }
    }

    private static void UvOperations(Action<bool,string> check)
    {
        foreach(var operation in Enum.GetValues<MapStudioUvOperation>())
        {
            var document=MeshDocument();var mesh=Mesh(document);var source=Uv(mesh);
            if(operation==MapStudioUvOperation.Explicit)mesh.FaceTexcoords[0]=null;
            string before=Snapshot(document);var state=document.CurrentStateId;
            float[][] clipboard={new[]{21f,31},new[]{25f,31},new[]{25f,39},new[]{21f,39}};
            var values=new MapStudioUvValues(2,3,5,7,0,8);
            MapStudioModelingCommands.EditUv(document,mesh.Id,new[]{0,0},operation,values,clipboard);
            var result=Mesh(document);var uv=result.FaceTexcoords[0];
            bool correct=operation switch
            {
                MapStudioUvOperation.Transform=>UvNear(uv!,source.Select(p=>new[]{p[0]*2+5,p[1]*3+7}).ToArray()),
                MapStudioUvOperation.Fit=>UvNear(uv!,new[]{new[]{0f,0},new[]{64f,0},new[]{64f,64},new[]{0f,64}}),
                MapStudioUvOperation.Density=>Math.Abs(Area(uv!)-8*8*8)<.01f,
                MapStudioUvOperation.Paste=>UvNear(uv!,clipboard),
                MapStudioUvOperation.RotatePositive=>UvNear(uv!,source.Select(p=>new[]{-p[1],p[0]}).ToArray()),
                MapStudioUvOperation.RotateNegative=>UvNear(uv!,source.Select(p=>new[]{p[1],-p[0]}).ToArray()),
                MapStudioUvOperation.FlipU=>UvNear(uv!,source.Select(p=>new[]{-p[0],p[1]}).ToArray()),
                MapStudioUvOperation.FlipV=>UvNear(uv!,source.Select(p=>new[]{p[0],-p[1]}).ToArray()),
                MapStudioUvOperation.Explicit=>uv is {Length:4}&&uv.All(p=>p.Length==2&&p.All(float.IsFinite)),
                MapStudioUvOperation.Projected=>uv==null,
                _=>false
            };
            check(correct,"actual "+operation+" UV helper produces expected coordinates/density/projection");
            string after=Snapshot(document);var afterState=document.CurrentStateId;
            check(before!=after&&document.History.CommandCount==1,"UV "+operation+" creates one command for duplicate selected face indices");
            clipboard[0][0]=-777;
            check(Snapshot(document)==after,"UV "+operation+" stores detached input arrays");
            document.History.Undo();check(Snapshot(document)==before&&document.CurrentStateId==state,"UV "+operation+" exact undo/state");
            document.History.Redo();check(Snapshot(document)==after&&document.CurrentStateId==afterState,"UV "+operation+" exact redo/state");
        }
        var copyDocument=MeshDocument();var copyMesh=Mesh(copyDocument);string original=Snapshot(copyDocument);
        var copied=MapStudioModelingCommands.CopyUv(copyDocument,copyMesh.Id,new[]{0});copied[0][0]=999;
        check(Snapshot(copyDocument)==original&&!copyDocument.History.CanUndo,
            "actual UV copy returns detached arrays without touching source/history");
        var second=MapStudioModelingCommands.CopyUv(copyDocument,copyMesh.Id,new[]{0});
        check(second[0][0]!=999&&!ReferenceEquals(second[0],copyMesh.FaceTexcoords[0]![0]),"repeated UV copy has no source or clipboard alias");
        MapStudioModelingCommands.EditUv(copyDocument,copyMesh.Id,new[]{0},MapStudioUvOperation.Explicit,new());
        check(Snapshot(copyDocument)==original&&copyDocument.History.CommandCount==0,
            "making already explicit UVs explicit is a history no-op");
    }

    private static void FaceMaterials(Action<bool,string> check)
    {
        var document=MeshDocument();var mesh=Mesh(document);
        document.Edit("Fixture second material",d=>d.Materials.Add(new(){Name="Second"}));
        string before=Snapshot(document);var state=document.CurrentStateId;int commands=document.History.CommandCount;
        MapStudioModelingCommands.AssignFaces(document,mesh.Id,new[]{0},1);mesh=Mesh(document);string applied=Snapshot(document);
        check(mesh.FaceMaterials[0]==1&&mesh.FaceMaterials.Skip(1).All(m=>m==mesh.Material)&&document.History.CommandCount==commands+1,
            "component material assignment changes only selected face through one history command");
        document.History.Undo();check(Snapshot(document)==before&&document.CurrentStateId==state,"component material undo restores exact source and state");
        document.History.Redo();check(Snapshot(document)==applied,"component material redo restores exact face assignment");
        AtomicReject(document,()=>MapStudioModelingCommands.AssignFaces(document,mesh.Id,new[]{0},999),check,"invalid component material rejects before document edit");
        AtomicReject(document,()=>MapStudioModelingCommands.AssignFaces(document,mesh.Id,new[]{999},1),check,"invalid component face rejects before material edit");
    }

    private static void InvalidCommands(Action<bool,string> check)
    {
        var document=MeshDocument();var mesh=Mesh(document);
        foreach(int[] faces in new[]{Array.Empty<int>(),new[]{-1},new[]{1},new[]{0,int.MaxValue}})
        {
            AtomicReject(document,()=>MapStudioModelingCommands.CopyUv(document,mesh.Id,faces),check,"UV copy rejects empty/out-of-range face selection");
            AtomicReject(document,()=>MapStudioModelingCommands.EditUv(document,mesh.Id,faces,MapStudioUvOperation.Fit,new()),check,"UV edit rejects empty/out-of-range face selection atomically");
        }
        foreach(var values in new MapStudioUvValues[]{new(ScaleU:float.NaN),new(ScaleV:0),new(OffsetU:float.PositiveInfinity),
            new(OffsetV:1000001),new(Angle:float.NegativeInfinity),new(Density:0),new(Density:65537)})
            AtomicReject(document,()=>MapStudioModelingCommands.EditUv(document,mesh.Id,new[]{0},MapStudioUvOperation.Transform,values),check,"invalid UV numeric input rejects atomically");
        AtomicReject(document,()=>MapStudioModelingCommands.EditUv(document,mesh.Id,new[]{0},(MapStudioUvOperation)999,new()),check,"unknown UV operation rejects atomically");
        foreach(float[][]? clipboard in new float[][]?[]{null,Array.Empty<float[]>(),new[]{new[]{1f,2}},
            new[]{new[]{1f,2},new[]{3f,4},new[]{5f,6},new[]{7f}},
            new[]{new[]{1f,2},new[]{3f,4},new[]{5f,float.NaN},new[]{7f,8}},
            new[]{new[]{1f,2},new[]{3f,4},null!,new[]{7f,8}}})
            AtomicReject(document,()=>MapStudioModelingCommands.EditUv(document,mesh.Id,new[]{0},MapStudioUvOperation.Paste,new(),clipboard),check,"null/malformed/nonfinite/wrong-corner clipboard rejects atomically");
        foreach(float[][] malformed in new[]{new[]{new[]{1f,2}},new[]{new[]{1f,2},new[]{3f,4},new[]{5f,6},new[]{7f}},
            new[]{new[]{1f,2},new[]{3f,4},new[]{5f,float.PositiveInfinity},new[]{7f,8}}})
        {
            var invalid=MeshDocument();var bad=Mesh(invalid);bad.FaceTexcoords[0]=malformed;
            AtomicReject(invalid,()=>MapStudioModelingCommands.CopyUv(invalid,bad.Id,new[]{0}),check,"UV copy rejects malformed authored UV channel");
            AtomicReject(invalid,()=>MapStudioModelingCommands.EditUv(invalid,bad.Id,new[]{0},MapStudioUvOperation.Transform,new()),check,"malformed authored UV channel edit rejects without partial mutation");
        }
        var overflow=MeshDocument();var overflowMesh=Mesh(overflow);
        overflowMesh.FaceTexcoords[0]![0][0]=float.MaxValue;
        AtomicReject(overflow,()=>MapStudioModelingCommands.EditUv(overflow,overflowMesh.Id,new[]{0},MapStudioUvOperation.Transform,new(ScaleU:1000)),check,
            "finite UV input whose transformed output overflows rejects atomically");
        var degenerate=MeshDocument();var degenerateMesh=Mesh(degenerate);
        degenerateMesh.FaceTexcoords[0]=new[]{new[]{1f,1},new[]{1f,1},new[]{1f,1},new[]{1f,1}};
        AtomicReject(degenerate,()=>MapStudioModelingCommands.EditUv(degenerate,degenerateMesh.Id,new[]{0},MapStudioUvOperation.Density,new()),check,
            "density rejects zero-area authored UVs without committing intermediate explicit coordinates");
        mesh.Locked=true;
        AtomicReject(document,()=>MapStudioModelingCommands.CopyUv(document,mesh.Id,new[]{0}),check,"locked mesh UV copy rejects");
        AtomicReject(document,()=>MapStudioModelingCommands.EditUv(document,mesh.Id,new[]{0},MapStudioUvOperation.Fit,new()),check,"locked mesh UV edit rejects atomically");
        AtomicReject(document,()=>new MapSubTransformPreview(mesh,Vertices(mesh,0),"Vertex","Center",Vector3.Zero),check,"locked component preview rejects");
        mesh.Locked=false;
        foreach(var move in new[]{new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0),new Vector3(1000001,0,0)})
            AtomicReject(document,()=>MapStudioModelingCommands.TransformElements(document,Vertices(mesh,0),"Vertex","Move",move,0,1,false,Vector3.UnitX,Vector3.One,"Center",Vector3.Zero),check,"nonfinite/oversized component transform rejects atomically");
        foreach(int[] indices in new[]{Array.Empty<int>(),new[]{-1},new[]{int.MaxValue}})
            AtomicReject(document,()=>MapStudioModelingCommands.TransformElements(document,Vertices(mesh,indices),"Vertex","Move",Vector3.One,0,1,false,Vector3.UnitX,Vector3.One,"Center",Vector3.Zero),check,"empty/out-of-range component selection rejects");
        var preview=new MapSubTransformPreview(mesh,Vertices(mesh,0),"Vertex","Center",Vector3.Zero);
        AtomicReject(document,()=>preview.Update("Move",new(float.NaN,0,0),0,1,false,Vector3.UnitX,Vector3.One),check,"actual preview rejects nonfinite update without document/history change");
        preview.Positions[0]=new(float.NaN,0,0);
        AtomicReject(document,()=>MapStudioModelingCommands.CommitPreview(document,preview,"Move",MapStudioModelingCommands.TopologySignature(mesh)),check,"nonfinite detached output rejects before commit");
        preview.Positions[0]=preview.Positions[1];
        preview.Positions[2]=preview.Positions[1];preview.Positions[3]=preview.Positions[1];
        AtomicReject(document,()=>MapStudioModelingCommands.CommitPreview(document,preview,"Scale",MapStudioModelingCommands.TopologySignature(mesh)),check,"zero-area detached geometry rejects before commit");
    }

    private static void StalePreview(Action<bool,string> check)
    {
        foreach(string mutation in new[]{"reference","vertices","topology","face order","lock"})
        {
            var document=MeshDocument();var mesh=Mesh(document);
            if(mutation=="face order")
            {
                mesh.Faces=new(){new[]{0,1,2},new[]{0,2,3}};mesh.FaceMaterials=new(){0,0};
                mesh.FaceTexcoords=new(){new[]{new[]{2f,3},new[]{10f,3},new[]{10f,7}},new[]{new[]{2f,3},new[]{10f,7},new[]{2f,7}}};
            }
            string topology=MapStudioModelingCommands.TopologySignature(mesh);
            var preview=new MapSubTransformPreview(mesh,Vertices(mesh,0),"Vertex","Center",Vector3.Zero);
            preview.Update("Move",new(.25f,0,0),0,1,false,Vector3.UnitX,Vector3.One);
            if(mutation=="reference")document.Project.Definition.Geometry[0]=MapMeshEditing.Clone(mesh);
            if(mutation=="vertices")mesh.Vertices[1][0]+=.25f;
            if(mutation=="topology")mesh.Faces[0]=new[]{1,2,3,0};
            if(mutation=="face order")(mesh.Faces[0],mesh.Faces[1])=(mesh.Faces[1],mesh.Faces[0]);
            if(mutation=="lock")mesh.Locked=true;
            AtomicReject(document,()=>MapStudioModelingCommands.CommitPreview(document,preview,"Move",topology),check,
                "component commit rejects stale "+mutation+" without changing current document/history");
        }
    }

    private static void GestureFence(Action<bool,string> check)
    {
        var ticket=new NativeStudioModelingTicket(new(Guid.NewGuid(),7,11,Guid.NewGuid()),3,Guid.NewGuid(),"quad-0123","vertex-0");
        var basis=new NativeStudioViewportBasis(1600,1200,800,600,200,100,1000,800,new(3,4,8),Vector3.Zero,true);
        check(basis.IsValid&&basis.Contains(100,50)&&basis.Contains(599.5,449.5)&&!basis.Contains(600,450)
            &&!basis.Contains(99.5,50)&&!basis.Contains(double.NaN,60),"viewport basis uses exact physical framebuffer rectangle with half-open client hit bounds");
        check(basis.Local(110,60)==(20d,20d),"viewport local geometry coordinates scale client input into physical framebuffer units");
        var fence=new NativeStudioGestureFence();
        check(fence.Begin(ticket,basis,110,60,out var gesture)&&gesture!=Guid.Empty,"gesture fence acquires valid exact owner/basis capture");
        check(!fence.Begin(ticket,basis,120,70,out _)&&fence.Active?.Id==gesture,"second begin cannot replace live capture");
        check(fence.Move(gesture,ticket,basis,115,67,out var delta)&&delta==new Vector2(5,7)
            &&basis.Local(115,67)==(30d,34d),"gesture frame uses physical coordinates while transform delta remains logical client units");
        check(!fence.Move(Guid.NewGuid(),ticket,basis,115,67,out _)&&fence.Active?.Id==gesture,"unknown gesture ID cannot move or cancel current capture");
        var capture=fence.Finish(gesture,ticket,basis,115,67);
        check(capture?.Id==gesture&&capture.Ticket==ticket&&capture.Basis==basis&&fence.Active==null
            &&fence.Finish(gesture,ticket,basis,115,67)==null,"gesture finish returns exact frozen ticket/basis once");
        foreach(var changed in new[]{basis with {FrameWidth=1601},basis with {FrameHeight=1201},basis with {ClientWidth=801},basis with {ClientHeight=601},
            basis with {X=201},basis with {Y=101},basis with {Width=999},basis with {Height=799},
            basis with {CameraPosition=new(3,4,9)},basis with {CameraTarget=Vector3.One},basis with {Perspective=false}})
        {
            fence.Begin(ticket,basis,110,60,out gesture);
            check(!fence.Move(gesture,ticket,changed,115,67,out _)&&fence.Active==null,"any framebuffer/client/density/viewport/camera basis change retires gesture");
        }
        foreach(var stale in new[]{ticket with {Owner=ticket.Owner with {Lease=Guid.NewGuid()}},ticket with {Owner=ticket.Owner with {State=8}},
            ticket with {Owner=ticket.Owner with {Revision=12}},ticket with {Owner=ticket.Owner with {Selection=Guid.NewGuid()}},
            ticket with {ConfigurationRevision=4},ticket with {Mesh=Guid.NewGuid()},ticket with {Topology="quad-1032"},ticket with {Elements="vertex-1"}})
        {
            fence.Begin(ticket,basis,110,60,out gesture);
            check(!fence.Validate(gesture,stale,basis)&&fence.Active==null,"stale lease/state/configuration/selection/mesh/topology/elements ticket retires capture");
        }
        foreach(var point in new[]{(-1d,60d),(600d,60d),(110d,450d),(double.NaN,60d),(110d,double.PositiveInfinity)})
        {
            fence.Begin(ticket,basis,110,60,out gesture);
            check(!fence.Move(gesture,ticket,basis,point.Item1,point.Item2,out _)&&fence.Active==null,"out-of-rectangle/nonfinite input cancels capture");
        }
        foreach(var invalid in new[]{basis with {FrameWidth=0},basis with {ClientHeight=0},basis with {X=-1},basis with {Width=1601},
            basis with {CameraTarget=basis.CameraPosition},basis with {CameraPosition=new(float.NaN,0,0)}})
            check(!invalid.IsValid&&!fence.Begin(ticket,invalid,110,60,out _),"invalid physical/client/camera basis cannot acquire gesture");
        check(!fence.Begin(ticket with {Owner=ticket.Owner with {Lease=Guid.Empty}},basis,110,60,out _),"empty owner lease cannot acquire gesture");
        fence.Begin(ticket,basis,110,60,out var old);fence.Cancel(old);
        check(fence.Active==null,"explicit focus-loss/Escape cancellation retires active capture");
        check(fence.Begin(ticket,basis,110,60,out var current)&&current!=old,"capture reacquires after represented focus-loss cancellation");
        fence.Cancel(old);
        check(fence.Active?.Id==current&&!fence.Validate(old,ticket,basis)&&fence.Active?.Id==current,
            "old cancel/validation cannot retire a new capture after reacquisition");
        check(fence.Finish(old,ticket,basis,110,60)==null&&fence.Active?.Id==current,
            "old release cannot finish a new capture");
        fence.Cancel(current);
    }

    private static MapDocument MeshDocument()
    {
        var mesh=new MapMesh {Label="Synthetic CPU quad",Material=0,Solid=false,
            Vertices=new(){new[]{0f,0,0},new[]{4f,0,0},new[]{4f,2,0},new[]{0f,2,0}},
            Faces=new(){new[]{0,1,2,3}},FaceMaterials=new(){0},
            FaceTexcoords=new(){new[]{new[]{2f,3},new[]{10f,3},new[]{10f,7},new[]{2f,7}}}};
        return Document(mesh);
    }
    private static MapDocument Document(params MapGeometry[] geometry)
    {
        var definition=new MapDefinition {Name="PRIVATE_SYNTHETIC_STUDIO_C"};
        definition.Materials.Add(new(){Name="Synthetic metadata only",TexScale=16});definition.Geometry.AddRange(geometry);
        return new(new MapProject(definition));
    }
    private static MapMesh Mesh(MapDocument document)=>(MapMesh)document.Project.Definition.Geometry[0];
    private static MapSubSelection Vertices(MapMesh mesh,params int[] indices)
    {var selection=new MapSubSelection {ObjectId=mesh.Id};selection.Vertices.UnionWith(indices);return selection;}
    private static Vector3 Point(float[] p)=>new(p[0],p[1],p[2]);
    private static float[][] Uv(MapMesh mesh)=>mesh.FaceTexcoords[0]!.Select(p=>(float[])p.Clone()).ToArray();
    private static bool UvNear(float[][] actual,float[][] expected)=>actual.Length==expected.Length&&actual.Zip(expected)
        .All(p=>p.First.Length==2&&p.Second.Length==2&&Math.Abs(p.First[0]-p.Second[0])<.0001f&&Math.Abs(p.First[1]-p.Second[1])<.0001f);
    private static float Area(float[][] uv)=>Math.Abs(uv.Select((p,i)=>p[0]*uv[(i+1)%uv.Length][1]-uv[(i+1)%uv.Length][0]*p[1]).Sum())/2;
    private static string Snapshot(MapDocument document)=>JsonSerializer.Serialize(document.Project.Definition,
        new JsonSerializerOptions {NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals});
    private static void AtomicReject(MapDocument document,Action action,Action<bool,string> check,string label)
    {
        string before=Snapshot(document);var state=document.CurrentStateId;int commands=document.History.CommandCount;
        bool undo=document.History.CanUndo,redo=document.History.CanRedo;long bytes=document.History.ApproximateBytes;
        bool rejected=false;
        try { action(); }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or MapAuthoringException or JsonException)
        { rejected=true; }
        check(rejected&&Snapshot(document)==before&&document.CurrentStateId==state&&document.History.CommandCount==commands
            &&document.History.CanUndo==undo&&document.History.CanRedo==redo&&document.History.ApproximateBytes==bytes,label);
    }
}
