using System.Numerics;
using MphRead.Mods.MapEditor;
using MphRead.Mods.StudioRendering;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render.Materials;
using SkiaSharp;
using System.Runtime.InteropServices;

int checks=0;
void Check(bool condition,string name) { if(!condition)throw new InvalidOperationException(name);Console.WriteLine("STUDIO RENDER PASS "+name);checks++; }
Guid front=Guid.NewGuid(),back=Guid.NewGuid();
MapViewportMesh Mesh(Guid id,float depth) => new(id,new[] { new MapViewportFace(id,
    new[] {new Vector3(-2,-2,depth),new Vector3(2,-2,depth),new Vector3(0,2,depth)},.8f,0,true) });
var meshes=new[] {Mesh(front,0),Mesh(back,-2)};
var frame=new MapRenderFrame(new(128,128),new(new(0,0,8),Vector3.Zero,true),meshes,new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false);
Check(EditorPickPass.Cpu(frame,64,64).Surface?.ObjectId==front,"CPU parity oracle resolves nearest actual triangle");
foreach(uint id in new uint[] {0,1,255,256,65535,0x80ff00fe,uint.MaxValue})
{
    var encoded=EditorPickPass.EncodeRgba(id);Check(EditorPickPass.DecodeRgba(encoded.R,encoded.G,encoded.B,encoded.A)==id,"RGBA fallback preserves all 32 ID bits "+id);
}
var dpi=frame with {Layout=new(64,64,2)};
Check(dpi.Layout.PixelWidth==128 && dpi.Camera.Project(dpi.Layout,Vector3.Zero) is {X:32,Y:32},"DPI bounds and canonical logical camera agree");
if(args.Contains("--gpu"))
{
    using var device=new StudioRenderDevice();using var world=device.CreateWorld();
    var views=Enumerable.Range(0,4).Select(_=>device.CreateSurface()).ToArray();
    try
    {
        foreach(var view in views)
        {
            // Real authoring controls keep independent canonical CPU caches.
            // Equivalent source objects must share one GPU promotion too.
            var separateCache=new[] {Mesh(front,0),Mesh(back,-2)};
            var picture=view.Render(world,frame with {Meshes=separateCache,ResidentMeshes=separateCache});
            Check(picture.Rgba[(64*128+64)*4]>180,"retained modern graph produces expected shaded triangle pixels");
        }
        Check(world.MeshUploads==2,"four independent canonical CPU caches share document mesh uploads");
        long uploads=world.MeshUploads;
        views[0].Render(world,frame with {Camera=new(new(1,1,8),Vector3.Zero,true),Selection=new HashSet<Guid>{front}});
        Check(world.MeshUploads==uploads,"camera and selection changes retain uploaded geometry");
        var hit=views[0].Pick(world,frame,64,64,verifyCpuParity:true);
        Check(hit.GpuUsed && hit.MatchesCpu && hit.CpuParityChecked && hit.Surface?.ObjectId==front,"one-pixel R32Uint depth pick agrees with CPU oracle");
        long pickPasses=views[0].PickPassSubmissions;
        var repeated=views[0].Pick(world,frame with {Selection=new HashSet<Guid>{front}},64,64,verifyCpuParity:true);
        Check(repeated.GpuUsed && repeated.MatchesCpu && repeated.CpuParityChecked && views[0].PickPassSubmissions==pickPasses,"unchanged geometry and camera reuse the integer pick target across selection changes");
        views[0].Pick(world,frame with {Camera=new(new(1,1,8),Vector3.Zero,true)},64,64);
        Check(views[0].PickPassSubmissions==pickPasses+1,"camera changes invalidate the cached integer pick target");
        var vertex=views[0].Pick(world,frame,36,92,StudioPickKind.Vertex,verifyCpuParity:true);
        Check(vertex.GpuUsed && vertex.MatchesCpu && vertex.CpuParityChecked && vertex.Element is {Kind:StudioPickKind.Vertex,A:0}
            && vertex.GpuElement is {Kind:StudioPickKind.Vertex,A:0},"GPU vertex ID quads preserve CPU element parity");
        var edge=views[0].Pick(world,frame,64,90,StudioPickKind.Edge,verifyCpuParity:true);
        Check(edge.GpuUsed && edge.MatchesCpu && edge.CpuParityChecked && edge.Element is {Kind:StudioPickKind.Edge,A:0,B:1}
            && edge.GpuElement is {Kind:StudioPickKind.Edge,A:0,B:1},"GPU edge ID quads preserve CPU element parity");
        var beforeNormal=views[0].PickDiagnostics;
        var normal=views[0].Pick(world,frame,64,64);
        var afterNormal=views[0].PickDiagnostics;
        Check(normal.GpuUsed && !normal.CpuParityChecked && normal.Surface?.ObjectId==front
            && afterNormal.FullSceneCpuQueries==beforeNormal.FullSceneCpuQueries
            && afterNormal.WinningFaceQueries==beforeNormal.WinningFaceQueries+1
            && afterNormal.WinningFaceTriangleTests==beforeNormal.WinningFaceTriangleTests+1
            && afterNormal.ReadbackBytes==beforeNormal.ReadbackBytes+4,
            "normal GPU hover reconstructs one winning triangle with one pixel and no full-scene CPU query");
        var background=views[0].Pick(world,frame,3,3);
        Check(background.GpuUsed && background.Element==null && background.Surface==null
            && views[0].PickDiagnostics.FullSceneCpuQueries==afterNormal.FullSceneCpuQueries,
            "valid GPU background hover returns a miss without a full-scene CPU query");
        var beforeFault=views[0].PickDiagnostics;
        views[0].FailNextPickReadbackForDiagnostics();
        var fallback=views[0].Pick(world,frame,64,64);
        Check(!fallback.GpuUsed && fallback.Surface?.ObjectId==front
            && views[0].PickDiagnostics.FullSceneCpuQueries==beforeFault.FullSceneCpuQueries+1
            && views[0].PickDiagnostics.FallbackQueries==beforeFault.FallbackQueries+1
            && views[0].PickDiagnostics.ReadbackBytes==beforeFault.ReadbackBytes,
            "injected readback failure takes the real canonical CPU fallback without reading another target");
        var recoveredPick=views[0].Pick(world,frame,64,64);
        Check(recoveredPick.GpuUsed && recoveredPick.Surface?.ObjectId==front
            && views[0].PickDiagnostics.FullSceneCpuQueries==beforeFault.FullSceneCpuQueries+1,
            "normal GPU picking resumes after the one-shot readback fault");
        var beforeInvalid=views[0].PickDiagnostics;
        var invalid=views[0].Pick(world,frame with {Layout=new(0,0)},64,64);
        var nonfinite=views[0].Pick(world,frame,double.NaN,64);
        Check(!invalid.GpuUsed && invalid.Surface==null && !nonfinite.GpuUsed && nonfinite.Surface==null
            && views[0].PickDiagnostics.FallbackQueries==beforeInvalid.FallbackQueries+2,
            "invalid viewport bounds and coordinates take a safe CPU fallback");
        using(var collisionWorld=device.CreateWorld())
        {
            var collisionMesh=meshes[0] with {CollisionFaces=Mesh(front,2).Faces};
            var collisionFrame=frame with {Meshes=new[]{collisionMesh},ResidentMeshes=new[]{collisionMesh},Collision=true};
            var collisionHit=views[0].Pick(collisionWorld,collisionFrame,64,64,verifyCpuParity:true);
            Check(collisionHit.GpuUsed && collisionHit.MatchesCpu && collisionHit.CpuParityChecked
                && collisionHit.Surface is {Point.Z:2,Distance:6},
                "collision GPU ID and CPU oracle reconstruct the dedicated canonical collision face");
        }
        var densePickDefinition=new MapDefinition {Geometry=Enumerable.Range(0,1024).Select(i=>(MapGeometry)new MapBox
            {Transform=new() {Position=new[]{(i%32-16)*2f,0f,(i/32-16)*2f},Scale=new[]{1f,1f,1f}}}).ToList()};
        var densePickCache=new MapViewportCache();densePickCache.Invalidate(densePickDefinition,new(MapChangeDomain.All));
        var densePickFrame=new MapRenderFrame(new(640,360),new(new(60,45,70),Vector3.Zero,true),densePickCache.Meshes,
            new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false) {ResidentMeshes=densePickCache.Meshes};
        using(var densePickWorld=device.CreateWorld())
        {
            var beforeDenseParity=views[0].PickDiagnostics;
            // Match the rasterizer's physical sample centres: half a pixel can cross the
            // silhouette of a unit box projected to only a few pixels in this dense fixture.
            var denseParity=views[0].Pick(densePickWorld,densePickFrame,320.5,180.5,verifyCpuParity:true);
            Console.WriteLine("Dense parity result: "+System.Text.Json.JsonSerializer.Serialize(new {Result=denseParity,
                Before=beforeDenseParity,After=views[0].PickDiagnostics,Camera=densePickFrame.Camera},
                new System.Text.Json.JsonSerializerOptions {IncludeFields=true}));
            Check(denseParity.GpuUsed && denseParity.CpuParityChecked && denseParity.MatchesCpu && denseParity.Surface!=null
                && views[0].PickDiagnostics.FullSceneCpuQueries==beforeDenseParity.FullSceneCpuQueries+1,
                "dense GPU pick retains explicit full-scene CPU parity verification");
            views[0].SubmitForDiagnostics(densePickWorld,densePickFrame);
            var beforeDense=views[0].PickDiagnostics;
            long denseMeshUploads=densePickWorld.MeshUploads;
            long densePickSubmissions=views[0].PickPassSubmissions;
            var densePicks=Enumerable.Range(0,20).Select(i=>views[0].Pick(densePickWorld,densePickFrame,
                320.5+(i%2),180.5+((i/2)%2))).ToArray();
            var afterDense=views[0].PickDiagnostics;
            Check(densePicks.All(pick=>pick.GpuUsed && !pick.CpuParityChecked && pick.Surface!=null)
                && afterDense.FullSceneCpuQueries==beforeDense.FullSceneCpuQueries
                && afterDense.FallbackQueries==beforeDense.FallbackQueries
                && afterDense.WinningFaceQueries==beforeDense.WinningFaceQueries+20
                && afterDense.WinningFaceTriangleTests==beforeDense.WinningFaceTriangleTests+40
                && afterDense.ReadbackBytes==beforeDense.ReadbackBytes+80
                && densePickWorld.MeshUploads==denseMeshUploads && densePickWorld.ResidentMeshes==1024
                && views[0].PickPassSubmissions==densePickSubmissions && views[0].Metrics?.ReadbackBytes==0
                && views[0].Metrics?.PickReadbackBytes==80,
                "twenty dense hovers test forty winning-face triangles instead of all 12288 scene triangles and read only twenty pixels");
            Console.WriteLine("Dense pick counters: "+System.Text.Json.JsonSerializer.Serialize(afterDense));
        }
        views[0].Render(world,frame with {Meshes=new[] {meshes[1]},ResidentMeshes=new[] {meshes[1]}});
        Check(world.ResidentMeshes==1,"removed document mesh releases retained resources");
        device.SimulateDeviceLossForDiagnostics();
        views[0].Render(world,frame);
        Check(device.Generation==2 && world.ResidentMeshes==2,"device loss recreates native resources from retained canonical CPU meshes");
        string materialDirectory=Path.Combine(Path.GetTempPath(),"studio-material-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(materialDirectory);
        try
        {
            string normalPath=Path.Combine(materialDirectory,"normal.png"),specularPath=Path.Combine(materialDirectory,"specular.png"),emissivePath=Path.Combine(materialDirectory,"emissive.png");
            void Channel(string path,SKColor color)
            {
                using var bitmap=new SKBitmap(1,1,SKColorType.Rgba8888,SKAlphaType.Unpremul);bitmap.SetPixel(0,0,color);
                using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,png.ToArray());
            }
            Channel(normalPath,new(128,128,255));Channel(specularPath,new(128,255,0,0));Channel(emissivePath,new(0,255,0));
            var enhanced=new ResolvedMaterial(new("map/studio-render-check"),null,new(normalPath,1,1),new(specularPath,1,1),new(emissivePath,1,1));
            var green=new MapViewportMaterial("pbr-solid",1,1,new[] {new MphRead.ColorRgba(0,0,0,255)},Enhanced:enhanced);
            var greenFrame=frame with {Materials=new Dictionary<(bool,int),MapViewportMaterial>{{(false,0),green}}};
            long pbrUploads=world.MeshUploads;
            var greenPicture=views[0].Render(world,greenFrame);int center=(64*128+64)*4;
            Console.WriteLine("PBR green center="+string.Join(',',greenPicture.Rgba.Skip(center).Take(4))+" resident textures="+world.ResidentTextures);
            Check(greenPicture.Rgba[center+1]>210 && greenPicture.Rgba[center]<30,"shared material decoder and authoritative PBR shader apply emissive companion pixels");
            Channel(emissivePath,new(255,0,0));
            // Same canonical albedo key and same mutable source path, with the
            // original green material still resident in another material slot.
            var red=green with {Pixels=green.Pixels.ToArray()};
            var redFrame=frame with {Materials=new Dictionary<(bool,int),MapViewportMaterial>{{(false,0),red},{(false,1),green}}};
            var redPicture=views[0].Render(world,redFrame);
            Check(redPicture.Rgba[center]>210 && redPicture.Rgba[center+1]<30 && world.MeshUploads==pbrUploads,"companion content revisions avoid mutable-path collisions without geometry uploads");
            File.Delete(normalPath);File.Delete(specularPath);File.Delete(emissivePath);
            device.SimulateDeviceLossForDiagnostics();var restored=views[0].Render(world,redFrame);
            Check(device.Generation==3 && restored.Rgba[center]>210 && restored.Rgba[center+1]<30,"device recovery promotes frozen companion bytes after external sources disappear");
        }
        finally{Directory.Delete(materialDirectory,recursive:true);}
        // Modes must change the real retained GPU output without rebuilding
        // canonical geometry; captures alone may read back full images.
        using var diagnosticWorld=device.CreateWorld();
        var diagnosticMeshes=new[] {Mesh(front,0),Mesh(back,-2)};
        var diagnosticFrame=frame with {Meshes=diagnosticMeshes,ResidentMeshes=diagnosticMeshes};
        int diagnosticCenter=(64*128+64)*4;
        var unlit=views[0].Render(diagnosticWorld,diagnosticFrame);
        var lit=views[0].Render(diagnosticWorld,diagnosticFrame with {LightingPreview=true,Light1Color=Vector3.Zero,Light2Color=Vector3.Zero});
        Check(!unlit.Rgba.AsSpan(diagnosticCenter,3).SequenceEqual(lit.Rgba.AsSpan(diagnosticCenter,3)),"authored lighting uniforms change native material preview pixels");
        var fogged=views[0].Render(diagnosticWorld,diagnosticFrame with {FogPreview=true,FogEnabled=true,FogColor=new(.1f,.2f,.8f),FogOffset=0,FogSlope=5});
        Check(Math.Abs(fogged.Rgba[diagnosticCenter]-26)<=1 && Math.Abs(fogged.Rgba[diagnosticCenter+2]-204)<=1,"authoritative World fog block applies authored color and range");
        var terrain=views[0].Render(diagnosticWorld,diagnosticFrame with {DiagnosticMode=MapViewportDiagnosticMode.Terrain});
        var terrainColor=MapViewportDiagnostics.TerrainColor(MphRead.Terrain.Metal);
        Check(Math.Abs(terrain.Rgba[diagnosticCenter]-terrainColor.X*255)<=1 && Math.Abs(terrain.Rgba[diagnosticCenter+2]-terrainColor.Z*255)<=1,"terrain mode samples canonical collision terrain palette per face");
        var materialIds=views[0].Render(diagnosticWorld,diagnosticFrame with {DiagnosticMode=MapViewportDiagnosticMode.MaterialId});
        var materialColor=MapViewportDiagnostics.MaterialColor(false,0);
        Check(Math.Abs(materialIds.Rgba[diagnosticCenter]-materialColor.X*255)<=1 && Math.Abs(materialIds.Rgba[diagnosticCenter+1]-materialColor.Y*255)<=1,"material ID view uses deterministic canonical material keys");
        var doubled=views[0].Render(diagnosticWorld,diagnosticFrame with {DiagnosticMode=MapViewportDiagnosticMode.Overdraw});
        var single=views[0].Render(diagnosticWorld,diagnosticFrame with {DiagnosticMode=MapViewportDiagnosticMode.Overdraw,Meshes=new[]{diagnosticMeshes[0]}});
        Check(doubled.Rgba[diagnosticCenter]>=single.Rgba[diagnosticCenter]*2-1 && single.Rgba[diagnosticCenter]>20,"overdraw pass counts occluded fragments with additive GPU blending");
        var density=views[0].Render(diagnosticWorld,diagnosticFrame with {DiagnosticMode=MapViewportDiagnosticMode.TexelDensity});
        Check(density.Rgba[diagnosticCenter+2]>230 && density.Rgba[diagnosticCenter]<60,"texel density derives blue low-density output from canonical UV world derivatives");
        Check(diagnosticWorld.MeshUploads==2,"changing diagnostic previews preserves retained mesh uploads");
        var shadowDefinition=new MapDefinition {Geometry=new() {new MapBox {Transform=new() {Position=new[]{0f,-1f,0f},Scale=new[]{20f,1f,20f}}},new MapBox {Transform=new() {Position=new[]{0f,3f,0f},Scale=new[]{4f,6f,4f}}}}};
        var shadowCache=new MapViewportCache();shadowCache.Invalidate(shadowDefinition,new(MapChangeDomain.All));
        var shadowFrame=new MapRenderFrame(new(384,256),new(new(20,17,22),Vector3.Zero,true),shadowCache.Meshes,new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false)
            {ResidentMeshes=shadowCache.Meshes,Light1Vector=new(.7f,-.7f,.1f)};
        using var shadowWorld=device.CreateWorld();
        var noShadow=views[0].Render(shadowWorld,shadowFrame);
        var shadowed=views[0].Render(shadowWorld,shadowFrame with {ShadowPreview=true});
        int darkened=Enumerable.Range(0,shadowed.Width*shadowed.Height).Count(i=>noShadow.Rgba[i*4]>shadowed.Rgba[i*4]+10);
        Check(darkened>50 && views[0].Metrics?.DrawCalls==shadowCache.Meshes.Count*2,"shared runtime directional shadow camera and PCF shader produce retained depth shadows");
        int outputIndex=Array.IndexOf(args,"--output");
        if(outputIndex>=0 && outputIndex+1<args.Length)
        {
            string directory=Path.GetFullPath(args[outputIndex+1]);Directory.CreateDirectory(directory);
            var definition=MapTemplates.Create("Studio rendering acceptance",false).Definition;
            var cache=new MapViewportCache();cache.Invalidate(definition,new(MapChangeDomain.All));
            var materialTable=cache.Meshes.SelectMany(mesh=>mesh.Faces).Select(face=>(false,face.Material)).Distinct()
                .ToDictionary(key=>key,_=>MapViewportMaterials.Checker);
            var canonical=new MapRenderFrame(new(960,600),new(new(28,24,32),Vector3.Zero,true),cache.Meshes,new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false)
                {ResidentMeshes=cache.Meshes,Materials=materialTable};
            using var canonicalWorld=device.CreateWorld();
            void Save(string name,MapRenderFrame pictureFrame)
            {
                var capture=views[0].Render(canonicalWorld,pictureFrame);
                using var bitmap=new SKBitmap(capture.Width,capture.Height,SKColorType.Rgba8888,SKAlphaType.Opaque);
                Marshal.Copy(capture.Rgba,0,bitmap.GetPixels(),capture.Rgba.Length);
                using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
                File.WriteAllBytes(Path.Combine(directory,name+".png"),png.ToArray());
            }
            Save("map-default",canonical);Save("map-hidpi",canonical with {Layout=new(960,600,2)});
            Save("map-uv-checker",canonical with {UvChecker=true});
            Save("map-wireframe",canonical with {Wireframe=true});
            Save("map-lighting",canonical with {LightingPreview=true});
            Save("map-fog",canonical with {FogPreview=true,FogEnabled=true,FogColor=new(.12f,.18f,.3f),FogOffset=24000,FogSlope=1});
            Save("map-terrain",canonical with {DiagnosticMode=MapViewportDiagnosticMode.Terrain});
            Save("map-overdraw",canonical with {DiagnosticMode=MapViewportDiagnosticMode.Overdraw});
            Save("map-texel-density",canonical with {DiagnosticMode=MapViewportDiagnosticMode.TexelDensity});
            Save("map-material-id",canonical with {DiagnosticMode=MapViewportDiagnosticMode.MaterialId});
            Save("map-shadows",shadowFrame with {ShadowPreview=true});
            Save("map-shadows-disabled",shadowFrame);
            Save("map-four-view-perspective",canonical with {Layout=new(480,300)});
            Save("map-four-view-top",canonical with {Layout=new(480,300),Camera=new(new(0,60,0),Vector3.Zero,false)});
            Save("map-four-view-front",canonical with {Layout=new(480,300),Camera=new(new(0,0,60),Vector3.Zero,false)});
            Save("map-four-view-side",canonical with {Layout=new(480,300),Camera=new(new(60,0,0),Vector3.Zero,false)});
            Console.WriteLine("Explicit diagnostic GPU captures: "+directory);
            var denseDefinition=new MapDefinition {Geometry=Enumerable.Range(0,1024).Select(i=>(MapGeometry)new MapBox
                {Transform=new() {Position=new[]{(i%32-16)*2f,0f,(i/32-16)*2f},Scale=new[]{1f,1f,1f}}}).ToList()};
            var denseCache=new MapViewportCache();denseCache.Invalidate(denseDefinition,new(MapChangeDomain.All));
            var denseFrame=new MapRenderFrame(new(2560,1440),new(new(65,65,65),Vector3.Zero,true),denseCache.Meshes,new HashSet<Guid>(),new Dictionary<Guid,Matrix4x4>(),false,false)
                {ResidentMeshes=denseCache.Meshes};
            using var denseWorld=device.CreateWorld();
            views[0].SubmitForDiagnostics(denseWorld,denseFrame);
            long denseUploads=denseWorld.GeometryUploadBytes;
            var samples=Enumerable.Range(0,20).Select(i=>views[0].SubmitForDiagnostics(denseWorld,denseFrame with
                {Camera=new(new(65+i*.1f,65,65),Vector3.Zero,true)})).ToArray();
            Check(samples.All(sample=>sample.PixelWidth==2560 && sample.PixelHeight==1440 && sample.ReadbackBytes==0)
                && denseWorld.GeometryUploadBytes==denseUploads && denseWorld.ResidentMeshes==1024,"twenty dense 1440p retained submissions keep geometry uploads stable and perform zero GPU readback");
            var times=samples.Select(sample=>sample.CpuMilliseconds).Order().ToArray();
            var report=new {Scope="Retained offscreen GPU targets; CPU measures renderer submission only; GPU time unavailable",device.Backend,device.Adapter,
                Width=2560,Height=1440,Objects=1024,CpuMedianMilliseconds=times[times.Length/2],CpuP95Milliseconds=times[(int)Math.Ceiling(times.Length*.95)-1],
                CpuMaximumMilliseconds=times[^1],Samples=samples};
            File.WriteAllText(Path.Combine(directory,"dense-1440p.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            var denseCapture=views[0].Render(denseWorld,denseFrame);
            using var denseBitmap=new SKBitmap(denseCapture.Width,denseCapture.Height,SKColorType.Rgba8888,SKAlphaType.Opaque);
            Marshal.Copy(denseCapture.Rgba,0,denseBitmap.GetPixels(),denseCapture.Rgba.Length);
            using var denseImage=SKImage.FromBitmap(denseBitmap);using var densePng=denseImage.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(Path.Combine(directory,"dense-1440p.png"),densePng.ToArray());
        }
        Console.WriteLine($"backend={device.Backend} adapter={device.Adapter} generation={device.Generation}");
    }
    finally {foreach(var view in views)view.Dispose();}
}
Console.WriteLine($"Studio rendering checks passed: {checks}. GPU mode: {args.Contains("--gpu")}.");
